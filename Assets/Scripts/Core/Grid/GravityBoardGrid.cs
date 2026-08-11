using GravityPuzzle.Gameplay.Pieces;

namespace GravityPuzzle.Core.Grid
{
    public sealed class GravityBoardGrid
    {
        private readonly GridCellState[,] cellStates;
        private readonly int[,] occupantIds;

        public int Columns { get; }
        public int Rows { get; }

        public GravityBoardGrid(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            cellStates = new GridCellState[columns, rows];
            occupantIds = new int[columns, rows];
        }

        public bool IsInside(GridCoordinate coordinate)
        {
            return coordinate.X >= 0 && coordinate.X < Columns &&
                   coordinate.Y >= 0 && coordinate.Y < Rows;
        }

        public GridCellState GetCellState(GridCoordinate coordinate)
        {
            return IsInside(coordinate)
                ? cellStates[coordinate.X, coordinate.Y]
                : GridCellState.Blocked;
        }

        public int GetOccupantId(GridCoordinate coordinate)
        {
            return IsInside(coordinate)
                ? occupantIds[coordinate.X, coordinate.Y]
                : default;
        }

        public void SetBlocked(GridCoordinate coordinate)
        {
            if (!IsInside(coordinate) || cellStates[coordinate.X, coordinate.Y] != GridCellState.Empty)
                return;

            cellStates[coordinate.X, coordinate.Y] = GridCellState.Blocked;
        }

        public bool CanPlace(PieceModel piece, GridCoordinate anchor)
        {
            return CheckPlacement(piece, anchor).IsSuccess;
        }

        public bool TryPlace(PieceModel piece)
        {
            return TryPlace(piece, out _);
        }

        public bool TryPlace(PieceModel piece, out GridPlacementResult result)
        {
            result = CheckPlacement(piece, piece.Anchor);
            if (!result.IsSuccess)
                return false;

            SetPieceCells(piece, GridCellState.Occupied);
            return true;
        }

        public GridPlacementResult CheckPlacement(PieceModel piece, GridCoordinate anchor)
        {
            if (piece.LocalCells.Count == 0)
                return GridPlacementResult.Failure(
                    GridPlacementFailureReason.EmptyPiece,
                    anchor,
                    GridCellState.Empty,
                    default);

            for (int index = 0; index < piece.LocalCells.Count; index++)
            {
                GridCoordinate coordinate = anchor.Offset(piece.LocalCells[index]);
                for (int previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    if (coordinate.Equals(anchor.Offset(piece.LocalCells[previousIndex])))
                        return GridPlacementResult.Failure(
                            GridPlacementFailureReason.DuplicateCell,
                            coordinate,
                            GridCellState.Empty,
                            default);
                }

                if (!IsInside(coordinate))
                    return GridPlacementResult.Failure(
                        GridPlacementFailureReason.OutOfBounds,
                        coordinate,
                        GridCellState.Blocked,
                        default);

                GridCellState state = cellStates[coordinate.X, coordinate.Y];
                if (state == GridCellState.Empty)
                    continue;

                GridPlacementFailureReason reason;
                switch (state)
                {
                    case GridCellState.Blocked:
                        reason = GridPlacementFailureReason.BlockedCell;
                        break;
                    case GridCellState.Occupied:
                        reason = GridPlacementFailureReason.OccupiedCell;
                        break;
                    case GridCellState.Reserved:
                        reason = GridPlacementFailureReason.ReservedCell;
                        break;
                    default:
                        reason = GridPlacementFailureReason.None;
                        break;
                }

                return GridPlacementResult.Failure(
                    reason,
                    coordinate,
                    state,
                    occupantIds[coordinate.X, coordinate.Y]);
            }

            return GridPlacementResult.Success();
        }

        public bool TryMove(PieceModel piece, GridCoordinate targetAnchor)
        {
            ClearPieceCells(piece);
            if (!CanPlace(piece, targetAnchor))
            {
                SetPieceCells(piece, GridCellState.Occupied);
                return false;
            }

            piece.SetAnchor(targetAnchor);
            SetPieceCells(piece, GridCellState.Occupied);
            return true;
        }

        public bool TryReserve(PieceModel piece)
        {
            for (int index = 0; index < piece.LocalCells.Count; index++)
            {
                GridCoordinate coordinate = piece.GetWorldCell(index);
                if (!IsInside(coordinate) ||
                    cellStates[coordinate.X, coordinate.Y] != GridCellState.Occupied &&
                    cellStates[coordinate.X, coordinate.Y] != GridCellState.Reserved)
                    return false;
            }

            SetPieceCells(piece, GridCellState.Reserved);
            return true;
        }

        public void ClearPiece(PieceModel piece)
        {
            ClearPieceCells(piece);
        }

        private void SetPieceCells(PieceModel piece, GridCellState state)
        {
            for (int index = 0; index < piece.LocalCells.Count; index++)
            {
                GridCoordinate coordinate = piece.GetWorldCell(index);
                cellStates[coordinate.X, coordinate.Y] = state;
                occupantIds[coordinate.X, coordinate.Y] = piece.Id;
            }
        }

        private void ClearPieceCells(PieceModel piece)
        {
            for (int index = 0; index < piece.LocalCells.Count; index++)
            {
                GridCoordinate coordinate = piece.GetWorldCell(index);
                if (!IsInside(coordinate) || occupantIds[coordinate.X, coordinate.Y] != piece.Id)
                    continue;

                cellStates[coordinate.X, coordinate.Y] = GridCellState.Empty;
                occupantIds[coordinate.X, coordinate.Y] = default;
            }
        }
    }
}
