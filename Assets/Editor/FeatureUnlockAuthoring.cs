using System.Collections.Generic;
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
    /// Keeps milestone assets and their pre-authored New Feature panel content
    /// in sync without creating any runtime UI objects.
    /// </summary>
    internal static class FeatureUnlockAuthoring
    {
        private const string BoosterConfigDirectory = "Assets/Config/Boosters";
        private const string PieceVisualConfigPath = "Assets/PieceVisualConfig.asset";
        private const string RevealPresentationConfigPath = "Assets/Config/RevealPresentationConfig.asset";
        private const string BombSpritePath = "Assets/Art/Parlak Altın Bantlı Bomba.png";
        // The source filename on disk uses macOS's decomposed umlaut form.
        // Keep that exact asset path so AssetDatabase can import the door slices.
        private const string ElevatorSpritePath = "Assets/Art/asanso\u0308r.png";
        private const string BoxSpritePath = "Assets/Art/Parlak Mavi Garaj Kapısı Giriş.png";
        private const string BoxCoverSpriteName = "BrightBlueGarageFrame";

        [InitializeOnLoadMethod]
        private static void RestoreRevealArtSlicesAfterAssemblyReload()
        {
            EditorApplication.delayCall += RestoreRevealArtSlices;
        }

        private static void RestoreRevealArtSlices()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RestoreRevealArtSlices;
                return;
            }

            RevealPresentationConfig revealConfig =
                AssetDatabase.LoadAssetAtPath<RevealPresentationConfig>(RevealPresentationConfigPath);
            if (revealConfig == null ||
                (revealConfig.boxCoverSprite != null &&
                 revealConfig.boxCoverSprite.name == BoxCoverSpriteName &&
                 revealConfig.elevatorFrameSprite != null &&
                 revealConfig.elevatorLeftDoorSprite != null &&
                 revealConfig.elevatorRightDoorSprite != null))
            {
                return;
            }

            ConfigureRevealArtSprites(out Sprite boxCover, out Sprite elevatorFrame, out Sprite leftDoor, out Sprite rightDoor);
            if (boxCover == null || elevatorFrame == null || leftDoor == null || rightDoor == null)
                return;

            SerializedObject serializedRevealConfig = new SerializedObject(revealConfig);
            serializedRevealConfig.FindProperty("boxCoverSprite").objectReferenceValue = boxCover;
            serializedRevealConfig.FindProperty("elevatorFrameSprite").objectReferenceValue = elevatorFrame;
            serializedRevealConfig.FindProperty("elevatorLeftDoorSprite").objectReferenceValue = leftDoor;
            serializedRevealConfig.FindProperty("elevatorRightDoorSprite").objectReferenceValue = rightDoor;
            serializedRevealConfig.FindProperty("elevatorDoorSprite").objectReferenceValue = elevatorFrame;
            serializedRevealConfig.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Gravity Puzzle/Boosters/Configure Feature Unlocks")]
        private static void ConfigureFeatureUnlocks()
        {
            BoosterRewardConfig iceBlock = EnsureRewardConfig("IceBlockRewardConfig", BoosterRewardType.IceBlock, 8);
            BoosterRewardConfig bombBlock = EnsureRewardConfig("BombBlockRewardConfig", BoosterRewardType.BombBlock, 15);
            BoosterRewardConfig elevator = EnsureRewardConfig("ElevatorRewardConfig", BoosterRewardType.Elevator, 25);
            BoosterRewardConfig box = EnsureRewardConfig("BoxRewardConfig", BoosterRewardType.Box, 40);

            ConfigureGameplayArt();
            ConfigurePanel(iceBlock, bombBlock, elevator, box);
            ConfigureBoardRewardList(iceBlock, bombBlock, elevator, box);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Feature Unlocks] Ice Block, Bomb Block, Elevator and Box milestones are configured.");
        }

        private static BoosterRewardConfig EnsureRewardConfig(
            string assetName,
            BoosterRewardType rewardType,
            int presentationLevel)
        {
            string path = $"{BoosterConfigDirectory}/{assetName}.asset";
            BoosterRewardConfig config = AssetDatabase.LoadAssetAtPath<BoosterRewardConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<BoosterRewardConfig>();
                AssetDatabase.CreateAsset(config, path);
            }

            SerializedObject serializedConfig = new SerializedObject(config);
            serializedConfig.FindProperty("boosterType").enumValueIndex = (int)rewardType;
            serializedConfig.FindProperty("presentationLevel").intValue = presentationLevel;
            serializedConfig.FindProperty("usesPerLevel").intValue = 3;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static void ConfigureGameplayArt()
        {
            PieceVisualConfig pieceVisualConfig = AssetDatabase.LoadAssetAtPath<PieceVisualConfig>(PieceVisualConfigPath);
            if (pieceVisualConfig != null)
            {
                SerializedObject serializedPieceVisuals = new SerializedObject(pieceVisualConfig);
                serializedPieceVisuals.FindProperty("bombOverlaySprite").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>(BombSpritePath);
                serializedPieceVisuals.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[Feature Unlocks] PieceVisualConfig is missing; Bomb Block will retain its fallback appearance.");
            }

            RevealPresentationConfig revealConfig = AssetDatabase.LoadAssetAtPath<RevealPresentationConfig>(RevealPresentationConfigPath);
            if (revealConfig == null)
            {
                revealConfig = ScriptableObject.CreateInstance<RevealPresentationConfig>();
                AssetDatabase.CreateAsset(revealConfig, RevealPresentationConfigPath);
            }

            SerializedObject serializedRevealConfig = new SerializedObject(revealConfig);
            ConfigureRevealArtSprites(out Sprite boxCover, out Sprite elevatorFrame, out Sprite leftDoor, out Sprite rightDoor);
            serializedRevealConfig.FindProperty("boxCoverSprite").objectReferenceValue = boxCover;
            serializedRevealConfig.FindProperty("elevatorFrameSprite").objectReferenceValue = elevatorFrame;
            serializedRevealConfig.FindProperty("elevatorLeftDoorSprite").objectReferenceValue = leftDoor;
            serializedRevealConfig.FindProperty("elevatorRightDoorSprite").objectReferenceValue = rightDoor;
            serializedRevealConfig.FindProperty("elevatorDoorSprite").objectReferenceValue = elevatorFrame;
            serializedRevealConfig.ApplyModifiedPropertiesWithoutUndo();

            RuntimePieceFactoryBootstrap bootstrap = Object.FindFirstObjectByType<RuntimePieceFactoryBootstrap>(
                FindObjectsInactive.Include);
            if (bootstrap == null)
            {
                Debug.LogWarning("[Feature Unlocks] Runtime Piece Factory Bootstrap is missing; generated Box/Elevator art is not wired.");
                return;
            }

            SerializedObject serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("revealPresentationConfig").objectReferenceValue = revealConfig;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The supplied art is sliced once in the editor: a trimmed 9-slice cover
        /// for Garage, a static Elevator frame, and only the two glass door panels.
        /// Runtime code receives stable Sprite references and never crops art.
        /// </summary>
        private static void ConfigureRevealArtSprites(
            out Sprite boxCover,
            out Sprite elevatorFrame,
            out Sprite leftDoor,
            out Sprite rightDoor)
        {
            boxCover = ConfigureBoxCoverSprite();
            ConfigureElevatorDoorSprites(out elevatorFrame, out leftDoor, out rightDoor);
        }

        private static Sprite ConfigureBoxCoverSprite()
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BoxSpritePath);
            TextureImporter importer = AssetImporter.GetAtPath(BoxSpritePath) as TextureImporter;
            if (texture == null || importer == null)
            {
                Debug.LogWarning("[Feature Unlocks] Garage art could not be imported for sliced rendering.");
                return null;
            }

            importer.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable CS0618
            importer.spritesheet = new[]
            {
                CreateSpriteMetaData(
                    BoxCoverSpriteName,
                    new Rect(94f, 170f, texture.width - 188f, texture.height - 340f),
                    new Vector4(118f, 120f, 118f, 120f))
            };
