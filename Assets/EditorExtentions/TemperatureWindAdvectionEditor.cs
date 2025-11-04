using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TemperatureWindAdvectionComponent))]
public class TemperatureWindAdvectionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (TemperatureWindAdvectionComponent)target;

        GUILayout.Label("Step: Wind Advection on Temperature", EditorStyles.boldLabel);

        // Inputs
        comp.windSource = (WindFieldSmootherComponent)EditorGUILayout.ObjectField(
            "Wind Source",
            comp.windSource,
            typeof(WindFieldSmootherComponent),
            true
        );

        comp.temperatureSource = (TemperatureRefiner_MountainCooling)EditorGUILayout.ObjectField(
            "Temperature Source",
            comp.temperatureSource,
            typeof(TemperatureRefiner_MountainCooling),
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

        comp.globalTempoffset = EditorGUILayout.Slider(
            "Global Temp Offset",
            comp.globalTempoffset,
            -0.5f, 0.5f
        );

        GUILayout.Space(8);

        if (GUILayout.Button("Generate Wind-Adjusted Temperature"))
        {
            comp.GenerateWindAdjustedTemperature();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture("Wind-Adjusted Temperature Preview", comp.windAdjustedPreviewTexture);

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "This step pulls temperature from 'upwind' and blends it in.\n" +
            "Stronger winds = stronger transport.\n" +
            "Result goes to windAdjustedTemperature01.",
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
