using System;
using System.IO;
using GravityPuzzle.Creative.Config;
using GravityPuzzle.Creative.Runtime;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Creative.EditorTools
{
    /// <summary>
    /// Starts a creative take: writes the one-shot take request (EditorPrefs) and enters Play Mode.
    /// The scene's composition root consumes the request; CreativeRecorderDriver records the take
    /// with Unity Recorder from Play Mode start to Play Mode exit.
    /// Normal Play Mode is unaffected because no request exists.
    /// </summary>
    public static class CreativeTakeLauncher
    {
        /// <summary>Allowed relative difference between the Game View aspect and the capture aspect.</summary>
        private const float AspectTolerance = .01f;

        /// <summary>Default output root, shared with the creative-pipeline data folder.</summary>
        public static string DefaultOutputRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                         "Documents", "CreativePipeline", "cache", "unity-takes");

        [MenuItem("Gravity Puzzle/Creative/Record Take From Selected Config")]
        private static void RecordSelected()
        {
            var config = Selection.activeObject as CreativeTakeConfig;
            if (config == null)
            {
                EditorUtility.DisplayDialog("Creative take", "Select a CreativeTakeConfig asset first.", "OK");
                return;
            }

            string takeName = $"gbp_{(config.Level != null ? config.Level.name : "level")}_{DateTime.Now:yyyyMMdd-HHmmss}";
            RequestTake(AssetDatabase.GetAssetPath(config), takeName, Path.Combine(DefaultOutputRoot, takeName));
        }

        [MenuItem("Gravity Puzzle/Creative/Record Take From Selected Config", true)]
        private static bool RecordSelectedValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Writes the take request and enters Play Mode. Returns false (and changes nothing) when not possible.</summary>
        public static bool RequestTake(string configPath, string takeName, string outputFolder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Creative] A take can only start from Edit Mode.");
                return false;
            }

            var config = AssetDatabase.LoadAssetAtPath<CreativeTakeConfig>(configPath);
            if (config == null || config.Level == null)
            {
                Debug.LogError($"[Creative] '{configPath}' is not a CreativeTakeConfig with a level.");
                return false;
            }

            // The level computes its camera fit once, from the screen aspect, when it builds. The
            // recorder resizes the Game View to the capture size only after Play Mode starts, so the
            // Game View must already have the capture aspect or the framing would be wrong.
            Vector2 gameView = Handles.GetMainGameViewSize();
            float viewAspect = gameView.y > 0f ? gameView.x / gameView.y : 0f;
            float captureAspect = (float)config.CaptureWidth / config.CaptureHeight;
            if (Mathf.Abs(viewAspect - captureAspect) > captureAspect * AspectTolerance)
            {
                Debug.LogError($"[Creative] The Game View is {gameView.x}x{gameView.y}, but the take captures {config.CaptureWidth}x{config.CaptureHeight}. " +
                               $"Set the Game View resolution to {config.CaptureWidth}x{config.CaptureHeight} (or that aspect) and start the take again.");
                return false;
            }

            Directory.CreateDirectory(outputFolder);
            var data = new CreativeTakeRequest.Data
            {
                configPath = configPath,
                takeName = takeName,
                outputFolder = outputFolder,
                captureWidth = config.CaptureWidth,
                captureHeight = config.CaptureHeight,
                captureFrameRate = config.CaptureFrameRate,
            };
            EditorPrefs.SetString(CreativeTakeRequest.EditorPrefsKey, JsonUtility.ToJson(data));
            Debug.Log($"[Creative] Requested take '{takeName}' ({config.CaptureWidth}x{config.CaptureHeight} @ {config.CaptureFrameRate} fps) -> {outputFolder}");
            EditorApplication.EnterPlaymode();
            return true;
        }

        [MenuItem("Gravity Puzzle/Creative/Cancel Pending Take Request")]
        private static void CancelPending()
        {
            EditorPrefs.DeleteKey(CreativeTakeRequest.EditorPrefsKey);
            Debug.Log("[Creative] Pending take request cleared.");
        }
    }
}
