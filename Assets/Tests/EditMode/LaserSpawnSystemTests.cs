using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the spawn trigger: the streak that earns a laser converts exactly one cell, exactly once,
    /// and a board with nothing to convert is silently skipped.
    /// </summary>
    public class LaserSpawnSystemTests
    {
        private const int SPAWN_STREAK = 4;

        private ScoreModel _scoreModel;
        private BoardModel _boardModel;
        private LaserSpawnSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _scoreModel = new ScoreModel();
            _boardModel = new BoardModel();
            _system = new LaserSpawnSystem(_scoreModel, _boardModel, seed: 1);
        }

        [TearDown]
        public void DisposeSystem() => _system.Dispose();

        /// <summary>AC1 and AC3: reaching the streak always converts a cell, with no roll deciding
        /// whether the reward appears at all.</summary>
        [Test]
        public void OnReachingTheSpawnStreak_ConvertsOneOccupiedCellIntoALaser()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(1, CountLasers());
        }

        [Test]
        public void OnReachingTheSpawnStreak_ConvertsACellThatHoldsABlock()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK);

            GridPosition laser = FindTheLaser();
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(laser), "A laser has to sit on a block.");
        }

        /// <summary>Converted in place: a placement that earned the streak must not have its own board
        /// state quietly changed by its reward.</summary>
        [Test]
        public void OnReachingTheSpawnStreak_LeavesOccupancyAndColoursAlone()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK);

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(1, _boardModel.GetCell(new GridPosition(x, 3)), $"({x}, 3) colour.");
            }

            Assert.AreEqual(Board.SIZE, _boardModel.Board.OccupiedCellCount());
        }

        [Test]
        public void OnReachingTheSpawnStreak_AnnouncesTheNewKindToTheView()
        {
            OccupyRow(3);

            GridPosition announced = default;
            SpecialCellKind announcedKind = SpecialCellKind.None;
            int raised = 0;
            _boardModel.SpecialKindChanged += (position, kind) =>
            {
                announced = position;
                announcedKind = kind;
                raised++;
            };

            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(1, raised);
            Assert.AreEqual(SpecialCellKind.Laser, announcedKind);
            Assert.AreEqual(announced, FindTheLaser());
        }

        [Test]
        public void BelowTheSpawnStreak_ConvertsNothing()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK - 1);

            Assert.AreEqual(0, CountLasers());
        }

        /// <summary>The edge-triggered claim: the threshold is crossed once, so continuing to clear must
        /// not hand out a laser per placement for the rest of the run.</summary>
        [Test]
        public void ContinuingPastTheSpawnStreak_ConvertsNothingFurther()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK + 4);

            Assert.AreEqual(1, CountLasers(), "Only the placement that crossed the threshold earns one.");
        }

        /// <summary>Breaking the streak and rebuilding it is a second achievement, and earns a second
        /// laser — which is the intent, not a leak.</summary>
        [Test]
        public void RebuildingTheStreakAfterItBroke_ConvertsAnotherCell()
        {
            FillBoard();

            // Counted as announcements rather than as lasers on the board: the two rolls are independent
            // and may land on the same cell, which is a legitimate outcome and not a missing spawn.
            int raised = 0;
            _boardModel.SpecialKindChanged += (position, kind) => raised++;

            AdvanceStreakTo(SPAWN_STREAK);
            _scoreModel.Streak.Value = 0;
            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(2, raised);
        }

        /// <summary>AC4: no occupied cell to convert — a streak-completing placement that also emptied
        /// the board — is silently skipped.</summary>
        [Test]
        public void OnReachingTheSpawnStreak_WithAnEmptyBoard_ConvertsNothing()
        {
            int raised = 0;
            _boardModel.SpecialKindChanged += (position, kind) => raised++;

            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(0, CountLasers());
            Assert.AreEqual(0, raised, "Nothing happened, so nothing is announced.");
        }

        /// <summary>Construction subscribes and is immediately handed the current streak. That streak is
        /// 0 at construction and at every run start — never the threshold — so merely starting to listen
        /// can never spawn anything.</summary>
        [Test]
        public void Constructing_SpawnsNothingByItself()
        {
            OccupyRow(3);

            var freshSystem = new LaserSpawnSystem(_scoreModel, _boardModel, seed: 2);

            Assert.AreEqual(0, _scoreModel.Streak.Value);
            Assert.AreEqual(0, CountLasers());
            freshSystem.Dispose();
        }

        [Test]
        public void AfterDispose_ReachingTheSpawnStreak_ConvertsNothing()
        {
            OccupyRow(3);
            _system.Dispose();

            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(0, CountLasers());
        }

        /// <summary>Steps the streak one at a time, the only way <c>ScoreSystem</c> ever moves it.</summary>
        private void AdvanceStreakTo(int streak)
        {
            for (int value = _scoreModel.Streak.Value + 1; value <= streak; value++)
            {
                _scoreModel.Streak.Value = value;
            }
        }

        private void OccupyRow(int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                _boardModel.Occupy(new GridPosition(x, y), 1);
            }
        }

        private void FillBoard()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                OccupyRow(y);
            }
        }

        private int CountLasers()
        {
            int count = 0;
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    if (_boardModel.GetSpecialKind(new GridPosition(x, y)) == SpecialCellKind.Laser)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private GridPosition FindTheLaser()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_boardModel.GetSpecialKind(position) == SpecialCellKind.Laser)
                    {
                        return position;
                    }
                }
            }

            Assert.Fail("No laser was spawned.");
            return default;
        }
    }
}
