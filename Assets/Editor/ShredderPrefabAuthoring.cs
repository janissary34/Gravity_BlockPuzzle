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
        private const string FeedMaskPrefabPath = PrefabFolder + "/ShredderFeedMask.prefab";

        [MenuItem("Gravity Puzzle/Refactor/Create Shredder Wheel Prefab")]
        private static void CreatePrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                Debug.Log("[ShredderPrefab] Existing ShredderWheel.prefab was preserved. Edit its art directly; the create command will not overwrite authored sprites.");
                return;
            }

            ShredderConfig config = LoadOrCreateConfig();

            GameObject root = new GameObject("ShredderWheel");
            try
            {
                ShredderWheel wheel = root.AddComponent<ShredderWheel>();
                wheel.BuildAuthoredPrefabHierarchy(config);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                SerializedObject serializedConfig = new SerializedObject(config);
                serializedConfig.FindProperty("wheelPrefab").objectReferenceValue = prefab.GetComponent<ShredderWheel>();
                serializedConfig.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
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

        [MenuItem("Gravity Puzzle/Refactor/Create And Configure Shredder Feed Mask")]
        private static void CreateFeedMaskPrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            GameObject root = new GameObject("ShredderFeedMask");
            try
            {
                SpriteMask mask = root.AddComponent<SpriteMask>();
                mask.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/exec-dced8d92-19b9-4407-ba20-eb6bcd4d5bac.png");
                mask.frontSortingLayerID = SortingLayer.NameToID("Default");
                mask.frontSortingOrder = 32767;
                mask.backSortingLayerID = SortingLayer.NameToID("Default");
                mask.backSortingOrder = -32768;
                root.AddComponent<ShredderFeedMask>();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, FeedMaskPrefabPath);
                ShredderConfig config = AssetDatabase.LoadAssetAtPath<ShredderConfig>("Assets/ShredderConfig.asset");
                if (config != null)
                {
                    SerializedObject serializedConfig = new SerializedObject(config);
                    serializedConfig.FindProperty("feedMaskPrefab").objectReferenceValue = prefab.GetComponent<ShredderFeedMask>();
                    serializedConfig.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssets();
                }

                Selection.activeObject = prefab;
                Debug.Log("[ShredderPrefab] Created and assigned Assets/Prefabs/ShredderFeedMask.prefab.");
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
                ShredderConfig config = LoadOrCreateConfig();
                SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    Transform visual = renderers[index].transform;
                    switch (visual.name)
                    {
                        case "Shredder Disc":
                            visual.localScale = Vector3.one * (config.DiscArtScale * config.WheelArtScale);
                            break;
                        case "Shredder Hub":
                            visual.localScale = Vector3.one * (config.HubArtScale * config.WheelArtScale);
                            break;
                        default:
                            if (visual.name.StartsWith("Tooth"))
                                visual.localScale = Vector3.one * config.WheelArtScale;
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

        private static ShredderConfig LoadOrCreateConfig()
        {
            ShredderConfig config = AssetDatabase.LoadAssetAtPath<ShredderConfig>("Assets/ShredderConfig.asset");
            if (config != null)
                return config;

            config = ScriptableObject.CreateInstance<ShredderConfig>();
            AssetDatabase.CreateAsset(config, "Assets/ShredderConfig.asset");
            return config;
        }
    }
}
