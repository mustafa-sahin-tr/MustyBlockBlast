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
    /// <item>Starts/resets to the selected duration on every tray refill — the opening draw of a run
    /// and every later refill. Placing a single piece never resets it.</item>
    /// <item>Reaching zero ends the run through <see cref="BoardSystem.ForceGameOver"/>, so "the run
    /// is over" stays a single invariant owned by <see cref="BoardSystem"/>.</item>
    /// <item>Does nothing at all in endless runs, and is cleared when the mode leaves timed.</item>
    /// </list>
    /// <para>
    /// Two independent pause reasons — app backgrounding and the Settings panel being open — combine
    /// with OR: either one holds the clock. Dragging a piece deliberately does <b>not</b> pause:
    /// holding a piece in mid-air would otherwise be free time.
    /// </para>
    /// </summary>
    public sealed class TimerRunSystem : ITickable, IDisposable
    {
        private readonly TimerModel _timerModel;
        private readonly GameModeSystem _gameModeSystem;
        private readonly TimedModeSystem _timedModeSystem;
        private readonly BoardSystem _boardSystem;
        private readonly IDisposable _trayRefilledSubscription;
        private readonly IDisposable _gameOverSubscription;
        private readonly IDisposable _modeSubscription;

        private bool _isAppPaused;
        private bool _isMenuPaused;

        public TimerRunSystem(
            TimerModel timerModel,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            BoardSystem boardSystem,
            ISubscriber<TrayRefilledMessage> trayRefilledSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _timerModel = timerModel;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
            _boardSystem = boardSystem;

            _trayRefilledSubscription = trayRefilledSubscriber.Subscribe(OnTrayRefilled);
            _gameOverSubscription = gameOverSubscriber.Subscribe(OnGameOver);
            _modeSubscription = _gameModeSystem.CurrentMode.Subscribe(OnModeChanged);
        }

        /// <summary>
        /// Suspends the countdown without discarding the remaining time. Called only from the app
        /// pause hook: a backgrounded app must neither lose nor gain seconds.
        /// </summary>
        public void SetAppPaused(bool paused) => _isAppPaused = paused;

        /// <summary>
        /// Suspends the countdown while the Settings panel is open, so a player can't lose a timed
        /// run just from opening a menu. Called only from <c>SettingsPanelView.Open</c>/<c>Close</c>.
        /// </summary>
        public void SetMenuPaused(bool paused) => _isMenuPaused = paused;

        void ITickable.Tick()
        {
            if (!_timerModel.IsRunning.Value || _isAppPaused || _isMenuPaused)
            {
                return;
            }

            float remaining = _timerModel.RemainingSeconds.Value - Time.deltaTime;
            if (remaining > 0f)
            {
                _timerModel.RemainingSeconds.Value = remaining;
                return;
            }

            _timerModel.RemainingSeconds.Value = 0f;
            _timerModel.IsRunning.Value = false;
            _boardSystem.ForceGameOver();
        }

        public void Dispose()
        {
            _trayRefilledSubscription.Dispose();
            _gameOverSubscription.Dispose();
            _modeSubscription.Dispose();
        }

        private void OnTrayRefilled(TrayRefilledMessage message)
        {
            if (_gameModeSystem.CurrentMode.Value != GameMode.Timed)
            {
                return;
            }

            _isAppPaused = false;
            _isMenuPaused = false;
            _timerModel.RemainingSeconds.Value = _timedModeSystem.SelectedDuration.Value;
            _timerModel.IsRunning.Value = true;
        }

        /// <summary>
        /// Stops the clock whichever way the run ended — expiring here, or running out of moves. Note
        /// the restart path re-arms it: restarting refills the tray, which is the reset trigger.
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
        }
    }
}
