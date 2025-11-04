using UnityEngine;

/// <summary>
/// Step N: Apply wind advection to temperature.
/// Takes the final temperature (after mountains/snow)
/// and "pushes" it along wind directions to simulate
/// warm/cold air spreading.
/// </summary>
[ExecuteAlways]
public class TemperatureWindAdvectionComponent : MonoBehaviour
{
    [Header("Inputs")]
    [Tooltip("Wind field after smoothing (must have finalWindVector ready).")]
    public WindFieldSmootherComponent windSource;

    [Tooltip("Temperature after mountain cooling / snowcaps (must have finalTemperature01 ready).")]
    public TemperatureRefiner_MountainCooling temperatureSource;

    [Header("Resolution")]
    [Tooltip("Output map resolution (should match previous temperature maps).")]
    [Range(16, 2048)]
    public int mapResolution = 512;

    [Header("Advection Settings")]
    [Tooltip("How many pixels 'upwind' we look when sampling source air.")]
    [Range(0f, 64f)]
    public float baseShiftPixels = 8f;

    [Tooltip("How much wind speed matters. 1 = linear, 2 = wind^2, etc.")]
    [Range(0.1f, 3f)]
    public float windSpeedPower = 1f;

    [Tooltip("Overall strength of advection blend. 0 = keep original temp, 1 = fully use upwind temp.")]
    [Range(0f, 1f)]
    public float advectionBlend = 0.5f;

    [Tooltip("Clamp final values 0..1 anyway, but you can bias global cooling/heating afterwards.")]
    [Range(-0.5f, 0.5f)]
    public float globalTempoffset = 0f;

    [Header("Preview (read-only)")]
    [Tooltip("Grayscale: final temperature AFTER wind advection.")]
    public Texture2D windAdjustedPreviewTexture;

    // result after wind advection, [y,x] in 0..1
    [HideInInspector] public float[,] windAdjustedTemperature01;

