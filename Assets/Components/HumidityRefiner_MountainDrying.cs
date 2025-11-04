using UnityEngine;

[ExecuteAlways]
public class HumidityRefiner_MountainDrying : MonoBehaviour
{
    [Header("Resolution (match previous steps)")]
    [Range(16, 2048)] public int mapResolution = 512;

    [Header("Terrain input")]
    public Terrain terrain; // same Terrain as previous steps

    [Header("Sea level")]
    [Tooltip("Anything below this normalized height is considered ocean. Drying will NOT apply there.")]
    [Range(0f, 1f)] public float seaLevel = 0.3f;

    [Header("Highland / Mountain Drying")]
    [Tooltip("Heights above this start to get drier.")]
    [Range(0f, 1f)] public float dryStartHeight = 0.5f;

    [Tooltip("Max dryness applied at the highest peaks on land.")]
    [Range(0f, 2f)] public float dryStrength = 0.8f;

    [Tooltip("Power curve. Higher = only really tall areas get very dry.")]
    [Range(0.5f, 5f)] public float dryCurvePower = 2f;

    public bool applyMountainDrying = true;

    [Header("Clamp")]
    [Tooltip("Final lower clamp so humidity never goes negative.")]
    [Range(0f, 1f)] public float minHumidityFloor = 0.05f;

    [Header("Preview (read-only)")]
    public Texture2D finalPreviewTexture;

    [HideInInspector] public float[,] finalHumidity01; // [y,x] after drying step

    public void GenerateMountainRefinedHumidity()
    {
        // Step 2 is our input
        var step2 = GetComponent<HumidityRefiner_OceanAltitude>();
        if (step2 == null)
        {
            Debug.LogWarning("HumidityRefiner_MountainDrying: No HumidityRefiner_OceanAltitude found on this GameObject.");
            return;
        }

        // Make sure step2 has data
        if (step2.refinedHumidity01 == null ||
            step2.refinedHumidity01.GetLength(0) == 0 ||
            step2.refinedHumidity01.GetLength(1) == 0)
        {
            // auto-generate previous step if needed
            step2.GenerateRefinedHumidity();
        }

        if (step2.refinedHumidity01 == null)
        {
            Debug.LogWarning("HumidityRefiner_MountainDrying: Step 2 still has no data.");
            return;
        }

        // Terrain check
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("HumidityRefiner_MountainDrying: Terrain not assigned or invalid.");
            return;
        }

        TerrainData td = terrain.terrainData;

        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        // Output buffer
        if (finalHumidity01 == null ||
            finalHumidity01.GetLength(0) != h ||
            finalHumidity01.GetLength(1) != w)
        {
            finalHumidity01 = new float[h, w];
        }

        // Preview texture
        if (finalPreviewTexture == null ||
            finalPreviewTexture.width != w ||
            finalPreviewTexture.height != h)
        {
            finalPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            finalPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            finalPreviewTexture.filterMode = FilterMode.Point;
        }

        // Terrain heights 0..1
        int hmRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes);

        var pixels = new Color[w * h];

        for (int py = 0; py < h; py++)
        {
            float v = (float)py / (h - 1);

            for (int px = 0; px < w; px++)
            {
                float u = (float)px / (w - 1);

                // 1) humidity from previous step
                float hum = SampleFloatArrayBilinear(
                    step2.refinedHumidity01,
                    u, v
                );

                // 2) elevation
                float elev01 = SampleTerrainHeightBilinear(
                    heights, hmRes,
                    u, v
                );

                bool isOcean = elev01 < seaLevel;

                // --- Mountain / plateau drying ---
                // applies ONLY if this is land (not ocean) AND above dryStartHeight
                if (applyMountainDrying && !isOcean && elev01 > dryStartHeight)
                {
                    // how far above threshold we are
                    float dryFactor = Mathf.InverseLerp(dryStartHeight, 1f, elev01); // 0..1 for tall areas
                    // shape it
                    dryFactor = Mathf.Pow(dryFactor, dryCurvePower);
                    // subtract humidity proportional to height
                    hum -= dryFactor * dryStrength;
                }

                // clamp
                hum = Mathf.Clamp01(hum);
                if (hum < minHumidityFloor) hum = minHumidityFloor;

                finalHumidity01[py, px] = hum;

                // preview grayscale
                pixels[py * w + px] = new Color(hum, hum, hum, 1f);
            }
        }

        finalPreviewTexture.SetPixels(pixels);
        finalPreviewTexture.Apply();
    }

    // Bilinear sampler for [y,x] arrays
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

    // Bilinear sampler for terrain data
    float SampleTerrainHeightBilinear(float[,] heights, int hmRes, float u, float v)
    {
        float tx = u * (hmRes - 1);
        float ty = v * (hmRes - 1);

        int x0 = Mathf.FloorToInt(tx);
        int y0 = Mathf.FloorToInt(ty);
        int x1 = Mathf.Min(x0 + 1, hmRes - 1);
        int y1 = Mathf.Min(y0 + 1, hmRes - 1);

        float fx = tx - x0;
        float fy = ty - y0;

        float h00 = heights[y0, x0];
        float h10 = heights[y0, x1];
        float h01 = heights[y1, x0];
        float h11 = heights[y1, x1];

        float h0mix = Mathf.Lerp(h00, h10, fx);
        float h1mix = Mathf.Lerp(h01, h11, fx);

        return Mathf.Lerp(h0mix, h1mix, fy); // normalized 0..1
    }
}
