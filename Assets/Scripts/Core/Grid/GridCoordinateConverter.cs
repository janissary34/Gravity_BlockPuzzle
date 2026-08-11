using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Core.Grid
{
    public sealed class GridCoordinateConverter
    {
        private readonly int columns;
        private readonly int rows;
        private readonly int subdivisions;
        private readonly float fineCellSize;
        private readonly Vector2 origin;

        public GridCoordinateConverter(GridConfig config)
        {
            columns = config.Columns;
            rows = config.Rows;
            subdivisions = config.Subdivisions;
            fineCellSize = config.CellSize / subdivisions;
            origin = config.Origin;
        }

        public Vector2 CellToWorld(GridCoordinate coordinate)
        {
            float fineColumns = columns * subdivisions;
            float fineRows = rows * subdivisions;
            return origin + new Vector2(
                (coordinate.X + .5f - fineColumns * .5f) * fineCellSize,
                (coordinate.Y + .5f - fineRows * .5f) * fineCellSize);
        }

        public bool TryWorldToCell(Vector2 worldPosition, out GridCoordinate coordinate)
        {
            float fineColumns = columns * subdivisions;
            float fineRows = rows * subdivisions;
            Vector2 bottomLeft = origin - new Vector2(
                fineColumns * fineCellSize * .5f,
                fineRows * fineCellSize * .5f);
            int x = Mathf.FloorToInt((worldPosition.x - bottomLeft.x) / fineCellSize);
            int y = Mathf.FloorToInt((worldPosition.y - bottomLeft.y) / fineCellSize);
            coordinate = new GridCoordinate(x, y);
            return x >= 0 && x < fineColumns && y >= 0 && y < fineRows;
        }
    }
}
