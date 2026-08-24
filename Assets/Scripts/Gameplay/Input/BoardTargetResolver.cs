using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Input
{
    /// <summary>
    /// Resolves a board tap through the authoritative snapshot rather than
    /// presentation colliders.  Targeted boosters share this path so a pooled
    /// or fragmented piece cannot become visually selectable but logically
    /// untargetable (or the reverse).
    /// </summary>
    public static class BoardTargetResolver
    {
        public readonly struct Target
        {
            public PuzzlePiece Piece { get; }
            public GridCoordinate Cell { get; }
            public Vector2 WorldPosition { get; }

            public Target(PuzzlePiece piece, GridCoordinate cell, Vector2 worldPosition)
            {
                Piece = piece;
                Cell = cell;
                WorldPosition = worldPosition;
            }
        }

        public static bool TryResolve(
            PrototypeBoard board,
            Vector2 worldPosition,
            out Target target)
        {
            target = default;
            if (board == null || !board.IsLevelRunning || board.BoardSnapshot == null)
                return false;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
                return false;

            GridCoordinate cell = GravityLevelGridCoordinates.WorldToFineCell(level, worldPosition);
            GravityBoardGrid grid = board.BoardSnapshot.Grid;
            var pieces = PuzzlePiece.ActivePieces;

            GridCellState cellState = grid.GetCellState(cell);
            if (cellState == GridCellState.Occupied || cellState == GridCellState.Reserved)
            {
                int occupantId = grid.GetOccupantId(cell);
                for (int index = 0; index < pieces.Count; index++)
                {
                    PuzzlePiece piece = pieces[index];
                    if (piece == null || piece.IsBeingShredded || piece.SourcePieceId != occupantId)
                        continue;

                    target = new Target(
                        piece,
                        cell,
                        GravityLevelGridCoordinates.FineCellToWorld(level, cell));
                    return true;
                }
            }

            // A drag/fall presentation can clear a model's matrix cells for a
            // frame while the model itself remains authoritative. Resolve the
            // owning model, then restore its occupancy atomically before a
            // booster mutates it. This is intentionally model based: no
            // collider or raycast decides which board cell was selected.
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece == null || piece.IsBeingShredded ||
                    !board.TryGetPieceModel(piece, out PieceModel model) ||
                    !ModelOwnsCell(model, cell))
                    continue;

                if (!board.TryRestorePieceGridOccupancy(piece))
                    return false;

                target = new Target(
                    piece,
                    cell,
                    GravityLevelGridCoordinates.FineCellToWorld(level, cell));
                return true;
            }

            return false;
        }

        private static bool ModelOwnsCell(PieceModel model, GridCoordinate cell)
        {
            for (int index = 0; index < model.LocalCells.Count; index++)
            {
                if (model.GetWorldCell(index).Equals(cell))
                    return true;
            }

            return false;
        }
    }
}
