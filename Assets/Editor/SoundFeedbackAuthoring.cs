using GravityPuzzle.Bootstrap;
using GravityPuzzle.Config;
using GravityPuzzle.Presentation.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// One-time authoring for sound cues: adds ButtonClickSound to every game button and gives the
    /// scene bootstrap its AudioSource and SoundFeedbackConfig. Safe to run again; never runs in a player build.
    /// </summary>
    public static class SoundFeedbackAuthoring
    {
        private const string PrefabFolder = "Assets/MainPrefabs";
        private const string ConfigPath = "Assets/Config/Presentation/SoundFeedbackConfig.asset";

        [MenuItem("Gravity Puzzle/Audio/Wire Sound Feedback (MainPrefabs + Open Scenes)")]
        private static void WireSoundFeedback()
        {
            int prefabButtons = AddClickSoundToPrefabs();
            int sceneButtons = 0;
            int bootstraps = 0;
            SoundFeedbackConfig config = AssetDatabase.LoadAssetAtPath<SoundFeedbackConfig>(ConfigPath);
            if (config == null)
                Debug.LogError("[Sound] SoundFeedbackConfig not found at " + ConfigPath + ".");

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded)
                    continue;

                int sceneChanges = 0;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    sceneChanges += AddClickSound(roots[rootIndex], useUndo: true);
                    int wired = WireBootstraps(roots[rootIndex], config);
                    bootstraps += wired;
                    sceneChanges += wired;
                }

                sceneButtons += sceneChanges;
                if (sceneChanges > 0)
                    EditorSceneManager.MarkSceneDirty(scene);
            }

            Debug.Log($"[Sound] Click sound added to {prefabButtons} prefab buttons and {sceneButtons - bootstraps} scene buttons; {bootstraps} bootstrap(s) wired. Save the open scenes.");
        }

        private static int AddClickSoundToPrefabs()
        {
            int added = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int prefabAdded = AddClickSound(root, useUndo: false);
                    if (prefabAdded > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        added += prefabAdded;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return added;
        }

        // Buttons inside nested prefab instances are skipped; their own prefab asset receives the component.
        private static int AddClickSound(GameObject root, bool useUndo)
        {
            int added = 0;
            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                GameObject buttonObject = buttons[index].gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(buttonObject) ||
                    buttonObject.GetComponent<ButtonClickSound>() != null)
                    continue;

                if (useUndo)
                    Undo.AddComponent<ButtonClickSound>(buttonObject);
                else
                    buttonObject.AddComponent<ButtonClickSound>();
                added++;
            }

            return added;
        }

        private static int WireBootstraps(GameObject root, SoundFeedbackConfig config)
        {
            int wired = 0;
            RuntimePieceFactoryBootstrap[] bootstraps = root.GetComponentsInChildren<RuntimePieceFactoryBootstrap>(true);
            for (int index = 0; index < bootstraps.Length; index++)
            {
                RuntimePieceFactoryBootstrap bootstrap = bootstraps[index];
                AudioSource source = bootstrap.GetComponent<AudioSource>();
                if (source == null)
                {
                    source = Undo.AddComponent<AudioSource>(bootstrap.gameObject);
                    source.playOnAwake = false;
                    source.loop = false;
                    source.spatialBlend = 0f;
                }

                SerializedObject serialized = new SerializedObject(bootstrap);
                serialized.FindProperty("soundFeedbackAudioSource").objectReferenceValue = source;
                if (config != null)
                    serialized.FindProperty("soundFeedbackConfig").objectReferenceValue = config;
                serialized.ApplyModifiedProperties();
                wired++;
            }

            return wired;
        }
    }
}
