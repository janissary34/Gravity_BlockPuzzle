using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Makes Play mode start from the first Build Settings scene (Splash), matching
    /// a player build, regardless of which scene is open in the editor.
    /// </summary>
    [InitializeOnLoad]
    public static class SplashPlayModeStart
    {
        static SplashPlayModeStart()
        {
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
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
    }
}
