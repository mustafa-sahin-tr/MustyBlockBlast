using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers what a detonation is worth: the ordinary line-clear rate for however many lines it
    /// finished, doubled inside a frenzy, and never at the cost of the placement streak counters. A
    /// hand-off with no finished line scores nothing.
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
        public void OnDetonated_ScoresTheOrdinaryLineClearRate()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(finishedLineCount: 1, handOffCount: 0, detonations: null));

            Assert.AreEqual(ScoreRules.ClearScore(1, _scoreModel.Streak.Value), _scoreModel.Score.Value);
        }

        /// <summary>Two lines finished in the same detonation score exactly as two lines completed by
        /// one placement would.</summary>
        [Test]
        public void OnDetonated_WithTwoFinishedLines_ScoresTheTwoLineRate()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(finishedLineCount: 2, handOffCount: 0, detonations: null));

            Assert.AreEqual(ScoreRules.ClearScore(2, _scoreModel.Streak.Value), _scoreModel.Score.Value);
        }

        [Test]
        public void OnDetonated_ScoresMoreWithAHigherStreak()
        {
            _scoreModel.Streak.Value = 4;

            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(finishedLineCount: 1, handOffCount: 0, detonations: null));

            Assert.AreEqual(ScoreRules.ClearScore(1, 4), _scoreModel.Score.Value);
            Assert.Greater(_scoreModel.Score.Value, ScoreRules.ClearScore(1, 0));
        }

        [Test]
        public void OnDetonated_PublishesTheGainForTheHud()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(finishedLineCount: 1, handOffCount: 0, detonations: null));

            Assert.AreEqual(1, _scoreChangedBroker.Published.Count);
            Assert.AreEqual(_scoreModel.Score.Value, _scoreChangedBroker.Published[0].Gained);
            Assert.AreEqual(_scoreModel.Score.Value, _scoreChangedBroker.Published[0].Total);
        }

        [Test]
        public void OnDetonated_DuringAFrenzy_PaysDouble()
        {
            _doubleMultiplierModel.RemainingSeconds.Value = DoubleMultiplierModel.WINDOW_SECONDS;

            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(finishedLineCount: 1, handOffCount: 0, detonations: null));

            Assert.AreEqual(ScoreRules.ClearScore(1, 0) * 2, _scoreModel.Score.Value);
        }

        /// <summary>A detonation is not a placement: it may never extend, reset or otherwise touch the
        /// streak counters, which is the whole reason this lives apart from ScoreSystem.</summary>
        [Test]
        public void OnDetonated_LeavesEveryStreakCounterUntouched()
        {
            _scoreModel.Streak.Value = 3;
            _scoreModel.MultiClearStreak.Value = 2;
            _scoreModel.CumulativeMultiClearCount.Value = 7;

            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(finishedLineCount: 1, handOffCount: 0, detonations: null));

            Assert.AreEqual(3, _scoreModel.Streak.Value);
            Assert.AreEqual(2, _scoreModel.MultiClearStreak.Value);
            Assert.AreEqual(7, _scoreModel.CumulativeMultiClearCount.Value);
        }

        /// <summary>A hand-off finished no line — there is nothing to pay for.</summary>
        [Test]
        public void OnDetonated_WithOnlyAHandOff_ScoresNothingAndPublishesNothing()
        {
            _detonatedBroker.Publish(new ExplosiveCoreDetonatedMessage(finishedLineCount: 0, handOffCount: 1, detonations: null));

            Assert.AreEqual(0, _scoreModel.Score.Value);
            Assert.AreEqual(0, _scoreChangedBroker.Published.Count);
        }
    }
}
