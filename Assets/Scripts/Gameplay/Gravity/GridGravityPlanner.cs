using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;

namespace GravityPuzzle.Gameplay.Gravity
{
    /// <summary>
    /// Chooses one deterministic, legal gravity move from the board snapshot.
    /// It has no Unity or physics dependency; a later presentation step will
    /// animate the chosen move and commit it through GravityBoardGrid.
    /// </summary>
    public static class GridGravityPlanner
    {
        public static bool TryPlanNextMove(
            LevelBoardSnapshot snapshot,
            out GridGravityMove move)
        {
            move = default;
            if (snapshot == null)
                return false;

            bool foundMove = false;
            for (int index = 0; index < snapshot.Pieces.Count; index++)
            {
                PieceModel piece = snapshot.Pieces[index];
                if (!snapshot.Grid.TryGetFallTarget(piece, out GridCoordinate targetAnchor))
                    continue;

                GridGravityMove candidate = new GridGravityMove(
                    piece.Id,
                    piece.Anchor,
                    targetAnchor);
                if (!foundMove || candidate.ShouldRunBefore(move))
                {
                    move = candidate;
                    foundMove = true;
                }
            }

            return foundMove;
        }
    }

    public readonly struct GridGravityMove
    {
        public int PieceId { get; }
        public GridCoordinate FromAnchor { get; }
        public GridCoordinate ToAnchor { get; }

        public GridGravityMove(
            int pieceId,
            GridCoordinate fromAnchor,
            GridCoordinate toAnchor)
        {
            PieceId = pieceId;
            FromAnchor = fromAnchor;
            ToAnchor = toAnchor;
        }

        public bool ShouldRunBefore(GridGravityMove other)
        {
            // Resolve the lowest landing piece first. That makes a cascade
            // repeatable: lower moves commit before they can unblock pieces
            // above them. Piece id is a stable tie-breaker.
            int landingOrder = ToAnchor.Y.CompareTo(other.ToAnchor.Y);
            return landingOrder != 0
                ? landingOrder < 0
                : PieceId < other.PieceId;
        }
    }
}
