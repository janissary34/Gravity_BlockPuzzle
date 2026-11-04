#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

[assembly: PrebuildSetup(typeof(GravityPuzzle.Tests.PlayMode.PlayModeTestStartScene))]
[assembly: PostBuildCleanup(typeof(GravityPuzzle.Tests.PlayMode.PlayModeTestStartScene))]

namespace GravityPuzzle.Tests.PlayMode
{
    /// <summary>
    /// Lets PlayMode test runs start in the Test Framework's own bootstrap scene.
    ///
    /// The editor script SplashPlayModeStart sets EditorSceneManager.playModeStartScene to the
    /// Splash scene, so every Play Mode entry loads Splash. For a test run that replaces the Test
    /// Framework's generated init scene (which hosts the test controller). Splash then loads Scene1,
    /// and the tests never run or report. Before a PlayMode test run enters Play Mode (Edit Mode,
    /// prebuild setup), this clears the start scene. After the run (post-build cleanup, when Play
    /// Mode exits), it restores the previous one, so normal Play Mode still starts from Splash.
    /// </summary>
    public sealed class PlayModeTestStartScene : IPrebuildSetup, IPostBuildCleanup
    {
        private const string SavedStartSceneKey = "GravityPuzzle.Tests.PlayModeStartScene";

        // The Test Framework reads assembly-level [PrebuildSetup]/[PostBuildCleanup] only from
        // assemblies that reference UnityEditor.TestRunner. No other code here uses that assembly,
        // so this keeps the reference (otherwise the compiler drops it and the attributes are ignored).
        internal static readonly Type TestRunnerAssemblyReference =
            typeof(UnityEditor.TestTools.TestRunner.Api.TestRunnerApi);

        public void Setup()
        {
            SceneAsset current = EditorSceneManager.playModeStartScene;
            SessionState.SetString(SavedStartSceneKey, current != null ? AssetDatabase.GetAssetPath(current) : string.Empty);
            EditorSceneManager.playModeStartScene = null;
        }

        public void Cleanup()
        {
            string path = SessionState.GetString(SavedStartSceneKey, string.Empty);
            SessionState.EraseString(SavedStartSceneKey);
            if (!string.IsNullOrEmpty(path))
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        }
    }
}
#endif
