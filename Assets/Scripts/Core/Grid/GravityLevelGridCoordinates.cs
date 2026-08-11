using UnityEngine;

namespace GravityPuzzle.Core.Grid
{
    public static class GravityLevelGridCoordinates
    {
        public const float DefaultAlignmentTolerance = .03f;

        public static GridCoordinate WorldToFineCell(
            GravityLevelDefinition level,
            Vector2 worldPosition)
        {
            int x = Mathf.FloorToInt((worldPosition.x + level.boardColumns * .5f) * level.subdivisions);
            int y = Mathf.FloorToInt((worldPosition.y + level.boardRows * .5f) * level.subdivisions);
            return new GridCoordinate(x, y);
        }

        public static Vector2 FineCellToWorld(
            GravityLevelDefinition level,
            GridCoordinate coordinate)
        {
            float fineCellSize = 1f / level.subdivisions;
            return new Vector2(
                -level.boardColumns * .5f + (coordinate.X + .5f) * fineCellSize,
                -level.boardRows * .5f + (coordinate.Y + .5f) * fineCellSize);
        }

        public static bool IsAlignedToFineCell(
            GravityLevelDefinition level,
            Vector2 worldPosition,
            GridCoordinate coordinate,
            float tolerance = DefaultAlignmentTolerance)
        {
            Vector2 cellCenter = FineCellToWorld(level, coordinate);
            return Mathf.Abs(worldPosition.x - cellCenter.x) <= tolerance &&
                   Mathf.Abs(worldPosition.y - cellCenter.y) <= tolerance;
        }
    }
}
