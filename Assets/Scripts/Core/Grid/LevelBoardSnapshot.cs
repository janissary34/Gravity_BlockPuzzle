using System.Collections.Generic;
using GravityPuzzle.Gameplay.Pieces;

namespace GravityPuzzle.Core.Grid
{
    public sealed class LevelBoardSnapshot
    {
        public GravityBoardGrid Grid { get; }
        public IReadOnlyList<PieceModel> Pieces { get; }
        public IReadOnlyList<LevelBoardSnapshotIssue> Issues { get; }

        public LevelBoardSnapshot(
            GravityBoardGrid grid,
            List<PieceModel> pieces,
            List<LevelBoardSnapshotIssue> issues)
        {
            Grid = grid;
            Pieces = pieces;
            Issues = issues;
        }

        public bool TryGetPiece(int pieceId, out PieceModel piece)
        {
            if (pieceId >= 0 && pieceId < Pieces.Count)
            {
                piece = Pieces[pieceId];
                return true;
            }

            piece = null;
            return false;
        }
    }
}
