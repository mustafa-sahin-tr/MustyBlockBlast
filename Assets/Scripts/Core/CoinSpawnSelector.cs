using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Decides where a <see cref="SpecialCellKind.Coin"/> is spawned. Deterministic in the part that
    /// matters: whatever earned the coin cell always converts a cell, with no probability roll deciding
    /// whether it appears at all. Only <em>which</em> cell is random.
    /// <para>
    /// A plain static function with no state and no caller baked into it, deliberately. A coin cell has
    /// three separate sources — a level's authored count at run start, a combo-streak trigger, and the
    /// "Coin Sower" power-up still to come — and all three ask the same question of the same board, so
    /// the question is answered here once rather than re-implemented inside whichever System happens to
    /// be asking. Adding the power-up means calling this; it means nothing else.
    /// </para>
    /// <para>
    /// Pure occupancy: it never mutates the board and never tags a cell itself, so the caller stays the
    /// one place a spawn is actually written.
    /// </para>
    /// </summary>
    public static class CoinSpawnSelector
    {
        /// <summary>
        /// One occupied cell chosen uniformly, or null when the board holds no block at all — in which
        /// case the spawn is silently skipped, which is an ordinary outcome and not an error state. A
        /// coin has to sit on a block: like every other kind it is a property of a block rather than of
        /// an empty cell, so a board with none has nothing to convert.
        /// <para>
        /// Counts the candidates and then walks to the chosen one, rather than collecting them into a
        /// list: the board is 64 cells, cheap to visit twice, and it keeps the selector allocation-free —
        /// which matters more here than for the other selectors, because the Coin Sower will call it
        /// several times in one go.
        /// </para>
        /// <para>
        /// A cell that already carries a kind is a legitimate candidate and is simply overwritten —
        /// "already special" is not a reason to drop a reward the player earned, and the alternative
        /// (skipping such cells) would make the reward quietly less likely the more special cells are
        /// on the board.
        /// </para>
        /// </summary>
        public static GridPosition? SelectSpawnPosition(Board board, Random random)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            int candidateCount = board.OccupiedCellCount();
            if (candidateCount == 0)
            {
                return null;
            }

            int chosen = random.Next(candidateCount);
            int seen = 0;

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var candidate = new GridPosition(x, y);
                    if (!board.IsOccupied(candidate))
                    {
                        continue;
                    }

                    if (seen == chosen)
                    {
                        return candidate;
                    }

                    seen++;
                }
            }

            return null;
        }
    }
}
