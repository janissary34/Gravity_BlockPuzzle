using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.EditorTools
{
    public static class ShredderPrefabAuthoring
    {
        private const string PrefabFolder = "Assets/Prefabs";
        private const string PrefabPath = PrefabFolder + "/ShredderWheel.prefab";

        [MenuItem("Gravity Puzzle/Refactor/Create Shredder Wheel Prefab")]
        private static void CreatePrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                Debug.Log("[ShredderPrefab] ShredderWheel.prefab already exists.");
                return;
            }

            GameObject root = new GameObject("ShredderWheel");
            try
            {
                ShredderWheel wheel = root.AddComponent<ShredderWheel>();
                wheel.Build(1f, 0f);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Selection.activeObject = prefab;
                Debug.Log("[ShredderPrefab] Created Assets/Prefabs/ShredderWheel.prefab. Edit its Disc, Hub, and Tooth children before Play.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
