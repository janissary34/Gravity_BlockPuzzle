namespace GravityPuzzle.Core.Grid
{
    public enum GridPlacementFailureReason
    {
        None,
        EmptyPiece,
        DuplicateCell,
        OutOfBounds,
        BlockedCell,
        OccupiedCell,
        ReservedCell
    }
}
