using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Step X: Coastal gyre / ocean swirl refiner for low-altitude wind.
/// Hybrid approach:
/// - Terrain seaLevel -> smooth water mask and shoreline falloff (coastalFactor).
/// - Voronoi + PlateAssigner -> logical ocean basins and basin centroids.
/// - WindMapGeneratorComponent -> base wind field (global belts + terrain blocking).
///
/// Process:
/// 1. Sample terrain height to mark water pixels (height < seaLevel).
/// 2. Compute distToShore over water with a BFS from coastline water pixels.
/// 3. From Voronoi+PlateAssigner: detect "oceanic" regions (PlateType.Oceanic).
/// 4. Build region adjacency graph with border lengths (scan RIGHT/UP).
///    Merge oceanic regions into basins ONLY if they share at least
///    minSharedBorderForMerge pixels of common border.
///    => prevents 1-pixel bridges from merging huge chains.
/// 5. For each basin, gather its pixels (only where terrain says water),
///    compute basin centroid and basin pixel count.
/// 6. For each water pixel:
///    - Find which basin its region belongs to.
///    - Compute tangent swirl vector around that basin centroid,
///      clockwise in south hemi, counterclockwise in north.
///    - swirlWeight = coastalFactor * basinFactor * globalCoastalGyreStrength
///      where coastalFactor is stronger near shore,
///      basinFactor is larger for larger basins.
///    - finalWind = Lerp(originalWind, swirlVec, swirlWeight)
///
/// Land pixels keep original wind unchanged.
///
/// Outputs:
/// - refinedWindVector[y,x]
/// - refinedWindSpeed01[y,x]
/// - refinedPreviewTexture (debug visualization R=X,G=Y,B=speed)
/// </summary>
[ExecuteAlways]
public class CoastalGyreWindRefinerComponent : MonoBehaviour
{
    [Header("Sources (must be generated first)")]
    [Tooltip("Wind after global patterns + terrain blocking.")]
    public WindMapGeneratorComponent windSource;

    [Tooltip("VoronoiComponent that produced regionMap[x,y]. Resolution must match windSource.")]
    public VoronoiComponent voronoiSource;

    [Tooltip("PlateAssignerComponent that marked each region with PlateType (Oceanic / Continental / etc.).")]
    public PlateAssignerComponent plateSource;

    [Header("Terrain Water Mask")]
    [Tooltip("Terrain used to evaluate actual seaLevel water coverage and shoreline distance falloff.")]
    public Terrain terrain;

    [Tooltip("Anything below this normalized height is considered water for the swirl and coastal falloff.")]
    [Range(0f, 1f)]
    public float seaLevel = 0.3f;

    [Header("Basin Merge Rules")]
    [Tooltip("Minimum shared border length (in pixels) for two adjacent oceanic regions to be merged into the same basin. " +
             "Prevents long snake-oceans from collapsing into one mega-basin through 1px bottlenecks.")]
    public int minSharedBorderForMerge = 50;

    [Header("Gyre Behaviour")]
    [Tooltip("How far from the shore (in pixels) the swirl influence stays strong before fading. Bigger => gyre reaches deeper offshore.")]
    [Range(1, 256)]
    public int coastalInfluenceRangePx = 64;

    [Tooltip("Global multiplier for how strongly the swirl overrides the base wind (0 = ignore swirl, 1 = swirl dominates).")]
    [Range(0f, 1f)]
    public float globalCoastalGyreStrength = 0.5f;

    [Tooltip("Skip tiny basins. Basins with fewer water pixels than this won't generate a swirl center.")]
    public int minBasinPixelCount = 200;

    [Tooltip("Basin pixel count at which basinFactor reaches 1. Larger basins push wind direction more confidently.")]
    public int bigBasinPixelCount = 20000;

    [Header("Preview (read-only)")]
    [Tooltip("R = X vel, G = Y vel, B = speed magnitude AFTER gyre refinement.")]
    public Texture2D refinedPreviewTexture;

    [HideInInspector] public Vector2[,] refinedWindVector;   // [y,x]
    [HideInInspector] public float[,] refinedWindSpeed01;  // [y,x], 0..1 magnitude

