using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the joker fill-and-conditionally-clear rule: a legal fill only clears a row/column that
    /// actually became full as a result, an illegal target changes nothing, and the row/column
    /// intersection is never double-counted.
    /// </summary>
    public class JokerFillResolverTests
    {
        private const int COLOUR = 1;

        [Test]
        public void FillOnEmptyBoard_OccupiesTheCell_ClearsNothing()
        {
            Board board = new Board();
            GridPosition target = new GridPosition(3, 3);

            JokerFillResult result = JokerFillResolver.ResolveFill(board, target, COLOUR);

            Assert.IsTrue(result.Filled);
            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.LineCount);
            Assert.AreEqual(0, result.ClearedCellCount);
            Assert.IsTrue(board.IsOccupied(target));
        }

        [Test]
        public void Target_AlreadyOccupied_IsRejected_BoardUntouched()
        {
            Board board = new Board();
            GridPosition target = new GridPosition(2, 2);
            board.Occupy(target, COLOUR);

            JokerFillResult result = JokerFillResolver.ResolveFill(board, target, COLOUR + 1);

            Assert.IsFalse(result.Filled);
            Assert.IsFalse(result.AnyCleared);
            // Colour must be untouched by the rejected call, not merely "still occupied".
            Assert.AreEqual(COLOUR, board[target]);
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(Board.SIZE, 0)]
        [TestCase(0, Board.SIZE)]
        public void Target_OffBoard_IsRejected(int x, int y)
        {
            Board board = new Board();

            JokerFillResult result = JokerFillResolver.ResolveFill(board, new GridPosition(x, y), COLOUR);

            Assert.IsFalse(result.Filled);
        }

        [Test]
        public void FillingTheLastGapInARow_ClearsOnlyThatRow()
        {
            Board board = new Board();
            int row = 4;
            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x == 5)
                {
                    continue;
                }

                board.Occupy(new GridPosition(x, row), COLOUR);
            }

            GridPosition target = new GridPosition(5, row);
            JokerFillResult result = JokerFillResolver.ResolveFill(board, target, COLOUR);

            Assert.IsTrue(result.Filled);
            Assert.AreEqual(1, result.LineCount);
            CollectionAssert.AreEqual(new[] { row }, result.ClearedRows);
            Assert.AreEqual(0, result.ClearedColumns.Count);
            Assert.AreEqual(Board.SIZE, result.ClearedCellCount);
            Assert.IsFalse(board.IsOccupied(target));
            Assert.IsFalse(board.IsRowFull(row));
        }

        [Test]
        public void FillingTheLastGapInAColumn_ClearsOnlyThatColumn()
        {
            Board board = new Board();
            int column = 6;
            for (int y = 0; y < Board.SIZE; y++)
            {
                if (y == 2)
                {
                    continue;
                }

                board.Occupy(new GridPosition(column, y), COLOUR);
            }

            JokerFillResult result = JokerFillResolver.ResolveFill(board, new GridPosition(column, 2), COLOUR);

            Assert.IsTrue(result.Filled);
            Assert.AreEqual(1, result.LineCount);
            Assert.AreEqual(0, result.ClearedRows.Count);
            CollectionAssert.AreEqual(new[] { column }, result.ClearedColumns);
            Assert.AreEqual(Board.SIZE, result.ClearedCellCount);
        }

        [Test]
        public void FillingTheOnlyGapSharedByARowAndColumn_ClearsBoth_NoDoubleCount()
        {
            Board board = new Board();
            int row = 3;
            int column = 3;

            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x == column)
                {
                    continue;
                }

                board.Occupy(new GridPosition(x, row), COLOUR);
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                if (y == row)
                {
                    continue;
                }

                board.Occupy(new GridPosition(column, y), COLOUR);
            }

            GridPosition target = new GridPosition(column, row);
            JokerFillResult result = JokerFillResolver.ResolveFill(board, target, COLOUR);

            Assert.IsTrue(result.Filled);
            Assert.AreEqual(2, result.LineCount);
            CollectionAssert.AreEqual(new[] { row }, result.ClearedRows);
            CollectionAssert.AreEqual(new[] { column }, result.ClearedColumns);

            // Row (8) + column (8) - shared intersection counted once = 15, not 16.
            Assert.AreEqual((Board.SIZE * 2) - 1, result.ClearedCellCount);
            Assert.IsTrue(board.IsEmpty());
        }

        [Test]
        public void FillingAGapThatCompletesNeitherLine_LeavesBoardOtherwiseUnchanged()
        {
            Board board = new Board();
            board.Occupy(new GridPosition(0, 0), COLOUR);
            board.Occupy(new GridPosition(1, 1), COLOUR);

            GridPosition target = new GridPosition(4, 4);
            JokerFillResult result = JokerFillResolver.ResolveFill(board, target, COLOUR);

            Assert.IsTrue(result.Filled);
            Assert.IsFalse(result.AnyCleared);
            Assert.IsTrue(board.IsOccupied(new GridPosition(0, 0)));
            Assert.IsTrue(board.IsOccupied(new GridPosition(1, 1)));
            Assert.IsTrue(board.IsOccupied(target));
        }
    }
}
