using UnityEngine;

[ExecuteAlways]
public class HumidityRefiner_OceanAltitude : MonoBehaviour
{
    [Header("Resolution (usually same as Step 1)")]
    [Range(16, 2048)] public int mapResolution = 512;

    [Header("Terrain input")]
    public Terrain terrain; // assign your Terrain here

    [Header("Elevation / Sea")]
    [Tooltip("Anything below this normalized height is considered ocean.")]
    [Range(0f, 1f)] public float seaLevel = 0.3f;

    [Tooltip("How much elevation dries air. 0 = ignore, 2 = very strong drying.")]
    [Range(0f, 2f)] public float altitudeDrying = 0.5f;
    public bool applyAltitudeDrying = true;

    [Header("Ocean & Coast Influence")]
    public bool applyOceanInfluence = true;

    [Tooltip("How 'saturated' marine air is. 1 = almost rainforest air, 0 = desert air.")]
    [Range(0f, 1f)] public float oceanSaturation = 0.8f; // replaces old oceanTargetHumidity

    [Tooltip("How much we force ocean pixels toward oceanSaturation before bleeding inland.")]
    [Range(0f, 1f)] public float oceanWaterBlend = 0.7f; // replaces old oceanInfluence for water itself

    [Tooltip("How far inland moist sea air travels, in pixels at current resolution.")]
    [Range(0, 200)] public int coastRangePixels = 40;

    [Tooltip("How strongly coastal land is pulled toward moist air.\n0 = ignore coast bleed, 1 = super humid coasts.")]
    [Range(0f, 1f)] public float coastalBlendStrength = 0.6f;

    [Header("Preview (read-only)")]
    public Texture2D refinedPreviewTexture;

    [HideInInspector] public float[,] refinedHumidity01; // [y,x]

    public void GenerateRefinedHumidity()
    {
        // --- Step 1: ensure we have the base humidity from step 1
        var baseComp = GetComponent<HumidityMapGeneratorComponent>();
        if (baseComp == null)
        {
            Debug.LogWarning("HumidityRefiner_OceanAltitude: No HumidityMapGeneratorComponent found on this GameObject.");
            return;
        }

        if (baseComp.baseHumidity01 == null ||
            baseComp.baseHumidity01.GetLength(0) == 0 ||
            baseComp.baseHumidity01.GetLength(1) == 0)
        {
            baseComp.GenerateBaseHumidity();
        }

        // --- Step 2: terrain check
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("HumidityRefiner_OceanAltitude: Terrain not assigned or invalid.");
            return;
        }

        TerrainData td = terrain.terrainData;

        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        // --- Step 3: allocate outputs if needed
        if (refinedHumidity01 == null ||
            refinedHumidity01.GetLength(0) != h ||
            refinedHumidity01.GetLength(1) != w)
        {
            refinedHumidity01 = new float[h, w];
        }

        if (refinedPreviewTexture == null ||
            refinedPreviewTexture.width != w ||
            refinedPreviewTexture.height != h)
        {
            refinedPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            refinedPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            refinedPreviewTexture.filterMode = FilterMode.Point;
        }

        // --- Step 4: read full terrain heights
        int hmRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes);

        // --- PREPASS A: cache base humidity, elevation, and mark oceans
        float[,] baseHMap = new float[h, w];
        float[,] elevMap = new float[h, w];
        bool[,] oceanMask = new bool[h, w];

        for (int py = 0; py < h; py++)
        {
            float v = (float)py / (h - 1);
            for (int px = 0; px < w; px++)
            {
                float u = (float)px / (w - 1);

                float baseH = SampleFloatArrayBilinear(baseComp.baseHumidity01, u, v);
                float elev01 = SampleTerrainHeightBilinear(heights, hmRes, u, v);

                baseHMap[py, px] = baseH;
                elevMap[py, px] = elev01;
                oceanMask[py, px] = (elev01 < seaLevel);
            }
        }

        // --- PREPASS B: build distance-to-ocean map (approximate, iterative relax)
        int[,] dist = new int[h, w];
        int big = 999999;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                dist[y, x] = oceanMask[y, x] ? 0 : big;
            }
        }

        // a few forward/backward passes to propagate "distance in pixels"
        for (int iter = 0; iter < 4; iter++)
        {
            // forward sweep
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

            // backward sweep
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

        // turn distance into coastal factor:
        // coastFactor = 1.0 on ocean/shoreline, fades to 0 inland after coastRangePixels
        float[,] coastFactor = new float[h, w];
        float maxRange = Mathf.Max(1, coastRangePixels);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dNorm = Mathf.Clamp01(dist[y, x] / maxRange); // 0 .. 1
                coastFactor[y, x] = 1f - dNorm; // 1 near coast, 0 deep inland
            }
        }

        // --- PASS C: final humidity
        var pixels = new Color[w * h];

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float hVal = baseHMap[py, px];
                float elev01 = elevMap[py, px];
                bool isOcean = oceanMask[py, px];

                if (applyOceanInfluence)
                {
                    // Step 1: get "marine air" humidity at this pixel.
                    // For ocean pixels we push humidity toward oceanSaturation.
                    // For land pixels we will still use that marine air as the coastal source.
                    float marineHumidity = Mathf.Lerp(hVal, oceanSaturation, oceanWaterBlend);

                    if (isOcean)
                    {
                        // Over water: large pull straight to saturated marineHumidity.
                        hVal = marineHumidity;
                    }
                    else
                    {
                        // Over land: bleed that marineHumidity inland.
                        float coastalBlend = coastFactor[py, px] * coastalBlendStrength;
                        if (coastalBlend > 0f)
                        {
                            hVal = Mathf.Lerp(hVal, marineHumidity, coastalBlend);
                        }
                    }
                }

                // Altitude drying last (mountains wring out moisture)
                if (applyAltitudeDrying)
                {
                    hVal -= elev01 * altitudeDrying;
                }

                hVal = Mathf.Clamp01(hVal);

                refinedHumidity01[py, px] = hVal;
                pixels[py * w + px] = new Color(hVal, hVal, hVal, 1f);
            }
        }

        // --- push preview texture
        refinedPreviewTexture.SetPixels(pixels);
        refinedPreviewTexture.Apply();
    }

    // Bilinear sampler for [y,x] float[,] arrays like baseHumidity01
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

    // Bilinear sampler for terrain heightmap
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
