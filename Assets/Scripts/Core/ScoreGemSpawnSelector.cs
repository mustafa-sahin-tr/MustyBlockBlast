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
    /// there is no geometry to respect and every occupied cell is equally eligible.
    /// </para>
    /// <para>
    /// Pure occupancy: it never mutates the board and never tags a cell itself, so the caller stays the
    /// one place a spawn is actually written.
    /// </para>
    /// </summary>
    public static class ScoreGemSpawnSelector
    {
        /// <summary>
        /// One occupied cell chosen uniformly, or null when the board holds no block at all — in which
        /// case the reward is silently skipped, which is an ordinary outcome and not an error state.
        /// A gem has to sit on a block: it is a property of a block, and a board with none has nothing
        /// to convert.
        /// <para>
        /// Counts the candidates and then walks to the chosen one, rather than collecting them into a
        /// list: the board is 64 cells, cheap to visit twice, and it keeps the selector allocation-free.
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

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
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
