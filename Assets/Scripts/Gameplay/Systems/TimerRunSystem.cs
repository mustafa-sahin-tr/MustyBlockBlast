using System;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
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
            if (!_timerModel.IsRunning.Value || _isAppPaused || _isMenuPaused || _isPowerUpArmedPaused)
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

            _isAppPaused = false;
            _isMenuPaused = false;
            _isPowerUpArmedPaused = false;
            _timerModel.RemainingSeconds.Value = _timedModeSystem.SelectedDuration.Value;
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
