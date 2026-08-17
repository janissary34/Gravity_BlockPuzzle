using System.Collections.Generic;
using GravityPuzzle.Core.Grid;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class PieceModel
    {
        private readonly List<GridCoordinate> localCells;

        public int Id { get; }
        public GridCoordinate Anchor { get; private set; }
        public GridCoordinate PivotOffset { get; private set; }
        public bool IsOnBoard { get; private set; } = true;
        public PieceState State { get; private set; } = PieceState.Placed;
        public IReadOnlyList<GridCoordinate> LocalCells => localCells;

        public PieceModel(
            int id,
            GridCoordinate anchor,
            GridCoordinate pivotOffset,
            List<GridCoordinate> localCellCoordinates)
        {
            Id = id;
            Anchor = anchor;
            PivotOffset = pivotOffset;
            localCells = localCellCoordinates;
        }

        public GridCoordinate GetWorldCell(int localCellIndex)
        {
            return Anchor.Offset(localCells[localCellIndex]);
        }

        public void SetAnchor(GridCoordinate anchor)
        {
            Anchor = anchor;
            IsOnBoard = true;
        }

        /// <summary>
        /// Updates the shape owned by this stable piece id.  Hammer's interim
        /// behaviour removes cells without creating independent piece roots,
        /// so the grid identity must remain unchanged while its footprint is
        /// refreshed.
        /// </summary>
        public void ReplaceGeometry(
            GridCoordinate anchor,
            GridCoordinate pivotOffset,
            List<GridCoordinate> localCellCoordinates)
        {
            Anchor = anchor;
            PivotOffset = pivotOffset;
            localCells.Clear();
            localCells.AddRange(localCellCoordinates);
            IsOnBoard = true;
        }

        public void MarkOffBoard()
        {
            IsOnBoard = false;
        }

        public void MarkOnBoard()
        {
            IsOnBoard = true;
        }

        public bool SetState(PieceState state)
        {
            if (State == state)
                return false;

            State = state;
            return true;
        }
    }
}
