#if UNITY_EDITOR
using GravityPuzzle.Bootstrap;
using GravityPuzzle.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Creates the reusable navy board skin from the artist's Scene_Tuna Map2
    /// atlas and binds it to the active scene composition root.
    /// </summary>
    public static class SceneTunaBoardPresentationAuthoring
    {
        private const string ConfigPath = "Assets/Config/Presentation/BoardPresentation_Navy.asset";
        private const string MapAtlasPath = "Assets/Art/map.png";
        private const string EnvironmentFrameMaterialPath = "Assets/Materials/Folder/M_Environment_Frame.mat";
        private const string EnvironmentBackdropMaterialPath = "Assets/Materials/Folder/M_BlueEnvironment 2.mat";
        private const string GridDarkMaterialPath = "Assets/Materials/Folder/M_GridDark.mat";
        private const string GridLightMaterialPath = "Assets/Materials/Folder/M_GridLight.mat";

        [MenuItem("Gravity Puzzle/Art/Apply Navy Board Presentation To Active Scene")]
        private static void ApplyToActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            RuntimePieceFactoryBootstrap bootstrap = FindBootstrap(scene);
            if (bootstrap == null)
            {
                Debug.LogError("[SceneTunaBoard] Runtime Piece Factory Bootstrap was not found in the active scene.");
                return;
            }

            BoardPresentationConfig config = LoadOrCreateConfig();
            if (config == null)
                return;

            SerializedObject serializedBootstrap = new SerializedObject(bootstrap);
            SerializedProperty configProperty = serializedBootstrap.FindProperty("boardPresentationConfig");
            if (configProperty == null)
            {
                Debug.LogError("[SceneTunaBoard] RuntimePieceFactoryBootstrap has no boardPresentationConfig property.");
                return;
            }

            configProperty.objectReferenceValue = config;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SceneTunaBoard] Navy board presentation was bound to the active scene.");
        }

        private static BoardPresentationConfig LoadOrCreateConfig()
        {
            BoardPresentationConfig config = AssetDatabase.LoadAssetAtPath<BoardPresentationConfig>(ConfigPath);
            if (config == null)
            {
                EnsureFolder("Assets/Config");
                EnsureFolder("Assets/Config/Presentation");
                config = ScriptableObject.CreateInstance<BoardPresentationConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            SerializedObject serializedConfig = new SerializedObject(config);
            SetSprite(serializedConfig, "topEdge", "Map2_9");
            SetSprite(serializedConfig, "bottomEdge", "Map2_8");
            SetSprite(serializedConfig, "leftEdge", "Map2_7");
            SetSprite(serializedConfig, "rightEdge", "Map2_6");
            SetSprite(serializedConfig, "topLeftCorner", "Map2_4");
            SetSprite(serializedConfig, "topRightCorner", "Map2_3");
            serializedConfig.FindProperty("bottomLeftCorner").objectReferenceValue = null;
            serializedConfig.FindProperty("bottomRightCorner").objectReferenceValue = null;
            serializedConfig.FindProperty("renderBottomEdgeSegments").boolValue = false;
            Material environmentFrameMaterial = AssetDatabase.LoadAssetAtPath<Material>(EnvironmentFrameMaterialPath);
            serializedConfig.FindProperty("frameMaterial").objectReferenceValue = environmentFrameMaterial;
            serializedConfig.FindProperty("environmentBackdropMaterial").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>(EnvironmentBackdropMaterialPath);
            // The frame shader is opaque and exposes hidden atlas colours in
            // transparent obstacle corners. Obstacles use the default sprite
            // material so their authored alpha remains intact.
            serializedConfig.FindProperty("boardBlockMaterial").objectReferenceValue =
                AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            serializedConfig.FindProperty("gridDarkMaterial").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>(GridDarkMaterialPath);
            serializedConfig.FindProperty("gridLightMaterial").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>(GridLightMaterialPath);
            SetSprite(serializedConfig, "gridSprite", "BG2");
            serializedConfig.FindProperty("overrideLevelBackground").boolValue = true;
            Color environmentColor = new Color(.22f, .18f, .5f, 1f);
            serializedConfig.FindProperty("backgroundColor").colorValue = environmentColor;
            serializedConfig.FindProperty("alternateBackgroundColor").colorValue = environmentColor;
            serializedConfig.FindProperty("environmentTint").colorValue = new Color(.78f, .78f, .78f, 1f);
            serializedConfig.FindProperty("staticBlockTint").colorValue =
                new Color(.16777663f, .15045391f, .46226418f, 1f);
            serializedConfig.FindProperty("gridDarkTint").colorValue = new Color(.72f, .72f, .78f, 1f);
            serializedConfig.FindProperty("gridLightTint").colorValue = new Color(.82f, .82f, .88f, 1f);
            serializedConfig.FindProperty("extendPresentationToViewportBottom").boolValue = true;
            serializedConfig.FindProperty("viewportBottomPadding").floatValue = .35f;
            serializedConfig.FindProperty("cameraSizeMultiplier").floatValue = 1.2f;
            serializedConfig.FindProperty("frameTint").colorValue = Color.white;
            serializedConfig.FindProperty("cornerOutset").vector2Value = Vector2.zero;
            serializedConfig.FindProperty("topCornerHorizontalInset").floatValue = .25f;
            serializedConfig.FindProperty("topCornerVerticalInset").floatValue = .25f;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static void SetSprite(SerializedObject config, string propertyName, string spriteName)
        {
            SerializedProperty property = config.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[SceneTunaBoard] Missing BoardPresentationConfig property '{propertyName}'.");
                return;
            }

            Sprite sprite = LoadSprite(spriteName);
            if (sprite == null)
            {
                Debug.LogError($"[SceneTunaBoard] Sprite '{spriteName}' was not found in {MapAtlasPath}.");
                return;
            }

            property.objectReferenceValue = sprite;
        }

        private static Sprite LoadSprite(string spriteName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(MapAtlasPath);
            for (int index = 0; index < assets.Length; index++)
            {
                Sprite sprite = assets[index] as Sprite;
                if (sprite != null && sprite.name == spriteName)
                    return sprite;
            }

            return null;
        }

        private static RuntimePieceFactoryBootstrap FindBootstrap(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                RuntimePieceFactoryBootstrap bootstrap =
                    roots[index].GetComponentInChildren<RuntimePieceFactoryBootstrap>(true);
                if (bootstrap != null)
                    return bootstrap;
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int separatorIndex = path.LastIndexOf('/');
            string parent = path.Substring(0, separatorIndex);
            string leaf = path.Substring(separatorIndex + 1);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
