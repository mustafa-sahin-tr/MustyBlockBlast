using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>Result of applying one power-up to the board. Unlike <see cref="LineClearResult"/> this
    /// reports individual cells rather than whole lines, because a power-up clears an arbitrary region
    /// and never depends on a line being full.</summary>
    public readonly struct PowerUpClearResult
    {
        public PowerUpClearResult(
            IReadOnlyList<GridPosition> clearedCells,
            IReadOnlyList<int> emptiedRows,
            IReadOnlyList<int> emptiedColumns,
            IReadOnlyList<SpecialCellTrigger> triggeredSpecials)
        {
            ClearedCells = clearedCells;
            EmptiedRows = emptiedRows;
            EmptiedColumns = emptiedColumns;
            TriggeredSpecials = triggeredSpecials;
        }

        /// <summary>Exactly the cells that held a colour before the clear — cells that were already
        /// empty inside the affected region are not reported.</summary>
        public IReadOnlyList<GridPosition> ClearedCells { get; }

        /// <summary>Rows that had at least one cleared cell and, after clearing, have zero occupied
        /// cells — a row this clear happened to empty out entirely. Note this is the OPPOSITE
        /// condition from a normal line-clear (which fires on a row becoming full, not empty); a
        /// Row Clear/Column Clear power-up will trivially always report its own target line here
        /// (clearing a line necessarily empties it), so this is only a meaningful "surprise" signal
        /// for a power-up whose target doesn't already guarantee it, like Bomb.</summary>
        public IReadOnlyList<int> EmptiedRows { get; }

        /// <summary>Columns that had at least one cleared cell and, after clearing, are fully empty.</summary>
        public IReadOnlyList<int> EmptiedColumns { get; }

        /// <summary>
        /// The special cells this clear destroyed, found through the same
        /// <see cref="SpecialCellDetection"/> pass a placement's line clear uses — a special block is
        /// destroyed by a Bomb exactly as it is by a completed line, and neither resolver gets its own
        /// idea of what counts as destroying one.
        /// <para>
        /// Always empty while every kind is <see cref="SpecialCellKind.None"/>. Reported rather than
        /// applied here: a power-up clear is not a placement, so whether its triggers feed
        /// <see cref="CascadeClearResolver"/>'s loop is a decision for the first sub-issue that ships
        /// a real effect, made by the calling System with a real effect in hand.
        /// </para>
        /// </summary>
        public IReadOnlyList<SpecialCellTrigger> TriggeredSpecials { get; }

        public int ClearedCellCount => ClearedCells.Count;

        public bool AnyCleared => ClearedCellCount > 0;

        public int EmptiedLineCount => EmptiedRows.Count + EmptiedColumns.Count;

        public bool AnyLineEmptied => EmptiedLineCount > 0;
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

        /// <summary>Shared, never-mutated empties for a rejected <see cref="ResolveColorCleanser"/>
        /// call — avoids allocating a fresh empty list for the common "tapped nothing" case.</summary>
        private static readonly GridPosition[] EmptyCells = new GridPosition[0];
        private static readonly int[] EmptyLines = new int[0];
        private static readonly SpecialCellTrigger[] EmptyTriggers = new SpecialCellTrigger[0];

        /// <summary>Holds the single line index a Row Clear / Column Clear targeted, so detection can be
        /// told which axis destroyed the cells. Reused rather than allocated per call for the same
        /// reason <see cref="TargetBuffer"/> is, and equally never escapes a Resolve call.</summary>
        private static readonly int[] AxisLineBuffer = new int[1];

        /// <summary>Clears the 3x3 area centred on <paramref name="center"/>, clamped to the board — a
        /// corner centre therefore affects 4 cells and an edge centre 6.</summary>
        public static PowerUpClearResult ResolveBombClear(Board board, GridPosition center)
        {
            RequireBoard(board);

            if (!board.IsInside(center))
            {
                throw new ArgumentOutOfRangeException(nameof(center), center, "Outside the board.");
            }

            // No axis: a 3x3 is not a line, so a special cell destroyed by it has no "opposite"
            // direction to be given and must not be handed a made-up one.
            return ClearTargeted(
                board, PowerUpTargetCells.ForBomb(board.Shape, center, TargetBuffer), null, null);
        }

        /// <summary>Clears every occupied cell of <paramref name="row"/>, whether or not the row is full.</summary>
        public static PowerUpClearResult ResolveRowClear(Board board, int row)
        {
            RequireBoard(board);
            RequireInRange(PowerUpTargetCells.IsValidRowIndex(board.Shape, row), row, nameof(row));

            // Reported as a row clear, exactly as a completed row is: a special cell cares about what
            // destroyed it, never about whether the line happened to be full at the time.
            AxisLineBuffer[0] = row;
            return ClearTargeted(
                board, PowerUpTargetCells.ForRow(board.Shape, row, TargetBuffer), AxisLineBuffer, null);
        }

        /// <summary>Clears every occupied cell of <paramref name="column"/>, whether or not it is full.</summary>
        public static PowerUpClearResult ResolveColumnClear(Board board, int column)
        {
            RequireBoard(board);
            RequireInRange(PowerUpTargetCells.IsValidColumnIndex(board.Shape, column), column, nameof(column));

            AxisLineBuffer[0] = column;
            return ClearTargeted(
                board, PowerUpTargetCells.ForColumn(board.Shape, column, TargetBuffer), null, AxisLineBuffer);
        }

        /// <summary>
        /// Clears every cell on the whole board sharing <paramref name="target"/>'s colour. An empty
        /// target has no colour to extract and is rejected outright — nothing is read or written —
        /// so <see cref="PowerUpClearResult.AnyCleared"/> doubles as this call's legality signal: the
        /// caller (<c>PowerUpSystem.TryApplyColorCleanser</c>) must treat a false result as "the player
        /// aimed at nothing" and charge for nothing, the same contract <see cref="JokerFillResolver"/>
        /// establishes for an illegal joker target.
        /// </summary>
        public static PowerUpClearResult ResolveColorCleanser(Board board, GridPosition target)
        {
            RequireBoard(board);

            if (!board.IsInside(target))
            {
                throw new ArgumentOutOfRangeException(nameof(target), target, "Outside the board.");
            }

            int colourId = board[target];
            if (colourId == Board.EMPTY)
            {
                return new PowerUpClearResult(EmptyCells, EmptyLines, EmptyLines, EmptyTriggers);
            }

            var matchingCells = new List<GridPosition>();
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    GridPosition position = new GridPosition(x, y);
                    if (board[position] == colourId)
                    {
                        matchingCells.Add(position);
                    }
                }
            }

            // No axis, for the reason a Bomb has none: a colour is not a line.
            return ClearAndReport(board, matchingCells, null, null);
        }

        /// <summary>Clears whichever of <paramref name="targetedCells"/> hold a colour, and reports
        /// exactly those. Occupancy is read before anything is cleared: once cleared, a cell is
        /// indistinguishable from one that was already empty.</summary>
        private static PowerUpClearResult ClearTargeted(
            Board board,
            IReadOnlyList<GridPosition> targetedCells,
            IReadOnlyList<int> axisRows,
            IReadOnlyList<int> axisColumns)
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

            return ClearAndReport(board, clearedCells, axisRows, axisColumns);
        }

        /// <summary>Clears exactly <paramref name="clearedCells"/> (every entry is assumed already
        /// verified occupied) and reports which rows/columns that emptied out entirely. Shared by every
        /// resolve method so the emptied-line computation has exactly one implementation.
        /// <para>
        /// <paramref name="axisRows"/>/<paramref name="axisColumns"/> name the lines this clear was
        /// aimed at, if any, so each triggered special records what destroyed it. Both null for the
        /// kinds that target no line at all.
        /// </para>
        /// </summary>
        private static PowerUpClearResult ClearAndReport(
            Board board,
            List<GridPosition> clearedCells,
            IReadOnlyList<int> axisRows,
            IReadOnlyList<int> axisColumns)
        {
            // Before ClearAll, for the same reason occupancy was read before it: Board.Clear resets a
            // cell's special kind along with its colour, so this is the last moment the kinds exist.
            var triggeredSpecials = new List<SpecialCellTrigger>();
            SpecialCellDetection.CollectTriggered(
                board, clearedCells, triggeredSpecials, axisRows, axisColumns);

            ClearAll(board, clearedCells);

            // Only rows/columns a cleared cell actually belonged to can have changed emptiness — no
            // need to scan the whole board. Each cleared cell was occupied before this clear, so every
            // row/column it touches necessarily had at least one occupied cell; checked post-clear,
            // "now empty" can only be true because of what this clear just removed.
            var emptiedRows = new List<int>();
            var emptiedColumns = new List<int>();
            for (int i = 0; i < clearedCells.Count; i++)
            {
                GridPosition position = clearedCells[i];

                if (!emptiedRows.Contains(position.Y) && board.IsRowEmpty(position.Y))
                {
                    emptiedRows.Add(position.Y);
                }

                if (!emptiedColumns.Contains(position.X) && board.IsColumnEmpty(position.X))
                {
                    emptiedColumns.Add(position.X);
                }
            }

            return new PowerUpClearResult(clearedCells, emptiedRows, emptiedColumns, triggeredSpecials);
        }

        private static void ClearAll(Board board, List<GridPosition> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                board.Clear(cells[i]);
            }
        }

        private static void RequireBoard(Board board)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }
        }

        private static void RequireInRange(bool isInRange, int index, string parameterName)
        {
            if (!isInRange)
            {
                throw new ArgumentOutOfRangeException(parameterName, index, "Outside the board.");
            }
        }
    }
}
