using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a debug-friendly flow field for plate tectonics based on the output of a
/// <see cref="VoronoiComponent"/> and an optional shared seed.
/// </summary>
/// <remarks>
/// The component builds a per-region flow direction that other systems (for example
/// <see cref="PlateInteractionComponent"/>) can query.  The flow directions are visualised via a
/// texture of arrows for editor inspection.
/// </remarks>
[ExecuteInEditMode]
public class PlateTectonicsComponent : MonoBehaviour
{
    #region Inspector Fields
    [Tooltip("WorldSeedComponent to use for shared seed.")]
    public WorldSeedComponent seedSource;

    [Header("Source (auto)")]
    [Tooltip("VoronoiComponent to base the flow field on.")]
    public VoronoiComponent voronoiSource;

    [Header("Flow Settings (visual only)")]
    [Range(1, 100)]
    public int arrowSizePixels = 20;

    [Range(1, 10)]
    public int arrowThickness = 1;

    [Header("Output (read-only)")]
    public Texture2D debugTexture;

    [System.Serializable]
    public struct RegionFlowData
    {
        /// <summary>
        /// The region identifier coming from the Voronoi diagram.
        /// </summary>
        public int regionId;

        /// <summary>
        /// The centroid of the region in pixel coordinates.
        /// </summary>
        public Vector2 center;

        /// <summary>
        /// The average flow direction of the region, expressed as a unit vector.
        /// </summary>
        public Vector2 direction;
    }

    private List<RegionFlowData> regionsFlowData = new List<RegionFlowData>();
    #endregion

    #region Public API
    /// <summary>
    /// Returns a dictionary mapping region identifiers to their computed flow directions.
    /// </summary>
    /// <remarks>
    /// If <see cref="CreateFlowDirectionsOnVoronoiMap"/> has not been executed yet, the dictionary will
    /// be empty.  The returned dictionary is a copy and may be modified by callers without affecting the
    /// component.
    /// </remarks>
    public Dictionary<int, Vector2> GetRegionFlowDirections()
    {
        var dict = new Dictionary<int, Vector2>();
        foreach (var rd in regionsFlowData)
        {
            dict[rd.regionId] = rd.direction;
        }
        return dict;
    }


    /// <summary>
    /// Generates flow vectors for each Voronoi region using the configured seed and settings.
    /// </summary>
    /// <remarks>
    /// This method ensures that the Voronoi data is present, generates a flow field via
    /// <see cref="FlowFieldData.GenerateFlowFieldFromVoronoi"/>, stores the results for later queries and
    /// creates a debug texture with arrow glyphs.
    /// </remarks>
    public void CreateFlowDirectionsOnVoronoiMap()
    {
        VoronoiGenerator.VoronoiResult voroData;
        int seedToUse;
        if (!CanGenerateFlow(out voroData, out seedToUse))
        { 
            return; 
        }

        // build flow vectors using shared seed
        var flowField = FlowFieldData.GenerateFlowFieldFromVoronoi(voroData, seedToUse);

        regionsFlowData.Clear();
        foreach (var kvp in flowField.flowPerRegion)
        {
            var info = kvp.Value;
            regionsFlowData.Add(new RegionFlowData
            {
                regionId = info.regionId,
                center = info.center,
                direction = info.direction
            });
        }

        debugTexture = BuildDebugTexture(flowField, arrowSizePixels, arrowThickness);
        debugTexture.name = "FlowFieldDebug";

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (debugTexture != null) debugTexture.Apply();
#endif

        Debug.Log($"FlowField: generated {regionsFlowData.Count} flow vectors using shared seed {seedToUse}.");
    }
    #endregion

    #region Private Helpers
    /// <summary>
    /// Validates the component state and acquires the necessary data to generate flow vectors.
    /// </summary>
    /// <param name="voroData">On success, receives the Voronoi data used to build the flow field.</param>
    /// <param name="seedToUse">On success, receives the seed that should be used for deterministic generation.</param>
    /// <returns><see langword="true"/> if generation can continue; otherwise <see langword="false"/>.</returns>
    private bool CanGenerateFlow(out VoronoiGenerator.VoronoiResult voroData, out int seedToUse)
    {
        if (voronoiSource == null)
        {
            voronoiSource = GetComponent<VoronoiComponent>();
        }

        if (seedSource == null)
        { 
            seedSource = GetComponent<WorldSeedComponent>(); 
        }

        if (voronoiSource == null)
        {
            Debug.LogWarning("FlowField: No VoronoiComponent found on this GameObject. But was expected.");
            voroData = default;
            seedToUse = 0;
            return false;
        }

        voroData = voronoiSource.GetLastResult();
        
        if (voroData.regionMap == null || voroData.seeds == null || voroData.seeds.Length == 0)
        {
            Debug.LogWarning("FlowField: Voronoi data is empty. Generating Voronoi.");
            voronoiSource.CreateVoronoiMap();
            voroData = voronoiSource.GetLastResult();
        }

        seedToUse = (seedSource != null) ? seedSource.worldSeed : 0;

        return true;
    }

