using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers what a blast is worth: the per-cell rate a spent Bomb pays, doubled inside a frenzy, and
    /// never at the cost of the placement streak counters.
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
        public void OnDetonated_ScoresOnePointPerBlastedCell()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(8));

            Assert.AreEqual(ScoreRules.PlacementScore(8), _scoreModel.Score.Value);
            Assert.AreEqual(8, _scoreModel.Score.Value);
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

        /// <summary>A blast is not a placement: it may never extend, reset or otherwise touch the
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
        public void OnDetonated_WithNothingCleared_ScoresNothingAndPublishesNothing()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(0));

            Assert.AreEqual(0, _scoreModel.Score.Value);
            Assert.AreEqual(0, _scoreChangedBroker.Published.Count);
        }
    }
}
