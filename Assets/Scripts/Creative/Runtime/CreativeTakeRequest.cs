using System;
using System.IO;
using GravityPuzzle.Creative.Config;
using UnityEngine;

namespace GravityPuzzle.Creative.Runtime
{
    /// <summary>
    /// The explicit switch that turns Creative Mode on for one Play Mode session.
    ///
    /// Creative Mode is OFF by default: there is no request unless an editor tool (the
    /// "Gravity Puzzle/Creative" menu or the creative-pipeline job runner) wrote one to EditorPrefs
    /// just before entering Play Mode. The request is consumed (deleted) when read, so the next
    /// normal Play Mode session is unaffected. Player builds never contain a request.
    /// This follows the existing level-editor Play Preview pattern (EditorPrefs + AssetDatabase).
    /// </summary>
    public sealed class CreativeTakeRequest
    {
        /// <summary>EditorPrefs key holding the JSON request.</summary>
        public const string EditorPrefsKey = "GravityPuzzle.Creative.TakeRequest";

        [Serializable]
        public sealed class Data
        {
            public string configPath;   // asset path of the CreativeTakeConfig
            public string takeName;     // e.g. gbp_L12_take001
            public string outputFolder; // absolute folder; events.json (and the recorder's video) go here
            // Capture settings copied from the config by the editor launcher; read by the editor-only
            // recorder driver at Play Mode start (before this request is consumed by the scene).
            public int captureWidth;
            public int captureHeight;
            public int captureFrameRate;
        }

        private CreativeTakeRequest(CreativeTakeConfig config, string takeName, string outputFolder)
        {
            Config = config;
            TakeName = takeName;
            OutputFolder = outputFolder;
        }

        public CreativeTakeConfig Config { get; }
        public string TakeName { get; }
        public string OutputFolder { get; }
        public string EventsPath => Path.Combine(OutputFolder, TakeName + ".events.json");

        /// <summary>
        /// Reads and consumes the pending request. Returns null when Creative Mode is not requested
        /// (always null in player builds). Invalid requests are reported and ignored.
        /// </summary>
        public static CreativeTakeRequest ConsumePending()
        {
#if UNITY_EDITOR
            string json = UnityEditor.EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;

            UnityEditor.EditorPrefs.DeleteKey(EditorPrefsKey);
            Data data = JsonUtility.FromJson<Data>(json);
            if (data == null || string.IsNullOrEmpty(data.configPath) || string.IsNullOrEmpty(data.takeName) ||
                string.IsNullOrEmpty(data.outputFolder))
            {
                Debug.LogError($"[Creative] Ignoring an incomplete take request: {json}");
                return null;
            }

            CreativeTakeConfig config = UnityEditor.AssetDatabase.LoadAssetAtPath<CreativeTakeConfig>(data.configPath);
            if (config == null || config.Level == null)
            {
                Debug.LogError($"[Creative] Take request config '{data.configPath}' is missing or has no level. Creative Mode stays off.");
                return null;
            }

            return new CreativeTakeRequest(config, data.takeName, data.outputFolder);
#else
            return null;
#endif
        }
    }
}
