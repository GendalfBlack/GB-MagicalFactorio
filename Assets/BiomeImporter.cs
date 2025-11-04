using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

//
// DATA MODELS FOR JSON
//

[System.Serializable]
public class BiomeJsonColor
{
    public float r, g, b, a;
}

[System.Serializable]
public class BiomeJsonRecord
{
    public string biomeName;
    public BiomeJsonColor debugColor;

    public float minTemperature01;
    public float maxTemperature01;
    public float minHumidity01;
    public float maxHumidity01;

    public float minHeight01;
    public float maxHeight01;

    public bool requiresSnow;
    public float minSnowMask01;
}

// Wrapper because JsonUtility can't parse a top-level array directly.
// We'll wrap it as { "items": [ ... ] } before parsing.
[System.Serializable]
public class BiomeJsonWrapper
{
    public List<BiomeJsonRecord> items;
}

//
// CUSTOM INSPECTOR
//

// This custom editor runs on TextAsset so you can click your Biomes.json file.
[CustomEditor(typeof(TextAsset))]
public class BiomeImporterEditor : Editor
{
    // Where to drop / refresh generated assets
    // We'll keep everything Unity creates under this folder.
    private static readonly string OutputFolder = "Assets/WorldGen/BiomesGenerated";

    public override void OnInspectorGUI()
    {
        // Draw Unity's default TextAsset inspector first
        base.OnInspectorGUI();

        // We expect this .json to be imported as TextAsset, so this cast is valid here.
        TextAsset ta = (TextAsset)target;

        // Little hint box, purely informational
        if (!ta.name.ToLower().Contains("biome"))
        {
            EditorGUILayout.HelpBox(
                "File name does not contain 'biome', but you can still import.",
                MessageType.Info
            );
        }

        GUILayout.Space(8f);

        // IMPORTANT FIX:
        // Unity's default inspector sometimes leaves GUI.enabled = false.
        // We force-enable it so our button is actually clickable.
        bool prevEnabled = GUI.enabled;
        GUI.enabled = true;

        if (GUILayout.Button("Import Biomes (create/update ScriptableObjects)"))
        {
            ImportBiomesFromJson(ta);
        }

        // restore whatever Unity had before
        GUI.enabled = prevEnabled;
    }

    private void ImportBiomesFromJson(TextAsset ta)
    {
        // Ensure output folder exists ("Assets/WorldGen/BiomesGenerated")
        EnsureOutputFolder();

        // Unity's JsonUtility cannot parse a bare array like: [ { ... }, { ... } ]
        // We'll wrap it into { "items": [ ... ] } before parsing.
        string raw = ta.text.Trim();

        string wrapped = raw.StartsWith("[")
            ? "{ \"items\": " + raw + "}"
            : raw;

        var data = JsonUtility.FromJson<BiomeJsonWrapper>(wrapped);
        if (data == null || data.items == null || data.items.Count == 0)
        {
            Debug.LogError("BiomeImporter: No items parsed from JSON.");
            return;
        }

        foreach (var rec in data.items)
        {
            CreateOrUpdateBiomeAsset(rec);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("BiomeImporter: Imported/Updated " + data.items.Count + " biome(s) into " + OutputFolder);
    }

    private void EnsureOutputFolder()
    {
        // We want Assets/WorldGen/BiomesGenerated.
        // Make sure "Assets/WorldGen" exists, then "Assets/WorldGen/BiomesGenerated".

        if (!AssetDatabase.IsValidFolder("Assets/WorldGen"))
        {
            AssetDatabase.CreateFolder("Assets", "WorldGen");
        }

        if (!AssetDatabase.IsValidFolder("Assets/WorldGen/BiomesGenerated"))
        {
            AssetDatabase.CreateFolder("Assets/WorldGen", "BiomesGenerated");
        }
    }

    private void CreateOrUpdateBiomeAsset(BiomeJsonRecord rec)
    {
        // Try to locate an existing BiomeDefinition with same biomeName
        BiomeDefinition asset = FindExistingBiome(rec.biomeName);

        // If not found, create a new ScriptableObject asset file
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<BiomeDefinition>();

            string safeName = SanitizeFileName(rec.biomeName);
            string assetPath = Path.Combine("Assets/WorldGen/BiomesGenerated", safeName + ".asset");

            AssetDatabase.CreateAsset(asset, assetPath);
        }

        // Record undo for editor niceness, then update fields
        Undo.RecordObject(asset, "Update BiomeDefinition from JSON");

        asset.biomeName = rec.biomeName;
        asset.debugColor = new Color(
            rec.debugColor.r,
            rec.debugColor.g,
            rec.debugColor.b,
            rec.debugColor.a
        );

        asset.minTemperature01 = rec.minTemperature01;
        asset.maxTemperature01 = rec.maxTemperature01;
        asset.minHumidity01 = rec.minHumidity01;
        asset.maxHumidity01 = rec.maxHumidity01;

        asset.minHeight01 = rec.minHeight01;
        asset.maxHeight01 = rec.maxHeight01;

        asset.requiresSnow = rec.requiresSnow;
        asset.minSnowMask01 = rec.minSnowMask01;

        EditorUtility.SetDirty(asset);
    }

    private BiomeDefinition FindExistingBiome(string biomeName)
    {
        // Search all BiomeDefinition assets and match by biomeName field
        string[] guids = AssetDatabase.FindAssets("t:BiomeDefinition");

        for (int i = 0; i < guids.Length; i++)
        {
            string p = AssetDatabase.GUIDToAssetPath(guids[i]);
            var candidate = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(p);
            if (candidate != null && candidate.biomeName == biomeName)
            {
                return candidate;
            }
        }

        return null;
    }

    private string SanitizeFileName(string raw)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            raw = raw.Replace(c.ToString(), "_");
        }
        return raw;
    }
}
