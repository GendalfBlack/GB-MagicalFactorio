using UnityEngine;

[ExecuteAlways]
public class WindFieldSmootherComponent : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Upstream inland advection result (must be generated first).")]
    public CoastalInlandAdvectionComponent inlandSource;

    [Header("Base Smoothing")]
    [Tooltip("Radius in pixels for neighborhood averaging.")]
    [Range(1, 50)]
    public int smoothRadius = 8;

    [Tooltip("Baseline smoothing everywhere. 0 = keep original, 1 = full blur.")]
    [Range(0f, 1f)]
    public float smoothBlend = 0.4f;

    [Tooltip("Extra passes of smoothing. Each pass re-smooths the previous output.")]
    [Range(1, 5)]
    public int iterations = 1;

    [Header("Adaptive Edge Blending")]
    [Tooltip("If enabled, sharp seams get smoothed harder than already-smooth zones.")]
    public bool edgeAdaptive = true;

    [Tooltip("How much additional smoothing we can add on sharp edges.")]
    [Range(0f, 1f)]
    public float edgeBoost = 0.5f;

    [Tooltip("How sensitive we are to calling something a 'sharp edge'. Lower = more pixels treated as edges.")]
    [Min(0.001f)]
    public float edgeSensitivity = 1f;

    [Header("Preview (read-only)")]
    [Tooltip("R = X vel, G = Y vel, B = speed AFTER smoothing")]
    public Texture2D smoothedPreviewTexture;

    [HideInInspector] public Vector2[,] finalWindVector;   // [y,x] after smoothing
    [HideInInspector] public float[,] finalWindSpeed01;    // [y,x] 0..1 magnitude

    public void GenerateSmoothedWind()
    {
        if (inlandSource == null ||
            inlandSource.inlandWindVector == null ||
            inlandSource.inlandWindSpeed01 == null)
        {
            Debug.LogWarning("WindFieldSmoother: inlandSource not ready. Run CoastalInlandAdvection first.");
            return;
        }

        Vector2[,] srcVec = inlandSource.inlandWindVector;
        float[,] srcSpd01 = inlandSource.inlandWindSpeed01;

        int h = srcVec.GetLength(0);
        int w = srcVec.GetLength(1);

        EnsureBuffers(w, h);

        // copy source into working X/Y buffers
        float[,] curX = new float[h, w];
        float[,] curY = new float[h, w];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                curX[y, x] = srcVec[y, x].x;
                curY[y, x] = srcVec[y, x].y;
            }
        }

        float[,] workX = new float[h, w];
        float[,] workY = new float[h, w];

        // blur iterations
        for (int it = 0; it < iterations; it++)
        {
            BoxBlur(curX, workX, smoothRadius);
            BoxBlur(curY, workY, smoothRadius);

            // swap
            var tmpX = curX; curX = workX; workX = tmpX;
            var tmpY = curY; curY = workY; workY = tmpY;
        }

        // now curX/curY == blurred vectors
        // build final per-pixel using adaptive smoothing
        Color[] previewPixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 orig = srcVec[y, x];
                Vector2 blur = new Vector2(curX[y, x], curY[y, x]);

                // how different are we? (edge strength)
                float diffMag = (orig - blur).magnitude;

                // normalize by sensitivity
                float edgeFactor01 = Mathf.Clamp01(diffMag / edgeSensitivity);

                float localBlend = smoothBlend;
                if (edgeAdaptive)
                {
                    // add extra smoothing where big jumps happen
                    localBlend += edgeBoost * edgeFactor01;
                }

                // clamp 0..1 for safety
                localBlend = Mathf.Clamp01(localBlend);

                Vector2 blended = Vector2.Lerp(orig, blur, localBlend);

                finalWindVector[y, x] = blended;

                float spd = blended.magnitude;
                float spd01 = Mathf.Clamp01(spd);
                finalWindSpeed01[y, x] = spd01;

                // encode preview:
                // R=X, G=Y, B=speed01
                float r = 0.5f + blended.x * 0.5f;
                float g = 0.5f + blended.y * 0.5f;
                float b = spd01;

                previewPixels[y * w + x] = new Color(r, g, b, 1f);
            }
        }

        // push preview texture
        if (smoothedPreviewTexture == null ||
            smoothedPreviewTexture.width != w ||
            smoothedPreviewTexture.height != h)
        {
            smoothedPreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            smoothedPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            smoothedPreviewTexture.filterMode = FilterMode.Point;
        }

        smoothedPreviewTexture.SetPixels(previewPixels);
        smoothedPreviewTexture.Apply();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        Debug.Log("WindFieldSmoother: finalWindVector generated (adaptive edges applied).");
    }

    void EnsureBuffers(int w, int h)
    {
        if (finalWindVector == null ||
            finalWindVector.GetLength(0) != h ||
            finalWindVector.GetLength(1) != w)
        {
            finalWindVector = new Vector2[h, w];
        }

        if (finalWindSpeed01 == null ||
            finalWindSpeed01.GetLength(0) != h ||
            finalWindSpeed01.GetLength(1) != w)
        {
            finalWindSpeed01 = new float[h, w];
        }
    }

    // BoxBlur: clampy 2-pass blur (horizontal then vertical)
    void BoxBlur(float[,] src, float[,] dst, int r)
    {
        int h = src.GetLength(0);
        int w = src.GetLength(1);

        // horizontal into dst
        for (int y = 0; y < h; y++)
        {
            float run = 0f;
            int count = 0;

            for (int x = 0; x <= r && x < w; x++)
            {
                run += src[y, x];
                count++;
            }

            for (int x = 0; x < w; x++)
            {
                int leftOut = x - r - 1;
                int rightIn = x + r;

                if (leftOut >= 0)
                {
                    run -= src[y, leftOut];
                    count--;
                }
                if (rightIn < w)
                {
                    run += src[y, rightIn];
                    count++;
                }

                dst[y, x] = run / Mathf.Max(1, count);
            }
        }

        // vertical into tmp, then copy back
        float[,] tmp = new float[h, w];

        for (int x = 0; x < w; x++)
        {
            float run = 0f;
            int count = 0;

            for (int y = 0; y <= r && y < h; y++)
            {
                run += dst[y, x];
                count++;
            }

            for (int y = 0; y < h; y++)
            {
                int upOut = y - r - 1;
                int downIn = y + r;

                if (upOut >= 0)
                {
                    run -= dst[upOut, x];
                    count--;
                }
                if (downIn < h)
                {
                    run += dst[downIn, x];
                    count++;
                }

                tmp[y, x] = run / Mathf.Max(1, count);
            }
        }

        // copy tmp -> dst for final
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                dst[y, x] = tmp[y, x];
            }
        }
    }
}
