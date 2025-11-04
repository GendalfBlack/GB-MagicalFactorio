using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TemperatureMapGeneratorComponent))]
public class TemperatureMapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (TemperatureMapGeneratorComponent)target;

        GUILayout.Label("Step 1: Base Temperature (Latitude Model)", EditorStyles.boldLabel);

        comp.mapResolution = EditorGUILayout.IntSlider("Map Resolution", comp.mapResolution, 16, 2048);

        comp.temperatureFalloff = EditorGUILayout.Slider(
            "Temperature Falloff",
            comp.temperatureFalloff,
            0.5f, 3f
        );

        comp.equatorTemp = EditorGUILayout.Slider(
            "Equator Temp",
            comp.equatorTemp,
            0f, 1f
        );

        comp.poleTemp = EditorGUILayout.Slider(
            "Pole Temp",
            comp.poleTemp,
            0f, 1f
        );

        if (GUILayout.Button("Generate Base Temperature"))
        {
            comp.GenerateBaseTemperature();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture("Base Temperature Preview", comp.basePreviewTexture);
    }

    void DrawPreviewTexture(string label, Texture2D tex)
    {
        if (tex == null) return;

        GUILayout.Label(label, EditorStyles.miniBoldLabel);

        float maxWidth = 256f;
        float aspect = (float)tex.height / tex.width;
        Rect r = GUILayoutUtility.GetRect(maxWidth, maxWidth * aspect);
        EditorGUI.DrawPreviewTexture(r, tex, null, ScaleMode.ScaleToFit);
    }
}
