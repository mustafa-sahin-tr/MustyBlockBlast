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

        private static readonly SpecialCellTrigger[] NoTriggers = new SpecialCellTrigger[0];

        internal JokerFillResult(
            bool filled,
            GridPosition position,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            IReadOnlyList<GridPosition> clearedCells,
            IReadOnlyList<SpecialCellTrigger> triggeredSpecials)
            : this(
                filled, position, clearedRows, clearedColumns, clearedCells, triggeredSpecials,
                reinforcedCellsFullyClearedCount: 0)
        {
        }

        internal JokerFillResult(
            bool filled,
            GridPosition position,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            IReadOnlyList<GridPosition> clearedCells,
            IReadOnlyList<SpecialCellTrigger> triggeredSpecials,
            int reinforcedCellsFullyClearedCount)
            : this(
                filled, position, clearedRows, clearedColumns, clearedCells, triggeredSpecials,
                reinforcedCellsFullyClearedCount, destroyedCellCountByColour: null)
        {
        }

        internal JokerFillResult(
            bool filled,
            GridPosition position,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            IReadOnlyList<GridPosition> clearedCells,
            IReadOnlyList<SpecialCellTrigger> triggeredSpecials,
            int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour)
        {
            Filled = filled;
            Position = position;
            ClearedRows = clearedRows;
            ClearedColumns = clearedColumns;
            ClearedCells = clearedCells;
            TriggeredSpecials = triggeredSpecials;
            ReinforcedCellsFullyClearedCount = reinforcedCellsFullyClearedCount;
            DestroyedCellCountByColour = destroyedCellCountByColour;
        }

        /// <summary>How many cells of each colour the completed lines destroyed, indexed by colour id —
        /// see <see cref="ColourTally"/>. The joker's own filled cell is in it too, under the colour it
        /// was filled with. Null for a rejected fill.</summary>
        public IReadOnlyList<int> DestroyedCellCountByColour { get; }

        /// <summary>Of <see cref="ClearedCells"/>, how many were reinforced cells taking their last
        /// hit. Data plumbing for issue #154, mirroring
        /// <see cref="LineClearResult.ReinforcedCellsFullyClearedCount"/>.</summary>
        public int ReinforcedCellsFullyClearedCount { get; }

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

        /// <summary>
        /// The special cells this fill's clear destroyed, found through the same
        /// <see cref="SpecialCellDetection"/> pass a placement's or a power-up's clear uses — a joker
        /// completing a line destroys a special cell exactly as any other clear does. Always empty
        /// while every kind is <see cref="SpecialCellKind.None"/>. Reported rather than applied here,
        /// same as <see cref="PowerUpClearResult.TriggeredSpecials"/>: whether a joker's triggers feed
        /// <see cref="CascadeClearResolver"/>'s loop is a decision for the first sub-issue that ships a
        /// real effect.
        /// </summary>
        public IReadOnlyList<SpecialCellTrigger> TriggeredSpecials { get; }

        /// <summary>A target that could not be filled: nothing was read, written or cleared.</summary>
        internal static JokerFillResult Rejected(GridPosition position)
            => new JokerFillResult(false, position, NoLines, NoLines, NoCells, NoTriggers);
    }

    /// <summary>
    /// Fills one empty cell and clears whichever of that cell's row and column the fill completed.
    /// <para>
    /// Deliberately not a <see cref="PowerUpClearResolver"/> method: those force-clear a region
    /// whether or not it is full, whereas a joker clears on exactly the condition a placement does —
    /// the line actually became full. It therefore reuses <see cref="Board.IsRowFull"/>,
    /// <see cref="Board.IsColumnFull"/> and the same <see cref="ReinforcedCellDamage"/> gate a
    /// placement's clear removes its cells through, rather than carrying a second definition of either.
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
            // A hole is refused exactly as an off-board target is: a joker fills a cell a block could
            // have been placed on, and nothing can ever stand on a hole.
            if (!board.IsPlayable(target) || board.IsOccupied(target))
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
            var clearedCells = new List<GridPosition>(board.Width + board.Height);

            if (clearedRows.Count > 0)
            {
                // Through the board's own line geometry, so holes are skipped in exactly one place.
                board.CollectRowCells(target.Y, clearedCells);
            }

            if (clearedColumns.Count > 0)
            {
                int firstColumnCell = clearedCells.Count;
                board.CollectColumnCells(target.X, clearedCells);

                // The intersection of the two cleared lines is the filled cell itself, and the row
                // pass above already listed it.
                if (clearedRows.Count > 0)
                {
                    for (int i = clearedCells.Count - 1; i >= firstColumnCell; i--)
                    {
                        if (clearedCells[i].Y == target.Y)
                        {
                            clearedCells.RemoveAt(i);
                        }
                    }
                }
            }

            // The damage gate, before detection and before anything is removed — exactly the order
            // LineClearResolver uses, so a joker's completed line treats a reinforced cell the same way
            // a placement's does: it spends one hit, stays standing, and is dropped from the list of
            // cells this fill destroyed.
            int reinforcedCellsFullyClearedCount = ReinforcedCellDamage.SpendHits(board, clearedCells);

            // Post-gate and pre-removal, as LineClearResolver takes its own tally.
            int[] destroyedCellCountByColour = ColourTally.Count(board, clearedCells);

            // Collected before anything is cleared, same as clearedCells above: once a cell is cleared
            // its special kind is reset (Board.Clear), so detection has to read it first.
            var triggeredSpecials = new List<SpecialCellTrigger>();
            SpecialCellDetection.CollectTriggered(
                board, clearedCells, triggeredSpecials, clearedRows, clearedColumns);

            // The listed cells are every playable cell of the completed lines, each once, so removing
            // them one by one is exactly what clearing those lines means — and it is the one place the
            // removal can honour the damage gate above.
            ReinforcedCellDamage.RemoveAll(board, clearedCells);

            return new JokerFillResult(
                true, target, clearedRows, clearedColumns, clearedCells, triggeredSpecials,
                reinforcedCellsFullyClearedCount, destroyedCellCountByColour);
        }
    }
}
