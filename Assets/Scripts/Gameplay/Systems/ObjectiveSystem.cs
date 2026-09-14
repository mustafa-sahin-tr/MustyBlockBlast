using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="ObjectiveModel"/>. Like <see cref="ScoreSystem"/> it reacts to placements only
    /// and never sees the board; all rules live in <see cref="ObjectiveProgress"/>, so a new objective
    /// type is a Core change and this class does not move.
    /// <para>
    /// Reads the run score from <see cref="ScoreChangedMessage"/> rather than <see cref="ScoreModel"/>
    /// directly: <see cref="ScoreSystem"/> publishes it synchronously after writing
    /// <see cref="ScoreModel.Score"/> on the same <see cref="PiecePlacedMessage"/>, so caching it here
    /// makes the data dependency explicit instead of relying on MessagePipe subscriber resolve order.
    /// </para>
    /// </summary>
    public sealed class ObjectiveSystem : IDisposable
    {
        private readonly ObjectiveModel _objectiveModel;
        private readonly IPublisher<ObjectiveProgressChangedMessage> _progressChangedPublisher;
        private readonly IPublisher<ObjectiveCompletedMessage> _completedPublisher;
        private readonly IDisposable _subscriptions;

        private int _currentRunScore;

        public ObjectiveSystem(
            ObjectiveModel objectiveModel,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<ScoreChangedMessage> scoreChangedSubscriber,
            IPublisher<ObjectiveProgressChangedMessage> progressChangedPublisher,
            IPublisher<ObjectiveCompletedMessage> completedPublisher)
        {
            _objectiveModel = objectiveModel;
            _progressChangedPublisher = progressChangedPublisher;
            _completedPublisher = completedPublisher;

            var bag = DisposableBag.CreateBuilder();
            piecePlacedSubscriber.Subscribe(OnPiecePlaced).AddTo(bag);
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            scoreChangedSubscriber.Subscribe(OnScoreChanged).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        private void OnScoreChanged(ScoreChangedMessage message)
        {
            _currentRunScore = message.Total;
        }

        private void OnRunStarted(RunStartedMessage message)
        {
            _currentRunScore = 0;

            IReadOnlyList<ObjectiveProgress> objectives = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
            {
                // Cumulative objectives no-op inside ResetForNewRun, so no scope check belongs here.
                objectives[objectiveIndex].ResetForNewRun();
            }
        }

        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            ObjectivePlacementContext context = new ObjectivePlacementContext(
                message.LinesCleared,
                message.PieceFamily,
                _currentRunScore,
                message.BoardEmptyAfterPlacement);

            IReadOnlyList<ObjectiveProgress> objectives = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
            {
                ObjectiveProgress objective = objectives[objectiveIndex];

                // Captured before applying: completion is the false -> true edge, which is what makes
                // ObjectiveCompletedMessage fire exactly once.
                bool wasComplete = objective.IsComplete;
                if (!objective.ApplyPlacement(context))
                {
                    continue;
                }

                _progressChangedPublisher.Publish(new ObjectiveProgressChangedMessage(
                    objective.Definition.Id, objective.CurrentValue, objective.Definition.TargetValue));

                if (!wasComplete && objective.IsComplete)
                {
                    _completedPublisher.Publish(new ObjectiveCompletedMessage(objective.Definition.Id));
                }
            }
        }
    }
}
