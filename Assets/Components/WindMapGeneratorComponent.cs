using UnityEngine;

[ExecuteAlways]
public class WindMapGeneratorComponent : MonoBehaviour
{
    [Header("Resolution (match climate maps)")]
    [Range(16, 2048)] public int mapResolution = 512;

    [Header("Terrain Input")]
    [Tooltip("Same Terrain you feed into temperature/humidity refiners.")]
    public Terrain terrain;

    [Tooltip("Anything below this normalized height is considered ocean.")]
    [Range(0f, 1f)] public float seaLevel = 0.3f;

    [Header("Global Circulation")]
    [Tooltip("How strong the base planetary wind is before terrain distortion.")]
    [Range(0f, 2f)] public float baseWindStrength = 1.0f;

    [Tooltip("How much high mountains slow down near-surface wind.")]
    [Range(0f, 2f)] public float mountainBlockStrength = 0.8f;

    [Tooltip("Heights above this are considered 'blocking ridges' that slow wind.")]
    [Range(0f, 1f)] public float mountainBlockHeight = 0.6f;

    [Tooltip("How wide (in pixels) we look upwind to detect a blocking ridge.")]
    [Range(1, 8)] public int ridgeLookDistance = 3;

    [Header("Wind Variation Noise")]
    [Tooltip("Seeded per-world directional wobble / calm zones.")]
    public WorldSeedComponent worldSeedSource;

    [Range(0f, 1f)]
    public float directionJitter = 0.3f;
    // how hard we bend the base belt direction (0 = pure belt, 1 = can swing a lot)

    [Range(0f, 1f)]
    public float calmZonesStrength = 0.4f;
    // how hard we locally slow wind to create dead spots

    [Range(0f, 100f)]
    public float windNoiseScale = 1.5f;
    // spatial size of that noise (bigger = big continents of altered flow, smaller = chaotic)


    [Header("Preview (read-only)")]
    [Tooltip("R = X vel, G = Y vel, B = speed mag, A=1")]
    public Texture2D windPreviewTexture;

    // Wind field data exposed for other steps:
    // Note: [y,x] like your refinedHumidity/Temperature.
    [HideInInspector] public Vector2[,] windVector;   // normalized-ish local wind dir/speed per pixel
    [HideInInspector] public float[,] windSpeed01;    // 0..1 strength map for convenience

    /// <summary>
    /// Call this to regenerate the wind map.
    /// </summary>
    public void GenerateWindMap()
    {
        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("WindMapGeneratorComponent: Terrain not assigned or invalid.");
            return;
        }

        TerrainData td = terrain.terrainData;
        int hmRes = td.heightmapResolution;

        // prepare buffers
        if (windVector == null ||
            windVector.GetLength(0) != h ||
            windVector.GetLength(1) != w)
        {
            windVector = new Vector2[h, w];
        }

        if (windSpeed01 == null ||
            windSpeed01.GetLength(0) != h ||
            windSpeed01.GetLength(1) != w)
        {
            windSpeed01 = new float[h, w];
        }

        if (windPreviewTexture == null ||
            windPreviewTexture.width != w ||
            windPreviewTexture.height != h)
        {
            windPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            windPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            windPreviewTexture.filterMode = FilterMode.Point;
        }

        var pixels = new Color[w * h];

        // We will do 2 passes:
        // Pass A: compute base large-scale wind from latitude cells.
        // Pass B: apply terrain blocking / slowdown.

        // --- PASS A: base field from latitude circulation (smoothed) ---
        Vector2[,] baseWind = new Vector2[h, w];

