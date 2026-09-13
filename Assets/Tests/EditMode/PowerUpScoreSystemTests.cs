using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the two rules power-up scoring exists to enforce: a bomb pays per cell while a row/column
    /// pays the one-line rate, and neither ever touches the placement streak counters.
    /// </summary>
    public class PowerUpScoreSystemTests
    {
        private TestMessageBroker<PowerUpAppliedMessage> _appliedBroker;
        private TestMessageBroker<ScoreChangedMessage> _scoreChangedBroker;
        private ScoreModel _scoreModel;

        [SetUp]
        public void CreateSystem()
        {
            _appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _scoreChangedBroker = new TestMessageBroker<ScoreChangedMessage>();
            _scoreModel = new ScoreModel();
            PowerUpScoreSystem unused = new PowerUpScoreSystem(
                _scoreModel, _appliedBroker, _scoreChangedBroker);
        }

        [Test]
        public void OnPowerUpApplied_Bomb_ScoresOnePointPerClearedCell()
        {
            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, 5));

            Assert.AreEqual(ScoreRules.PlacementScore(5), _scoreModel.Score.Value);
            Assert.AreEqual(5, _scoreModel.Score.Value);
        }

        [Test]
        public void OnPowerUpApplied_BombTwice_AccumulatesOnTopOfTheExistingScore()
        {
            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, 9));
            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, 4));

            Assert.AreEqual(13, _scoreModel.Score.Value);
        }

        [TestCase(PowerUpKind.RowClear)]
        [TestCase(PowerUpKind.ColumnClear)]
        public void OnPowerUpApplied_LineClearAtZeroStreak_ScoresTheSingleLineRate(PowerUpKind kind)
        {
            _appliedBroker.Publish(new PowerUpAppliedMessage(kind, Board.SIZE));

            Assert.AreEqual(ScoreRules.ClearScore(1, 0), _scoreModel.Score.Value);
        }

        [TestCase(PowerUpKind.RowClear)]
        [TestCase(PowerUpKind.ColumnClear)]
        public void OnPowerUpApplied_LineClearAtAStreak_ScoresWithTheStreakBonus(PowerUpKind kind)
        {
            // A different number from the streak-0 case, which is what proves the streak is actually read.
            _scoreModel.Streak.Value = 3;

            _appliedBroker.Publish(new PowerUpAppliedMessage(kind, Board.SIZE));

            Assert.AreEqual(ScoreRules.ClearScore(1, 3), _scoreModel.Score.Value);
            Assert.AreNotEqual(ScoreRules.ClearScore(1, 0), _scoreModel.Score.Value);
        }

        /// <summary>The cell count never enters the row/column formula — a nearly empty row is worth the
        /// same as a full one, because the power-up cleared the line either way.</summary>
        [Test]
        public void OnPowerUpApplied_RowClear_IgnoresHowManyCellsItCleared()
        {
            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.RowClear, 1));

            Assert.AreEqual(ScoreRules.ClearScore(1, 0), _scoreModel.Score.Value);
        }

        [Test]
        public void OnPowerUpApplied_WithScore_PublishesScoreChangedWithTheGainedAmount()
        {
            _scoreModel.Score.Value = 100;

            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, 7));

            Assert.AreEqual(1, _scoreChangedBroker.Published.Count);
            Assert.AreEqual(107, _scoreChangedBroker.Published[0].Total);
            Assert.AreEqual(7, _scoreChangedBroker.Published[0].Gained);
            Assert.AreEqual(0, _scoreChangedBroker.Published[0].Streak);
        }

        [Test]
        public void OnPowerUpApplied_ClearingNothing_ScoresNothingAndPublishesNothing()
        {
            _scoreModel.Score.Value = 42;

            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, 0));
            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.RowClear, 0));

            Assert.AreEqual(42, _scoreModel.Score.Value);
            Assert.AreEqual(0, _scoreChangedBroker.Published.Count);
        }

        /// <summary>The single highest-risk requirement: the streaks belong to piece placement, so a
        /// power-up must neither extend nor break them.</summary>
        [Test]
        public void OnPowerUpApplied_Bomb_LeavesEveryStreakCounterUntouched()
        {
            _scoreModel.Streak.Value = 3;
            _scoreModel.MultiClearStreak.Value = 2;
            _scoreModel.CumulativeMultiClearCount.Value = 7;

            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, 5));

            Assert.AreEqual(3, _scoreModel.Streak.Value);
            Assert.AreEqual(2, _scoreModel.MultiClearStreak.Value);
            Assert.AreEqual(7, _scoreModel.CumulativeMultiClearCount.Value);
        }

        [TestCase(PowerUpKind.RowClear)]
        [TestCase(PowerUpKind.ColumnClear)]
        public void OnPowerUpApplied_LineClear_LeavesEveryStreakCounterUntouched(PowerUpKind kind)
        {
            _scoreModel.Streak.Value = 3;
            _scoreModel.MultiClearStreak.Value = 2;
            _scoreModel.CumulativeMultiClearCount.Value = 7;

            _appliedBroker.Publish(new PowerUpAppliedMessage(kind, Board.SIZE));

            Assert.AreEqual(3, _scoreModel.Streak.Value);
            Assert.AreEqual(2, _scoreModel.MultiClearStreak.Value);
            Assert.AreEqual(7, _scoreModel.CumulativeMultiClearCount.Value);
        }

        [Test]
        public void Dispose_ThenAnApplication_ScoresNothing()
        {
            var scoreModel = new ScoreModel();
            var system = new PowerUpScoreSystem(scoreModel, _appliedBroker, _scoreChangedBroker);

            system.Dispose();
            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, 5));

            Assert.AreEqual(0, scoreModel.Score.Value);
        }
    }
}
