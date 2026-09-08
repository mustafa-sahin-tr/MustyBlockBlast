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
