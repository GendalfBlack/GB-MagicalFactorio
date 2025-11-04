using UnityEngine;

[ExecuteAlways]
public class HumidityMapGeneratorComponent : MonoBehaviour
{
    [Header("Global Seed Source")]
    [Tooltip("Drag the object that has WorldSeedComponent here.")]
    public WorldSeedComponent worldSeedSource;

    [Header("Output Resolution")]
    [Range(16, 2048)] public int mapResolution = 512;

    [Header("Latitude → Humidity Curve")]
    [Tooltip("Higher = moist equator stretches farther toward poles.")]
    [Range(0.5f, 3f)] public float humidityFalloff = 1.2f;

    [Tooltip("Equator humidity (0..1). Typically high warm air moisture).")]
    [Range(0f, 1f)] public float equatorHumidity = 0.9f;

    [Tooltip("Pole humidity (0..1). Usually cold/dry air).")]
    [Range(0f, 1f)] public float poleHumidity = 0.2f;

    [Header("Noise (uses BaseNoiseGenerator)")]
    [Tooltip("How strongly noise warps humidity bands.")]
    [Range(0f, 1f)] public float noiseStrength = 0.25f;

    [Tooltip("Bigger scale = larger features (passed into BaseNoiseGenerator as noiseScale).")]
    [Range(0f, 20f)] public float noiseScale = 0.2f;

    [Tooltip("Number of fractal octaves for humidity noise.")]
    [Range(1, 8)] public int octaves = 4;

    [Tooltip("Amplitude drop per octave (<1 means each octave is weaker).")]
    [Range(0.1f, 1f)] public float persistence = 0.5f;

    [Tooltip("Frequency growth per octave (>1 means each octave is higher freq).")]
    [Range(1f, 4f)] public float lacunarity = 2f;

    [Tooltip("How strong the Hadley/Ferrel/Polar wet/dry bands affect humidity.")]
    [Range(0f, 0.5f)] public float cellStrength = 0.25f;

    [Header("Preview (read-only)")]
    public Texture2D basePreviewTexture;

    [HideInInspector] public float[,] baseHumidity01;   // final humidity 0..1
    [HideInInspector] public float[,] humidityNoise01;  // raw noise 0..1 from BaseNoiseGenerator

    public void GenerateBaseHumidity()
    {
        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        // --- 1. Resolve seed ---
        int seed = 0;

        // prefer explicit global reference
        if (worldSeedSource != null)
        {
            seed = worldSeedSource.worldSeed;
        }
        else
        {
            // fallback: try same object (nice for quick testing)
            var localSeedComp = GetComponent<WorldSeedComponent>();
            if (localSeedComp != null)
            {
                seed = localSeedComp.worldSeed;
            }
        }

        // still allow non-zero guarantee
        if (seed == 0)
        {
            seed = Random.Range(1, int.MaxValue);
            Debug.Log($"HumidityMapGeneratorComponent: No valid worldSeed, using random {seed}");
        }

        // --- 2. Build deterministic noise with BaseNoiseGenerator ---
        // biasSource = null and biasStrength = 0f means: just pure seeded fractal noise, 0..1
        humidityNoise01 = BaseNoiseGenerator.GenerateHeightmap(
            w,
            h,
            seed,
            noiseScale,
            octaves,
            persistence,
            lacunarity,
            null,
            0f
        );
        // Note: GenerateHeightmap returns float[width,height]

        // --- 3. Ensure buffers/preview exist ---
        if (baseHumidity01 == null ||
            baseHumidity01.GetLength(0) != h ||
            baseHumidity01.GetLength(1) != w)
        {
            baseHumidity01 = new float[h, w];
        }

        if (basePreviewTexture == null ||
            basePreviewTexture.width != w ||
            basePreviewTexture.height != h)
        {
            basePreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            basePreviewTexture.wrapMode = TextureWrapMode.Clamp;
            basePreviewTexture.filterMode = FilterMode.Point;
        }

        var pixels = new Color[w * h];

        // --- 4. Compose humidity from latitude profile + noisy circulation cells ---
        for (int py = 0; py < h; py++)
        {
            float v = (float)py / (h - 1);
            float lat01 = Mathf.Abs(v - 0.5f) * 2f;
            float rawLatHum01 = 1f - Mathf.Pow(lat01, humidityFalloff);
            float latBandHumidity = Mathf.Lerp(poleHumidity, equatorHumidity, rawLatHum01);

            // Convert to latitude degrees for cell logic
            float latitudeDeg = (v - 0.5f) * 180f;
            float absLat = Mathf.Abs(latitudeDeg);

            // Define base circulation intensity (-1 dry → +1 humid)
            // --- Smooth atmospheric circulation gradient ---
            float cellBase;

            // Define key latitude bands (degrees)
            float eqWet = 0f;      // equator humid
            float dry1 = 25f;      // subtropical dry
            float wet2 = 45f;      // mid-lat humid
            float dry3 = 75f;      // polar dry

            // Smooth transitions using inverse lerp
            if (absLat < dry1)
            {
                // Transition from +0.8 (wet) → -1.0 (dry)
                float t = Mathf.InverseLerp(eqWet, dry1, absLat);
                cellBase = Mathf.Lerp(+0.8f, -1.0f, Mathf.SmoothStep(0f, 1f, t));
            }
            else if (absLat < wet2)
            {
                // Transition from -1.0 → +0.6
                float t = Mathf.InverseLerp(dry1, wet2, absLat);
                cellBase = Mathf.Lerp(-1.0f, +0.6f, Mathf.SmoothStep(0f, 1f, t));
            }
            else if (absLat < dry3)
            {
                // Transition from +0.6 → -0.8
                float t = Mathf.InverseLerp(wet2, dry3, absLat);
                cellBase = Mathf.Lerp(+0.6f, -0.8f, Mathf.SmoothStep(0f, 1f, t));
            }
            else
            {
                // Clamp near poles
                cellBase = -0.8f;
            }                  

            for (int px = 0; px < w; px++)
            {
                float u = (float)px / (w - 1);

                // Now get base humidity noise
                float noiseSample01 = SampleBilinear(humidityNoise01, u, v);
                float noiseCentered = noiseSample01 - 0.5f;

                // Combine all influences
                float hum = latBandHumidity
                            + cellBase * cellStrength   // broad-scale cell shaping
                            + noiseCentered * noiseStrength;     // local terrain-scale noise

                hum = Mathf.Clamp01(hum);

                baseHumidity01[py, px] = hum;
                pixels[py * w + px] = new Color(hum, hum, hum, 1f);
            }
        }

        basePreviewTexture.SetPixels(pixels);
        basePreviewTexture.Apply();
    }

    // Bilinear sampler for arrays shaped [width,height] like BaseNoiseGenerator returns.
    // u,v are normalized 0..1
    private float SampleBilinear(float[,] dataWH, float u, float v)
    {
        int w = dataWH.GetLength(0);
        int h = dataWH.GetLength(1);

        float fx = u * (w - 1);
        float fy = v * (h - 1);

        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int x1 = Mathf.Min(x0 + 1, w - 1);
        int y1 = Mathf.Min(y0 + 1, h - 1);

        float tx = fx - x0;
        float ty = fy - y0;

        float a = dataWH[x0, y0];
        float b = dataWH[x1, y0];
        float c = dataWH[x0, y1];
        float d = dataWH[x1, y1];

        float ab = Mathf.Lerp(a, b, tx);
        float cd = Mathf.Lerp(c, d, tx);

        return Mathf.Lerp(ab, cd, ty);
    }
}
