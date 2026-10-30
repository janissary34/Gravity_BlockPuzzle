#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using GravityPuzzle.Bootstrap;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Gameplay.Reveal;
using GravityPuzzle.Presentation.Views;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityPuzzle.Editor
{
    public static class SceneTunaVisualAuthoring
    {
        private const string MapAtlasPath = "Assets/Art/map.png";
        private const string IceMaterialPath = "Assets/Materials/M_Ice.mat";
        private const string TMPFontPath = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Fonts/LilitaOne-Regular_BlueDark.asset";
        private const string BoxPanelPath = "Assets/Art/BoxReveal/TunnelShutter.png";
        private const string BoxBackingPath = "Assets/Art/BoxReveal/BoxBacking.png";
        private const string BoxFragmentMaterialPath = "Assets/Materials/BoxFragment.mat";
        private const string BoxPanelMaterialPath = "Assets/Materials/BoxShutter.mat";
        private const string BoxPrefabPath = "Assets/Prefabs/BoxRevealArea.prefab";
        private const string ElevatorPrefabPath = "Assets/Prefabs/ElevatorRevealArea.prefab";
        private const string SpringPrefabPath = "Assets/Prefabs/JointSpringView.prefab";
        private const string RevealConfigPath = "Assets/Config/RevealPresentationConfig.asset";
        private const string PieceVisualConfigPath = "Assets/PieceVisualConfig.asset";
        private const string BlockPiecePrefabPath = "Assets/Prefabs/BlockPiece.prefab";

        [MenuItem("Gravity Puzzle/Art/Author Scene Tuna Shared Assets")]
        public static void AuthorAll()
        {
            EnsurePrefabsFolder();

            Sprite tunnelSprite = LoadSprite("Tunnel");
            Sprite elevatorSprite = LoadSprite("Elevator");
            Sprite elevatorDoorSprite = LoadSprite("Elevator_Door");
            Sprite springSprite = LoadSprite("SpiralSpring");
            Material iceMaterial = AssetDatabase.LoadAssetAtPath<Material>(IceMaterialPath);
            TMP_FontAsset fontAsset = LoadTMPFont();

            RevealPresentationConfig existingRevealConfig = AssetDatabase.LoadAssetAtPath<RevealPresentationConfig>(RevealConfigPath);
            int previewSlotCapacity = existingRevealConfig != null
                ? Mathf.Max(1, existingRevealConfig.boxPreviewSlotCapacity)
                : RevealPresentationConfig.DefaultBoxPreviewSlotCapacity;
            BoxRevealAreaView boxPrefab = CreateBoxRevealPrefab(
                tunnelSprite,
                iceMaterial,
                fontAsset,
                previewSlotCapacity);
            ElevatorRevealAreaView elevatorPrefab = CreateElevatorRevealPrefab(
                elevatorSprite,
                elevatorDoorSprite,
                iceMaterial);
            JointSpringView springPrefab = CreateJointSpringPrefab(springSprite, iceMaterial);

            UpdateRevealPresentationConfig(
                boxPrefab,
                elevatorPrefab,
                tunnelSprite,
                elevatorSprite,
                elevatorDoorSprite,
                fontAsset);
            UpdatePieceVisualConfig(fontAsset);
            AddBombCounterToBlockPiece(fontAsset);

            ApplyToScene("Assets/Scenes/Scene1.unity", springPrefab, springSprite, iceMaterial);
            ApplyToScene("Assets/Scenes/Scene_Tuna.unity", springPrefab, springSprite, iceMaterial);

            AssetDatabase.SaveAssets();
            Debug.Log("[SceneTunaVisualAuthoring] Successfully authored all prefabs, configs, and scenes!");
        }

        private static void EnsurePrefabsFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        private static BoxRevealAreaView CreateBoxRevealPrefab(
            Sprite tunnelSprite,
            Material iceMaterial,
            TMP_FontAsset font,
            int previewSlotCapacity)
        {
            // The map atlas supplies the authored open-bottom Tunnel frame.
            // It is the only frame renderer in this composition.
            Sprite frameSprite = tunnelSprite;
            Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BoxPanelPath);
            Sprite backingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BoxBackingPath);
            Material panelMaterial = AssetDatabase.LoadAssetAtPath<Material>(BoxPanelMaterialPath);
            Material fragmentMaterial = AssetDatabase.LoadAssetAtPath<Material>(BoxFragmentMaterialPath);
            if (frameSprite == null || panelSprite == null || backingSprite == null || panelMaterial == null || fragmentMaterial == null)
                throw new System.InvalidOperationException("Box reveal artwork or shutter material is missing; cannot author the box prefab.");

            GameObject root = new GameObject("BoxRevealArea");
            try
            {
                BoxRevealAreaView view = root.AddComponent<BoxRevealAreaView>();

                GameObject shutter = new GameObject("Shutter");
                shutter.transform.SetParent(root.transform, false);

                GameObject cover = new GameObject("Tunnel Frame");
                cover.transform.SetParent(shutter.transform, false);
                SpriteRenderer coverRenderer = cover.AddComponent<SpriteRenderer>();
                coverRenderer.sprite = frameSprite;
                if (iceMaterial != null)
                    coverRenderer.material = iceMaterial;
                coverRenderer.drawMode = SpriteDrawMode.Sliced;
                coverRenderer.size = new Vector2(3f, 3f);
                coverRenderer.sortingOrder = 200;

                GameObject backing = new GameObject("Box Opaque Backing");
                backing.transform.SetParent(shutter.transform, false);
                SpriteRenderer backingRenderer = backing.AddComponent<SpriteRenderer>();
                backingRenderer.sprite = backingSprite;
                if (iceMaterial != null)
                    backingRenderer.sharedMaterial = iceMaterial;
                backingRenderer.sortingOrder = 198;

                GameObject panel = new GameObject("Box Moving Shutter");
                panel.transform.SetParent(shutter.transform, false);
                SpriteRenderer panelRenderer = panel.AddComponent<SpriteRenderer>();
                panelRenderer.sprite = panelSprite;
                panelRenderer.sharedMaterial = panelMaterial;
                panelRenderer.sortingOrder = 199;

                CreateBoxPreviewSlots(
                    shutter.transform,
                    backingSprite,
                    previewSlotCapacity,
                    out SpriteMask previewMask,
                    out SpriteRenderer[] previewShadowRenderers,
                    out SpriteRenderer[] previewRenderers);

                GameObject counterObj = new GameObject("Counter");
                counterObj.transform.SetParent(shutter.transform, false);
                counterObj.transform.localPosition = new Vector3(0f, 0f, -0.1f);
                TextMeshPro counterTmp = counterObj.AddComponent<TextMeshPro>();
                if (font != null)
                    counterTmp.font = font;
                counterTmp.fontSize = .55f;
                counterTmp.color = Color.white;
                counterTmp.outlineColor = new Color(0.055f, 0.08f, 0.28f, 1f);
                counterTmp.outlineWidth = 0.22f;
                counterTmp.alignment = TextAlignmentOptions.Center;
                counterTmp.text = "5";
                counterTmp.rectTransform.sizeDelta = new Vector2(20f, 5f);
                counterTmp.GetComponent<MeshRenderer>().sortingOrder = 201;

                const int burstColumns = 6;
                const int burstRows = 2;
                Transform[] fragments = new Transform[burstColumns * burstRows];
                SpriteRenderer[] fragmentRenderers = new SpriteRenderer[burstColumns * burstRows];

                for (int i = 0; i < burstColumns * burstRows; i++)
                {
                    GameObject frag = new GameObject($"Burst Fragment {i + 1}");
                    frag.transform.SetParent(shutter.transform, false);
                    SpriteRenderer fragRenderer = frag.AddComponent<SpriteRenderer>();
                    fragRenderer.sprite = tunnelSprite;
                    fragRenderer.sharedMaterial = fragmentMaterial;
                    fragRenderer.drawMode = SpriteDrawMode.Sliced;
                    fragRenderer.sortingOrder = 201;
                    frag.SetActive(false);
                    fragments[i] = frag.transform;
                    fragmentRenderers[i] = fragRenderer;
                }

                SerializedObject so = new SerializedObject(view);
                so.FindProperty("boxShutter").objectReferenceValue = shutter.transform;
                so.FindProperty("boxCoverRenderer").objectReferenceValue = coverRenderer;
                so.FindProperty("boxBackingRenderer").objectReferenceValue = backingRenderer;
                so.FindProperty("boxPanelRenderer").objectReferenceValue = panelRenderer;
                so.FindProperty("boxCounter").objectReferenceValue = counterTmp;
                so.FindProperty("previewMask").objectReferenceValue = previewMask;

                SerializedProperty previewShadowProp = so.FindProperty("boxPreviewShadowRenderers");
                previewShadowProp.arraySize = previewShadowRenderers.Length;
                for (int i = 0; i < previewShadowRenderers.Length; i++)
                    previewShadowProp.GetArrayElementAtIndex(i).objectReferenceValue = previewShadowRenderers[i];

                SerializedProperty previewProp = so.FindProperty("boxPreviewRenderers");
                previewProp.arraySize = previewRenderers.Length;
                for (int i = 0; i < previewRenderers.Length; i++)
                    previewProp.GetArrayElementAtIndex(i).objectReferenceValue = previewRenderers[i];

                SerializedProperty fragsProp = so.FindProperty("boxBurstFragments");
                fragsProp.arraySize = fragments.Length;
                for (int i = 0; i < fragments.Length; i++)
                    fragsProp.GetArrayElementAtIndex(i).objectReferenceValue = fragments[i];

                SerializedProperty rendsProp = so.FindProperty("boxBurstRenderers");
                rendsProp.arraySize = fragmentRenderers.Length;
                for (int i = 0; i < fragmentRenderers.Length; i++)
                    rendsProp.GetArrayElementAtIndex(i).objectReferenceValue = fragmentRenderers[i];

                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, BoxPrefabPath);
                return savedPrefab.GetComponent<BoxRevealAreaView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("Gravity Puzzle/Art/Refresh Box Reveal Preview Slots")]
        public static void RefreshBoxRevealPreviewSlots()
        {
            RevealPresentationConfig config = AssetDatabase.LoadAssetAtPath<RevealPresentationConfig>(RevealConfigPath);
            BoxRevealAreaView prefab = AssetDatabase.LoadAssetAtPath<BoxRevealAreaView>(BoxPrefabPath);
            if (config == null || prefab == null)
            {
                Debug.LogError("[SceneTunaVisualAuthoring] RevealPresentationConfig or BoxRevealArea prefab is missing.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(BoxPrefabPath);
            try
            {
                BoxRevealAreaView view = root.GetComponent<BoxRevealAreaView>();
                Transform shutter = root.transform.Find("Shutter");
                Sprite backingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BoxBackingPath);
                if (view == null || shutter == null || backingSprite == null)
                    throw new System.InvalidOperationException("Box preview slots require the authored Box view, Shutter, and opaque mask sprite.");

                CreateBoxPreviewSlots(
                    shutter,
                    backingSprite,
                    Mathf.Max(1, config.boxPreviewSlotCapacity),
                    out SpriteMask previewMask,
                    out SpriteRenderer[] previewShadowRenderers,
                    out SpriteRenderer[] previewRenderers);
                SerializedObject serializedView = new SerializedObject(view);
                serializedView.FindProperty("previewMask").objectReferenceValue = previewMask;
                SerializedProperty previewShadowProp = serializedView.FindProperty("boxPreviewShadowRenderers");
                previewShadowProp.arraySize = previewShadowRenderers.Length;
                for (int index = 0; index < previewShadowRenderers.Length; index++)
                    previewShadowProp.GetArrayElementAtIndex(index).objectReferenceValue = previewShadowRenderers[index];
                SerializedProperty previewProp = serializedView.FindProperty("boxPreviewRenderers");
                previewProp.arraySize = previewRenderers.Length;
                for (int index = 0; index < previewRenderers.Length; index++)
                    previewProp.GetArrayElementAtIndex(index).objectReferenceValue = previewRenderers[index];
                serializedView.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, BoxPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[SceneTunaVisualAuthoring] Refreshed Box reveal preview slots.");
        }

        private static void CreateBoxPreviewSlots(
            Transform shutter,
            Sprite maskSprite,
            int capacity,
            out SpriteMask previewMask,
            out SpriteRenderer[] previewShadowRenderers,
            out SpriteRenderer[] previewRenderers)
        {
            Transform previewRoot = shutter.Find("Preview Content");
            if (previewRoot == null)
            {
                GameObject previewRootObject = new GameObject("Preview Content");
                previewRootObject.transform.SetParent(shutter, false);
                previewRoot = previewRootObject.transform;
            }

            Transform maskTransform = previewRoot.Find("Aperture Mask");
            if (maskTransform == null)
            {
                GameObject maskObject = new GameObject("Aperture Mask");
                maskObject.transform.SetParent(previewRoot, false);
                maskTransform = maskObject.transform;
            }

            previewMask = maskTransform.GetComponent<SpriteMask>();
            if (previewMask == null)
                previewMask = maskTransform.gameObject.AddComponent<SpriteMask>();
            // Preview content is clipped to the rectangular reveal area, not
            // the rounded shutter artwork. The static outer Tunnel is the only
            // visible frame and remains above these renderers.
            previewMask.sprite = maskSprite;
            previewMask.isCustomRangeActive = true;
            previewMask.backSortingOrder = 196;
            previewMask.frontSortingOrder = 199;

            List<SpriteRenderer> shadowRenderers = new List<SpriteRenderer>(capacity);
            List<SpriteRenderer> renderers = new List<SpriteRenderer>(capacity);
            for (int index = 0; index < capacity; index++)
            {
                Transform shadowSlot = previewRoot.Find($"Preview Shadow Slot {index + 1}");
                if (shadowSlot == null)
                {
                    GameObject shadowSlotObject = new GameObject($"Preview Shadow Slot {index + 1}");
                    shadowSlotObject.transform.SetParent(previewRoot, false);
                    shadowSlot = shadowSlotObject.transform;
                }

                SpriteRenderer shadowRenderer = shadowSlot.GetComponent<SpriteRenderer>();
                if (shadowRenderer == null)
                    shadowRenderer = shadowSlot.gameObject.AddComponent<SpriteRenderer>();
                shadowRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                shadowRenderer.sortingOrder = 197;
                shadowRenderer.enabled = true;
                shadowSlot.gameObject.SetActive(false);
                shadowRenderers.Add(shadowRenderer);

                Transform slot = previewRoot.Find($"Preview Slot {index + 1}");
                if (slot == null)
                {
                    GameObject slotObject = new GameObject($"Preview Slot {index + 1}");
                    slotObject.transform.SetParent(previewRoot, false);
                    slot = slotObject.transform;
                }

                SpriteRenderer renderer = slot.GetComponent<SpriteRenderer>();
                if (renderer == null)
                    renderer = slot.gameObject.AddComponent<SpriteRenderer>();
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                renderer.sortingOrder = 198;
                renderer.enabled = true;
                slot.gameObject.SetActive(false);
                renderers.Add(renderer);
            }

            for (int index = previewRoot.childCount - 1; index >= 0; index--)
            {
                Transform child = previewRoot.GetChild(index);
                if (child == maskTransform || IsPreviewSlot(child.name, "Preview Slot ", capacity) ||
                    IsPreviewSlot(child.name, "Preview Shadow Slot ", capacity))
                {
                    continue;
                }

                Object.DestroyImmediate(child.gameObject);
            }

            previewShadowRenderers = shadowRenderers.ToArray();
            previewRenderers = renderers.ToArray();
        }

        private static bool IsPreviewSlot(string objectName, string prefix, int capacity)
        {
            return TryGetPreviewSlotIndex(objectName, prefix, out int index) && index <= capacity;
        }

        private static bool TryGetPreviewSlotIndex(string objectName, string prefix, out int index)
        {
            if (!objectName.StartsWith(prefix))
            {
                index = 0;
                return false;
            }

            return int.TryParse(objectName.Substring(prefix.Length), out index);
        }

        private static ElevatorRevealAreaView CreateElevatorRevealPrefab(
            Sprite elevatorSprite,
            Sprite elevatorDoorSprite,
            Material iceMaterial)
        {
            GameObject root = new GameObject("ElevatorRevealArea");
            try
            {
                ElevatorRevealAreaView view = root.AddComponent<ElevatorRevealAreaView>();

                GameObject frame = new GameObject("Elevator Frame");
                frame.transform.SetParent(root.transform, false);
                SpriteRenderer frameRenderer = frame.AddComponent<SpriteRenderer>();
                frameRenderer.sprite = elevatorSprite;
                frameRenderer.drawMode = SpriteDrawMode.Sliced;
                frameRenderer.size = Vector2.one;
                if (iceMaterial != null)
                    frameRenderer.material = iceMaterial;
                frameRenderer.sortingOrder = 1;

                GameObject opening = new GameObject("Elevator Opening");
                opening.transform.SetParent(root.transform, false);
                SpriteRenderer openingRenderer = opening.AddComponent<SpriteRenderer>();
                openingRenderer.drawMode = SpriteDrawMode.Sliced;
                openingRenderer.sortingOrder = 2;
                openingRenderer.color = new Color(0.04f, 0.08f, 0.14f, 1f);

                GameObject leftDoor = new GameObject("Left Door");
                leftDoor.transform.SetParent(root.transform, false);
                SpriteRenderer leftDoorRenderer = leftDoor.AddComponent<SpriteRenderer>();
                leftDoorRenderer.sprite = elevatorDoorSprite;
                leftDoorRenderer.drawMode = SpriteDrawMode.Sliced;
                leftDoorRenderer.size = Vector2.one;
                leftDoorRenderer.flipX = true;
                if (iceMaterial != null)
                    leftDoorRenderer.material = iceMaterial;
                leftDoorRenderer.sortingOrder = 3;

                GameObject rightDoor = new GameObject("Right Door");
                rightDoor.transform.SetParent(root.transform, false);
                SpriteRenderer rightDoorRenderer = rightDoor.AddComponent<SpriteRenderer>();
                rightDoorRenderer.sprite = elevatorDoorSprite;
                rightDoorRenderer.drawMode = SpriteDrawMode.Sliced;
                rightDoorRenderer.size = Vector2.one;
                if (iceMaterial != null)
                    rightDoorRenderer.material = iceMaterial;
                rightDoorRenderer.sortingOrder = 4;

                SerializedObject so = new SerializedObject(view);
                so.FindProperty("frameRenderer").objectReferenceValue = frameRenderer;
                so.FindProperty("openingRenderer").objectReferenceValue = openingRenderer;
                so.FindProperty("elevatorLeftDoor").objectReferenceValue = leftDoor.transform;
                so.FindProperty("elevatorRightDoor").objectReferenceValue = rightDoor.transform;

                SerializedProperty doorRendsProp = so.FindProperty("doorRenderers");
                doorRendsProp.arraySize = 2;
                doorRendsProp.GetArrayElementAtIndex(0).objectReferenceValue = leftDoorRenderer;
                doorRendsProp.GetArrayElementAtIndex(1).objectReferenceValue = rightDoorRenderer;

                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, ElevatorPrefabPath);
                return savedPrefab.GetComponent<ElevatorRevealAreaView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static JointSpringView CreateJointSpringPrefab(Sprite springSprite, Material iceMaterial)
        {
            GameObject root = new GameObject("JointSpringView");
            try
            {
                JointSpringView view = root.AddComponent<JointSpringView>();
                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = springSprite;
                if (iceMaterial != null)
                    renderer.material = iceMaterial;
                renderer.sortingOrder = 20;
                root.transform.localScale = Vector3.one * 0.1495036f;

                SerializedObject so = new SerializedObject(view);
                so.FindProperty("spriteRenderer").objectReferenceValue = renderer;
                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, SpringPrefabPath);
                return savedPrefab.GetComponent<JointSpringView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void UpdateRevealPresentationConfig(
            BoxRevealAreaView boxPrefab,
            ElevatorRevealAreaView elevatorPrefab,
            Sprite tunnelSprite,
            Sprite elevatorSprite,
            Sprite elevatorDoorSprite,
            TMP_FontAsset font)
        {
            RevealPresentationConfig config = AssetDatabase.LoadAssetAtPath<RevealPresentationConfig>(RevealConfigPath);
            if (config == null)
                return;

            SerializedObject so = new SerializedObject(config);
            so.FindProperty("boxPrefab").objectReferenceValue = boxPrefab;
            so.FindProperty("boxPoolCapacity").intValue = 8;
            so.FindProperty("elevatorPrefab").objectReferenceValue = elevatorPrefab;
            so.FindProperty("elevatorPoolCapacity").intValue = 4;
            so.FindProperty("boxCoverSprite").objectReferenceValue = tunnelSprite;
            so.FindProperty("elevatorFrameSprite").objectReferenceValue = elevatorSprite;
            so.FindProperty("elevatorLeftDoorSprite").objectReferenceValue = elevatorDoorSprite;
            so.FindProperty("elevatorRightDoorSprite").objectReferenceValue = elevatorDoorSprite;
            so.FindProperty("elevatorDoorSprite").objectReferenceValue = elevatorSprite;

            if (font != null)
                so.FindProperty("boxCounterFont").objectReferenceValue = font;
            so.FindProperty("boxCounterFontSize").floatValue = 1.2f;
            so.FindProperty("boxCounterTextColor").colorValue = Color.white;
            so.FindProperty("boxCounterOutlineColor").colorValue = new Color(0.055f, 0.08f, 0.28f, 1f);
            so.FindProperty("boxCounterOutlineWidth").floatValue = 0.22f;
            so.FindProperty("boxCounterSortingOrder").intValue = 201;
            so.FindProperty("elevatorVisualWidthScale").floatValue = 1f;
            so.FindProperty("elevatorDoorWidthRatio").floatValue = .86f;
            so.FindProperty("elevatorDoorHeightRatio").floatValue = .83f;
            so.FindProperty("elevatorDoorCenterYOffset").floatValue = 0f;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        private static void UpdatePieceVisualConfig(TMP_FontAsset font)
        {
            PieceVisualConfig config = AssetDatabase.LoadAssetAtPath<PieceVisualConfig>(PieceVisualConfigPath);
            if (config == null)
                return;

            SerializedObject so = new SerializedObject(config);
            if (font != null && so.FindProperty("iceCounterFont").objectReferenceValue == null)
                so.FindProperty("iceCounterFont").objectReferenceValue = font;

            so.FindProperty("iceCounterFontSize").floatValue = .55f;
            so.FindProperty("iceCounterTextColor").colorValue = Color.white;
            so.FindProperty("iceCounterOutlineColor").colorValue = new Color(0.055f, 0.08f, 0.28f, 1f);
            so.FindProperty("iceCounterOutlineWidth").floatValue = 0.22f;
            so.FindProperty("iceCounterSortingOrder").intValue = 50;

            SerializedProperty bombFont = so.FindProperty("bombCounterFont");
            if (bombFont != null && font != null)
                bombFont.objectReferenceValue = font;
            SerializedProperty bombSize = so.FindProperty("bombCounterFontSize");
            if (bombSize != null)
                bombSize.floatValue = .65f;
            SerializedProperty bombColor = so.FindProperty("bombCounterTextColor");
            if (bombColor != null)
                bombColor.colorValue = Color.white;
            SerializedProperty bombOutline = so.FindProperty("bombCounterOutlineColor");
            if (bombOutline != null)
                bombOutline.colorValue = new Color(0.055f, 0.08f, 0.28f, 1f);
            SerializedProperty bombWidth = so.FindProperty("bombCounterOutlineWidth");
            if (bombWidth != null)
                bombWidth.floatValue = 0.22f;
            SerializedProperty bombOffset = so.FindProperty("bombCounterOffset");
            if (bombOffset != null)
                bombOffset.vector2Value = new Vector2(-0.055f, -0.059f);
            SerializedProperty bombSortingOrder = so.FindProperty("bombCounterSortingOrder");
            if (bombSortingOrder != null)
                bombSortingOrder.intValue = 50;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        private static void AddBombCounterToBlockPiece(TMP_FontAsset font)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockPiecePrefabPath);
            if (prefab == null)
                return;

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                Transform existing = instance.transform.Find("Bomb Counter Presentation");
                if (existing == null)
                {
                    Transform iceCounter = instance.transform.Find("Ice Counter Presentation");
                    GameObject bombCounterObj;
                    if (iceCounter != null)
                    {
                        bombCounterObj = Object.Instantiate(iceCounter.gameObject, instance.transform);
                        bombCounterObj.name = "Bomb Counter Presentation";
                    }
                    else
                    {
                        bombCounterObj = new GameObject("Bomb Counter Presentation");
                        bombCounterObj.transform.SetParent(instance.transform, false);
                        bombCounterObj.AddComponent<MeshRenderer>();
                        bombCounterObj.AddComponent<TextMeshPro>();
                    }

                    TextMeshPro tmp = bombCounterObj.GetComponent<TextMeshPro>();
                    if (font != null)
                        tmp.font = font;
                    tmp.fontSize = .65f;
                    tmp.color = Color.white;
                    tmp.outlineColor = new Color(0.055f, 0.08f, 0.28f, 1f);
                    tmp.outlineWidth = 0.22f;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.rectTransform.sizeDelta = new Vector2(20f, 5f);
                    bombCounterObj.GetComponent<MeshRenderer>().sortingOrder = 50;
                    bombCounterObj.SetActive(true);

                    PrefabUtility.SaveAsPrefabAsset(instance, BlockPiecePrefabPath);
                    Debug.Log("[SceneTunaVisualAuthoring] Added Bomb Counter Presentation to BlockPiece.prefab.");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void ApplyToScene(
            string scenePath,
            JointSpringView springPrefab,
            Sprite springSprite,
            Material iceMaterial)
        {
            if (!File.Exists(scenePath))
                return;

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RuntimePieceFactoryBootstrap bootstrap = Object.FindObjectOfType<RuntimePieceFactoryBootstrap>();
            if (bootstrap == null)
                return;

            JointSpringPresenter presenter = bootstrap.GetComponent<JointSpringPresenter>();
            if (presenter == null)
                presenter = bootstrap.gameObject.AddComponent<JointSpringPresenter>();

            SerializedObject so = new SerializedObject(presenter);
            so.FindProperty("springPrefab").objectReferenceValue = springPrefab;
            so.FindProperty("prewarmCapacity").intValue = 16;
            so.FindProperty("springSprite").objectReferenceValue = springSprite;
            so.FindProperty("springMaterial").objectReferenceValue = iceMaterial;
            so.FindProperty("sortingOrder").intValue = 20;
            so.FindProperty("springScale").floatValue = 0.1495036f;
            so.FindProperty("studOffset").floatValue = 0.25f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(presenter);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SceneTunaVisualAuthoring] Configured JointSpringPresenter in {scenePath}");
        }

        private static Sprite LoadSprite(string spriteName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(MapAtlasPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Sprite s && s.name == spriteName)
                    return s;
            }
            return null;
        }

        private static TMP_FontAsset LoadTMPFont()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TMPFontPath);
            if (font == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (guids[i] == "0e1847d847368439f88be6c09b488ce3")
                        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                }
            }
            return font;
        }
    }
}
#endif
