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
    /// Three independent pause reasons — app backgrounding, a modal menu panel being open, and a
    /// power-up being armed — combine with OR: any one of them holds the clock. Dragging a piece
    /// deliberately does <b>not</b> pause: holding a piece in mid-air would otherwise be free time.
    /// Aiming a power-up does, because the player earned that power-up outside the run and must not
    /// be charged run time for spending it.
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
        private bool _isPowerUpArmedPaused;

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
        /// Suspends the countdown while a modal menu panel is open, so a player can't lose a timed run
        /// just from opening a menu. Called from <c>SettingsPanelView</c> and <c>LevelPathPanelView</c>
        /// on their own open/close edges — one flag rather than one per panel, because the input gate
        /// chain makes the two mutually exclusive, so they can never disagree about who holds it.
        /// </summary>
        public void SetMenuPaused(bool paused) => _isMenuPaused = paused;

        /// <summary>
        /// Suspends the countdown while a power-up is armed and being aimed, so the seconds spent
        /// choosing a target are free. Called only from <c>PowerUpSystem</c>, which arms, cancels and
        /// applies — and therefore owns both edges of this flag.
        /// </summary>
        public void SetPowerUpArmedPaused(bool paused) => _isPowerUpArmedPaused = paused;

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
                return;
            }

            _timerModel.RemainingSeconds.Value = 0f;
            _timerModel.IsRunning.Value = false;
            _boardSystem.ForceGameOver(GameOverReason.TimeUp);
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
            _isPowerUpArmedPaused = false;
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