        for (int py = 0; py < h; py++)
        {
            float v = (float)py / (h - 1);

            // latitudeDeg: -90 (bottom) .. +90 (top)
            float latitudeDeg = (v - 0.5f) * 180f;
            float absLat = Mathf.Abs(latitudeDeg);

            // --- 1. Define the 3 canonical wind patterns ---

            // Trades (0°–30°): westward, drift toward equator
            //  north hemi -> a bit south (y-)
            //  south hemi -> a bit north (y+)
            float yTowardEquator = (latitudeDeg > 0f) ? -1f : 1f;
            Vector2 dirTrades = new Vector2(-1f, yTowardEquator * 0.3f);

            // Westerlies (30°–60°): eastward, drift poleward
            float yTowardPole = (latitudeDeg > 0f) ? 1f : -1f;
            Vector2 dirWesterlies = new Vector2(+1f, yTowardPole * 0.2f);

            // Polar easterlies (60°–90°): westward, drift away from pole (toward equator)
            float yAwayFromPole = (latitudeDeg > 0f) ? -1f : 1f;
            Vector2 dirPolar = new Vector2(-0.7f, yAwayFromPole * 0.2f);

            // --- 2. Compute smooth weights for each band ---
            //
            // We'll treat the centers of the cells as:
            //   Trades center ~15°
            //   Westerlies center ~45°
            //   Polar center ~75°
            //
            // And fade each band in/out over ~30° total width (so ~15° each side).
            //
            // We'll create 3 weights based on distance from these centers:
            float distTrades = Mathf.Abs(absLat - 15f); // best around equator-tropics
            float distWesterlies = Mathf.Abs(absLat - 45f); // best around mid-lat
            float distPolar = Mathf.Abs(absLat - 75f); // best around polar front

            // How wide each cell influence is, in degrees.
            // Bigger number = softer transitions.
            float falloff = 50f;

            // Convert distance to unnormalized weight using inverse falloff.
            // At distance 0 -> weight ~1
            // At distance >= falloff -> weight ~0
            float wTrades = Mathf.Clamp01(1f - distTrades / falloff);
            float wWesterlies = Mathf.Clamp01(1f - distWesterlies / falloff);
            float wPolar = Mathf.Clamp01(1f - distPolar / falloff);

            // Avoid all-zero (shouldn't happen but just in case)
            float wSum = wTrades + wWesterlies + wPolar;
            if (wSum < 0.0001f)
            {
                // fallback: just pick something reasonable for extreme poles
                if (absLat < 30f) wTrades = 1f;
                else if (absLat < 60f) wWesterlies = 1f;
                else wPolar = 1f;
                wSum = wTrades + wWesterlies + wPolar;
            }

            // Normalize so weights add up to 1
            wTrades /= wSum;
            wWesterlies /= wSum;
            wPolar /= wSum;

            // --- 3. Blend the 3 canonical directions using the weights ---
            Vector2 blended =
                dirTrades * wTrades +
                dirWesterlies * wWesterlies +
                dirPolar * wPolar;

            // normalize + scale by baseWindStrength
            float mag = blended.magnitude;
            if (mag > 0.0001f)
                blended /= mag;
            blended *= baseWindStrength;

            // --- 4. Store row result ---
            for (int px = 0; px < w; px++)
            {
                baseWind[py, px] = blended;
            }
        }

        // --- NEW PASS A2: apply large-scale noise wobble and calm pockets ---

        // 1) resolve seed (like in humidity)
        int seed = 0;
        if (worldSeedSource != null) seed = worldSeedSource.worldSeed;
        if (seed == 0) seed = Random.Range(1, int.MaxValue);

        // 2) generate two noise maps:
        //    - angleNoise: turns into small rotation of wind direction
        //    - calmNoise: turns into local slowdown pockets
        float[,] angleNoise = BaseNoiseGenerator.GenerateHeightmap(
            w, h, seed + 12345,
            windNoiseScale,
            4,          // octaves
            0.4f,       // persistence
            1.5f,         // lacunarity
            null,
            0f
        );

        float[,] calmNoise = BaseNoiseGenerator.GenerateHeightmap(
            w, h, seed + 54321,
            windNoiseScale,
            4,
            0.4f,
            1.5f,
            null,
            0f
        );

