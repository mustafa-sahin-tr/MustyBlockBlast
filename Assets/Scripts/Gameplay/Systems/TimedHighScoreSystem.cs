using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="TimedHighScoreModel"/>: one persisted best per timed round length. Endless mode
    /// is untouched — <see cref="ScoreModel.HighScore"/> and <see cref="ScoreSystem"/> keep their exact
    /// previous behaviour and this System never writes to them.
    /// <para>
    /// The best is bumped live on every score change, mirroring what <see cref="ScoreSystem"/> already
    /// does for endless, so the in-run HUD updates the moment a personal best is beaten. That also
    /// makes a separate game-over pass unnecessary: by the time the run ends the best is already
    /// correct. PlayerPrefs writes only happen on an actual new best, not on every placement.
    /// </para>
    /// </summary>
    public sealed class TimedHighScoreSystem : IDisposable
    {
        private readonly TimedHighScoreModel _timedHighScoreModel;
        private readonly GameModeSystem _gameModeSystem;
        private readonly TimedModeSystem _timedModeSystem;
        private readonly IDisposable _subscriptions;

        /// <summary>
        /// Key of the duration the run in progress was started on. Captured at run start rather than
        /// read per write, so changing the picker mid-run can never file a score under a length that
        /// was not actually played. Null while no timed run is in progress.
        /// </summary>
        private string _activeDurationKey;

        public TimedHighScoreSystem(
            TimedHighScoreModel timedHighScoreModel,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<ScoreChangedMessage> scoreChangedSubscriber)
        {
            _timedHighScoreModel = timedHighScoreModel;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            scoreChangedSubscriber.Subscribe(OnScoreChanged).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        private void OnRunStarted(RunStartedMessage message)
        {
            if (_gameModeSystem.CurrentMode.Value != GameMode.Timed)
            {
                // Endless: park the model at zero and drop the key so nothing can be persisted. The
                // value is never displayed outside timed mode, but leaving a stale best sitting in a
                // live ReactiveProperty invites the wrong number flashing up on a later mode switch.
                _activeDurationKey = null;
                _timedHighScoreModel.Best.Value = 0;
                return;
            }

            _activeDurationKey = TimedHighScoreKey.For(_timedModeSystem.SelectedDuration.Value);
            _timedHighScoreModel.Best.Value = PlayerPrefs.GetInt(_activeDurationKey, 0);
        }

        private void OnScoreChanged(ScoreChangedMessage message)
        {
            if (_activeDurationKey == null || _gameModeSystem.CurrentMode.Value != GameMode.Timed)
            {
                return;
            }

            if (message.Total <= _timedHighScoreModel.Best.Value)
            {
                return;
            }

            _timedHighScoreModel.Best.Value = message.Total;
            PlayerPrefs.SetInt(_activeDurationKey, message.Total);
        }
    }
}
