using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HumidityRefiner_MountainDrying))]
public class HumidityRefiner_MountainDryingEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (HumidityRefiner_MountainDrying)target;

        GUILayout.Label("Step 3: Highland / Mountain Drying", EditorStyles.boldLabel);

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

        GUILayout.Space(6);
        GUILayout.Label("Sea Level", EditorStyles.miniBoldLabel);
        comp.seaLevel = EditorGUILayout.Slider("Sea Level", comp.seaLevel, 0f, 1f);

        GUILayout.Space(6);
        GUILayout.Label("Mountain / Plateau Drying", EditorStyles.miniBoldLabel);
        comp.applyMountainDrying = EditorGUILayout.Toggle("Apply Mountain Drying", comp.applyMountainDrying);
        comp.dryStartHeight = EditorGUILayout.Slider("Dry Start Height", comp.dryStartHeight, 0f, 1f);
        comp.dryStrength = EditorGUILayout.Slider("Dry Strength", comp.dryStrength, 0f, 2f);
        comp.dryCurvePower = EditorGUILayout.Slider("Dry Curve Power", comp.dryCurvePower, 0.5f, 5f);

        GUILayout.Space(6);
        GUILayout.Label("Clamp", EditorStyles.miniBoldLabel);
        comp.minHumidityFloor = EditorGUILayout.Slider("Min Humidity Floor", comp.minHumidityFloor, 0f, 1f);

        if (GUILayout.Button("Generate Mountain-Adjusted Humidity"))
        {
            comp.GenerateMountainRefinedHumidity();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture("Final Humidity Preview (after elevation drying)", comp.finalPreviewTexture);

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "This step auto-runs Step 2 (HumidityRefiner_OceanAltitude) if needed.\n" +
            "Drying only applies on land (above seaLevel) and above dryStartHeight.\n" +
            "Use finalHumidity01 for biome logic.",
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
