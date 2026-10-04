#if UNITY_EDITOR
using GravityPuzzle;
using GravityPuzzle.Bootstrap;
using GravityPuzzle.Presentation.Views;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Makes the artist-authored InGameUI prefab the one and only gameplay HUD
    /// in a scene. The scene continues to own runtime references and the
    /// fullscreen result panel, so no camera, board or bootstrap is copied.
    /// </summary>
    public static class SceneTunaUiMigrationAuthoring
    {
        private const string InGameUiPrefabPath = "Assets/Prefabs/UI/InGameUI.prefab";
        private const string FullscreenSuccessPrefabPath = "Assets/Prefabs/UI/FullscreenSuccess.prefab";

        [MenuItem("Gravity Puzzle/Art/Apply Scene Tuna UI To Active Scene")]
        private static void ApplyToActiveScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[SceneTunaUI] Exit Play Mode before applying scene UI references.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[SceneTunaUI] No loaded scene is available for UI migration.");
                return;
            }

            RuntimePieceFactoryBootstrap bootstrap = FindComponentInScene<RuntimePieceFactoryBootstrap>(scene);
            Transform legacyTimerCanvas = FindSceneRoot(scene, "Timer_Canvas (1)");
            if (bootstrap == null || legacyTimerCanvas == null)
            {
                Debug.LogError("[SceneTunaUI] Runtime Piece Factory Bootstrap and Timer_Canvas (1) are required.");
                return;
            }

            GameObject uiRoot = GetOrCreateInGameUi(scene);
            if (uiRoot == null)
                return;

            GameObject fullscreenSuccess = FindDescendant(legacyTimerCanvas, "FullscreenSuccess")?.gameObject;
            if (fullscreenSuccess == null)
                fullscreenSuccess = FindDescendant(uiRoot.transform, "FullscreenSuccess")?.gameObject;
            if (fullscreenSuccess == null)
            {
                Debug.LogError("[SceneTunaUI] FullscreenSuccess could not be found under the legacy HUD.");
                return;
            }

            // The win panel is a scene-owned behaviour contract. Preserve that
            // instance, move it out before disabling the old HUD, then make it
            // fill the new canvas without carrying legacy siblings along.
            if (fullscreenSuccess.transform.parent != uiRoot.transform)
                fullscreenSuccess.transform.SetParent(uiRoot.transform, false);
            ConfigureFullscreenSuccess(fullscreenSuccess);
            fullscreenSuccess.SetActive(false);

            // These are already standalone Tuna prefabs in the scene. They
            // remain scene instances because LevelTimerUI owns their runtime
            // controls, but must live below the new active canvas rather than
            // the legacy canvas which is about to be disabled.
            Transform panelsRoot = FindDescendant(legacyTimerCanvas, "New_Panels");
            if (panelsRoot != null && panelsRoot.parent != uiRoot.transform)
                panelsRoot.SetParent(uiRoot.transform, false);

            LevelTimerUI timerUi = uiRoot.GetComponentInChildren<LevelTimerUI>(true);
            if (timerUi == null)
            {
                Debug.LogError("[SceneTunaUI] The InGameUI prefab has no LevelTimerUI component.");
                return;
            }

            ConfigureTimerUi(timerUi, uiRoot.transform);
            ConfigureSettingsButtons(uiRoot.transform);
            ConfigureBootstrapReferences(bootstrap, uiRoot.transform, fullscreenSuccess);
            ConfigureBoosterTargetingPresentation(scene, uiRoot.transform);

            legacyTimerCanvas.gameObject.SetActive(false);
            EditorUtility.SetDirty(uiRoot);
            EditorUtility.SetDirty(legacyTimerCanvas.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SceneTunaUI] The complete Scene_Tuna InGameUI prefab is now the active HUD. The legacy Timer_Canvas is disabled for rollback safety.");
        }

        [MenuItem("Gravity Puzzle/Art/Normalize Fullscreen Success Prefab")]
        private static void NormalizeFullscreenSuccessPrefab()
        {
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(FullscreenSuccessPrefabPath);
            try
            {
                ConfigureFullscreenSuccess(prefabContents);
                PrefabUtility.SaveAsPrefabAsset(prefabContents, FullscreenSuccessPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[SceneTunaUI] FullscreenSuccess default copy and layout were normalized.");
        }

        private static GameObject GetOrCreateInGameUi(Scene scene)
        {
            Transform existing = FindSceneRoot(scene, "InGameUI");
            if (existing != null)
                return existing.gameObject;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InGameUiPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[SceneTunaUI] Source prefab was not found at {InGameUiPrefabPath}.");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[SceneTunaUI] InGameUI prefab could not be instantiated into the active scene.");
                return null;
            }

            instance.name = "InGameUI";
            return instance;
        }

        private static void ConfigureTimerUi(LevelTimerUI timerUi, Transform uiRoot)
        {
            GameObject failPanel = FindDescendant(uiRoot, "Fail_Panel")?.gameObject;
            GameObject revivePanel = FindDescendant(uiRoot, "RevivePanel")?.gameObject;
            GameObject retryPanel = FindDescendant(uiRoot, "Retry_Panel")?.gameObject;
            Button liveAdds = FindDescendant(uiRoot, "Live_adds__btn")?.GetComponent<Button>();
            Button playOn = FindDescendant(uiRoot, "Play_on_btn")?.GetComponent<Button>();
            Button closeRevive = FindDescendant(uiRoot, "CloseButton")?.GetComponent<Button>();
            Button failRetry = FindDescendant(failPanel != null ? failPanel.transform : null, "Button")?.GetComponent<Button>();
            Button retryConfirm = FindDescendant(retryPanel != null ? retryPanel.transform : null, "retry_btn")?.GetComponent<Button>();
            Button retryClose = FindDescendant(retryPanel != null ? retryPanel.transform : null, "Retry_Panel_CloseButton")?.GetComponent<Button>();

            SerializedObject serialized = new SerializedObject(timerUi);
            SetReference(serialized, "failPopupPanel", failPanel);
            SetReference(serialized, "keepOnPlayingPanel", revivePanel);
            SetReference(serialized, "liveAddsButton", liveAdds);
            SetReference(serialized, "playOnButton", playOn);
            SetReference(serialized, "closeKeepOnPlayingButton", closeRevive);
            SetReference(serialized, "retryButton", failRetry);
            SetReference(serialized, "retryConfirmationPanel", retryPanel);
            SetReference(serialized, "confirmRetryButton", retryConfirm);
            SetReference(serialized, "closeRetryConfirmationButton", retryClose);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(timerUi);
        }

        private static void ConfigureSettingsButtons(Transform uiRoot)
        {
            GameObject settingsPanel = FindDescendant(uiRoot, "Settings_Panel")?.gameObject;
            if (settingsPanel == null)
            {
                Debug.LogError("[SceneTunaUI] Settings_Panel is missing from InGameUI.");
                return;
            }

            SettingsPanelButton[] buttons = uiRoot.GetComponentsInChildren<SettingsPanelButton>(true);
            for (int index = 0; index < buttons.Length; index++)
                SetReference(buttons[index], "settingsPanel", settingsPanel);
        }

        private static void ConfigureBootstrapReferences(
            RuntimePieceFactoryBootstrap bootstrap,
            Transform uiRoot,
            GameObject fullscreenSuccess)
        {
            // The Tuna HUD deliberately contains several placeholder strings
            // such as "Level 99" (for example, in the retry panel). Resolve
            // the live header by its stable authored object name instead of
            // by placeholder text, otherwise the visible top-HUD label is
            // never updated at runtime.
            TMP_Text levelText = FindDescendant(uiRoot, "Level_Display_txt")?.GetComponent<TMP_Text>();
            TMP_Text retryLevelText = FindTextWithInitialValue(
                FindDescendant(uiRoot, "Retry_Panel"), "Level 99");
            TMP_Text failedLevelText = FindDescendant(uiRoot, "LevelFailText")?.GetComponent<TMP_Text>();

            SetReference(bootstrap, "levelDisplayText", levelText);
            SetReference(bootstrap, "retryLevelText", retryLevelText);
            SetReference(bootstrap, "failedLevelText", failedLevelText);

            PrototypeBoard board = bootstrap.GetComponent<PrototypeBoard>();
            if (board == null)
            {
                Debug.LogError("[SceneTunaUI] PrototypeBoard is missing from Runtime Piece Factory Bootstrap.");
                return;
            }

            SetReference(board, "winPanel", fullscreenSuccess);
            SetReference(board, "nextLevelButton", FindDescendant(fullscreenSuccess.transform, "Claim_btn")?.GetComponent<Button>());
            SetReference(board, "completedLevelText", FindDescendant(fullscreenSuccess.transform, "LevelText (TMP)")?.GetComponent<TMP_Text>());
            SetReference(board, "winPanelCoinAmountText", FindDescendant(fullscreenSuccess.transform, "Coin_Amount")?.GetComponent<TMP_Text>());
            SetReference(board, "newBoosterPanel", uiRoot.GetComponentInChildren<NewBoosterPanelView>(true));
            SetReference(board, "winVfx", FindDescendant(uiRoot, "WinVFX")?.gameObject);
        }

        private static void ConfigureBoosterTargetingPresentation(Scene scene, Transform uiRoot)
        {
            BoosterTargetingPresentation presentation = FindComponentInScene<BoosterTargetingPresentation>(scene);
            if (presentation == null)
            {
                Debug.LogWarning("[SceneTunaUI] BoosterTargetingPresentation is missing; targeting UI was not rebound.");
                return;
            }

            RocketBooster rocket = uiRoot.GetComponentInChildren<RocketBooster>(true);
            HammerBooster hammer = uiRoot.GetComponentInChildren<HammerBooster>(true);
            FreezeTimerBooster freeze = uiRoot.GetComponentInChildren<FreezeTimerBooster>(true);
            SerializedObject serialized = new SerializedObject(presentation);
            SetReference(serialized, "targetingUi", FindDescendant(uiRoot, "Booster Targeting UI")?.gameObject);
            SetReference(serialized, "titleText", FindDescendant(uiRoot, "Booster Title")?.GetComponent<TMP_Text>());
            SetReference(serialized, "instructionText", FindDescendant(uiRoot, "Booster Instruction")?.GetComponent<TMP_Text>());
            SetReference(serialized, "rocketIcon", FindDescendant(uiRoot, "Rocket Icon")?.GetComponent<Image>());
            SetReference(serialized, "hammerIcon", FindDescendant(uiRoot, "Hammer Icon")?.GetComponent<Image>());
            SetReference(serialized, "rocketBoosterButtonGroup", rocket != null ? rocket.GetComponent<CanvasGroup>() : null);
            SetReference(serialized, "hammerBoosterButtonGroup", hammer != null ? hammer.GetComponent<CanvasGroup>() : null);
            SetReference(serialized, "timerBoosterButtonGroup", freeze != null ? freeze.GetComponent<CanvasGroup>() : null);

            // Keep the selected button clickable so a second press deliberately
            // cancels targeting. Non-selected buttons are hidden by the
            // presentation itself; the rest of the new HUD remains visible.
            SerializedProperty hudGroups = serialized.FindProperty("gameplayHudGroups");
            if (hudGroups != null)
                hudGroups.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(presentation);
        }

        private static void ConfigureFullscreenSuccess(GameObject panel)
        {
            if (panel == null)
                return;

            RectTransform root = panel.transform as RectTransform;
            if (root != null)
            {
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
                root.localScale = Vector3.one;
            }

            TMP_Text completedText = FindDescendant(panel.transform, "LevelText (TMP)")?.GetComponent<TMP_Text>();
            if (completedText == null)
                return;

            completedText.text = "Level Completed!";
            RectTransform label = completedText.rectTransform;
            label.anchorMin = new Vector2(.5f, .5f);
            label.anchorMax = new Vector2(.5f, .5f);
            label.pivot = new Vector2(.5f, .5f);
            label.anchoredPosition = new Vector2(0f, 315f);
            label.sizeDelta = new Vector2(760f, 92f);
            label.localScale = Vector3.one;
        }

        private static void SetReference(Object target, string propertyName, Object value)
        {
            if (target == null)
                return;

            SerializedObject serialized = new SerializedObject(target);
            SetReference(serialized, propertyName, value);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetReference(SerializedObject serialized, string propertyName, Object value)
        {
            // The source prefab already owns many nested references. A missing
            // optional child must never erase one of those valid bindings.
            if (value == null)
                return;

            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[SceneTunaUI] Serialized property '{propertyName}' was not found.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static TMP_Text FindTextWithInitialValue(Transform root, string value)
        {
            if (root == null)
                return null;

            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                if (texts[index] != null && texts[index].text == value)
                    return texts[index];
            }

            return null;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                T component = roots[index].GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }

            return null;
        }

        private static Transform FindSceneRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == name)
                    return roots[index].transform;
            }

            return null;
        }

        private static Transform FindDescendant(Transform parent, string name)
        {
            if (parent == null)
                return null;
            if (parent.name == name)
                return parent;

            for (int index = 0; index < parent.childCount; index++)
            {
                Transform result = FindDescendant(parent.GetChild(index), name);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}
#endif
