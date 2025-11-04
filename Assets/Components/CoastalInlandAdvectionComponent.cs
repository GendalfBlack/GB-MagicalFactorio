using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Step X+1: Inland Coastal Advection
/// ---------------------------------
/// Takes refined coastal/gyre wind (ocean gyres) and bleeds that flow inland,
/// gradually weakening it with distance and stopping when mountains block it.
/// Result: smooth coastal influence instead of a hard shore cutoff,
/// great for humidity/temperature transport.
///
/// Pipeline logic:
/// 1. Classify water/land using terrain + seaLevel.
/// 2. Find coastal LAND cells (land pixel touching water pixel).
///    For each such land pixel, grab the neighbor WATER wind from
///    CoastalGyreWindRefinerComponent as an injected "coastal wind".
/// 3. Multi-source BFS flood fill over LAND ONLY:
///    - Propagate that injected wind inland.
///    - Each step decays remaining strength.
///    - If elevation >= mountainHeightThreshold, apply mountainBlockStrength
///      (can kill or weaken propagation).
///    - Stop if strength <= cutoff or maxRange reached.
///    Accumulate all incoming contributions per pixel (vector+weight).
/// 4. For each LAND pixel, blend original land wind from the refiner
///    with this coastal-advection result. Oceans stay as-is.
/// </summary>
[ExecuteAlways]
public class CoastalInlandAdvectionComponent : MonoBehaviour
{
    [Header("Upstream Sources")]
    [Tooltip("Output of CoastalGyreWindRefinerComponent (must already be generated).")]
    public CoastalGyreWindRefinerComponent coastalGyreSource;

    [Tooltip("Same Terrain you use everywhere else.")]
    public Terrain terrain;

    [Tooltip("Anything below this normalized height is considered water.")]
    [Range(0f, 1f)]
    public float seaLevel = 0.5f;

    [Header("Propagation Rules")]
    [Tooltip("Heights above this are considered mountains that strongly block inland flow.")]
    [Range(0f, 1f)]
    public float mountainHeightThreshold = 0.6f;

    [Tooltip("0 = mountains don't really block; 1 = mountains kill propagation immediately.")]
    [Range(0f, 1f)]
    public float mountainBlockStrength = 0.8f;

    [Tooltip("How far inland (in pixels) coastal wind can travel before we stop spreading.")]
    [Range(1, 512)]
    public int maxInlandRangePx = 96;

    [Tooltip("Per-step decay of propagated strength [0..1]. Higher means it dies faster each pixel.")]
    [Range(0f, 1f)]
    public float decayPerStep = 0.05f;

    [Tooltip("Initial multiplier for the coastal wind injected at shore.")]
    [Range(0f, 2f)]
    public float coastInjectStrength = 1.0f;

    [Tooltip("Final blend strength on land (0 = ignore bleed, 1 = fully apply bleed where strong).")]
    [Range(0f, 1f)]
    public float finalBlendStrength = 0.6f;

    [Header("Preview (read-only)")]
    [Tooltip("R = X vel, G = Y vel, B = speed AFTER inland advection")]
    public Texture2D inlandWindPreviewTexture;

    [HideInInspector] public Vector2[,] inlandWindVector;   // final blended result [y,x]
    [HideInInspector] public float[,] inlandWindSpeed01;    // magnitude 0..1

    struct PropNode
    {
        public int x;
        public int y;
        public Vector2 wind;
        public float strength;
        public int dist;
    }

    public void GenerateInlandAdvection()
    {
        // --- validation ---
        if (coastalGyreSource == null)
        {
            Debug.LogWarning("CoastalInlandAdvection: coastalGyreSource missing.");
            return;
        }
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("CoastalInlandAdvection: terrain missing or invalid.");
            return;
        }

        var gyreVec = coastalGyreSource.refinedWindVector;
        var gyreSpd = coastalGyreSource.refinedWindSpeed01;
        if (gyreVec == null || gyreSpd == null)
        {
            Debug.LogWarning("CoastalInlandAdvection: gyre data not ready. Run GenerateCoastalGyreWind() first.");
            return;
        }

