using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class ElevationBiasComponent : MonoBehaviour
{
    [Header("Sources (auto)")]
    public VoronoiComponent voronoiSource;
    public PlateAssignerComponent plateSource;
    public PlateInteractionComponent interactionSource;

    [Header("Base elevation by plate type")]
    [Tooltip("Bias for regions assigned as Oceanic plates (typically lower).")]
    public float oceanicBase = -0.5f;

    [Tooltip("Bias for regions assigned as Continental plates (typically higher).")]
    public float continentalBase = 0.5f;

    [Tooltip("Bias for regions assigned as Boundary / undefined plates.")]
    public float boundaryBase = 0.0f;

    [Header("Edge modifiers (added on top of base where boundary is detected)")]
    [Tooltip("Added to BOTH sides of a convergent boundary pixel. Positive ~ mountains, negative ~ trench.")]
    public float convergentDelta = 0.6f;

    [Tooltip("Added to BOTH sides of a divergent boundary pixel. Can be slightly positive for mid-ocean ridge, or negative for continental rift.")]
    public float divergentDelta = 0.2f;

    [Tooltip("Added to BOTH sides of a transform boundary pixel. Usually small.")]
    public float transformDelta = 0.05f;

    [Tooltip("Added to BOTH sides if we can't classify interaction.")]
    public float unknownDelta = 0.0f;

    [Header("Preview tone mapping")]
    [Tooltip("Value in bias = previewMid will render ~50% gray")]
    public float previewMid = 0.0f;

    [Tooltip("How strong preview scales around previewMid. Higher = more contrast.")]
    public float previewScale = 1.0f;

    [Header("Output (read-only)")]
    public Texture2D biasPreviewTexture;

    [Header("Edge influence radii (in pixels)")]
    [Tooltip("How wide convergent effects spread from the boundary line.")]
    public int convergentRadius = 6;

    [Tooltip("How wide divergent effects spread from the boundary line.")]
    public int divergentRadius = 4;

    [Tooltip("How wide transform effects spread from the boundary line.")]
    public int transformRadius = 2;

    [Tooltip("How wide unknown effects spread from the boundary line.")]
    public int unknownRadius = 2;

    [Header("Edge falloff")]
    [Tooltip("1.0 = linear falloff to 0 at max radius. >1 makes it drop faster, <1 makes it wider.")]
    public float falloffPower = 1.0f;

    [Header("Coastline smoothing (post-process)")]
    [Tooltip("Radius in pixels for shoreline/collision smoothing after stamping.")]
    public int shoreBlendRadius = 4;

    [Tooltip("How strongly to blend toward local average near sharp plate boundaries. 0 = off, 1 = max smoothing.")]
    [Range(0f, 1f)]
    public float shoreBlendStrength = 0.5f;

    [Tooltip("If local (max-min) in the neighborhood exceeds this, we treat it as a hard seam and smooth it.")]
    public float seamThreshold = 0.4f;

    // we keep raw bias result if game code wants to read it
    [NonSerialized]
    public float[,] biasData; // [x,y]

    public void GenerateNow()
    {
        // auto-link
        if (voronoiSource == null) voronoiSource = GetComponent<VoronoiComponent>();
        if (plateSource == null) plateSource = GetComponent<PlateAssignerComponent>();
        if (interactionSource == null) interactionSource = GetComponent<PlateInteractionComponent>();

        if (voronoiSource == null)
        {
            Debug.LogWarning("ElevationBiasComponent: Missing VoronoiComponent.");
            return;
        }
        if (plateSource == null)
        {
            Debug.LogWarning("ElevationBiasComponent: Missing PlateAssignerComponent.");
            return;
        }
        if (interactionSource == null)
        {
            Debug.LogWarning("ElevationBiasComponent: Missing PlateInteractionComponent.");
            return;
        }

        var voroData = voronoiSource.GetLastResult();
        if (voroData.regionMap == null)
        {
            Debug.LogWarning("ElevationBiasComponent: Voronoi data empty. Generate Voronoi first.");
            return;
        }

        int w = voronoiSource.width;
        int h = voronoiSource.height;
        int[,] regionMap = voroData.regionMap;

        // 1. pull plate map
        var plateMap = plateSource.GetRegionPlateMap();
        // 2. pull region motion from interactionSource's flowSource logic,
        //    but we only need interaction classification, not directions now.
        // We'll replicate the same classification logic here by directly
        // calling interactionSource.InferInteraction(regionA, regionB)
        // via a helper wrapper we'll expose below.

        biasData = new float[w, h];

        // We'll also make an InteractionType mask per pixel,
        // keeping the "strongest" boundary type that this pixel participates in.
        // If multiple edges meet here, we'll just pick the most 'intense' one,
        // priority Convergent > Divergent > Transform > Unknown > None
        PlateInteractionComponent.InteractionType[,] interactionAtPixel =
            new PlateInteractionComponent.InteractionType[w, h];

        // ------------------------------------------------
        // Step A: base bias (per plate type)
        // ------------------------------------------------
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int reg = regionMap[x, y];

                var pType = PlateAssignerComponent.PlateType.Continental;
                if (plateMap.TryGetValue(reg, out var t))
                    pType = t;

                float baseVal = pType switch
                {
                    PlateAssignerComponent.PlateType.Oceanic => oceanicBase,
                    PlateAssignerComponent.PlateType.Continental => continentalBase,
                    PlateAssignerComponent.PlateType.Boundary => boundaryBase,
                    _ => boundaryBase
                };

                biasData[x, y] = baseVal;
                interactionAtPixel[x, y] = PlateInteractionComponent.InteractionType.Unknown;
            }
        }

        // ------------------------------------------------
        // Step B: detect boundary pixels and store interaction type
        //         (like before, but no adding yet)
        // ------------------------------------------------
        // We'll collect all boundary pixels into a list to stamp later
        List<BoundaryPixel> boundaryPixels = new List<BoundaryPixel>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int regHere = regionMap[x, y];

                // check RIGHT
                if (x + 1 < w)
                {
                    int regRight = regionMap[x + 1, y];
                    if (regRight != regHere)
                    {
                        var it = GetInteractionSafe(interactionSource, regHere, regRight);
                        RegisterBoundaryPixel(interactionAtPixel, boundaryPixels, x, y, it);
                    }
                }

                // check UP
                if (y + 1 < h)
                {
                    int regUp = regionMap[x, y + 1];
                    if (regUp != regHere)
                    {
                        var it = GetInteractionSafe(interactionSource, regHere, regUp);
                        RegisterBoundaryPixel(interactionAtPixel, boundaryPixels, x, y, it);
                    }
                }
            }
        }

        // ------------------------------------------------
        // Step C: edge influence field
        // We'll accumulate deltas separately, then add to biasData at the end.
        // ------------------------------------------------
        float[,] edgeDelta = new float[w, h];

        // Stamp each boundary pixel outward with falloff.
        foreach (var bp in boundaryPixels)
        {
            StampInfluence(
                edgeDelta,
                w,
                h,
                bp.x,
                bp.y,
                bp.type,
                falloffPower,
                convergentDelta,
                divergentDelta,
                transformDelta,
                unknownDelta,
                convergentRadius,
                divergentRadius,
                transformRadius,
                unknownRadius
            );
        }

        // Now apply edgeDelta on top of base bias
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                biasData[x, y] += edgeDelta[x, y];
            }
        }

        // ------------------------------------------------
        // Step C2: shoreline / plate-boundary smoothing
        //          (soften huge jumps between oceanic/continental)
        // ------------------------------------------------
        if (shoreBlendStrength > 0f && shoreBlendRadius > 0)
        {
            biasData = SmoothSeams(
                biasData,
                w,
                h,
                shoreBlendRadius,
                shoreBlendStrength,
                seamThreshold
            );
        }

        // ------------------------------------------------
        // Step D: build preview
        // ------------------------------------------------
        biasPreviewTexture = BuildPreviewTexture(w, h, biasData, previewMid, previewScale);
        biasPreviewTexture.name = "ElevationBiasPreview";

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (biasPreviewTexture != null) biasPreviewTexture.Apply();
#endif

        Debug.Log("ElevationBiasComponent: bias map generated with falloff stamping.");
    }

    // --------------------------------------------------------------------------------
    // Helper: choose strongest interaction for pixel
    // Priority: Convergent > Divergent > Transform > Unknown
    // --------------------------------------------------------------------------------
    private void ApplyInteractionTypeToPixel(
        PlateInteractionComponent.InteractionType[,] map,
        int x,
        int y,
        PlateInteractionComponent.InteractionType candidate
    )
    {
        var current = map[x, y];
        if (IsStronger(candidate, current))
            map[x, y] = candidate;
    }

    private bool IsStronger(
        PlateInteractionComponent.InteractionType a,
        PlateInteractionComponent.InteractionType b
    )
    {
        int Rank(PlateInteractionComponent.InteractionType t)
        {
            switch (t)
            {
                case PlateInteractionComponent.InteractionType.Convergent: return 4;
                case PlateInteractionComponent.InteractionType.Divergent: return 3;
                case PlateInteractionComponent.InteractionType.Transform: return 2;
                case PlateInteractionComponent.InteractionType.Unknown: return 1;
                default: return 0;
            }
        }

        return Rank(a) > Rank(b);
    }

    /// <summary>
    /// SmoothSeams:
    /// For each pixel, look at a neighborhood (disk with given radius).
    /// - Compute local min, max, avg.
    /// - If (max - min) is large (bigger than seamThreshold), we assume this area
    ///   is where oceanic/continental or trench/mountain meet too brutally.
    ///   Then we blend the pixel a bit toward that local average.
    ///
    /// This preserves highlands vs lowlands inside a single plate,
    /// but softens coastal cliffs and plate-drop walls.
    /// </summary>
    private float[,] SmoothSeams(
        float[,] src,
        int w,
        int h,
        int radius,
        float blendStrength,
        float seamThresholdVal
    )
    {
        float[,] dst = new float[w, h];

        // precompute radius^2 to avoid sqrt per neighbor
        int r2 = radius * radius;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                int count = 0;
                float localMin = float.PositiveInfinity;
                float localMax = float.NegativeInfinity;

                int minX = Mathf.Max(0, x - radius);
                int maxX = Mathf.Min(w - 1, x + radius);
                int minY = Mathf.Max(0, y - radius);
                int maxY = Mathf.Min(h - 1, y + radius);

                // gather neighborhood stats
                for (int ny = minY; ny <= maxY; ny++)
                {
                    int dy = ny - y;
                    for (int nx = minX; nx <= maxX; nx++)
                    {
                        int dx = nx - x;
                        int d2 = dx * dx + dy * dy;
                        if (d2 > r2) continue; // outside disk

                        float v = src[nx, ny];
                        sum += v;
                        count++;

                        if (v < localMin) localMin = v;
                        if (v > localMax) localMax = v;
                    }
                }

                if (count == 0)
                {
                    dst[x, y] = src[x, y];
                    continue;
                }

                float avg = sum / count;
                float range = localMax - localMin;

                // if range is small -> already smooth -> keep original
                // if range is huge -> strong seam -> nudge toward avg
                if (range >= seamThresholdVal)
                {
                    // linear blend:
                    // new = lerp(original, avg, blendStrength)
                    float original = src[x, y];
                    float blended = Mathf.Lerp(original, avg, blendStrength);
                    dst[x, y] = blended;
                }
                else
                {
                    dst[x, y] = src[x, y];
                }
            }
        }

        return dst;
    }



    private void StampInfluence(
        float[,] edgeDelta,
        int w,
        int h,
        int cx,
        int cy,
        PlateInteractionComponent.InteractionType interaction,
        float falloffPow,
        float convDelta,
        float divDelta,
        float transDelta,
        float unkDelta,
        int convRadius,
        int divRadius,
        int transRadius,
        int unkRadius
    )
    {
        // Pick params based on interaction type
        float strength;   // base added at center
        int radius;       // how far we spread

        switch (interaction)
        {
            case PlateInteractionComponent.InteractionType.Convergent:
                strength = convDelta;
                radius = Mathf.Max(1, convRadius);
                break;
            case PlateInteractionComponent.InteractionType.Divergent:
                strength = divDelta;
                radius = Mathf.Max(1, divRadius);
                break;
            case PlateInteractionComponent.InteractionType.Transform:
                strength = transDelta;
                radius = Mathf.Max(1, transRadius);
                break;
            case PlateInteractionComponent.InteractionType.Unknown:
            default:
                strength = unkDelta;
                radius = Mathf.Max(1, unkRadius);
                break;
        }

        // square we'll iterate
        int minX = Mathf.Max(0, cx - radius);
        int maxX = Mathf.Min(w - 1, cx + radius);
        int minY = Mathf.Max(0, cy - radius);
        int maxY = Mathf.Min(h - 1, cy + radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - cx;
                int dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > radius)
                    continue;

                // normalized distance 0..1
                float t = dist / Mathf.Max(0.0001f, radius);

                // falloff: (1 - t)^power
                // dist=0   -> 1
                // dist=r   -> 0
                float fall = Mathf.Pow(1f - t, falloffPow);

                // stamp contribution
                float add = strength * fall;

                edgeDelta[x, y] += add;
            }
        }
    }

    // --------------------------------------------------------------------------------
    // Ask PlateInteractionComponent to infer interaction between any two regions.
    // We reuse its inference (dot product logic).
    // --------------------------------------------------------------------------------
    private PlateInteractionComponent.InteractionType GetInteractionSafe(
        PlateInteractionComponent pic,
        int regionA,
        int regionB
    )
    {
        try
        {
            return picDebugInfer(pic, regionA, regionB);
        }
        catch (Exception)
        {
            Debug.LogWarning("ElevationBiasComponent: interactionSource infer failed; defaulting Unknown.");
            return PlateInteractionComponent.InteractionType.Unknown;
        }
    }

    // Represents a single boundary pixel and its interaction type
    private struct BoundaryPixel
    {
        public int x;
        public int y;
        public PlateInteractionComponent.InteractionType type;
    }

    private void RegisterBoundaryPixel(
        PlateInteractionComponent.InteractionType[,] interactionAtPixel,
        List<BoundaryPixel> list,
        int x,
        int y,
        PlateInteractionComponent.InteractionType candidate
    )
    {
        // store in interactionAtPixel using the same priority logic
        ApplyInteractionTypeToPixel(interactionAtPixel, x, y, candidate);

        // add to list so we can stamp later
        list.Add(new BoundaryPixel
        {
            x = x,
            y = y,
            type = candidate
        });
    }

    // We call internal logic via a small wrapper. We'll add a public helper on PlateInteractionComponent.
    private PlateInteractionComponent.InteractionType picDebugInfer(
        PlateInteractionComponent pic,
        int regionA,
        int regionB
    )
    {
        // We'll rely on PlateInteractionComponent having a public method:
        // public InteractionType InferInteractionPublic(int regionA, int regionB)
        return pic.InferInteractionPublic(regionA, regionB);
    }

    // --------------------------------------------------------------------------------
    // Preview texture builder
    // --------------------------------------------------------------------------------
    private Texture2D BuildPreviewTexture(int w, int h, float[,] bias, float mid, float scale)
    {
        Color[] pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v = bias[x, y];

                // map bias → 0..1 for grayscale preview
                // first shift so mid becomes 0, then scale, then clamp
                float normalized = 0.5f + (v - mid) * scale;
                if (normalized < 0f) normalized = 0f;
                if (normalized > 1f) normalized = 1f;

                Color c = new Color(normalized, normalized, normalized, 1f);
                pixels[y * w + x] = c;
            }
        }

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // --------------------------------------------------------------------------------
    // Public accessors for your terrain generator
    // --------------------------------------------------------------------------------
    public float[,] GetBiasData()
    {
        return biasData;
    }

    public Texture2D GetBiasPreviewTexture()
    {
        return biasPreviewTexture;
    }
}
