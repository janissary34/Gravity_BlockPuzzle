using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class PooledRuntimePieceRootProvider : IRuntimePieceRootProvider
    {
        private readonly PoolService poolService;

        public PooledRuntimePieceRootProvider(PoolService pools)
        {
            poolService = pools;
        }

        public RuntimePieceRoot Create(string pieceName)
        {
            if (poolService == null ||
                !poolService.TryGet(out IPool<PuzzlePiece> pool) ||
                !pool.TryRent(out PuzzlePiece piece))
            {
                throw new System.InvalidOperationException(
                    "[PiecePool] Pool exhausted. Increase PoolConfig BlockPieceCapacity before starting the level.");
            }

            piece.gameObject.name = pieceName;
            return new RuntimePieceRoot(
                piece.gameObject,
                piece.Body,
                piece.CompositeCollider,
                piece.Outline,
                piece);
        }
    }
}
