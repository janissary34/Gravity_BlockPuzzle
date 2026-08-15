using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle;
using UnityEngine;

namespace GravityPuzzle.Bootstrap
{
    public sealed class RuntimePieceFactoryBootstrap : MonoBehaviour
    {
        [Header("Piece Pool")]
        [Tooltip("Root prefab with PuzzlePiece, Rigidbody2D, CompositeCollider2D, and LineRenderer.")]
        [SerializeField] private PuzzlePiece blockPiecePrefab;

        [Tooltip("Prefab with VoxelShard, SpriteRenderer, Rigidbody2D, BoxCollider2D, and GemFlyToUI.")]
        [SerializeField] private VoxelShard voxelShardPrefab;

        [Tooltip("Controls the BlockPiece pool prewarm capacity.")]
        [SerializeField] private PoolConfig poolConfig;

        [Tooltip("Optional parent for inactive pooled pieces. Uses this object when empty.")]
        [SerializeField] private Transform poolParent;

        private void Awake()
        {
            if (blockPiecePrefab == null || poolConfig == null ||
                poolConfig.BlockPieceCapacity <= 0 || !IsPrefabReady(blockPiecePrefab))
            {
                Debug.LogError(
                    "[PiecePool] RuntimePieceFactoryBootstrap needs a valid BlockPiece prefab and PoolConfig with a positive BlockPieceCapacity.",
                    this);
                return;
            }

            Transform parent = poolParent != null ? poolParent : transform;
            GameObjectPool<PuzzlePiece> piecePool = new GameObjectPool<PuzzlePiece>(
                blockPiecePrefab,
                parent,
                poolConfig.BlockPieceCapacity);
            piecePool.Prewarm();
            RuntimePieceFactory.SetRootProvider(new PooledRuntimePieceRootProvider(piecePool));

            if (voxelShardPrefab == null || poolConfig.ShredVoxelCapacity <= 0)
            {
                Debug.LogError(
                    "[VoxelPool] RuntimePieceFactoryBootstrap needs a VoxelShard prefab and a positive ShredVoxelCapacity.",
                    this);
                return;
            }

            GravityLevelDefinition selectedLevel = GravityLevelRuntime.FindLevelToPlay();
            int requiredVoxelCapacity = VoxelBlockBuilder.EstimateMaximumVoxelCount(
                selectedLevel,
                poolConfig.VoxelSubdivisions);
            int voxelCapacity = Mathf.Max(poolConfig.ShredVoxelCapacity, requiredVoxelCapacity);
            if (voxelCapacity > poolConfig.ShredVoxelCapacity)
            {
                Debug.LogWarning(
                    $"[VoxelPool] PoolConfig capacity {poolConfig.ShredVoxelCapacity} was below this level's maximum requirement {requiredVoxelCapacity}. Prewarming {voxelCapacity} voxels.",
                    this);
            }

            GameObjectPool<VoxelShard> voxelPool = new GameObjectPool<VoxelShard>(
                voxelShardPrefab,
                parent,
                voxelCapacity);
            voxelPool.Prewarm();
            VoxelBlockBuilder.SetVoxelPool(voxelPool, poolConfig.VoxelSubdivisions);
        }

        private static bool IsPrefabReady(PuzzlePiece prefab)
        {
            if (prefab.GetComponent<Rigidbody2D>() == null ||
                prefab.GetComponent<CompositeCollider2D>() == null ||
                prefab.GetComponent<LineRenderer>() == null)
            {
                Debug.LogWarning(
                    "[PiecePool] BlockPiece prefab is missing required root components. Falling back to generated runtime pieces.");
                return false;
            }

            return true;
        }
    }
}
