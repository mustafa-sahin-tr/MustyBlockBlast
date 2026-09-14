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
        /// <summary>
        /// Scratch buffer for the targeted geometry handed back by <see cref="PowerUpTargetCells"/>.
        /// It never escapes a Resolve call, so reusing it costs nothing and keeps applying a power-up
        /// down to the one allocation that actually leaves: the result list.
        /// </summary>
        private static readonly List<GridPosition> TargetBuffer =
            new List<GridPosition>(PowerUpTargetCells.MAX_TARGET_CELLS);

        /// <summary>Clears the 3x3 area centred on <paramref name="center"/>, clamped to the board — a
        /// corner centre therefore affects 4 cells and an edge centre 6.</summary>
        public static PowerUpClearResult ResolveBombClear(Board board, GridPosition center)
        {
            if (!Board.IsInside(center))
            {
                throw new ArgumentOutOfRangeException(nameof(center), center, "Outside the board.");
            }

            return ClearTargeted(board, PowerUpTargetCells.ForBomb(center, TargetBuffer));
        }

        /// <summary>Clears every occupied cell of <paramref name="row"/>, whether or not the row is full.</summary>
        public static PowerUpClearResult ResolveRowClear(Board board, int row)
        {
            RequireInRange(row, nameof(row));
            return ClearTargeted(board, PowerUpTargetCells.ForRow(row, TargetBuffer));
        }

        /// <summary>Clears every occupied cell of <paramref name="column"/>, whether or not it is full.</summary>
        public static PowerUpClearResult ResolveColumnClear(Board board, int column)
        {
            RequireInRange(column, nameof(column));
            return ClearTargeted(board, PowerUpTargetCells.ForColumn(column, TargetBuffer));
        }

        /// <summary>Clears whichever of <paramref name="targetedCells"/> hold a colour, and reports
        /// exactly those. Occupancy is read before anything is cleared: once cleared, a cell is
        /// indistinguishable from one that was already empty.</summary>
        private static PowerUpClearResult ClearTargeted(Board board, IReadOnlyList<GridPosition> targetedCells)
        {
            var clearedCells = new List<GridPosition>();
            for (int i = 0; i < targetedCells.Count; i++)
            {
                GridPosition position = targetedCells[i];
                if (board.IsOccupied(position))
                {
                    clearedCells.Add(position);
                }
            }

            ClearAll(board, clearedCells);
            return new PowerUpClearResult(clearedCells);
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