#pragma warning restore CS0618
            importer.SaveAndReimport();
            return FindSprite(BoxSpritePath, BoxCoverSpriteName);
        }

        private static void ConfigureElevatorDoorSprites(
            out Sprite elevatorFrame,
            out Sprite leftDoor,
            out Sprite rightDoor)
        {
            elevatorFrame = null;
            leftDoor = null;
            rightDoor = null;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ElevatorSpritePath);
            TextureImporter importer = AssetImporter.GetAtPath(ElevatorSpritePath) as TextureImporter;
            if (texture == null || importer == null)
            {
                Debug.LogWarning("[Feature Unlocks] Elevator art could not be imported for door animation.");
                return;
            }

            importer.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable CS0618
            importer.spritesheet = new[]
            {
                CreateSpriteMetaData("ElevatorFrame", new Rect(145f, 65f, texture.width - 290f, texture.height - 130f), new Vector4(145f, 125f, 145f, 190f)),
                CreateSpriteMetaData("ElevatorDoorLeft", new Rect(368f, 205f, 258f, 745f), new Vector4(20f, 20f, 20f, 20f)),
                CreateSpriteMetaData("ElevatorDoorRight", new Rect(628f, 205f, 258f, 745f), new Vector4(20f, 20f, 20f, 20f))
            };
