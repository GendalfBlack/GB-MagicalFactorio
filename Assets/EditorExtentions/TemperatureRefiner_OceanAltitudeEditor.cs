using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TemperatureRefiner_OceanAltitude))]
public class TemperatureRefiner_OceanAltitudeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (TemperatureRefiner_OceanAltitude)target;

        GUILayout.Label("Step 2: Refined Temperature (Altitude + Ocean)", EditorStyles.boldLabel);

        comp.mapResolution = EditorGUILayout.IntSlider("Map Resolution", comp.mapResolution, 16, 2048);

        comp.terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", comp.terrain, typeof(Terrain), true);

        GUILayout.Space(4);
        GUILayout.Label("Elevation / Sea", EditorStyles.miniBoldLabel);
        comp.seaLevel = EditorGUILayout.Slider("Sea Level", comp.seaLevel, 0f, 1f);
        comp.altitudeCooling = EditorGUILayout.Slider("Altitude Cooling", comp.altitudeCooling, 0f, 2f);
        comp.applyAltitudeCooling = EditorGUILayout.Toggle("Apply Altitude Cooling", comp.applyAltitudeCooling);

        GUILayout.Space(4);
        GUILayout.Label("Ocean Moderation", EditorStyles.miniBoldLabel);
        comp.applyOceanModeration = EditorGUILayout.Toggle("Apply Ocean Moderation", comp.applyOceanModeration);
        comp.coastRangePixels = EditorGUILayout.IntSlider("Coast Range", comp.coastRangePixels, 0, 100);
        comp.coastalBlendStrength = EditorGUILayout.Slider("Ocean Influence", comp.coastalBlendStrength, 0f, 1f);

        if (GUILayout.Button("Generate Refined Temperature"))
        {
            comp.GenerateRefinedTemperature();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture("Refined Temperature Preview", comp.refinedPreviewTexture);

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "This component automatically calls Step 1 on the same GameObject if needed. " +
            "So you can just click this button and you should always get output.",
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
