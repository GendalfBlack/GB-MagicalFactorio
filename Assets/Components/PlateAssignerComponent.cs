using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class PlateAssignerComponent : MonoBehaviour
{
    [Serializable]
    public enum PlateType
    {
        Oceanic,
        Continental,
        Boundary
    }

    [Serializable]
    public struct PlateWeight
    {
        public PlateType plateType;
        [Min(0f)] public float weight;
    }

    [Header("Source (auto)")]
    public VoronoiComponent voronoiSource;

    public WorldSeedComponent seedSource;

    [Header("Distribution Settings")]
    public PlateWeight[] plateWeights =
    {
        new PlateWeight { plateType = PlateType.Oceanic,     weight = 1f },
        new PlateWeight { plateType = PlateType.Continental, weight = 1f },
        new PlateWeight { plateType = PlateType.Boundary,    weight = 0.5f }
    };

    [Header("Output (read-only)")]
    public Texture2D platePreviewTexture;

    [SerializeField]
    [HideInInspector]
    private List<int> regionIdsPreview = new List<int>();
    [SerializeField]
    [HideInInspector]
    private List<PlateType> regionPlatesPreview = new List<PlateType>();

    private Dictionary<int, PlateType> regionToPlate = new Dictionary<int, PlateType>();

    public void CreatePlateTypesOnVoronoiMap()
    {
        if (voronoiSource == null)
            voronoiSource = GetComponent<VoronoiComponent>();

        if (seedSource == null)
            seedSource = GetComponent<WorldSeedComponent>();

        if (voronoiSource == null)
        {
            Debug.LogWarning("PlateAssigner: No VoronoiComponent found.");
            return;
        }

        var data = voronoiSource.GetLastResult();
        if (data.seeds == null || data.seeds.Length == 0 || data.regionMap == null)
        {
            Debug.LogWarning("FlowField: Voronoi data is empty. Generating Voronoi.");
            voronoiSource.CreateVoronoiMap();
            data = voronoiSource.GetLastResult();
        }

        int seedToUse = (seedSource != null) ? seedSource.worldSeed : 0;

        List<int> allRegionIds = new List<int>(data.regions.Keys);
        if (allRegionIds.Count == 0)
        {
            Debug.LogWarning("PlateAssigner: No regions found.");
            return;
        }

        int regionCount = allRegionIds.Count;

        List<PlateType> plateList = BuildExactPlateList(regionCount, plateWeights, seedToUse);

        Shuffle(allRegionIds, seedToUse);
        Shuffle(plateList, seedToUse + 1337);

        regionToPlate.Clear();
        for (int i = 0; i < regionCount; i++)
        {
            regionToPlate[allRegionIds[i]] = plateList[i];
        }

        regionIdsPreview.Clear();
        regionPlatesPreview.Clear();
        foreach (var kvp in regionToPlate)
        {
            regionIdsPreview.Add(kvp.Key);
            regionPlatesPreview.Add(kvp.Value);
        }

        platePreviewTexture = BuildPlatePreviewTexture(
            data.regionMap,
            voronoiSource.width,
            voronoiSource.height,
            regionToPlate
        );

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        Debug.Log($"PlateAssigner: Assigned {regionToPlate.Count} regions by shared seed {seedToUse}.");
    }

    List<PlateType> BuildExactPlateList(int regionCount, PlateWeight[] weights, int seed)
    {
        var result = new List<PlateType>(regionCount);

        if (weights == null || weights.Length == 0)
        {
            for (int i = 0; i < regionCount; i++)
                result.Add(PlateType.Continental);
            return result;
        }

        float totalWeight = 0f;
        foreach (var w in weights)
            totalWeight += Mathf.Max(0f, w.weight);

        if (totalWeight <= 0f)
        {
            for (int i = 0; i < regionCount; i++)
                result.Add(PlateType.Continental);
            return result;
        }

        int remaining = regionCount;
        var counts = new int[weights.Length];
        var remainders = new float[weights.Length];

        for (int i = 0; i < weights.Length; i++)
        {
            float normalized = Mathf.Max(0f, weights[i].weight) / totalWeight;
            float exact = normalized * regionCount;
            int c = Mathf.FloorToInt(exact);
            counts[i] = c;
            remainders[i] = exact - c;
            remaining -= c;
        }

        // largest remainders method
        List<int> idx = new List<int>();
        for (int i = 0; i < weights.Length; i++) idx.Add(i);
        idx.Sort((a, b) => remainders[b].CompareTo(remainders[a]));

        for (int k = 0; k < idx.Count && remaining > 0; k++)
        {
            counts[idx[k]] += 1;
            remaining -= 1;
            if (remaining > 0 && k == idx.Count - 1)
                k = -1;
        }

        for (int i = 0; i < weights.Length; i++)
        {
            for (int c = 0; c < counts[i]; c++)
                result.Add(weights[i].plateType);
        }

        if (result.Count > regionCount)
        {
            result.RemoveRange(regionCount, result.Count - regionCount);
        }
        else if (result.Count < regionCount)
        {
            while (result.Count < regionCount)
                result.Add(weights[0].plateType);
        }

        Shuffle(result, seed == 0 ? 2025 : seed);

        return result;
    }

    public PlateType GetPlateTypeAt(int x, int y)
    {
        if (voronoiSource == null) return PlateType.Continental;

        var data = voronoiSource.GetLastResult();
        if (data.regionMap == null) return PlateType.Continental;

        int regionId = data.regionMap[x, y];
        if (regionToPlate.TryGetValue(regionId, out var p))
            return p;

        return PlateType.Continental;
    }

    public Dictionary<int, PlateType> GetRegionPlateMap()
    {
        return regionToPlate;
    }

    Texture2D BuildPlatePreviewTexture(
        int[,] regionMap,
        int w,
        int h,
        Dictionary<int, PlateType> regionToPlateMap
    )
    {
        Color[] pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int regionId = regionMap[x, y];

                if (!regionToPlateMap.TryGetValue(regionId, out var plate))
                    plate = PlateType.Continental;

                pixels[y * w + x] = ColorForPlateType(plate);
            }
        }

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    Color ColorForPlateType(PlateType t)
    {
        switch (t)
        {
            case PlateType.Oceanic:
                return new Color(0.1f, 0.2f, 0.8f, 1f);
            case PlateType.Continental:
                return new Color(0.6f, 0.5f, 0.2f, 1f);
            case PlateType.Boundary:
                return new Color(0.9f, 0.3f, 0.1f, 1f);
            default:
                return Color.magenta;
        }
    }

    void Shuffle<T>(List<T> list, int seed)
    {
        System.Random rng = (seed == 0) ? new System.Random() : new System.Random(seed);

        for (int i = list.Count - 1; i > 0; i--)
        {
            int k = rng.Next(i + 1);
            (list[i], list[k]) = (list[k], list[i]);
        }
    }
}
