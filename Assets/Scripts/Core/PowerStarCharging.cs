using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The line-clear half of the <see cref="SpecialCellKind.PowerStar"/> rule (issue #482): a completed
    /// row or column does not remove a power star, it charges it — one per cleared line through it, two
    /// where a cleared row and a cleared column cross on it. Run on a line clear's candidate cells before
    /// the damage gate, exactly where <see cref="ReinforcedCellDamage.SpendHits"/> lets a reinforced cell
    /// absorb its hit: a star still short of <see cref="Board.POWER_STAR_BURST_CHARGE"/> is dropped from
    /// the list — it stays standing and counts as destroyed by no measure — and one this clear brings to
    /// it is kept, so it is removed and triggers its burst like any destroyed special cell.
    /// </summary>
    public static class PowerStarCharging
    {
        /// <summary>
        /// Charges every power star in <paramref name="candidates"/> by the number of
        /// <paramref name="clearedRows"/>/<paramref name="clearedColumns"/> passing through it, and removes
        /// from the list every star that stays standing. Compacts in place and allocates nothing.
        /// </summary>
        public static void ChargeAndKeepStanding(
            Board board,
            List<GridPosition> candidates,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns)
        {
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < candidates.Count; readIndex++)
            {
                GridPosition position = candidates[readIndex];
                if (board.GetSpecialKind(position) == SpecialCellKind.PowerStar)
                {
                    int lines = (Contains(clearedRows, position.Y) ? 1 : 0)
                        + (Contains(clearedColumns, position.X) ? 1 : 0);
                    if (!board.ChargePowerStar(position, lines))
                    {
                        continue;
                    }
                }

                candidates[writeIndex] = position;
                writeIndex++;
            }

            candidates.RemoveRange(writeIndex, candidates.Count - writeIndex);
        }

        private static bool Contains(IReadOnlyList<int> values, int value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
