using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Decides where a run's Nth-line-clear reward — a <see cref="SpecialCellKind.Vortex"/> — is
    /// spawned, once <c>VortexProgressModel</c> has decided the placement earned one. Fully
    /// deterministic, with no probability roll and no randomness anywhere, not even as a tie-break: the
    /// cell is one the clear itself vacated, mirroring <see cref="ExplosiveCoreSpawnSelector"/>'s
    /// "convert a cell the clear produced, in place" rule — a line only clears because every one of its
    /// cells was filled, so each is guaranteed to have just been emptied and the caller only has to
    /// occupy it. Lines are tried in the order they cleared and cells in ascending order along each, so
    /// the answer is a pure function of the clear.
    /// <para>
    /// Pure geometry: it never mutates the board and never tags a cell itself, so the caller stays the
    /// one place a spawn is actually written. Whether this clear earned a vortex at all is
    /// <c>VortexProgressModel</c>'s decision, not this selector's — it only answers "where", never
    /// "whether".
    /// </para>
    /// </summary>
    public static class VortexSpawnSelector
    {
        /// <summary>
        /// Where this clear should place its vortex, or null when it should place none.
        /// <para>
        /// Null in two ordinary, silent, non-error cases: the clear cleared nothing (there is no cleared
        /// cell to convert), or no cell of the cleared lines can carry the tile. The latter is defensive
        /// on today's boards — a cleared cell is by construction playable, empty and carrying no kind —
        /// and exists so a future board shape or effect ordering that breaks that drops the reward
        /// quietly instead of throwing.
        /// </para>
        /// </summary>
        public static GridPosition? SelectSpawnPosition(
            Board board,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (!AnyCleared(clearedRows) && !AnyCleared(clearedColumns))
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
        /// emptied, still empty once everything has settled, carrying no other kind, and not where a
        /// reinforced block or a lock broke during this same clear (issue #441) — see
        /// <see cref="SpecialCellSpawnEligibility"/>.</summary>
        private static bool IsCandidate(Board board, GridPosition candidate)
            => SpecialCellSpawnEligibility.CanOccupy(board, candidate);
    }
}
