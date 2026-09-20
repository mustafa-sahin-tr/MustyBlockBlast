using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Decides where a qualifying "Perfect Match" placement's reward — a
    /// <see cref="SpecialCellKind.ExplosiveCore"/> — is spawned (issue #352). Deterministic in the part
    /// that matters: whether <see cref="PerfectMatchQualifier"/> says a placement earned one is decided
    /// entirely before this runs. Only <em>which</em> cell is random.
    /// <para>
    /// Shaped like <see cref="ScoreGemSpawnSelector"/>, and for the same reason: the trigger is a fact
    /// about the piece and the clear, not a shape on the board, so there is no geometry to respect and
    /// every currently-occupied, not-yet-special cell is equally eligible. Unlike that selector, a cell
    /// already carrying a kind is excluded rather than overwritten — this reward can stack with the
    /// same placement's other spawns, and the fixed spawn order elsewhere is what keeps two of them off
    /// the same cell.
    /// </para>
    /// <para>
    /// Pure occupancy: it never mutates the board and never tags a cell itself, so the caller stays the
    /// one place a spawn is actually written.
    /// </para>
    /// </summary>
    public static class PerfectMatchSpawnSelector
    {
        /// <summary>
        /// One occupied, not-yet-special cell chosen uniformly, or null when the board holds none — in
        /// which case the reward is silently skipped, which is an ordinary outcome and not an error
        /// state (AC3).
        /// <para>
        /// Counts the candidates and then walks to the chosen one, rather than collecting them into a
        /// list: the board is a few dozen cells, cheap to visit twice, and it keeps the selector
        /// allocation-free.
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
                    if (!IsCandidate(board, candidate))
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
                    if (IsCandidate(board, new GridPosition(x, y)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>True when <paramref name="candidate"/> could carry the core: occupied and carrying
        /// no kind of its own yet.</summary>
        private static bool IsCandidate(Board board, GridPosition candidate)
            => board.IsOccupied(candidate) && board.GetSpecialKind(candidate) == SpecialCellKind.None;
    }
}
