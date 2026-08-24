using GravityPuzzle;
using GravityPuzzle.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GravityPuzzle.EditorTools
{
    public static class ShredderConfigAuthoring
    {
        private const string ConfigPath = "Assets/ShredderConfig.asset";
        private const string FeedPhysicsMaterialPath = "Assets/Config/ShredderFeed.physicsMaterial2D";

        [MenuItem("Gravity Puzzle/Refactor/Create And Configure Shredder Settings")]
        private static void CreateAndConfigure()
        {
            ShredderConfig config = AssetDatabase.LoadAssetAtPath<ShredderConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ShredderConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            BlockShredder[] shredders = Object.FindObjectsOfType<BlockShredder>(true);
            for (int i = 0; i < shredders.Length; i++)
            {
                SerializedObject serializedShredder = new SerializedObject(shredders[i]);
                serializedShredder.FindProperty("shredderConfig").objectReferenceValue = config;
                serializedShredder.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(shredders[i]);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeObject = config;
            Debug.Log("[ShredderConfig] Created and assigned ShredderConfig.asset. Edit it before Play to tune runtime shredder wheels and feed behaviour.");
        }

        [MenuItem("Gravity Puzzle/Refactor/Create And Configure Shredder Feed Physics Material")]
        private static void CreateAndConfigureFeedPhysicsMaterial()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Config"))
                AssetDatabase.CreateFolder("Assets", "Config");

            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(FeedPhysicsMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial2D("Shredder Feed");
                AssetDatabase.CreateAsset(material, FeedPhysicsMaterialPath);
            }

            material.friction = 0f;
            material.bounciness = 0f;
            EditorUtility.SetDirty(material);

            ShredderConfig config = AssetDatabase.LoadAssetAtPath<ShredderConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ShredderConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            SerializedObject serializedConfig = new SerializedObject(config);
            serializedConfig.FindProperty("feedPhysicsMaterial").objectReferenceValue = material;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Selection.activeObject = material;
            Debug.Log("[ShredderConfig] Created and assigned the authored Shredder Feed physics material.");
        }
    }
}
