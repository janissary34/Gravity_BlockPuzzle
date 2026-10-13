namespace GravityPuzzle.Core.Grid
{
    /// <summary>
    /// Produces one orthogonal fine-grid drag candidate. The caller owns the
    /// authoritative model read and commit so every candidate can be checked
    /// against the latest board topology.
    /// </summary>
    public static class FineGridDragStepper
    {
        /// <summary>
        /// Returns the ordered orthogonal candidates for one point on a drag
        /// traversal. The candidate with the larger remaining error is tried
        /// first; the other axis is a deterministic slide fallback when the
        /// preferred route is blocked. A caller validates and commits each
        /// candidate before requesting the next pair.
        /// </summary>
        public static bool TryGetTraversalSteps(
            GridCoordinate currentAnchor,
            GridCoordinate targetAnchor,
            out GridCoordinate primaryDelta,
            out GridCoordinate fallbackDelta)
        {
            primaryDelta = default;
            fallbackDelta = default;

            int deltaX = targetAnchor.X - currentAnchor.X;
            int deltaY = targetAnchor.Y - currentAnchor.Y;
            if (deltaX == 0 && deltaY == 0)
                return false;

            GridCoordinate horizontal = deltaX == 0
                ? default
                : new GridCoordinate(deltaX > 0 ? 1 : -1, 0);
            GridCoordinate vertical = deltaY == 0
                ? default
                : new GridCoordinate(0, deltaY > 0 ? 1 : -1);

            if (deltaX == 0)
            {
                primaryDelta = vertical;
                return true;
            }

            if (deltaY == 0)
            {
                primaryDelta = horizontal;
                return true;
            }

            // Re-evaluating the remaining error after every committed cell
            // produces an interleaved, Bresenham-like orthogonal route without
            // storing drag history. Ties intentionally favour X so the path is
            // deterministic across input rates and platforms.
            if (System.Math.Abs(deltaX) >= System.Math.Abs(deltaY))
            {
                primaryDelta = horizontal;
                fallbackDelta = vertical;
            }
            else
            {
                primaryDelta = vertical;
                fallbackDelta = horizontal;
            }

            return true;
        }

        public static bool TryGetAxisStep(
            GridCoordinate currentAnchor,
            GridCoordinate targetAnchor,
            bool horizontalAxis,
            out GridCoordinate nextAnchor)
        {
            nextAnchor = currentAnchor;
            if (horizontalAxis)
            {
                if (currentAnchor.X == targetAnchor.X)
                    return false;

                int direction = targetAnchor.X > currentAnchor.X ? 1 : -1;
                nextAnchor = new GridCoordinate(currentAnchor.X + direction, currentAnchor.Y);
                return true;
            }

            if (currentAnchor.Y == targetAnchor.Y)
                return false;

            int verticalDirection = targetAnchor.Y > currentAnchor.Y ? 1 : -1;
            nextAnchor = new GridCoordinate(currentAnchor.X, currentAnchor.Y + verticalDirection);
            return true;
        }
    }
}
