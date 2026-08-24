using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
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

        [Header("Piece Visuals")]
        [Tooltip("Optional visual lookup for level piece Visual Id values. Empty IDs keep their authored legacy colours.")]
        [SerializeField] private PieceVisualConfig pieceVisualConfig;

        [Tooltip("Optional parent for inactive pooled pieces. Uses this object when empty.")]
        [SerializeField] private Transform poolParent;

        private PoolService poolService;

        private void Awake()
        {
            GravityLevelDefinition selectedLevel = GravityLevelRuntime.FindLevelToPlay();
            string pieceValidationError = "BlockPiece prefab or PoolConfig is not assigned.";
            if (blockPiecePrefab == null || poolConfig == null ||
                poolConfig.BlockPieceCapacity <= 0 ||
                !TryValidateBlockPiecePrefab(blockPiecePrefab, selectedLevel, out pieceValidationError))
            {
                Debug.LogError(
                    $"[PiecePool] RuntimePieceFactoryBootstrap is not ready. {pieceValidationError}",
                    this);
                return;
            }

            Transform parent = poolParent != null ? poolParent : transform;
            poolService = new PoolService();
            int requiredPieceCapacity = CountRuntimePieceRoots(selectedLevel);
            int pieceCapacity = Mathf.Max(poolConfig.BlockPieceCapacity, requiredPieceCapacity);
            if (pieceCapacity > poolConfig.BlockPieceCapacity)
            {
                Debug.LogWarning(
                    $"[PiecePool] PoolConfig capacity {poolConfig.BlockPieceCapacity} was below this level's requirement {requiredPieceCapacity}. Prewarming {pieceCapacity} BlockPiece roots.",
                    this);
            }

            GameObjectPool<PuzzlePiece> piecePool = new GameObjectPool<PuzzlePiece>(
                blockPiecePrefab,
                parent,
                pieceCapacity);
            piecePool.Prewarm();
            poolService.Register<PuzzlePiece>(piecePool);
            RuntimePieceFactory.SetRootProvider(new PooledRuntimePieceRootProvider(poolService));
            RuntimePieceFactory.SetVisualConfig(pieceVisualConfig);

            if (voxelShardPrefab == null || poolConfig.ShredVoxelCapacity <= 0 ||
                !IsVoxelShardPrefabReady(voxelShardPrefab))
            {
                Debug.LogError(
                    "[VoxelPool] RuntimePieceFactoryBootstrap needs a VoxelShard prefab with SpriteRenderer, Rigidbody2D, BoxCollider2D and GemFlyToUI, plus a positive ShredVoxelCapacity.",
                    this);
                return;
            }

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
            poolService.Register<VoxelShard>(voxelPool);
            VoxelBlockBuilder.SetPoolService(poolService, poolConfig.VoxelSubdivisions);

            WarnForUnresolvedVisualIds(selectedLevel);
        }

        private static bool TryValidateBlockPiecePrefab(
            PuzzlePiece prefab,
            GravityLevelDefinition level,
            out string error)
        {
            error = null;
            if (prefab == null)
            {
                error = "BlockPiece prefab is not assigned.";
                return false;
            }

            PieceGridFallView gridFallView = prefab.GetComponent<PieceGridFallView>();
            if (prefab.GetComponent<Rigidbody2D>() == null ||
                prefab.GetComponent<CompositeCollider2D>() == null ||
                prefab.GetComponent<LineRenderer>() == null ||
                gridFallView == null)
            {
                error = "BlockPiece prefab is missing a required root component (Rigidbody2D, CompositeCollider2D, LineRenderer or PieceGridFallView).";
                return false;
            }

            if (gridFallView.Config == null)
            {
                error = "BlockPiece prefab PieceGridFallView is missing its TweenConfig.";
                return false;
            }

            PiecePartSlot[] slots = prefab.GetComponentsInChildren<PiecePartSlot>(true);
            if (slots.Length == 0)
            {
                error = "BlockPiece prefab has no authored PiecePartSlots.";
                return false;
            }

            for (int index = 0; index < slots.Length; index++)
            {
                if (slots[index] == null || slots[index].Visual == null || slots[index].Collision == null)
                {
                    error = $"BlockPiece prefab has an incomplete PiecePartSlot at index {index}.";
                    return false;
                }
            }

            int requiredSlots = GetMaximumRequiredPartSlots(level);
            if (requiredSlots > slots.Length)
            {
                error = $"BlockPiece prefab has {slots.Length} PiecePartSlots but this level needs up to {requiredSlots}.";
                return false;
            }

            return true;
        }

        private static bool IsVoxelShardPrefabReady(VoxelShard prefab)
        {
            if (prefab == null ||
                prefab.GetComponent<SpriteRenderer>() == null ||
                prefab.GetComponent<Rigidbody2D>() == null ||
                prefab.GetComponent<BoxCollider2D>() == null)
                return false;

            GemFlyToUI gemFly = prefab.GetComponent<GemFlyToUI>();
            return gemFly != null && gemFly.Config != null;
        }

        private static int GetMaximumRequiredPartSlots(GravityLevelDefinition level)
        {
            if (level == null || level.pieces == null)
                return 0;

            int maximum = 0;
            for (int index = 0; index < level.pieces.Count; index++)
            {
                PieceDefinition piece = level.pieces[index];
                if (piece != null && piece.cells != null)
                    maximum = Mathf.Max(maximum, piece.cells.Count);
            }

            return maximum;
        }

        // A definition without a Block cell is ignored by RuntimePieceFactory and must not
        // consume a prewarmed root. Hook-only/empty authoring mistakes therefore cannot make
        // the configured pool appear undersized.
        private static int CountRuntimePieceRoots(GravityLevelDefinition level)
        {
            if (level == null || level.pieces == null)
                return 0;

            int count = 0;
            for (int pieceIndex = 0; pieceIndex < level.pieces.Count; pieceIndex++)
            {
                PieceDefinition piece = level.pieces[pieceIndex];
                if (piece == null || piece.cells == null)
                    continue;

                for (int cellIndex = 0; cellIndex < piece.cells.Count; cellIndex++)
                {
                    if (piece.cells[cellIndex].type != PieceCellType.Block)
                        continue;

                    count++;
                    break;
                }
            }

            return count;
        }

        private void WarnForUnresolvedVisualIds(GravityLevelDefinition level)
        {
            if (level == null || level.pieces == null || pieceVisualConfig == null)
                return;

            for (int index = 0; index < level.pieces.Count; index++)
            {
                PieceDefinition piece = level.pieces[index];
                if (piece == null || string.IsNullOrWhiteSpace(piece.visualId) ||
                    pieceVisualConfig.TryGet(piece.visualId, out _))
                    continue;

                Debug.LogWarning(
                    $"[PieceVisual] Level piece '{piece.name}' references missing Visual Id '{piece.visualId}'. Using its authored colour.",
                    this);
            }
        }
    }
}
