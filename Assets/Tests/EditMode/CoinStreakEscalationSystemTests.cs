using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #401: every streak level from 2 to 6 drops one coin cell worth 1, 2, 4, 8, 16; nothing
    /// pays above 6; a broken streak restarts the schedule at 1; Classic mode earns nothing.
    /// </summary>
    public class CoinStreakEscalationSystemTests
    {
        private ScoreModel _scoreModel;
        private BoardModel _boardModel;
        private CoinStreakEscalationSystem _system;

        /// <summary>Every coin announced, in order, with the value it was priced at when announced —
        /// read back off the model inside the handler, which is exactly when a View would read it.</summary>
        private List<int> _announcedValues;

        [SetUp]
        public void CreateSystem()
        {
            _scoreModel = new ScoreModel();
            _boardModel = new BoardModel();
            _announcedValues = new List<int>();
            _boardModel.SpecialKindChanged += (position, kind) =>
            {
                if (kind == SpecialCellKind.Coin)
                {
                    _announcedValues.Add(_boardModel.GetCoinValue(position));
                }
            };

            _system = new CoinStreakEscalationSystem(_scoreModel, _boardModel, seed: 1);
        }

        [TearDown]
        public void DisposeSystem() => _system.Dispose();

        // --- AC2: the schedule ---

        /// <summary>Each paying level, reached one step at a time, drops exactly one coin worth
        /// 2^(streak - 2).</summary>
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 4)]
        [TestCase(5, 8)]
        [TestCase(6, 16)]
        public void OnReachingAPayingStreak_DropsOneCoinWorthTheScheduledValue(int streak, int expectedValue)
        {
            FillBoard();
            AdvanceStreakTo(streak - 1);
            _announcedValues.Clear();

            _scoreModel.Streak.Value = streak;

            Assert.AreEqual(1, _announcedValues.Count, "Exactly one drop per level reached.");
            Assert.AreEqual(expectedValue, _announcedValues[0]);
        }

        [Test]
        public void ClimbingFromZeroToSix_DropsTheWholeScheduleInOrder()
        {
            FillBoard();

            AdvanceStreakTo(6);

            CollectionAssert.AreEqual(new[] { 1, 2, 4, 8, 16 }, _announcedValues);
        }

        [Test]
        public void OnReachingAPayingStreak_ConvertsACellThatHoldsABlock_AndLeavesItsColourAlone()
        {
            OccupyRow(3);

            AdvanceStreakTo(2);

            GridPosition coin = FindTheCoin();
            Assert.AreEqual(1, _boardModel.GetCell(coin), "Converted in place: same block, same colour.");
            Assert.AreEqual(Board.SIZE, _boardModel.Board.OccupiedCellCount());
            Assert.AreEqual(1, _boardModel.GetCoinValue(coin));
        }

        // --- AC3: the cap ---

        [Test]
        public void ContinuingPastStreakSix_DropsNothingFurther()
        {
            FillBoard();
            AdvanceStreakTo(6);
            _announcedValues.Clear();

            AdvanceStreakTo(12);

            Assert.AreEqual(0, _announcedValues.Count, "The schedule is capped at 6, not repeating.");
        }

        // --- AC8: below the first paying level ---

        [Test]
        public void AStreakThatOnlyReachesOne_DropsNothing()
        {
            FillBoard();

            AdvanceStreakTo(1);
            _scoreModel.Streak.Value = 0;

            Assert.AreEqual(0, _announcedValues.Count);
        }

        // --- AC4 / AC9: reset on break ---

        /// <summary>The headline reset case: 2..6 pays 1, 2, 4, 8, 16; break; 2..3 pays 1, 2 again — not
        /// a continuation from 16, and not nothing.</summary>
        [Test]
        public void AfterABreak_ASecondRunRestartsTheScheduleAtOne()
        {
            FillBoard();

            AdvanceStreakTo(6);
            _scoreModel.Streak.Value = 0;
            AdvanceStreakTo(3);

            CollectionAssert.AreEqual(new[] { 1, 2, 4, 8, 16, 1, 2 }, _announcedValues);
        }

        /// <summary>A break after only part of the schedule was paid resets it just the same: the second
        /// run does not resume at the level the first one stopped at.</summary>
        [Test]
        public void AfterABreakMidSchedule_ASecondRunRestartsAtOne()
        {
            FillBoard();

            AdvanceStreakTo(4);
            _scoreModel.Streak.Value = 0;
            AdvanceStreakTo(4);

            CollectionAssert.AreEqual(new[] { 1, 2, 4, 1, 2, 4 }, _announcedValues);
        }

        /// <summary>A break from above the cap forgets the cap too: the run that went to 9 and broke
        /// does not leave the next one silent.</summary>
        [Test]
        public void AfterABreakFromAboveTheCap_ASecondRunPaysAgainFromOne()
        {
            FillBoard();

            AdvanceStreakTo(9);
            _scoreModel.Streak.Value = 0;
            AdvanceStreakTo(2);

            CollectionAssert.AreEqual(new[] { 1, 2, 4, 8, 16, 1 }, _announcedValues);
        }

        /// <summary>Only a break resets. A streak that keeps climbing within the paying range never pays
        /// the same level twice, however the property is written.</summary>
        [Test]
        public void WithinOneRun_NoLevelIsPaidTwice()
        {
            FillBoard();

            AdvanceStreakTo(6);
            AdvanceStreakTo(8);

            Assert.AreEqual(5, _announcedValues.Count);
        }

        /// <summary>Documented judgment call: were the streak ever to jump several levels in one change,
        /// every level stepped over is paid exactly once, in order — never skipped, never doubled.</summary>
        [Test]
        public void AStreakThatJumpsSeveralLevelsAtOnce_PaysEachSteppedOverLevelOnce()
        {
            FillBoard();

            _scoreModel.Streak.Value = 4;
            _scoreModel.Streak.Value = 5;

            CollectionAssert.AreEqual(new[] { 1, 2, 4, 8 }, _announcedValues);
        }

        // --- AC7: mode gating ---

        [Test]
        public void InClassicMode_ClimbingThroughEveryPayingStreak_DropsNothing()
        {
            _system.Dispose();
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Timed;
            _system = new CoinStreakEscalationSystem(_scoreModel, _boardModel, seed: 1, gameModeModel: gameModeModel);
            FillBoard();

            AdvanceStreakTo(7);

            Assert.AreEqual(0, _announcedValues.Count);
            Assert.AreEqual(0, CountCoins());
        }

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Path)]
        public void InAModeWithExtras_ClimbingToSix_DropsTheWholeSchedule(GameMode mode)
        {
            _system.Dispose();
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = mode;
            _system = new CoinStreakEscalationSystem(_scoreModel, _boardModel, seed: 1, gameModeModel: gameModeModel);
            FillBoard();

            AdvanceStreakTo(6);

            CollectionAssert.AreEqual(new[] { 1, 2, 4, 8, 16 }, _announcedValues);
        }

        // --- Edges shared with the old trigger ---

        /// <summary>No occupied cell to convert — a streak-advancing placement that also emptied the
        /// board — is silently skipped, and the level still counts as paid.</summary>
        [Test]
        public void OnReachingAPayingStreak_WithAnEmptyBoard_DropsNothing_AndDoesNotPayItLater()
        {
            AdvanceStreakTo(2);
            Assert.AreEqual(0, _announcedValues.Count);

            OccupyRow(3);
            _scoreModel.Streak.Value = 3;

            CollectionAssert.AreEqual(new[] { 2 }, _announcedValues, "Level 2 had nowhere to land; level 3 pays 2.");
        }

        [Test]
        public void Constructing_DropsNothingByItself()
        {
            OccupyRow(3);

            var freshSystem = new CoinStreakEscalationSystem(_scoreModel, _boardModel, seed: 2);

            Assert.AreEqual(0, _scoreModel.Streak.Value);
            Assert.AreEqual(0, CountCoins());
            freshSystem.Dispose();
        }

        [Test]
        public void AfterDispose_ReachingAPayingStreak_DropsNothing()
        {
            OccupyRow(3);
            _system.Dispose();

            AdvanceStreakTo(6);

            Assert.AreEqual(0, CountCoins());
        }

        [Test]
        public void CoinValueForStreak_FollowsThePowerOfTwoSchedule()
        {
            Assert.AreEqual(1, CoinStreakEscalationSystem.CoinValueForStreak(2));
            Assert.AreEqual(2, CoinStreakEscalationSystem.CoinValueForStreak(3));
            Assert.AreEqual(4, CoinStreakEscalationSystem.CoinValueForStreak(4));
            Assert.AreEqual(8, CoinStreakEscalationSystem.CoinValueForStreak(5));
            Assert.AreEqual(16, CoinStreakEscalationSystem.CoinValueForStreak(6));
        }

        /// <summary>Steps the streak one at a time, the only way <c>ScoreSystem</c> ever moves it up.</summary>
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

        private int CountCoins()
        {
            int count = 0;
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    if (_boardModel.GetSpecialKind(new GridPosition(x, y)) == SpecialCellKind.Coin)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private GridPosition FindTheCoin()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_boardModel.GetSpecialKind(position) == SpecialCellKind.Coin)
                    {
                        return position;
                    }
                }
            }

            Assert.Fail("No coin was dropped.");
            return default;
        }
    }
}
