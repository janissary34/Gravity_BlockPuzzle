using System.Collections.Generic;
using GravityPuzzle.Core.Grid;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class PieceModel
    {
        private readonly List<GridCoordinate> localCells;

        public int Id { get; }
        public GridCoordinate Anchor { get; private set; }
        public IReadOnlyList<GridCoordinate> LocalCells => localCells;

        public PieceModel(int id, GridCoordinate anchor, List<GridCoordinate> localCellCoordinates)
        {
            Id = id;
            Anchor = anchor;
            localCells = localCellCoordinates;
        }

        public GridCoordinate GetWorldCell(int localCellIndex)
        {
            return Anchor.Offset(localCells[localCellIndex]);
        }

        public void SetAnchor(GridCoordinate anchor)
        {
            Anchor = anchor;
        }
    }
}
