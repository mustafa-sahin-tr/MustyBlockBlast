using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>Covers <see cref="Board.IsEmpty"/> (a fresh board starts empty, occupying any cell makes
    /// it non-empty, clearing it returns to empty), <see cref="Board.HasIsolatedEmptyCells"/> (the
    /// flood-fill correctly distinguishes a walled-off hole from one with any path to the edge, and
    /// treats every border cell as inherently reachable), <see cref="Board.LargestEmptyRegionSize"/> (the
    /// second flood-fill, which counts connected components rather than answering a reachability
    /// question), <see cref="Board.CollectEnclosedEmptyIslands"/> (the third, component-based flood-fill
    /// issue #349 added, which — unlike <see cref="Board.HasIsolatedEmptyCells"/> — also catches a pocket
    /// that happens to sit on the board's last row or column) and <see cref="Board.IsCenterCoreEmpty"/>.</summary>
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
        public void CollectEnclosedEmptyIslands_OnAnEmptyBoard_FindsNoIsland()
        {
            // Every empty cell forms one single connected region — trivially "the largest" — so nothing
            // is reported, whatever shape that region happens to be.
            Board board = new Board();
            var results = new List<GridPosition>();

            board.CollectEnclosedEmptyIslands(results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void CollectEnclosedEmptyIslands_WithOneOpenRegionOnly_FindsNoIslandEvenIfItTouchesTheEdge()
        {
            // Everything occupied except an open corridor from the middle out to the edge: one region,
            // edge-touching or not, so there is nothing smaller for it to be compared against.
            Board board = new Board();
            FillEntireBoard(board);
            for (int x = 0; x <= 3; x++)
            {
                board.Clear(new GridPosition(x, 3));
            }

            var results = new List<GridPosition>();
            board.CollectEnclosedEmptyIslands(results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void CollectEnclosedEmptyIslands_WithAWalledOffInteriorPocket_ReportsExactlyItsCells()
        {
            // The 3x3 block around (3,3) is occupied except its centre: a one-cell pocket with no path
            // to the rest of the board's open space, which is everything outside the block.
            Board board = new Board();
            for (int y = 2; y <= 4; y++)
            {
                for (int x = 2; x <= 4; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            GridPosition pocket = new GridPosition(3, 3);
            board.Clear(pocket);

            var results = new List<GridPosition>();
            board.CollectEnclosedEmptyIslands(results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(pocket, results[0]);
        }

        /// <summary>
        /// Issue #349 AC2: a pocket on the board's last row/column, boxed in from every in-board side, is
        /// still an island — the case <see cref="Board.HasIsolatedEmptyCells"/> misses because it treats
        /// every border-cell as inherently reachable regardless of what surrounds it (see
        /// <see cref="EmptyCellOnTheBorder_IsNeverIsolated"/> above). Here the pocket sits in the last row
        /// (y = 7) and includes the last column too (x = 7), and the rest of the board is occupied except
        /// one larger, unambiguously-the-main-region opening elsewhere, so the pocket is not "the
        /// largest" and is reported.
        /// </summary>
        [Test]
        public void CollectEnclosedEmptyIslands_WithAPocketOnTheLastRowAndColumn_StillReportsIt()
        {
            Board board = new Board();
            FillEntireBoard(board);

            // The main open area: a 4x4 block, plus a two-cell corridor out to the left edge so it is
            // genuinely reachable from the border — otherwise HasIsolatedEmptyCells would (correctly, by
            // its own definition) flag this block itself as isolated, and the assertion below would prove
            // nothing about the pocket specifically.
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    board.Clear(new GridPosition(x, y));
                }
            }

            board.Clear(new GridPosition(0, 2));
            board.Clear(new GridPosition(1, 2));

            // The dead pocket: the last three cells of the last row. Boxed in above (row 6 is still
            // fully occupied) and to the left (column 4, row 7 is occupied); below and to the right of
            // (7, 7) is off-board, which is a wall exactly as an occupied cell is.
            var pocket = new List<GridPosition>
            {
                new GridPosition(5, 7), new GridPosition(6, 7), new GridPosition(7, 7),
            };
            for (int i = 0; i < pocket.Count; i++)
            {
                board.Clear(pocket[i]);
            }

            Assert.IsFalse(
                board.HasIsolatedEmptyCells(),
                "The border-seeded flood-fill waves this pocket through for sitting on the edge.");

            var results = new List<GridPosition>();
            board.CollectEnclosedEmptyIslands(results);

            Assert.AreEqual(pocket.Count, results.Count);
            for (int i = 0; i < pocket.Count; i++)
            {
                Assert.Contains(pocket[i], results);
            }
        }

        [Test]
        public void CollectEnclosedEmptyIslands_WithTwoSeparatePockets_ReportsBothButNotTheMainRegion()
        {
            Board board = new Board();
            FillEntireBoard(board);

            // The main open area.
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    board.Clear(new GridPosition(x, y));
                }
            }

            GridPosition firstPocket = new GridPosition(0, 0);
            GridPosition secondPocket = new GridPosition(0, 7);
            board.Clear(firstPocket);
            board.Clear(secondPocket);

            var results = new List<GridPosition>();
            board.CollectEnclosedEmptyIslands(results);

            Assert.AreEqual(2, results.Count);
            Assert.Contains(firstPocket, results);
            Assert.Contains(secondPocket, results);
        }

        [Test]
        public void CollectEnclosedEmptyIslands_DoesNotClearResultsFirst()
        {
            // Matches the append convention of CollectRowCells/CollectColumnCells.
            Board board = new Board();
            var results = new List<GridPosition> { new GridPosition(0, 0) };

            board.CollectEnclosedEmptyIslands(results);

            Assert.AreEqual(1, results.Count, "The pre-existing entry must survive.");
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
