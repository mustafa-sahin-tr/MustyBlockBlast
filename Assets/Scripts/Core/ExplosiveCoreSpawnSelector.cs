using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Decides where one placement's reward — a <see cref="SpecialCellKind.ExplosiveCore"/> — is
    /// spawned. Deterministic in the only part that matters: a placement that cleared at least one row
    /// AND at least one column always spawns exactly one core, with no probability roll anywhere.
    /// <para>
    /// Pure geometry plus occupancy: it never mutates the board and never tags a cell itself, so the
    /// caller stays the one place a spawn is actually written.
    /// </para>
    /// </summary>
    public static class ExplosiveCoreSpawnSelector
    {
        /// <summary>Cells reached in each direction when falling back to a neighbour — the same
        /// Chebyshev-1 ring the blast footprint covers.</summary>
        private const int NEIGHBOUR_RADIUS = 1;

        /// <summary>
        /// Where this clear should spawn its explosive core, or null when it should spawn none.
        /// <para>
        /// Null whenever the clear was rows-only or columns-only: the reward is specifically for
        /// closing a row and a column at once, so "no intersection exists" is the ordinary, silent,
        /// non-error outcome — not a failure to report.
        /// </para>
        /// <para>
        /// The intersection of the first cleared column and the first cleared row is the spawn cell.
        /// A row or column can only clear because every one of its cells was occupied, so that
        /// intersection was necessarily occupied before the clear and is necessarily empty after it —
        /// which is why the ordinary path lands on an empty cell and the caller only has to occupy it.
        /// </para>
        /// <para>
        /// The neighbour fallback below cannot be reached on today's full 8x8 grid for exactly that
        /// reason. It exists so the rule survives a board shape where the intersection is not part of
        /// the clear (a hole, a masked cell): rather than silently dropping the reward, the core is
        /// placed on an occupied neighbour, converting that block into a core in place. Which
        /// neighbour is the only random decision in this feature, and only ever a tie-break.
        /// </para>
        /// </summary>
        public static GridPosition? SelectSpawnPosition(
            Board board,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            Random random)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (clearedRows == null || clearedColumns == null
                || clearedRows.Count == 0 || clearedColumns.Count == 0)
            {
                return null;
            }

            var intersection = new GridPosition(clearedColumns[0], clearedRows[0]);
            if (!Board.IsInside(intersection))
            {
                return null;
            }

            if (!board.IsOccupied(intersection))
            {
                return intersection;
            }

            return SelectOccupiedNeighbour(board, intersection, random);
        }

        /// <summary>
        /// One occupied cell within <see cref="NEIGHBOUR_RADIUS"/> of <paramref name="center"/>, chosen
        /// uniformly, or null when there is none — in which case the reward is silently skipped.
        /// <para>
        /// Counts the candidates and then walks to the chosen one, rather than collecting them into a
        /// list: nine cells are cheap to visit twice and it keeps the selector allocation-free.
        /// </para>
        /// </summary>
        private static GridPosition? SelectOccupiedNeighbour(Board board, GridPosition center, Random random)
        {
            int candidateCount = CountOccupiedNeighbours(board, center);
            if (candidateCount == 0)
            {
                return null;
            }

            int chosen = random.Next(candidateCount);
            int seen = 0;

            for (int y = center.Y - NEIGHBOUR_RADIUS; y <= center.Y + NEIGHBOUR_RADIUS; y++)
            {
                for (int x = center.X - NEIGHBOUR_RADIUS; x <= center.X + NEIGHBOUR_RADIUS; x++)
                {
                    var candidate = new GridPosition(x, y);
                    if (!IsCandidate(board, center, candidate))
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

        private static int CountOccupiedNeighbours(Board board, GridPosition center)
        {
            int count = 0;

            for (int y = center.Y - NEIGHBOUR_RADIUS; y <= center.Y + NEIGHBOUR_RADIUS; y++)
            {
                for (int x = center.X - NEIGHBOUR_RADIUS; x <= center.X + NEIGHBOUR_RADIUS; x++)
                {
                    if (IsCandidate(board, center, new GridPosition(x, y)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>True when <paramref name="candidate"/> is a neighbour that could carry the core:
        /// on the board, not the centre itself, and currently holding a block.</summary>
        private static bool IsCandidate(Board board, GridPosition center, GridPosition candidate)
        {
            if (candidate.X == center.X && candidate.Y == center.Y)
            {
                return false;
            }

            return Board.IsInside(candidate) && board.IsOccupied(candidate);
        }
    }
}
