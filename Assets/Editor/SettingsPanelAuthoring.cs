using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>One-time scene authoring for the settings UI. Never runs in a player build.</summary>
    public static class SettingsPanelAuthoring
    {
        [MenuItem("Gravity Puzzle/Refactor/Configure Authored Settings UI")]
        private static void ConfigureAuthoredSettingsUi()
        {
            GameObject settingsObject = FindSceneObject("Settings_btn");
            GameObject panelObject = FindSceneObject("Setting_panel");
            if (settingsObject == null || panelObject == null)
            {
                Debug.LogError("[Settings] Could not find Settings_btn and Setting_panel in the active scene.");
                return;
            }

            SettingsPanelButton settings = settingsObject.GetComponent<SettingsPanelButton>();
            if (settings == null)
                settings = Undo.AddComponent<SettingsPanelButton>(settingsObject);

            Button settingsButton = settingsObject.GetComponent<Button>();
            Button soundButton = FindButton(panelObject.transform, "Sound_btn");
            Button musicButton = FindButton(panelObject.transform, "Music_btn");
            if (settingsButton == null || soundButton == null || musicButton == null)
            {
                Debug.LogError("[Settings] Could not resolve all authored settings buttons.", settingsObject);
                return;
            }

            AudioSource[] allSources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int musicCount = 0;
            for (int index = 0; index < allSources.Length; index++)
            {
                if (allSources[index].loop)
                    musicCount++;
            }

            AudioSource[] musicSources = new AudioSource[musicCount];
            AudioSource[] soundSources = new AudioSource[allSources.Length - musicCount];
            int musicIndex = 0;
            int soundIndex = 0;
            for (int index = 0; index < allSources.Length; index++)
            {
                AudioSource source = allSources[index];
                if (source.loop)
                    musicSources[musicIndex++] = source;
                else
                    soundSources[soundIndex++] = source;
            }

            SerializedObject serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("settingsPanel").objectReferenceValue = panelObject;
            serializedSettings.FindProperty("settingsButton").objectReferenceValue = settingsButton;
            serializedSettings.FindProperty("soundButton").objectReferenceValue = soundButton;
            serializedSettings.FindProperty("musicButton").objectReferenceValue = musicButton;
            SetObjectArray(serializedSettings.FindProperty("soundSources"), soundSources);
            SetObjectArray(serializedSettings.FindProperty("musicSources"), musicSources);
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(settings);
            EditorSceneManager.MarkSceneDirty(settingsObject.scene);
            Debug.Log("[Settings] Authored settings UI configured. References are now scene-owned.", settings);
        }

        private static Button FindButton(Transform parent, string objectName)
        {
            Transform[] transforms = parent.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform candidate = transforms[index];
                if (candidate.name == objectName)
                    return candidate.GetComponent<Button>();
            }

            return null;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] roots = activeScene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    Transform candidate = transforms[transformIndex];
                    if (candidate.name == objectName)
                        return candidate.gameObject;
                }
            }

            return null;
        }

        private static void SetObjectArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }
    }
}
