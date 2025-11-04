using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VoronoiComponent))]
public class VoronoiComponentEditor : Editor
{
    // For nicer preview spacing
    const float PreviewMaxSize = 256f;

    public override void OnInspectorGUI()
    {
        // Get the target component
        VoronoiComponent comp = (VoronoiComponent)target;

        // Draw default fields first (width, height, etc.)
        DrawDefaultInspector();

        EditorGUILayout.Space(10);

        // Generate button
        if (GUILayout.Button("Generate Voronoi"))
        {
            comp.CreateVoronoiMap();
        }

        EditorGUILayout.Space(10);

        // If we have a texture, preview it
        Texture2D tex = comp.generatedTexture;
        if (tex != null)
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            float w = tex.width;
            float h = tex.height;

            // scale down if huge
            float scale = 1f;
            if (w > PreviewMaxSize || h > PreviewMaxSize)
            {
                float sw = PreviewMaxSize / w;
                float sh = PreviewMaxSize / h;
                scale = Mathf.Min(sw, sh);
            }

            Rect r = GUILayoutUtility.GetRect(w * scale, h * scale, GUILayout.ExpandWidth(false));
            if (Event.current.type == EventType.Repaint)
            {
                // Draw the texture pixels 1:1 (no GUI tint changes)
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, false);
            }

            // optional: show size label
            EditorGUILayout.LabelField($"{tex.width} x {tex.height}, {comp.cellCount} cells");
        }
        else
        {
            EditorGUILayout.HelpBox("No texture generated yet.", MessageType.Info);
        }
    }
}
