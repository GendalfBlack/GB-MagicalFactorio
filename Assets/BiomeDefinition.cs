using UnityEngine;

[CreateAssetMenu(
    fileName = "NewBiome",
    menuName = "WorldGen/Biome Definition",
    order = 0)]
public class BiomeDefinition : ScriptableObject
{
    [Header("Debug / Identity")]
    public string biomeName = "Temperate Forest";
    public Color debugColor = Color.green;

    [Header("Climate Ranges (0..1)")]
    [Range(0f, 1f)] public float minTemperature01 = 0.2f;
    [Range(0f, 1f)] public float maxTemperature01 = 0.8f;
    [Range(0f, 1f)] public float minHumidity01 = 0.3f;
    [Range(0f, 1f)] public float maxHumidity01 = 0.9f;

    [Header("Elevation range (0..1 world height)")]
    [Tooltip("Inclusive. Use [0,1] for 'any land'. Use [0.6,1] for mountain-only biomes.")]
    [Range(0f, 1f)] public float minHeight01 = 0.0f;
    [Range(0f, 1f)] public float maxHeight01 = 1.0f;

    [Header("Snow / frost gating")]
    public bool requiresSnow = false;
    [Range(0f, 1f)] public float minSnowMask01 = 0.5f;

    public bool Matches(float temp01, float hum01, float snow01, float height01)
    {
        if (temp01 < minTemperature01 || temp01 > maxTemperature01) return false;
        if (hum01 < minHumidity01 || hum01 > maxHumidity01) return false;
        if (height01 < minHeight01 || height01 > maxHeight01) return false;

        if (requiresSnow && snow01 < minSnowMask01) return false;

        return true;
    }
}
