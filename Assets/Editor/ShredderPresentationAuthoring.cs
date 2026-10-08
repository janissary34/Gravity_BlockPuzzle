#if UNITY_EDITOR
using GravityPuzzle.Presentation.Views;
using GravityPuzzle.Presentation.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityPuzzle.EditorTools
{
    /// <summary>
    /// Creates the shared dissolve material and wires the presentation-only
    /// components into every authored gameplay scene. This is editor-only;
    /// gameplay never creates renderers, materials, or particle systems.
    /// </summary>
    public static class ShredderPresentationAuthoring
    {
        private const string MaterialPath = "Assets/Materials/ShredderDissolveSprite.mat";
        private static readonly string[] GameplayScenePaths =
        {
            "Assets/Scenes/Scene1.unity",
            "Assets/Scenes/Scene_Tuna.unity",
            "Assets/Scenes/Scene1_GoldRecovery.unity"
        };

        [MenuItem("Gravity Puzzle/Refactor/Create And Configure Shredder Presentation")]
        public static void CreateAndConfigure()
        {
            Material material = GetOrCreateDissolveMaterial();
            if (material == null)
                return;

            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                for (int index = 0; index < GameplayScenePaths.Length; index++)
                    ConfigureScene(GameplayScenePaths[index], material);
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = material;
            Debug.Log("[ShredderPresentation] Configured rough cutter dissolve and shared debris particles in gameplay scenes.");
        }

        private static Material GetOrCreateDissolveMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            Shader shader = Shader.Find("GravityPuzzle/Shredder Dissolve Sprite");
            if (shader == null)
            {
                Debug.LogError("[ShredderPresentation] Dissolve shader has not imported yet. Recompile and run the authoring command again.");
                return null;
            }

            material = new Material(shader) { name = "ShredderDissolveSprite" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static void ConfigureScene(string scenePath, Material dissolveMaterial)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            BlockShredder[] shredders = Object.FindObjectsOfType<BlockShredder>(true);
            for (int index = 0; index < shredders.Length; index++)
            {
                BlockShredder shredder = shredders[index];
                ShredderDebrisParticleSystem debris = shredder.GetComponent<ShredderDebrisParticleSystem>();
                if (debris == null)
                {
                    ParticleSystem particles = shredder.gameObject.AddComponent<ParticleSystem>();
                    ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
                    renderer.sortingOrder = 24;
                    debris = shredder.gameObject.AddComponent<ShredderDebrisParticleSystem>();
                }

                SerializedObject serializedShredder = new SerializedObject(shredder);
                serializedShredder.FindProperty("debrisParticleSystem").objectReferenceValue = debris;
                serializedShredder.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(shredder);
            }

            PuzzlePiece[] piecePrefabsInScene = Object.FindObjectsOfType<PuzzlePiece>(true);
            for (int index = 0; index < piecePrefabsInScene.Length; index++)
                ConfigureDissolve(piecePrefabsInScene[index], dissolveMaterial);

            PuzzlePiece prefab = AssetDatabase.LoadAssetAtPath<PuzzlePiece>("Assets/Prefabs/BlockPiece.prefab");
            if (prefab != null)
            {
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents("Assets/Prefabs/BlockPiece.prefab");
                PuzzlePiece prefabPiece = prefabRoot.GetComponent<PuzzlePiece>();
                ConfigureDissolve(prefabPiece, dissolveMaterial);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, "Assets/Prefabs/BlockPiece.prefab");
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureDissolve(PuzzlePiece piece, Material dissolveMaterial)
        {
            if (piece == null)
                return;

            ShredderDissolvePresentation dissolve = piece.GetComponent<ShredderDissolvePresentation>();
            if (dissolve == null)
                dissolve = piece.gameObject.AddComponent<ShredderDissolvePresentation>();

            SerializedObject serializedDissolve = new SerializedObject(dissolve);
            serializedDissolve.FindProperty("dissolveMaterial").objectReferenceValue = dissolveMaterial;
            serializedDissolve.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(dissolve);
        }
    }
}
#endif