        int h = gyreVec.GetLength(0);
        int w = gyreVec.GetLength(1);

        EnsureBuffers(w, h);

        TerrainData td = terrain.terrainData;
        int hmRes = td.heightmapResolution;
        float[,] heights01 = td.GetHeights(0, 0, hmRes, hmRes);

        // classify water/land
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

        // coastal land pixels that touch water
        // for each such pixel we grab a representative coastal wind from neighbor water
        // and push into a BFS queue
        // We'll accumulate results as we propagate.
        Vector2[,] accumWind = new Vector2[h, w];
        float[,] accumWeight = new float[h, w];

        Queue<PropNode> q = new Queue<PropNode>();

        // helper to push initial seeds
        void EnqueueIfLandAndTouchWater(int lx, int ly)
        {
            if (lx < 0 || lx >= w || ly < 0 || ly >= h) return;
            if (isWater[ly, lx]) return; // only land

            // find best water neighbor to copy from
            bool foundWater = false;
            Vector2 shoreWind = Vector2.zero;
            float bestSpeed = -1f;

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0) continue;
                    int nx = lx + ox;
                    int ny = ly + oy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (!isWater[ny, nx]) continue;

                    // pick the strongest neighbor water wind as representative
                    float sp = gyreVec[ny, nx].magnitude;
                    if (sp > bestSpeed)
                    {
                        bestSpeed = sp;
                        shoreWind = gyreVec[ny, nx];
                        foundWater = true;
                    }
                }
            }

            if (!foundWater) return;

            // inject
            Vector2 injected = shoreWind * coastInjectStrength;
            float injectedStrength = injected.magnitude;
            if (injectedStrength <= 0f) return;

            var node = new PropNode
            {
                x = lx,
                y = ly,
                wind = injected,
                strength = injectedStrength,
                dist = 0
            };
            q.Enqueue(node);

            // record immediate contribution
            accumWind[ly, lx] += injected;
            accumWeight[ly, lx] += injectedStrength;
        }

        // find all coastline land cells
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (isWater[y, x]) continue; // land only

                bool touchesWater = false;
                for (int oy = -1; oy <= 1 && !touchesWater; oy++)
                {
                    for (int ox = -1; ox <= 1 && !touchesWater; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        int nx = x + ox;
                        int ny = y + oy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        if (isWater[ny, nx]) touchesWater = true;
                    }
                }

                if (touchesWater)
                {
                    EnqueueIfLandAndTouchWater(x, y);
                }
            }
        }

        // BFS propagate inland
        // We'll do 4-dir for stability
        Vector2Int[] dirs4 = new Vector2Int[]
        {
            new Vector2Int( 1, 0 ),
            new Vector2Int(-1, 0 ),
            new Vector2Int( 0, 1 ),
            new Vector2Int( 0,-1 ),
        };

        // we'll keep a visitedStrength map so we don't endlessly revisit weaker signals
        float[,] bestStrengthSeen = new float[h, w];
        for (int yy = 0; yy < h; yy++)
        {
            for (int xx = 0; xx < w; xx++)
            {
                bestStrengthSeen[yy, xx] = 0f;
            }
        }
        // initialize bestStrengthSeen with seeds we already enqueued
        foreach (var seedNode in q.ToArray())
        {
            bestStrengthSeen[seedNode.y, seedNode.x] = Mathf.Max(
                bestStrengthSeen[seedNode.y, seedNode.x],
                seedNode.strength
            );
        }

        while (q.Count > 0)
        {
            var n = q.Dequeue();

            if (n.dist >= maxInlandRangePx) continue;

            for (int di = 0; di < dirs4.Length; di++)
            {
                int nx = n.x + dirs4[di].x;
                int ny = n.y + dirs4[di].y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (isWater[ny, nx]) continue; // do NOT spread across water, only inland

                // check mountain blocking
                float u = (float)nx / (w - 1);
                float v = (float)ny / (h - 1);
                float elev01 = SampleTerrainHeightBilinear(heights01, hmRes, u, v);

                float block = 1f;
                if (elev01 >= mountainHeightThreshold)
                {
                    // mountains eat a fraction of the signal
                    block -= mountainBlockStrength;
                    if (block < 0f) block = 0f;
                }

                if (block <= 0f)
                {
                    // fully blocked: no further spread
                    continue;
                }

                // decay with step + block
                float nextStrength = n.strength * (1f - decayPerStep) * block;
                if (nextStrength <= 0.0001f) continue;

                // if we've already propagated here with >= this strength, skip
                if (nextStrength <= bestStrengthSeen[ny, nx]) continue;
                bestStrengthSeen[ny, nx] = nextStrength;

                // direction keeps the same vector orientation/magnitude ratio
                // scale original wind vector to new strength
                Vector2 dir;
                float mag = n.wind.magnitude;
                if (mag > 0f) dir = n.wind / mag;
                else dir = Vector2.zero;

                Vector2 newWindVec = dir * nextStrength;

                // accumulate contribution
                accumWind[ny, nx] += newWindVec;
                accumWeight[ny, nx] += nextStrength;

                // enqueue forward
                q.Enqueue(new PropNode
                {
                    x = nx,
                    y = ny,
                    wind = newWindVec,
                    strength = nextStrength,
                    dist = n.dist + 1
                });
            }
        }

        // Build final inlandWindVector / inlandWindSpeed01 + preview
        Color[] previewPixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 baseWind = gyreVec[y, x]; // already has gyre+global+terrain for ocean; land is still base

                if (isWater[y, x])
                {
                    // Ocean: keep as-is.
                    inlandWindVector[y, x] = baseWind;
                }
                else
                {
                    // Land: blend base with coastal-advection influence if any
                    float wSum = accumWeight[y, x];

                    if (wSum > 0f)
                    {
                        Vector2 advectWind = accumWind[y, x] / wSum; // weighted avg
                        // how hard we blend on this pixel:
                        // clamp so that insanely huge wSum doesn't turn into 1.5 etc.
                        float localBlend = Mathf.Clamp01(finalBlendStrength);

                        Vector2 blended = Vector2.Lerp(baseWind, advectWind, localBlend);
                        inlandWindVector[y, x] = blended;
                    }
                    else
                    {
                        inlandWindVector[y, x] = baseWind;
                    }
                }

                float spd = inlandWindVector[y, x].magnitude;
                inlandWindSpeed01[y, x] = Mathf.Clamp01(spd);

                float r = 0.5f + inlandWindVector[y, x].x * 0.5f;
                float g = 0.5f + inlandWindVector[y, x].y * 0.5f;
                float b = Mathf.Clamp01(spd);

                previewPixels[y * w + x] = new Color(r, g, b, 1f);
            }
        }

        // bake preview texture
        if (inlandWindPreviewTexture == null ||
            inlandWindPreviewTexture.width != w ||
            inlandWindPreviewTexture.height != h)
        {
            inlandWindPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            inlandWindPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            inlandWindPreviewTexture.filterMode = FilterMode.Point;
        }

        inlandWindPreviewTexture.SetPixels(previewPixels);
        inlandWindPreviewTexture.Apply();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        Debug.Log("CoastalInlandAdvection: inland wind field generated.");
    }

    void EnsureBuffers(int w, int h)
    {
        if (inlandWindVector == null ||
            inlandWindVector.GetLength(0) != h ||
            inlandWindVector.GetLength(1) != w)
        {
            inlandWindVector = new Vector2[h, w];
        }

        if (inlandWindSpeed01 == null ||
            inlandWindSpeed01.GetLength(0) != h ||
            inlandWindSpeed01.GetLength(1) != w)
        {
            inlandWindSpeed01 = new float[h, w];
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
