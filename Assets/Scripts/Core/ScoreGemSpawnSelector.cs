using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Decides where a perfect round's reward — a <see cref="SpecialCellKind.ScoreGem"/> — is spawned.
    /// Deterministic in the part that matters: completing the round always converts a cell, with no
    /// probability roll deciding whether the reward appears at all. Only <em>which</em> cell is random.
    /// <para>
    /// Shaped exactly like <see cref="LaserSpawnSelector"/>, and for the same reason: the trigger is a
    /// history (every piece of one dock cycle cleared a line) rather than a shape on the board, so
    /// there is no geometry to respect and every plain occupied cell is equally eligible.
    /// </para>
    /// <para>
    /// Pure occupancy: it never mutates the board and never tags a cell itself, so the caller stays the
    /// one place a spawn is actually written.
    /// </para>
    /// </summary>
    public static class ScoreGemSpawnSelector
    {
        /// <summary>
        /// One plain occupied cell chosen uniformly, or null when the board holds none — in which
        /// case the reward is silently skipped, which is an ordinary outcome and not an error state.
        /// A gem has to sit on a block: it is a property of a block, and a board with none has nothing
        /// to convert.
        /// <para>
        /// Counts the candidates and then walks to the chosen one, rather than collecting them into a
        /// list: the board is 64 cells, cheap to visit twice, and it keeps the selector allocation-free.
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
