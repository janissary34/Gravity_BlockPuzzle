using System;
using GravityPuzzle.Infrastructure.Pooling;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class PooledRuntimePieceRootProvider : IRuntimePieceRootProvider
    {
        private readonly IPool<PuzzlePiece> pool;

        public PooledRuntimePieceRootProvider(IPool<PuzzlePiece> piecePool)
        {
            pool = piecePool;
        }

        public RuntimePieceRoot Create(string pieceName)
        {
            if (pool == null || !pool.TryRent(out PuzzlePiece piece))
                throw new InvalidOperationException("No pooled PuzzlePiece is available.");

            piece.gameObject.name = pieceName;
            piece.ConfigurePoolReturn(pool.Return);
            return new RuntimePieceRoot(
                piece.gameObject,
                piece.Body,
                piece.CompositeCollider,
                piece.Outline,
                piece);
        }
    }
}
