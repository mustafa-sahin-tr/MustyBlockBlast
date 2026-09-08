using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="ScoreModel"/>. Reacts to placements only — it never sees the board and never
    /// references BoardSystem. All arithmetic comes from <see cref="ScoreRules"/>.
    /// </summary>
    public sealed class ScoreSystem : IDisposable
    {
        private readonly ScoreModel _scoreModel;
        private readonly IPublisher<ScoreChangedMessage> _scoreChangedPublisher;
        private readonly IDisposable _subscriptions;

        public ScoreSystem(
            ScoreModel scoreModel,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher)
        {
            _scoreModel = scoreModel;
            _scoreChangedPublisher = scoreChangedPublisher;

            var bag = DisposableBag.CreateBuilder();
            piecePlacedSubscriber.Subscribe(OnPiecePlaced).AddTo(bag);
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        private void OnRunStarted(RunStartedMessage message)
        {
            _scoreModel.Score.Value = 0;
            _scoreModel.Streak.Value = 0;
            _scoreChangedPublisher.Publish(new ScoreChangedMessage(0, 0, 0));
        }

        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            int gained = ScoreRules.PlacementScore(message.CellCount);

            if (message.LinesCleared > 0)
            {
                gained += ScoreRules.ClearScore(message.LinesCleared, _scoreModel.Streak.Value);
                _scoreModel.Streak.Value += 1;
            }
            else
            {
                _scoreModel.Streak.Value = 0;
            }

            _scoreModel.Score.Value += gained;
            if (_scoreModel.Score.Value > _scoreModel.HighScore.Value)
            {
                _scoreModel.HighScore.Value = _scoreModel.Score.Value;
            }

            _scoreChangedPublisher.Publish(new ScoreChangedMessage(
                _scoreModel.Score.Value, gained, _scoreModel.Streak.Value));
        }
    }
}
