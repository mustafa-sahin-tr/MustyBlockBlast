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

        public static LineClearResult ResolveClears(Board board)
        {
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
