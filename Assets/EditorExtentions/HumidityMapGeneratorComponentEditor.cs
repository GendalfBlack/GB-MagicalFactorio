using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HumidityMapGeneratorComponent))]
public class HumidityMapGeneratorComponentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // draw default fields
        DrawDefaultInspector();

        var comp = (HumidityMapGeneratorComponent)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Base Humidity"))
        {
            comp.GenerateBaseHumidity();

            // mark dirty so Unity knows the texture updated in edit mode
            EditorUtility.SetDirty(comp);
            if (comp.basePreviewTexture != null)
                EditorUtility.SetDirty(comp.basePreviewTexture);
        }

        // preview
        if (comp.basePreviewTexture != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            var tex = comp.basePreviewTexture;
            if (tex == null) return;

            float maxWidth = 256f;
            float w = tex.width;
            float h = tex.height;
            float aspect = (tex.height == 0) ? 1f : ((float)tex.height / tex.width);

            Rect r = GUILayoutUtility.GetRect(maxWidth, maxWidth * aspect);
            EditorGUI.DrawPreviewTexture(r, tex, null, ScaleMode.ScaleToFit);
        }
    }
}
