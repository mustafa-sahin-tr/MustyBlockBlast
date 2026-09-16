using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers when a big-piece clear earns a chain lightning tile and where it lands: which three
    /// shapes qualify, that the clear is as necessary as the shape, the cell of the clear it converts,
    /// and the ways the answer is "spawn nothing" — none of which is an error.
    /// </summary>
    public class ChainLightningSpawnSelectorTests
    {
        /// <summary>AC1/AC3: a 3x3 square that also cleared a line always spawns one, with no roll.</summary>
        [Test]
        public void SelectSpawnPosition_WithASquare3X3ThatCleared_SpawnsOnACellTheClearEmptied()
        {
            Board board = BoardWithClearedRow(3);

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "square_3x3");

            Assert.IsTrue(spawn.HasValue);
            Assert.AreEqual(new GridPosition(0, 3), spawn.Value);
        }

        /// <summary>Both orientations of the 1x5 bar qualify: the catalog stores each as its own entry,
        /// so a rule that named only one would reward the same move half the time.</summary>
        [Test]
        public void SelectSpawnPosition_WithAHorizontal1X5ThatCleared_Spawns()
        {
            Board board = BoardWithClearedRow(3);

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "line_h5");

            Assert.AreEqual(new GridPosition(0, 3), spawn.Value);
        }

        [Test]
        public void SelectSpawnPosition_WithAVertical1X5ThatCleared_Spawns()
        {
            Board board = BoardWithClearedRow(3);

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "line_v5");

            Assert.AreEqual(new GridPosition(0, 3), spawn.Value);
        }

        /// <summary>AC3: deterministic — the same clear by the same shape resolves to the same cell every
        /// time. Only the targets a destroyed tile takes are random; whether one appears never is.</summary>
        [Test]
        public void SelectSpawnPosition_CalledTwiceWithTheSameClear_ReturnsTheSameCell()
        {
            Board board = BoardWithClearedRow(3);

            GridPosition? first = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "square_3x3");
            GridPosition? second = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "square_3x3");

            Assert.AreEqual(first, second);
        }

        /// <summary>The reward is for the two awkward big pieces specifically, not for size: the 1x4 bar
        /// and the 2x2 square are easier asks and earn nothing.</summary>
        [Test]
        public void SelectSpawnPosition_WithANonQualifyingShape_SpawnsNothing()
        {
            Board board = BoardWithClearedRow(3);

            Assert.IsNull(ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "line_h4"));
            Assert.IsNull(ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "square_2x2"));
            Assert.IsNull(ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "single_1x1"));
            Assert.IsNull(ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "corner3_bl"));
        }

        [Test]
        public void SelectSpawnPosition_WithANullPieceId_SpawnsNothing()
        {
            Board board = BoardWithClearedRow(3);

            Assert.IsNull(ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], null));
        }

        /// <summary>The shape is not enough on its own: the reward is for landing one of them
        /// <em>and</em> clearing with it.</summary>
        [Test]
        public void SelectSpawnPosition_WithAQualifyingShapeButNothingCleared_SpawnsNothing()
        {
            var board = new Board();

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new int[0], new int[0], "square_3x3");

            Assert.IsNull(spawn);
        }

        [Test]
        public void SelectSpawnPosition_WithNullLineLists_SpawnsNothing()
        {
            var board = new Board();

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, null, null, "square_3x3");

            Assert.IsNull(spawn);
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

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new int[0], new[] { 5 }, "line_v5");

            Assert.AreEqual(new GridPosition(5, 0), spawn.Value);
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

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "square_3x3");

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

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "square_3x3");

            Assert.IsNull(spawn);
        }

        /// <summary>A cell that already is a core, a laser or a vortex is left as the one it is; the scan
        /// moves on to the next cell of the clear rather than overwriting it.</summary>
        [Test]
        public void SelectSpawnPosition_WithACellAlreadyCarryingAKind_SkipsIt()
        {
            Board board = BoardWithClearedRow(3);
            board.SetSpecialKind(new GridPosition(0, 3), SpecialCellKind.Vortex);

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], "square_3x3");

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
