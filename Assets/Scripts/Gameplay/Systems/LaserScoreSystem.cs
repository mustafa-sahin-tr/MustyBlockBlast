using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Scores laser wipes. Kept apart from <see cref="ScoreSystem"/> for the same reason
    /// <see cref="ExplosiveCoreScoreSystem"/> is: the streak counters are a placement concept, and a
    /// wipe is not a placement — the player built the streak that spawned the laser, not the line the
    /// wipe happened to run down. It reads the streak, never writes it.
    /// <para>
    /// A wipe pays per cell (<see cref="ScoreRules.PlacementScore"/>), at exactly the rate a spent Row
    /// Clear or Column Clear pays: the two destroy the same thing — every occupied cell of one line —
    /// so paying them differently would say the same destruction is worth different amounts depending
    /// on what set it off.
    /// </para>
    /// </summary>
    public sealed class LaserScoreSystem : IDisposable
    {
        private readonly ScoreModel _scoreModel;
        private readonly DoubleMultiplierModel _doubleMultiplierModel;
        private readonly IPublisher<ScoreChangedMessage> _scoreChangedPublisher;
        private readonly IDisposable _subscription;

        public LaserScoreSystem(
            ScoreModel scoreModel,
            DoubleMultiplierModel doubleMultiplierModel,
            ISubscriber<LaserFiredMessage> firedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher)
        {
            _scoreModel = scoreModel;
            _doubleMultiplierModel = doubleMultiplierModel;
            _scoreChangedPublisher = scoreChangedPublisher;
            _subscription = firedSubscriber.Subscribe(OnFired);
        }

        public void Dispose() => _subscription.Dispose();

        private void OnFired(LaserFiredMessage message)
        {
            // A wipe whose line was already empty is never published, but a zero count would pay nothing
            // anyway — guarded rather than assumed, so nothing is published for a gain of 0.
            if (message.WipedCellCount <= 0)
            {
                return;
            }

            int gained = ScoreRules.PlacementScore(message.WipedCellCount);
            if (gained <= 0)
            {
                return;
            }

            // Doubled on the way out, like a placement's total and like a power-up clear's: a frenzy
            // applies to every score gain in the run, not only to the ones that came from placing.
            gained = _doubleMultiplierModel.Multiply(gained);

            _scoreModel.Score.Value += gained;

            _scoreChangedPublisher.Publish(new ScoreChangedMessage(
                _scoreModel.Score.Value, gained, _scoreModel.Streak.Value));
        }
    }
}
