#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ElevationBiasComponent))]
public class ElevationBiasEditor : Editor
{
    private Vector2 scroll;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate Now"))
        {
            var comp = (ElevationBiasComponent)target;
            comp.GenerateNow();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bias Preview", EditorStyles.boldLabel);

        var c = (ElevationBiasComponent)target;
        var tex = c.biasPreviewTexture;

        if (tex != null)
        {
            float maxPreview = 256f;
            float aspect = (float)tex.width / Mathf.Max(1f, tex.height);
            float drawW = maxPreview;
            float drawH = maxPreview / Mathf.Max(0.0001f, aspect);

            Rect r = GUILayoutUtility.GetRect(drawW, drawH, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(r, tex, null, ScaleMode.ScaleToFit);

            EditorGUILayout.HelpBox(
                $"Texture: {tex.width} x {tex.height}\n" +
                "White = high, Black = low (after previewMid/previewScale mapping)",
                MessageType.None
            );
        }
        else
        {
            EditorGUILayout.HelpBox("No biasPreviewTexture yet. Click Generate Now.", MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sample bias values", EditorStyles.boldLabel);

        var data = c.biasData;
        if (data != null)
        {
            // just show a tiny sample center 5x5 so we don't spam the inspector
            int w = data.GetLength(0);
            int h = data.GetLength(1);

            int cx = w / 2;
            int cy = h / 2;

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120));
            for (int yy = -2; yy <= 2; yy++)
            {
                string line = "";
                for (int xx = -2; xx <= 2; xx++)
                {
                    int px = Mathf.Clamp(cx + xx, 0, w - 1);
                    int py = Mathf.Clamp(cy + yy, 0, h - 1);
                    float v = data[px, py];
                    line += v.ToString("0.00").PadLeft(6);
                }
                EditorGUILayout.LabelField(line);
            }
            EditorGUILayout.EndScrollView();
        }
        else
        {
            EditorGUILayout.HelpBox("No biasData yet.", MessageType.None);
        }
    }
}
#endif
