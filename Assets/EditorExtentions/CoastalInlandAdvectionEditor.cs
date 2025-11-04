using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CoastalInlandAdvectionComponent))]
public class CoastalInlandAdvectionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var comp = (CoastalInlandAdvectionComponent)target;

        GUILayout.Label("Step X+1: Inland Coastal Advection", EditorStyles.boldLabel);

        comp.coastalGyreSource = (CoastalGyreWindRefinerComponent)EditorGUILayout.ObjectField(
            "Coastal Gyre Source",
            comp.coastalGyreSource,
            typeof(CoastalGyreWindRefinerComponent),
            true
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
        GUILayout.Label("Propagation Rules", EditorStyles.miniBoldLabel);

        comp.mountainHeightThreshold = EditorGUILayout.Slider(
            "Mountain Height Threshold",
            comp.mountainHeightThreshold,
            0f, 1f
        );

        comp.mountainBlockStrength = EditorGUILayout.Slider(
            "Mountain Block Strength",
            comp.mountainBlockStrength,
            0f, 1f
        );

        comp.maxInlandRangePx = EditorGUILayout.IntSlider(
            "Max Inland Range (px)",
            comp.maxInlandRangePx,
            1, 512
        );

        comp.decayPerStep = EditorGUILayout.Slider(
            "Decay Per Step",
            comp.decayPerStep,
            0f, 1f
        );

        comp.coastInjectStrength = EditorGUILayout.Slider(
            "Coast Inject Strength",
            comp.coastInjectStrength,
            0f, 2f
        );

        comp.finalBlendStrength = EditorGUILayout.Slider(
            "Final Blend Strength",
            comp.finalBlendStrength,
            0f, 1f
        );

        if (GUILayout.Button("Generate Inland Advection"))
        {
            comp.GenerateInlandAdvection();
            EditorUtility.SetDirty(comp);
            Repaint();
        }

        DrawPreviewTexture(
            "Inland Wind Preview (R=X, G=Y, B=speed)",
            comp.inlandWindPreviewTexture
        );

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "This step:\n" +
            "• Finds coastal land cells and injects nearby ocean gyre wind.\n" +
            "• Spreads that influence inland with decay.\n" +
            "• Stops or weakens when it hits mountains.\n" +
            "• Blends result back into the refined wind field.\n\n" +
            "Use inlandWindVector[y,x] as your final near-surface wind for:\n" +
            "  - humidity inland advection\n" +
            "  - coastal temperature bleed\n" +
            "  - rain shadow later.",
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
