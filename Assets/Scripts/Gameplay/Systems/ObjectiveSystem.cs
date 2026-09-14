using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="ObjectiveModel"/>. Reacts to events only — placements and power-up
    /// applications — and never sees the board; all rules live in <see cref="ObjectiveProgress"/>, so a
    /// new objective type is a Core change and this class does not move.
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
        private int _currentStreak;

        public ObjectiveSystem(
            ObjectiveModel objectiveModel,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<ScoreChangedMessage> scoreChangedSubscriber,
            ISubscriber<PowerUpAppliedMessage> powerUpAppliedSubscriber,
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
            powerUpAppliedSubscriber.Subscribe(OnPowerUpApplied).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        private void OnScoreChanged(ScoreChangedMessage message)
        {
            _currentRunScore = message.Total;
            _currentStreak = message.Streak;
        }

        private void OnRunStarted(RunStartedMessage message)
        {
            _currentRunScore = 0;
            _currentStreak = 0;

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
                message.RowsCleared,
                message.ColumnsCleared,
                message.PieceFamily,
                _currentRunScore,
                message.BoardEmptyAfterPlacement,
                _currentStreak);

            ApplyToAllObjectives(objective => objective.ApplyPlacement(context));
        }

        /// <summary>
        /// A Bomb clear is not a placement — it never publishes <see cref="PiecePlacedMessage"/> — so
        /// it needs its own event source into the objective engine rather than being folded into
        /// <see cref="OnPiecePlaced"/>'s context.
        /// </summary>
        private void OnPowerUpApplied(PowerUpAppliedMessage message)
        {
            if (message.Kind != PowerUpKind.Bomb || message.EmptiedLineCount <= 0)
            {
                return;
            }

            ApplyToAllObjectives(objective => objective.ApplyPowerUpLineEmptied());
        }

        /// <summary>
        /// Shared fold-and-publish: applies <paramref name="apply"/> to every tracked objective and
        /// announces exactly the two things a change can mean — progress moved, and/or the objective
        /// just completed (the false-to-true edge, so <see cref="ObjectiveCompletedMessage"/> fires
        /// exactly once regardless of which event source drove the change).
        /// </summary>
        private void ApplyToAllObjectives(Func<ObjectiveProgress, bool> apply)
        {
            IReadOnlyList<ObjectiveProgress> objectives = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
            {
                ObjectiveProgress objective = objectives[objectiveIndex];

                bool wasComplete = objective.IsComplete;
                if (!apply(objective))
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
