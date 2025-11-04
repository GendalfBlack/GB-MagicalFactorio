using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TemperatureRefiner_MountainCooling))]
public class TemperatureRefiner_MountainCoolingEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (TemperatureRefiner_MountainCooling)target;

        GUILayout.Label("Step 3: Mountain Cooling & Snowcaps", EditorStyles.boldLabel);

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
        GUILayout.Label("Mountain Cooling", EditorStyles.miniBoldLabel);
        comp.applyMountainCooling = EditorGUILayout.Toggle("Apply Mountain Cooling", comp.applyMountainCooling);
        comp.mountainStartHeight = EditorGUILayout.Slider("Mountain Start Height", comp.mountainStartHeight, 0f, 1f);
        comp.mountainCoolingStrength = EditorGUILayout.Slider("Cooling Strength", comp.mountainCoolingStrength, 0f, 2f);
        comp.mountainCoolingCurve = EditorGUILayout.Slider("Cooling Curve Power", comp.mountainCoolingCurve, 0.5f, 5f);

        GUILayout.Space(6);
        GUILayout.Label("Snowcaps", EditorStyles.miniBoldLabel);
        comp.applySnowCaps = EditorGUILayout.Toggle("Apply Snow Caps", comp.applySnowCaps);
        comp.snowCapHeight = EditorGUILayout.Slider("Snow Cap Height", comp.snowCapHeight, 0f, 1f);
        comp.snowCapTemp = EditorGUILayout.Slider("Snow Cap Temp", comp.snowCapTemp, 0f, 1f);
        comp.snowCapBlend = EditorGUILayout.Slider("Snow Cap Blend", comp.snowCapBlend, 0f, 1f);

        if (GUILayout.Button("Generate Mountain-Adjusted Temperature"))
        {
            comp.GenerateMountainRefinedTemperature();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture("Final Temperature Preview (after mountains/snow)", comp.finalPreviewTexture);

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "This step auto-runs Step 2 if needed.\n" +
            "Use snowMask01 later to mark permanent snow biomes.",
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
