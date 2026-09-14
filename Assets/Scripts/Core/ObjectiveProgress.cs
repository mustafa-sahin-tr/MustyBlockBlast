using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Live progress of one <see cref="ObjectiveDefinition"/>. The whole objective rule engine lives
    /// here: a placement is fed in, each type decides whether it qualifies, and the value is clamped to
    /// the target so a completed objective can never overshoot.
    /// </summary>
    public sealed class ObjectiveProgress
    {
        /// <summary>The ongoing (not best-ever) run of consecutive placements with no isolated holes.
        /// Meaningful only for <see cref="ObjectiveType.NoIsolatedHolesStreak"/> — every other type
        /// leaves this at 0 and never reads it. Separate from <see cref="CurrentValue"/>, which tracks
        /// the high-water mark this feeds, the same split <see cref="ObjectiveType.StreakThreshold"/>
        /// uses for the combo streak.</summary>
        private int _consecutiveNoIsolatedHolesCount;

        public ObjectiveProgress(ObjectiveDefinition definition)
        {
            Definition = definition;
        }

        public ObjectiveDefinition Definition { get; }

        public int CurrentValue { get; private set; }

        public bool IsComplete { get; private set; }

        /// <summary>
        /// Folds one placement into this objective's progress. Returns true only when
        /// <see cref="CurrentValue"/> actually moved, so callers can publish a change message without
        /// filtering no-ops. A completed objective ignores everything.
        /// </summary>
        public bool ApplyPlacement(ObjectivePlacementContext context)
        {
            if (IsComplete)
            {
                return false;
            }

            int previousValue = CurrentValue;

            switch (Definition.Type)
            {
                case ObjectiveType.SimultaneousLineClear:
                    // Exact match, not "at least": a "clear 2 lines at once" objective is not satisfied
                    // by a 3-line clear, which is its own, harder objective.
                    if (context.LinesCleared == Definition.RequiredLineCount)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.PieceFamilyCount:
                    if (context.PieceFamily == Definition.RequiredPieceFamily)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.BoardWipeCount:
                    if (context.BoardEmptyAfterPlacement)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.ScoreInRun:
                    // Mirrors the score rather than counting events. Only monotonic within a single run
                    // (the run score only grows), which is why ObjectiveDefinition forbids pairing this
                    // type with Cumulative scope — ResetForNewRun is what keeps it from going backwards.
                    CurrentValue = Math.Min(context.CurrentRunScore, Definition.TargetValue);
                    break;

                case ObjectiveType.RowAndColumnCrossClear:
                    if (context.RowsCleared >= 1 && context.ColumnsCleared >= 1)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.AtLeastLineClear:
                    // "At least", not exact: deliberately the opposite rule from SimultaneousLineClear
                    // above, which is why this is its own type rather than a flag on that one.
                    if (context.LinesCleared >= Definition.RequiredLineCount)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.PieceIdCount:
                    if (context.PieceId == Definition.RequiredPieceId)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.PieceIdLineClear:
                    if (context.PieceId == Definition.RequiredPieceId && context.LinesCleared >= 1)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.ClutchRecoveryClear:
                    if (context.LinesCleared >= 1
                        && context.OccupiedCellCountBeforeClear >= Definition.RequiredOccupancyThreshold)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.StreakThreshold:
                    // Unlike ScoreInRun, the streak itself is NOT monotonic — it drops to 0 on a
                    // non-clearing placement. Tracking the high-water mark (rather than mirroring the
                    // live value) is what stops an earlier peak from being erased by a later reset, and
                    // is also what stops two separate streaks from summing toward the target: a streak
                    // of 4 then a reset then a streak of 6 reads as "best streak 6", never "10".
                    CurrentValue = Math.Max(CurrentValue, Math.Min(context.CurrentStreak, Definition.TargetValue));
                    break;

                case ObjectiveType.FourCornersCleared:
                    if (context.AnyCornerCleared)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.CenterCoreEvacuated:
                    if (context.CenterCoreEmptyAfterPlacement)
                    {
                        CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.NoIsolatedHolesStreak:
                    // Every placement counts here, not just clearing ones — this is a hygiene streak,
                    // not a clear-event counter. Mirrors StreakThreshold's high-water-mark split: the
                    // live streak can drop to 0, but CurrentValue (the best run ever) never does.
                    _consecutiveNoIsolatedHolesCount = context.HasIsolatedHolesAfterPlacement
                        ? 0
                        : _consecutiveNoIsolatedHolesCount + 1;
                    CurrentValue = Math.Max(
                        CurrentValue, Math.Min(_consecutiveNoIsolatedHolesCount, Definition.TargetValue));
                    break;
            }

            if (CurrentValue == previousValue)
            {
                return false;
            }

            IsComplete = CurrentValue >= Definition.TargetValue;
            return true;
        }

        /// <summary>
        /// Folds one Bomb-induced empty line into this objective's progress. Deliberately a separate
        /// method from <see cref="ApplyPlacement"/> rather than a field on <see cref="ObjectivePlacementContext"/>:
        /// a power-up application is not a placement, and reusing the placement context here would risk
        /// a synthetic context accidentally satisfying an unrelated objective type (e.g. a stray
        /// default <see cref="PieceFamily"/> value matching a <see cref="ObjectiveType.PieceFamilyCount"/>
        /// objective that never actually saw a placement). Gating on <see cref="ObjectiveDefinition.Type"/>
        /// up front makes every other type structurally immune to a power-up event, not just
        /// incidentally so.
        /// </summary>
        public bool ApplyPowerUpLineEmptied()
        {
            if (IsComplete || Definition.Type != ObjectiveType.BombInducedLineClear)
            {
                return false;
            }

            int previousValue = CurrentValue;
            CurrentValue = Math.Min(CurrentValue + 1, Definition.TargetValue);
            if (CurrentValue == previousValue)
            {
                return false;
            }

            IsComplete = CurrentValue >= Definition.TargetValue;
            return true;
        }

        /// <summary>
        /// Rehydrates persisted progress (e.g. after an app relaunch) without going through
        /// <see cref="ApplyPlacement"/>'s qualification rules. Clamps to the target and re-derives
        /// <see cref="IsComplete"/> exactly like a normal update would.
        /// <para>
        /// Deliberately silent: nothing happened this session, so the caller must not treat a restore
        /// as a progress or completion event.
        /// </para>
        /// </summary>
        public void RestoreProgress(int currentValue)
        {
            CurrentValue = Math.Min(Math.Max(currentValue, 0), Definition.TargetValue);
            IsComplete = CurrentValue >= Definition.TargetValue;
        }

        /// <summary>
        /// Clears progress at the start of a run. Cumulative objectives deliberately ignore this — that
        /// is the entire difference between the two scopes.
        /// </summary>
        public void ResetForNewRun()
        {
            if (Definition.Scope == ObjectiveScope.Cumulative)
            {
                return;
            }

            CurrentValue = 0;
            IsComplete = false;
            _consecutiveNoIsolatedHolesCount = 0;
        }
    }
}
