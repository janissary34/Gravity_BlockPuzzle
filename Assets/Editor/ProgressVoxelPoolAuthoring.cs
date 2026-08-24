#if UNITY_EDITOR
using GravityPuzzle.Config;
using GravityPuzzle.Presentation.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>One-time authoring for the typed progress-flight voxel pool.</summary>
    public static class ProgressVoxelPoolAuthoring
    {
        private const string PrefabPath = "Assets/Prefabs/FlyingProgressVoxel.prefab";
        private const string PoolConfigPath = "Assets/PoolConfig.asset";

        [MenuItem("Gravity Puzzle/Refactor/Create And Configure Progress Voxel Pool")]
        private static void CreateAndConfigure()
        {
            FlyingProgressVoxelView prefab = AssetDatabase.LoadAssetAtPath<FlyingProgressVoxelView>(PrefabPath);
            if (prefab == null)
            {
                GameObject root = new GameObject("FlyingProgressVoxel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                Image image = root.GetComponent<Image>();
                image.raycastTarget = false;
                prefab = root.AddComponent<FlyingProgressVoxelView>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Object.DestroyImmediate(root);
                prefab = AssetDatabase.LoadAssetAtPath<FlyingProgressVoxelView>(PrefabPath);
            }

            LevelProgressManager progressManager = FindSceneObject<LevelProgressManager>();
            PoolConfig poolConfig = AssetDatabase.LoadAssetAtPath<PoolConfig>(PoolConfigPath);
            if (progressManager == null || poolConfig == null)
            {
                Debug.LogError("[LevelProgress] Missing authored LevelProgressManager or Assets/PoolConfig.asset in the active scene.");
                return;
            }

            SerializedObject serializedManager = new SerializedObject(progressManager);
            serializedManager.FindProperty("flyingProgressVoxelPrefab").objectReferenceValue = prefab;
            serializedManager.FindProperty("poolConfig").objectReferenceValue = poolConfig;
            serializedManager.FindProperty("flyingProgressVoxelPoolParent").objectReferenceValue = progressManager.transform;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(progressManager);
            EditorSceneManager.MarkSceneDirty(progressManager.gameObject.scene);
            Debug.Log("[LevelProgress] Typed FlyingProgressVoxel pool configured.", progressManager);
        }

        private static T FindSceneObject<T>() where T : Component
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                T candidate = roots[rootIndex].GetComponentInChildren<T>(true);
                if (candidate != null)
                    return candidate;
            }

            return null;
        }
    }
}
#endif
