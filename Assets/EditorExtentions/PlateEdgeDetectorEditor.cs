#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PlateEdgeDetectorComponent))]
public class PlateEdgeDetectorEditor : Editor
{
    private Vector2 scrollPos;

    public override void OnInspectorGUI()
    {
        // Draw default fields first
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate Now"))
        {
            var comp = (PlateEdgeDetectorComponent)target;
            comp.GenerateNow();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        var detector = (PlateEdgeDetectorComponent)target;
        var tex = detector.edgePreviewTexture;

        if (tex != null)
        {
            float maxPreview = 256f;
            float aspect = (float)tex.width / Mathf.Max(1f, tex.height);
            float drawW = maxPreview;
            float drawH = maxPreview / Mathf.Max(0.0001f, aspect);

            Rect r = GUILayoutUtility.GetRect(drawW, drawH, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(r, tex, null, ScaleMode.ScaleToFit);
            EditorGUILayout.HelpBox(
                $"Texture: {tex.width} x {tex.height}",
                MessageType.None
            );
        }
        else
        {
            EditorGUILayout.HelpBox("No edgePreviewTexture yet. Click Generate Now.", MessageType.Info);
        }

        //EditorGUILayout.Space();
        //EditorGUILayout.LabelField("Edges Debug (unique plate boundaries)", EditorStyles.boldLabel);

        //var list = detector.edgesDebug;
        //if (list != null && list.Count > 0)
        //{
        //    // Scrollable box so we don't explode inspector
        //    int showCount = Mathf.Min(list.Count, 50); // just not to spam 1000+ lines
        //    scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
        //    for (int i = 0; i < showCount; i++)
        //    {
        //        var e = list[i];
        //        EditorGUILayout.LabelField(
        //            $"#{i}: R{e.regionA}({e.plateA}) <-> R{e.regionB}({e.plateB}) " +
        //            (e.IsDifferentPlateType ? "[TECTONIC]" : "[internal]"));
        //    }

        //    if (list.Count > showCount)
        //    {
        //        EditorGUILayout.LabelField($"... and {list.Count - showCount} more ...");
        //    }

        //    EditorGUILayout.EndScrollView();
        //}
        //else
        //{
        //    EditorGUILayout.HelpBox("No edges found yet.", MessageType.None);
        //}
    }
}
#endif