    // internal helper struct for Union-Find
    private class UnionFind
    {
        private Dictionary<int, int> parent = new Dictionary<int, int>();

        public int Find(int x)
        {
            if (!parent.ContainsKey(x))
            {
                parent[x] = x;
                return x;
            }
            if (parent[x] == x) return x;
            parent[x] = Find(parent[x]);
            return parent[x];
        }

        public void Union(int a, int b)
        {
            int pa = Find(a);
            int pb = Find(b);
            if (pa == pb) return;
            parent[pb] = pa;
        }

        public Dictionary<int, int> Collapse()
        {
            // returns a mapping from each element's representative to itself's final rep
            // (in practice we just rely on Find() externally)
            var finalMap = new Dictionary<int, int>();
            var keys = new List<int>(parent.Keys);
            foreach (var k in keys)
            {
                finalMap[k] = Find(k);
            }
            return finalMap;
        }
    }

    public void GenerateCoastalGyreWind()
    {
        // --- validation ---
        if (windSource == null)
        {
            Debug.LogWarning("CoastalGyreWindRefiner: windSource missing.");
            return;
        }
        if (voronoiSource == null)
        {
            Debug.LogWarning("CoastalGyreWindRefiner: voronoiSource missing.");
            return;
        }
        if (plateSource == null)
        {
            Debug.LogWarning("CoastalGyreWindRefiner: plateSource missing.");
            return;
        }
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("CoastalGyreWindRefiner: terrain missing or invalid.");
            return;
        }

        // pull upstream wind
        Vector2[,] baseWindField = windSource.windVector;
        float[,] baseWindSpeed = windSource.windSpeed01;
        if (baseWindField == null)
        {
            Debug.LogWarning("CoastalGyreWindRefiner: windSource.windVector is null. " +
                             "Run GenerateWindMap() first.");
            return;
        }

        // pull voronoi data
        var voroData = voronoiSource.GetLastResult();
        if (voroData.regionMap == null)
        {
            Debug.LogWarning("CoastalGyreWindRefiner: Voronoi data empty. " +
                             "Generate Voronoi first.");
            return;
        }

        // region -> PlateType (Oceanic, Continental, etc.)
        Dictionary<int, PlateAssignerComponent.PlateType> regionToPlate =
            plateSource.GetRegionPlateMap();
        if (regionToPlate == null || regionToPlate.Count == 0)
        {
            Debug.LogWarning("CoastalGyreWindRefiner: Plates not assigned. " +
                             "Run AssignPlatesNow() first.");
            return;
        }

        // dimensions
        int w = voronoiSource.width;
        int h = voronoiSource.height;

        // Check resolution match between wind and voronoi
        if (baseWindField.GetLength(0) != h ||
            baseWindField.GetLength(1) != w)
        {
            Debug.LogWarning("CoastalGyreWindRefiner: resolution mismatch. " +
                             "Wind map and Voronoi map must match.");
            return;
        }

        EnsureBuffers(w, h);

        // ======================================================
        // STEP 1. WATER MASK FROM TERRAIN HEIGHT
        // ======================================================
        // We'll check (height < seaLevel) at (u,v) where
        // u = x/(w-1), v = y/(h-1)
        TerrainData td = terrain.terrainData;
        int hmRes = td.heightmapResolution;
        float[,] heights01 = td.GetHeights(0, 0, hmRes, hmRes);

