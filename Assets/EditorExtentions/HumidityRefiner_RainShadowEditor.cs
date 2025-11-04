using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HumidityRefiner_RainShadow))]
public class HumidityRefiner_RainShadowEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (HumidityRefiner_RainShadow)target;

        GUILayout.Label("Step 4: Rain Shadow Simulation", EditorStyles.boldLabel);

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
        GUILayout.Label("Sea / Land", EditorStyles.miniBoldLabel);
        comp.seaLevel = EditorGUILayout.Slider("Sea Level", comp.seaLevel, 0f, 1f);

        GUILayout.Space(6);
        GUILayout.Label("Wind Model", EditorStyles.miniBoldLabel);
        comp.windDirection = (HumidityRefiner_RainShadow.WindDirection)
            EditorGUILayout.EnumPopup("Wind Direction", comp.windDirection);

        comp.oceanRecharge = EditorGUILayout.Slider(
            "Ocean Recharge",
            comp.oceanRecharge,
            0f, 1f
        );

        comp.startingAirMoisture = EditorGUILayout.Slider(
            "Starting Air Moisture",
            comp.startingAirMoisture,
            0f, 2f
        );

        GUILayout.Space(6);
        GUILayout.Label("Orographic Rainout", EditorStyles.miniBoldLabel);

        comp.ridgeSensitivity = EditorGUILayout.Slider(
            "Ridge Sensitivity",
            comp.ridgeSensitivity,
            0f, 5f
        );

        comp.windwardRainBoost = EditorGUILayout.Slider(
            "Windward Rain Boost",
            comp.windwardRainBoost,
            0f, 2f
        );

        comp.leewardDryLoss = EditorGUILayout.Slider(
            "Leeward Dry Loss",
            comp.leewardDryLoss,
            0f, 2f
        );

        GUILayout.Space(6);
        GUILayout.Label("Shadow Persistence", EditorStyles.miniBoldLabel);

        comp.shadowPersistence = EditorGUILayout.Slider(
            "Shadow Persistence",
            comp.shadowPersistence,
            0f, 1f
        );

        GUILayout.Space(6);
        GUILayout.Label("Clamp", EditorStyles.miniBoldLabel);

        comp.minHumidityFloor = EditorGUILayout.Slider(
            "Min Humidity Floor",
            comp.minHumidityFloor,
            0f, 1f
        );

        comp.maxHumidityCeiling = EditorGUILayout.Slider(
            "Max Humidity Ceiling",
            comp.maxHumidityCeiling,
            0f, 1f
        );

        if (GUILayout.Button("Generate Rain Shadow Humidity"))
        {
            comp.GenerateRainShadowHumidity();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture("Rain Shadow Humidity Preview", comp.rainShadowPreviewTexture);

        //EditorGUILayout.Space(12);
        //EditorGUILayout.HelpBox(
        //    "This step simulates moist air moving across the world.\n" +
        //    "- When air hits a ridge, that cell gets wetter (rainout).\n" +
        //    "- After crossing the ridge, the air is drier, creating a 'rain shadow' behind mountains.\n" +
        //    "- Ocean tiles recharge the moving air with moisture.\n\n" +
        //    "Result goes into rainShadowHumidity01 and should be used as final humidity for biome generation.",
        //    MessageType.Info
        //);
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
