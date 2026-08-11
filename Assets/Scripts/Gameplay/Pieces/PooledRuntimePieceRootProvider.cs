using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class PooledRuntimePieceRootProvider : IRuntimePieceRootProvider
    {
        private readonly IPool<PuzzlePiece> pool;
        private readonly GeneratedRuntimePieceRootProvider fallbackProvider = new GeneratedRuntimePieceRootProvider();
        private bool warnedAboutExhaustion;

        public PooledRuntimePieceRootProvider(IPool<PuzzlePiece> piecePool)
        {
            pool = piecePool;
        }

        public RuntimePieceRoot Create(string pieceName)
        {
            if (pool == null || !pool.TryRent(out PuzzlePiece piece))
            {
                if (!warnedAboutExhaustion)
                {
                    warnedAboutExhaustion = true;
                    Debug.LogWarning(
                        "[PiecePool] Pool exhausted. Falling back to generated runtime piece. Increase PoolConfig BlockPieceCapacity.");
                }

                return fallbackProvider.Create(pieceName);
            }

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
