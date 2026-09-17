using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The damage gate every "collect the cells, read what was on them, then clear them" resolver runs
    /// its candidate list through — <see cref="LineClearResolver"/>, <see cref="PowerUpClearResolver"/>
    /// and <see cref="JokerFillResolver"/>.
    /// <para>
    /// <b>Why the two halves are separate calls.</b> Those resolvers must read each doomed cell's
    /// <see cref="SpecialCellKind"/> before it is cleared (<see cref="Board.Clear"/> resets the kind),
    /// and they must not read it for a cell that survived — a cell that was not destroyed owes no
    /// effect. So the pass is: <see cref="SpendHits"/> settles which cells are actually going
    /// (spending a hit on the ones that are not, there and then),
    /// <see cref="SpecialCellDetection.CollectTriggered"/> runs on exactly what is left, and
    /// <see cref="RemoveAll"/> finishes the job. The candidate list is the destroyed-cell list: it is
    /// compacted in place, so a surviving reinforced cell is invisible to the caller's score, count and
    /// trigger bookkeeping alike.
    /// </para>
    /// <para>
    /// Public rather than internal for exactly one caller outside this assembly:
    /// <c>BoardSystem.TryUseDemolitionHammer</c>, which destroys a single cell in the same
    /// collect-gate-detect-remove order and must not carry its own copy of the rule.
    /// </para>
    /// </summary>
    public static class ReinforcedCellDamage
    {
        /// <summary>
        /// Spends one hit on every candidate from <paramref name="startIndex"/> onwards that has more
        /// than one left, drops it from <paramref name="candidates"/>, and leaves the list holding
        /// exactly the cells <see cref="RemoveAll"/> will go on to remove.
        /// <para>
        /// Returns how many of the surviving entries were reinforced cells taking their last hit, as
        /// opposed to ordinary cells that were never reinforced — the count issue #154's
        /// "clear all reinforced cells" objective needs, and the reason this returns anything at all.
        /// </para>
        /// </summary>
        public static int SpendHits(Board board, List<GridPosition> candidates, int startIndex = 0)
        {
            int lastHitRemovalCount = 0;

            // Compacted in place rather than by building a second list: this runs once per clear over
            // at most a few dozen cells and allocates nothing, exactly as
            // LineClearResolver's own intersection de-duplication does.
            int writeIndex = startIndex;
            for (int readIndex = startIndex; readIndex < candidates.Count; readIndex++)
            {
                GridPosition position = candidates[readIndex];
                int hitCount = board.GetHitCount(position);

                if (hitCount > 1)
                {
                    // Spends the hit now, while the caller's "what did I destroy" list is still being
                    // settled — the cell stays occupied and is not a destroyed cell by any measure.
                    board.TryDamage(position);
                    continue;
                }

                if (hitCount == 1)
                {
                    lastHitRemovalCount++;
                }

                candidates[writeIndex] = position;
                writeIndex++;
            }

            candidates.RemoveRange(writeIndex, candidates.Count - writeIndex);
            return lastHitRemovalCount;
        }

        /// <summary>Removes every cell from <paramref name="startIndex"/> onwards. Each is known to have
        /// no hit left to absorb (<see cref="SpendHits"/> just established that), so every
        /// <see cref="Board.TryDamage"/> here removes.</summary>
        public static void RemoveAll(Board board, IReadOnlyList<GridPosition> cells, int startIndex = 0)
        {
            for (int i = startIndex; i < cells.Count; i++)
            {
                board.TryDamage(cells[i]);
            }
        }
    }
}
