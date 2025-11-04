using UnityEngine;

/// <summary>
/// Post-processes a heightmap (0..1) to simulate basic erosion / plateauing:
/// - Flattens low-slope regions into plains / plateaus
/// - Keeps detail in high-slope / mountain regions
/// </summary>
[ExecuteInEditMode]
public class HeightPostProcessComponent : MonoBehaviour
{
    [Header("General toggles")]
    public bool enablePostProcess = true;

    [Tooltip("How wide the sampling kernel is for slope+blur. 1 = 3x3, 2 = 5x5, etc.")]
    [Range(1, 4)]
    public int kernelRadius = 1;

    [Header("Slope thresholds")]
    [Tooltip("Below this slope we consider area 'flat land' and we aggressively smooth/terrace it.")]
    [Range(0f, 1f)]
    public float flatSlopeThreshold = 0.02f;

    [Tooltip("Above this slope we consider area 'mountainous', we keep sharp detail.")]
    [Range(0f, 1f)]
    public float mountainSlopeThreshold = 0.08f;

    [Header("Plains / Plateaus shaping")]
    [Tooltip("0=don't smooth plains at all, 1=fully smooth plains to local average.")]
    [Range(0f, 1f)]
    public float plainsSmoothingStrength = 0.6f;

    [Tooltip("Number of 'terrace levels' to snap flat areas toward. 0 = disabled.")]
    [Min(0)]
    public int plainsTerraceSteps = 4;

    [Tooltip("How strong we snap towards terrace steps (0=no snapping, 1=full snap).")]
    [Range(0f, 1f)]
    public float terraceStrength = 0.5f;

    [Header("Mountains shaping")]
    [Tooltip("0=leave mountains raw, 1=slightly sharpen cliffs by pushing away from local average.")]
    [Range(0f, 1f)]
    public float mountainSharpenStrength = 0.25f;

    /// <summary>
    /// Main entry: takes heights in [0..1], mutates/returns processed copy.
    /// </summary>
    public float[,] Process(float[,] srcHeights01)
    {
        if (!enablePostProcess || srcHeights01 == null)
            return srcHeights01;

        int w = srcHeights01.GetLength(0);
        int h = srcHeights01.GetLength(1);

        float[,] result = new float[w, h];

        // pass 1: we’ll compute local average + local slope for each pixel
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Local average and max diff (simple slope metric)
                float localSum = 0f;
                float localCount = 0f;
                float center = srcHeights01[x, y];
                float maxDiff = 0f;

                for (int oy = -kernelRadius; oy <= kernelRadius; oy++)
                {
                    int ny = y + oy;
                    if (ny < 0 || ny >= h) continue;

                    for (int ox = -kernelRadius; ox <= kernelRadius; ox++)
                    {
                        int nx = x + ox;
                        if (nx < 0 || nx >= w) continue;

                        float v = srcHeights01[nx, ny];
                        localSum += v;
                        localCount += 1f;

                        float d = Mathf.Abs(v - center);
                        if (d > maxDiff) maxDiff = d;
                    }
                }

                float localAvg = localSum / Mathf.Max(1f, localCount);
                float slope = maxDiff; // cheap "roughness" estimate 0..1-ish

                // Decide behavior based on slope:
                float newHeight = center;

                if (slope <= flatSlopeThreshold)
                {
                    // --- PLAINS / PLATEAU REGION ---
                    // 1. Smooth towards local average
                    newHeight = Mathf.Lerp(center, localAvg, plainsSmoothingStrength);

                    // 2. Terracing: snap toward discrete steps
                    if (plainsTerraceSteps > 0 && terraceStrength > 0f)
                    {
                        // quantize newHeight into N steps across [0..1]
                        float stepSize = 1f / plainsTerraceSteps;
                        float snapped = Mathf.Round(newHeight / stepSize) * stepSize;
                        newHeight = Mathf.Lerp(newHeight, snapped, terraceStrength);
                    }
                }
                else if (slope >= mountainSlopeThreshold)
                {
                    // --- MOUNTAIN / RIDGE REGION ---
                    // Sharpen: exaggerate difference from local avg
                    // If center > avg -> push up a bit; if < avg -> push down a bit
                    float diff = center - localAvg;
                    newHeight = center + diff * mountainSharpenStrength;
                }
                else
                {
                    // --- TRANSITION REGION ---
                    // Blend between plains behavior and mountain behavior proportionally
                    float t = Mathf.InverseLerp(flatSlopeThreshold, mountainSlopeThreshold, slope);

                    // plains-style smoothed:
                    float plainsHeight = Mathf.Lerp(center, localAvg, plainsSmoothingStrength);
                    if (plainsTerraceSteps > 0 && terraceStrength > 0f)
                    {
                        float stepSize = 1f / plainsTerraceSteps;
                        float snapped = Mathf.Round(plainsHeight / stepSize) * stepSize;
                        plainsHeight = Mathf.Lerp(plainsHeight, snapped, terraceStrength);
                    }

                    // mountain-style sharpened:
                    float diff = center - localAvg;
                    float mountainHeight = center + diff * mountainSharpenStrength;

                    // t=0 -> plains, t=1 -> mountain
                    newHeight = Mathf.Lerp(plainsHeight, mountainHeight, t);
                }

                // clamp for safety
                result[x, y] = Mathf.Clamp01(newHeight);
            }
        }

        return result;
    }
}
