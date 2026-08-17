namespace GravityPuzzle.Core.Grid
{
    public readonly struct LevelBoardSnapshotIssue
    {
        public LevelBoardSnapshotIssue(int pieceId, string pieceName, GridPlacementResult placementResult)
            : this(
                LevelBoardSnapshotIssueKind.PiecePlacement,
                pieceId,
                pieceName,
                string.Empty,
                placementResult)
        {
        }

        public LevelBoardSnapshotIssue(
            LevelBoardSnapshotIssueKind kind,
            string source,
            GridPlacementResult placementResult)
            : this(kind, -1, string.Empty, source, placementResult)
        {
        }

        private LevelBoardSnapshotIssue(
            LevelBoardSnapshotIssueKind kind,
            int pieceId,
            string pieceName,
            string source,
            GridPlacementResult placementResult)
        {
            Kind = kind;
            PieceId = pieceId;
            PieceName = pieceName;
            Source = source;
            PlacementResult = placementResult;
        }

        public LevelBoardSnapshotIssueKind Kind { get; }
        public int PieceId { get; }
        public string PieceName { get; }
        public string Source { get; }
        public GridPlacementResult PlacementResult { get; }
    }
}
