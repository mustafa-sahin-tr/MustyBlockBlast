using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    public class MoveAvailabilityTests
    {
        [Test]
        public void CanPlaceAnywhere_EmptyBoard_ReturnsTrue()
        {
            var board = new Board();
            Piece piece = Square(3);

            Assert.IsTrue(MoveAvailability.CanPlaceAnywhere(board, piece));
        }

        [Test]
        public void CanPlaceAnywhere_BoardFullExceptOneCell_LargerPieceReturnsFalse()
        {
            var board = new Board();
            FillEntireBoard(board, colourId: 1);
            board.Clear(new GridPosition(0, 0));

            Piece twoByTwo = Square(2);

            Assert.IsFalse(MoveAvailability.CanPlaceAnywhere(board, twoByTwo));
        }

        [Test]
        public void CanPlaceAnywhere_BoardFullExceptOneCell_SingleCellPieceReturnsTrue()
        {
            var board = new Board();
            FillEntireBoard(board, colourId: 1);
            board.Clear(new GridPosition(4, 4));

            Piece single = new Piece("single", new[] { new GridPosition(0, 0) });

            Assert.IsTrue(MoveAvailability.CanPlaceAnywhere(board, single));
        }

        [Test]
        public void HasAnyMove_AtLeastOnePieceFits_ReturnsTrue()
        {
            var board = new Board();
            FillEntireBoard(board, colourId: 1);
            board.Clear(new GridPosition(0, 0));

            var tray = new List<Piece>
            {
                Square(3),
                new Piece("single", new[] { new GridPosition(0, 0) }),
            };

            Assert.IsTrue(MoveAvailability.HasAnyMove(board, tray));
        }

        [Test]
        public void HasAnyMove_FullBoard_NoTrayPieceFits_ReturnsFalse()
        {
            var board = new Board();
            FillEntireBoard(board, colourId: 1);

            var tray = new List<Piece>
            {
                new Piece("single", new[] { new GridPosition(0, 0) }),
                Square(2),
                Square(3),
            };

            Assert.IsFalse(MoveAvailability.HasAnyMove(board, tray));
        }

        [Test]
        public void HasAnyMove_GapTooSmallForEveryTrayPiece_ReturnsFalse()
        {
            // A single 1x1 gap in an otherwise full board: nothing bigger than 1 cell fits,
            // and the tray only offers pieces of 2+ cells — this is the "no moves left" game-over case.
            var board = new Board();
            FillEntireBoard(board, colourId: 1);
            board.Clear(new GridPosition(7, 7));

            var tray = new List<Piece>
            {
                Square(2),
                new Piece("domino", new[] { new GridPosition(0, 0), new GridPosition(1, 0) }),
            };

            Assert.IsFalse(MoveAvailability.HasAnyMove(board, tray));
        }

        private static Piece Square(int size)
        {
            var offsets = new List<GridPosition>();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    offsets.Add(new GridPosition(x, y));
                }
            }

            return new Piece($"square_{size}", offsets);
        }

        private static void FillEntireBoard(Board board, int colourId)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                for (int y = 0; y < Board.SIZE; y++)
                {
                    board.Occupy(new GridPosition(x, y), colourId);
                }
            }
        }
    }
}
