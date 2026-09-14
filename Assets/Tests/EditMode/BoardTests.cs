using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>Covers <see cref="Board.IsEmpty"/> (a fresh board starts empty, occupying any cell makes
    /// it non-empty, clearing it returns to empty), <see cref="Board.HasIsolatedEmptyCells"/> (the
    /// flood-fill correctly distinguishes a walled-off hole from one with any path to the edge, and
    /// treats every border cell as inherently reachable), and <see cref="Board.IsCenterCoreEmpty"/>.</summary>
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

        [Test]
        public void EmptyBoard_HasNoIsolatedCells()
        {
            Board board = new Board();

            Assert.IsFalse(board.HasIsolatedEmptyCells());
        }

        [Test]
        public void FullBoard_HasNoIsolatedCells()
        {
            // No empty cells at all, so there is nothing to be isolated.
            Board board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            Assert.IsFalse(board.HasIsolatedEmptyCells());
        }

        [Test]
        public void SingleEmptyCellFullyEnclosedByOccupiedNeighbours_IsIsolated()
        {
            // Every cell filled except (3,3), which is surrounded on all 4 sides — walled off from
            // every board edge, so it cannot be reached by the flood-fill.
            Board board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            GridPosition hole = new GridPosition(3, 3);
            board.Clear(hole);

            Assert.IsTrue(board.HasIsolatedEmptyCells());
        }

        [Test]
        public void EmptyCellWithAnOpenPathToTheEdge_IsNotIsolated()
        {
            // A corridor of empty cells connects (3,3) straight out to the left edge — reachable,
            // not isolated, even though most of the board is occupied.
            Board board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            for (int x = 0; x <= 3; x++)
            {
                board.Clear(new GridPosition(x, 3));
            }

            Assert.IsFalse(board.HasIsolatedEmptyCells());
        }

        [Test]
        public void EmptyCellOnTheBorder_IsNeverIsolated()
        {
            // A border cell is its own edge contact — the flood-fill seeds from every empty border
            // cell directly, so this can never be walled off regardless of its neighbours.
            Board board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            board.Clear(new GridPosition(0, 4));

            Assert.IsFalse(board.HasIsolatedEmptyCells());
        }

        [Test]
        public void EmptyBoard_HasAnEmptyCenterCore()
        {
            Board board = new Board();

            Assert.IsTrue(board.IsCenterCoreEmpty());
        }

        [Test]
        public void OccupyingACellOutsideTheCenterCore_LeavesItEmpty()
        {
            Board board = new Board();
            // Corner — well outside the centered 4x4 core (indices 2..5 on an 8-wide board).
            board.Occupy(new GridPosition(0, 0), 1);

            Assert.IsTrue(board.IsCenterCoreEmpty());
        }

        [Test]
        public void OccupyingASingleCellInsideTheCenterCore_MakesItNotEmpty()
        {
            Board board = new Board();
            board.Occupy(new GridPosition(3, 3), 1);

            Assert.IsFalse(board.IsCenterCoreEmpty());
        }

        [Test]
        public void ClearingTheOnlyOccupiedCoreCell_RestoresAnEmptyCore()
        {
            Board board = new Board();
            GridPosition corePosition = new GridPosition(4, 4);
            board.Occupy(corePosition, 1);

            board.Clear(corePosition);

            Assert.IsTrue(board.IsCenterCoreEmpty());
        }
    }
}
