using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO; // <-- NEW

[ExecuteAlways]
public class BiomeGeneratorComponent : MonoBehaviour
{
    [Header("Resolution (match temp/humidity refiners)")]
    [Range(16, 2048)]
    public int mapResolution = 512;

    [Header("Inputs")]
    [Tooltip("Main terrain used for elevation / sea level check.")]
    public Terrain terrain;

    [Header("Elevation Normalization")]
    [Tooltip("If true, remaps elevation values based on actual min/max heights across the terrain data.")]
    public bool normalizeElevation = true;

    [Tooltip("Shows detected min/max elevation (after scan) for debug.")]
    public float detectedMinHeight;
    public float detectedMaxHeight;
    public float seaLevel = 0f;

    [Tooltip("Final temperature refiner (mountain cooling & snow mask). " +
             "If left null, we'll try GetComponent<TemperatureRefiner_MountainCooling>().")]
    public TemperatureRefiner_MountainCooling temperatureSource;

    [Tooltip("Final humidity refiner (mountain drying). " +
             "If left null, we'll try GetComponent<HumidityRefiner_MountainDrying>().")]
    public HumidityRefiner_MountainDrying humiditySource;

    [Tooltip("Optional. If assigned & generated, use wind-adjusted temperature instead of raw mountain-cooled.")]
    public TemperatureWindAdvectionComponent temperatureWindAdvection;

    [Tooltip("Optional. If assigned & generated, use wind-adjusted humidity instead of raw mountain-drying.")]
    public HumidityWindAdvectionComponent humidityWindAdvection;

    [Header("Biome Library")]
    [Tooltip("Folder with BiomeDefinition ScriptableObjects (for auto-load). " +
             "Drag the 'WorldGen/BiomesGenerated' folder here.")]
    public DefaultAsset biomeFolder; // this will actually be a folder asset

    [Tooltip("Order matters. First biome that matches temp/humidity/snow/height wins.")]
    public BiomeDefinition[] biomes;

    [Header("Ocean Debug Color")]
    [Tooltip("Color used for shallow water (just below sea level). " +
             "Deep water will lerp toward a darker version automatically.")]
    public Color shallowOceanColor = new Color(0.2f, 0.4f, 1f, 1f);

    [Tooltip("Multiplier for how dark deep water becomes.")]
    [Range(0f, 2f)]
    public float deepOceanDarken = 0.6f;

    [Tooltip("How deep (relative to seaLevel range) we consider 'max depth' for darkening curve. " +
             "0.3 means 30% below sea level counts as fully dark.")]
    [Range(0.05f, 1f)]
    public float oceanDepthRange01 = 0.3f;

    [Header("Preview (read-only)")]
    public Texture2D biomePreviewTexture;

    [Tooltip("Material used to preview biome map on terrain.")]
    public Material debugMaterial;

    // biome index per pixel. -1 = no biome found, -2 = ocean.
    [HideInInspector]
    public int[,] biomeIndexMap;


    /// <summary>
    /// Generate biome map + preview texture.
    /// Call this from the custom editor button.
    /// </summary>
    public void GenerateBiomeMap()
    {
        // --- Try to auto-wire missing refs ---
        if (temperatureSource == null)
            temperatureSource = GetComponent<TemperatureRefiner_MountainCooling>();

        if (humiditySource == null)
            humiditySource = GetComponent<HumidityRefiner_MountainDrying>();

        if (terrain == null && humiditySource != null)
            terrain = humiditySource.terrain;
        if (terrain == null && temperatureSource != null && terrain == null)
            terrain = temperatureSource.terrain;

        // --- Validate required inputs ---
        if (temperatureSource == null)
        {
            Debug.LogWarning("BiomeGeneratorComponent: temperatureSource is missing.");
            return;
        }

        if (humiditySource == null)
        {
            Debug.LogWarning("BiomeGeneratorComponent: humiditySource is missing.");
            return;
        }

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("BiomeGeneratorComponent: terrain is missing or invalid.");
            return;
        }

        // Make sure upstream data is available / generated
        if (temperatureSource.finalTemperature01 == null ||
            temperatureSource.finalTemperature01.GetLength(0) == 0 ||
            temperatureSource.finalTemperature01.GetLength(1) == 0)
        {
            temperatureSource.GenerateMountainRefinedTemperature();
        }

