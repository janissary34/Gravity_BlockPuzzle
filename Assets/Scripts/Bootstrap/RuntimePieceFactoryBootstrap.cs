using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Bootstrap
{
    public sealed class RuntimePieceFactoryBootstrap : MonoBehaviour
    {
        [Header("Piece Pool")]
        [Tooltip("Root prefab with PuzzlePiece, Rigidbody2D, CompositeCollider2D, and LineRenderer.")]
        [SerializeField] private PuzzlePiece blockPiecePrefab;

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
