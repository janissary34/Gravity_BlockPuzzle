using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Bootstrap
{
    public sealed class RuntimePieceFactoryBootstrap : MonoBehaviour
    {
        private readonly GeneratedRuntimePieceRootProvider generatedProvider = new GeneratedRuntimePieceRootProvider();

        [Header("Optional Piece Pool")]
        [Tooltip("Root prefab with PuzzlePiece, Rigidbody2D, CompositeCollider2D, and LineRenderer. Leave empty to use generated runtime pieces.")]
        [SerializeField] private PuzzlePiece blockPiecePrefab;

        [Tooltip("Controls prewarm capacity. Leave empty to use generated runtime pieces.")]
        [SerializeField] private PoolConfig poolConfig;

        [Tooltip("Optional parent for inactive pooled pieces. Uses this object when empty.")]
        [SerializeField] private Transform poolParent;

        private void Awake()
        {
            if (blockPiecePrefab != null &&
                poolConfig != null &&
                poolConfig.BlockPieceCapacity > 0 &&
                IsPrefabReady(blockPiecePrefab))
            {
                Transform parent = poolParent != null ? poolParent : transform;
                GameObjectPool<PuzzlePiece> piecePool = new GameObjectPool<PuzzlePiece>(
                    blockPiecePrefab,
                    parent,
                    poolConfig.BlockPieceCapacity);
                piecePool.Prewarm();
                RuntimePieceFactory.SetRootProvider(new PooledRuntimePieceRootProvider(piecePool));
                return;
            }

            RuntimePieceFactory.SetRootProvider(generatedProvider);
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
