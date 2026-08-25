#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GravityPuzzle.Presentation.VFX;

namespace GravityPuzzle.Editor
{
    public static class ProgressVoxelParticleAuthoring
    {
        private const string PrefabPath = "Assets/Prefabs/ProgressVoxelParticleSystem.prefab";

        [MenuItem("Gravity Puzzle/Refactor/Create Progress Voxel Particle Prefab")]
        public static void CreateOrUpdatePrefab()
        {
            GameObject go = new GameObject("ProgressVoxelParticleSystem");
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psRenderer = go.GetComponent<ParticleSystemRenderer>();
            ProgressVoxelParticleSystem progressVfx = go.AddComponent<ProgressVoxelParticleSystem>();

            // Configure Main Module
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = 5000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = 0f;
            main.startLifetime = 0.55f;
            main.startSize = 0.28f;

            // Emission module (off by default, driven by Emit)
            var emission = ps.emission;
            emission.enabled = false;

            // Shape module
            var shape = ps.shape;
            shape.enabled = false;

            // Renderer setup
            Material particleMat = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            if (particleMat == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader != null) particleMat = new Material(shader);
            }

            if (particleMat != null)
                psRenderer.material = particleMat;

            psRenderer.sortingLayerName = "Default";
            psRenderer.sortingOrder = 3300;
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            // Wire serialized fields on component
            SerializedObject serializedVfx = new SerializedObject(progressVfx);
            serializedVfx.FindProperty("particleSys").objectReferenceValue = ps;
            serializedVfx.FindProperty("particleRenderer").objectReferenceValue = psRenderer;
            serializedVfx.ApplyModifiedPropertiesWithoutUndo();

            // Save Prefab
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);

            Debug.Log($"[ProgressVFX] ProgressVoxelParticleSystem prefab created at {PrefabPath}");
        }
    }
}
#endif