        // 3) apply wobble to each pixel in baseWind
        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                Vector2 wv = baseWind[py, px];
                float baseMag = wv.magnitude;
                if (baseMag > 0.0001f)
                {
                    // normalize for rotation
                    Vector2 dirN = wv / baseMag;

                    // angleNoise 0..1 -> -1..1
                    float angJit = (angleNoise[px, py] - 0.5f) * 2f;
                    // rotate direction a bit
                    float angleRad = angJit * directionJitter * Mathf.PI;
                    float cosA = Mathf.Cos(angleRad);
                    float sinA = Mathf.Sin(angleRad);

                    Vector2 rotatedDir = new Vector2(
                        dirN.x * cosA - dirN.y * sinA,
                        dirN.x * sinA + dirN.y * cosA
                    );

                    // calmNoise 0..1 -> 0..1
                    float calmFactor = 1f - calmZonesStrength * calmNoise[px, py];
                    calmFactor = Mathf.Clamp01(calmFactor);

                    // rebuild vector with slightly rotated dir and maybe reduced magnitude
                    baseWind[py, px] = rotatedDir * (baseMag * calmFactor);
                }
            }
        }

        // --- PASS B: terrain blocking / slowdown
        // heuristic:
        // 1. Sample local elevation.
        // 2. Look "upwind" a few pixels; if any of them are above mountainBlockHeight,
        //    reduce final speed here (mountain shadow).
        // 3. Also, if THIS pixel is very high (ridge itself), also reduce.
        float[,] heights01 = td.GetHeights(0, 0, hmRes, hmRes);

        for (int py = 0; py < h; py++)
        {
            float v = (float)py / (h - 1);

            for (int px = 0; px < w; px++)
            {
                float u = (float)px / (w - 1);

                // sample height 0..1
                float elev01 = SampleTerrainHeightBilinear(heights01, hmRes, u, v);

                Vector2 wv = baseWind[py, px];

                // compute slowdown factor (1 means no slowdown)
                float slowFactor = 1f;

                // 2. look upwind: march opposite of wv
                if (wv.sqrMagnitude > 0.00001f && ridgeLookDistance > 0)
                {
                    Vector2 dirUpwind = -wv.normalized;
                    // step in texture pixel space (since we're looping pixel coords)
                    // convert dirUpwind from [0..1 uv] direction to pixel steps:
                    // du = dirUpwind.x in "east-west", dv = dirUpwind.y in "north-south"
                    // px runs east-west, py runs south-north, so:
                    for (int step = 1; step <= ridgeLookDistance; step++)
                    {
                        float samplePxF = px + dirUpwind.x * step;
                        float samplePyF = py + dirUpwind.y * step;

                        int sx = Mathf.RoundToInt(samplePxF);
                        int sy = Mathf.RoundToInt(samplePyF);

                        if (sx < 0 || sx >= w || sy < 0 || sy >= h)
                            break;

                        float su = (float)sx / (w - 1);
                        float sv = (float)sy / (h - 1);

                        float elevUpwind = SampleTerrainHeightBilinear(heights01, hmRes, su, sv);

                        if (elevUpwind >= mountainBlockHeight)
                        {
                            // more blocking if tall ridge upwind
                            float blockAmount = (elevUpwind - mountainBlockHeight) * mountainBlockStrength;
                            slowFactor -= blockAmount;
                        }
                    }
                }

                // 3. also if current pixel itself is high
                if (elev01 >= mountainBlockHeight)
                {
                    float selfBlock = (elev01 - mountainBlockHeight) * mountainBlockStrength;
                    slowFactor -= selfBlock * 0.5f; // slightly less than full, it's a ridge not shadow
                }

                slowFactor = Mathf.Clamp01(slowFactor);

                Vector2 finalWind = wv * slowFactor;

                // store
                windVector[py, px] = finalWind;

                // magnitude for helpers
                float speed = finalWind.magnitude;
                windSpeed01[py, px] = Mathf.Clamp01(speed); // not physically scaled, just 0..1-ish

                // build debug pixel:
                // pack x into R: 0.5 means 0, 1 means strong +x, 0 means strong -x
                // pack y into G similarly
                float r = 0.5f + finalWind.x * 0.5f; // map -1..1 → 0..1
                float g = 0.5f + finalWind.y * 0.5f;
                float b = Mathf.Clamp01(speed);     // brightness = wind speed
                pixels[py * w + px] = new Color(r, g, b, 1f);
            }
        }

        // apply preview
        windPreviewTexture.SetPixels(pixels);
        windPreviewTexture.Apply();
    }

    // --- Helpers ---

    // Bilinear sampler for terrain heights (0..1) similar to your refiners
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