        if (humiditySource.finalHumidity01 == null ||
            humiditySource.finalHumidity01.GetLength(0) == 0 ||
            humiditySource.finalHumidity01.GetLength(1) == 0)
        {
            humiditySource.GenerateMountainRefinedHumidity();
        }

        if (temperatureSource.finalTemperature01 == null ||
            humiditySource.finalHumidity01 == null)
        {
            Debug.LogWarning("BiomeGeneratorComponent: upstream maps still missing after generation.");
            return;
        }

        // choose temperature map (wind-adjusted if available)
        float[,] tempData = null;
        if (temperatureWindAdvection != null &&
            temperatureWindAdvection.windAdjustedTemperature01 != null &&
            temperatureWindAdvection.windAdjustedTemperature01.GetLength(0) > 0)
        {
            tempData = temperatureWindAdvection.windAdjustedTemperature01;
        }
        else
        {
            tempData = temperatureSource.finalTemperature01;
        }

        // choose humidity map (wind-adjusted if available)
        float[,] humData = null;
        if (humidityWindAdvection != null &&
            humidityWindAdvection.windAdjustedHumidity01 != null &&
            humidityWindAdvection.windAdjustedHumidity01.GetLength(0) > 0)
        {
            humData = humidityWindAdvection.windAdjustedHumidity01;
        }
        else
        {
            humData = humiditySource.finalHumidity01;
        }

        // snow stays the same
        float[,] snowData = temperatureSource.snowMask01; // can still be null

        TerrainData td = terrain.terrainData;

