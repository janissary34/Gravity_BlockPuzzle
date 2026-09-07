using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Presentation.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Binds the three scene-authored New Booster content objects to config
    /// assets. This is editor-only because the game must not instantiate UI
    /// prefabs when displaying a reward.
    /// </summary>
    internal static class BoosterRewardPanelAuthoring
    {
        private const string ConfigFolderPath = "Assets/Config/Boosters";
        private const string PanelName = "NewBoosterPanel";
        private const string ContentName = "BoosterContent";

        [MenuItem("Gravity Puzzle/UI/Configure New Booster Rewards")]
        private static void ConfigureNewBoosterRewards()
        {
            RemoveMissingComponentsFromBoosterButtons();
            NewBoosterPanelView panel = FindInActiveScene<NewBoosterPanelView>(PanelName);
            if (panel == null)
            {
                Debug.LogError("[New Booster Rewards] NewBoosterPanel was not found in the active scene.");
                return;
            }

            Transform contentRoot = panel.transform.Find(ContentName);
            if (contentRoot == null)
            {
                Debug.LogError("[New Booster Rewards] NewBoosterPanel requires a BoosterContent child.", panel);
                return;
            }

            EnsureConfigFolder();
            BoosterRewardContentView freezeTimer = ConfigureContent(
                contentRoot,
                "Freeze_Timer",
                BoosterRewardType.FreezeTimer,
                "FrozenClock_newBooster");
            BoosterRewardContentView rocket = ConfigureContent(
                contentRoot,
                "Rocket",
                BoosterRewardType.Rocket,
                "Rocket_obj");
            BoosterRewardContentView hammer = ConfigureContent(
                contentRoot,
                "Hammer",
                BoosterRewardType.Hammer,
                "hammer");
            if (freezeTimer == null || rocket == null || hammer == null)
                return;

            SerializedObject serializedPanel = new SerializedObject(panel);
            ConfigurePanelButtons(panel, serializedPanel);
            SerializedProperty previewProperty = serializedPanel.FindProperty("previewRewardConfig");
            previewProperty.objectReferenceValue = freezeTimer.RewardConfig;
            SetContentArray(serializedPanel.FindProperty("rewardContents"), freezeTimer, rocket, hammer);
            GameObject overlayRoot = panel.transform.parent != null ? panel.transform.parent.gameObject : null;
            serializedPanel.FindProperty("overlayRoot").objectReferenceValue = overlayRoot;
            SetGameObjectArray(
                serializedPanel.FindProperty("hudObjectsToHide"),
                "Gold_obj",
                "Time",
                "ProgressBarSlider",
                "Freeze_Slider",
                "Settings_Obj");
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();
            ReparentTimerPresentationOutsideOverlay(panel, overlayRoot);
            ConfigureBoardRewardFlow(panel, freezeTimer.RewardConfig, rocket.RewardConfig, hammer.RewardConfig);
            BoosterRewardUnlockState.ClearPresentation(freezeTimer.RewardConfig);
            BoosterRewardUnlockState.ClearPresentation(rocket.RewardConfig);
            BoosterRewardUnlockState.ClearPresentation(hammer.RewardConfig);

            SetContentActive(freezeTimer, false);
            SetContentActive(rocket, false);
            SetContentActive(hammer, false);
            if (overlayRoot != null)
            {
                panel.gameObject.SetActive(true);
                overlayRoot.SetActive(false);
            }
            else
                panel.gameObject.SetActive(false);
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[New Booster Rewards] Freeze Timer, Rocket and Hammer reward configs are configured.", panel);
        }

        private static void RemoveMissingComponentsFromBoosterButtons()
        {
            BoosterButton[] buttons = Object.FindObjectsByType<BoosterButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < buttons.Length; index++)
            {
                BoosterButton button = buttons[index];
                if (button != null)
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(button.gameObject);
            }
        }

        private static BoosterRewardContentView ConfigureContent(
            Transform contentRoot,
            string contentName,
            BoosterRewardType rewardType,
            string visualObjectName)
        {
            Transform contentTransform = contentRoot.Find(contentName);
            if (contentTransform == null)
            {
                Debug.LogError($"[New Booster Rewards] Missing BoosterContent/{contentName}.", contentRoot);
                return null;
            }

            Transform visualTransform = FindDescendant(contentTransform, visualObjectName);
            if (visualTransform == null)
            {
                Debug.LogError($"[New Booster Rewards] Missing visual '{visualObjectName}' below {contentName}.", contentTransform);
                return null;
            }

            GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(visualTransform.gameObject);
            if (prefabSource == null)
            {
                Debug.LogError($"[New Booster Rewards] '{visualObjectName}' must be a prefab instance.", visualTransform);
                return null;
            }

            BoosterRewardConfig config = GetOrCreateConfig(rewardType, prefabSource);
            BoosterRewardContentView contentView = contentTransform.GetComponent<BoosterRewardContentView>();
            if (contentView == null)
                contentView = Undo.AddComponent<BoosterRewardContentView>(contentTransform.gameObject);

            SerializedObject serializedContent = new SerializedObject(contentView);
            serializedContent.FindProperty("rewardConfig").objectReferenceValue = config;
            serializedContent.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(contentView);
            return contentView;
        }

        private static BoosterRewardConfig GetOrCreateConfig(BoosterRewardType rewardType, GameObject prefabSource)
        {
            string assetPath = $"{ConfigFolderPath}/{rewardType}BoosterRewardConfig.asset";
            BoosterRewardConfig config = AssetDatabase.LoadAssetAtPath<BoosterRewardConfig>(assetPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<BoosterRewardConfig>();
                AssetDatabase.CreateAsset(config, assetPath);
            }

            SerializedObject serializedConfig = new SerializedObject(config);
            serializedConfig.FindProperty("boosterType").enumValueIndex = (int)rewardType;
            serializedConfig.FindProperty("visualPrefab").objectReferenceValue = prefabSource;
            serializedConfig.FindProperty("presentationLevel").intValue = GetPresentationLevel(rewardType);
            serializedConfig.FindProperty("usesPerLevel").intValue = 3;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
        }

        private static void ConfigureBoardRewardFlow(
            NewBoosterPanelView panel,
            BoosterRewardConfig freezeTimer,
            BoosterRewardConfig rocket,
            BoosterRewardConfig hammer)
        {
            PrototypeBoard board = Object.FindFirstObjectByType<PrototypeBoard>(FindObjectsInactive.Include);
            if (board == null)
            {
                Debug.LogError("[New Booster Rewards] PrototypeBoard was not found in the active scene.");
                return;
            }

            SerializedObject serializedBoard = new SerializedObject(board);
            serializedBoard.FindProperty("newBoosterPanel").objectReferenceValue = panel;
            SetConfigArray(serializedBoard.FindProperty("boosterRewardConfigs"), freezeTimer, hammer, rocket);
            Transform winVfx = FindTransformInActiveScene("WinVFX");
            serializedBoard.FindProperty("winVfx").objectReferenceValue = winVfx != null ? winVfx.gameObject : null;
            serializedBoard.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(board);
            ConfigureCampaignBoosterAvailability(freezeTimer, rocket, hammer);
        }

        private static void ConfigureCampaignBoosterAvailability(
            BoosterRewardConfig freezeTimer,
            BoosterRewardConfig rocket,
            BoosterRewardConfig hammer)
        {
            GravityLevelSequence sequence = AssetDatabase.LoadAssetAtPath<GravityLevelSequence>("Assets/Resources/LevelSequence.asset");
            if (sequence == null)
            {
                Debug.LogError("[New Booster Rewards] Assets/Resources/LevelSequence.asset is missing.");
                return;
            }

            for (int index = 0; index < sequence.levels.Count; index++)
            {
                GravityLevelDefinition level = sequence.levels[index];
                if (level == null)
                    continue;

                int levelNumber = index + 1;
                level.timerBoosterCount = levelNumber >= freezeTimer.PresentationLevel ? freezeTimer.UsesPerLevel : 0;
                level.hammerBoosterCount = levelNumber >= hammer.PresentationLevel ? hammer.UsesPerLevel : 0;
                level.rocketBoosterCount = levelNumber >= rocket.PresentationLevel ? rocket.UsesPerLevel : 0;
                EditorUtility.SetDirty(level);
            }
        }

        private static int GetPresentationLevel(BoosterRewardType rewardType)
        {
            return rewardType switch
            {
                BoosterRewardType.FreezeTimer => 4,
                BoosterRewardType.Hammer => 6,
                BoosterRewardType.Rocket => 10,
                _ => 1
            };
        }

        private static void ConfigurePanelButtons(NewBoosterPanelView panel, SerializedObject serializedPanel)
        {
            Button closeButton = FindButton(panel.transform, "CloseButton");
            Button continueButton = FindButton(panel.transform, "ContinueButton");
            if (closeButton == null || continueButton == null)
            {
                Debug.LogError("[New Booster Rewards] CloseButton or ContinueButton is missing below NewBoosterPanel.", panel);
                return;
            }

            serializedPanel.FindProperty("closeButton").objectReferenceValue = closeButton;
            serializedPanel.FindProperty("continueButton").objectReferenceValue = continueButton;
        }

        private static void ReparentTimerPresentationOutsideOverlay(NewBoosterPanelView panel, GameObject overlayRoot)
        {
            if (overlayRoot == null || overlayRoot.transform.parent == null)
                return;

            // The timer flight visual was accidentally parented under the reward
            // popup. Resolve the authored child by name as well as the runtime
            // TimerBooster reference so the two presentation trees stay separate.
            Transform nestedTimerPresentation = FindDescendant(panel.transform, "Timer_obj");
            if (nestedTimerPresentation != null && nestedTimerPresentation != panel.transform)
            {
                Undo.SetTransformParent(
                    nestedTimerPresentation,
                    overlayRoot.transform.parent,
                    "Move Timer Presentation Outside Booster Overlay");
                nestedTimerPresentation.gameObject.SetActive(false);
                EditorUtility.SetDirty(nestedTimerPresentation.gameObject);
            }

            TimerBooster[] timerBoosters = Object.FindObjectsByType<TimerBooster>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < timerBoosters.Length; index++)
            {
                SerializedObject serializedTimerBooster = new SerializedObject(timerBoosters[index]);
                if (serializedTimerBooster.FindProperty("timer_obj").objectReferenceValue is not GameObject timerObject ||
                    timerObject == panel.gameObject ||
                    !timerObject.transform.IsChildOf(overlayRoot.transform))
                    continue;

                Undo.SetTransformParent(timerObject.transform, overlayRoot.transform.parent, "Move Timer Presentation Outside Booster Overlay");
                timerObject.SetActive(false);
                EditorUtility.SetDirty(timerObject);
                EditorSceneManager.MarkSceneDirty(timerObject.scene);
            }
        }

        private static Button FindButton(Transform parent, string objectName)
        {
            Transform transform = FindDescendant(parent, objectName);
            return transform != null ? transform.GetComponent<Button>() : null;
        }

        private static void EnsureConfigFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Config"))
                AssetDatabase.CreateFolder("Assets", "Config");
            if (!AssetDatabase.IsValidFolder(ConfigFolderPath))
                AssetDatabase.CreateFolder("Assets/Config", "Boosters");
        }

        private static void SetContentActive(BoosterRewardContentView content, bool active)
        {
            if (content.gameObject.activeSelf != active)
                content.gameObject.SetActive(active);
        }

        private static T FindInActiveScene<T>(string objectName) where T : Component
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
            {
                T[] components = rootObjects[rootIndex].GetComponentsInChildren<T>(true);
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    if (components[componentIndex].name == objectName)
                        return components[componentIndex];
                }
            }

            return null;
        }

        private static Transform FindDescendant(Transform parent, string objectName)
        {
            Transform[] transforms = parent.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName)
                    return transforms[index];
            }

            return null;
        }

        private static Transform FindTransformInActiveScene(string objectName)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            for (int index = 0; index < rootObjects.Length; index++)
            {
                Transform result = FindDescendant(rootObjects[index].transform, objectName);
                if (result != null)
                    return result;
            }

            return null;
        }

        private static void SetContentArray(SerializedProperty property, params BoosterRewardContentView[] values)
        {
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        private static void SetConfigArray(SerializedProperty property, params BoosterRewardConfig[] values)
        {
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        private static void SetGameObjectArray(SerializedProperty property, params string[] objectNames)
        {
            property.arraySize = objectNames.Length;
            for (int index = 0; index < objectNames.Length; index++)
            {
                Transform target = FindTransformInActiveScene(objectNames[index]);
                if (target == null)
                {
                    Debug.LogError($"[New Booster Rewards] HUD object '{objectNames[index]}' was not found.");
                    continue;
                }

                property.GetArrayElementAtIndex(index).objectReferenceValue = target.gameObject;
            }
        }

    }
}
