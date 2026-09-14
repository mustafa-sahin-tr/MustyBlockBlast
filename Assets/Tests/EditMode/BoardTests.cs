using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>Covers <see cref="Board.IsEmpty"/> (a fresh board starts empty, occupying any cell makes
    /// it non-empty, clearing it returns to empty), <see cref="Board.HasIsolatedEmptyCells"/> (the
    /// flood-fill correctly distinguishes a walled-off hole from one with any path to the edge, and
    /// treats every border cell as inherently reachable), <see cref="Board.LargestEmptyRegionSize"/> (the
    /// second flood-fill, which counts connected components rather than answering a reachability
    /// question) and <see cref="Board.IsCenterCoreEmpty"/>.</summary>
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
        public void EmptyBoard_LargestEmptyRegionIsTheWholeBoard()
        {
            Board board = new Board();

            Assert.AreEqual(Board.SIZE * Board.SIZE, LargestEmptyRegion(board));
        }

        [Test]
        public void FullBoard_HasNoEmptyRegionAtAll()
        {
            Board board = new Board();
            FillEntireBoard(board);

            Assert.AreEqual(0, LargestEmptyRegion(board));
        }

        [Test]
        public void OneOccupiedCell_LeavesTheRestAsASingleRegion()
        {
            // Removing a non-cut cell from an open board splits nothing: everything else is still
            // reachable from everything else.
            Board board = new Board();
            board.Occupy(new GridPosition(4, 4), 1);

            Assert.AreEqual((Board.SIZE * Board.SIZE) - 1, LargestEmptyRegion(board));
        }

        [Test]
        public void TwoSeparatePockets_ReportsTheLargerOne()
        {
            // Column 4 walled top to bottom: 32 empty cells to its left, 24 to its right, no path
            // between them. Unlike HasIsolatedEmptyCells — which would call both sides reachable, since
            // both touch the border — this has to tell them apart and pick the bigger.
            Board board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                board.Occupy(new GridPosition(4, y), 1);
            }

            Assert.AreEqual(4 * Board.SIZE, LargestEmptyRegion(board));
        }

        [Test]
        public void AWalledOffHole_IsNotCountedTowardsTheOpenRegion()
        {
            // The 3x3 block around (3,3) is occupied except its centre, so that centre is a region of
            // exactly one cell while everything outside the block is one big region.
            Board board = new Board();
            for (int y = 2; y <= 4; y++)
            {
                for (int x = 2; x <= 4; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            board.Clear(new GridPosition(3, 3));

            Assert.AreEqual((Board.SIZE * Board.SIZE) - 9, LargestEmptyRegion(board));
        }

        [Test]
        public void LargestEmptyRegionSize_WithTooSmallABuffer_IsRejected()
        {
            Board board = new Board();

            Assert.Throws<System.ArgumentException>(
                () => board.LargestEmptyRegionSize(new bool[4], new int[Board.SIZE * Board.SIZE]));
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

        private static int LargestEmptyRegion(Board board)
            => board.LargestEmptyRegionSize(
                new bool[Board.SIZE * Board.SIZE], new int[Board.SIZE * Board.SIZE]);

        private static void FillEntireBoard(Board board)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }
        }
    }
}
