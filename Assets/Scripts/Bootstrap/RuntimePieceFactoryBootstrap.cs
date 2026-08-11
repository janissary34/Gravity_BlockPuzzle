using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Bootstrap
{
    public sealed class RuntimePieceFactoryBootstrap : MonoBehaviour
    {
        private readonly GeneratedRuntimePieceRootProvider generatedProvider = new GeneratedRuntimePieceRootProvider();
        [SerializeField] private PuzzlePiece blockPiecePrefab;
        [SerializeField] private PoolConfig poolConfig;
        [SerializeField] private Transform poolParent;

        private void Awake()
        {
            if (blockPiecePrefab != null &&
                poolConfig != null &&
                poolConfig.BlockPieceCapacity > 0)
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
    }
}
