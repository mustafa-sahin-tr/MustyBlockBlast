using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>Result of applying one power-up to the board. Unlike <see cref="LineClearResult"/> this
    /// reports individual cells rather than whole lines, because a power-up clears an arbitrary region
    /// and never depends on a line being full.</summary>
    public readonly struct PowerUpClearResult
    {
        public PowerUpClearResult(IReadOnlyList<GridPosition> clearedCells)
        {
            ClearedCells = clearedCells;
        }

        /// <summary>Exactly the cells that held a colour before the clear — cells that were already
        /// empty inside the affected region are not reported.</summary>
        public IReadOnlyList<GridPosition> ClearedCells { get; }

        public int ClearedCellCount => ClearedCells.Count;

        public bool AnyCleared => ClearedCellCount > 0;
    }

    /// <summary>Clears a region of the board outside the normal placement flow: a 3x3 bomb area, a whole
    /// row, or a whole column. Fullness is irrelevant — unlike <see cref="LineClearResolver"/> these
    /// always clear, and simply report nothing when the target region was already empty.</summary>
    public static class PowerUpClearResolver
    {
        private const int BOMB_RADIUS = 1;

        /// <summary>Clears the 3x3 area centred on <paramref name="center"/>, clamped to the board — a
        /// corner centre therefore affects 4 cells and an edge centre 6.</summary>
        public static PowerUpClearResult ResolveBombClear(Board board, GridPosition center)
        {
            if (!Board.IsInside(center))
            {
                throw new ArgumentOutOfRangeException(nameof(center), center, "Outside the board.");
            }

            int minX = Math.Max(0, center.X - BOMB_RADIUS);
            int maxX = Math.Min(Board.SIZE - 1, center.X + BOMB_RADIUS);
            int minY = Math.Max(0, center.Y - BOMB_RADIUS);
            int maxY = Math.Min(Board.SIZE - 1, center.Y + BOMB_RADIUS);

            var clearedCells = new List<GridPosition>();

            // Occupancy is read before anything is cleared: once cleared, the cell is indistinguishable
            // from one that was already empty.
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    CollectIfOccupied(board, new GridPosition(x, y), clearedCells);
                }
            }

            ClearAll(board, clearedCells);
            return new PowerUpClearResult(clearedCells);
        }

        /// <summary>Clears every occupied cell of <paramref name="row"/>, whether or not the row is full.</summary>
        public static PowerUpClearResult ResolveRowClear(Board board, int row)
        {
            RequireInRange(row, nameof(row));

            var clearedCells = new List<GridPosition>();
            for (int x = 0; x < Board.SIZE; x++)
            {
                CollectIfOccupied(board, new GridPosition(x, row), clearedCells);
            }

            ClearAll(board, clearedCells);
            return new PowerUpClearResult(clearedCells);
        }

        /// <summary>Clears every occupied cell of <paramref name="column"/>, whether or not it is full.</summary>
        public static PowerUpClearResult ResolveColumnClear(Board board, int column)
        {
            RequireInRange(column, nameof(column));

            var clearedCells = new List<GridPosition>();
            for (int y = 0; y < Board.SIZE; y++)
            {
                CollectIfOccupied(board, new GridPosition(column, y), clearedCells);
            }

            ClearAll(board, clearedCells);
            return new PowerUpClearResult(clearedCells);
        }

        private static void CollectIfOccupied(Board board, GridPosition position, List<GridPosition> clearedCells)
        {
            if (board.IsOccupied(position))
            {
                clearedCells.Add(position);
            }
        }

        private static void ClearAll(Board board, List<GridPosition> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                board.Clear(cells[i]);
            }
        }

        private static void RequireInRange(int index, string parameterName)
        {
            if (index < 0 || index >= Board.SIZE)
            {
                throw new ArgumentOutOfRangeException(parameterName, index, "Outside the board.");
            }
        }
    }
}
