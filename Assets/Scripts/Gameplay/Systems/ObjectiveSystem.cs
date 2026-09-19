using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

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
    /// <para>
    /// Elapsed-run-time for rolling-window objectives (<see cref="ObjectiveType.RollingLineClearWindow"/>,
    /// <see cref="ObjectiveType.EarlyScoreRush"/>) is wall-clock time with any paused spans subtracted
    /// out, mirroring <see cref="RunPauseModel"/>'s three pause reasons — the same ones
    /// <see cref="TimerRunSystem"/>'s countdown already respects. This keeps opening a menu, arming a
    /// power-up, or backgrounding the app from silently burning a burst window or a rush deadline.
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
        private float _runStartTimestamp;
        private float _accumulatedPausedSeconds;
        private float? _pauseStartedAt;

        public ObjectiveSystem(
            ObjectiveModel objectiveModel,
            RunPauseModel runPauseModel,
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
            runPauseModel.IsPaused.Subscribe(OnPauseChanged).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        private void OnScoreChanged(ScoreChangedMessage message)
        {
            _currentRunScore = message.Total;
            _currentStreak = message.Streak;
        }

        /// <summary>
        /// Records the start/end of a paused span so <see cref="ElapsedRunSeconds"/> can subtract it
        /// out. <see cref="RunPauseModel.IsPaused"/> invokes this immediately on subscribe with the
        /// current value, which is harmless here — a false-to-false "change" leaves both fields as-is.
        /// </summary>
        private void OnPauseChanged(bool isPaused)
        {
            if (isPaused)
            {
                _pauseStartedAt = Time.realtimeSinceStartup;
                return;
            }

            if (_pauseStartedAt.HasValue)
            {
                _accumulatedPausedSeconds += Time.realtimeSinceStartup - _pauseStartedAt.Value;
                _pauseStartedAt = null;
            }
        }

        private void OnRunStarted(RunStartedMessage message)
        {
            _currentRunScore = 0;
            _currentStreak = 0;
            _runStartTimestamp = Time.realtimeSinceStartup;
            _accumulatedPausedSeconds = 0f;

            // A run can legitimately start while already paused: confirming a mode switch in the
            // settings panel calls StartNewRun without closing the panel, so the menu pause is still
            // held. Re-base the in-progress pause onto the new run's clock rather than dropping it —
            // clearing it here would leave the unpause edge with nothing to subtract, and every second
            // the panel stayed open would count as elapsed run time.
            if (_pauseStartedAt.HasValue)
            {
                _pauseStartedAt = _runStartTimestamp;
            }

            IReadOnlyList<ObjectiveProgress> objectives = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
            {
                // Cumulative objectives no-op inside ResetForNewRun, so no scope check belongs here.
                objectives[objectiveIndex].ResetForNewRun();
            }
        }

        /// <summary>Wall-clock seconds since the run started, minus every paused span — including one
        /// still in progress right now, so a placement that somehow lands mid-pause never counts the
        /// live pause as elapsed time.</summary>
        private float ElapsedRunSeconds()
        {
            float livePause = _pauseStartedAt.HasValue
                ? Time.realtimeSinceStartup - _pauseStartedAt.Value
                : 0f;
            return Time.realtimeSinceStartup - _runStartTimestamp - _accumulatedPausedSeconds - livePause;
        }

        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            float elapsedRunSeconds = ElapsedRunSeconds();

            ObjectivePlacementContext context = new ObjectivePlacementContext(
                message.LinesCleared,
                message.RowsCleared,
                message.ColumnsCleared,
                message.PieceFamily,
                message.PieceId,
                _currentRunScore,
                message.BoardEmptyAfterPlacement,
                _currentStreak,
                message.OccupiedCellCountBeforeClear,
                message.AnyCornerCleared,
                message.CenterCoreEmptyAfterPlacement,
                message.HasIsolatedHolesAfterPlacement,
                elapsedRunSeconds,
                message.ReinforcedCellsFullyClearedCount,
                message.DestroyedCellCountByColour,
                message.TimerCellsClearedInTimeCount);

            ApplyToAllObjectives(objective => objective.ApplyPlacement(context));
        }

        /// <summary>
        /// A Bomb clear, a "clutch" Reroll and a reinforced cell finished off by a spent power-up are
        /// not placements — none publishes <see cref="PiecePlacedMessage"/> — so each needs its own
        /// event source into the objective engine rather than being folded into
        /// <see cref="OnPiecePlaced"/>'s context.
        /// <para>
        /// The reinforced-cell branch is checked first and independently rather than chained onto the
        /// other two, because it is the only one not keyed to a single <see cref="PowerUpKind"/>: any
        /// kind that clears a region can finish a reinforced cell off, so one message can legitimately
        /// need this branch AND the Bomb branch below (which early-returns) to fire.
        /// </para>
        /// </summary>
        private void OnPowerUpApplied(PowerUpAppliedMessage message)
        {
            if (message.ReinforcedCellsFullyClearedCount > 0)
            {
                ApplyToAllObjectives(objective =>
                    objective.ApplyPowerUpReinforcedCellsCleared(message.ReinforcedCellsFullyClearedCount));
            }

            if (message.TimerCellsClearedInTimeCount > 0)
            {
                ApplyToAllObjectives(objective =>
                    objective.ApplyPowerUpTimerCellsClearedInTime(message.TimerCellsClearedInTimeCount));
            }

            // Before the kind-specific branches below, which return early: a colour-count objective
            // takes every kind's destroyed cells, whatever else that kind meant to another objective.
            if (message.DestroyedCellCountByColour != null && message.ClearedCellCount > 0)
            {
                ApplyToAllObjectives(objective =>
                    objective.ApplyPowerUpColourCleared(message.DestroyedCellCountByColour));
            }

            if (message.Kind == PowerUpKind.Bomb && message.EmptiedLineCount > 0)
            {
                ApplyToAllObjectives(objective => objective.ApplyPowerUpLineEmptied());
                return;
            }

            if (message.Kind == PowerUpKind.Reroll && message.WasClutchSave)
            {
                ApplyToAllObjectives(objective => objective.ApplyPowerUpRerollSave());
            }
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
