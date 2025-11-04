using UnityEngine;

[ExecuteAlways]
public class TemperatureRefiner_OceanAltitude : MonoBehaviour
{
    [Header("Resolution (usually same as Step 1)")]
    [Range(16, 2048)] public int mapResolution = 512;

    [Header("Terrain input")]
    public Terrain terrain; // assign your Terrain here

    [Header("Elevation / Sea")]
    [Tooltip("Anything below this normalized height is considered ocean.")]
    [Range(0f, 1f)] public float seaLevel = 0.3f;

    [Tooltip("How much height cools land. 0 = ignore, 1 = strong cooling.")]
    [Range(0f, 2f)] public float altitudeCooling = 0.5f;
    public bool applyAltitudeCooling = true;

    [Header("Ocean & Coast Moderation")]
    public bool applyOceanModeration = true;

    [Tooltip("How far inland the ocean effect bleeds, in pixels at current resolution.")]
    [Range(0, 200)] public int coastRangePixels = 40; // NEW

    [Tooltip("How strong the ocean smooths nearby land.\n0=no coastal moderation, 1=strong pull to ocean surface temp.")]
    [Range(0f, 1f)] public float coastalBlendStrength = 0.6f; // NEW

    [Tooltip("How much we soften ocean extremes toward mild mid-temp (0=no soften,1=fully 0.5).")]
    [Range(0f, 1f)] public float oceanSoften = 0.4f; // RENAMED IDEA: replaces oceanModerateTemp/oceanInfluence combo

    [Header("Preview (read-only)")]
    public Texture2D refinedPreviewTexture;

    [HideInInspector] public float[,] refinedTemperature01; // [y,x]

    public void GenerateRefinedTemperature()
    {
        var baseComp = GetComponent<TemperatureMapGeneratorComponent>();
        if (baseComp == null)
        {
            Debug.LogWarning("Refiner: No TemperatureMapGeneratorComponent found on this GameObject.");
            return;
        }

        if (baseComp.baseTemperature01 == null ||
            baseComp.baseTemperature01.GetLength(0) == 0 ||
            baseComp.baseTemperature01.GetLength(1) == 0)
        {
            baseComp.GenerateBaseTemperature();
        }

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("Refiner: Terrain not assigned or invalid.");
            return;
        }

        TerrainData td = terrain.terrainData;

        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        // output buffer
        if (refinedTemperature01 == null ||
            refinedTemperature01.GetLength(0) != h ||
            refinedTemperature01.GetLength(1) != w)
        {
            refinedTemperature01 = new float[h, w];
        }

        // preview texture
        if (refinedPreviewTexture == null ||
            refinedPreviewTexture.width != w ||
            refinedPreviewTexture.height != h)
        {
            refinedPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            refinedPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            refinedPreviewTexture.filterMode = FilterMode.Point;
        }

        // grab terrain heights
        int hmRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes);

        // --- PREPASS A: sample height + baseT once, also build ocean mask ---
        float[,] baseTMap = new float[h, w];
        float[,] elevMap = new float[h, w];
        bool[,] oceanMask = new bool[h, w];

        for (int py = 0; py < h; py++)
        {
            float v = (float)py / (h - 1);
            for (int px = 0; px < w; px++)
            {
                float u = (float)px / (w - 1);

                float baseT = SampleFloatArrayBilinear(baseComp.baseTemperature01, u, v);
                float elev01 = SampleTerrainHeightBilinear(heights, hmRes, u, v);

                baseTMap[py, px] = baseT;
                elevMap[py, px] = elev01;
                oceanMask[py, px] = (elev01 < seaLevel);
            }
        }

        // --- PREPASS B: build distance-to-ocean in pixels (cheap BFS-ish relax) ---
        // We'll do a chamfer-ish expansion, no heap, just iterative smoothing.

        // init dist with large for land, 0 for ocean
        int[,] dist = new int[h, w];
        int big = 999999;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                dist[y, x] = oceanMask[y, x] ? 0 : big;
            }
        }

        // relax multiple times in forward/backward sweeps
        // few passes is enough (~4-6). coastRangePixels is our scale anyway.
        for (int iter = 0; iter < 4; iter++)
        {
            // forward
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int d = dist[y, x];
                    if (y > 0) d = Mathf.Min(d, dist[y - 1, x] + 1);
                    if (x > 0) d = Mathf.Min(d, dist[y, x - 1] + 1);
                    if (y > 0 && x > 0) d = Mathf.Min(d, dist[y - 1, x - 1] + 1);
                    if (y > 0 && x < w - 1) d = Mathf.Min(d, dist[y - 1, x + 1] + 1);
                    dist[y, x] = d;
                }
            }
            // backward
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    int d = dist[y, x];
                    if (y < h - 1) d = Mathf.Min(d, dist[y + 1, x] + 1);
                    if (x < w - 1) d = Mathf.Min(d, dist[y, x + 1] + 1);
                    if (y < h - 1 && x < w - 1) d = Mathf.Min(d, dist[y + 1, x + 1] + 1);
                    if (y < h - 1 && x > 0) d = Mathf.Min(d, dist[y + 1, x - 1] + 1);
                    dist[y, x] = d;
                }
            }
        }

        // normalize distance to 0..1 range based on coastRangePixels
        float[,] coastFactor = new float[h, w];
        float maxRange = Mathf.Max(1, coastRangePixels);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dNorm = Mathf.Clamp01(dist[y, x] / maxRange); // 0 at shore, 1 far inland
                // invert because we want 1 at shore, 0 inland
                coastFactor[y, x] = 1f - dNorm;
            }
        }

        // --- PASS C: final temperature ---
        var pixels = new Color[w * h];

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float t = baseTMap[py, px];
                float elev01 = elevMap[py, px];
                bool isOcean = oceanMask[py, px];

                // altitude cooling
                if (applyAltitudeCooling)
                {
                    t -= elev01 * altitudeCooling;
                }

                // ocean/coast moderation
                if (applyOceanModeration)
                {
                    // "surface water temp" idea:
                    // Start from baseT (already latitude-driven),
                    // then optionally soften extremes toward 0.5 so tropics cool a bit & poles warm a bit.
                    float oceanSurfaceT = Mathf.Lerp(baseTMap[py, px], 0.5f, oceanSoften);

                    if (isOcean)
                    {
                        // big pull on actual ocean
                        t = Mathf.Lerp(t, oceanSurfaceT, 1f); // full replace basically
                    }
                    else
                    {
                        // coast bleed. coastFactor ~1 near coast, 0 deep inland
                        float blend = coastFactor[py, px] * coastalBlendStrength;
                        if (blend > 0f)
                        {
                            t = Mathf.Lerp(t, oceanSurfaceT, blend);
                        }
                    }
                }

                t = Mathf.Clamp01(t);

                refinedTemperature01[py, px] = t;
                pixels[py * w + px] = new Color(t, t, t, 1f);
            }
        }

        refinedPreviewTexture.SetPixels(pixels);
        refinedPreviewTexture.Apply();
    }

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

        return Mathf.Lerp(h0mix, h1mix, fy); // 0..1 normalized
    }
}
