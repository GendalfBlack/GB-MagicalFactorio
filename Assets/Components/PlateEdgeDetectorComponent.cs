using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class PlateEdgeDetectorComponent : MonoBehaviour
{
    [Header("Sources (auto)")]
    [Tooltip("Expect VoronoiComponent on the same GameObject")]
    public VoronoiComponent voronoiSource;

    [Tooltip("Expect PlateAssignerComponent on the same GameObject")]
    public PlateAssignerComponent plateSource;

    [Header("Debug Settings")]
    [Tooltip("Color for edges where BOTH sides are the same plate type")]
    public Color samePlateEdgeColor = Color.white;

    [Tooltip("Color for edges where plate types DIFFER (actual tectonic boundary)")]
    public Color diffPlateEdgeColor = Color.red;

    [Range(1, 4)]
    [Tooltip("Thickness in pixels when drawing edges to preview texture")]
    public int edgeThickness = 1;

    [Header("Output (read-only)")]
    public Texture2D edgePreviewTexture;

    [Tooltip("List of unique neighbour plate pairs we detected")]
    public List<PlateEdgeInfo> edgesDebug = new List<PlateEdgeInfo>();

    // INTERNAL
    // We’ll build this once per CreateVoronoiMap so other systems can query.
    private Dictionary<int, PlateAssignerComponent.PlateType> regionToPlateCached =
        new Dictionary<int, PlateAssignerComponent.PlateType>();

    // This describes one plate boundary relationship (unique per pair of regions)
    [Serializable]
    public struct PlateEdgeInfo
    {
        public int regionA;
        public int regionB;

        public PlateAssignerComponent.PlateType plateA;
        public PlateAssignerComponent.PlateType plateB;

        // convenience
        public bool IsDifferentPlateType => plateA != plateB;
    }

    /// <summary>
    /// Call this from the custom inspector button.
    /// </summary>
    public void GenerateNow()
    {
        // auto-wire
        if (voronoiSource == null)
            voronoiSource = GetComponent<VoronoiComponent>();
        if (plateSource == null)
            plateSource = GetComponent<PlateAssignerComponent>();

        // sanity
        if (voronoiSource == null)
        {
            Debug.LogWarning("PlateEdgeDetector: No VoronoiComponent found.");
            return;
        }
        if (plateSource == null)
        {
            Debug.LogWarning("PlateEdgeDetector: No PlateAssignerComponent found.");
            return;
        }

        var voroData = voronoiSource.GetLastResult();
        if (voroData.regionMap == null)
        {
            Debug.LogWarning("FlowField: Voronoi data is empty. Generating Voronoi.");
            voronoiSource.CreateVoronoiMap();
            voroData = voronoiSource.GetLastResult();
        }

        // PlateAssignerComponent must have assigned plates already
        var plateMap = plateSource.GetRegionPlateMap();
        if (plateMap == null || plateMap.Count == 0)
        {
            Debug.LogWarning("PlateEdgeDetector: Plate data is empty. Generating plate data.");
            plateSource.AssignPlatesNow();
            plateMap = plateSource.GetRegionPlateMap();
        }

        int w = voronoiSource.width;
        int h = voronoiSource.height;
        int[,] regionMap = voroData.regionMap;

        // Cache region -> plateType for fast access
        regionToPlateCached.Clear();
        foreach (var kvp in plateMap)
            regionToPlateCached[kvp.Key] = kvp.Value;

        // We’ll build:
        // - edge mask pixels with info whether it's same-plate or diff-plate
        // - adjacency list of unique boundaries
        var sameMask = new bool[w * h];
        var diffMask = new bool[w * h];

        // We'll store edges in HashSet to avoid duplicates.
        // We'll encode unordered pair (minRegionId, maxRegionId) as long key.
        var uniquePairs = new Dictionary<long, PlateEdgeInfo>();

        // Walk through pixels. Compare to RIGHT and UP only (avoids duplicates).
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int regionHere = regionMap[x, y];

                // Right neighbor
                if (x + 1 < w)
                {
                    int regionRight = regionMap[x + 1, y];
                    if (regionRight != regionHere)
                    {
                        MarkEdgePixel(sameMask, diffMask, w, h, x, y, regionHere, regionRight);
                        RegisterPair(uniquePairs, regionHere, regionRight);
                    }
                }

                // Up neighbor
                if (y + 1 < h)
                {
                    int regionUp = regionMap[x, y + 1];
                    if (regionUp != regionHere)
                    {
                        MarkEdgePixel(sameMask, diffMask, w, h, x, y, regionHere, regionUp);
                        RegisterPair(uniquePairs, regionHere, regionUp);
                    }
                }
            }
        }

        // Convert mask(s) to texture
        edgePreviewTexture = BuildEdgeTexture(
            sameMask,
            diffMask,
            w,
            h,
            samePlateEdgeColor,
            diffPlateEdgeColor,
            edgeThickness
        );
        edgePreviewTexture.name = "PlateEdgesPreview";

        // Fill debug list
        edgesDebug.Clear();
        foreach (var kvp in uniquePairs)
        {
            edgesDebug.Add(kvp.Value);
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (edgePreviewTexture != null) edgePreviewTexture.Apply();
#endif

        Debug.Log($"PlateEdgeDetector: generated {edgesDebug.Count} unique plate boundaries.");
    }

    /// <summary>
    /// Marks the pixel as boundary. We decide if it's samePlate or diffPlate.
    /// </summary>
    private void MarkEdgePixel(
        bool[] sameMask,
        bool[] diffMask,
        int w,
        int h,
        int x,
        int y,
        int regionA,
        int regionB)
    {
        // plate types
        var plateA = GetPlateOfRegion(regionA);
        var plateB = GetPlateOfRegion(regionB);

        int idx = y * w + x;

        if (plateA == plateB)
        {
            sameMask[idx] = true;
        }
        else
        {
            diffMask[idx] = true;
        }

        // We could also expand thickness here,
        // but we'll apply thickness in texture build stage instead
    }

    /// <summary>
    /// Registers an unordered pair of touching regions into the uniquePairs dict.
    /// We also cache their plate types.
    /// </summary>
    private void RegisterPair(Dictionary<long, PlateEdgeInfo> pairs, int regionA, int regionB)
    {
        // normalized order so A<B, purely to deduplicate
        int a = regionA < regionB ? regionA : regionB;
        int b = regionA < regionB ? regionB : regionA;

        long key = ((long)a << 32) ^ (uint)b;
        if (pairs.ContainsKey(key))
            return;

        var plateA = GetPlateOfRegion(a);
        var plateB = GetPlateOfRegion(b);

        PlateEdgeInfo info = new PlateEdgeInfo
        {
            regionA = a,
            regionB = b,
            plateA = plateA,
            plateB = plateB
        };

        pairs[key] = info;
    }

    private PlateAssignerComponent.PlateType GetPlateOfRegion(int regionId)
    {
        if (regionToPlateCached.TryGetValue(regionId, out var t))
            return t;
        // fallback - Continental as neutral default
        return PlateAssignerComponent.PlateType.Continental;
    }

    /// <summary>
    /// Builds a texture where:
    ///  - transparent background
    ///  - same-plate edges drawn with samePlateEdgeColor
    ///  - different-plate edges drawn with diffPlateEdgeColor (tectonic boundary)
    /// </summary>
    Texture2D BuildEdgeTexture(
        bool[] sameMask,
        bool[] diffMask,
        int w,
        int h,
        Color sameCol,
        Color diffCol,
        int thick
    )
    {
        // Start transparent
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(0, 0, 0, 0);

        // Paint pixels. We'll "stamp" a small square for thickness.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;

                if (!sameMask[idx] && !diffMask[idx])
                    continue;

                Color c = sameMask[idx] ? sameCol : diffCol;

                for (int oy = -thick; oy <= thick; oy++)
                {
                    for (int ox = -thick; ox <= thick; ox++)
                    {
                        int px = x + ox;
                        int py = y + oy;
                        if (px < 0 || px >= w || py < 0 || py >= h) continue;

                        int pidx = py * w + px;
                        pixels[pidx] = c;
                    }
                }
            }
        }

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // --- Public helpers for later systems ---

    /// <summary>
    /// Returns all unique region-to-region contacts with plate info.
    /// </summary>
    public List<PlateEdgeInfo> GetEdgesDebug()
    {
        return edgesDebug;
    }

    /// <summary>
    /// Just in case something else wants to ask quickly.
    /// </summary>
    public Texture2D GetEdgeTexture()
    {
        return edgePreviewTexture;
    }
}