    /// <summary>
    /// Builds a texture that visualises the flow field as colored Voronoi regions with arrows.
    /// </summary>
    /// <param name="flowField">The generated flow field data.</param>
    /// <param name="arrowLen">The arrow length in pixels.</param>
    /// <param name="arrowThick">The arrow thickness in pixels.</param>
    /// <returns>A texture representing the flow field for debugging purposes.</returns>
    private Texture2D BuildDebugTexture(
        FlowFieldData.FlowFieldResult flowField,
        int arrowLen,
        int arrowThick)
    {
        int w = flowField.voronoi.regionMap.GetLength(0);
        int h = flowField.voronoi.regionMap.GetLength(1);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        var regionColors = new Dictionary<int, Color>();
        foreach (var r in flowField.voronoi.regions.Keys)
            regionColors[r] = RandomPastelColor(r);

        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int regionId = flowField.voronoi.regionMap[x, y];
                if (!regionColors.TryGetValue(regionId, out var c))
                    c = Color.magenta;
                pixels[y * w + x] = c;
            }
        }

        foreach (var kvp in flowField.flowPerRegion)
        {
            var info = kvp.Value;
            DrawArrowOnPixels(
                pixels,
                w,
                h,
                info.center,
                info.direction,
                arrowLen,
                arrowThick,
                Color.black
            );
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Generates a deterministic pastel color based on the provided region identifier.
    /// </summary>
    /// <param name="seed">The identifier used to seed the color generation.</param>
    /// <returns>A pastel <see cref="Color"/> used to draw the region.</returns>
    private Color RandomPastelColor(int seed)
    {
        unchecked
        {
            int hash = (int)(seed * 73856093 ^ 0x9e3779b9);
            float r = ((hash & 0xFF) / 255f + 1f) * 0.5f;
            float g = (((hash >> 8) & 0xFF) / 255f + 1f) * 0.5f;
            float b = (((hash >> 16) & 0xFF) / 255f + 1f) * 0.5f;
            return new Color(r, g, b, 1f);
        }
    }

    /// <summary>
    /// Draws a single arrow into the pixel buffer.
    /// </summary>
    /// <param name="pixels">The pixel array that represents the texture.</param>
    /// <param name="w">Texture width in pixels.</param>
    /// <param name="h">Texture height in pixels.</param>
    /// <param name="center">The starting point of the arrow.</param>
    /// <param name="dir">The direction of the arrow.</param>
    /// <param name="length">Arrow length in pixels.</param>
    /// <param name="thick">Arrow thickness in pixels.</param>
    /// <param name="col">Arrow color.</param>
    private void DrawArrowOnPixels(
        Color[] pixels,
        int w,
        int h,
        Vector2 center,
        Vector2 dir,
        int length,
        int thick,
        Color col)
    {
        Vector2 end = center + dir.normalized * length;

        DrawLine(pixels, w, h, center, end, thick, col);

        Vector2 leftDir = Quaternion.Euler(0, 0, 150f) * dir.normalized;
        Vector2 rightDir = Quaternion.Euler(0, 0, -150f) * dir.normalized;

        Vector2 headL = end + leftDir * (length * 0.3f);
        Vector2 headR = end + rightDir * (length * 0.3f);

        DrawLine(pixels, w, h, end, headL, thick, col);
        DrawLine(pixels, w, h, end, headR, thick, col);
    }

    /// <summary>
    /// Draws a line onto the pixel buffer using a Bresenham-style rasterisation.
    /// </summary>
    /// <param name="pixels">The target pixel buffer.</param>
    /// <param name="w">Texture width in pixels.</param>
    /// <param name="h">Texture height in pixels.</param>
    /// <param name="a">Line starting point.</param>
    /// <param name="b">Line ending point.</param>
    /// <param name="thick">Line thickness in pixels.</param>
    /// <param name="col">The color to apply.</param>
    private void DrawLine(
        Color[] pixels,
        int w,
        int h,
        Vector2 a,
        Vector2 b,
        int thick,
        Color col)
    {
        int x0 = Mathf.RoundToInt(a.x);
        int y0 = Mathf.RoundToInt(a.y);
        int x1 = Mathf.RoundToInt(b.x);
        int y1 = Mathf.RoundToInt(b.y);

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = (x0 < x1) ? 1 : -1;
        int sy = (y0 < y1) ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            for (int oy = -thick; oy <= thick; oy++)
                for (int ox = -thick; ox <= thick; ox++)
                {
                    int px = x0 + ox;
                    int py = y0 + oy;
                    if (px >= 0 && px < w && py >= 0 && py < h)
                    {
                        pixels[py * w + px] = col;
                    }
                }

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }
    #endregion
}
