using System;

namespace MustyBlockBlast.Core
{
    /// <summary>Integer board coordinate. Origin is bottom-left.</summary>
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public int X { get; }
        public int Y { get; }

        public GridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static GridPosition operator +(GridPosition a, GridPosition b)
            => new GridPosition(a.X + b.X, a.Y + b.Y);

        public bool Equals(GridPosition other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPosition other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public override string ToString() => $"({X},{Y})";
    }
}
