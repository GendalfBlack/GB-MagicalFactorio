using UnityEngine;

[ExecuteAlways]
public class TemperatureRefiner_MountainCooling : MonoBehaviour
{
    [Header("Resolution (match previous steps)")]
    [Range(16, 2048)] public int mapResolution = 512;

    [Header("Terrain input")]
    public Terrain terrain; // same Terrain as Step 2

    [Header("Mountain Cooling")]
    [Tooltip("Heights above this start to cool faster.")]
    [Range(0f, 1f)] public float mountainStartHeight = 0.5f;

    [Tooltip("Max extra cooling applied at highest peaks.")]
    [Range(0f, 2f)] public float mountainCoolingStrength = 0.8f;

    [Tooltip("Power curve. Higher = only very high peaks get big cooling.")]
    [Range(0.5f, 5f)] public float mountainCoolingCurve = 2f;

    public bool applyMountainCooling = true;

    [Header("Snowcaps")]
    [Tooltip("Heights above this are considered 'always snowy'.")]
    [Range(0f, 1f)] public float snowCapHeight = 0.8f;

    [Tooltip("Target temperature for snowcaps.")]
    [Range(0f, 1f)] public float snowCapTemp = 0.1f;

    [Tooltip("How hard we force peaks toward snowCapTemp.")]
    [Range(0f, 1f)] public float snowCapBlend = 0.7f;

    public bool applySnowCaps = true;

    [Header("Preview (read-only)")]
    public Texture2D finalPreviewTexture;

    [HideInInspector] public float[,] finalTemperature01; // [y,x] after mountain logic
    [HideInInspector] public float[,] snowMask01;          // [y,x] 0..1 (mostly for biome use)

    public void GenerateMountainRefinedTemperature()
    {
        // Step 2 is our input
        var step2 = GetComponent<TemperatureRefiner_OceanAltitude>();
        if (step2 == null)
        {
            Debug.LogWarning("MountainCooling: No TemperatureRefiner_OceanAltitude found on this GameObject.");
            return;
        }

        // make sure step2 has data
        if (step2.refinedTemperature01 == null ||
            step2.refinedTemperature01.GetLength(0) == 0 ||
            step2.refinedTemperature01.GetLength(1) == 0)
        {
            // try to generate step2 automatically
            step2.GenerateRefinedTemperature();
        }

        if (step2.refinedTemperature01 == null)
        {
            Debug.LogWarning("MountainCooling: Step 2 still has no data.");
            return;
        }

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("MountainCooling: Terrain not assigned or invalid.");
            return;
        }

        TerrainData td = terrain.terrainData;

        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        // output buffers
        if (finalTemperature01 == null ||
            finalTemperature01.GetLength(0) != h ||
            finalTemperature01.GetLength(1) != w)
        {
            finalTemperature01 = new float[h, w];
        }

        if (snowMask01 == null ||
            snowMask01.GetLength(0) != h ||
            snowMask01.GetLength(1) != w)
        {
            snowMask01 = new float[h, w];
        }

        // preview texture
        if (finalPreviewTexture == null ||
            finalPreviewTexture.width != w ||
            finalPreviewTexture.height != h)
        {
            finalPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            finalPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            finalPreviewTexture.filterMode = FilterMode.Point;
        }

        // terrain heights
        int hmRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes);

        var pixels = new Color[w * h];

        for (int py = 0; py < h; py++)
        {
            float v = (float)py / (h - 1);

            for (int px = 0; px < w; px++)
            {
                float u = (float)px / (w - 1);

                // 1) get temp from previous step (bilinear from refinedTemperature01)
                float temp = SampleFloatArrayBilinear(
                    step2.refinedTemperature01,
                    u, v
                );

                // 2) get elevation
                float elev01 = SampleTerrainHeightBilinear(
                    heights, hmRes,
                    u, v
                );

                // --- Mountain cooling ---
                if (applyMountainCooling && elev01 > mountainStartHeight)
                {
                    // how far above start
                    float mountainFactor = Mathf.InverseLerp(mountainStartHeight, 1f, elev01);
                    // shape it with power curve
                    mountainFactor = Mathf.Pow(mountainFactor, mountainCoolingCurve);
                    // apply cooling proportional to strength
                    temp -= mountainFactor * mountainCoolingStrength;
                }

                // --- Snowcaps clamp ---
                float snowMask = 0f;
                if (applySnowCaps && elev01 > snowCapHeight)
                {
                    float snowFactor = Mathf.InverseLerp(snowCapHeight, 1f, elev01); // 0..1 across high peaks
                    snowFactor = Mathf.Clamp01(snowFactor);

                    // this is where we "force toward icy temp"
                    float forced = Mathf.Lerp(temp, snowCapTemp, snowCapBlend * snowFactor);

                    temp = forced;

                    // snowMask is useful for biome classification later
                    snowMask = snowFactor;
                }

                temp = Mathf.Clamp01(temp);

                finalTemperature01[py, px] = temp;
                snowMask01[py, px] = snowMask;

                // preview: grayscale temp
                pixels[py * w + px] = new Color(temp, temp, temp, 1f);
            }
        }

        finalPreviewTexture.SetPixels(pixels);
        finalPreviewTexture.Apply();
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
