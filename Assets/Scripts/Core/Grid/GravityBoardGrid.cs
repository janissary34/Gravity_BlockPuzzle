using System.Collections.Generic;
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

        public bool TryGetOccupantId(GridCoordinate coordinate, out int occupantId)
        {
            occupantId = default;
            if (!IsInside(coordinate))
                return false;

            GridCellState state = cellStates[coordinate.X, coordinate.Y];
            if (state != GridCellState.Occupied && state != GridCellState.Reserved)
                return false;

            occupantId = occupantIds[coordinate.X, coordinate.Y];
            return true;
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
        /// Atomically moves a connected piece group. During validation every
        /// current group footprint is ignored, which permits members to move
        /// through one another's old cells while still rejecting all external
        /// occupancy, bounds and duplicate-target failures.
        /// </summary>
        public bool TryMoveGroupIgnoringMembers(
            IReadOnlyList<PieceModel> pieces,
            IReadOnlyList<GridCoordinate> targetAnchors,
            out GridPlacementResult result)
        {
            result = CheckGroupPlacementIgnoringMembers(pieces, targetAnchors);
            if (!result.IsSuccess)
                return false;

            for (int index = 0; index < pieces.Count; index++)
                ClearPieceCells(pieces[index]);

            for (int index = 0; index < pieces.Count; index++)
            {
                PieceModel piece = pieces[index];
                piece.SetAnchor(targetAnchors[index]);
                SetPieceCells(piece, GridCellState.Occupied);
            }

            return true;
        }

        /// <summary>
        /// Validates a simultaneous group move without mutating board state.
        /// The caller owns the supplied buffers, keeping the gravity hot path
        /// allocation-free.
        /// </summary>
        public GridPlacementResult CheckGroupPlacementIgnoringMembers(
            IReadOnlyList<PieceModel> pieces,
            IReadOnlyList<GridCoordinate> targetAnchors)
        {
            if (pieces == null || targetAnchors == null || pieces.Count == 0 ||
                pieces.Count != targetAnchors.Count)
            {
                return GridPlacementResult.Failure(
                    GridPlacementFailureReason.EmptyPiece,
                    default,
                    GridCellState.Empty,
                    default);
            }

            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                PieceModel piece = pieces[pieceIndex];
                if (piece == null || !piece.IsOnBoard || piece.LocalCells.Count == 0 ||
                    IsDuplicateGroupMember(pieces, pieceIndex))
                {
                    return GridPlacementResult.Failure(
                        GridPlacementFailureReason.EmptyPiece,
                        targetAnchors[pieceIndex],
                        GridCellState.Empty,
                        default);
                }

                for (int localIndex = 0; localIndex < piece.LocalCells.Count; localIndex++)
                {
                    GridCoordinate coordinate = targetAnchors[pieceIndex].Offset(piece.LocalCells[localIndex]);
                    if (!IsInside(coordinate))
                    {
                        return GridPlacementResult.Failure(
                            GridPlacementFailureReason.OutOfBounds,
                            coordinate,
                            GridCellState.Blocked,
                            default);
                    }

                    if (TargetCoordinateIsDuplicated(
                            pieces,
                            targetAnchors,
                            pieceIndex,
                            localIndex,
                            coordinate))
                    {
                        return GridPlacementResult.Failure(
                            GridPlacementFailureReason.DuplicateCell,
                            coordinate,
                            GridCellState.Empty,
                            default);
                    }

                    GridCellState state = cellStates[coordinate.X, coordinate.Y];
                    if (state == GridCellState.Empty)
                        continue;

                    int occupantId = occupantIds[coordinate.X, coordinate.Y];
                    if ((state == GridCellState.Occupied || state == GridCellState.Reserved) &&
                        ContainsGroupMember(pieces, occupantId))
                    {
                        continue;
                    }

                    return GridPlacementResult.Failure(
                        FailureReasonFor(state),
                        coordinate,
                        state,
                        occupantId);
                }
            }

            return GridPlacementResult.Success();
        }

        /// <summary>
        /// Calculates the lowest legal anchor for an on-board piece without
        /// mutating the grid or the piece model. The piece's current cells are
        /// ignored while checking each candidate so its own geometry never
        /// blocks a vertical move.
        /// </summary>
        public bool TryGetFallTarget(
            PieceModel piece,
            out GridCoordinate targetAnchor,
            System.Func<PieceModel, GridCoordinate, bool> canMovePiece = null)
        {
            targetAnchor = piece != null ? piece.Anchor : default;
            if (piece == null || !piece.IsOnBoard)
                return false;

            GridCoordinate candidate = targetAnchor.Offset(Down);
            while (CheckPlacementIgnoringPiece(piece, candidate, piece.Id).IsSuccess &&
                   (canMovePiece == null || canMovePiece(piece, candidate)))
            {
                targetAnchor = candidate;
                candidate = candidate.Offset(Down);
            }

            return !targetAnchor.Equals(piece.Anchor);
        }

        /// <summary>
        /// Returns the common lowest legal anchors for a piece group. Every
        /// member moves by the same vertical delta, so a support under any one
        /// member stops the full group.
        /// </summary>
        public bool TryGetGroupFallTarget(
            IReadOnlyList<PieceModel> pieces,
            List<GridCoordinate> targetAnchors,
            System.Func<PieceModel, GridCoordinate, bool> canMovePiece = null)
        {
            if (pieces == null || targetAnchors == null || pieces.Count == 0)
                return false;

            targetAnchors.Clear();
            for (int index = 0; index < pieces.Count; index++)
            {
                if (pieces[index] == null)
                {
                    targetAnchors.Clear();
                    return false;
                }

                targetAnchors.Add(pieces[index].Anchor);
            }

            bool moved = false;
            while (true)
            {
                for (int index = 0; index < targetAnchors.Count; index++)
                    targetAnchors[index] = targetAnchors[index].Offset(Down);

                bool valid = CheckGroupPlacementIgnoringMembers(pieces, targetAnchors).IsSuccess;
                if (valid && canMovePiece != null)
                {
                    for (int index = 0; index < pieces.Count; index++)
                    {
                        if (!canMovePiece(pieces[index], targetAnchors[index]))
                        {
                            valid = false;
                            break;
                        }
                    }
                }

                if (!valid)
                {
                    for (int index = 0; index < targetAnchors.Count; index++)
                        targetAnchors[index] = targetAnchors[index].Offset(new GridCoordinate(0, 1));
                    return moved;
                }

                moved = true;
            }
        }

        private static bool IsDuplicateGroupMember(IReadOnlyList<PieceModel> pieces, int pieceIndex)
        {
            PieceModel piece = pieces[pieceIndex];
            for (int previousIndex = 0; previousIndex < pieceIndex; previousIndex++)
            {
                if (pieces[previousIndex] != null && pieces[previousIndex].Id == piece.Id)
                    return true;
            }

            return false;
        }

        private static bool ContainsGroupMember(IReadOnlyList<PieceModel> pieces, int memberId)
        {
            for (int index = 0; index < pieces.Count; index++)
            {
                if (pieces[index] != null && pieces[index].Id == memberId)
                    return true;
            }

            return false;
        }

        private static bool TargetCoordinateIsDuplicated(
            IReadOnlyList<PieceModel> pieces,
            IReadOnlyList<GridCoordinate> targetAnchors,
            int pieceIndex,
            int localIndex,
            GridCoordinate coordinate)
        {
            for (int previousPieceIndex = 0; previousPieceIndex <= pieceIndex; previousPieceIndex++)
            {
                PieceModel previousPiece = pieces[previousPieceIndex];
                int localCellCount = previousPiece.LocalCells.Count;
                if (previousPieceIndex == pieceIndex)
                    localCellCount = localIndex;

                for (int previousLocalIndex = 0; previousLocalIndex < localCellCount; previousLocalIndex++)
                {
                    if (coordinate.Equals(targetAnchors[previousPieceIndex].Offset(
                            previousPiece.LocalCells[previousLocalIndex])))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static GridPlacementFailureReason FailureReasonFor(GridCellState state)
        {
            switch (state)
            {
                case GridCellState.Blocked:
                    return GridPlacementFailureReason.BlockedCell;
                case GridCellState.Occupied:
                    return GridPlacementFailureReason.OccupiedCell;
                case GridCellState.Reserved:
                    return GridPlacementFailureReason.ReservedCell;
                default:
                    return GridPlacementFailureReason.None;
            }
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

        /// <summary>
        /// Checks that every currently authored cell of a piece is still owned
        /// by that piece as a shredder reservation. This is used only by the
        /// atomic handoff rollback path; normal gameplay never infers state
        /// from collider contacts.
        /// </summary>
        public bool IsFullyReservedBy(PieceModel piece)
        {
            if (piece == null)
                return false;

            for (int index = 0; index < piece.LocalCells.Count; index++)
            {
                GridCoordinate coordinate = piece.GetWorldCell(index);
                if (!IsInside(coordinate) ||
                    cellStates[coordinate.X, coordinate.Y] != GridCellState.Reserved ||
                    occupantIds[coordinate.X, coordinate.Y] != piece.Id)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reverts a not-yet-activated shredder reservation to its original
        /// occupied footprint. Callers must pair this with the explicit
        /// HandoffToPhysics-to-Placed transaction recovery transition.
        /// </summary>
        public bool TryRestoreReservedPiece(PieceModel piece)
        {
            if (!IsFullyReservedBy(piece))
                return false;

            SetPieceCells(piece, GridCellState.Occupied);
            piece.MarkOnBoard();
            return true;
        }

        public void ClearPiece(PieceModel piece)
        {
            ClearPieceCells(piece);
            piece.MarkOffBoard();
        }

        /// <summary>
        /// Releases a subset of a shredding piece's footprint. The shredder
        /// hands a piece through HandoffToPhysics and Shredding on separate
        /// lifecycle calls; both states must expose the cells that have already
        /// crossed the cutter. Remaining cells continue to block gravity.
        /// </summary>
        public bool TryReleaseReservedCells(
            PieceModel piece,
            IReadOnlyList<GridCoordinate> localCells)
        {
            if (piece == null || localCells == null)
                return false;

            bool releasedAny = false;
            for (int index = 0; index < localCells.Count; index++)
            {
                GridCoordinate coordinate = piece.Anchor.Offset(localCells[index]);
                if (!IsInside(coordinate) ||
                    (cellStates[coordinate.X, coordinate.Y] != GridCellState.Reserved &&
                     cellStates[coordinate.X, coordinate.Y] != GridCellState.Occupied) ||
                    occupantIds[coordinate.X, coordinate.Y] != piece.Id)
                    continue;

                cellStates[coordinate.X, coordinate.Y] = GridCellState.Empty;
                occupantIds[coordinate.X, coordinate.Y] = default;
                releasedAny = true;
            }

            return releasedAny;
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
