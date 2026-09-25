using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Pays the puzzle-link bonus (issue #483): <see cref="ScoreRules.PUZZLE_LINK_BONUS_PER_CELL"/> for
    /// every link cell a whole-group removal took out, doubled during a frenzy like every other score gain
    /// — the same shape as <see cref="ExplosiveCoreScoreSystem"/>'s bonus for a core's wipe.
    /// </summary>
    public sealed class PuzzleLinkScoreSystem : IDisposable
    {
        private readonly ScoreModel _scoreModel;
        private readonly DoubleMultiplierModel _doubleMultiplierModel;
        private readonly IPublisher<ScoreChangedMessage> _scoreChangedPublisher;
        private readonly IDisposable _subscription;

        public PuzzleLinkScoreSystem(
            ScoreModel scoreModel,
            DoubleMultiplierModel doubleMultiplierModel,
            ISubscriber<PuzzleLinksClearedMessage> clearedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher)
        {
            _scoreModel = scoreModel;
            _doubleMultiplierModel = doubleMultiplierModel;
            _scoreChangedPublisher = scoreChangedPublisher;
            _subscription = clearedSubscriber.Subscribe(OnCleared);
        }

        public void Dispose() => _subscription.Dispose();

        private void OnCleared(PuzzleLinksClearedMessage message)
        {
            if (message.CellCount <= 0)
            {
                return;
            }

            int gained = _doubleMultiplierModel.Multiply(message.CellCount * ScoreRules.PUZZLE_LINK_BONUS_PER_CELL);
            _scoreModel.Score.Value += gained;
            _scoreChangedPublisher.Publish(new ScoreChangedMessage(
                _scoreModel.Score.Value, gained, _scoreModel.Streak.Value));
        }
    }
}
