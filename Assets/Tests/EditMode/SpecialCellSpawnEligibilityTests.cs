using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #441: no special cell ever spawns onto, or immediately over, a cell that already is one
    /// special thing — a kind, a lock, a standing reinforced cell, or a cell where a reinforced block or
    /// a lock broke during the same resolution. Covers the board's broken-obstacle marks and every one of
    /// the seven spawn selectors that share <see cref="SpecialCellSpawnEligibility"/>.
    /// </summary>
    public class SpecialCellSpawnEligibilityTests
    {
        private const int COLOUR = 1;
        private const int CLEARED_ROW = 3;
        private const int SEED_COUNT = 40;

        // --- The board's broken-obstacle marks ---

        [Test]
        public void ResolveClears_FinishingOffAReinforcedCell_MarksItAsABrokenObstacle()
        {
            var board = new Board();
            var reinforced = new GridPosition(0, CLEARED_ROW);
            FillRowWithReinforcedCellAt(board, reinforced, hitCount: 1);

            LineClearResolver.ResolveClears(board);

            Assert.IsTrue(board.IsBrokenObstacle(reinforced));
            Assert.IsFalse(board.IsBrokenObstacle(new GridPosition(1, CLEARED_ROW)), "An ordinary cell is no obstacle.");
        }

        [Test]
        public void ResolveClears_ThroughASurvivingReinforcedCell_DoesNotMarkIt()
        {
            var board = new Board();
            var reinforced = new GridPosition(0, CLEARED_ROW);
            FillRowWithReinforcedCellAt(board, reinforced, hitCount: 2);

            LineClearResolver.ResolveClears(board);

            Assert.IsFalse(board.IsBrokenObstacle(reinforced), "Still standing, so not broken.");
        }

        [Test]
        public void ResolveClears_OpeningALockFromBesideIt_MarksItAsABrokenObstacle()
        {
            var board = new Board();
            var lockPosition = new GridPosition(2, CLEARED_ROW + 1);
            board.OccupyLocked(lockPosition, COLOUR, threshold: 1, skin: 0);
            FillRow(board, CLEARED_ROW);

            LineClearResolver.ResolveClears(board);

            Assert.IsFalse(board.IsOccupied(lockPosition), "Precondition: the lock opened.");
            Assert.IsTrue(board.IsBrokenObstacle(lockPosition));
        }

        [Test]
        public void ClearBrokenObstacles_ForgetsEveryMark()
        {
            Board board = ABoardWithABrokenReinforcedCellAt(new GridPosition(0, CLEARED_ROW));

            board.ClearBrokenObstacles();

            Assert.IsFalse(board.IsBrokenObstacle(new GridPosition(0, CLEARED_ROW)));
        }

        [Test]
        public void CloneAndCopyFrom_NeverCarryTheMarks()
        {
            var broken = new GridPosition(0, CLEARED_ROW);
            Board board = ABoardWithABrokenReinforcedCellAt(broken);

            Board clone = board.Clone();
            var copy = new Board();
            copy.CopyFrom(board);

            Assert.IsFalse(clone.IsBrokenObstacle(broken), "Clone starts with no resolution in flight.");
            Assert.IsFalse(copy.IsBrokenObstacle(broken), "CopyFrom empties the resolution in flight.");
        }

        // --- Selectors that occupy a cell the clear emptied ---

        [Test]
        public void VortexSpawnSelector_SkipsTheCellWhereAReinforcedBlockJustBroke()
        {
            Board board = ABoardWithABrokenReinforcedCellAt(new GridPosition(0, CLEARED_ROW));

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(board, new[] { CLEARED_ROW }, Array.Empty<int>());

            Assert.AreEqual(new GridPosition(1, CLEARED_ROW), spawn);
        }

        [Test]
        public void VortexSpawnSelector_OnceTheMarksAreCleared_UsesThatCellAgain()
        {
            Board board = ABoardWithABrokenReinforcedCellAt(new GridPosition(0, CLEARED_ROW));
            board.ClearBrokenObstacles();

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(board, new[] { CLEARED_ROW }, Array.Empty<int>());

            Assert.AreEqual(new GridPosition(0, CLEARED_ROW), spawn);
        }

        [Test]
        public void ChainLightningSpawnSelector_SkipsTheCellWhereAReinforcedBlockJustBroke()
        {
            Board board = ABoardWithABrokenReinforcedCellAt(new GridPosition(0, CLEARED_ROW));

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                board, new[] { CLEARED_ROW }, Array.Empty<int>(), "square_3x3");

            Assert.AreEqual(new GridPosition(1, CLEARED_ROW), spawn);
        }

        [Test]
        public void ExplosiveCoreSpawnSelector_WithTheIntersectionWhereAReinforcedBlockJustBroke_FallsBackToAPlainNeighbour()
        {
            var board = new Board();
            var intersection = new GridPosition(2, CLEARED_ROW);
            var plainNeighbour = new GridPosition(1, CLEARED_ROW - 1);
            FillRowWithReinforcedCellAt(board, intersection, hitCount: 1);
            FillColumnExcept(board, intersection.X, intersection);
            SurroundWithEveryObstacleExcept(board, plainNeighbour);

            LineClearResolver.ResolveClears(board);

            for (int seed = 0; seed < SEED_COUNT; seed++)
            {
                GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                    board, new[] { CLEARED_ROW }, new[] { intersection.X }, new Random(seed));
                Assert.AreEqual(plainNeighbour, spawn, $"seed {seed}");
            }
        }

        [Test]
        public void ExplosiveCoreSpawnSelector_WithOnlyObstaclesAroundAStandingIntersection_SelectsNothing()
        {
            var board = new Board();
            var intersection = new GridPosition(2, CLEARED_ROW);
            board.OccupyReinforced(intersection, COLOUR, 3, 0);
            SurroundWithEveryObstacleExcept(board, new GridPosition(-1, -1));

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new[] { CLEARED_ROW }, new[] { intersection.X }, new Random(1));

            Assert.IsNull(spawn);
        }

        // --- Selectors that convert a block in place ---

        [Test]
        public void EveryConvertingSelector_WithOnlyObstaclesAndOnePlainBlock_OnlyEverSelectsThePlainBlock()
        {
            var board = new Board();
            var plain = new GridPosition(6, 6);
            board.Occupy(plain, COLOUR);
            board.OccupyReinforced(new GridPosition(0, 0), COLOUR, 2, 0);
            board.OccupyLocked(new GridPosition(4, 0), COLOUR, threshold: 2, skin: 0);
            board.OccupyTimer(new GridPosition(0, 4), COLOUR, 5);
            board.Occupy(new GridPosition(4, 4), COLOUR);
            board.SetSpecialKind(new GridPosition(4, 4), SpecialCellKind.Vortex);

            for (int seed = 0; seed < SEED_COUNT; seed++)
            {
                Assert.AreEqual(plain, LaserSpawnSelector.SelectSpawnPosition(board, new Random(seed)), $"laser, seed {seed}");
                Assert.AreEqual(plain, ScoreGemSpawnSelector.SelectSpawnPosition(board, new Random(seed)), $"gem, seed {seed}");
                Assert.AreEqual(plain, CoinSpawnSelector.SelectSpawnPosition(board, new Random(seed)), $"coin, seed {seed}");
                Assert.AreEqual(plain, PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(seed)), $"perfect match, seed {seed}");
            }
        }

        [Test]
        public void EveryConvertingSelector_WithOnlyAReinforcedCell_SelectsNothing()
        {
            var board = new Board();
            board.OccupyReinforced(new GridPosition(3, 3), COLOUR, 2, 0);

            Assert.IsNull(LaserSpawnSelector.SelectSpawnPosition(board, new Random(1)));
            Assert.IsNull(ScoreGemSpawnSelector.SelectSpawnPosition(board, new Random(1)));
            Assert.IsNull(CoinSpawnSelector.SelectSpawnPosition(board, new Random(1)));
            Assert.IsNull(PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(1)));
        }

        [Test]
        public void EveryConvertingSelector_WithOnlyALockedCell_SelectsNothing()
        {
            var board = new Board();
            board.OccupyLocked(new GridPosition(3, 3), COLOUR, threshold: 2, skin: 0);

            Assert.IsNull(LaserSpawnSelector.SelectSpawnPosition(board, new Random(1)));
            Assert.IsNull(ScoreGemSpawnSelector.SelectSpawnPosition(board, new Random(1)));
            Assert.IsNull(CoinSpawnSelector.SelectSpawnPosition(board, new Random(1)));
            Assert.IsNull(PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(1)));
        }

        // --- Helpers ---

        private static Board ABoardWithABrokenReinforcedCellAt(GridPosition reinforced)
        {
            var board = new Board();
            FillRowWithReinforcedCellAt(board, reinforced, hitCount: 1);
            LineClearResolver.ResolveClears(board);
            return board;
        }

        private static void FillRow(Board board, int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, y), COLOUR);
            }
        }

        private static void FillRowWithReinforcedCellAt(Board board, GridPosition reinforced, int hitCount)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, reinforced.Y);
                if (position.Equals(reinforced))
                {
                    board.OccupyReinforced(position, COLOUR, hitCount, 0);
                    continue;
                }

                board.Occupy(position, COLOUR);
            }
        }

        private static void FillColumnExcept(Board board, int x, GridPosition skip)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                var position = new GridPosition(x, y);
                if (!position.Equals(skip))
                {
                    board.Occupy(position, COLOUR);
                }
            }
        }

        /// <summary>Fills the four diagonal neighbours of (2, <see cref="CLEARED_ROW"/>) — the only ring
        /// cells a row-and-column clear through it leaves standing — with a plain block at
        /// <paramref name="plain"/> and a different obstacle on each of the others.</summary>
        private static void SurroundWithEveryObstacleExcept(Board board, GridPosition plain)
        {
            var diagonals = new[]
            {
                new GridPosition(1, CLEARED_ROW - 1),
                new GridPosition(3, CLEARED_ROW - 1),
                new GridPosition(1, CLEARED_ROW + 1),
                new GridPosition(3, CLEARED_ROW + 1),
            };

            for (int diagonalIndex = 0; diagonalIndex < diagonals.Length; diagonalIndex++)
            {
                GridPosition position = diagonals[diagonalIndex];
                if (position.Equals(plain))
                {
                    board.Occupy(position, COLOUR);
                    continue;
                }

                switch (diagonalIndex % 3)
                {
                    case 0:
                        board.OccupyReinforced(position, COLOUR, 3, 0);
                        break;
                    case 1:
                        board.OccupyLocked(position, COLOUR, threshold: 4, skin: 0);
                        break;
                    default:
                        board.Occupy(position, COLOUR);
                        board.SetSpecialKind(position, SpecialCellKind.ScoreGem);
                        break;
                }
            }
        }
    }
}
