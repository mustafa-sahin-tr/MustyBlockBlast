using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the board-shape foundation: a <see cref="BoardShape"/>'s hole set and dimensions, the
    /// line-clear rule restated as "every PLAYABLE cell of the line is filled",
    /// <see cref="PlacementRules.CanPlace"/> refusing a hole, and both flood-fills treating a hole as a
    /// wall rather than as room.
    /// <para>
    /// The last fixture here is the regression guard AC6 asks for: a board built the way every existing
    /// level builds one — <c>new Board()</c> — must be indistinguishable from the 8x8 hole-free board
    /// this project has always had. The rest of the EditMode suite is that guard's real body; these
    /// assertions pin the specific properties the shape work could have moved.
    /// </para>
    /// </summary>
    public class BoardShapeTests
    {
        /// <summary>A 5x4 board whose bottom-right corner cell is a hole. Non-square on purpose: a
        /// square board cannot catch a width/height mix-up.</summary>
        private static BoardShape NotchedShape()
            => new BoardShape(5, 4, new List<GridPosition> { new GridPosition(4, 0) });

        private static Piece SingleCell()
            => new Piece("test_single", new List<GridPosition> { new GridPosition(0, 0) });

        [Test]
        public void StandardShape_IsEightBySquareWithNoHoles()
        {
            BoardShape shape = BoardShape.Standard;

            Assert.AreEqual(Board.SIZE, shape.Width);
            Assert.AreEqual(Board.SIZE, shape.Height);
            Assert.IsFalse(shape.HasHoles);
            Assert.AreEqual(Board.SIZE * Board.SIZE, shape.PlayableCellCount);
        }

        [Test]
        public void ParameterlessBoard_UsesTheStandardShape()
        {
            Board board = new Board();

            Assert.AreSame(BoardShape.Standard, board.Shape);
            Assert.AreEqual(Board.SIZE, board.Width);
            Assert.AreEqual(Board.SIZE, board.Height);
            Assert.AreEqual(Board.SIZE * Board.SIZE, board.PlayableCellCount);
        }

        [Test]
        public void Shape_ReportsHolesAndDimensions()
        {
            BoardShape shape = NotchedShape();

            Assert.AreEqual(5, shape.Width);
            Assert.AreEqual(4, shape.Height);
            Assert.IsTrue(shape.HasHoles);
            Assert.AreEqual((5 * 4) - 1, shape.PlayableCellCount);

            Assert.IsTrue(shape.IsHole(new GridPosition(4, 0)));
            Assert.IsFalse(shape.IsPlayable(new GridPosition(4, 0)));
            Assert.IsTrue(shape.IsPlayable(new GridPosition(0, 0)));

            // Off-board is not a hole — it is simply not on the board, and IsPlayable is the single
            // predicate that refuses both.
            Assert.IsFalse(shape.IsHole(new GridPosition(5, 0)));
            Assert.IsFalse(shape.IsPlayable(new GridPosition(5, 0)));
        }

        [Test]
        public void Shape_RejectsAHoleOutsideItsRectangle()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new BoardShape(4, 4, new List<GridPosition> { new GridPosition(4, 0) }));
        }

        [Test]
        public void CanPlace_RefusesAHoleCell()
        {
            Board board = new Board(NotchedShape());

            Assert.IsFalse(PlacementRules.CanPlace(board, SingleCell(), new GridPosition(4, 0)));
            Assert.IsTrue(PlacementRules.CanPlace(board, SingleCell(), new GridPosition(3, 0)));
        }

        [Test]
        public void RowWithAHole_IsFullWithFewerFilledCellsThanItsWidth()
        {
            Board board = new Board(NotchedShape());

            // Row 0 has five cells but only four playable ones.
            for (int x = 0; x < 4; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            Assert.IsTrue(board.IsRowFull(0), "A row shortened by a hole clears on its playable cells.");
            Assert.IsFalse(board.IsRowFull(1), "An untouched full-width row is not full.");
        }

        [Test]
        public void RowWithAHole_ClearsThroughTheResolverAndReportsOnlyItsPlayableCells()
        {
            Board board = new Board(NotchedShape());
            for (int x = 0; x < 4; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(1, result.LineCount);
            Assert.AreEqual(0, result.ClearedRows[0]);
            Assert.AreEqual(4, result.ClearedCellCount, "Four playable cells, not the row's width of five.");
            Assert.IsTrue(board.IsRowEmpty(0));
        }

        [Test]
        public void ColumnMadeEntirelyOfHoles_IsNeverFull()
        {
            // Column 2 of a 3x3 board is nothing but holes: no placement could ever complete it, so
            // reporting it full would have it "clear" on every single pass forever.
            var holes = new List<GridPosition>
            {
                new GridPosition(2, 0),
                new GridPosition(2, 1),
                new GridPosition(2, 2),
            };

            Board board = new Board(new BoardShape(3, 3, holes));

            Assert.IsFalse(board.IsColumnFull(2));

            for (int y = 0; y < 3; y++)
            {
                board.Occupy(new GridPosition(0, y), 1);
                board.Occupy(new GridPosition(1, y), 1);
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            // Every playable cell is filled, so both remaining columns and all three (two-cell) rows
            // clear — but the all-hole column contributes nothing.
            Assert.AreEqual(2, result.ClearedColumns.Count);
            Assert.AreEqual(3, result.ClearedRows.Count);
            Assert.AreEqual(6, result.ClearedCellCount);
        }

        [Test]
        public void HasIsolatedEmptyCells_TreatsAHoleAsAWall()
        {
            // A 4x3 board whose column 1 is all holes. Every playable cell still reaches an edge, so
            // nothing is trapped yet.
            var holes = new List<GridPosition>
            {
                new GridPosition(1, 0),
                new GridPosition(1, 1),
                new GridPosition(1, 2),
            };

            Board board = new Board(new BoardShape(4, 3, holes));

            Assert.IsFalse(board.HasIsolatedEmptyCells());

            // Wall (2,1) in. It is not itself a border cell, its left neighbour is a hole, and the
            // other three are now filled — so it can only count as trapped if the flood-fill refuses to
            // walk through the hole.
            board.Occupy(new GridPosition(2, 0), 1);
            board.Occupy(new GridPosition(2, 2), 1);
            board.Occupy(new GridPosition(3, 1), 1);

            Assert.IsTrue(board.HasIsolatedEmptyCells());
        }

        [Test]
        public void HolesAreNotCountedAsAnIsolatedEmptyCell()
        {
            // A single hole ringed by blocks is unreachable from the edge — and must still not count as
            // a trapped pocket, because it was never room the player had.
            Board board = new Board(new BoardShape(3, 3, new List<GridPosition> { new GridPosition(1, 1) }));

            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    var cell = new GridPosition(x, y);
                    if (!board.IsHole(cell))
                    {
                        board.Occupy(cell, 1);
                    }
                }
            }

            Assert.IsFalse(board.HasIsolatedEmptyCells());
        }

        [Test]
        public void LargestEmptyRegionSize_CountsOnlyPlayableCellsAndSplitsAtHoles()
        {
            var holes = new List<GridPosition>
            {
                new GridPosition(1, 0),
                new GridPosition(1, 1),
                new GridPosition(1, 2),
            };

            Board board = new Board(new BoardShape(4, 3, holes));
            var visited = new bool[board.CellCount];
            var stack = new int[board.CellCount];

            // Column 1 is a wall of holes: the left region is 1 x 3 = 3 cells, the right is 2 x 3 = 6.
            Assert.AreEqual(6, board.LargestEmptyRegionSize(visited, stack));

            // Fill the whole right side and the largest region becomes the left one.
            for (int y = 0; y < 3; y++)
            {
                board.Occupy(new GridPosition(2, y), 1);
                board.Occupy(new GridPosition(3, y), 1);
            }

            Assert.AreEqual(3, board.LargestEmptyRegionSize(visited, stack));
        }

        [Test]
        public void MoveAvailability_ScansANonSquareBoardAndRefusesHoles()
        {
            // A 2x1 board with one hole leaves exactly one playable cell.
            Board board = new Board(new BoardShape(2, 1, new List<GridPosition> { new GridPosition(1, 0) }));

            Assert.IsTrue(MoveAvailability.CanPlaceAnywhere(board, SingleCell()));

            board.Occupy(new GridPosition(0, 0), 1);

            Assert.IsFalse(
                MoveAvailability.CanPlaceAnywhere(board, SingleCell()),
                "The only other cell is a hole, so nothing fits.");
        }

        [Test]
        public void GhostFitSearch_FindsAMoveOnAHoledNonSquareBoard()
        {
            Board board = new Board(NotchedShape());
            var search = new GhostFitSearch();
            var dock = new List<Piece> { SingleCell() };

            Assert.IsTrue(search.TryFindBestMove(board, dock, false, out GhostFitMove move));
            Assert.IsTrue(board.IsPlayable(move.Anchor));

            // Called twice on the same board: the second call must reuse the scratch it grew on the
            // first, and must not be confused by it.
            Assert.IsTrue(search.TryFindBestMove(board, dock, false, out GhostFitMove repeat));
            Assert.AreEqual(move, repeat);
        }

        [Test]
        public void JokerFill_RefusesAHoleAndStillClearsAShortenedRow()
        {
            Board board = new Board(NotchedShape());

            Assert.IsFalse(
                JokerFillResolver.ResolveFill(board, new GridPosition(4, 0), 1).Filled,
                "A hole is refused exactly as an off-board target is.");

            for (int x = 0; x < 3; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            JokerFillResult result = JokerFillResolver.ResolveFill(board, new GridPosition(3, 0), 1);

            Assert.IsTrue(result.Filled);
            Assert.AreEqual(1, result.ClearedRows.Count);
            Assert.AreEqual(4, result.ClearedCellCount, "The hole is not one of the cells that cleared.");
        }

        [Test]
        public void PowerUpTargetCells_DropHolesFromEveryFootprint()
        {
            BoardShape shape = NotchedShape();
            var buffer = new List<GridPosition>(PowerUpTargetCells.MAX_TARGET_CELLS);

            // Row 0 is five wide with one hole.
            Assert.AreEqual(4, PowerUpTargetCells.ForRow(shape, 0, buffer).Count);

            // Column 4 is four tall with one hole.
            Assert.AreEqual(3, PowerUpTargetCells.ForColumn(shape, 4, buffer).Count);

            // A corner-clamped 3x3 around the hole itself covers four cells, one of which is the hole.
            Assert.AreEqual(3, PowerUpTargetCells.ForBomb(shape, new GridPosition(4, 0), buffer).Count);

            // The single-cell reticles refuse the hole outright, so no aim preview can tint it.
            Assert.AreEqual(0, PowerUpTargetCells.ForJoker(shape, new GridPosition(4, 0), buffer).Count);
            Assert.AreEqual(0, PowerUpTargetCells.ForColorCleanser(shape, new GridPosition(4, 0), buffer).Count);
            Assert.AreEqual(0, PowerUpTargetCells.ForDemolitionHammer(shape, new GridPosition(4, 0), buffer).Count);

            // Line indices are bounded by the right axis: a 5-wide, 4-tall board has rows 0..3 and
            // columns 0..4.
            Assert.IsFalse(PowerUpTargetCells.IsValidRowIndex(shape, 4));
            Assert.IsTrue(PowerUpTargetCells.IsValidColumnIndex(shape, 4));
        }

        [Test]
        public void StandardBoard_ClearsAFullRowExactlyAsItAlwaysHas()
        {
            Board board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 3), 1);
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(1, result.LineCount);
            Assert.AreEqual(Board.SIZE, result.ClearedCellCount);
            Assert.AreEqual(1, result.MonochromeLineCount);
            Assert.IsTrue(board.IsEmpty());
        }

        [Test]
        public void StandardBoard_ClearsARowAndColumnCountingTheIntersectionOnce()
        {
            Board board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 3), 1);
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                if (y != 3)
                {
                    board.Occupy(new GridPosition(5, y), 1);
                }
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(2, result.LineCount);
            Assert.AreEqual((Board.SIZE * 2) - 1, result.ClearedCellCount);
        }
    }
}
