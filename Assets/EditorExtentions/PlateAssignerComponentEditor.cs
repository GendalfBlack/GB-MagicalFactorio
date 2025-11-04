#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlateAssignerComponent))]
public class PlateAssignerComponentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // малюємо всі публічні поля як є
        DrawDefaultInspector();

        var comp = (PlateAssignerComponent)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Assign Plates Now"))
        {
            comp.CreatePlateTypesOnVoronoiMap();
        }

        EditorGUILayout.HelpBox(
            "1. На VoronoiComponent натисни GenerateNow.\n" +
            "2. Потім тут натисни Assign Plates Now.\n" +
            "Текстура нижче показує типи плит за кольорами.",
            MessageType.Info
        );

        // прев’ю текстури плит
        if (comp.platePreviewTexture != null)
        {
            EditorGUILayout.LabelField("Plate Preview:", EditorStyles.boldLabel);

            float maxWidth = 256f;
            Texture2D tex = comp.platePreviewTexture;

            // співвідношення сторін
            float aspect = (float)tex.height / tex.width;
            Rect r = GUILayoutUtility.GetRect(
                maxWidth,
                maxWidth * aspect,
                GUILayout.ExpandWidth(false)
            );

            // малюємо текстуру всередині прямокутника
            if (Event.current.type == EventType.Repaint)
            {
                // ми не хочемо щоб інспектор тягнув текстуру як іконку, тому використовуєм DrawPreviewTexture
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, false);
            }
        }
        else
        {
            EditorGUILayout.LabelField("No plate preview texture yet.");
        }
    }
}
#endif
