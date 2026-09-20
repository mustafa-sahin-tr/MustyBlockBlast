using System.Collections;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer.Unity;

namespace MustyBlockBlast.Tests.PlayMode
{
    /// <summary>
    /// Issue #350: exercises <see cref="TimerRunSystem"/>'s clear-animation pause reason with real,
    /// advancing frames — <c>Time.deltaTime</c> is always 0 in EditMode, so the countdown's actual
    /// decrease/pause/resume behaviour can only be observed here.
    /// <para>
    /// Drives <see cref="ITickable.Tick"/> by hand once per yielded frame, exactly as the real
    /// <c>EntryPoint</c> loop would, without needing a full <c>VContainer</c> scope or scene.
    /// </para>
    /// </summary>
    public sealed class TimerRunSystemClearAnimationPausePlayModeTests
    {
        private const int SETTLE_FRAMES = 5;

        private TimerRunSystem _timerRunSystem;
        private TimerModel _timerModel;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _timerModel = new TimerModel();
            BoardSystem boardSystem = CreateBoardSystem();
            var gameModeSystem = new GameModeSystem(new GameModeModel(), boardSystem);
            gameModeSystem.SelectMode(GameMode.Timed);

            var timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            var runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _timerRunSystem = new TimerRunSystem(
                _timerModel,
                new RunPauseModel(),
                gameModeSystem,
                new TimedModeSystem(new TimedModeModel(), timedModeConfig),
                boardSystem,
                runStartedBroker,
                new TestMessageBroker<GameOverMessage>());

            // TimedModeConfig is created with no configured durations, so TimerRunSystem starts the run
            // at TimedModeConfig's own fallback duration — plenty of headroom for these tests' handful
            // of frames, and avoids needing write access to TimerModel.RemainingSeconds, whose setter is
            // internal to MustyBlockBlast.Gameplay and not visible to this assembly.
            runStartedBroker.Publish(new RunStartedMessage());

            // Let one frame pass before any assertion so the very first Time.deltaTime read is not an
            // editor-transition outlier.
            yield return null;
        }

        [UnityTest]
        public IEnumerator Tick_WithNoAnimationPlaying_CountsDownOverFrames()
        {
            float before = _timerModel.RemainingSeconds.Value;

            yield return TickFrames(SETTLE_FRAMES);

            Assert.Less(_timerModel.RemainingSeconds.Value, before);
        }

        [UnityTest]
        public IEnumerator Tick_WhileClearAnimationPlaying_DoesNotCountDown()
        {
            _timerRunSystem.SetClearAnimationPlaying(true);
            float before = _timerModel.RemainingSeconds.Value;

            yield return TickFrames(SETTLE_FRAMES);

            Assert.AreEqual(before, _timerModel.RemainingSeconds.Value);
        }

        [UnityTest]
        public IEnumerator Tick_AfterClearAnimationEnds_ResumesWithinOneFrame()
        {
            _timerRunSystem.SetClearAnimationPlaying(true);
            yield return TickFrames(SETTLE_FRAMES);
            float pausedRemaining = _timerModel.RemainingSeconds.Value;

            _timerRunSystem.SetClearAnimationPlaying(false);
            yield return TickFrames(1);

            Assert.Less(_timerModel.RemainingSeconds.Value, pausedRemaining);
        }

        /// <summary>
        /// AC4: a fast combo can start a second clear animation before the first one finishes fading.
        /// The countdown must stay paused for the union of both spans — ending only the first must not
        /// resume it, and it must resume only once the last one open ends. A plain bool pause flag would
        /// resume the instant the first "false" call landed; this asserts the reference count does not.
        /// </summary>
        [UnityTest]
        public IEnumerator Tick_WithOverlappingClearAnimations_StaysPausedUntilBothEnd()
        {
            _timerRunSystem.SetClearAnimationPlaying(true); // first clear starts
            yield return TickFrames(2);
            _timerRunSystem.SetClearAnimationPlaying(true); // second clear starts, first still fading
            yield return TickFrames(2);

            _timerRunSystem.SetClearAnimationPlaying(false); // first clear ends, second still open
            float stillPausedRemaining = _timerModel.RemainingSeconds.Value;
            yield return TickFrames(SETTLE_FRAMES);

            Assert.AreEqual(
                stillPausedRemaining, _timerModel.RemainingSeconds.Value,
                "Countdown must stay paused while the second clear animation is still open.");

            _timerRunSystem.SetClearAnimationPlaying(false); // second clear ends, union closes
            yield return TickFrames(1);

            Assert.Less(
                _timerModel.RemainingSeconds.Value, stillPausedRemaining,
                "Countdown must resume once the last open clear animation ends.");
        }

        [UnityTest]
        public IEnumerator SetClearAnimationPlaying_UnmatchedFalse_DoesNotBreakLaterPauses()
        {
            // One extra, unmatched false — defensive against a bookkeeping bug in a caller. Must not
            // drive the counter negative, which would otherwise require an extra "true" before the next
            // real animation could pause the clock at all.
            _timerRunSystem.SetClearAnimationPlaying(true);
            _timerRunSystem.SetClearAnimationPlaying(false);
            _timerRunSystem.SetClearAnimationPlaying(false);

            _timerRunSystem.SetClearAnimationPlaying(true);
            float pausedRemaining = _timerModel.RemainingSeconds.Value;
            yield return TickFrames(SETTLE_FRAMES);

            Assert.AreEqual(
                pausedRemaining, _timerModel.RemainingSeconds.Value,
                "A genuinely open animation must still pause the clock after an earlier unmatched false.");
        }

        private IEnumerator TickFrames(int frameCount)
        {
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                ((ITickable)_timerRunSystem).Tick();
                yield return null;
            }
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
