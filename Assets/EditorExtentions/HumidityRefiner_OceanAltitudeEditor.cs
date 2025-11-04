using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HumidityRefiner_OceanAltitude))]
public class HumidityRefiner_OceanAltitudeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (HumidityRefiner_OceanAltitude)target;

        GUILayout.Label("Step 2: Refined Humidity (Ocean + Altitude)", EditorStyles.boldLabel);

        comp.mapResolution = EditorGUILayout.IntSlider("Map Resolution", comp.mapResolution, 16, 2048);
        comp.terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", comp.terrain, typeof(Terrain), true);

        GUILayout.Space(4);
        GUILayout.Label("Elevation / Sea", EditorStyles.miniBoldLabel);
        comp.seaLevel = EditorGUILayout.Slider("Sea Level", comp.seaLevel, 0f, 1f);

        comp.altitudeDrying = EditorGUILayout.Slider("Altitude Drying", comp.altitudeDrying, 0f, 2f);
        comp.applyAltitudeDrying = EditorGUILayout.Toggle("Apply Altitude Drying", comp.applyAltitudeDrying);

        GUILayout.Space(4);
        GUILayout.Label("Ocean & Coast Influence", EditorStyles.miniBoldLabel);
        comp.applyOceanInfluence = EditorGUILayout.Toggle("Apply Ocean Influence", comp.applyOceanInfluence);

        comp.oceanSaturation = EditorGUILayout.Slider(
            new GUIContent("Ocean Saturation (target)",
                "Humid value that open ocean tends toward."),
            comp.oceanSaturation, 0f, 1f);

        comp.oceanWaterBlend = EditorGUILayout.Slider(
            new GUIContent("Ocean Water Blend",
                "How much oceans are pulled toward saturation before bleeding inland."),
            comp.oceanWaterBlend, 0f, 1f);

        comp.coastRangePixels = EditorGUILayout.IntSlider(
            new GUIContent("Coast Range (px)",
                "Distance inland (pixels at current resolution) where moist air still matters."),
            comp.coastRangePixels, 0, 200);

        comp.coastalBlendStrength = EditorGUILayout.Slider(
            new GUIContent("Coastal Blend Strength",
                "How strongly coast pulls toward marine humidity."),
            comp.coastalBlendStrength, 0f, 1f);

        GUILayout.Space(6);
        if (GUILayout.Button("Generate Refined Humidity"))
        {
            comp.GenerateRefinedHumidity();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture("Refined Humidity Preview", comp.refinedPreviewTexture);

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "Step 1 (HumidityMapGeneratorComponent) gives base humidity (Hadley cells, etc.).\n" +
            "This step:\n" +
            "- Makes oceans very humid\n" +
            "- Bleeds that humid air inland by distance to coast\n" +
            "- Dries air over altitude (rain shadow / mountains).",
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
