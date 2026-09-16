using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers what a wipe is worth: the per-cell rate a spent Row Clear pays, doubled inside a frenzy,
    /// and never at the cost of the placement streak counters.
    /// </summary>
    public class LaserScoreSystemTests
    {
        private TestMessageBroker<LaserFiredMessage> _firedBroker;
        private TestMessageBroker<ScoreChangedMessage> _scoreChangedBroker;
        private ScoreModel _scoreModel;
        private DoubleMultiplierModel _doubleMultiplierModel;

        [SetUp]
        public void CreateSystem()
        {
            _firedBroker = new TestMessageBroker<LaserFiredMessage>();
            _scoreChangedBroker = new TestMessageBroker<ScoreChangedMessage>();
            _scoreModel = new ScoreModel();
            _doubleMultiplierModel = new DoubleMultiplierModel();

            LaserScoreSystem unused = new LaserScoreSystem(
                _scoreModel, _doubleMultiplierModel, _firedBroker, _scoreChangedBroker);
        }

        [Test]
        public void OnFired_ScoresOnePointPerWipedCell()
        {
            _firedBroker.Publish(new LaserFiredMessage(7));

            Assert.AreEqual(ScoreRules.PlacementScore(7), _scoreModel.Score.Value);
            Assert.AreEqual(7, _scoreModel.Score.Value);
        }

        [Test]
        public void OnFired_PublishesTheGainForTheHud()
        {
            _firedBroker.Publish(new LaserFiredMessage(5));

            Assert.AreEqual(1, _scoreChangedBroker.Published.Count);
            Assert.AreEqual(5, _scoreChangedBroker.Published[0].Gained);
            Assert.AreEqual(5, _scoreChangedBroker.Published[0].Total);
        }

        [Test]
        public void OnFired_DuringAFrenzy_PaysDouble()
        {
            _doubleMultiplierModel.RemainingSeconds.Value = DoubleMultiplierModel.WINDOW_SECONDS;

            _firedBroker.Publish(new LaserFiredMessage(6));

            Assert.AreEqual(12, _scoreModel.Score.Value);
        }

        /// <summary>A wipe is not a placement: it may never extend, reset or otherwise touch the streak
        /// counters, which is the whole reason this lives apart from ScoreSystem — and doubly so here,
        /// since the streak is what spawned the laser in the first place.</summary>
        [Test]
        public void OnFired_LeavesEveryStreakCounterUntouched()
        {
            _scoreModel.Streak.Value = 4;
            _scoreModel.MultiClearStreak.Value = 2;
            _scoreModel.CumulativeMultiClearCount.Value = 7;

            _firedBroker.Publish(new LaserFiredMessage(4));

            Assert.AreEqual(4, _scoreModel.Streak.Value);
            Assert.AreEqual(2, _scoreModel.MultiClearStreak.Value);
            Assert.AreEqual(7, _scoreModel.CumulativeMultiClearCount.Value);
        }

        [Test]
        public void OnFired_WithNothingWiped_ScoresNothingAndPublishesNothing()
        {
            _firedBroker.Publish(new LaserFiredMessage(0));

            Assert.AreEqual(0, _scoreModel.Score.Value);
            Assert.AreEqual(0, _scoreChangedBroker.Published.Count);
        }
    }
}