#pragma warning restore CS0618
            importer.SaveAndReimport();
            elevatorFrame = FindSprite(ElevatorSpritePath, "ElevatorFrame");
            leftDoor = FindSprite(ElevatorSpritePath, "ElevatorDoorLeft");
            rightDoor = FindSprite(ElevatorSpritePath, "ElevatorDoorRight");
        }

        private static SpriteMetaData CreateSpriteMetaData(string name, Rect rect, Vector4 border)
        {
            return new SpriteMetaData
            {
                name = name,
                rect = rect,
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(.5f, .5f),
                border = border
            };
        }

        private static Sprite FindSprite(string assetPath, string spriteName)
        {
            Object[] importedAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int index = 0; index < importedAssets.Length; index++)
            {
                Sprite sprite = importedAssets[index] as Sprite;
                if (sprite != null && sprite.name == spriteName)
                    return sprite;
            }

            return null;
        }

        private static void ConfigurePanel(
            BoosterRewardConfig iceBlock,
            BoosterRewardConfig bombBlock,
            BoosterRewardConfig elevator,
            BoosterRewardConfig box)
        {
            Transform featureContent = FindTransform("FeeatureContent");
            if (featureContent == null)
            {
                Debug.LogWarning("[Feature Unlocks] FeeatureContent was not found in the active scene.");
                return;
            }

            NewBoosterPanelView panel = featureContent.GetComponentInParent<NewBoosterPanelView>(true);
            if (panel == null)
            {
                panel = Object.FindFirstObjectByType<NewBoosterPanelView>(FindObjectsInactive.Include);
            }

            if (panel == null)
            {
                Transform overlayRoot = featureContent.parent != null && featureContent.parent.parent != null
                    ? featureContent.parent.parent
                    : featureContent;
                panel = Undo.AddComponent<NewBoosterPanelView>(overlayRoot.gameObject);
            }

            BoosterRewardConfig freezeTimer = LoadExistingConfig("FreezeTimerBoosterRewardConfig.asset");
            BoosterRewardConfig rocket = LoadExistingConfig("RocketBoosterRewardConfig.asset");
            BoosterRewardConfig hammer = LoadExistingConfig("HammerBoosterRewardConfig.asset");
            var contents = new List<BoosterRewardContentView>(7);
            AddContent(featureContent, "Freeze_Timer", freezeTimer, contents);
            AddContent(featureContent, "IceBlock", iceBlock, contents);
            AddContent(featureContent, "BombBlock", bombBlock, contents);
            AddContent(featureContent, "Elevator", elevator, contents);
            AddContent(featureContent, "Box", box, contents);
            AddContent(featureContent, "Rocket", rocket, contents);
            AddContent(featureContent, "Hammer", hammer, contents);

            SerializedObject serializedPanel = new SerializedObject(panel);
            serializedPanel.FindProperty("previewRewardConfig").objectReferenceValue = freezeTimer;
            WriteObjectArray(serializedPanel.FindProperty("rewardContents"), contents);

            if (serializedPanel.FindProperty("closeButton").objectReferenceValue == null)
                serializedPanel.FindProperty("closeButton").objectReferenceValue = FindComponent<Button>(panel.transform, "CloseButton");
            if (serializedPanel.FindProperty("continueButton").objectReferenceValue == null)
                serializedPanel.FindProperty("continueButton").objectReferenceValue = FindComponent<Button>(panel.transform, "ContinueButton");
            if (serializedPanel.FindProperty("overlayRoot").objectReferenceValue == null)
                serializedPanel.FindProperty("overlayRoot").objectReferenceValue = featureContent.parent != null
                    ? featureContent.parent.parent != null ? featureContent.parent.parent.gameObject : featureContent.parent.gameObject
                    : featureContent.gameObject;

            serializedPanel.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureBoardRewardList(params BoosterRewardConfig[] featureConfigs)
        {
            PrototypeBoard board = Object.FindFirstObjectByType<PrototypeBoard>(FindObjectsInactive.Include);
            if (board == null)
            {
                Debug.LogWarning("[Feature Unlocks] PrototypeBoard is missing; milestone configs are not wired to level completion.");
                return;
            }

            SerializedObject serializedBoard = new SerializedObject(board);
            SerializedProperty rewards = serializedBoard.FindProperty("boosterRewardConfigs");
            var combined = new List<BoosterRewardConfig>(rewards.arraySize + featureConfigs.Length);
            for (int index = 0; index < rewards.arraySize; index++)
            {
                BoosterRewardConfig existing = rewards.GetArrayElementAtIndex(index).objectReferenceValue as BoosterRewardConfig;
                if (existing != null && !combined.Contains(existing))
                    combined.Add(existing);
            }

            for (int index = 0; index < featureConfigs.Length; index++)
            {
                BoosterRewardConfig feature = featureConfigs[index];
                if (feature != null && !combined.Contains(feature))
                    combined.Add(feature);
            }

            WriteObjectArray(rewards, combined);
            serializedBoard.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddContent(
            Transform parent,
            string objectName,
            BoosterRewardConfig config,
            List<BoosterRewardContentView> contents)
        {
            Transform contentTransform = parent.Find(objectName);
            if (contentTransform == null || config == null)
                return;

            BoosterRewardContentView content = contentTransform.GetComponent<BoosterRewardContentView>();
            if (content == null)
                content = Undo.AddComponent<BoosterRewardContentView>(contentTransform.gameObject);

            SerializedObject serializedContent = new SerializedObject(content);
            serializedContent.FindProperty("rewardConfig").objectReferenceValue = config;
            serializedContent.ApplyModifiedPropertiesWithoutUndo();
            contents.Add(content);
        }

        private static BoosterRewardConfig LoadExistingConfig(string filename)
        {
            return AssetDatabase.LoadAssetAtPath<BoosterRewardConfig>($"{BoosterConfigDirectory}/{filename}");
        }

        private static Transform FindTransform(string name)
        {
            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform candidate = transforms[index];
                if (candidate.gameObject.scene == SceneManager.GetActiveScene() && candidate.name == name)
                    return candidate;
            }

            return null;
        }

        private static T FindComponent<T>(Transform parent, string objectName) where T : Component
        {
            T[] components = parent.GetComponentsInChildren<T>(true);
            for (int index = 0; index < components.Length; index++)
            {
                if (components[index].name == objectName)
                    return components[index];
            }

            return null;
        }

        private static void WriteObjectArray<T>(SerializedProperty property, IList<T> values) where T : Object
        {
            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }
    }
}
