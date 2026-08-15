using GravityPuzzle.Gameplay.Pieces;

namespace GravityPuzzle.Core.Grid
{
    public sealed class GravityBoardGrid
    {
        private static readonly GridCoordinate Down = new GridCoordinate(0, -1);

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
            TrySetBlocked(coordinate, out _);
        }

        public bool TrySetBlocked(GridCoordinate coordinate, out GridPlacementResult result)
        {
            if (!IsInside(coordinate))
            {
                result = GridPlacementResult.Failure(
                    GridPlacementFailureReason.OutOfBounds,
                    coordinate,
                    GridCellState.Blocked,
                    default);
                return false;
            }

            GridCellState state = cellStates[coordinate.X, coordinate.Y];
            if (state == GridCellState.Occupied || state == GridCellState.Reserved)
            {
                GridPlacementFailureReason reason = state == GridCellState.Occupied
                    ? GridPlacementFailureReason.OccupiedCell
                    : GridPlacementFailureReason.ReservedCell;
                result = GridPlacementResult.Failure(
                    reason,
                    coordinate,
                    state,
                    occupantIds[coordinate.X, coordinate.Y]);
                return false;
            }

            if (state == GridCellState.Empty)
                cellStates[coordinate.X, coordinate.Y] = GridCellState.Blocked;

            result = GridPlacementResult.Success();
            return true;
        }

        public bool CanPlace(PieceModel piece, GridCoordinate anchor)
        {
            return CheckPlacement(piece, anchor).IsSuccess;
        }

        public bool CanPlaceIgnoringPiece(PieceModel piece, GridCoordinate anchor, int ignoredPieceId)
        {
            return CheckPlacementIgnoringPiece(piece, anchor, ignoredPieceId).IsSuccess;
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
            piece.MarkOnBoard();
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

        public GridPlacementResult CheckPlacementIgnoringPiece(
            PieceModel piece,
            GridCoordinate anchor,
            int ignoredPieceId)
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
                bool isIgnoredPieceCell =
                    (state == GridCellState.Occupied || state == GridCellState.Reserved) &&
                    occupantIds[coordinate.X, coordinate.Y] == ignoredPieceId;
                if (state == GridCellState.Empty || isIgnoredPieceCell)
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

        public bool TryMoveIgnoringPiece(
            PieceModel piece,
            GridCoordinate targetAnchor,
            int ignoredPieceId,
            out GridPlacementResult result)
        {
            result = CheckPlacementIgnoringPiece(piece, targetAnchor, ignoredPieceId);
            if (!result.IsSuccess)
                return false;

            ClearPieceCells(piece);
            piece.SetAnchor(targetAnchor);
            SetPieceCells(piece, GridCellState.Occupied);
            return true;
        }

        /// <summary>
        /// Calculates the lowest legal anchor for an on-board piece without
        /// mutating the grid or the piece model. The piece's current cells are
        /// ignored while checking each candidate so its own geometry never
        /// blocks a vertical move.
        /// </summary>
        public bool TryGetFallTarget(PieceModel piece, out GridCoordinate targetAnchor)
        {
            targetAnchor = piece != null ? piece.Anchor : default;
            if (piece == null || !piece.IsOnBoard)
                return false;

            GridCoordinate candidate = targetAnchor.Offset(Down);
            while (CheckPlacementIgnoringPiece(piece, candidate, piece.Id).IsSuccess)
            {
                targetAnchor = candidate;
                candidate = candidate.Offset(Down);
            }

            return !targetAnchor.Equals(piece.Anchor);
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
            piece.MarkOnBoard();
            return true;
        }

        public void ClearPiece(PieceModel piece)
        {
            ClearPieceCells(piece);
            piece.MarkOffBoard();
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
