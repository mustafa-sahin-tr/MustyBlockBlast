using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Result of pointing a joker at one cell. Reports both shapes the callers need: the lines that
    /// cleared (scoring pays per line, exactly as a placement's clear does) and the individual cells
    /// that were emptied (the View repaints cells, not lines).
    /// </summary>
    public readonly struct JokerFillResult
    {
        private static readonly int[] NoLines = new int[0];
        private static readonly GridPosition[] NoCells = new GridPosition[0];

        internal JokerFillResult(
            bool filled,
            GridPosition position,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            IReadOnlyList<GridPosition> clearedCells)
        {
            Filled = filled;
            Position = position;
            ClearedRows = clearedRows;
            ClearedColumns = clearedColumns;
            ClearedCells = clearedCells;
        }

        /// <summary>Whether the cell was actually filled. False means nothing on the board changed —
        /// the target was off the board or already occupied — so the caller must charge nothing.</summary>
        public bool Filled { get; }

        /// <summary>The targeted cell, filled or not.</summary>
        public GridPosition Position { get; }

        /// <summary>The one row that became full, or nothing. Never more than one: a single filled
        /// cell can only complete the row it sits in.</summary>
        public IReadOnlyList<int> ClearedRows { get; }

        /// <summary>The one column that became full, or nothing.</summary>
        public IReadOnlyList<int> ClearedColumns { get; }

        /// <summary>Every cell emptied by the clear, each listed once — the row/column intersection
        /// cell (which is always <see cref="Position"/> when both lines cleared) is not repeated.</summary>
        public IReadOnlyList<GridPosition> ClearedCells { get; }

        public int ClearedCellCount => ClearedCells.Count;

        /// <summary>0, 1 or 2 — the "lines" term <see cref="ScoreRules.ClearScore"/> takes.</summary>
        public int LineCount => ClearedRows.Count + ClearedColumns.Count;

        public bool AnyCleared => LineCount > 0;

        /// <summary>A target that could not be filled: nothing was read, written or cleared.</summary>
        internal static JokerFillResult Rejected(GridPosition position)
            => new JokerFillResult(false, position, NoLines, NoLines, NoCells);
    }

    /// <summary>
    /// Fills one empty cell and clears whichever of that cell's row and column the fill completed.
    /// <para>
    /// Deliberately not a <see cref="PowerUpClearResolver"/> method: those force-clear a region
    /// whether or not it is full, whereas a joker clears on exactly the condition a placement does —
    /// the line actually became full. It therefore reuses <see cref="Board.IsRowFull"/>,
    /// <see cref="Board.IsColumnFull"/> and <see cref="LineClearResolver"/>'s own line-clearing
    /// primitives rather than carrying a second definition of either.
    /// </para>
    /// <para>
    /// Only the filled cell's own row and column are examined. No other line's fullness can have
    /// changed, so the full-board sweep <see cref="LineClearResolver.ResolveClears"/> performs would
    /// be waste here.
    /// </para>
    /// </summary>
    public static class JokerFillResolver
    {
        /// <summary>
        /// Occupies <paramref name="target"/> with <paramref name="colourId"/> and clears its row
        /// and/or column if that fill completed them. An off-board or already-occupied target is
        /// rejected without touching <paramref name="board"/> — the caller is expected to treat that
        /// as "the player aimed at nothing" and charge for nothing.
        /// </summary>
        public static JokerFillResult ResolveFill(Board board, GridPosition target, int colourId)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            // Occupancy is the legality rule, and it lives here rather than in the caller so the check
            // and the fill cannot drift apart: whatever decides a target is legal is what fills it.
            if (!Board.IsInside(target) || board.IsOccupied(target))
            {
                return JokerFillResult.Rejected(target);
            }

            board.Occupy(target, colourId);

            var clearedRows = new List<int>(1);
            var clearedColumns = new List<int>(1);

            if (board.IsRowFull(target.Y))
            {
                clearedRows.Add(target.Y);
            }

            if (board.IsColumnFull(target.X))
            {
                clearedColumns.Add(target.X);
            }

            // Collected before anything is cleared: afterwards an emptied cell is indistinguishable
            // from one that was already empty. Every cell of a full line is occupied by definition, so
            // no occupancy filter is needed — only the intersection needs skipping.
            var clearedCells = new List<GridPosition>(Board.SIZE * 2);

            if (clearedRows.Count > 0)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    clearedCells.Add(new GridPosition(x, target.Y));
                }
            }

            if (clearedColumns.Count > 0)
            {
                for (int y = 0; y < Board.SIZE; y++)
                {
                    // The intersection of the two cleared lines is the filled cell itself, and the row
                    // pass above already listed it.
                    if (clearedRows.Count > 0 && y == target.Y)
                    {
                        continue;
                    }

                    clearedCells.Add(new GridPosition(target.X, y));
                }
            }

            for (int i = 0; i < clearedRows.Count; i++)
            {
                LineClearResolver.ClearRow(board, clearedRows[i]);
            }

            for (int i = 0; i < clearedColumns.Count; i++)
            {
                LineClearResolver.ClearColumn(board, clearedColumns[i]);
            }

            return new JokerFillResult(true, target, clearedRows, clearedColumns, clearedCells);
        }
    }
}
