using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers what a detonation's bonus wipe is worth (issue #398 AC5/AC6): the flat per-cell rate a
    /// laser's wipe and a spent Row Clear pay, doubled inside a frenzy, and never at the cost of the
    /// placement streak counters.
    /// </summary>
    public class ExplosiveCoreScoreSystemTests
    {
        private TestMessageBroker<ExplosiveCoreDetonatedMessage> _detonatedBroker;
        private TestMessageBroker<ScoreChangedMessage> _scoreChangedBroker;
        private ScoreModel _scoreModel;
        private DoubleMultiplierModel _doubleMultiplierModel;

        [SetUp]
        public void CreateSystem()
        {
            _detonatedBroker = new TestMessageBroker<ExplosiveCoreDetonatedMessage>();
            _scoreChangedBroker = new TestMessageBroker<ScoreChangedMessage>();
            _scoreModel = new ScoreModel();
            _doubleMultiplierModel = new DoubleMultiplierModel();

            ExplosiveCoreScoreSystem unused = new ExplosiveCoreScoreSystem(
                _scoreModel, _doubleMultiplierModel, _detonatedBroker, _scoreChangedBroker);
        }

        [Test]
        public void OnDetonated_ScoresOnePointPerWipedCell()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(7));

            Assert.AreEqual(ScoreRules.PlacementScore(7), _scoreModel.Score.Value);
            Assert.AreEqual(7, _scoreModel.Score.Value);
        }

        /// <summary>AC5: the payout formula is the laser's, not the line-clear one — a wipe of the same
        /// size is worth the same whatever kind of cell set it off, and the streak plays no part.</summary>
        [Test]
        public void OnDetonated_PaysTheSameAsALaserWipeOfTheSameSize()
        {
            _scoreModel.Streak.Value = 4;

            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(5));

            Assert.AreEqual(ScoreRules.PlacementScore(5), _scoreModel.Score.Value);
            Assert.AreNotEqual(ScoreRules.ClearScore(1, 4), _scoreModel.Score.Value, "Not the line-clear rate.");
        }

        [Test]
        public void OnDetonated_PublishesTheGainForTheHud()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(5));

            Assert.AreEqual(1, _scoreChangedBroker.Published.Count);
            Assert.AreEqual(5, _scoreChangedBroker.Published[0].Gained);
            Assert.AreEqual(5, _scoreChangedBroker.Published[0].Total);
        }

        [Test]
        public void OnDetonated_DuringAFrenzy_PaysDouble()
        {
            _doubleMultiplierModel.RemainingSeconds.Value = DoubleMultiplierModel.WINDOW_SECONDS;

            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(6));

            Assert.AreEqual(12, _scoreModel.Score.Value);
        }

        /// <summary>A detonation is not a placement: it may never extend, reset or otherwise touch the
        /// streak counters, which is the whole reason this lives apart from ScoreSystem.</summary>
        [Test]
        public void OnDetonated_LeavesEveryStreakCounterUntouched()
        {
            _scoreModel.Streak.Value = 3;
            _scoreModel.MultiClearStreak.Value = 2;
            _scoreModel.CumulativeMultiClearCount.Value = 7;

            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(4));

            Assert.AreEqual(3, _scoreModel.Streak.Value);
            Assert.AreEqual(2, _scoreModel.MultiClearStreak.Value);
            Assert.AreEqual(7, _scoreModel.CumulativeMultiClearCount.Value);
        }

        [Test]
        public void OnDetonated_WithNothingWiped_ScoresNothingAndPublishesNothing()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(0));

            Assert.AreEqual(0, _scoreModel.Score.Value);
            Assert.AreEqual(0, _scoreChangedBroker.Published.Count);
        }
    }
}
