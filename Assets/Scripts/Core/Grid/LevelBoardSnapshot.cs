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

        public int NextPieceId
        {
            get
            {
                int nextId = 0;
                for (int index = 0; index < pieces.Count; index++)
                    nextId = System.Math.Max(nextId, pieces[index].Id + 1);

                return nextId;
            }
        }

        /// <summary>
        /// Registers a model whose cells have already been committed to Grid.
        /// Runtime topology changes use this after atomically replacing a
        /// source piece with its hammer-created fragments.
        /// </summary>
        public bool TryRegisterPlacedPiece(PieceModel piece)
        {
            if (piece == null || piece.Id != NextPieceId || !piece.IsOnBoard)
                return false;

            pieces.Add(piece);
            return true;
        }

        public bool TryReplacePlacedPiece(PieceModel piece)
        {
            if (piece == null || !piece.IsOnBoard)
                return false;

            for (int index = 0; index < pieces.Count; index++)
            {
                if (pieces[index].Id != piece.Id)
                    continue;

                pieces[index] = piece;
                return true;
            }

            return false;
        }

        public bool TryGetPiece(int pieceId, out PieceModel piece)
        {
            for (int index = 0; index < Pieces.Count; index++)
            {
                PieceModel candidate = Pieces[index];
                if (candidate.Id != pieceId)
                    continue;

                piece = candidate;
                return true;
            }

            piece = null;
            return false;
        }
    }
}
