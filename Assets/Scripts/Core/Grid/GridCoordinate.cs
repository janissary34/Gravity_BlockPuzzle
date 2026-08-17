using System;

namespace GravityPuzzle.Core.Grid
{
    public readonly struct GridCoordinate : IEquatable<GridCoordinate>
    {
        public readonly int X;
        public readonly int Y;

        public GridCoordinate(int x, int y)
        {
            X = x;
            Y = y;
        }

        public GridCoordinate Offset(GridCoordinate offset)
        {
            return new GridCoordinate(X + offset.X, Y + offset.Y);
        }

        public bool Equals(GridCoordinate other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object other)
        {
            return other is GridCoordinate coordinate && Equals(coordinate);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }
    }
}
