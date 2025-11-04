using UnityEngine;

[ExecuteAlways]
public class HumidityRefiner_RainShadow : MonoBehaviour
{
    public enum WindDirection
    {
        WestToEast,
        EastToWest,
        NorthToSouth,
        SouthToNorth
    }

    [Header("Resolution (match previous steps)")]
    [Range(16, 2048)] public int mapResolution = 512;

    [Header("Terrain input")]
    public Terrain terrain;

    [Header("Sea / Land")]
    [Tooltip("Below this normalized terrain height = ocean/water. Used for moisture recharge.")]
    [Range(0f, 1f)] public float seaLevel = 0.3f;

    [Header("Wind Model")]
    [Tooltip("Direction of prevailing moist air flow across the map.")]
    public WindDirection windDirection = WindDirection.WestToEast;

    [Tooltip("How fast air refills with moisture when crossing ocean tiles.")]
    [Range(0f, 1f)] public float oceanRecharge = 0.5f;

    [Tooltip("Base initial moisture carried by the air at the map edge.")]
    [Range(0f, 2f)] public float startingAirMoisture = 1.0f;

    [Header("Orographic Rainout")]
    [Tooltip("How sensitive we are to 'wall height'. Higher = more rain dumped at steep rises.")]
    [Range(0f, 5f)] public float ridgeSensitivity = 1.0f;

    [Tooltip("Extra humidity added on the windward cell when air hits a ridge.")]
    [Range(0f, 2f)] public float windwardRainBoost = 0.4f;

    [Tooltip("How much air moisture is lost after raining out on the ridge. Higher = drier downwind.")]
    [Range(0f, 2f)] public float leewardDryLoss = 0.6f;

    [Header("Shadow Persistence")]
    [Tooltip("How long the 'dry air' effect lingers downwind. 0 = effect resets immediately, 1 = fully persistent.")]
    [Range(0f, 1f)] public float shadowPersistence = 0.6f;

    [Header("Clamp")]
    [Tooltip("Never drop below this humidity.")]
    [Range(0f, 1f)] public float minHumidityFloor = 0.02f;

    [Tooltip("Never exceed this humidity.")]
    [Range(0f, 1f)] public float maxHumidityCeiling = 1.0f;

    [Header("Preview (read-only)")]
    public Texture2D rainShadowPreviewTexture;

    [HideInInspector] public float[,] rainShadowHumidity01; // final humidity after rain shadow step

    public void GenerateRainShadowHumidity()
    {
        // Step 3 (mountain drying) is our input
        var step3 = GetComponent<HumidityRefiner_MountainDrying>();
        if (step3 == null)
        {
            Debug.LogWarning("HumidityRefiner_RainShadow: No HumidityRefiner_MountainDrying found on this GameObject.");
            return;
        }

        if (step3.finalHumidity01 == null ||
            step3.finalHumidity01.GetLength(0) == 0 ||
            step3.finalHumidity01.GetLength(1) == 0)
        {
            // auto-run previous step if needed
            step3.GenerateMountainRefinedHumidity();
        }

        if (step3.finalHumidity01 == null)
        {
            Debug.LogWarning("HumidityRefiner_RainShadow: Step 3 still has no data.");
            return;
        }

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("HumidityRefiner_RainShadow: Terrain not assigned or invalid.");
            return;
        }

        TerrainData td = terrain.terrainData;
        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        // prepare output buffer
        if (rainShadowHumidity01 == null ||
            rainShadowHumidity01.GetLength(0) != h ||
            rainShadowHumidity01.GetLength(1) != w)
        {
            rainShadowHumidity01 = new float[h, w];
        }

        // preview texture
        if (rainShadowPreviewTexture == null ||
            rainShadowPreviewTexture.width != w ||
            rainShadowPreviewTexture.height != h)
        {
            rainShadowPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            rainShadowPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            rainShadowPreviewTexture.filterMode = FilterMode.Point;
        }

