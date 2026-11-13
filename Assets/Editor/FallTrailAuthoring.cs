using System.IO;
using GravityPuzzle.Config;
using GravityPuzzle.Presentation.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// One-click setup for the piece motion trail: creates (or reuses) the
    /// config and material assets and adds the presentation object to the
    /// active scene.
    /// </summary>
    internal static class FallTrailAuthoring
    {
        private const string ConfigPath = "Assets/Config/Presentation/FallTrailConfig.asset";
        private const string MaterialPath = "Assets/Materials/M_FallTrail.mat";
        private const string ObjectName = "Fall Trail Presentation";
        private const string TrailShaderName = "GravityPuzzle/Fall Trail";

        [MenuItem("Gravity Puzzle/Presentation/Create Fall Trail In Active Scene")]
        private static void CreateFallTrail()
        {
            FallTrailConfig config = LoadOrCreateConfig();
            Material material = LoadOrCreateMaterial();
            if (config == null || material == null)
                return;

            FallTrailPresentation presentation = Object.FindObjectOfType<FallTrailPresentation>(true);
            bool created = presentation == null;
            if (created)
            {
                GameObject root = new GameObject(ObjectName, typeof(MeshFilter), typeof(MeshRenderer));
                Undo.RegisterCreatedObjectUndo(root, "Create Fall Trail");
                presentation = Undo.AddComponent<FallTrailPresentation>(root);
            }

            GameObject target = presentation.gameObject;
            Undo.RecordObject(target.transform, "Configure Fall Trail");
            target.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            target.transform.localScale = Vector3.one;

            MeshRenderer meshRenderer = target.GetComponent<MeshRenderer>();
            Undo.RecordObject(meshRenderer, "Configure Fall Trail");
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = config.SortingOrder;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            SerializedObject serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("config").objectReferenceValue = config;
            serializedPresentation.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(target.scene);
            Selection.activeGameObject = target;
            EditorGUIUtility.PingObject(target);
            Debug.Log(
                $"[FallTrail] {(created ? "Created" : "Updated")} '{ObjectName}' in scene '{target.scene.name}'. " +
                $"Tune it in {ConfigPath}. Save the scene to keep it.",
                target);
        }

        private static FallTrailConfig LoadOrCreateConfig()
        {
            FallTrailConfig config = AssetDatabase.LoadAssetAtPath<FallTrailConfig>(ConfigPath);
            if (config != null)
                return config;

            EnsureFolder(Path.GetDirectoryName(ConfigPath));
            config = ScriptableObject.CreateInstance<FallTrailConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static Material LoadOrCreateMaterial()
        {
            Shader shader = Shader.Find(TrailShaderName);
            if (shader == null)
            {
                Debug.LogError($"[FallTrail] Shader '{TrailShaderName}' was not found; the trail material was not configured.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
            {
                // Upgrade a material created before the dedicated trail shader existed.
                if (material.shader != shader)
                {
                    material.shader = shader;
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssets();
                }

                return material;
            }

            EnsureFolder(Path.GetDirectoryName(MaterialPath));
            material = new Material(shader) { name = "M_FallTrail" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
