using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #370 AC7: a rescue-eligible <see cref="GameOverMessage"/> holds the Timed-mode countdown
    /// rather than stopping it, exactly as a modal menu does, and <see cref="RunRescuedMessage"/>
    /// releases the hold. Like <see cref="TimerRunSystemClearAnimationPauseTests"/>, only what does not
    /// depend on real elapsed time is checked here — <c>Time.deltaTime</c> is 0 outside Play mode.
    /// </summary>
    public sealed class TimerRunSystemRescuePauseTests
    {
        private const float MATCH_LENGTH_SECONDS = 120f;

        private TimerModel _timerModel;
        private RunPauseModel _runPauseModel;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<RunRescuedMessage> _runRescuedBroker;
        private TimedModeConfig _timedModeConfig;

        [SetUp]
        public void CreateModels()
        {
            _timerModel = new TimerModel();
            _runPauseModel = new RunPauseModel();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _runRescuedBroker = new TestMessageBroker<RunRescuedMessage>();
            _timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
        }

        [TearDown]
        public void DestroyConfig()
        {
            if (_timedModeConfig != null)
            {
                Object.DestroyImmediate(_timedModeConfig);
            }
        }

        [Test]
        public void ARescueAvailableEnding_HoldsTheClockInsteadOfStoppingIt()
        {
            TimerRunSystem unused = CreateTimedRun();

            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: true));

            Assert.IsTrue(_timerModel.IsRunning.Value, "Held, not stopped: the seconds stay live for a rescue.");
            Assert.AreEqual(MATCH_LENGTH_SECONDS, _timerModel.RemainingSeconds.Value);
            Assert.IsTrue(_runPauseModel.IsPaused.Value, "The same hold every other modal takes.");
        }

        [Test]
        public void AnOrdinaryEnding_StillStopsTheClock()
        {
            TimerRunSystem unused = CreateTimedRun();

            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: false));

            Assert.IsFalse(_timerModel.IsRunning.Value);
            Assert.IsFalse(_runPauseModel.IsPaused.Value);
        }

        [Test]
        public void ARescue_ReleasesTheHold()
        {
            TimerRunSystem unused = CreateTimedRun();
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: true));

            _runRescuedBroker.Publish(new RunRescuedMessage());

            Assert.IsTrue(_timerModel.IsRunning.Value);
            Assert.IsFalse(_runPauseModel.IsPaused.Value);
        }

        [Test]
        public void ADeclinedOffer_IsReleasedByTheNextRun_InEveryMode(
            [Values(GameMode.Endless, GameMode.Timed)] GameMode mode)
        {
            TimerRunSystem unused = CreateTimerRunSystem(mode);
            _runStartedBroker.Publish(new RunStartedMessage());
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: true));
            Assert.IsTrue(_runPauseModel.IsPaused.Value);

            // No rescue, no decline message: the player simply restarted.
            _runStartedBroker.Publish(new RunStartedMessage());

            Assert.IsFalse(
                _runPauseModel.IsPaused.Value,
                "An Endless offer holds the objective clock too, and must not outlive the run it was made for.");
        }

        private TimerRunSystem CreateTimedRun()
        {
            TimerRunSystem timerRunSystem = CreateTimerRunSystem(GameMode.Timed);
            _runStartedBroker.Publish(new RunStartedMessage());
            Assert.IsTrue(_timerModel.IsRunning.Value);
            Assert.AreEqual(MATCH_LENGTH_SECONDS, _timerModel.RemainingSeconds.Value);
            return timerRunSystem;
        }

        private TimerRunSystem CreateTimerRunSystem(GameMode mode)
        {
            BoardSystem boardSystem = CreateBoardSystem();
            var gameModeSystem = new GameModeSystem(new GameModeModel(), boardSystem);
            gameModeSystem.SelectMode(mode);

            var timedModeSystem = new TimedModeSystem(new TimedModeModel(), _timedModeConfig);
            timedModeSystem.SelectedDuration.Value = MATCH_LENGTH_SECONDS;

            return new TimerRunSystem(
                _timerModel,
                _runPauseModel,
                gameModeSystem,
                timedModeSystem,
                boardSystem,
                _runStartedBroker,
                _gameOverBroker,
                _runRescuedBroker);
        }

        private static BoardSystem CreateBoardSystem()
        {
            return new BoardSystem(
                new BoardModel(),
                new TrayModel(),
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                reinforcedCellSeeder: null);
        }
    }
}
