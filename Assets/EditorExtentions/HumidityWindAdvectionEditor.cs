using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HumidityWindAdvectionComponent))]
public class HumidityWindAdvectionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (HumidityWindAdvectionComponent)target;

        GUILayout.Label("Step: Wind Advection on Humidity", EditorStyles.boldLabel);

        comp.windSource = (WindFieldSmootherComponent)EditorGUILayout.ObjectField(
            "Wind Source",
            comp.windSource,
            typeof(WindFieldSmootherComponent),
            true
        );

        comp.humiditySource = (HumidityRefiner_MountainDrying)EditorGUILayout.ObjectField(
            "Humidity Source",
            comp.humiditySource,
            typeof(HumidityRefiner_MountainDrying),
            true
        );

        GUILayout.Space(6);

        comp.mapResolution = EditorGUILayout.IntSlider(
            "Map Resolution",
            comp.mapResolution,
            16, 2048
        );

        GUILayout.Space(6);
        GUILayout.Label("Advection Settings", EditorStyles.miniBoldLabel);

        comp.baseShiftPixels = EditorGUILayout.Slider(
            "Base Shift (px)",
            comp.baseShiftPixels,
            0f, 64f
        );

        comp.windSpeedPower = EditorGUILayout.Slider(
            "Wind Speed Power",
            comp.windSpeedPower,
            0.1f, 3f
        );

        comp.advectionBlend = EditorGUILayout.Slider(
            "Advection Blend",
            comp.advectionBlend,
            0f, 1f
        );

        comp.globalHumidityOffset = EditorGUILayout.Slider(
            "Global Humidity Offset",
            comp.globalHumidityOffset,
            -0.5f, 0.5f
        );

        GUILayout.Space(6);
        GUILayout.Label("Clamp / Floors", EditorStyles.miniBoldLabel);

        comp.minHumidityFloor = EditorGUILayout.Slider(
            "Min Humidity Floor",
            comp.minHumidityFloor,
            0f, 1f
        );

        comp.maxHumidityCeil = EditorGUILayout.Slider(
            "Max Humidity Ceil",
            comp.maxHumidityCeil,
            0f, 1f
        );

        GUILayout.Space(8);

        if (GUILayout.Button("Generate Wind-Adjusted Humidity"))
        {
            comp.GenerateWindAdjustedHumidity();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture("Wind-Adjusted Humidity Preview", comp.windAdjustedPreviewTexture);

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "This step transports moisture along your wind field.\n" +
            "Result goes to windAdjustedHumidity01.\n" +
            "Use this to drive biome wetness / rainfall distribution.",
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
