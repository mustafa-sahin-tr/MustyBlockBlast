using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers when a clutch clear earns a vortex and where it lands: the occupancy threshold and its
    /// boundary, the cell of the clear it converts, and the three ways the answer is "spawn nothing" —
    /// none of which is an error.
    /// </summary>
    public class VortexSpawnSelectorTests
    {
        /// <summary>More than 80% of the standard board's 64 playable cells.</summary>
        private const int CROWDED = 52;

        /// <summary>The fullest board that still does not qualify: 51 of 64 is 79.7%, and 80% of 64 —
        /// 51.2 cells — is not a board any number of blocks can produce, so this is the boundary.</summary>
        private const int BELOW_THRESHOLD = 51;

        /// <summary>AC1/AC3: a line cleared on a crowded board always spawns one, with no roll.</summary>
        [Test]
        public void SelectSpawnPosition_WithAClearOnACrowdedBoard_SpawnsOnACellTheClearEmptied()
        {
            Board board = BoardWithClearedRow(3);

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], CROWDED);

            Assert.IsTrue(spawn.HasValue);
            Assert.AreEqual(new GridPosition(0, 3), spawn.Value);
        }

        /// <summary>AC3: deterministic — the same clear resolves to the same cell every time.</summary>
        [Test]
        public void SelectSpawnPosition_CalledTwiceWithTheSameClear_ReturnsTheSameCell()
        {
            Board board = BoardWithClearedRow(3);

            GridPosition? first = VortexSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], CROWDED);
            GridPosition? second = VortexSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], CROWDED);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void SelectSpawnPosition_WithOnlyAColumnCleared_SpawnsOnACellOfThatColumn()
        {
            var board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    if (x != 5)
                    {
                        board.Occupy(new GridPosition(x, y), 1);
                    }
                }
            }

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, new int[0], new[] { 5 }, CROWDED);

            Assert.AreEqual(new GridPosition(5, 0), spawn.Value);
        }

        /// <summary>The threshold is strictly greater than: the fullest board below 80% has not earned
        /// one, so the boundary belongs to the ordinary case.</summary>
        [Test]
        public void SelectSpawnPosition_WithOccupancyAtTheBoundary_SpawnsNothing()
        {
            Board board = BoardWithClearedRow(3);

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], BELOW_THRESHOLD);

            Assert.IsNull(spawn);
        }

        [Test]
        public void SelectSpawnPosition_WithAnEmptyBoardBeforeTheClear_SpawnsNothing()
        {
            Board board = BoardWithClearedRow(3);

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], 0);

            Assert.IsNull(spawn);
        }

        /// <summary>The reward is for a clear, not for a crowded board: a placement that completed no
        /// line earns nothing however full the board was.</summary>
        [Test]
        public void SelectSpawnPosition_WithNothingCleared_SpawnsNothing()
        {
            var board = new Board();

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, new int[0], new int[0], Board.SIZE * Board.SIZE);

            Assert.IsNull(spawn);
        }

        [Test]
        public void SelectSpawnPosition_WithNullLineLists_SpawnsNothing()
        {
            var board = new Board();

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, null, null, Board.SIZE * Board.SIZE);

            Assert.IsNull(spawn);
        }

        /// <summary>AC4: no valid cell to spawn on is silently skipped, not an error. Here every cell of
        /// the cleared row has been refilled by a later cascade phase.</summary>
        [Test]
        public void SelectSpawnPosition_WithNoFreeCellInTheClearedLine_SpawnsNothingAndDoesNotThrow()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 3), 1);
            }

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], CROWDED);

            Assert.IsNull(spawn);
        }

        /// <summary>A row made entirely of holes has no cell that could carry the tile, so the reward is
        /// skipped rather than placed on something a block can never stand on.</summary>
        [Test]
        public void SelectSpawnPosition_WithAClearedLineOfHoles_SpawnsNothing()
        {
            var holes = new GridPosition[Board.SIZE];
            for (int x = 0; x < Board.SIZE; x++)
            {
                holes[x] = new GridPosition(x, 3);
            }

            var board = new Board(new BoardShape(Board.SIZE, Board.SIZE, holes));

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], CROWDED);

            Assert.IsNull(spawn);
        }

        /// <summary>A cell that already is a core or a laser is left as the one it is; the scan moves on
        /// to the next cell of the clear rather than overwriting it.</summary>
        [Test]
        public void SelectSpawnPosition_WithACellAlreadyCarryingAKind_SkipsIt()
        {
            Board board = BoardWithClearedRow(3);
            board.SetSpecialKind(new GridPosition(0, 3), SpecialCellKind.Laser);

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], CROWDED);

            Assert.AreEqual(new GridPosition(1, 3), spawn.Value);
        }

        /// <summary>The board as a clear left it: every cell filled except the one row that cleared.</summary>
        private static Board BoardWithClearedRow(int clearedRow)
        {
            var board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                if (y == clearedRow)
                {
                    continue;
                }

                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            return board;
        }
    }
}
