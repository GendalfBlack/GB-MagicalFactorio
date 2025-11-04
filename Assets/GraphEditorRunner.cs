#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.VisualScripting;

[ExecuteAlways]
public class GraphEditorRunner : MonoBehaviour
{
    public ScriptMachine machine;

    [CustomEditor(typeof(GraphEditorRunner))]
    public class GraphEditorRunnerInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var runner = (GraphEditorRunner)target;

            if (GUILayout.Button("Run Graph In Editor"))
            {
                if (runner.machine != null)
                    runner.machine.TriggerUnityEvent("GenerateWorld");
            }
        }
    }
}
#endif