        // Terrain raw heights for world elevation sampling:
        int hmRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes);

        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        // allocate biomeIndexMap if needed
        if (biomeIndexMap == null ||
            biomeIndexMap.GetLength(0) != h ||
            biomeIndexMap.GetLength(1) != w)
        {
            biomeIndexMap = new int[h, w];
        }

        // allocate / resize preview texture if needed
        if (biomePreviewTexture == null ||
            biomePreviewTexture.width != w ||
            biomePreviewTexture.height != h)
        {
            biomePreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            biomePreviewTexture.wrapMode = TextureWrapMode.Clamp;
            biomePreviewTexture.filterMode = FilterMode.Point;
        }

        // --- Compute actual min/max terrain elevation ---
        float minH = float.MaxValue;
        float maxH = float.MinValue;
        int hCount = heights.GetLength(0);
        int wCount = heights.GetLength(1);

        for (int y = 0; y < hCount; y++)
        {
            for (int x = 0; x < wCount; x++)
            {
                float hVal = heights[y, x];
                if (hVal < minH) minH = hVal;
                if (hVal > maxH) maxH = hVal;
            }
        }

        detectedMinHeight = minH;
        detectedMaxHeight = maxH;

        var pixels = new Color[w * h];

        // === DEBUG GAP TRACKING: we'll collect failed (no biome) buckets here ===
        Dictionary<string, GapInfo> gapBuckets = new Dictionary<string, GapInfo>();

        // --- Core loop ---
        for (int py = 0; py < h; py++)
        {
            float v = (float)py / (h - 1);

            for (int px = 0; px < w; px++)
            {
                float u = (float)px / (w - 1);

                // 1. Sample elevation in 0..1 terrain space
                float elev01 = SampleTerrainHeightBilinear(heights, hmRes, u, v);

                if (normalizeElevation && maxH > minH)
                {
                    elev01 = Mathf.InverseLerp(minH, maxH, elev01);
                }

                // 2. Ocean check
                if (elev01 < seaLevel)
                {
                    biomeIndexMap[py, px] = -2; // mark as ocean

                    // depth factor: 0 at seaLevel, 1 at "deep"
                    float depthBelow = (seaLevel - elev01); // positive if below
                    float depth01 = 0f;
                    if (oceanDepthRange01 > 0f)
                        depth01 = Mathf.Clamp01(depthBelow / oceanDepthRange01);

                    // Darken shallowOceanColor by lerping toward (shallowOceanColor * deepOceanDarken)
                    Color deepColor = shallowOceanColor * deepOceanDarken;
                    deepColor.a = 1f;

                    Color waterColor = Color.Lerp(shallowOceanColor, deepColor, depth01);
                    pixels[py * w + px] = waterColor;
                    continue;
                }

                // 3. Land: sample climate
                float temp01 = SampleFloatArrayBilinear(tempData, u, v);
                float hum01 = SampleFloatArrayBilinear(humData, u, v);
                float snow01 = (snowData != null && snowData.GetLength(0) > 0)
                               ? SampleFloatArrayBilinear(snowData, u, v)
                               : 0f;

                // 4. Pick biome index based on climate + elevation
                int biomeIdx = PickBiomeIndex(temp01, hum01, snow01, elev01);
                biomeIndexMap[py, px] = biomeIdx;

                // 5. Decide pixel color
                Color col;
                if (biomeIdx >= 0 && biomeIdx < biomes.Length && biomes[biomeIdx] != null)
                {
                    col = biomes[biomeIdx].debugColor;
                }
                else
                {
                    // no biome matched -> magenta for debugging gaps
                    col = new Color(1f, 0f, 1f, 1f);

                    // DEBUG GAP TRACKING:
                    // bucket by rounded values so we don't spam
                    string key = MakeGapKey(temp01, hum01, snow01, elev01);

                    if (!gapBuckets.TryGetValue(key, out GapInfo info))
                    {
                        info = new GapInfo
                        {
                            count = 0,
                            exampleTemp = temp01,
                            exampleHum = hum01,
                            exampleSnow = snow01,
                            exampleHeight = elev01,
                            exampleU = u,
                            exampleV = v
                        };
                    }

                    info.count++;
                    gapBuckets[key] = info;
                }

                pixels[py * w + px] = col;
            }
        }

        biomePreviewTexture.SetPixels(pixels);
        biomePreviewTexture.Apply();
        terrain.materialTemplate = debugMaterial;
        debugMaterial.SetTexture("_MainTex", biomePreviewTexture);

        // === DEBUG GAP TRACKING OUTPUT ===
        if (gapBuckets.Count > 0)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("BiomeGeneratorComponent: GAP REPORT (unmatched biome cases)");
            sb.AppendLine("Each bucket is rounded to 0.05 steps. Values are approx.");
            foreach (var kvp in gapBuckets)
            {
                var g = kvp.Value;
                sb.AppendLine(
                    $"• {kvp.Key} -> {g.count} px; " +
                    $"sample h={g.exampleHeight:F3}, t={g.exampleTemp:F3}, hum={g.exampleHum:F3}, snow={g.exampleSnow:F3}, uv=({g.exampleU:F3},{g.exampleV:F3})"
                );
            }
            Debug.LogWarning(sb.ToString());
            File.WriteAllText(
                System.IO.Path.Combine(
                    Application.dataPath,
                    "D://Downloads/biome_gap_report.txt"
                ),
                sb.ToString()
            );
        }
        else
        {
            Debug.Log("BiomeGeneratorComponent: No biome gaps 🎉");
        }
    }


    /// <summary>
    /// Returns index in 'biomes' array.
    /// First match that says "true" in b.Matches(...) wins.
    /// If nothing matches, returns -1.
    /// </summary>
    int PickBiomeIndex(float temp01, float hum01, float snow01, float height01)
    {
        if (biomes == null) return -1;

        for (int i = 0; i < biomes.Length; i++)
        {
            var b = biomes[i];
            if (b == null) continue;

            if (b.Matches(temp01, hum01, snow01, height01))
                return i;
        }

        return -1;
    }


    // === Utility samplers ===

    // Bilinear sampler for float[,] arrays [y,x]
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

    // Bilinear sampler for terrain heightmap array (also [y,x])
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

        return Mathf.Lerp(h0mix, h1mix, fy); // normalized 0..1 from TerrainData
    }


    // === DEBUG HELPERS ===

    struct GapInfo
    {
        public int count;

        public float exampleTemp;
        public float exampleHum;
        public float exampleSnow;
        public float exampleHeight;

        public float exampleU;
        public float exampleV;
    }

    // Round value to nearest 0.05 so we cluster similar pixels
    float RoundToStep(float v, float step)
    {
        if (step <= 0f) return v;
        return Mathf.Round(v / step) * step;
    }

    string MakeGapKey(float temp01, float hum01, float snow01, float height01)
    {
        float tR = RoundToStep(temp01, 0.05f);
        float hR = RoundToStep(hum01, 0.05f);
        float sR = RoundToStep(snow01, 0.05f);
        float eR = RoundToStep(height01, 0.05f);

        // Key is human-readable and stable enough
        return $"H={eR:0.00} T={tR:0.00} Hum={hR:0.00} Snow={sR:0.00}";
    }
}
