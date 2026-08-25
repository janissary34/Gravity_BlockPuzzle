#if UNITY_EDITOR
using GravityPuzzle.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    public static class VoxelShardPoolAuthoring
    {
        private const string PrefabPath = "Assets/Prefabs/VoxelShard.prefab";
        private const string PoolConfigPath = "Assets/PoolConfig.asset";

        [MenuItem("Gravity Puzzle/Refactor/Create And Configure Voxel Shard Pool")]
        private static void CreateAndConfigure()
        {
            VoxelShard prefab = AssetDatabase.LoadAssetAtPath<VoxelShard>(PrefabPath);
            if (prefab == null)
            {
                GameObject root = new GameObject("VoxelShard");
                root.AddComponent<SpriteRenderer>();

                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.simulated = false;
                body.gravityScale = 0f;

                BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
                collider.enabled = false;
                prefab = root.AddComponent<VoxelShard>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Object.DestroyImmediate(root);
                prefab = AssetDatabase.LoadAssetAtPath<VoxelShard>(PrefabPath);
            }

            RuntimePieceFactoryBootstrap bootstrap = Object.FindObjectOfType<RuntimePieceFactoryBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogWarning("[VoxelPool] Could not find Runtime Piece Factory Bootstrap in the open scene.");
                return;
            }

            SerializedObject serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("voxelShardPrefab").objectReferenceValue = prefab;
            serializedBootstrap.FindProperty("poolConfig").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Config.PoolConfig>(PoolConfigPath);
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
        }
    }
}
#endif
