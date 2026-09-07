using GravityPuzzle.Presentation.Views;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>Creates the reference-style New Booster popup in the open UI scene.</summary>
    internal static class NewBoosterPanelAuthoring
    {
        private const string SpriteSheetPath = "Assets/Art/CommonAssets_UI_LionPack (1).png";
        private const string PopupName = "NewBoosterPanel";
        private const string BangersFontPath = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Bangers SDF.asset";

        [MenuItem("Gravity Puzzle/UI/Create New Booster Panel")]
        private static void CreateNewBoosterPanel()
        {
            Canvas canvas = FindRootCanvas();
            if (canvas == null)
            {
                Debug.LogError("[New Booster Panel] Open a UI scene containing a Canvas before creating the panel.");
                return;
            }

            EnsureEventSystem();

            Sprite panelSprite = GetSprite("Panel_Blue_Tall");
            Sprite headerSprite = GetSprite("Header_Yellow");
            Sprite closeSprite = GetSprite("Icon_Close_Red");
            Sprite contentSprite = GetSprite("Panel_Blue_Square");
            Sprite continueSprite = GetSprite("Button_Green_Large");
            if (panelSprite == null || headerSprite == null || closeSprite == null || contentSprite == null || continueSprite == null)
            {
                Debug.LogError("[New Booster Panel] Required LionPack sprites could not be found.");
                return;
            }

            Transform existing = canvas.transform.Find(PopupName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            GameObject root = CreateUiObject(PopupName, canvas.transform);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            SetRect(rootRect, new Vector2(.5f, .5f), Vector2.zero, new Vector2(650f, 820f));

            Image shell = AddImage(root, panelSprite, false);
            shell.preserveAspect = false;
            Stretch(shell.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Image content = AddImage(CreateUiObject("BoosterContent", root.transform), contentSprite, false);
            content.preserveAspect = false;
            SetRect(content.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 24f), new Vector2(505f, 440f));

            Image header = AddImage(CreateUiObject("Header", root.transform), headerSprite, false);
            header.preserveAspect = true;
            SetRect(header.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -16f), new Vector2(430f, 150f));
            AddLabel("Title", header.transform, "New Booster", 58f, Vector2.zero, new Vector2(395f, 92f));

            Button closeButton = AddButton("CloseButton", root.transform, closeSprite, new Vector2(.86f, .91f), Vector2.zero, new Vector2(96f, 96f));
            Button continueButton = AddButton("ContinueButton", root.transform, continueSprite, new Vector2(.5f, 0f), new Vector2(0f, 93f), new Vector2(330f, 112f));
            AddLabel("Label", continueButton.transform, "Continue", 47f, new Vector2(0f, 1f), new Vector2(295f, 76f));

            NewBoosterPanelView panelView = root.AddComponent<NewBoosterPanelView>();
            SerializedObject serializedPanel = new SerializedObject(panelView);
            serializedPanel.FindProperty("closeButton").objectReferenceValue = closeButton;
            serializedPanel.FindProperty("continueButton").objectReferenceValue = continueButton;
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(root, "Create New Booster Panel");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            root.SetActive(false);
        }

        private static Canvas FindRootCanvas()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < canvases.Length; index++)
            {
                Canvas canvas = canvases[index];
                if (canvas.isRootCanvas && canvas.gameObject.scene == SceneManager.GetActiveScene())
                    return canvas;
            }

            return null;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
                return;

            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(eventSystem, "Create Event System");
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

        private static void AddLabel(string name, Transform parent, string value, float fontSize, Vector2 position, Vector2 size)
        {
            GameObject gameObject = CreateUiObject(name, parent);
            TextMeshProUGUI label = gameObject.AddComponent<TextMeshProUGUI>();
            label.text = value;
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BangersFontPath);
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.outlineColor = new Color(.22f, .15f, .04f, 1f);
            label.outlineWidth = .18f;
            label.raycastTarget = false;
            SetRect(label.rectTransform, new Vector2(.5f, .5f), position, size);
        }

        private static void SetRect(RectTransform rectTransform, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = new Vector2(.5f, .5f);
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
