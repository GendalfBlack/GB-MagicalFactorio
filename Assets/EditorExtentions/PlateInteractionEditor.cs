#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PlateInteractionComponent))]
public class PlateInteractionEditor : Editor
{
    private Vector2 scrollPos;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate Now"))
        {
            var comp = (PlateInteractionComponent)target;
            comp.GenerateNow();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview (Interaction Map)", EditorStyles.boldLabel);

        var compRef = (PlateInteractionComponent)target;
        var tex = compRef.interactionPreviewTexture;

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
            EditorGUILayout.HelpBox("No interactionPreviewTexture yet. Click Generate Now.", MessageType.Info);
        }

        //EditorGUILayout.Space();
        //EditorGUILayout.LabelField("Detected Interactions (unique boundaries)", EditorStyles.boldLabel);

        //var list = compRef.interactionsDebug;
        //if (list != null && list.Count > 0)
        //{
        //    int showCount = Mathf.Min(list.Count, 50);

        //    scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(220));
        //    for (int i = 0; i < showCount; i++)
        //    {
        //        var e = list[i];
        //        EditorGUILayout.LabelField(
        //            $"#{i}: R{e.regionA}({e.plateA}) ↔ R{e.regionB}({e.plateB}) " +
        //            $"[{e.interaction}] " +
        //            $"dirA=({e.dirA.x:F2},{e.dirA.y:F2}) dirB=({e.dirB.x:F2},{e.dirB.y:F2})"
        //        );
        //    }

        //    if (list.Count > showCount)
        //    {
        //        EditorGUILayout.LabelField($"... and {list.Count - showCount} more ...");
        //    }

        //    EditorGUILayout.EndScrollView();
        //}
        //else
        //{
        //    EditorGUILayout.HelpBox("No interactions detected.", MessageType.None);
        //}
    }
}
#endif
