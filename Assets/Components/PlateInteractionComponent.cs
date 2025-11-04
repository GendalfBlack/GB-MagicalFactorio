using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class PlateInteractionComponent : MonoBehaviour
{
    [Header("Sources (auto)")]
    [Tooltip("We expect VoronoiComponent on the same GameObject")]
    public VoronoiComponent voronoiSource;

    [Tooltip("We expect FlowFieldComponent on the same GameObject")]
    public PlateTectonicsComponent flowSource;

    [Tooltip("We expect PlateAssignerComponent on the same GameObject")]
    public PlateAssignerComponent plateSource;

    [Tooltip("We expect PlateEdgeDetectorComponent on the same GameObject")]
    public PlateEdgeDetectorComponent edgeSource;

    [Header("Debug Colors")]
    public Color convergentColor = Color.red;        // plates pushing into each other
    public Color divergentColor = Color.blue;        // plates pulling apart / spreading
    public Color transformColor = Color.yellow;      // plates sliding along each other
    public Color unknownColor = Color.magenta;       // fallback

    [Range(1, 4)]
    public int edgeThickness = 1;

    [Header("Output (read-only)")]
    public Texture2D interactionPreviewTexture;

    [Tooltip("Per unique boundary we store regions, plates, and interaction type")]
    public List<InteractionEdgeInfo> interactionsDebug = new List<InteractionEdgeInfo>();

    // --- Types ---

    public enum InteractionType
    {
        Unknown,
        Convergent,   // towards each other (collision/subduction style)
        Divergent,    // away / opening rift / spreading ridge
        Transform     // mostly same direction (strike-slip style)
    }

    [Serializable]
    public struct InteractionEdgeInfo
    {
        public int regionA;
        public int regionB;

        public PlateAssignerComponent.PlateType plateA;
        public PlateAssignerComponent.PlateType plateB;

        public Vector2 dirA;
        public Vector2 dirB;

        public InteractionType interaction;
    }

    // cached for CreateVoronoiMap()
    private Dictionary<int, Vector2> regionDirCache = new Dictionary<int, Vector2>();

    public InteractionType InferInteractionPublic(int regionA, int regionB)
    {
        return InferInteraction(regionA, regionB);
    }

    /// <summary>
    /// Call this from the inspector button.
    /// </summary>
    public void GenerateNow()
    {
        // auto-wire
        if (voronoiSource == null)
            voronoiSource = GetComponent<VoronoiComponent>();
        if (flowSource == null)
            flowSource = GetComponent<PlateTectonicsComponent>();
        if (plateSource == null)
            plateSource = GetComponent<PlateAssignerComponent>();
        if (edgeSource == null)
            edgeSource = GetComponent<PlateEdgeDetectorComponent>();

        if (voronoiSource == null)
        {
            Debug.LogWarning("PlateInteractionComponent: Missing VoronoiComponent.");
            return;
        }
        if (flowSource == null)
        {
            Debug.LogWarning("PlateInteractionComponent: Missing FlowFieldComponent.");
            return;
        }
        if (plateSource == null)
        {
            Debug.LogWarning("PlateInteractionComponent: Missing PlateAssignerComponent.");
            return;
        }
        if (edgeSource == null)
        {
            Debug.LogWarning("PlateInteractionComponent: Missing PlateEdgeDetectorComponent.");
            return;
        }

        var voroData = voronoiSource.GetLastResult();
        if (voroData.regionMap == null)
        {
            Debug.LogWarning("FlowField: Voronoi data is empty. Generating Voronoi.");
            voronoiSource.CreateVoronoiMap();
            voroData = voronoiSource.GetLastResult();
        }

        // We'll ask FlowField for average motion per region.
        // You just need PlateTectonicsComponent to expose something like:
        // public Dictionary<int, Vector2> GetRegionFlowDirections()
        // where the Vector2 is the normalized average direction of that region's flow.
        regionDirCache = GetRegionFlowDirectionsSafe(flowSource);

        int w = voronoiSource.width;
        int h = voronoiSource.height;
        int[,] regionMap = voroData.regionMap;

        // We'll build 4 masks (per pixel):
        bool[] convergentMask = new bool[w * h];
        bool[] divergentMask = new bool[w * h];
        bool[] transformMask = new bool[w * h];
        bool[] unknownMask = new bool[w * h];

        // We'll also gather unique pairs like before
        var pairDict = new Dictionary<long, InteractionEdgeInfo>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int rHere = regionMap[x, y];

                // check RIGHT neighbor
                if (x + 1 < w)
                {
                    int rRight = regionMap[x + 1, y];
                    if (rRight != rHere)
                    {
                        ClassifyEdgePixel(
                            x, y,
                            rHere, rRight,
                            w, h,
                            convergentMask,
                            divergentMask,
                            transformMask,
                            unknownMask
                        );

                        RegisterPair(rHere, rRight, pairDict);
                    }
                }

                // check UP neighbor
                if (y + 1 < h)
                {
                    int rUp = regionMap[x, y + 1];
                    if (rUp != rHere)
                    {
                        ClassifyEdgePixel(
                            x, y,
                            rHere, rUp,
                            w, h,
                            convergentMask,
                            divergentMask,
                            transformMask,
                            unknownMask
                        );

                        RegisterPair(rHere, rUp, pairDict);
                    }
                }
            }
        }

        // Build texture with color-coded interaction types
        interactionPreviewTexture = BuildInteractionTexture(
            w,
            h,
            convergentMask,
            divergentMask,
            transformMask,
            unknownMask,
            convergentColor,
            divergentColor,
            transformColor,
            unknownColor,
            edgeThickness
        );
        interactionPreviewTexture.name = "PlateInteractionsPreview";

        // Push final debug list in inspector-friendly form
        interactionsDebug.Clear();
        foreach (var kvp in pairDict)
        {
            interactionsDebug.Add(kvp.Value);
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (interactionPreviewTexture != null) interactionPreviewTexture.Apply();
#endif

        Debug.Log($"PlateInteractionComponent: stamped {interactionsDebug.Count} unique interactions.");
    }

    // -------------------------------------------------
    // Core logic per boundary pixel
    // -------------------------------------------------
    private void ClassifyEdgePixel(
        int x, int y,
        int regionA,
        int regionB,
        int w,
        int h,
        bool[] convergentMask,
        bool[] divergentMask,
        bool[] transformMask,
        bool[] unknownMask
    )
    {
        var ia = InferInteraction(regionA, regionB);
        int idx = y * w + x;
        switch (ia)
        {
            case InteractionType.Convergent:
                convergentMask[idx] = true;
                break;
            case InteractionType.Divergent:
                divergentMask[idx] = true;
                break;
            case InteractionType.Transform:
                transformMask[idx] = true;
                break;
            default:
                unknownMask[idx] = true;
                break;
        }
    }

    // -------------------------------------------------
    // Deduplicate boundaries and store their info
    // -------------------------------------------------
    private void RegisterPair(int rawA, int rawB, Dictionary<long, InteractionEdgeInfo> dict)
    {
        int a = rawA < rawB ? rawA : rawB;
        int b = rawA < rawB ? rawB : rawA;

        long key = ((long)a << 32) ^ (uint)b;
        if (dict.ContainsKey(key))
            return;

        var plateMap = plateSource.GetRegionPlateMap(); // assumes PlateAssignerComponent exposes this like before

        plateMap.TryGetValue(a, out var plateA);
        plateMap.TryGetValue(b, out var plateB);

        Vector2 dirA = regionDirCache.TryGetValue(a, out var da) ? da : Vector2.zero;
        Vector2 dirB = regionDirCache.TryGetValue(b, out var db) ? db : Vector2.zero;

        InteractionEdgeInfo info = new InteractionEdgeInfo
        {
            regionA = a,
            regionB = b,
            plateA = plateA,
            plateB = plateB,
            dirA = da,
            dirB = db,
            interaction = InferInteraction(a, b)
        };

        dict[key] = info;
    }

    // -------------------------------------------------
    // Interaction classification
    // -------------------------------------------------
    private InteractionType InferInteraction(int regionA, int regionB)
    {
        Vector2 aDir = regionDirCache.TryGetValue(regionA, out var ad) ? ad : Vector2.zero;
        Vector2 bDir = regionDirCache.TryGetValue(regionB, out var bd) ? bd : Vector2.zero;

        // if we have no motion data at all
        if (aDir.sqrMagnitude < 0.0001f && bDir.sqrMagnitude < 0.0001f)
            return InteractionType.Unknown;

        // normalize
        aDir = aDir.sqrMagnitude > 0.0001f ? aDir.normalized : aDir;
        bDir = bDir.sqrMagnitude > 0.0001f ? bDir.normalized : bDir;

        // dot product tells us relative heading:
        //  1.0  -> same direction
        //  0.0  -> perpendicular
        // -1.0  -> opposite directions
        float d = Vector2.Dot(aDir, bDir);

        // Heuristic:
        // strongly opposite => Convergent (push against)
        // strongly same     => Transform (slide along)
        // else              => Divergent (opening / shearing apart-ish)
        // tweak thresholds if needed
        if (d < -0.5f)
            return InteractionType.Convergent;
        if (d > 0.5f)
            return InteractionType.Transform;

        return InteractionType.Divergent;
    }

    // -------------------------------------------------
    // Texture building
    // -------------------------------------------------
    private Texture2D BuildInteractionTexture(
        int w,
        int h,
        bool[] convergentMask,
        bool[] divergentMask,
        bool[] transformMask,
        bool[] unknownMask,
        Color convergentCol,
        Color divergentCol,
        Color transformCol,
        Color unknownCol,
        int thickness
    )
    {
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(0f, 0f, 0f, 0f); // transparent bg

        void StampPixel(int px, int py, Color col)
        {
            if (px < 0 || px >= w || py < 0 || py >= h) return;
            int pidx = py * w + px;
            pixels[pidx] = col;
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;

                if (!convergentMask[idx] &&
                    !divergentMask[idx] &&
                    !transformMask[idx] &&
                    !unknownMask[idx])
                    continue;

                Color c =
                    convergentMask[idx] ? convergentCol :
                    divergentMask[idx] ? divergentCol :
                    transformMask[idx] ? transformCol :
                    unknownCol;

                for (int oy = -thickness; oy <= thickness; oy++)
                    for (int ox = -thickness; ox <= thickness; ox++)
                        StampPixel(x + ox, y + oy, c);
            }
        }

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // -------------------------------------------------
    // Safe pull of region directions from flowSource
    // -------------------------------------------------
    private Dictionary<int, Vector2> GetRegionFlowDirectionsSafe(PlateTectonicsComponent flow)
    {
        // EXPECTED API IN PlateTectonicsComponent:
        // public Dictionary<int, Vector2> GetRegionFlowDirections()
        // If it doesn't exist yet, you'll add it there.
        // For now we try to call it via a helper method pattern.

        try
        {
            return flow.GetRegionFlowDirections();
        }
        catch (System.Exception)
        {
            Debug.LogWarning("PlateInteractionComponent: FlowFieldComponent.GetRegionFlowDirections() not implemented yet. Returning zero vectors.");
            return new Dictionary<int, Vector2>();
        }
    }

    // Public getters for others if needed
    public Texture2D GetInteractionTexture()
    {
        return interactionPreviewTexture;
    }

    public List<InteractionEdgeInfo> GetInteractionsDebug()
    {
        return interactionsDebug;
    }
}
