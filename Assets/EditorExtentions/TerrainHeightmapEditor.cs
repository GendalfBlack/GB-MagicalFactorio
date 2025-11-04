#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainHeightmapComponent))]
public class TerrainHeightmapEditor : Editor
{
    public override void OnInspectorGUI()
    {
        TerrainHeightmapComponent comp = (TerrainHeightmapComponent)target;

        // стандартні поля
        DrawDefaultInspector();

        EditorGUILayout.Space();

        // прев'ю
        Texture2D preview = comp.lastPreviewTexture;
        if (preview != null)
        {
            EditorGUILayout.LabelField("Last Generated Preview", EditorStyles.boldLabel);

            float maxSize = 128f;
            float aspect = preview.width / (float)preview.height;
            Rect r;
            if (aspect >= 1f)
            {
                r = GUILayoutUtility.GetRect(maxSize, maxSize / aspect, GUILayout.ExpandWidth(false));
            }
            else
            {
                r = GUILayoutUtility.GetRect(maxSize * aspect, maxSize, GUILayout.ExpandWidth(false));
            }
            EditorGUI.DrawPreviewTexture(r, preview, null, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Heightmap now"))
        {
            comp.GenerateNow();
        }
    }
}
#endif