        bool[,] isWater = new bool[h, w];

        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1);
                float elev01 = SampleTerrainHeightBilinear(heights01, hmRes, u, v);
                isWater[y, x] = elev01 < seaLevel;
            }
        }

        // ======================================================
        // STEP 2. DISTANCE-TO-SHORE USING isWater
        // (shore = water pixel touching land in 8-neighborhood)
        // ======================================================
        int[,] distToShore = new int[h, w];
        for (int yy = 0; yy < h; yy++)
        {
            for (int xx = 0; xx < w; xx++)
                distToShore[yy, xx] = 999999;
        }

        Queue<Vector2Int> bfsQ = new Queue<Vector2Int>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!isWater[y, x]) continue;

                bool coast = false;
                for (int oy = -1; oy <= 1 && !coast; oy++)
                {
                    for (int ox = -1; ox <= 1 && !coast; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        int nx = x + ox;
                        int ny = y + oy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        if (!isWater[ny, nx]) coast = true;
                    }
                }

                if (coast)
                {
                    distToShore[y, x] = 0;
                    bfsQ.Enqueue(new Vector2Int(x, y));
                }
            }
        }

        Vector2Int[] dirs4 = new Vector2Int[]
        {
            new Vector2Int( 1, 0 ),
            new Vector2Int(-1, 0 ),
            new Vector2Int( 0, 1 ),
            new Vector2Int( 0,-1 ),
        };

        while (bfsQ.Count > 0)
        {
            var p = bfsQ.Dequeue();
            int cx = p.x;
            int cy = p.y;
            int cd = distToShore[cy, cx];

            for (int i = 0; i < dirs4.Length; i++)
            {
                int nx = cx + dirs4[i].x;
                int ny = cy + dirs4[i].y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (!isWater[ny, nx]) continue;

                int nd = cd + 1;
                if (nd < distToShore[ny, nx])
                {
                    distToShore[ny, nx] = nd;
                    bfsQ.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }

        // ======================================================
        // STEP 3. REGION DATA + BASIN CLUSTERING
        // We:
        // - Get regionMap[x,y]
        // - Mark which regions are OCEANIC based on PlateType.Oceanic
        // - Measure shared border lengths between adjacent oceanic regions
        //   (checking RIGHT / UP neighbours).
        // - Union any two oceanic regions if they share ≥ minSharedBorderForMerge pixels.
        // => regionToBasinId map
        // ======================================================
        int[,] regionMap = voroData.regionMap;

        // 3a. build a HashSet<int> of oceanic regions
        HashSet<int> oceanicRegions = new HashSet<int>();
        foreach (var kv in regionToPlate)
        {
            if (kv.Value == PlateAssignerComponent.PlateType.Oceanic)
                oceanicRegions.Add(kv.Key);
        }

        // 3b. count shared border length for each pair of touching oceanic regions
        // We'll store border lengths in a dictionary keyed by (min,max) region pair.
        Dictionary<(int, int), int> borderLen = new Dictionary<(int, int), int>();

        void AddBorder(int a, int b)
        {
            if (a == b) return;
            // Only care if both are oceanic
            if (!oceanicRegions.Contains(a) || !oceanicRegions.Contains(b))
                return;

            int r0 = (a < b) ? a : b;
            int r1 = (a < b) ? b : a;
            var key = (r0, r1);
            if (!borderLen.ContainsKey(key)) borderLen[key] = 0;
            borderLen[key]++;
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int rHere = regionMap[x, y];

                // Right neighbor
                if (x + 1 < w)
                {
                    int rRight = regionMap[x + 1, y];
                    if (rRight != rHere)
                    {
                        AddBorder(rHere, rRight);
                    }
                }

                // Up neighbor
                if (y + 1 < h)
                {
                    int rUp = regionMap[x, y + 1];
                    if (rUp != rHere)
                    {
                        AddBorder(rHere, rUp);
                    }
                }
            }
        }

        // 3c. Union-Find merge of regions into basins using borderLen threshold
        UnionFind uf = new UnionFind();

        // init union-find parents for all oceanic regions so Find() knows them
        foreach (int r in oceanicRegions)
        {
            uf.Find(r);
        }

        foreach (var kv in borderLen)
        {
            var pair = kv.Key;
            int len = kv.Value;
            if (len >= minSharedBorderForMerge)
            {
                uf.Union(pair.Item1, pair.Item2);
            }
        }

        // 3d. Build a stable basinId index
        // We'll compress union-find sets into 1..N basin IDs.
        Dictionary<int, int> rootToBasinId = new Dictionary<int, int>();
        Dictionary<int, int> regionToBasinId = new Dictionary<int, int>();
        int nextBasinId = 1;

        foreach (int regionId in oceanicRegions)
        {
            int root = uf.Find(regionId);
            if (!rootToBasinId.ContainsKey(root))
            {
                rootToBasinId[root] = nextBasinId;
                nextBasinId++;
            }
            regionToBasinId[regionId] = rootToBasinId[root];
        }

        int basinCount = nextBasinId - 1;

        // ======================================================
        // STEP 4. BASIN STATS (centroid and size IN WATER ONLY)
        // We'll only count pixels that are actually water by height<seaLevel.
        // That way basins respect real coastlines, not just tectonics.
        // ======================================================
        Vector2[] basinSum = new Vector2[basinCount + 1];
        int[] basinPixCount = new int[basinCount + 1];

        int[,] basinIdPx = new int[h, w]; // 0 = not water / not oceanic

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!isWater[y, x]) continue;

                int rId = regionMap[x, y];
                int bId;
                if (!regionToBasinId.TryGetValue(rId, out bId))
                {
                    // Region is not an oceanic basin (either land plate or tiny lagoon
                    // that didn't qualify as oceanic). We'll treat as no-basin water.
                    continue;
                }

                basinIdPx[y, x] = bId;
                basinSum[bId] += new Vector2(x, y);
                basinPixCount[bId]++;
            }
        }

        Vector2[] basinCentroid = new Vector2[basinCount + 1];
        for (int b = 1; b <= basinCount; b++)
        {
            if (basinPixCount[b] > 0)
                basinCentroid[b] = basinSum[b] / (float)basinPixCount[b];
            else
                basinCentroid[b] = Vector2.zero;
        }

        // ======================================================
        // STEP 5. FINAL WIND FIELD
        // For each pixel:
        // - If land (not isWater), copy original wind.
        // - Else compute swirlWeight and blend with swirl vector.
        //   swirl vector orbits basin centroid:
        //      tangent = perpendicular(radial)
        //      spin clockwise in south hemi (y < h*0.5), CCW in north.
        //
        // swirlWeight = coastalFactor * basinFactor * globalCoastalGyreStrength
        //   coastalFactor  -> near real shoreline (from distToShore)
        //   basinFactor    -> bigger basins push harder
        // ======================================================
        Color[] previewPixels = new Color[w * h];
        float baseWindStrength = windSource.baseWindStrength;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 origWind = baseWindField[y, x];

                // Land: leave unchanged
                if (!isWater[y, x])
                {
                    refinedWindVector[y, x] = origWind;
                    float spdLand = origWind.magnitude;
                    refinedWindSpeed01[y, x] = Mathf.Clamp01(spdLand);

                    float rL = 0.5f + origWind.x * 0.5f;
                    float gL = 0.5f + origWind.y * 0.5f;
                    float bL = Mathf.Clamp01(spdLand);
                    previewPixels[y * w + x] = new Color(rL, gL, bL, 1f);
                    continue;
                }

                int bIdHere = basinIdPx[y, x];
                if (bIdHere <= 0 || bIdHere > basinCount)
                {
                    // Water with no basin (e.g. shallow gulf that didn't classify):
                    // Just copy original wind.
                    refinedWindVector[y, x] = origWind;
                    float spd0 = origWind.magnitude;
                    refinedWindSpeed01[y, x] = Mathf.Clamp01(spd0);

                    float r0 = 0.5f + origWind.x * 0.5f;
                    float g0 = 0.5f + origWind.y * 0.5f;
                    float b0 = Mathf.Clamp01(spd0);
                    previewPixels[y * w + x] = new Color(r0, g0, b0, 1f);
                    continue;
                }

                int basinSize = basinPixCount[bIdHere];
                if (basinSize < minBasinPixelCount)
                {
                    // Too tiny / enclosed -> no stable gyre override
                    refinedWindVector[y, x] = origWind;
                    float spd1 = origWind.magnitude;
                    refinedWindSpeed01[y, x] = Mathf.Clamp01(spd1);

                    float r1 = 0.5f + origWind.x * 0.5f;
                    float g1 = 0.5f + origWind.y * 0.5f;
                    float b1 = Mathf.Clamp01(spd1);
                    previewPixels[y * w + x] = new Color(r1, g1, b1, 1f);
                    continue;
                }

                Vector2 c = basinCentroid[bIdHere];
                Vector2 radial = new Vector2(x, y) - c;
                if (radial.sqrMagnitude < 0.0001f)
                {
                    // basically at centroid, swirling is undefined so fallback
                    refinedWindVector[y, x] = origWind;
                    float spd2 = origWind.magnitude;
                    refinedWindSpeed01[y, x] = Mathf.Clamp01(spd2);

                    float r2 = 0.5f + origWind.x * 0.5f;
                    float g2 = 0.5f + origWind.y * 0.5f;
                    float b2 = Mathf.Clamp01(spd2);
                    previewPixels[y * w + x] = new Color(r2, g2, b2, 1f);
                    continue;
                }

                // Hemisphere spin rule:
                // y=0 = south pole, y=h-1 = north pole in your latitude math,
                // so for y < h*0.5 (south hemi) spin clockwise,
                // for y > h*0.5 (north hemi) spin counterclockwise.
                float spinDir = (y > h * 0.5f) ? -1f : 1f;

                Vector2 tangent;
                if (spinDir > 0f)
                {
                    // clockwise
                    tangent = new Vector2(radial.y, -radial.x);
                }
                else
                {
                    // counterclockwise
                    tangent = new Vector2(-radial.y, radial.x);
                }
                tangent.Normalize();

                // distance-based coastal falloff using real shoreline
                int dShorePx = distToShore[y, x];
                float coastalFactor = 1f - (dShorePx / (float)coastalInfluenceRangePx);
                if (coastalFactor < 0f) coastalFactor = 0f;

                // basin size factor
                float basinFactor = Mathf.Clamp01(basinSize / (float)bigBasinPixelCount);

                // final swirl weight
                float swirlWeight = coastalFactor * basinFactor * globalCoastalGyreStrength;
                swirlWeight = Mathf.Clamp01(swirlWeight);

                // swirl vector: tangent scaled by some baseline magnitude
                Vector2 swirlVec = tangent * baseWindStrength;

                Vector2 blended = Vector2.Lerp(origWind, swirlVec, swirlWeight);

                refinedWindVector[y, x] = blended;

                float spd = blended.magnitude;
                refinedWindSpeed01[y, x] = Mathf.Clamp01(spd);

                float r = 0.5f + blended.x * 0.5f;
                float g = 0.5f + blended.y * 0.5f;
                float b = Mathf.Clamp01(spd);
                previewPixels[y * w + x] = new Color(r, g, b, 1f);
            }
        }

        // ======================================================
        // STEP 6. Bake preview texture
        // ======================================================
        if (refinedPreviewTexture == null ||
            refinedPreviewTexture.width != w ||
            refinedPreviewTexture.height != h)
        {
            refinedPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            refinedPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            refinedPreviewTexture.filterMode = FilterMode.Point;
        }

        refinedPreviewTexture.SetPixels(previewPixels);
        refinedPreviewTexture.Apply();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        Debug.Log($"CoastalGyreWindRefiner: basins={basinCount}, refined wind ready.");
    }

    // ----------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------
    void EnsureBuffers(int w, int h)
    {
        if (refinedWindVector == null ||
            refinedWindVector.GetLength(0) != h ||
            refinedWindVector.GetLength(1) != w)
        {
            refinedWindVector = new Vector2[h, w];
        }

        if (refinedWindSpeed01 == null ||
            refinedWindSpeed01.GetLength(0) != h ||
            refinedWindSpeed01.GetLength(1) != w)
        {
            refinedWindSpeed01 = new float[h, w];
        }
    }

    float SampleTerrainHeightBilinear(float[,] heights, int hmRes, float u, float v)
    {
        if (u < 0f) u = 0f; else if (u > 1f) u = 1f;
        if (v < 0f) v = 0f; else if (v > 1f) v = 1f;

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
        return Mathf.Lerp(h0mix, h1mix, fy);
    }
}
