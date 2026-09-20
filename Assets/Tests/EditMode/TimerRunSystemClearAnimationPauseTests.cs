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
    /// Issue #350: covers <see cref="TimerRunSystem.SetClearAnimationPlaying"/> behaviour that does not
    /// depend on real elapsed wall-clock time — <c>Time.deltaTime</c> is always 0 outside Play mode, so
    /// the actual countdown-pauses/resumes-and-decreases assertions live in the PlayMode counterpart of
    /// this file instead, where frames genuinely advance.
    /// <para>
    /// Deliberately checks <see cref="RunPauseModel"/> stays untouched: that is the one decision this
    /// issue makes that a purely timing-based test could never catch, since <c>RunPauseModel</c> is read
    /// only by <c>ObjectiveSystem</c>, not by this countdown.
    /// </para>
    /// </summary>
    public sealed class TimerRunSystemClearAnimationPauseTests
    {
        [Test]
        public void SetClearAnimationPlaying_NeverWritesRunPauseModel()
        {
            var runPauseModel = new RunPauseModel();
            TimerRunSystem timerRunSystem = CreateTimerRunSystem(GameMode.Timed, runPauseModel);

            timerRunSystem.SetClearAnimationPlaying(true);

            Assert.IsFalse(runPauseModel.IsPaused.Value);
        }

        [Test]
        public void SetClearAnimationPlaying_InEndlessMode_LeavesTimerModelCleared()
        {
            // AC5's negative: Endless mode never runs this countdown — OnModeChanged already cleared
            // IsRunning/RemainingSeconds as soon as the mode left Timed, and a clear-animation pause
            // reason arriving afterwards must not resurrect either.
            var timerModel = new TimerModel();
            TimerRunSystem timerRunSystem = CreateTimerRunSystem(GameMode.Endless, new RunPauseModel(), timerModel);

            timerRunSystem.SetClearAnimationPlaying(true);

            Assert.IsFalse(timerModel.IsRunning.Value);
            Assert.AreEqual(0f, timerModel.RemainingSeconds.Value);
        }

        private static TimerRunSystem CreateTimerRunSystem(GameMode mode, RunPauseModel runPauseModel)
        {
            return CreateTimerRunSystem(mode, runPauseModel, new TimerModel());
        }

        private static TimerRunSystem CreateTimerRunSystem(
            GameMode mode, RunPauseModel runPauseModel, TimerModel timerModel)
        {
            BoardSystem boardSystem = CreateBoardSystem();
            var gameModeSystem = new GameModeSystem(new GameModeModel(), boardSystem);
            gameModeSystem.SelectMode(mode);

            var timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            return new TimerRunSystem(
                timerModel,
                runPauseModel,
                gameModeSystem,
                new TimedModeSystem(new TimedModeModel(), timedModeConfig),
                boardSystem,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
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
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                reinforcedCellSeeder: null);
        }
    }
}
