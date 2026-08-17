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
    }
}
