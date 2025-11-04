using System.Collections.Generic;
using UnityEngine;

public static class FlowFieldData
{
    // one region's flow info
    public struct RegionFlowInfo
    {
        public int regionId;
        public Vector2 center;      // region centroid in pixel coords
        public Vector2 direction;   // random unit vector
    }

    // whole result: references the voronoi + the flow directions
    public struct FlowFieldResult
    {
        public VoronoiGenerator.VoronoiResult voronoi;
        public Dictionary<int, RegionFlowInfo> flowPerRegion;
    }

    /// <summary>
    /// Build a flow field on top of an EXISTING VoronoiResult.
    /// No Voronoi generation here.
    /// </summary>
    public static FlowFieldResult GenerateFromVoronoi(
        VoronoiGenerator.VoronoiResult voro,
        int randomSeed = 0)
    {
        // 1. compute center of each region
        var centers = ComputeRegionCenters(voro.regions);

        // 2. assign random directions
        var rng = (randomSeed == 0)
            ? new System.Random()
            : new System.Random(randomSeed);

        var flowDict = new Dictionary<int, RegionFlowInfo>();

        foreach (var kvp in centers)
        {
            int regionId = kvp.Key;
            Vector2 center = kvp.Value;

            // pick random angle 0..2π
            double angle = rng.NextDouble() * Mathf.PI * 2f;
            Vector2 dir = new Vector2(
                Mathf.Cos((float)angle),
                Mathf.Sin((float)angle)
            ).normalized;

            flowDict[regionId] = new RegionFlowInfo
            {
                regionId = regionId,
                center = center,
                direction = dir
            };
        }

        return new FlowFieldResult
        {
            voronoi = voro,
            flowPerRegion = flowDict
        };
    }

    /// <summary>
    /// Average all tile coords in a region -> centroid.
    /// </summary>
    private static Dictionary<int, Vector2> ComputeRegionCenters(
        Dictionary<int, List<(int x, int y)>> regions)
    {
        var result = new Dictionary<int, Vector2>();

        foreach (var kvp in regions)
        {
            int regionId = kvp.Key;
            var tiles = kvp.Value;
            if (tiles == null || tiles.Count == 0)
            {
                result[regionId] = Vector2.zero;
                continue;
            }

            long sumX = 0;
            long sumY = 0;

            for (int i = 0; i < tiles.Count; i++)
            {
                sumX += tiles[i].x;
                sumY += tiles[i].y;
            }

            float cx = (float)sumX / tiles.Count;
            float cy = (float)sumY / tiles.Count;

            result[regionId] = new Vector2(cx, cy);
        }

        return result;
    }
}
