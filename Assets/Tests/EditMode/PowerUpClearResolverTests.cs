using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the two properties every power-up clear has to hold: the affected region is clamped to
    /// the board, and only cells that actually held a colour are reported as cleared.
    /// </summary>
    public class PowerUpClearResolverTests
    {
        [Test]
        public void ResolveBombClear_CentredInTheMiddleOfAFullBoard_ClearsNineCells()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(4, 4));

            Assert.IsTrue(result.AnyCleared);
            Assert.AreEqual(9, result.ClearedCellCount);

            for (int y = 3; y <= 5; y++)
            {
                for (int x = 3; x <= 5; x++)
                {
                    Assert.AreEqual(Board.EMPTY, board[new GridPosition(x, y)], $"({x},{y}) should be cleared.");
                }
            }
        }

        [Test]
        public void ResolveBombClear_CentredOnACorner_ClampsToFourCells()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(0, 0));

            Assert.AreEqual(4, result.ClearedCellCount);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(1, 1)]);
            // One step past the clamped region on both axes is untouched.
            Assert.AreEqual(1, board[new GridPosition(2, 0)]);
            Assert.AreEqual(1, board[new GridPosition(0, 2)]);
        }

        [Test]
        public void ResolveBombClear_CentredOnTheOppositeCorner_AlsoClampsToFourCells()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(
                board, new GridPosition(Board.SIZE - 1, Board.SIZE - 1));

            Assert.AreEqual(4, result.ClearedCellCount);
        }

        [Test]
        public void ResolveBombClear_CentredOnAnEdge_ClampsToSixCells()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(0, 4));

            Assert.AreEqual(6, result.ClearedCellCount);
        }

        [Test]
        public void ResolveBombClear_PartiallyFilledArea_ReportsOnlyTheCellsThatWereOccupied()
        {
            var board = new Board();
            board.Occupy(new GridPosition(3, 3), 1);
            board.Occupy(new GridPosition(4, 4), 2);
            // Just outside the 3x3 around (4,4) — must survive and must not be reported.
            board.Occupy(new GridPosition(6, 4), 3);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(4, 4));

            Assert.AreEqual(2, result.ClearedCellCount);
            CollectionAssert.AreEquivalent(
                new[] { new GridPosition(3, 3), new GridPosition(4, 4) }, result.ClearedCells);
            Assert.AreEqual(3, board[new GridPosition(6, 4)]);
        }

        [Test]
        public void ResolveBombClear_OverAnEmptyArea_ClearsNothingAndLeavesTheBoardUntouched()
        {
            var board = new Board();
            board.Occupy(new GridPosition(7, 7), 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(2, 2));

            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.ClearedCellCount);
            Assert.AreEqual(1, board[new GridPosition(7, 7)]);
        }

        [Test]
        public void ResolveBombClear_ClearsTheLastCellsOfARowAndColumn_ReportsBothAsEmptied()
        {
            var board = new Board();
            // Row 4 and column 4 each have exactly one occupied cell, both inside the bomb's 3x3 —
            // clearing it empties the whole row and the whole column, not just the one cell.
            board.Occupy(new GridPosition(4, 4), 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(4, 4));

            Assert.IsTrue(result.AnyLineEmptied);
            Assert.AreEqual(2, result.EmptiedLineCount);
            CollectionAssert.Contains(result.EmptiedRows, 4);
            CollectionAssert.Contains(result.EmptiedColumns, 4);
            Assert.IsTrue(board.IsRowEmpty(4));
            Assert.IsTrue(board.IsColumnEmpty(4));
        }

        [Test]
        public void ResolveBombClear_RowStillHasAnOccupiedCellOutsideTheBlast_DoesNotReportItEmptied()
        {
            var board = new Board();
            board.Occupy(new GridPosition(4, 4), 1);
            // Same row, well outside the 3x3 blast around (4,4) — survives, so the row is not empty.
            board.Occupy(new GridPosition(0, 4), 2);
            // Also give column 4 a survivor outside the blast, so only the row question is isolated —
            // otherwise (4,4) being the column's only occupied cell would trivially empty it too.
            board.Occupy(new GridPosition(4, 0), 3);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(4, 4));

            CollectionAssert.DoesNotContain(result.EmptiedRows, 4);
            Assert.IsFalse(board.IsRowEmpty(4));
        }

        [Test]
        public void ResolveBombClear_ClearsTwoCellsOfTheSameRow_ReportsThatRowOnlyOnce()
        {
            var board = new Board();
            // Two occupied cells in row 4, both inside the 3x3 around (4,4) — the row is emptied by two
            // separate cleared cells, so it must be deduped to a single entry, not reported per cell.
            board.Occupy(new GridPosition(3, 4), 1);
            board.Occupy(new GridPosition(5, 4), 2);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(4, 4));

            CollectionAssert.AreEqual(new[] { 4 }, result.EmptiedRows);
        }

        [Test]
        public void ResolveBombClear_OverAnEmptyArea_ReportsNoEmptiedLines()
        {
            var board = new Board();
            board.Occupy(new GridPosition(7, 7), 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(2, 2));

            Assert.IsFalse(result.AnyLineEmptied);
            Assert.AreEqual(0, result.EmptiedLineCount);
        }

        [Test]
        public void ResolveRowClear_PartiallyFilledRow_ClearsItWithoutNeedingItToBeFull()
        {
            var board = new Board();
            board.Occupy(new GridPosition(1, 2), 1);
            board.Occupy(new GridPosition(5, 2), 2);
            board.Occupy(new GridPosition(5, 3), 3);

            PowerUpClearResult result = PowerUpClearResolver.ResolveRowClear(board, 2);

            Assert.AreEqual(2, result.ClearedCellCount);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(1, 2)]);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(5, 2)]);
            // A neighbouring row is untouched.
            Assert.AreEqual(3, board[new GridPosition(5, 3)]);
        }

        [Test]
        public void ResolveRowClear_FullRow_ClearsEveryCell()
        {
            var board = new Board();
            FillRow(board, y: 6, colourId: 4);

            PowerUpClearResult result = PowerUpClearResolver.ResolveRowClear(board, 6);

            Assert.AreEqual(Board.SIZE, result.ClearedCellCount);
            Assert.IsFalse(board.IsRowFull(6));
        }

        [Test]
        public void ResolveRowClear_PartiallyFilledRow_TriviallyReportsItsOwnRowAsEmptied()
        {
            // Documents the trivial case the type-level doc comment calls out: Row Clear always empties
            // its own target, so EmptiedRows always contains it when anything was cleared at all — this
            // is not a "surprise" signal for this power-up the way it is for Bomb.
            var board = new Board();
            board.Occupy(new GridPosition(1, 2), 1);
            // Give column 1 a survivor outside row 2, so it isn't trivially emptied too — isolates the
            // assertion to "the row clear reports its own row", not an incidental column side effect.
            board.Occupy(new GridPosition(1, 5), 2);

            PowerUpClearResult result = PowerUpClearResolver.ResolveRowClear(board, 2);

            CollectionAssert.AreEqual(new[] { 2 }, result.EmptiedRows);
            Assert.AreEqual(0, result.EmptiedColumns.Count);
        }

        [Test]
        public void ResolveRowClear_EmptyRow_ClearsNothing()
        {
            var board = new Board();

            PowerUpClearResult result = PowerUpClearResolver.ResolveRowClear(board, 0);

            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.ClearedCellCount);
        }

        [Test]
        public void ResolveColumnClear_PartiallyFilledColumn_ClearsItWithoutNeedingItToBeFull()
        {
            var board = new Board();
            board.Occupy(new GridPosition(4, 0), 1);
            board.Occupy(new GridPosition(4, 7), 2);
            board.Occupy(new GridPosition(3, 7), 3);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColumnClear(board, 4);

            Assert.AreEqual(2, result.ClearedCellCount);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(4, 0)]);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(4, 7)]);
            Assert.AreEqual(3, board[new GridPosition(3, 7)]);
        }

        [Test]
        public void ResolveColumnClear_FullColumn_ClearsEveryCell()
        {
            var board = new Board();
            FillColumn(board, x: 1, colourId: 5);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColumnClear(board, 1);

            Assert.AreEqual(Board.SIZE, result.ClearedCellCount);
            Assert.IsFalse(board.IsColumnFull(1));
        }

        [Test]
        public void ResolveColumnClear_EmptyColumn_ClearsNothing()
        {
            var board = new Board();

            PowerUpClearResult result = PowerUpClearResolver.ResolveColumnClear(board, 7);

            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.ClearedCellCount);
        }

        private static void FillBoard(Board board, int colourId)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                FillRow(board, y, colourId);
            }
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

        [Test]
        public void ResolveColorCleanser_OnAnOccupiedCell_ClearsEveryMatchingCellAcrossTheWholeBoard()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(3, 5), 1);
            board.Occupy(new GridPosition(7, 7), 1);
            // A different colour at a cell physically between two matches — must survive.
            board.Occupy(new GridPosition(2, 5), 2);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(board, new GridPosition(0, 0));

            Assert.IsTrue(result.AnyCleared);
            Assert.AreEqual(3, result.ClearedCellCount);
            CollectionAssert.AreEquivalent(
                new[] { new GridPosition(0, 0), new GridPosition(3, 5), new GridPosition(7, 7) },
                result.ClearedCells);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(0, 0)]);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(3, 5)]);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(7, 7)]);
            Assert.AreEqual(2, board[new GridPosition(2, 5)]);
        }

        [Test]
        public void ResolveColorCleanser_OnAnEmptyCell_IsRejected_BoardUntouched()
        {
            var board = new Board();
            board.Occupy(new GridPosition(4, 4), 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(board, new GridPosition(0, 0));

            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.ClearedCellCount);
            Assert.AreEqual(1, board[new GridPosition(4, 4)]);
        }

        [Test]
        public void ResolveColorCleanser_OffBoard_Throws()
        {
            var board = new Board();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => PowerUpClearResolver.ResolveColorCleanser(board, new GridPosition(-1, 0)));
        }

        [Test]
        public void ResolveColorCleanser_ClearingTheOnlyCellsOfARow_ReportsItEmptied()
        {
            var board = new Board();
            board.Occupy(new GridPosition(1, 3), 5);
            board.Occupy(new GridPosition(6, 3), 5);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(board, new GridPosition(1, 3));

            Assert.IsTrue(result.AnyLineEmptied);
            CollectionAssert.Contains(result.EmptiedRows, 3);
        }
    }
}
