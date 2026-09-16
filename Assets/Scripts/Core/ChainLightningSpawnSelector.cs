using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Decides where a big-piece clear's reward — a <see cref="SpecialCellKind.ChainLightning"/> — is
    /// spawned. Fully deterministic, with no probability roll and no randomness anywhere, not even as a
    /// tie-break: a placement of one of the three qualifying shapes that also clears a line always
    /// spawns exactly one tile, on the same cell every time for the same clear. (The randomness this
    /// feature does have lives in <see cref="ChainLightningEffect"/>, which picks the cells a destroyed
    /// tile vaporizes — never in whether one appears.)
    /// <para>
    /// <b>The qualifying shapes are named by piece id</b>, not by cell count or bounding box: the
    /// reward is for landing one of the two awkward big pieces — the 3x3 square and the 1x5 bar, in
    /// either orientation — <em>and</em> clearing with it, and every other five-cell or nine-cell shape
    /// in the catalog is a different, easier ask. Ids are the catalog's own stable names
    /// (<see cref="PieceCatalog"/>), so a shape that is not one of the three is simply not this reward.
    /// </para>
    /// <para>
    /// <b>The cell is one the clear itself vacated</b>, exactly as <see cref="VortexSpawnSelector"/>'s
    /// is: a line only clears because every one of its cells was filled, so each is guaranteed to have
    /// just been emptied and the caller only has to occupy it. Lines are tried in the order they
    /// cleared and cells in ascending order along each, so the answer is a pure function of the clear.
    /// </para>
    /// <para>
    /// Pure geometry plus occupancy: it never mutates the board and never tags a cell itself, so the
    /// caller stays the one place a spawn is actually written.
    /// </para>
    /// </summary>
    public static class ChainLightningSpawnSelector
    {
        /// <summary>The 3x3 square. Named as a literal rather than derived from a piece's cell count,
        /// because "nine cells" is a property several future shapes could share and this reward is
        /// scoped to this one.</summary>
        private const string SQUARE_3X3_ID = "square_3x3";

        /// <summary>The two orientations of the 1x5 bar. Both qualify: the catalog stores every
        /// orientation as its own entry (pieces are never rotated at runtime), so a rule that named only
        /// one of them would reward the same move half the time depending on which way the bar was
        /// drawn.</summary>
        private const string LINE_H5_ID = "line_h5";
        private const string LINE_V5_ID = "line_v5";

        /// <summary>
        /// Where this placement should spawn its chain lightning tile, or null when it should spawn
        /// none.
        /// <para>
        /// Null in three ordinary, silent, non-error cases (AC4): the placed piece is not one of the
        /// three qualifying shapes; the placement cleared no line (the reward is for doing both at
        /// once); or no cell of the cleared lines can carry the tile. The last is defensive on today's
        /// boards — a cleared cell is by construction playable, empty and carrying no kind — and exists
        /// so a future board shape or effect ordering that breaks that drops the reward quietly instead
        /// of throwing.
        /// </para>
        /// </summary>
        public static GridPosition? SelectSpawnPosition(
            Board board,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            string pieceId)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (!Qualifies(pieceId))
            {
                return null;
            }

            if (!AnyCleared(clearedRows) && !AnyCleared(clearedColumns))
            {
                return null;
            }

            GridPosition? inRows = SelectInRows(board, clearedRows);
            return inRows ?? SelectInColumns(board, clearedColumns);
        }

        /// <summary>True when <paramref name="pieceId"/> names one of the three shapes this reward is
        /// for. Ordinal comparison: these are catalog ids, not text a player ever sees, so culture must
        /// play no part in whether a reward fires.</summary>
        private static bool Qualifies(string pieceId)
            => string.Equals(pieceId, SQUARE_3X3_ID, StringComparison.Ordinal)
                || string.Equals(pieceId, LINE_H5_ID, StringComparison.Ordinal)
                || string.Equals(pieceId, LINE_V5_ID, StringComparison.Ordinal);

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

        /// <summary>True when <paramref name="candidate"/> could carry the tile: a real cell the clear
        /// emptied, still empty once everything has settled, and carrying no other kind — a cell that
        /// already is a core, a laser or a vortex is left as the one it is rather than quietly
        /// overwritten.</summary>
        private static bool IsCandidate(Board board, GridPosition candidate)
            => board.IsPlayable(candidate)
                && !board.IsOccupied(candidate)
                && board.GetSpecialKind(candidate) == SpecialCellKind.None;
    }
}
