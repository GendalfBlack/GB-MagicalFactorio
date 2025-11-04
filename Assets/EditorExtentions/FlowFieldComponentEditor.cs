#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlateTectonicsComponent))]
public class FlowFieldComponentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // малюємо стандартні поля
        DrawDefaultInspector();

        var comp = (PlateTectonicsComponent)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Generate Flow Field"))
        {
            comp.CreateFlowDirectionsOnVoronoiMap();
            // позначимо об'єкт як змінений, щоб Unity дозволила зберегти сцену
            EditorUtility.SetDirty(comp);

            if (comp.debugTexture != null)
            {
                // теж помітимо текстуру як asset-dirty якщо треба зберегти в проект
                comp.debugTexture.Apply();
            }
        }

        GUILayout.Space(10);

        // прев'ю текстури
        if (comp.debugTexture != null)
        {
            GUILayout.Label("Debug Preview:");
            float maxSize = 256f;
            float w = comp.debugTexture.width;
            float h = comp.debugTexture.height;
            float scale = Mathf.Min(maxSize / w, maxSize / h);

            Rect r = GUILayoutUtility.GetRect(w * scale, h * scale, GUILayout.ExpandWidth(false));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, comp.debugTexture, ScaleMode.ScaleToFit, false);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No debugTexture yet. Press 'Generate Flow Field'.", MessageType.Info);
        }
    }
}
#endif
