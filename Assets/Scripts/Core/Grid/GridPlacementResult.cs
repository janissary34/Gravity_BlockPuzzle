namespace GravityPuzzle.Core.Grid
{
    public readonly struct GridPlacementResult
    {
        public static GridPlacementResult Success()
        {
            return new GridPlacementResult(true, GridPlacementFailureReason.None, default, GridCellState.Empty, default);
        }

        public static GridPlacementResult Failure(
            GridPlacementFailureReason reason,
            GridCoordinate coordinate,
            GridCellState cellState,
            int occupantId)
        {
            return new GridPlacementResult(false, reason, coordinate, cellState, occupantId);
        }

        private GridPlacementResult(
            bool isSuccess,
            GridPlacementFailureReason reason,
            GridCoordinate coordinate,
            GridCellState cellState,
            int occupantId)
        {
            IsSuccess = isSuccess;
            Reason = reason;
            Coordinate = coordinate;
            CellState = cellState;
            OccupantId = occupantId;
        }

        public bool IsSuccess { get; }
        public GridPlacementFailureReason Reason { get; }
        public GridCoordinate Coordinate { get; }
        public GridCellState CellState { get; }
        public int OccupantId { get; }
    }
}
