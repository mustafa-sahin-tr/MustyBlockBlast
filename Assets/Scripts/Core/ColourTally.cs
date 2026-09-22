using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Per-colour cell counts, indexed by colour id: index 0 is <see cref="Board.EMPTY"/> and stays
    /// zero, indexes 1..<see cref="Board.COLOUR_COUNT"/> are the palette. The one shape every "what did
    /// this clear destroy, by colour" report uses — a completed line's, a cascade's, a power-up's —
    /// so a colour-count objective reads them all the same way (issue #147).
    /// <para>
    /// Built from cells that are <em>about</em> to be removed, so it has to be taken before
    /// <see cref="Board.Clear"/> wipes the colour, exactly as the monochrome count is.
    /// </para>
    /// </summary>
    public static class ColourTally
    {
        /// <summary>Length of a tally array: one slot per colour id plus the unused zero slot.</summary>
        public const int LENGTH = Board.COLOUR_COUNT + 1;

        /// <summary>Counts the colour of every cell in <paramref name="cells"/> as the board holds it
        /// right now. Empty cells and ids outside the palette are ignored rather than thrown on: a
        /// stale or hand-built list must never fail a resolve over a cell it cannot classify.</summary>
        public static int[] Count(Board board, IReadOnlyList<GridPosition> cells)
        {
            var tally = new int[LENGTH];
            Add(board, cells, tally);
            return tally;
        }

        /// <summary>Adds every cell of <paramref name="cells"/> into an existing <paramref name="tally"/>.</summary>
        public static void Add(Board board, IReadOnlyList<GridPosition> cells, int[] tally)
        {
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                int colourId = board[cells[cellIndex]];
                if (colourId >= 1 && colourId < LENGTH)
                {
                    tally[colourId]++;
                }
            }
        }

        /// <summary>Counts one cell of <paramref name="colourId"/> into <paramref name="tally"/>. An id
        /// outside the palette is ignored rather than thrown on, for the reason <see cref="Add(Board, IReadOnlyList{GridPosition}, int[])"/>
        /// ignores one: a tally must never fail a resolve over a value it cannot classify. Shared by
        /// every effect that counts <see cref="SpecialCellKind.Diamond"/> cells by their own colour
        /// (<see cref="DiamondClearEffect"/> and the blast/wipe/strike effects) so "which slot does this
        /// colour go in" has one definition.</summary>
        public static void Increment(int[] tally, int colourId)
        {
            if (colourId >= 1 && colourId < LENGTH)
            {
                tally[colourId]++;
            }
        }

        /// <summary>Sums <paramref name="other"/> into <paramref name="tally"/>. A null source adds nothing.</summary>
        public static void Add(IReadOnlyList<int> other, int[] tally)
        {
            if (other == null)
            {
                return;
            }

            int count = other.Count < tally.Length ? other.Count : tally.Length;
            for (int colourId = 0; colourId < count; colourId++)
            {
                tally[colourId] += other[colourId];
            }
        }

        /// <summary>Reads one colour's count, treating a null tally or an id outside it as zero.</summary>
        public static int CountOf(IReadOnlyList<int> tally, int colourId)
        {
            if (tally == null || colourId < 0 || colourId >= tally.Count)
            {
                return 0;
            }

            return tally[colourId];
        }
    }
}
