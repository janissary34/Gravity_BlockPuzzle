using System;
using GravityPuzzle.Presentation.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Moves the artist-authored Scene Tuna frame out of the active scene's
    /// anonymous root and into the reusable LevelRuntimeRoot prefab hierarchy.
    /// The frame remains presentation-only; map collision is still generated
    /// from each level's authoritative grid.
    /// </summary>
    public static class SceneTunaBoardFrameAuthoring
    {
        private const string FramePrefabPath = "Assets/Prefabs/SceneTunaBoardFrame.prefab";
        private const string RuntimeRootPrefabPath = "Assets/Prefabs/LevelRuntimeRoot.prefab";
        private const string FrameRootName = "Scene Tuna Board Frame";
        private const string LegacyFramePrefabRootName = "SceneTunaBoardFrame";
        private const string CompositeFrameRootName = "Composite Board Frame";
        private const int MinimumFrameModuleCount = 30;

        [MenuItem("Gravity Puzzle/Level Authoring/Save Scene Tuna Board Frame Prefab")]
        public static void SaveSceneTunaBoardFramePrefab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BoardFrameAuthoring] Exit Play Mode before saving the board-frame prefab.");
                return;
            }

            GameObject source = FindFrameSource();
            if (source == null)
            {
                Debug.LogError(
                    "[BoardFrameAuthoring] Could not find the Scene Tuna board frame. " +
                    "Select or restore the 30+ module frame group in Scene1 first.");
                return;
            }

            RenameForAuthoring(source.transform);
            source.name = FrameRootName;

            GameObject framePrefab = PrefabUtility.SaveAsPrefabAsset(source, FramePrefabPath);
            if (framePrefab == null)
                throw new InvalidOperationException("[BoardFrameAuthoring] Failed to save SceneTunaBoardFrame.prefab.");

            AddFrameToRuntimeRootPrefab(framePrefab);
            Object.DestroyImmediate(source);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log(
                "[BoardFrameAuthoring] Saved SceneTunaBoardFrame and nested it under " +
                "LevelRuntimeRoot/Composite Board Frame. Edit it from that hierarchy or Prefab Mode.");
        }

        private static GameObject FindFrameSource()
        {
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                GameObject candidate = roots[index];
                if (candidate == null ||
                    (candidate.name != "GameObject (5)" && candidate.name != FrameRootName))
                {
                    continue;
                }

                if (candidate.GetComponentsInChildren<SpriteRenderer>(true).Length >=
                    MinimumFrameModuleCount)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void RenameForAuthoring(Transform frameRoot)
        {
            for (int index = 0; index < frameRoot.childCount; index++)
            {
                Transform module = frameRoot.GetChild(index);
                if (module.name.StartsWith("GameObject (", StringComparison.Ordinal))
                    module.name = $"Frame Module {index + 1:00}";
            }
        }

        private static void AddFrameToRuntimeRootPrefab(GameObject framePrefab)
        {
            GameObject runtimeRootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeRootPrefabPath);
            if (runtimeRootPrefab == null)
                throw new InvalidOperationException("[BoardFrameAuthoring] LevelRuntimeRoot.prefab is missing.");

            GameObject editableRoot = PrefabUtility.LoadPrefabContents(RuntimeRootPrefabPath);
            try
            {
                Transform compositeFrameRoot = FindDirectChild(editableRoot.transform, CompositeFrameRootName);
                if (compositeFrameRoot == null)
                {
                    throw new InvalidOperationException(
                        "[BoardFrameAuthoring] LevelRuntimeRoot is missing its Composite Board Frame child.");
                }

                Transform existingFrame = FindDirectChild(compositeFrameRoot, FrameRootName) ??
                                          FindDirectChild(compositeFrameRoot, LegacyFramePrefabRootName);
                if (existingFrame != null)
                    Object.DestroyImmediate(existingFrame.gameObject);

                GameObject frameInstance = PrefabUtility.InstantiatePrefab(framePrefab) as GameObject;
                if (frameInstance == null)
                    throw new InvalidOperationException("[BoardFrameAuthoring] Could not instantiate the saved board-frame prefab.");

                frameInstance.transform.SetParent(compositeFrameRoot, false);
                frameInstance.name = FrameRootName;
                PrefabUtility.SaveAsPrefabAsset(editableRoot, RuntimeRootPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(editableRoot);
            }
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (child.name == childName)
                    return child;
            }

            return null;
        }
    }
}
