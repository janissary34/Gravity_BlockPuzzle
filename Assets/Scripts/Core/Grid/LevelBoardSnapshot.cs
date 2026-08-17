using System.Collections.Generic;
using GravityPuzzle.Gameplay.Pieces;

namespace GravityPuzzle.Core.Grid
{
    public sealed class LevelBoardSnapshot
    {
        private readonly List<PieceModel> pieces;

        public GravityBoardGrid Grid { get; }
        public IReadOnlyList<PieceModel> Pieces => pieces;
        public IReadOnlyList<LevelBoardSnapshotIssue> Issues { get; }

        public LevelBoardSnapshot(
            GravityBoardGrid grid,
            List<PieceModel> pieces,
            List<LevelBoardSnapshotIssue> issues)
        {
            Grid = grid;
            this.pieces = pieces;
            Issues = issues;
        }

        public int NextPieceId => pieces.Count;

        /// <summary>
        /// Registers a model whose cells have already been committed to Grid.
        /// Runtime topology changes use this after atomically replacing a
        /// source piece with its hammer-created fragments.
        /// </summary>
        public bool TryRegisterPlacedPiece(PieceModel piece)
        {
            if (piece == null || piece.Id != pieces.Count || !piece.IsOnBoard)
                return false;

            pieces.Add(piece);
            return true;
        }

        public bool TryReplacePlacedPiece(PieceModel piece)
        {
            if (piece == null || piece.Id < 0 || piece.Id >= pieces.Count || !piece.IsOnBoard)
                return false;

            pieces[piece.Id] = piece;
            return true;
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