        // get terrain heights
        int hmRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes);

        // we start from step3 data as baseline
        // we'll create a working buffer we mutate line-by-line
        float[,] workHum = new float[h, w];
        CopyArrayBilinear(step3.finalHumidity01, workHum);

        // apply directional marching
        switch (windDirection)
        {
            case WindDirection.WestToEast:
                ApplyRainShadow_WestToEast(workHum, heights, hmRes);
                break;
            case WindDirection.EastToWest:
                ApplyRainShadow_EastToWest(workHum, heights, hmRes);
                break;
            case WindDirection.NorthToSouth:
                ApplyRainShadow_NorthToSouth(workHum, heights, hmRes);
                break;
            case WindDirection.SouthToNorth:
                ApplyRainShadow_SouthToNorth(workHum, heights, hmRes);
                break;
        }

        // clamp, bake to rainShadowHumidity01, and build preview
        Color[] pixels = new Color[w * h];
        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float val = workHum[py, px];
                if (val < minHumidityFloor) val = minHumidityFloor;
                if (val > maxHumidityCeiling) val = maxHumidityCeiling;

                rainShadowHumidity01[py, px] = val;

                pixels[py * w + px] = new Color(val, val, val, 1f);
            }
        }

        rainShadowPreviewTexture.SetPixels(pixels);
        rainShadowPreviewTexture.Apply();
    }

    //
    // Directional passes
    //
    void ApplyRainShadow_WestToEast(float[,] hum, float[,] heights, int hmRes)
    {
        int h = hum.GetLength(0);
        int w = hum.GetLength(1);

        for (int py = 0; py < h; py++)
        {
            float airMoisture = startingAirMoisture;
            float prevElev = SampleTerrainHeightBilinear(heights, hmRes, 0f, (float)py / (h - 1));

            for (int px = 0; px < w; px++)
            {
                float u = (float)px / (w - 1);
                float v = (float)py / (h - 1);

                float elev = SampleTerrainHeightBilinear(heights, hmRes, u, v);
                bool isOcean = elev < seaLevel;

                // recharge over ocean
                if (isOcean)
                {
                    airMoisture = Mathf.Lerp(airMoisture, 1f, oceanRecharge);
                }

                float ridgeDelta = Mathf.Max(0f, elev - prevElev); // how much we "climb"
                float ridgeStrength = ridgeDelta * ridgeSensitivity;

                if (ridgeStrength > 0f)
                {
                    // windward rainout: add humidity here
                    hum[py, px] += windwardRainBoost * ridgeStrength;

                    // lose moisture going forward (leeward will be drier)
                    airMoisture -= leewardDryLoss * ridgeStrength;
                    if (airMoisture < 0.05f) airMoisture = 0.05f; // floor
                }

                // apply current airMoisture to humidity (dry air -> less humidity downstream)
                hum[py, px] *= Mathf.Lerp(1f, airMoisture, shadowPersistence);

                prevElev = elev;
            }
        }
    }

    void ApplyRainShadow_EastToWest(float[,] hum, float[,] heights, int hmRes)
    {
        int h = hum.GetLength(0);
        int w = hum.GetLength(1);

        for (int py = 0; py < h; py++)
        {
            float airMoisture = startingAirMoisture;
            float prevElev = SampleTerrainHeightBilinear(heights, hmRes, 1f, (float)py / (h - 1));

            for (int px = w - 1; px >= 0; px--)
            {
                float u = (float)px / (w - 1);
                float v = (float)py / (h - 1);

                float elev = SampleTerrainHeightBilinear(heights, hmRes, u, v);
                bool isOcean = elev < seaLevel;

                if (isOcean)
                {
                    airMoisture = Mathf.Lerp(airMoisture, 1f, oceanRecharge);
                }

                float ridgeDelta = Mathf.Max(0f, elev - prevElev);
                float ridgeStrength = ridgeDelta * ridgeSensitivity;

                if (ridgeStrength > 0f)
                {
                    hum[py, px] += windwardRainBoost * ridgeStrength;
                    airMoisture -= leewardDryLoss * ridgeStrength;
                    if (airMoisture < 0.05f) airMoisture = 0.05f;
                }

                hum[py, px] *= Mathf.Lerp(1f, airMoisture, shadowPersistence);

                prevElev = elev;
            }
        }
    }

    void ApplyRainShadow_NorthToSouth(float[,] hum, float[,] heights, int hmRes)
    {
        int h = hum.GetLength(0);
        int w = hum.GetLength(1);

        for (int px = 0; px < w; px++)
        {
            float airMoisture = startingAirMoisture;
            float prevElev = SampleTerrainHeightBilinear(heights, hmRes, (float)px / (w - 1), 0f);

            for (int py = 0; py < h; py++)
            {
                float u = (float)px / (w - 1);
                float v = (float)py / (h - 1);

                float elev = SampleTerrainHeightBilinear(heights, hmRes, u, v);
                bool isOcean = elev < seaLevel;

                if (isOcean)
                {
                    airMoisture = Mathf.Lerp(airMoisture, 1f, oceanRecharge);
                }

                float ridgeDelta = Mathf.Max(0f, elev - prevElev);
                float ridgeStrength = ridgeDelta * ridgeSensitivity;

                if (ridgeStrength > 0f)
                {
                    hum[py, px] += windwardRainBoost * ridgeStrength;
                    airMoisture -= leewardDryLoss * ridgeStrength;
                    if (airMoisture < 0.05f) airMoisture = 0.05f;
                }

                hum[py, px] *= Mathf.Lerp(1f, airMoisture, shadowPersistence);

                prevElev = elev;
            }
        }
    }

    void ApplyRainShadow_SouthToNorth(float[,] hum, float[,] heights, int hmRes)
    {
        int h = hum.GetLength(0);
        int w = hum.GetLength(1);

        for (int px = 0; px < w; px++)
        {
            float airMoisture = startingAirMoisture;
            float prevElev = SampleTerrainHeightBilinear(heights, hmRes, (float)px / (w - 1), 1f);

            for (int py = h - 1; py >= 0; py--)
            {
                float u = (float)px / (w - 1);
                float v = (float)py / (h - 1);

                float elev = SampleTerrainHeightBilinear(heights, hmRes, u, v);
                bool isOcean = elev < seaLevel;

                if (isOcean)
                {
                    airMoisture = Mathf.Lerp(airMoisture, 1f, oceanRecharge);
                }

                float ridgeDelta = Mathf.Max(0f, elev - prevElev);
                float ridgeStrength = ridgeDelta * ridgeSensitivity;

                if (ridgeStrength > 0f)
                {
                    hum[py, px] += windwardRainBoost * ridgeStrength;
                    airMoisture -= leewardDryLoss * ridgeStrength;
                    if (airMoisture < 0.05f) airMoisture = 0.05f;
                }

                hum[py, px] *= Mathf.Lerp(1f, airMoisture, shadowPersistence);

                prevElev = elev;
            }
        }
    }

    //
    // helpers
    //
    void CopyArrayBilinear(float[,] src, float[,] dst)
    {
        // Copies src into dst using bilinear resample to match mapResolution
        int dstH = dst.GetLength(0);
        int dstW = dst.GetLength(1);

        int srcH = src.GetLength(0);
        int srcW = src.GetLength(1);

        for (int py = 0; py < dstH; py++)
        {
            float v = (dstH == 1) ? 0f : (float)py / (dstH - 1);
            for (int px = 0; px < dstW; px++)
            {
                float u = (dstW == 1) ? 0f : (float)px / (dstW - 1);
                dst[py, px] = SampleFloatArrayBilinear(src, u, v);
            }
        }
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

        return Mathf.Lerp(h0mix, h1mix, fy); // normalized 0..1
    }
}
