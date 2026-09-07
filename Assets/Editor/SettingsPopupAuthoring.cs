using GravityPuzzle.Presentation.Views;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>Creates the Settings popup from the sliced LionPack sprites in the open scene.</summary>
    internal static class SettingsPopupAuthoring
    {
        private const string SpriteSheetPath = "Assets/Art/CommonAssets_UI_LionPack (1).png";
        private const string PopupName = "SettingsPopup";

        [MenuItem("Gravity Puzzle/UI/Create Settings Popup")]
        private static void CreateSettingsPopup()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Settings Popup] Open a UI scene containing a Canvas before creating the popup.");
                return;
            }

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(eventSystem, "Create Event System");
            }

            Sprite panelSprite = GetSprite("Panel_Blue_Large");
            Sprite headerSprite = GetSprite("Header_Yellow");
            Sprite closeSprite = GetSprite("Icon_Close_Red");
            Sprite innerPanelSprite = GetSprite("Panel_Blue_Square");
            Sprite soundSprite = GetSprite("Icon_Sound");
            Sprite vibrationSprite = GetSprite("Icon_Vibration");
            Sprite toggleOffSprite = GetSprite("Toggle_Purple");
            Sprite toggleOnSprite = GetSprite("Toggle_Green");

            if (panelSprite == null || headerSprite == null || closeSprite == null || innerPanelSprite == null ||
                soundSprite == null || vibrationSprite == null || toggleOffSprite == null || toggleOnSprite == null)
            {
                Debug.LogError("[Settings Popup] One or more required sprites are missing. Check the sprite names in the LionPack sheet.");
                return;
            }

            Transform existing = canvas.transform.Find(PopupName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            GameObject root = CreateUiObject(PopupName, canvas.transform);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(720f, 620f);

            Image panel = AddImage(root, panelSprite, false);
            // The supplied background is intentionally wider in the reference UI.
            // It is a decorative shell, so the UI owns its landscape presentation size.
            panel.preserveAspect = false;
            Stretch(panel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Image innerPanel = AddImage(CreateUiObject("OptionsPanel", root.transform), innerPanelSprite, false);
            innerPanel.preserveAspect = false;
            SetRect(innerPanel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -35f), new Vector2(535f, 300f));

            Image header = AddImage(CreateUiObject("Header", root.transform), headerSprite, false);
            header.preserveAspect = true;
            SetRect(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(460f, 160f));
            AddLabel("Title", header.transform, "Settings", 56, Color.white, new Vector2(0f, -6f), new Vector2(400f, 96f));

            Button closeButton = AddButton("CloseButton", root.transform, closeSprite, new Vector2(0.87f, 0.87f), new Vector2(0f, 0f), new Vector2(105f, 105f));

            AddImage(CreateUiObject("SoundIcon", root.transform), soundSprite, false).rectTransform
                .SetParent(root.transform, false);
            SetRect(root.transform.Find("SoundIcon").GetComponent<RectTransform>(), new Vector2(0.22f, 0.60f), Vector2.zero, new Vector2(100f, 100f));
            AddLabel("SoundLabel", root.transform, "Sound", 42, Color.white, new Vector2(-50f, 76f), new Vector2(175f, 68f));
            Button soundToggle = AddButton("SoundToggle", root.transform, toggleOffSprite, new Vector2(0.70f, 0.60f), Vector2.zero, new Vector2(150f, 64f));

            AddImage(CreateUiObject("VibrationIcon", root.transform), vibrationSprite, false).rectTransform
                .SetParent(root.transform, false);
            SetRect(root.transform.Find("VibrationIcon").GetComponent<RectTransform>(), new Vector2(0.22f, 0.37f), Vector2.zero, new Vector2(100f, 100f));
            AddLabel("VibrationLabel", root.transform, "Vibration", 42, Color.white, new Vector2(-45f, -67f), new Vector2(205f, 68f));
            Button vibrationToggle = AddButton("VibrationToggle", root.transform, toggleOnSprite, new Vector2(0.70f, 0.37f), Vector2.zero, new Vector2(150f, 64f));

            SettingsPopupView popupView = root.AddComponent<SettingsPopupView>();
            SerializedObject serializedPopup = new SerializedObject(popupView);
            serializedPopup.FindProperty("closeButton").objectReferenceValue = closeButton;
            serializedPopup.FindProperty("soundToggleButton").objectReferenceValue = soundToggle;
            serializedPopup.FindProperty("vibrationToggleButton").objectReferenceValue = vibrationToggle;
            serializedPopup.FindProperty("soundToggleImage").objectReferenceValue = soundToggle.targetGraphic;
            serializedPopup.FindProperty("vibrationToggleImage").objectReferenceValue = vibrationToggle.targetGraphic;
            serializedPopup.FindProperty("toggleOffSprite").objectReferenceValue = toggleOffSprite;
            serializedPopup.FindProperty("toggleOnSprite").objectReferenceValue = toggleOnSprite;
            serializedPopup.ApplyModifiedPropertiesWithoutUndo();

            SettingsPanelButton settingsController = Object.FindFirstObjectByType<SettingsPanelButton>();
            if (settingsController != null)
            {
                SerializedObject serializedController = new SerializedObject(settingsController);
                serializedController.FindProperty("settingsPanel").objectReferenceValue = root;
                serializedController.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settingsController);
            }
            else
            {
                Debug.LogWarning("[Settings Popup] The popup was created, but no SettingsPanelButton was found to open it.");
            }

            Undo.RegisterCreatedObjectUndo(root, "Create Settings Popup");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        private static Sprite GetSprite(string spriteName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(SpriteSheetPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Sprite sprite && sprite.name == spriteName)
                    return sprite;
            }

            return null;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = 5;
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static Image AddImage(GameObject gameObject, Sprite sprite, bool raycastTarget)
        {
            Image image = gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private static Button AddButton(string name, Transform parent, Sprite sprite, Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject gameObject = CreateUiObject(name, parent);
            Image image = AddImage(gameObject, sprite, true);
            image.preserveAspect = true;
            Button button = gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            SetRect(image.rectTransform, anchor, position, size);
            return button;
        }

        private static void AddLabel(string name, Transform parent, string value, float fontSize, Color color, Vector2 position, Vector2 size)
        {
            GameObject gameObject = CreateUiObject(name, parent);
            TextMeshProUGUI label = gameObject.AddComponent<TextMeshProUGUI>();
            label.text = value;
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            SetRect(label.rectTransform, new Vector2(0.5f, 0.5f), position, size);
        }

        private static void SetRect(RectTransform rectTransform, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rectTransform.anchorMin = rectTransform.anchorMax = anchor;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;
        }

        private static void Stretch(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }
    }
}
