using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    public class PlacementRulesTests
    {
        [Test]
        public void CanPlace_OnEmptyBoardWithinBounds_ReturnsTrue()
        {
            var board = new Board();
            Piece piece = Line(3);

            Assert.IsTrue(PlacementRules.CanPlace(board, piece, new GridPosition(0, 0)));
        }

        [Test]
        public void CanPlace_WhenPieceWouldExtendPastRightEdge_ReturnsFalse()
        {
            var board = new Board();
            Piece piece = Line(3);

            Assert.IsFalse(PlacementRules.CanPlace(board, piece, new GridPosition(6, 0)));
        }

        [Test]
        public void CanPlace_WhenAnchorIsNegative_ReturnsFalse()
        {
            var board = new Board();
            Piece piece = Line(2);

            Assert.IsFalse(PlacementRules.CanPlace(board, piece, new GridPosition(-1, 0)));
        }

        [Test]
        public void CanPlace_WhenTargetCellIsOccupied_ReturnsFalse()
        {
            var board = new Board();
            board.Occupy(new GridPosition(1, 0), 1);
            Piece piece = Line(3);

            Assert.IsFalse(PlacementRules.CanPlace(board, piece, new GridPosition(0, 0)));
        }

        [Test]
        public void Place_OccupiesEveryOffsetCellWithGivenColour()
        {
            var board = new Board();
            Piece piece = Line(3);

            PlacementRules.Place(board, piece, new GridPosition(0, 0), colourId: 5);

            Assert.AreEqual(5, board[new GridPosition(0, 0)]);
            Assert.AreEqual(5, board[new GridPosition(1, 0)]);
            Assert.AreEqual(5, board[new GridPosition(2, 0)]);
        }

        [Test]
        public void Place_WhenNotLegal_Throws()
        {
            var board = new Board();
            board.Occupy(new GridPosition(1, 0), 1);
            Piece piece = Line(3);

            Assert.Throws<InvalidOperationException>(
                () => PlacementRules.Place(board, piece, new GridPosition(0, 0), colourId: 2));
        }

        [Test]
        public void Place_DoesNotAffectCellsOutsidePieceOffsets()
        {
            var board = new Board();
            Piece piece = Line(2);

            PlacementRules.Place(board, piece, new GridPosition(0, 0), colourId: 3);

            Assert.AreEqual(Board.EMPTY, board[new GridPosition(2, 0)]);
        }

        private static Piece Line(int length)
        {
            var offsets = new GridPosition[length];
            for (int i = 0; i < length; i++)
            {
                offsets[i] = new GridPosition(i, 0);
            }

            return new Piece($"test_line_{length}", offsets);
        }
    }
}