    /// <summary>
    /// Call from editor button.
    /// </summary>
    public void GenerateWindAdjustedTemperature()
    {
        // basic safety checks
        if (windSource == null ||
            windSource.finalWindVector == null ||
            windSource.finalWindVector.GetLength(0) == 0)
        {
            Debug.LogWarning("TemperatureWindAdvection: windSource not ready. Run WindFieldSmootherComponent first.");
            return;
        }

        if (temperatureSource == null ||
            temperatureSource.finalTemperature01 == null ||
            temperatureSource.finalTemperature01.GetLength(0) == 0)
        {
            // try auto-generate mountain step
            temperatureSource?.GenerateMountainRefinedTemperature();
        }

        if (temperatureSource == null ||
            temperatureSource.finalTemperature01 == null ||
            temperatureSource.finalTemperature01.GetLength(0) == 0)
        {
            Debug.LogWarning("TemperatureWindAdvection: temperatureSource not ready. Run TemperatureRefiner_MountainCooling first.");
            return;
        }

        // We'll sample both wind and temp in UV space [0..1],
        // so resolution mismatch is fine.
        int outW = mapResolution;
        int outH = mapResolution;
        if (outW <= 0 || outH <= 0) return;

        EnsureBuffers(outW, outH);

        // prepare preview texture
        if (windAdjustedPreviewTexture == null ||
            windAdjustedPreviewTexture.width != outW ||
            windAdjustedPreviewTexture.height != outH)
        {
            windAdjustedPreviewTexture = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            windAdjustedPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            windAdjustedPreviewTexture.filterMode = FilterMode.Point;
        }

        Color[] previewPixels = new Color[outW * outH];

        // We'll use bilinear sampling helpers
        for (int py = 0; py < outH; py++)
        {
            float v = (float)py / (outH - 1);

            for (int px = 0; px < outW; px++)
            {
                float u = (float)px / (outW - 1);

                // 1) sample current temp
                float tempHere = SampleFloatArrayBilinear(
                    temperatureSource.finalTemperature01,
                    u, v
                );

                // 2) sample wind vector at this uv
                Vector2 wv = SampleWindVectorBilinear(
                    windSource.finalWindVector,
                    u, v
                );

                // magnitude 0..~?
                float spd = wv.magnitude;
                float spdFactor = Mathf.Pow(spd, windSpeedPower); // emphasize stronger winds

                // direction opposite of wv = where air CAME FROM (upwind)
                Vector2 dirNorm = (spd > 1e-6f) ? (wv.normalized) : Vector2.zero;

                // how far to shift in pixels in OUTPUT grid
                float shiftPx = baseShiftPixels * spdFactor;

                // convert pixel shift -> uv shift for this output resolution
                // note: x shift is along +dirNorm.x, y along +dirNorm.y
                // wind vector is in "map pixel space" already? We stored X,Y components
                // as unit-ish, so we treat 1.0 in wind x as 1 pixel shift direction unit.
                // We'll interpret dirNorm in "pixel units per 1px step".
                // So final offset in pixel space is dirNorm * shiftPx.
                // Convert that to UV:
                float du = (dirNorm.x * shiftPx) / (float)(outW - 1);
                float dv = (dirNorm.y * shiftPx) / (float)(outH - 1);

                // go UPWIND: source air is behind us, so subtract
                float srcU = u - du;
                float srcV = v - dv;

                // clamp to map edge
                srcU = Mathf.Clamp01(srcU);
                srcV = Mathf.Clamp01(srcV);

                // 3) grab source temp from that upwind location
                float tempSourceUpwind = SampleFloatArrayBilinear(
                    temperatureSource.finalTemperature01,
                    srcU, srcV
                );

                // 4) blend local vs advected air
                float mixed = Mathf.Lerp(tempHere, tempSourceUpwind, advectionBlend);

                // 5) apply optional global offset
                mixed += globalTempoffset;

                // clamp to [0..1] final
                mixed = Mathf.Clamp01(mixed);

                windAdjustedTemperature01[py, px] = mixed;

                // preview grayscale
                previewPixels[py * outW + px] = new Color(mixed, mixed, mixed, 1f);
            }
        }

        windAdjustedPreviewTexture.SetPixels(previewPixels);
        windAdjustedPreviewTexture.Apply();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        Debug.Log("TemperatureWindAdvection: wind-adjusted temperature generated.");
    }

    void EnsureBuffers(int w, int h)
    {
        if (windAdjustedTemperature01 == null ||
            windAdjustedTemperature01.GetLength(0) != h ||
            windAdjustedTemperature01.GetLength(1) != w)
        {
            windAdjustedTemperature01 = new float[h, w];
        }
    }

    // --- Helpers ---

    float SampleFloatArrayBilinear(float[,] data, float u, float v)
    {
        int h = data.GetLength(0);
        int w = data.GetLength(1);

        float x = u * (w - 1);
        float y = v * (h - 1);

        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, w - 1);
        int y1 = Mathf.Min(y0 + 1, h - 1);

        float fx = x - x0;
        float fy = y - y0;

        float a = Mathf.Lerp(data[y0, x0], data[y0, x1], fx);
        float b = Mathf.Lerp(data[y1, x0], data[y1, x1], fx);

        return Mathf.Lerp(a, b, fy);
    }

    Vector2 SampleWindVectorBilinear(Vector2[,] vecs, float u, float v)
    {
        int h = vecs.GetLength(0);
        int w = vecs.GetLength(1);

        float x = u * (w - 1);
        float y = v * (h - 1);

        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, w - 1);
        int y1 = Mathf.Min(y0 + 1, h - 1);

        float fx = x - x0;
        float fy = y - y0;

        // bilinear on X and Y separately
        Vector2 v00 = vecs[y0, x0];
        Vector2 v10 = vecs[y0, x1];
        Vector2 v01 = vecs[y1, x0];
        Vector2 v11 = vecs[y1, x1];

        Vector2 a = Vector2.Lerp(v00, v10, fx);
        Vector2 b = Vector2.Lerp(v01, v11, fx);

        return Vector2.Lerp(a, b, fy);
    }
}
