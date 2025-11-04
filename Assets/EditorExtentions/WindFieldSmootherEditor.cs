using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WindFieldSmootherComponent))]
public class WindFieldSmootherEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (WindFieldSmootherComponent)target;

        GUILayout.Label("Step X+2: Wind Field Smoother", EditorStyles.boldLabel);

        comp.inlandSource = (CoastalInlandAdvectionComponent)EditorGUILayout.ObjectField(
            "Inland Source",
            comp.inlandSource,
            typeof(CoastalInlandAdvectionComponent),
            true
        );

        EditorGUILayout.Space(6);
        GUILayout.Label("Base Smoothing", EditorStyles.miniBoldLabel);

        comp.smoothRadius = EditorGUILayout.IntSlider(
            "Smooth Radius (px)",
            comp.smoothRadius,
            1, 50
        );

        comp.smoothBlend = EditorGUILayout.Slider(
            "Base Smooth Blend",
            comp.smoothBlend,
            0f, 1f
        );

        comp.iterations = EditorGUILayout.IntSlider(
            "Iterations",
            comp.iterations,
            1, 5
        );

        EditorGUILayout.Space(6);
        GUILayout.Label("Adaptive Edge Blending", EditorStyles.miniBoldLabel);

        comp.edgeAdaptive = EditorGUILayout.Toggle(
            "Enable Adaptive",
            comp.edgeAdaptive
        );

        using (new EditorGUI.DisabledScope(!comp.edgeAdaptive))
        {
            comp.edgeBoost = EditorGUILayout.Slider(
                "Edge Boost",
                comp.edgeBoost,
                0f, 1f
            );

            comp.edgeSensitivity = EditorGUILayout.FloatField(
                new GUIContent("Edge Sensitivity", "Lower = we treat smaller jumps as 'harsh edges'"),
                comp.edgeSensitivity
            );
        }

        EditorGUILayout.Space(6);

        if (GUILayout.Button("Generate Smoothed Wind"))
        {
            comp.GenerateSmoothedWind();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture(
            "Smoothed Wind Preview (R=X, G=Y, B=speed)",
            comp.smoothedPreviewTexture
        );

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "How it works:\n" +
            "• We blur X and Y vectors.\n" +
            "• We blend original→blurred.\n" +
            "• Adaptive mode: pixels sitting on brutal seams get EXTRA smoothing.\n" +
            "  - edgeSensitivity: how fast we call something 'brutal'.\n" +
            "  - edgeBoost: how much extra smoothing there.\n\n" +
            "Use finalWindVector[y,x] as the ultimate low-alt wind field.",
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
