using System.Collections.Generic;
using UnityEngine;

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
        public int regionId;
        public Vector2 center;
        public Vector2 direction;
    }

    private List<RegionFlowData> regionsFlowData = new List<RegionFlowData>();
    #endregion

    public Dictionary<int, Vector2> GetRegionFlowDirections() 
    {
        var dict = new Dictionary<int, Vector2>();
        foreach (var rd in regionsFlowData)
        {
            dict[rd.regionId] = rd.direction;
        }
        return dict;
    }


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
}
