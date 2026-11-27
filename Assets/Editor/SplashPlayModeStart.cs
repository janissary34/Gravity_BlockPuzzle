using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Makes Play mode start from the first Build Settings scene (Splash), matching
    /// a player build, regardless of which scene is open in the editor, while
    /// preserving Unity Test Runner's temporary PlayMode bootstrap scene.
    /// </summary>
    [InitializeOnLoad]
    public static class SplashPlayModeStart
    {
        private const string TestBootstrapScenePrefix = "Assets/InitTestScene";

        static SplashPlayModeStart()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            QueueApplyIfNotTestRun();
        }

        private static void OnSceneSaved(Scene scene)
        {
            if (IsPlayModeTestScene(scene))
                EditorSceneManager.playModeStartScene = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (IsPlayModeTestScene(SceneManager.GetActiveScene()))
                {
                    EditorSceneManager.playModeStartScene = null;
                    return;
                }

                ApplySplashStartScene();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
                QueueApplyIfNotTestRun();
        }

        private static void QueueApplyIfNotTestRun()
        {
            EditorApplication.delayCall -= ApplyIfNotTestRun;
            EditorApplication.delayCall += ApplyIfNotTestRun;
        }

        private static void ApplyIfNotTestRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                IsPlayModeTestScene(SceneManager.GetActiveScene()))
            {
                return;
            }

            ApplySplashStartScene();
        }

        private static void ApplySplashStartScene()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || !scenes[0].enabled)
            {
                Debug.LogWarning("[Splash] No enabled first scene in Build Settings; Play mode start scene unchanged.");
                return;
            }

            SceneAsset splash = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenes[0].path);
            if (splash != null && EditorSceneManager.playModeStartScene != splash)
                EditorSceneManager.playModeStartScene = splash;
        }

        private static bool IsPlayModeTestScene(Scene scene)
        {
            return scene.IsValid() &&
                   !string.IsNullOrEmpty(scene.path) &&
                   scene.path.StartsWith(TestBootstrapScenePrefix, StringComparison.Ordinal);
        }
    }
}
