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
        /// One plain occupied cell chosen uniformly, or null when the board holds none — in which
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
        /// A cell that already carries a kind, or is a reinforced cell, is never a candidate (issue #441,
        /// product decision): overwriting it would delete a lock, a timer or another reward outright, and
        /// no special cell may ever stack on another. See <see cref="SpecialCellSpawnEligibility"/>.
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

            int candidateCount = CountCandidates(board);
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
                    if (!SpecialCellSpawnEligibility.CanConvert(board, candidate))
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

        private static int CountCandidates(Board board)
        {
            int count = 0;

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    if (SpecialCellSpawnEligibility.CanConvert(board, new GridPosition(x, y)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
