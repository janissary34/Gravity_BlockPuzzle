namespace GravityPuzzle.Core.Grid
{
    public readonly struct LevelBoardSnapshotIssue
    {
        public LevelBoardSnapshotIssue(int pieceId, string pieceName, GridPlacementResult placementResult)
        {
            PieceId = pieceId;
            PieceName = pieceName;
            PlacementResult = placementResult;
        }

        public int PieceId { get; }
        public string PieceName { get; }
        public GridPlacementResult PlacementResult { get; }
    }
}
