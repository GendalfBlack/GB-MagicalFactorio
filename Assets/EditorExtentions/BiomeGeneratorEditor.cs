using UnityEngine;
using UnityEditor;
using System.Linq;

[CustomEditor(typeof(BiomeGeneratorComponent))]
public class BiomeGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var gen = (BiomeGeneratorComponent)target;

        // draw normal serialized fields
        DrawDefaultInspector();

        GUILayout.Space(8f);

        // --- AUTO-LOAD BIOMES SECTION ---
        EditorGUILayout.LabelField("Biome Library Utilities", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Loads all BiomeDefinition assets from the assigned folder into the 'biomes' list.\n" +
            "This is meant to make it easy to refresh from an external / shared biome library.",
            MessageType.Info
        );

        using (new EditorGUI.DisabledScope(gen.biomeFolder == null))
        {
            if (GUILayout.Button("Load Biomes From Folder"))
            {
                LoadBiomesFromFolder(gen);
            }
        }

        GUILayout.Space(10f);

        // --- GENERATE MAP BUTTON ---
        if (GUILayout.Button("Generate Biome Map"))
        {
            gen.GenerateBiomeMap();

            EditorUtility.SetDirty(gen);
            if (gen.biomePreviewTexture != null)
            {
                EditorUtility.SetDirty(gen.biomePreviewTexture);
            }
        }

        // --- LIVE PREVIEW ---
        GUILayout.Space(10f);
        if (gen != null && gen.biomePreviewTexture != null)
        {
            GUILayout.Label("Biome Preview:");
            float w = 256f;
            float h = 256f;
            Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, gen.biomePreviewTexture, ScaleMode.ScaleToFit, false);
            }
        }
    }

    private void LoadBiomesFromFolder(BiomeGeneratorComponent gen)
    {
        // Safety
        if (gen.biomeFolder == null)
        {
            Debug.LogWarning("BiomeGenerator: No biomeFolder assigned.");
            return;
        }

        // Resolve folder path on disk
        string folderPath = AssetDatabase.GetAssetPath(gen.biomeFolder);
        if (string.IsNullOrEmpty(folderPath))
        {
            Debug.LogWarning("BiomeGenerator: biomeFolder has no valid AssetDatabase path.");
            return;
        }

        // Find all assets of type BiomeDefinition in that folder (non-recursive or recursive; let's do recursive)
        string[] guids = AssetDatabase.FindAssets("t:BiomeDefinition", new[] { folderPath });

        // Load them
        var biomeList = guids
            .Select(guid =>
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                return AssetDatabase.LoadAssetAtPath<BiomeDefinition>(path);
            })
            .Where(b => b != null)
            .ToList();

        // Optional: sort by name for deterministic order in array
        biomeList.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        gen.biomes = biomeList.ToArray();

        // mark dirty so Unity knows we changed serialized data
        EditorUtility.SetDirty(gen);

        Debug.Log($"BiomeGenerator: Loaded {gen.biomes.Length} biome(s) from '{folderPath}'.");
    }
}
