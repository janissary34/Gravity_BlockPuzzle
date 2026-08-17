using UnityEditor;
using UnityEngine;
using GravityPuzzle.Config;

namespace GravityPuzzle.EditorTools
{
    public static class ShredderPrefabAuthoring
    {
        private const string PrefabFolder = "Assets/Prefabs";
        private const string PrefabPath = PrefabFolder + "/ShredderWheel.prefab";
        private const string CatchZonePrefabPath = PrefabFolder + "/ShredderCatchZone.prefab";

        [MenuItem("Gravity Puzzle/Refactor/Create Shredder Wheel Prefab")]
        private static void CreatePrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(PrefabPath);
            }

            GameObject root = new GameObject("ShredderWheel");
            try
            {
                ShredderWheel wheel = root.AddComponent<ShredderWheel>();
                wheel.BuildAuthoredPrefabHierarchy();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                ShredderConfig config = AssetDatabase.LoadAssetAtPath<ShredderConfig>("Assets/ShredderConfig.asset");
                if (config != null)
                {
                    SerializedObject serializedConfig = new SerializedObject(config);
                    serializedConfig.FindProperty("wheelPrefab").objectReferenceValue = prefab.GetComponent<ShredderWheel>();
                    serializedConfig.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssets();
                }
                Selection.activeObject = prefab;
                Debug.Log("[ShredderPrefab] Created Assets/Prefabs/ShredderWheel.prefab. Edit its Disc, Hub, and Tooth children before Play.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("Gravity Puzzle/Refactor/Create And Configure Shredder Catch Zone Pool")]
        private static void CreateCatchZonePrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            GameObject root = new GameObject("ShredderCatchZone");
            try
            {
                BoxCollider2D trigger = root.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                root.AddComponent<ShredderCatchZone>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CatchZonePrefabPath);

                ShredderConfig config = AssetDatabase.LoadAssetAtPath<ShredderConfig>("Assets/ShredderConfig.asset");
                if (config != null)
                {
                    SerializedObject serializedConfig = new SerializedObject(config);
                    serializedConfig.FindProperty("catchZonePrefab").objectReferenceValue = prefab.GetComponent<ShredderCatchZone>();
                    serializedConfig.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssets();
                }

                Selection.activeObject = prefab;
                Debug.Log("[ShredderPrefab] Created and assigned Assets/Prefabs/ShredderCatchZone.prefab.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("Gravity Puzzle/Refactor/Set Shredder Art Scale To 0.04")]
        private static void SetArtScale()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    Transform visual = renderers[index].transform;
                    switch (visual.name)
                    {
                        case "Shredder Disc":
                            visual.localScale = Vector3.one * (1.65f * .04f);
                            break;
                        case "Shredder Hub":
                            visual.localScale = Vector3.one * (.48f * .04f);
                            break;
                        default:
                            if (visual.name.StartsWith("Tooth"))
                                visual.localScale = new Vector3(.42f * .04f, .24f * .04f, 1f);
                            break;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[ShredderPrefab] Set all shredder art child scales to 0.04.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
