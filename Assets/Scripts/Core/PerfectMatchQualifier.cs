using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Decides whether one placement earned the "Perfect Match" bonus: a shaped, non-straight piece
    /// placed so that every cell it just filled is itself swept away by that same placement's own
    /// primary clear (issue #352).
    /// <para>
    /// Pure function of the piece, where it landed and what cleared — it never touches the board and
    /// never decides where the reward goes, mirroring how <see cref="ChainLightningSpawnSelector"/>'s
    /// shape check is separate from its cell selection. The Gameplay assembly's board system is the
    /// one place a qualifying placement is turned into a spawn.
    /// </para>
    /// </summary>
    public static class PerfectMatchQualifier
    {
        /// <summary>
        /// True when <paramref name="piece"/>, placed at <paramref name="anchor"/>, qualifies for the
        /// bonus given <paramref name="clearResult"/> — the placement's own (primary) clear, never a
        /// cascade phase.
        /// <para>
        /// Two conditions, both required: the piece's family
        /// (<see cref="PieceFamilyClassifier.Classify"/>) is neither <see cref="PieceFamily.Single"/>
        /// nor <see cref="PieceFamily.Line"/> — a single cell or a straight bar is not the "shaped"
        /// move this rewards — and every cell the piece occupied lies in a row or a column that is part
        /// of this clear, so the piece is consumed whole rather than leaving any of its own cells
        /// standing.
        /// </para>
        /// </summary>
        public static bool Qualifies(Piece piece, GridPosition anchor, LineClearResult clearResult)
        {
            if (piece == null)
            {
                return false;
            }

            PieceFamily family = PieceFamilyClassifier.Classify(piece.Id);
            if (family == PieceFamily.Single || family == PieceFamily.Line)
            {
                return false;
            }

            IReadOnlyList<GridPosition> offsets = piece.Offsets;
            for (int i = 0; i < offsets.Count; i++)
            {
                GridPosition cell = anchor + offsets[i];
                if (!CellIsCleared(cell, clearResult))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when <paramref name="cell"/>'s row or column is one this clear took out.</summary>
        private static bool CellIsCleared(GridPosition cell, LineClearResult clearResult)
        {
            return ContainsIndex(clearResult.ClearedRows, cell.Y)
                || ContainsIndex(clearResult.ClearedColumns, cell.X);
        }

        private static bool ContainsIndex(IReadOnlyList<int> indices, int value)
        {
            if (indices == null)
            {
                return false;
            }

            for (int i = 0; i < indices.Count; i++)
            {
                if (indices[i] == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
