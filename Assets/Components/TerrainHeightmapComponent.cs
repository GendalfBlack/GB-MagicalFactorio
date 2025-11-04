using UnityEngine;

/// <summary>
/// Створює heightmap і пушить її в TerrainData.
/// </summary>
[ExecuteInEditMode]
public class TerrainHeightmapComponent : MonoBehaviour
{
    [Header("Sources (auto)")]
    [Tooltip("Глобальний сид світу")]
    public WorldSeedComponent seedSource;

    [Tooltip("Компонент з biasData (континенти / океани / кордони плит)")]
    public ElevationBiasComponent biasSource;

    [Tooltip("Компонент постобробки висот (ерозія / плато)")]
    public HeightPostProcessComponent postProcess;

    [Tooltip("Цільовий Terrain у сцені, куди ми штовхаємо висоти")]
    public Terrain targetTerrain;

    [Header("Noise Settings")]
    [Tooltip("Чим менше значення – тим крупніші 'плями', чим більше – тим дрібніші деталі")]
    public float noiseScale = 100f;

    [Min(1)]
    public int octaves = 4;

    [Range(0f, 1f)]
    public float persistence = 0.5f;

    [Min(1f)]
    public float lacunarity = 2f;

    [Header("Bias Settings")]
    [Tooltip("Множник впливу biasData на кінцеву висоту")]
    public float biasStrength = 1.0f;

    [Header("Output (read-only)")]
    [Tooltip("Остання згенерена карта висот (нормалізована 0..1). Для дебагу.")]
    public Texture2D lastPreviewTexture;

    public void GenerateNow()
    {
        // авто-лінк
        if (seedSource == null)
            seedSource = GetComponent<WorldSeedComponent>();
        if (biasSource == null)
            biasSource = GetComponent<ElevationBiasComponent>();
        if (postProcess == null)
            postProcess = GetComponent<HeightPostProcessComponent>();

        if (targetTerrain == null)
        {
            Debug.LogWarning("TerrainHeightmapComponent: targetTerrain не задано.");
            return;
        }

        TerrainData tData = targetTerrain.terrainData;
        if (tData == null)
        {
            Debug.LogWarning("TerrainHeightmapComponent: targetTerrain.terrainData = null");
            return;
        }

        int resW = tData.heightmapResolution;
        int resH = tData.heightmapResolution;

        int currentSeed = (seedSource != null) ? seedSource.worldSeed : 0;
        float[,] biasData = (biasSource != null) ? biasSource.GetBiasData() : null;

        // 1. Raw procedural heights [0..1]
        float[,] heights01 = BaseNoiseGenerator.GenerateHeightmap(
            resW,
            resH,
            currentSeed,
            noiseScale,
            octaves,
            persistence,
            lacunarity,
            biasData,
            biasStrength
        );

        // 2. Post-process (erosion / plateau flattening)
        if (postProcess != null)
        {
            heights01 = postProcess.Process(heights01);
        }

        // 3. Unity wants [y,x]
        float[,] unityHeights = new float[resH, resW];
        for (int y = 0; y < resH; y++)
        {
            for (int x = 0; x < resW; x++)
            {
                unityHeights[y, x] = heights01[x, y];
            }
        }

        tData.SetHeights(0, 0, unityHeights);

        // 4. preview
        lastPreviewTexture = BuildPreviewTexture(resW, resH, heights01);
        lastPreviewTexture.name = "TerrainHeightmapPreview";

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (lastPreviewTexture != null) lastPreviewTexture.Apply();
        UnityEditor.SceneView.RepaintAll();
#endif

        Debug.Log($"TerrainHeightmapComponent: Applied {resW}x{resH} heightmap to Terrain (post-processed).");
    }

    private Texture2D BuildPreviewTexture(int w, int h, float[,] data01)
    {
        Color[] pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v = Mathf.Clamp01(data01[x, y]);
                pixels[y * w + x] = new Color(v, v, v, 1f);
            }
        }

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
