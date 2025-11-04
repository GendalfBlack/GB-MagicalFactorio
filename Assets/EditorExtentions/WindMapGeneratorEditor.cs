using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WindMapGeneratorComponent))]
public class WindMapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (WindMapGeneratorComponent)target;

        GUILayout.Label("Step 7: Wind Field Generator", EditorStyles.boldLabel);

        comp.mapResolution = EditorGUILayout.IntSlider(
            "Map Resolution",
            comp.mapResolution,
            16, 2048
        );

        comp.terrain = (Terrain)EditorGUILayout.ObjectField(
            "Terrain",
            comp.terrain,
            typeof(Terrain),
            true
        );

        comp.seaLevel = EditorGUILayout.Slider(
            "Sea Level",
            comp.seaLevel,
            0f, 1f
        );

        EditorGUILayout.Space(6);
        GUILayout.Label("Global Circulation", EditorStyles.miniBoldLabel);
        comp.baseWindStrength = EditorGUILayout.Slider(
            "Base Wind Strength",
            comp.baseWindStrength,
            0f, 2f
        );

        EditorGUILayout.Space(6);
        GUILayout.Label("Wind Variation Noise", EditorStyles.miniBoldLabel);
        comp.worldSeedSource = (WorldSeedComponent)EditorGUILayout.ObjectField(
            "World Seed Source",
            comp.worldSeedSource,
            typeof(WorldSeedComponent),
            true
        );

        comp.directionJitter = EditorGUILayout.Slider(
            "Direction Jitter",
            comp.directionJitter,
            0f, 1f
        );

        comp.calmZonesStrength = EditorGUILayout.Slider(
            "Calm Zones Strength",
            comp.calmZonesStrength,
            0f, 1f
        );

        comp.windNoiseScale = EditorGUILayout.Slider(
            "Wind Noise Scale",
            comp.windNoiseScale,
            0f, 100f
        );

        EditorGUILayout.Space(6);
        GUILayout.Label("Terrain Blocking", EditorStyles.miniBoldLabel);
        comp.mountainBlockStrength = EditorGUILayout.Slider(
            "Mountain Block Strength",
            comp.mountainBlockStrength,
            0f, 2f
        );
        comp.mountainBlockHeight = EditorGUILayout.Slider(
            "Mountain Block Height",
            comp.mountainBlockHeight,
            0f, 1f
        );
        comp.ridgeLookDistance = EditorGUILayout.IntSlider(
            "Ridge Look Distance (px)",
            comp.ridgeLookDistance,
            1, 8
        );

        if (GUILayout.Button("Generate Wind Map"))
        {
            comp.GenerateWindMap();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture(
            "Wind Preview (R=X, G=Y, B=speed)",
            comp.windPreviewTexture
        );

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "windVector[y,x] is final near-surface wind after global circulation, " +
            "large-scale wobble, calm pockets, and mountain blocking. " +
            "Use it to move humidity inland and carry warm/cold air for biome steps.",
            MessageType.Info
        );
    }

    void DrawPreviewTexture(string label, Texture2D tex)
    {
        if (tex == null) return;

        GUILayout.Label(label, EditorStyles.miniBoldLabel);

        float maxWidth = 256f;
        float aspect = (tex.height == 0) ? 1f : ((float)tex.height / tex.width);
        Rect r = GUILayoutUtility.GetRect(maxWidth, maxWidth * aspect);
        EditorGUI.DrawPreviewTexture(r, tex, null, ScaleMode.ScaleToFit);
    }
}
