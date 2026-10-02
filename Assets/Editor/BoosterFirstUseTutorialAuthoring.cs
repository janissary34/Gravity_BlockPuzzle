using GravityPuzzle.Presentation.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Creates the scene-owned tutorial pointer once. Artists can replace the
    /// generated Image/ripple children without changing tutorial code.
    /// </summary>
    internal static class BoosterFirstUseTutorialAuthoring
    {
        private const string HandRootName = "Booster Tutorial Hand";
        private const string HandSpritePath =
            "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Sprites/Components/IconMisc/Icon_ImageIcon_Tutorial_Hand.png";
        private const string RippleSpritePath =
            "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Sprites/Demo/Demo_Image/Common_White_Oval.png";

        [MenuItem("Gravity Puzzle/UI/Configure First-Use Tutorial Presentation")]
        private static void Configure()
        {
            NewBoosterPanelView panel = FindPanel();
            Canvas canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
            {
                Debug.LogError("[BoosterTutorial] NewBoosterPanel must be under a Canvas before configuring its tutorial pointer.");
                return;
            }

            Transform existing = canvas.transform.Find(HandRootName);
            TutorialHandView handView = existing != null
                ? existing.GetComponent<TutorialHandView>()
                : CreateHandView(canvas.transform);
            if (handView == null)
                return;

            SerializedObject serializedHand = new SerializedObject(handView);
            serializedHand.FindProperty("handRoot").objectReferenceValue = handView.transform;
            serializedHand.ApplyModifiedPropertiesWithoutUndo();
            handView.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            handView.transform.SetAsLastSibling();

            ConfigureBoardTargetIndicator();

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[BoosterTutorial] Scene tutorial hand is configured. Artists may now replace its Hand and Ripple children.", handView);
        }

        private static TutorialHandView CreateHandView(Transform canvasTransform)
        {
            Sprite handSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HandSpritePath);
            Sprite rippleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RippleSpritePath);
            if (handSprite == null)
            {
                Debug.LogError("[BoosterTutorial] Tutorial hand sprite could not be loaded.");
                return null;
            }

            GameObject root = new GameObject(HandRootName, typeof(RectTransform), typeof(Image), typeof(TutorialHandView));
            Undo.RegisterCreatedObjectUndo(root, "Create booster tutorial hand");
            RectTransform rootTransform = root.GetComponent<RectTransform>();
            rootTransform.SetParent(canvasTransform, false);
            rootTransform.anchorMin = new Vector2(.5f, .5f);
            rootTransform.anchorMax = new Vector2(.5f, .5f);
            rootTransform.pivot = new Vector2(.5f, .5f);
            rootTransform.sizeDelta = new Vector2(132f, 132f);
            rootTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            Image handImage = root.GetComponent<Image>();
            handImage.sprite = handSprite;
            handImage.preserveAspect = true;
            handImage.raycastTarget = false;

            if (rippleSprite != null)
                CreateRipple(rootTransform, rippleSprite);

            return root.GetComponent<TutorialHandView>();
        }

        private static void ConfigureBoardTargetIndicator()
        {
            BoosterTargetingPresentation[] presentations = Object.FindObjectsByType<BoosterTargetingPresentation>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (presentations.Length == 0)
            {
                Debug.LogError("[BoosterTutorial] BoosterTargetingPresentation was not found in the active scene.");
                return;
            }

            BoosterTargetingPresentation presentation = presentations[0];
            Transform existing = presentation.transform.Find("Tutorial Target Indicator");
            TutorialTargetIndicatorView indicator = existing != null
                ? existing.GetComponent<TutorialTargetIndicatorView>()
                : CreateTargetIndicator(presentation.transform);
            if (indicator == null)
                return;

            SerializedObject serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("tutorialTargetIndicator").objectReferenceValue = indicator;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TutorialTargetIndicatorView CreateTargetIndicator(Transform parent)
        {
            Transform backdrop = parent.Find("Board Dim Backdrop");
            SpriteRenderer backdropRenderer = backdrop != null ? backdrop.GetComponent<SpriteRenderer>() : null;
            if (backdropRenderer == null || backdropRenderer.sprite == null)
            {
                Debug.LogError("[BoosterTutorial] Board Dim Backdrop needs a SpriteRenderer with a sprite.");
                return null;
            }

            GameObject root = new GameObject("Tutorial Target Indicator", typeof(TutorialTargetIndicatorView));
            Undo.RegisterCreatedObjectUndo(root, "Create tutorial target indicator");
            root.transform.SetParent(parent, false);

            SpriteRenderer brightness = CreateTargetSprite(
                "Brightness", root.transform, backdropRenderer.sprite, new Color(1f, .76f, .12f, .48f));
            SpriteRenderer horizontal = CreateTargetSprite(
                "Horizontal Sight", root.transform, backdropRenderer.sprite, Color.white);
            SpriteRenderer vertical = CreateTargetSprite(
                "Vertical Sight", root.transform, backdropRenderer.sprite, Color.white);

            TutorialTargetIndicatorView indicator = root.GetComponent<TutorialTargetIndicatorView>();
            SerializedObject serializedIndicator = new SerializedObject(indicator);
            serializedIndicator.FindProperty("brightnessOverlay").objectReferenceValue = brightness;
            serializedIndicator.FindProperty("horizontalSight").objectReferenceValue = horizontal;
            serializedIndicator.FindProperty("verticalSight").objectReferenceValue = vertical;
            serializedIndicator.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false);
            return indicator;
        }

        private static SpriteRenderer CreateTargetSprite(
            string name,
            Transform parent,
            Sprite sprite,
            Color color)
        {
            GameObject target = new GameObject(name, typeof(SpriteRenderer));
            target.transform.SetParent(parent, false);
            SpriteRenderer renderer = target.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            return renderer;
        }

        private static void CreateRipple(RectTransform parent, Sprite sprite)
        {
            GameObject ripple = new GameObject("Ripple", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(ripple, "Create booster tutorial ripple");
            RectTransform transform = ripple.GetComponent<RectTransform>();
            transform.SetParent(parent, false);
            transform.anchorMin = new Vector2(.5f, .5f);
            transform.anchorMax = new Vector2(.5f, .5f);
            transform.sizeDelta = new Vector2(72f, 72f);

            Image image = ripple.GetComponent<Image>();
            image.sprite = sprite;
            image.color = new Color(1f, .78f, .2f, .9f);
            image.raycastTarget = false;

            TutorialHandView handView = parent.GetComponent<TutorialHandView>();
            SerializedObject serializedHand = new SerializedObject(handView);
            serializedHand.FindProperty("rippleRoot").objectReferenceValue = transform;
            serializedHand.FindProperty("rippleCanvasGroup").objectReferenceValue = ripple.GetComponent<CanvasGroup>();
            serializedHand.ApplyModifiedPropertiesWithoutUndo();
        }

        private static NewBoosterPanelView FindPanel()
        {
            NewBoosterPanelView[] panels = Object.FindObjectsByType<NewBoosterPanelView>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            return panels.Length > 0 ? panels[0] : null;
        }
    }
}
