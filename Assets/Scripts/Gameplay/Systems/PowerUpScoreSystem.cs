using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Scores power-up clears. Kept apart from <see cref="ScoreSystem"/> because the streak counters
    /// are a placement concept: a power-up is not a placement, so it must never extend, reset or
    /// otherwise touch <see cref="ScoreModel.Streak"/>, <see cref="ScoreModel.MultiClearStreak"/> or
    /// <see cref="ScoreModel.CumulativeMultiClearCount"/>. It reads the streak, never writes it.
    /// <para>
    /// A bomb pays per cell (<see cref="ScoreRules.PlacementScore"/>) because its yield already scales
    /// with how much it destroyed; a row/column clear pays the one-line clear rate, so it is worth the
    /// same as earning that line the hard way.
    /// </para>
    /// </summary>
    public sealed class PowerUpScoreSystem : IDisposable
    {
        private const int POWER_UP_LINES_CLEARED = 1;

        private readonly ScoreModel _scoreModel;
        private readonly IPublisher<ScoreChangedMessage> _scoreChangedPublisher;
        private readonly IDisposable _subscription;

        public PowerUpScoreSystem(
            ScoreModel scoreModel,
            ISubscriber<PowerUpAppliedMessage> powerUpAppliedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher)
        {
            _scoreModel = scoreModel;
            _scoreChangedPublisher = scoreChangedPublisher;
            _subscription = powerUpAppliedSubscriber.Subscribe(OnPowerUpApplied);
        }

        public void Dispose() => _subscription.Dispose();

        private void OnPowerUpApplied(PowerUpAppliedMessage message)
        {
            // A power-up spent on an empty target is still consumed, but it cleared nothing, so there
            // is nothing to pay for and nothing for the HUD to react to.
            if (message.ClearedCellCount <= 0)
            {
                return;
            }

            int gained = message.Kind == PowerUpKind.Bomb
                ? ScoreRules.PlacementScore(message.ClearedCellCount)
                : ScoreRules.ClearScore(POWER_UP_LINES_CLEARED, _scoreModel.Streak.Value);

            _scoreModel.Score.Value += gained;

            _scoreChangedPublisher.Publish(new ScoreChangedMessage(
                _scoreModel.Score.Value, gained, _scoreModel.Streak.Value));
        }
    }
}
