using UnityEngine;

/// <summary>
/// Step N: Apply wind advection to humidity.
/// Takes humidity AFTER mountain drying and pushes it along wind,
/// so moist air spreads inland and dry air pushes dryness.
/// </summary>
[ExecuteAlways]
public class HumidityWindAdvectionComponent : MonoBehaviour
{
    [Header("Inputs")]
    [Tooltip("Wind field after smoothing (must have finalWindVector ready).")]
    public WindFieldSmootherComponent windSource;

    [Tooltip("Humidity after mountain drying step (must have finalHumidity01 ready).")]
    public HumidityRefiner_MountainDrying humiditySource;

    [Header("Resolution")]
    [Tooltip("Output map resolution (recommended: same as humiditySource.mapResolution).")]
    [Range(16, 2048)]
    public int mapResolution = 512;

    [Header("Advection Settings")]
    [Tooltip("How many pixels 'upwind' we look when sampling source air.")]
    [Range(0f, 64f)]
    public float baseShiftPixels = 8f;

    [Tooltip("How much wind speed affects shift distance. 1 = linear, 2 = wind^2, etc.")]
    [Range(0.1f, 3f)]
    public float windSpeedPower = 1f;

    [Tooltip("How much of that imported (upwind) humidity replaces local humidity. 0 = none, 1 = full.")]
    [Range(0f, 1f)]
    public float advectionBlend = 0.5f;

    [Tooltip("Global bias after advection. Positive = slightly more humid everywhere.")]
    [Range(-0.5f, 0.5f)]
    public float globalHumidityOffset = 0f;

    [Header("Clamp / Floors")]
    [Tooltip("Never let humidity drop below this after all operations.")]
    [Range(0f, 1f)]
    public float minHumidityFloor = 0.02f;

    [Tooltip("Never let humidity exceed this after all operations.")]
    [Range(0f, 1f)]
    public float maxHumidityCeil = 1f;

    [Header("Preview (read-only)")]
    [Tooltip("Grayscale: final humidity AFTER wind advection.")]
    public Texture2D windAdjustedPreviewTexture;

    // final result [y,x] in 0..1
    [HideInInspector] public float[,] windAdjustedHumidity01;

    /// <summary>
    /// Call from editor button.
    /// </summary>
    public void GenerateWindAdjustedHumidity()
    {
        // Safety: humidity source must exist and be generated
        if (humiditySource == null ||
            humiditySource.finalHumidity01 == null ||
            humiditySource.finalHumidity01.GetLength(0) == 0)
        {
            humiditySource?.GenerateMountainRefinedHumidity();
        }

        if (humiditySource == null ||
            humiditySource.finalHumidity01 == null ||
            humiditySource.finalHumidity01.GetLength(0) == 0)
        {
            Debug.LogWarning("HumidityWindAdvection: humiditySource not ready. Run HumidityRefiner_MountainDrying first.");
            return;
        }

        // Safety: wind must exist and be generated
        if (windSource == null ||
            windSource.finalWindVector == null ||
            windSource.finalWindVector.GetLength(0) == 0)
        {
            Debug.LogWarning("HumidityWindAdvection: windSource not ready. Run WindFieldSmootherComponent first.");
            return;
        }

        int outW = mapResolution;
        int outH = mapResolution;
        if (outW <= 0 || outH <= 0) return;

        EnsureBuffers(outW, outH);

        // build preview texture
        if (windAdjustedPreviewTexture == null ||
            windAdjustedPreviewTexture.width != outW ||
            windAdjustedPreviewTexture.height != outH)
        {
            windAdjustedPreviewTexture = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            windAdjustedPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            windAdjustedPreviewTexture.filterMode = FilterMode.Point;
        }

        Color[] previewPixels = new Color[outW * outH];

        // main loop
        for (int py = 0; py < outH; py++)
        {
            float v = (float)py / (outH - 1);

            for (int px = 0; px < outW; px++)
            {
                float u = (float)px / (outW - 1);

                // 1) local humidity at this uv
                float humHere = SampleFloatArrayBilinear(
                    humiditySource.finalHumidity01,
                    u, v
                );

                // 2) wind vector at this uv
                Vector2 wv = SampleWindVectorBilinear(
                    windSource.finalWindVector,
                    u, v
                );

                float spd = wv.magnitude;
                float spdFactor = Mathf.Pow(spd, windSpeedPower);

                // upwind direction: where air originated
                Vector2 dirNorm = (spd > 1e-6f) ? (wv.normalized) : Vector2.zero;

                // how far we trace back in pixels (output grid space)
                float shiftPx = baseShiftPixels * spdFactor;

                // pixel -> uv shift
                float du = (dirNorm.x * shiftPx) / (float)(outW - 1);
                float dv = (dirNorm.y * shiftPx) / (float)(outH - 1);

                // upwind sample point
                float srcU = u - du;
                float srcV = v - dv;

                srcU = Mathf.Clamp01(srcU);
                srcV = Mathf.Clamp01(srcV);

                // 3) humidity from that upwind source
                float humSourceUpwind = SampleFloatArrayBilinear(
                    humiditySource.finalHumidity01,
                    srcU, srcV
                );

                // 4) blend
                float mixed = Mathf.Lerp(humHere, humSourceUpwind, advectionBlend);

                // 5) global tweak
                mixed += globalHumidityOffset;

                // 6) clamp + floors/ceil
                mixed = Mathf.Clamp01(mixed);
                if (mixed < minHumidityFloor) mixed = minHumidityFloor;
                if (mixed > maxHumidityCeil) mixed = maxHumidityCeil;

                windAdjustedHumidity01[py, px] = mixed;

                // grayscale preview
                previewPixels[py * outW + px] = new Color(mixed, mixed, mixed, 1f);
            }
        }

        windAdjustedPreviewTexture.SetPixels(previewPixels);
        windAdjustedPreviewTexture.Apply();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        Debug.Log("HumidityWindAdvection: wind-adjusted humidity generated.");
    }

    void EnsureBuffers(int w, int h)
    {
        if (windAdjustedHumidity01 == null ||
            windAdjustedHumidity01.GetLength(0) != h ||
            windAdjustedHumidity01.GetLength(1) != w)
        {
            windAdjustedHumidity01 = new float[h, w];
        }
    }

    // --- samplers (same pattern as temp advection component) ---

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

        Vector2 v00 = vecs[y0, x0];
        Vector2 v10 = vecs[y0, x1];
        Vector2 v01 = vecs[y1, x0];
        Vector2 v11 = vecs[y1, x1];

        Vector2 a = Vector2.Lerp(v00, v10, fx);
        Vector2 b = Vector2.Lerp(v01, v11, fx);

        return Vector2.Lerp(a, b, fy);
    }
}
