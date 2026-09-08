using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    public class LineClearResolverTests
    {
        [Test]
        public void ResolveClears_NoFullLines_ReturnsEmptyResultAndLeavesBoardUntouched()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.LineCount);
            Assert.AreEqual(0, result.ClearedCellCount);
            Assert.AreEqual(1, board[new GridPosition(0, 0)]);
        }

        [Test]
        public void ResolveClears_OneFullRow_ClearsOnlyThatRow()
        {
            var board = new Board();
            FillRow(board, y: 3, colourId: 1);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.IsTrue(result.AnyCleared);
            Assert.AreEqual(1, result.LineCount);
            CollectionAssert.AreEqual(new[] { 3 }, result.ClearedRows);
            Assert.AreEqual(0, result.ClearedColumns.Count);
            Assert.AreEqual(Board.SIZE, result.ClearedCellCount);
            Assert.IsFalse(board.IsRowFull(3));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(Board.EMPTY, board[new GridPosition(x, 3)]);
            }
        }

        [Test]
        public void ResolveClears_OneFullColumn_ClearsOnlyThatColumn()
        {
            var board = new Board();
            FillColumn(board, x: 5, colourId: 1);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(1, result.LineCount);
            CollectionAssert.AreEqual(new[] { 5 }, result.ClearedColumns);
            Assert.AreEqual(Board.SIZE, result.ClearedCellCount);
        }

        [Test]
        public void ResolveClears_IntersectingRowAndColumn_ClearsBothAndCountsSharedCellOnce()
        {
            var board = new Board();
            FillRow(board, y: 2, colourId: 1);
            FillColumn(board, x: 4, colourId: 1);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(2, result.LineCount);
            CollectionAssert.AreEqual(new[] { 2 }, result.ClearedRows);
            CollectionAssert.AreEqual(new[] { 4 }, result.ClearedColumns);
            // 8 + 8 - 1 shared intersection cell.
            Assert.AreEqual((Board.SIZE * 2) - 1, result.ClearedCellCount);

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(Board.EMPTY, board[new GridPosition(x, 2)]);
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                Assert.AreEqual(Board.EMPTY, board[new GridPosition(4, y)]);
            }
        }

        [Test]
        public void ResolveClears_MultipleRowsAndColumnsSimultaneously_ClearsAllOfThem()
        {
            var board = new Board();
            FillRow(board, y: 0, colourId: 1);
            FillRow(board, y: 1, colourId: 1);
            FillColumn(board, x: 0, colourId: 1);
            FillColumn(board, x: 1, colourId: 1);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(4, result.LineCount);
            // 2 rows * 8 + 2 cols * 8 - (2 rows * 2 cols) shared intersections.
            Assert.AreEqual((2 * Board.SIZE) + (2 * Board.SIZE) - (2 * 2), result.ClearedCellCount);

            for (int x = 0; x < Board.SIZE; x++)
            {
                for (int y = 0; y < Board.SIZE; y++)
                {
                    if (x < 2 || y < 2)
                    {
                        Assert.AreEqual(Board.EMPTY, board[new GridPosition(x, y)], $"({x},{y}) should be cleared.");
                    }
                }
            }
        }

        [Test]
        public void ResolveClears_CellOutsideAnyClearedLine_RemainsOccupied()
        {
            var board = new Board();
            FillRow(board, y: 0, colourId: 1);
            board.Occupy(new GridPosition(3, 5), 2);

            LineClearResolver.ResolveClears(board);

            Assert.AreEqual(2, board[new GridPosition(3, 5)]);
        }

        private static void FillRow(Board board, int y, int colourId)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, y), colourId);
            }
        }

        private static void FillColumn(Board board, int x, int colourId)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                board.Occupy(new GridPosition(x, y), colourId);
            }
        }
    }
}
