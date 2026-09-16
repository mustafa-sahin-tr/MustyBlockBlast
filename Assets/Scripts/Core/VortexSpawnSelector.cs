using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Decides where a clutch clear's reward — a <see cref="SpecialCellKind.Vortex"/> — is spawned.
    /// Fully deterministic, with no probability roll and no randomness anywhere, not even as a
    /// tie-break: a clear made on a board that was more than
    /// <see cref="OCCUPANCY_THRESHOLD"/>-full always spawns exactly one vortex, on the same cell every
    /// time for the same clear.
    /// <para>
    /// <b>The occupancy is the pre-clear one</b>, measured immediately after the piece landed and
    /// before any line resolved — the same reading the "clutch recovery" objective already uses, passed
    /// in rather than recomputed here so the two can never drift apart. Measuring it after the clear
    /// would be measuring the board the reward is for rescuing the player <em>from</em>, which is by
    /// then exactly the board they are no longer on.
    /// </para>
    /// <para>
    /// <b>The cell is one the clear itself vacated</b>, mirroring <see cref="ExplosiveCoreSpawnSelector"/>'s
    /// "convert a cell the clear produced, in place" rule: a line only clears because every one of its
    /// cells was filled, so each is guaranteed to have just been emptied and the caller only has to
    /// occupy it. Lines are tried in the order they cleared and cells in ascending order along each, so
    /// the answer is a pure function of the clear.
    /// </para>
    /// <para>
    /// Pure geometry plus occupancy: it never mutates the board and never tags a cell itself, so the
    /// caller stays the one place a spawn is actually written.
    /// </para>
    /// </summary>
    public static class VortexSpawnSelector
    {
        /// <summary>
        /// How full the board must have been, immediately before the clear, for the clear to earn a
        /// vortex. Strictly greater than: a board exactly this full has not earned one, so the boundary
        /// belongs to the ordinary case and there is no "sometimes" band around it.
        /// <para>
        /// Measured against <see cref="Board.PlayableCellCount"/> rather than the bounding rectangle, so
        /// a shaped board's holes do not count towards a fullness they can never contribute to.
        /// </para>
        /// </summary>
        public const float OCCUPANCY_THRESHOLD = 0.8f;

        /// <summary>
        /// Where this clear should spawn its vortex, or null when it should spawn none.
        /// <para>
        /// Null in three ordinary, silent, non-error cases: the clear cleared nothing (the reward is for
        /// a clear, not for a crowded board); the board was not more than
        /// <see cref="OCCUPANCY_THRESHOLD"/>-full before it; or no cell of the cleared lines can carry
        /// the tile. The last is defensive on today's boards — a cleared cell is by construction
        /// playable, empty and carrying no kind — and exists so a future board shape or effect ordering
        /// that breaks that drops the reward quietly instead of throwing.
        /// </para>
        /// </summary>
        public static GridPosition? SelectSpawnPosition(
            Board board,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            int occupiedCellCountBeforeClear)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (!AnyCleared(clearedRows) && !AnyCleared(clearedColumns))
            {
                return null;
            }

            if (occupiedCellCountBeforeClear <= OCCUPANCY_THRESHOLD * board.PlayableCellCount)
            {
                return null;
            }

            GridPosition? inRows = SelectInRows(board, clearedRows);
            return inRows ?? SelectInColumns(board, clearedColumns);
        }

        private static bool AnyCleared(IReadOnlyList<int> lines) => lines != null && lines.Count > 0;

        /// <summary>The first eligible cell of the first cleared row that has one, scanning left to
        /// right. Rows before columns, and both in the order the clear reported them, so two clears that
        /// look the same to the player resolve to the same cell.</summary>
        private static GridPosition? SelectInRows(Board board, IReadOnlyList<int> clearedRows)
        {
            if (clearedRows == null)
            {
                return null;
            }

            for (int i = 0; i < clearedRows.Count; i++)
            {
                int y = clearedRows[i];
                for (int x = 0; x < board.Width; x++)
                {
                    var candidate = new GridPosition(x, y);
                    if (IsCandidate(board, candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>Counterpart to <see cref="SelectInRows"/>, scanning bottom to top.</summary>
        private static GridPosition? SelectInColumns(Board board, IReadOnlyList<int> clearedColumns)
        {
            if (clearedColumns == null)
            {
                return null;
            }

            for (int i = 0; i < clearedColumns.Count; i++)
            {
                int x = clearedColumns[i];
                for (int y = 0; y < board.Height; y++)
                {
                    var candidate = new GridPosition(x, y);
                    if (IsCandidate(board, candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>True when <paramref name="candidate"/> could carry the vortex: a real cell the clear
        /// emptied, still empty once everything has settled, and carrying no other kind — a cell that
        /// already is a core or a laser is left as the one it is rather than quietly overwritten.</summary>
        private static bool IsCandidate(Board board, GridPosition candidate)
            => board.IsPlayable(candidate)
                && !board.IsOccupied(candidate)
                && board.GetSpecialKind(candidate) == SpecialCellKind.None;
    }
}
