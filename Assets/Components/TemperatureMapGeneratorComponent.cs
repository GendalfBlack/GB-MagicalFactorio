using UnityEngine;

[ExecuteAlways]
public class TemperatureMapGeneratorComponent : MonoBehaviour
{
    [Header("Output Resolution")]
    [Range(16, 2048)] public int mapResolution = 512;

    [Header("Latitude → Temperature Curve")]
    [Tooltip("Higher = equator stays hot wider, poles drop faster.")]
    [Range(0.5f, 3f)] public float temperatureFalloff = 1.5f;

    [Tooltip("Equator temperature (0..1).")]
    [Range(0f, 1f)] public float equatorTemp = 1f;

    [Tooltip("Pole temperature (0..1).")]
    [Range(0f, 1f)] public float poleTemp = 0f;

    [Header("Preview (read-only)")]
    public Texture2D basePreviewTexture;

    [HideInInspector] public float[,] baseTemperature01; // [y,x], normalized 0..1

    public void GenerateBaseTemperature()
    {
        int w = mapResolution;
        int h = mapResolution;
        if (w <= 0 || h <= 0) return;

        // allocate / resize data buffer
        if (baseTemperature01 == null ||
            baseTemperature01.GetLength(0) != h ||
            baseTemperature01.GetLength(1) != w)
        {
            baseTemperature01 = new float[h, w];
        }

        // allocate / resize preview texture
        if (basePreviewTexture == null ||
            basePreviewTexture.width != w ||
            basePreviewTexture.height != h)
        {
            basePreviewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            basePreviewTexture.wrapMode = TextureWrapMode.Clamp;
            basePreviewTexture.filterMode = FilterMode.Point;
        }

        var pixels = new Color[w * h];

        for (int py = 0; py < h; py++)
        {
            // v goes 0 (bottom/south) → 1 (top/north)
            float v = (float)py / (h - 1);

            // distance from equator: 0 at equator, 1 at poles
            float lat01 = Mathf.Abs(v - 0.5f) * 2f;

            // 1 at equator → 0 at poles, shaped by falloff
            float rawLatTemp01 = 1f - Mathf.Pow(lat01, temperatureFalloff);

            // real temp row blend between poleTemp..equatorTemp
            float rowTemp01 = Mathf.Lerp(poleTemp, equatorTemp, rawLatTemp01);

            for (int px = 0; px < w; px++)
            {
                float t = Mathf.Clamp01(rowTemp01);

                baseTemperature01[py, px] = t;
                pixels[py * w + px] = new Color(t, t, t, 1f);
            }
        }

        basePreviewTexture.SetPixels(pixels);
        basePreviewTexture.Apply();
    }
}
