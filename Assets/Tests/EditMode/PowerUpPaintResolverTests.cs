using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the properties a Paint Cross (issue #295) has to hold: the whole row and column are
    /// targeted with the intersection counted once, only cells that actually held a colour are painted
    /// and reported, nothing is ever cleared, and a cell keeps everything but its colour.
    /// </summary>
    public class PowerUpPaintResolverTests
    {
        private const int PAINT_COLOUR = 3;

        [Test]
        public void ResolvePaintCross_OnAFullBoard_PaintsTheRowAndColumnWithTheIntersectionOnce()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);

            PowerUpPaintResult result = PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(2, 5), PAINT_COLOUR);

            Assert.IsTrue(result.AnyPainted);
            Assert.AreEqual((Board.SIZE * 2) - 1, result.PaintedCellCount);
            Assert.AreEqual(PAINT_COLOUR, result.ColourId);

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(PAINT_COLOUR, board[new GridPosition(x, 5)], $"({x},5) is on the row.");
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                Assert.AreEqual(PAINT_COLOUR, board[new GridPosition(2, y)], $"(2,{y}) is on the column.");
            }

            // Off both lines: untouched.
            Assert.AreEqual(1, board[new GridPosition(0, 0)]);
            Assert.AreEqual(1, board[new GridPosition(7, 7)]);
        }

        [Test]
        public void ResolvePaintCross_OnAPartiallyFilledCross_PaintsOnlyTheOccupiedCellsAndLeavesEmptiesEmpty()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 4), 1);
            board.Occupy(new GridPosition(4, 7), 2);
            // Off the cross: must survive in its own colour.
            board.Occupy(new GridPosition(6, 6), 5);

            PowerUpPaintResult result = PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(4, 4), PAINT_COLOUR);

            Assert.AreEqual(2, result.PaintedCellCount);
            CollectionAssert.AreEquivalent(
                new[] { new GridPosition(0, 4), new GridPosition(4, 7) }, result.PaintedCells);
            Assert.AreEqual(PAINT_COLOUR, board[new GridPosition(0, 4)]);
            Assert.AreEqual(PAINT_COLOUR, board[new GridPosition(4, 7)]);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(4, 4)], "The empty target stays empty.");
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(3, 4)]);
            Assert.AreEqual(5, board[new GridPosition(6, 6)]);
        }

        [Test]
        public void ResolvePaintCross_OnAnEmptyCross_IsRejected_BoardUntouched()
        {
            var board = new Board();
            board.Occupy(new GridPosition(7, 7), 1);

            PowerUpPaintResult result = PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(2, 2), PAINT_COLOUR);

            Assert.IsFalse(result.AnyPainted);
            Assert.AreEqual(0, result.PaintedCellCount);
            Assert.AreEqual(1, board[new GridPosition(7, 7)]);
        }

        /// <summary>The target names the cross, not the block: an empty target cell with something
        /// standing elsewhere on its row is a legal, painted cross.</summary>
        [Test]
        public void ResolvePaintCross_OnAnEmptyTargetWithAnOccupiedRow_IsLegalAndPaintsTheRow()
        {
            var board = new Board();
            board.Occupy(new GridPosition(7, 3), 1);

            PowerUpPaintResult result = PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(0, 3), PAINT_COLOUR);

            Assert.IsTrue(result.AnyPainted);
            Assert.AreEqual(1, result.PaintedCellCount);
            Assert.AreEqual(PAINT_COLOUR, board[new GridPosition(7, 3)]);
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(0, 3)]);
        }

        /// <summary>A cell already wearing the paint colour is occupied and targeted, so it is painted
        /// (a no-op write) and reported — the legality signal is about occupancy, not about change.</summary>
        [Test]
        public void ResolvePaintCross_OnACellAlreadyOfThatColour_StillReportsIt()
        {
            var board = new Board();
            board.Occupy(new GridPosition(4, 4), PAINT_COLOUR);

            PowerUpPaintResult result = PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(4, 4), PAINT_COLOUR);

            Assert.IsTrue(result.AnyPainted);
            Assert.AreEqual(1, result.PaintedCellCount);
            Assert.AreEqual(PAINT_COLOUR, board[new GridPosition(4, 4)]);
        }

        /// <summary>The whole point of <see cref="Board.Paint"/>: a painted cell keeps its special kind,
        /// its hit count and its gem — only the colour changes.</summary>
        [Test]
        public void ResolvePaintCross_OverSpecialCells_ChangesOnlyTheirColour()
        {
            var board = new Board();
            var reinforced = new GridPosition(1, 4);
            var timer = new GridPosition(4, 1);
            var diamond = new GridPosition(6, 4);
            var coin = new GridPosition(4, 6);
            board.OccupyReinforced(reinforced, 1, hitCount: 3);
            board.OccupyTimer(timer, 2, startingCountdown: 5);
            board.OccupyDiamond(diamond, 1, diamondColourId: 2);
            board.Occupy(coin, 1);
            board.SetSpecialKind(coin, SpecialCellKind.Coin);
            board.SetCoinValue(coin, 4);

            PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(4, 4), PAINT_COLOUR);

            Assert.AreEqual(PAINT_COLOUR, board[reinforced]);
            Assert.AreEqual(3, board.GetHitCount(reinforced));

            Assert.AreEqual(PAINT_COLOUR, board[timer]);
            Assert.AreEqual(SpecialCellKind.Timer, board.GetSpecialKind(timer));
            Assert.AreEqual(5, board.GetTimerCountdown(timer));

            Assert.AreEqual(PAINT_COLOUR, board[diamond]);
            Assert.AreEqual(SpecialCellKind.Diamond, board.GetSpecialKind(diamond));
            Assert.AreEqual(2, board.GetDiamondColourId(diamond));

            Assert.AreEqual(PAINT_COLOUR, board[coin]);
            Assert.AreEqual(SpecialCellKind.Coin, board.GetSpecialKind(coin));
            Assert.AreEqual(4, board.GetCoinValue(coin));
        }

        /// <summary>Colour never affects clearing: a row the paint makes monochrome — or one that was
        /// already full — stays full and standing. Nothing here empties a cell.</summary>
        [Test]
        public void ResolvePaintCross_OnAFullRow_ClearsNothing()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 2), (x % Board.COLOUR_COUNT) + 1);
            }

            PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(3, 2), PAINT_COLOUR);

            Assert.IsTrue(board.IsRowFull(2));
            Assert.AreEqual(Board.SIZE, board.OccupiedCellCount());
        }

        [Test]
        public void ResolvePaintCross_OnAShapedBoard_SkipsHoles()
        {
            var shape = new BoardShape(Board.SIZE, Board.SIZE, new[] { new GridPosition(3, 4) });
            var board = new Board(shape);
            board.Occupy(new GridPosition(0, 4), 1);
            board.Occupy(new GridPosition(4, 0), 1);

            PowerUpPaintResult result = PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(4, 4), PAINT_COLOUR);

            Assert.AreEqual(2, result.PaintedCellCount);
            CollectionAssert.DoesNotContain(result.PaintedCells, new GridPosition(3, 4));
        }

        [Test]
        public void ResolvePaintCross_OffBoard_Throws()
        {
            var board = new Board();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(-1, 0), PAINT_COLOUR));
        }

        [TestCase(Board.EMPTY)]
        [TestCase(Board.COLOUR_COUNT + 1)]
        [TestCase(-1)]
        public void ResolvePaintCross_WithAColourOutsideThePalette_Throws(int colourId)
        {
            var board = new Board();
            board.Occupy(new GridPosition(4, 4), 1);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => PowerUpPaintResolver.ResolvePaintCross(board, new GridPosition(4, 4), colourId));
        }

        [Test]
        public void ResolvePaintCross_WithANullBoard_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PowerUpPaintResolver.ResolvePaintCross(null, new GridPosition(0, 0), PAINT_COLOUR));
        }

        // --- Board.Paint, the primitive ---

        [Test]
        public void BoardPaint_OnAnEmptyCell_Throws()
        {
            var board = new Board();

            Assert.Throws<ArgumentException>(() => board.Paint(new GridPosition(0, 0), PAINT_COLOUR));
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(0, 0)]);
        }

        [Test]
        public void BoardPaint_WithTheEmptyColour_Throws()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);

            Assert.Throws<ArgumentException>(() => board.Paint(new GridPosition(0, 0), Board.EMPTY));
            Assert.AreEqual(1, board[new GridPosition(0, 0)]);
        }

        // --- PowerUpTargetCells.ForPaintCross, the geometry ---

        [Test]
        public void ForPaintCross_ListsTheIntersectionOnce()
        {
            var buffer = new List<GridPosition>(PowerUpTargetCells.MAX_TARGET_CELLS);

            IReadOnlyList<GridPosition> cells =
                PowerUpTargetCells.ForPaintCross(BoardShape.Standard, new GridPosition(4, 4), buffer);

            Assert.AreEqual((Board.SIZE * 2) - 1, cells.Count);
            int intersectionCount = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Equals(new GridPosition(4, 4)))
                {
                    intersectionCount++;
                }
            }

            Assert.AreEqual(1, intersectionCount);
        }

        [Test]
        public void ForPaintCross_OffBoard_YieldsNothing()
        {
            var buffer = new List<GridPosition>();

            IReadOnlyList<GridPosition> cells =
                PowerUpTargetCells.ForPaintCross(BoardShape.Standard, new GridPosition(Board.SIZE, 0), buffer);

            Assert.AreEqual(0, cells.Count);
        }

        /// <summary>The buffer hint covers a standard-board cross, so the first aim frame of a Paint
        /// Cross never grows the list every other kind shares.</summary>
        [Test]
        public void MaxTargetCells_CoversAStandardBoardCross()
        {
            Assert.GreaterOrEqual(PowerUpTargetCells.MAX_TARGET_CELLS, (Board.SIZE * 2) - 1);
        }

        private static void FillBoard(Board board, int colourId)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), colourId);
                }
            }
        }
    }
}
