using System;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer.Unity;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Drives the <see cref="GameMode.Timed"/> countdown. Owns <see cref="TimerModel"/>.
    /// <list type="bullet">
    /// <item>Starts once per run, at the selected match length, on <see cref="RunStartedMessage"/>.
    /// Placing a piece and refilling the tray never reset it: the whole match shares one clock.</item>
    /// <item>Reaching zero ends the run through <see cref="BoardSystem.ForceGameOver"/>, so "the run
    /// is over" stays a single invariant owned by <see cref="BoardSystem"/>.</item>
    /// <item>Does nothing at all in endless runs, and is cleared when the mode leaves timed.</item>
    /// </list>
    /// <para>
    /// Three independent pause reasons — app backgrounding, a modal menu panel being open, and a
    /// power-up being armed — combine with OR: any one of them holds the clock. Dragging a piece
    /// deliberately does <b>not</b> pause: holding a piece in mid-air would otherwise be free time.
    /// Aiming a power-up does, because the player earned that power-up outside the run and must not
    /// be charged run time for spending it.
    /// </para>
    /// </summary>
    public sealed class TimerRunSystem : ITickable, IDisposable
    {
        /// <summary>
        /// Remaining seconds at which the HUD switches to its low-time look. Ten seconds, which is
        /// about one placement's worth of thinking time — long enough to act on, short enough that it
        /// does not sit lit for most of a three-minute round.
        /// </summary>
        private const float LOW_TIME_WARNING_THRESHOLD_SECONDS = 10f;

        private readonly TimerModel _timerModel;
        private readonly RunPauseModel _runPauseModel;
        private readonly GameModeSystem _gameModeSystem;
        private readonly TimedModeSystem _timedModeSystem;
        private readonly BoardSystem _boardSystem;
        private readonly IDisposable _runStartedSubscription;
        private readonly IDisposable _gameOverSubscription;
        private readonly IDisposable _modeSubscription;

        private bool _isAppPaused;
        private bool _isMenuPaused;
        private bool _isPowerUpArmedPaused;

        /// <summary>
        /// How many clear animations <see cref="BoardView"/> currently has in flight. A reference
        /// count rather than a bool: a fast combo can start a second clear animation while the first
        /// one is still fading (issue #350 AC4), and a naive bool would resume the clock the instant
        /// either one finished instead of waiting for the union of both spans. Deliberately local to
        /// this system rather than routed through <see cref="RunPauseModel"/> — that model also feeds
        /// <c>ObjectiveSystem</c>'s rolling-window objectives in both Endless and Timed mode, which must
        /// not be paused by a purely cosmetic Timed-mode concession.
        /// </summary>
        private int _clearAnimationPauseCount;

        public TimerRunSystem(
            TimerModel timerModel,
            RunPauseModel runPauseModel,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            BoardSystem boardSystem,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _timerModel = timerModel;
            _runPauseModel = runPauseModel;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
            _boardSystem = boardSystem;

            _runStartedSubscription = runStartedSubscriber.Subscribe(OnRunStarted);
            _gameOverSubscription = gameOverSubscriber.Subscribe(OnGameOver);
            _modeSubscription = _gameModeSystem.CurrentMode.Subscribe(OnModeChanged);
        }

        /// <summary>
        /// Suspends the countdown without discarding the remaining time. Called only from the app
        /// pause hook: a backgrounded app must neither lose nor gain seconds.
        /// </summary>
        public void SetAppPaused(bool paused)
        {
            _isAppPaused = paused;
            RefreshPauseModel();
        }

        /// <summary>
        /// Suspends the countdown while a modal menu panel is open, so a player can't lose a timed run
        /// just from opening a menu. Called from <c>SettingsPanelView</c> and <c>LevelPathPanelView</c>
        /// on their own open/close edges — one flag rather than one per panel, because the input gate
        /// chain makes the two mutually exclusive, so they can never disagree about who holds it.
        /// </summary>
        public void SetMenuPaused(bool paused)
        {
            _isMenuPaused = paused;
            RefreshPauseModel();
        }

        /// <summary>
        /// Suspends the countdown while a power-up is armed and being aimed, so the seconds spent
        /// choosing a target are free. Called only from <c>PowerUpSystem</c>, which arms, cancels and
        /// applies — and therefore owns both edges of this flag.
        /// </summary>
        public void SetPowerUpArmedPaused(bool paused)
        {
            _isPowerUpArmedPaused = paused;
            RefreshPauseModel();
        }

        /// <summary>
        /// Holds the countdown for the wall-clock duration of a row/column clear animation, so a player
        /// never loses Timed-mode seconds watching a clear resolve visually (issue #350). Called once
        /// per animating cell from the single <c>BoardView</c> code path that plays the fade/stagger/
        /// flash visuals — covering ordinary placements, power-up-forced clears and cross-clear combo
        /// flashes uniformly, since they all funnel through that one path.
        /// <para>
        /// A reference count, not a bool: pass <c>true</c> when a clear animation starts and
        /// <c>true</c>/<c>false</c> must be paired per call — every <c>true</c> from a caller must be
        /// matched by exactly one later <c>false</c> once that specific animation finishes. Overlapping
        /// animations (a fast combo) each hold their own slot in the count, so the countdown stays
        /// paused for the union of every span currently open and resumes only once the last one closes.
        /// </para>
        /// <para>
        /// Deliberately not routed through <see cref="RunPauseModel"/>: that model also feeds
        /// <c>ObjectiveSystem</c>'s rolling-window objectives, which must keep ticking through a clear
        /// animation in both Endless and Timed mode.
        /// </para>
        /// </summary>
        public void SetClearAnimationPlaying(bool playing)
        {
            if (playing)
            {
                _clearAnimationPauseCount++;
                return;
            }

            if (_clearAnimationPauseCount > 0)
            {
                _clearAnimationPauseCount--;
            }
        }

        /// <summary>
        /// Publishes the combined pause state to <see cref="RunPauseModel"/> so other wall-clock-driven
        /// systems — currently <c>ObjectiveSystem</c>'s rolling-window objectives — hold the same three
        /// reasons this countdown already does, without each caller needing to know about both.
        /// </summary>
        private void RefreshPauseModel()
        {
            _runPauseModel.IsPaused.Value = _isAppPaused || _isMenuPaused || _isPowerUpArmedPaused;
        }

        void ITickable.Tick()
        {
            if (!_timerModel.IsRunning.Value || _isAppPaused || _isMenuPaused || _isPowerUpArmedPaused
                || _clearAnimationPauseCount > 0)
            {
                return;
            }

            float remaining = _timerModel.RemainingSeconds.Value - Time.deltaTime;
            if (remaining > 0f)
            {
                _timerModel.RemainingSeconds.Value = remaining;
                _timerModel.IsLowTime.Value = remaining < LOW_TIME_WARNING_THRESHOLD_SECONDS;
                return;
            }

            _timerModel.RemainingSeconds.Value = 0f;
            _timerModel.IsRunning.Value = false;
            _boardSystem.ForceGameOver(GameOverReason.TimeUp);
        }

        public void Dispose()
        {
            _runStartedSubscription.Dispose();
            _gameOverSubscription.Dispose();
            _modeSubscription.Dispose();
        }

        /// <summary>
        /// Starts the one clock this run gets, at the selected match length. Fires once per run — the
        /// board being emptied and the tray drawn — so later refills cannot hand out extra time.
        /// </summary>
        private void OnRunStarted(RunStartedMessage message)
        {
            if (_gameModeSystem.CurrentMode.Value != GameMode.Timed)
            {
                return;
            }

            float duration = _timedModeSystem.SelectedDuration.Value;
            if (TimedModeConfig.IsEndlessDuration(duration))
            {
                // Classic mode's "Sınırsız" duration (issue #355): the run plays forever with no clock
                // and no countdown HUD, exactly like Endless — the only difference from Endless is the
                // ruleset GameModeModel.ExtrasEnabled gates, which this System never touches.
                _timerModel.IsRunning.Value = false;
                _timerModel.RemainingSeconds.Value = 0f;
                _timerModel.IsLowTime.Value = false;
                return;
            }

            _isAppPaused = false;
            _isMenuPaused = false;
            _isPowerUpArmedPaused = false;
            // Defensive reset: a fresh run means no clear animation from a previous run can still be
            // in flight holding this. Any BoardView fade objects from before were destroyed with the
            // old board, so their eventual SetClearAnimationPlaying(false) — if it still fires — will
            // never fire for a generation this run recognises.
            _clearAnimationPauseCount = 0;
            _timerModel.RemainingSeconds.Value = duration;
            _timerModel.IsRunning.Value = true;
            _timerModel.IsLowTime.Value = false;
        }

        /// <summary>
        /// Stops the clock whichever way the run ended — expiring here, or running out of moves. Note
        /// the restart path re-arms it: restarting starts a fresh run, which is the reset trigger.
        /// </summary>
        private void OnGameOver(GameOverMessage message) => _timerModel.IsRunning.Value = false;

        private void OnModeChanged(GameMode mode)
        {
            if (mode == GameMode.Timed)
            {
                return;
            }

            // Endless runs are never ended by time and show no HUD, so the model is cleared rather
            // than merely paused.
            _timerModel.IsRunning.Value = false;
            _timerModel.RemainingSeconds.Value = 0f;
            _timerModel.IsLowTime.Value = false;
        }
    }
}
