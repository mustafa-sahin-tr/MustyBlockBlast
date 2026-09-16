using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>Result of resolving line clears for one placement. Rows and columns are cleared
    /// simultaneously; a cell at their intersection is counted once.</summary>
    public readonly struct LineClearResult
    {
        public LineClearResult(
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            int clearedCellCount,
            int monochromeLineCount)
        {
            ClearedRows = clearedRows;
            ClearedColumns = clearedColumns;
            ClearedCellCount = clearedCellCount;
            MonochromeLineCount = monochromeLineCount;
        }

        public IReadOnlyList<int> ClearedRows { get; }

        public IReadOnlyList<int> ClearedColumns { get; }

        /// <summary>Total distinct cells emptied, counting each row/column intersection once.</summary>
        public int ClearedCellCount { get; }

        /// <summary>How many of the cleared lines consisted entirely of one colour. Each row/column is
        /// evaluated independently, so a shared intersection cell never forces the two lines to match.</summary>
        public int MonochromeLineCount { get; }

        /// <summary>Simultaneous lines cleared — the "lines" term used by <see cref="ScoreRules"/>.</summary>
        public int LineCount => ClearedRows.Count + ClearedColumns.Count;

        public bool AnyCleared => LineCount > 0;
    }

    /// <summary>Finds and clears every full row/column on the board. Must be run once, after the
    /// whole piece has been placed — never mid-placement.</summary>
    public static class LineClearResolver
    {
        private const int PreviewColourId = 1; // Arbitrary non-EMPTY id; colour is cosmetic and never affects placement/clearing.

        /// <summary>Non-mutating: what would clear if <paramref name="piece"/> were placed at
        /// <paramref name="anchor"/> on <paramref name="board"/>, without touching <paramref name="board"/>.
        /// Uses <paramref name="scratchBoard"/> as scratch space (its contents are overwritten) and
        /// <paramref name="resultRows"/>/<paramref name="resultColumns"/> as the result buffers (cleared then
        /// repopulated) so this can be called every drag-update frame with zero allocation.</summary>
        public static LineClearResult PreviewClears(
            Board board,
            Piece piece,
            GridPosition anchor,
            Board scratchBoard,
            List<int> resultRows,
            List<int> resultColumns)
        {
            resultRows.Clear();
            resultColumns.Clear();

            if (!PlacementRules.CanPlace(board, piece, anchor))
            {
                return new LineClearResult(resultRows, resultColumns, 0, 0);
            }

            scratchBoard.CopyFrom(board);
            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                scratchBoard.Occupy(anchor + piece.Offsets[i], PreviewColourId);
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                if (scratchBoard.IsRowFull(y))
                {
                    resultRows.Add(y);
                }
            }

            for (int x = 0; x < Board.SIZE; x++)
            {
                if (scratchBoard.IsColumnFull(x))
                {
                    resultColumns.Add(x);
                }
            }

            int clearedCellCount = (resultRows.Count * Board.SIZE)
                + (resultColumns.Count * Board.SIZE)
                - (resultRows.Count * resultColumns.Count);

            // Always 0: the previewed piece is stamped with the placeholder PreviewColourId rather than its
            // real colour, so per-line colour uniformity cannot be judged here. No UI surfaces it yet.
            return new LineClearResult(resultRows, resultColumns, clearedCellCount, 0);
        }

        /// <summary>Clears every currently-full row and column, once. Deliberately single-pass: it
        /// resolves the board exactly as it stands and never chains. Chaining is
        /// <see cref="CascadeClearResolver"/>'s job, which layers on top of this and is used only for a
        /// real placement — a drag preview must keep seeing "what is full right now", nothing more.</summary>
        public static LineClearResult ResolveClears(Board board)
            => ResolveClears(board, null, null);

        /// <summary>
        /// As <see cref="ResolveClears(Board)"/>, additionally reporting what this pass destroyed.
        /// Both buffers are optional (pass null to skip that bookkeeping) and are cleared before use.
        /// <para>
        /// <paramref name="destroyedCells"/> receives each distinct emptied cell exactly once — a
        /// cleared row/column intersection is listed by the row pass only, matching the
        /// "count intersections once" rule <see cref="LineClearResult.ClearedCellCount"/> uses.
        /// <paramref name="triggeredSpecials"/> receives the special cells among them, collected
        /// through <see cref="SpecialCellDetection"/> before anything is cleared — the one moment the
        /// kinds are still readable.
        /// </para>
        /// <para>
        /// Internal because only <see cref="CascadeClearResolver"/> needs it: it exists so the cascade
        /// does not re-implement "find the full lines and clear them", which is the one behaviour AC3
        /// requires to stay bit-identical.
        /// </para>
        /// </summary>
        internal static LineClearResult ResolveClears(
            Board board, List<GridPosition> destroyedCells, List<SpecialCellTrigger> triggeredSpecials)
        {
            // Validated up front, before anything is read, written or handed back: detection reads the
            // destroyed-cell list, so the two buffers are supplied together or not at all rather than
            // this method quietly allocating a hidden one. Checking here rather than at the point of
            // use means a caller that got the pairing wrong never sees a half-processed buffer.
            if (triggeredSpecials != null && destroyedCells == null)
            {
                throw new ArgumentException(
                    "A destroyed-cell buffer is required to collect triggered specials.",
                    nameof(destroyedCells));
            }

            var clearedRows = new List<int>();
            var clearedColumns = new List<int>();

            for (int y = 0; y < Board.SIZE; y++)
            {
                if (board.IsRowFull(y))
                {
                    clearedRows.Add(y);
                }
            }

            for (int x = 0; x < Board.SIZE; x++)
            {
                if (board.IsColumnFull(x))
                {
                    clearedColumns.Add(x);
                }
            }

            int clearedCellCount = (clearedRows.Count * Board.SIZE)
                + (clearedColumns.Count * Board.SIZE)
                - (clearedRows.Count * clearedColumns.Count);

            // Must run before any clearing — once cleared, the colour data is gone.
            int monochromeLineCount = 0;
            for (int i = 0; i < clearedRows.Count; i++)
            {
                if (IsRowMonochrome(board, clearedRows[i]))
                {
                    monochromeLineCount++;
                }
            }

            for (int i = 0; i < clearedColumns.Count; i++)
            {
                if (IsColumnMonochrome(board, clearedColumns[i]))
                {
                    monochromeLineCount++;
                }
            }

            // Both must also run before any clearing: the cell list is built from the lines as they
            // still stand, and a cell's special kind is wiped by Board.Clear along with its colour.
            if (destroyedCells != null)
            {
                CollectDestroyedCells(board, clearedRows, clearedColumns, destroyedCells);
            }

            if (triggeredSpecials != null)
            {
                triggeredSpecials.Clear();

                // The cleared lines are passed along so each trigger records which of them destroyed
                // it: an effect that fires relative to that line (a laser) needs to know, and this is
                // the only point where "this cell went with that row/column" is still known.
                SpecialCellDetection.CollectTriggered(
                    board, destroyedCells, triggeredSpecials, clearedRows, clearedColumns);
            }

            for (int i = 0; i < clearedRows.Count; i++)
            {
                ClearRow(board, clearedRows[i]);
            }

            for (int i = 0; i < clearedColumns.Count; i++)
            {
                ClearColumn(board, clearedColumns[i]);
            }

            return new LineClearResult(clearedRows, clearedColumns, clearedCellCount, monochromeLineCount);
        }

        /// <summary>Fills <paramref name="results"/> with every distinct cell the given lines cover.
        /// Column cells sitting in an already-listed row are skipped, so an intersection appears once.
        /// The cells of a line come from the board itself rather than a <see cref="Board.SIZE"/> loop
        /// here, so a board with a different shape has one place to change.</summary>
        private static void CollectDestroyedCells(
            Board board,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            List<GridPosition> results)
        {
            results.Clear();

            for (int i = 0; i < clearedRows.Count; i++)
            {
                board.CollectRowCells(clearedRows[i], results);
            }

            int cellCountFromRows = results.Count;

            for (int i = 0; i < clearedColumns.Count; i++)
            {
                board.CollectColumnCells(clearedColumns[i], results);
            }

            // Walk only the cells the column pass just appended and drop the ones a cleared row
            // already contributed. Compacting in place beats a HashSet: this runs once per placement
            // over at most a few dozen cells, and allocates nothing.
            int writeIndex = cellCountFromRows;
            for (int readIndex = cellCountFromRows; readIndex < results.Count; readIndex++)
            {
                if (ContainsIndex(clearedRows, results[readIndex].Y))
                {
                    continue;
                }

                results[writeIndex] = results[readIndex];
                writeIndex++;
            }

            results.RemoveRange(writeIndex, results.Count - writeIndex);
        }

        private static bool ContainsIndex(IReadOnlyList<int> indices, int value)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                if (indices[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when every cell of row <paramref name="y"/> holds the same non-empty colour.</summary>
        private static bool IsRowMonochrome(Board board, int y)
        {
            int firstColourId = board[new GridPosition(0, y)];
            if (firstColourId == Board.EMPTY)
            {
                return false;
            }

            for (int x = 1; x < Board.SIZE; x++)
            {
                if (board[new GridPosition(x, y)] != firstColourId)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when every cell of column <paramref name="x"/> holds the same non-empty colour.</summary>
        private static bool IsColumnMonochrome(Board board, int x)
        {
            int firstColourId = board[new GridPosition(x, 0)];
            if (firstColourId == Board.EMPTY)
            {
                return false;
            }

            for (int y = 1; y < Board.SIZE; y++)
            {
                if (board[new GridPosition(x, y)] != firstColourId)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Empties row <paramref name="y"/>. Internal so <see cref="JokerFillResolver"/>
        /// clears a completed line through the exact same primitive a placement does, instead of
        /// carrying a second definition of "clear this line".</summary>
        internal static void ClearRow(Board board, int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Clear(new GridPosition(x, y));
            }
        }

        /// <summary>Empties column <paramref name="x"/>. Internal for the same reason as
        /// <see cref="ClearRow"/>.</summary>
        internal static void ClearColumn(Board board, int x)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                board.Clear(new GridPosition(x, y));
            }
        }
    }
}
