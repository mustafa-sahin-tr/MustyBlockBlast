using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the spawn trigger while laser formation is paused (issue #399): the streak that used to
    /// earn a laser now earns nothing, at the threshold, past it, and on a rebuilt streak. The System
    /// stays wired and disposable so a future revival only has to flip its gate; these tests then
    /// become the ones to invert.
    /// </summary>
    public class LaserSpawnSystemTests
    {
        /// <summary>The streak that earned a laser before #399 paused formation.</summary>
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

        /// <summary>AC1: the old threshold no longer converts anything, even with a full row of
        /// candidates to convert.</summary>
        [Test]
        public void OnReachingTheOldSpawnStreak_ConvertsNothing()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(0, CountLasers());
        }

        /// <summary>AC1: nothing is announced either — the gate sits before the selector, so no
        /// special-kind change is ever raised from this System.</summary>
        [Test]
        public void OnReachingTheOldSpawnStreak_AnnouncesNothingToTheView()
        {
            OccupyRow(3);

            int raised = 0;
            _boardModel.SpecialKindChanged += (position, kind) => raised++;

            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(0, raised);
        }

        /// <summary>AC1: the board is left exactly as it was — colours and occupancy untouched.</summary>
        [Test]
        public void OnReachingTheOldSpawnStreak_LeavesTheBoardAlone()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK);

            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, 3);
                Assert.AreEqual(1, _boardModel.GetCell(position), $"({x}, 3) colour.");
                Assert.AreEqual(SpecialCellKind.None, _boardModel.GetSpecialKind(position), $"({x}, 3) kind.");
            }

            Assert.AreEqual(Board.SIZE, _boardModel.Board.OccupiedCellCount());
        }

        [Test]
        public void BelowTheOldSpawnStreak_ConvertsNothing()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK - 1);

            Assert.AreEqual(0, CountLasers());
        }

        /// <summary>AC1: "under any streak value" — walk the streak well past the old threshold and
        /// nothing is ever handed out.</summary>
        [Test]
        public void ContinuingPastTheOldSpawnStreak_ConvertsNothing()
        {
            OccupyRow(3);

            AdvanceStreakTo(SPAWN_STREAK + 4);

            Assert.AreEqual(0, CountLasers());
        }

        /// <summary>Breaking and rebuilding the streak used to be a second achievement; with formation
        /// paused it is nothing twice.</summary>
        [Test]
        public void RebuildingTheStreakAfterItBroke_ConvertsNothing()
        {
            FillBoard();

            int raised = 0;
            _boardModel.SpecialKindChanged += (position, kind) => raised++;

            AdvanceStreakTo(SPAWN_STREAK);
            _scoreModel.Streak.Value = 0;
            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(0, raised);
        }

        /// <summary>Construction subscribes and is immediately handed the current streak, which is 0 —
        /// and now gated regardless — so merely starting to listen can never spawn anything.</summary>
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
        public void AfterDispose_ReachingTheOldSpawnStreak_ConvertsNothing()
        {
            OccupyRow(3);
            _system.Dispose();

            AdvanceStreakTo(SPAWN_STREAK);

            Assert.AreEqual(0, CountLasers());
        }

        /// <summary>
        /// AC3 (#399): the paused streak-4 threshold does not resurrect a laser through any other
        /// streak-driven spawner. Since issue #401 the coin escalation legitimately drops a coin at
        /// streak 4 (worth 4), so with both spawners listening a streak of 4 yields coins only — never a
        /// laser. (<c>GoldenPieceTriggerSystem</c> needs a full <c>BoardSystem</c> and is not constructed
        /// here; it fires at 5.)
        /// </summary>
        [Test]
        public void OnReachingTheOldSpawnStreak_WithEveryStreakSpawnerListening_GrantsNoLaser()
        {
            OccupyRow(3);

            int lasersAnnounced = 0;
            _boardModel.SpecialKindChanged += (position, kind) =>
            {
                if (kind == SpecialCellKind.Laser)
                {
                    lasersAnnounced++;
                }
            };

            using (var coinSystem = new CoinStreakEscalationSystem(_scoreModel, _boardModel, seed: 1))
            {
                AdvanceStreakTo(SPAWN_STREAK);

                Assert.AreEqual(0, lasersAnnounced, "No spawner may claim the streak-4 threshold for a laser.");
                Assert.AreEqual(0, CountLasers());
            }
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
    }
}
