using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>Result of resolving line clears for one placement. Rows and columns are cleared
    /// simultaneously; a cell at their intersection is counted once.</summary>
    public readonly struct LineClearResult
    {
        public LineClearResult(IReadOnlyList<int> clearedRows, IReadOnlyList<int> clearedColumns, int clearedCellCount)
        {
            ClearedRows = clearedRows;
            ClearedColumns = clearedColumns;
            ClearedCellCount = clearedCellCount;
        }

        public IReadOnlyList<int> ClearedRows { get; }

        public IReadOnlyList<int> ClearedColumns { get; }

        /// <summary>Total distinct cells emptied, counting each row/column intersection once.</summary>
        public int ClearedCellCount { get; }

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
                return new LineClearResult(resultRows, resultColumns, 0);
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

            return new LineClearResult(resultRows, resultColumns, clearedCellCount);
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

            for (int i = 0; i < clearedRows.Count; i++)
            {
                ClearRow(board, clearedRows[i]);
            }

            for (int i = 0; i < clearedColumns.Count; i++)
            {
                ClearColumn(board, clearedColumns[i]);
            }

            return new LineClearResult(clearedRows, clearedColumns, clearedCellCount);
        }

        private static void ClearRow(Board board, int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Clear(new GridPosition(x, y));
            }
        }

        private static void ClearColumn(Board board, int x)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                board.Clear(new GridPosition(x, y));
            }
        }
    }
}
