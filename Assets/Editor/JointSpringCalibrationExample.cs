using GravityPuzzle.Config;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    // An isolated authoring reference, not a gameplay object or a runtime calibration source.
    internal static class JointSpringCalibrationExample
    {
        private const string ConfigPath = "Assets/Config/Presentation/JointAnchorConfig.asset";
        private const string SpringPath = "Assets/Prefabs/JointSpringView.prefab";

        [MenuItem("Gravity Puzzle/Presentation/Create Spring Calibration Example")]
        private static void CreateExample()
        {
            var config = AssetDatabase.LoadAssetAtPath<JointAnchorConfig>(ConfigPath);
            var springPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpringPath);
            if (config == null || springPrefab == null)
            {
                Debug.LogWarning("Spring calibration: JointAnchorConfig or JointSpringView prefab is missing.");
                return;
            }

            var data = new SerializedObject(config);
            var layouts = data.FindProperty("layouts");
            var block = layouts.arraySize > 0
                ? layouts.GetArrayElementAtIndex(0).FindPropertyRelative("sprite").objectReferenceValue as Sprite
                : null;
            if (block == null || block.bounds.size.x <= 0f || block.bounds.size.y <= 0f)
            {
                Debug.LogWarning("Spring calibration: assign a block sprite to the first anchor layout before creating the example.");
                return;
            }

            var root = new GameObject("Spring Calibration Example (Editor Only)");
            Undo.RegisterCreatedObjectUndo(root, "Create spring calibration example");
            root.tag = "EditorOnly";
            root.transform.position = new Vector3(30f, 0f, 0f);
            CreateBlock(root.transform, block, "Left Block", -0.5f, "Assets/Materials/M_Orange.mat");
            CreateBlock(root.transform, block, "Right Block", 0.5f, "Assets/Materials/M_GreenLight.mat");

            var spring = (GameObject)PrefabUtility.InstantiatePrefab(springPrefab, root.transform);
            spring.name = "Adjust This Spring";
            spring.transform.localPosition = new Vector3(0f, 0.15f, -0.15f);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        }

        private static void CreateBlock(Transform parent, Sprite sprite, string name, float x, string materialPath)
        {
            var block = new GameObject(name);
            block.transform.SetParent(parent, false);
            block.transform.localPosition = new Vector3(x, 0f, 0f);
            block.transform.localScale = new Vector3(1f / sprite.bounds.size.x, 1f / sprite.bounds.size.y, 1f);
            var renderer = block.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            renderer.sortingOrder = 5;
        }
    }
}
