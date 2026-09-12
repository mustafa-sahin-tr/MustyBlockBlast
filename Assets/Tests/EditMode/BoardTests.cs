using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>Covers <see cref="Board.IsEmpty"/>: a fresh board starts empty, occupying any cell makes it
    /// non-empty, and clearing that cell back returns it to empty.</summary>
    public class BoardTests
    {
        [Test]
        public void NewBoard_IsEmpty()
        {
            Board board = new Board();

            Assert.IsTrue(board.IsEmpty());
        }

        [Test]
        public void OccupyingOneCell_MakesBoardNotEmpty()
        {
            Board board = new Board();
            GridPosition position = new GridPosition(0, 0);

            board.Occupy(position, 1);

            Assert.IsFalse(board.IsEmpty());
        }

        [Test]
        public void ClearingTheOnlyOccupiedCell_MakesBoardEmptyAgain()
        {
            Board board = new Board();
            GridPosition position = new GridPosition(3, 5);
            board.Occupy(position, 2);

            board.Clear(position);

            Assert.IsTrue(board.IsEmpty());
        }
    }
}
