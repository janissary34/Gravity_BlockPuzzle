using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    [CustomEditor(typeof(GravityLevelSequence))]
    public sealed class GravityLevelSequenceEditor : UnityEditor.Editor
    {
        private ReorderableList levelList;

        private void OnEnable()
        {
            SerializedProperty levels = serializedObject.FindProperty("levels");
            levelList = new ReorderableList(serializedObject, levels, true, true, true, true);
            levelList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Play Order (top to bottom)");
            levelList.elementHeight = EditorGUIUtility.singleLineHeight + 6f;
            levelList.drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 3f;
                rect.height = EditorGUIUtility.singleLineHeight;
                SerializedProperty level = levels.GetArrayElementAtIndex(index);
                EditorGUI.PropertyField(rect, level, new GUIContent($"Level {index + 1}"));
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "Drag the handles to set the order. The game starts at the top and advances downward.",
                MessageType.Info);
            levelList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
