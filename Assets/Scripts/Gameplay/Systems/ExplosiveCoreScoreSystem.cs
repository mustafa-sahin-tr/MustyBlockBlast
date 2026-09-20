using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Scores explosive-core detonations. Kept apart from <see cref="ScoreSystem"/> for the same reason
    /// <see cref="PowerUpScoreSystem"/> is: the streak counters are a placement concept, and a
    /// detonation is not a placement — the player lined up the row and column that spawned the core, not
    /// the lines it went on to finish for them. It reads the streak, never writes it.
    /// <para>
    /// A finished line pays the ordinary line-clear rate (<see cref="ScoreRules.ClearScore"/>), exactly
    /// as a placement completing that same line the hard way would — the whole point of the mechanic is
    /// that a near-complete line finished by the core is worth what finishing it normally would have
    /// been, never a flat per-cell blast payout.
    /// </para>
    /// </summary>
    public sealed class ExplosiveCoreScoreSystem : IDisposable
    {
        private readonly ScoreModel _scoreModel;
        private readonly DoubleMultiplierModel _doubleMultiplierModel;
        private readonly IPublisher<ScoreChangedMessage> _scoreChangedPublisher;
        private readonly IDisposable _subscription;

        public ExplosiveCoreScoreSystem(
            ScoreModel scoreModel,
            DoubleMultiplierModel doubleMultiplierModel,
            ISubscriber<ExplosiveCoreDetonatedMessage> detonatedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher)
        {
            _scoreModel = scoreModel;
            _doubleMultiplierModel = doubleMultiplierModel;
            _scoreChangedPublisher = scoreChangedPublisher;
            _subscription = detonatedSubscriber.Subscribe(OnDetonated);
        }

        public void Dispose() => _subscription.Dispose();

        private void OnDetonated(ExplosiveCoreDetonatedMessage message)
        {
            // A hand-off finished no line — there is nothing to pay for, and a zero count would pay
            // nothing anyway, so this is guarded rather than assumed.
            if (message.FinishedLineCount <= 0)
            {
                return;
            }

            int gained = ScoreRules.ClearScore(message.FinishedLineCount, _scoreModel.Streak.Value);
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
