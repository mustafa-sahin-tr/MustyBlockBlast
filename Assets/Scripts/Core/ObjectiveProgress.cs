using System;
using System.Collections.Generic;

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

        /// <summary>Timestamp (elapsed-run-seconds) and line count of every recent qualifying clear
        /// still inside the rolling window, oldest first. Meaningful only for
        /// <see cref="ObjectiveType.RollingLineClearWindow"/>. Entries are appended in increasing timestamp
        /// order (elapsed-run-seconds only ever grows within a run), so purging expired entries from
        /// the front is always correct and never needs to scan the middle of the list.</summary>
        private readonly List<(float Timestamp, int LinesCleared)> _recentBurstEvents =
            new List<(float, int)>();

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

                case ObjectiveType.RollingLineClearWindow:
                    if (context.LinesCleared >= 1)
                    {
                        _recentBurstEvents.Add((context.ElapsedRunSeconds, context.LinesCleared));
                    }

                    // Entries are appended in increasing timestamp order, so anything expired is
                    // always at the front — no need to scan past the first surviving entry.
                    float windowStart = context.ElapsedRunSeconds - Definition.WindowSeconds;
                    while (_recentBurstEvents.Count > 0 && _recentBurstEvents[0].Timestamp < windowStart)
                    {
                        _recentBurstEvents.RemoveAt(0);
                    }

                    int burstLineTotal = 0;
                    for (int burstIndex = 0; burstIndex < _recentBurstEvents.Count; burstIndex++)
                    {
                        burstLineTotal += _recentBurstEvents[burstIndex].LinesCleared;
                    }

                    // Not a high-water mark: a live reading of "lines in the window right now", which
                    // can fall back down as old entries expire. The shared IsComplete latch below is
                    // what keeps a target once reached from being un-reached by a later expiry.
                    CurrentValue = Math.Min(burstLineTotal, Definition.TargetValue);
                    break;

                case ObjectiveType.EarlyScoreRush:
                    // Only updates while still inside the deadline — once elapsed time passes
                    // WindowSeconds this branch simply stops running, freezing CurrentValue at
                    // whatever it last read rather than completing late.
                    if (context.ElapsedRunSeconds <= Definition.WindowSeconds)
                    {
                        CurrentValue = Math.Min(context.CurrentRunScore, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.ColourCleared:
                    // Counts THINGS DESTROYED like ReinforcedCellsCleared below, not events: one
                    // placement that completes two lines of the wanted colour credits every one of
                    // those cells. Keyed on the colour id alone — never on any theme's rendering of it —
                    // which is what keeps progress a pure function of the board across a theme switch.
                    int destroyedOfColour = context.DestroyedCountOf(Definition.RequiredColourId);
                    if (destroyedOfColour > 0)
                    {
                        CurrentValue = Math.Min(CurrentValue + destroyedOfColour, Definition.TargetValue);
                    }

                    break;

                case ObjectiveType.ReinforcedCellsCleared:
                    // Advances by the count this placement actually removed, not by a flat +1 like
                    // every other counting type above. Those count EVENTS ("a clear that qualified
                    // happened"), and one placement is one event however much it cleared. This one
                    // counts THINGS DESTROYED, and one cleared row can finish off two reinforced cells
                    // at once — crediting only 1 would make "clear all N" unreachable on a board where
                    // two of them share a line, so each destruction is credited separately.
                    if (context.ReinforcedCellsFullyCleared > 0)
                    {
                        CurrentValue = Math.Min(
                            CurrentValue + context.ReinforcedCellsFullyCleared, Definition.TargetValue);
                    }

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
        /// Folds one "clutch" Reroll save into this objective's progress. Deliberately a separate
        /// method from <see cref="ApplyPlacement"/>, mirroring <see cref="ApplyPowerUpLineEmptied"/>:
        /// a Reroll is not a placement, and the qualifying condition (zero legal moves before, at
        /// least one after) is decided entirely by the caller before this is invoked — this method
        /// only knows "a clutch save just happened", the same trust boundary
        /// <see cref="ApplyPowerUpLineEmptied"/> already draws for Bomb.
        /// </summary>
        public bool ApplyPowerUpRerollSave()
        {
            if (IsComplete || Definition.Type != ObjectiveType.RerollSave)
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
        /// Folds the reinforced cells a spent power-up finished off into this objective's progress.
        /// Deliberately a separate method from <see cref="ApplyPlacement"/> for exactly the reason
        /// <see cref="ApplyPowerUpLineEmptied"/> already gives: a power-up application is not a
        /// placement, and feeding one through a synthetic <see cref="ObjectivePlacementContext"/> would
        /// risk its default field values satisfying an unrelated objective type that never saw a
        /// placement at all.
        /// <para>
        /// Unlike the other two power-up-sourced types, <see cref="ApplyPlacement"/> ALSO advances
        /// <see cref="ObjectiveType.ReinforcedCellsCleared"/> — and must. A Bomb-induced line clear and
        /// a clutch Reroll are only ever power-up events, so for those two this method shape is the
        /// whole story; a reinforced cell, by contrast, dies just as readily to an ordinary placement's
        /// line clear. Both routes are the same destruction from the objective's point of view, so both
        /// credit it, and the two paths are disjoint by construction: a placement publishes
        /// <c>PiecePlacedMessage</c> and a spent power-up publishes <c>PowerUpAppliedMessage</c>, never
        /// both for one destruction, so nothing is ever counted twice.
        /// </para>
        /// <para>
        /// Takes a count rather than being a bare "one happened" signal, because a single power-up clear
        /// can finish off several reinforced cells at once — see the placement branch in
        /// <see cref="ApplyPlacement"/> for why each destruction is credited separately.
        /// </para>
        /// </summary>
        public bool ApplyPowerUpReinforcedCellsCleared(int count)
        {
            if (IsComplete || Definition.Type != ObjectiveType.ReinforcedCellsCleared || count <= 0)
            {
                return false;
            }

            int previousValue = CurrentValue;
            CurrentValue = Math.Min(CurrentValue + count, Definition.TargetValue);
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
        /// <summary>
        /// Credits the cells a power-up clear destroyed, by colour, to a <see cref="ObjectiveType.ColourCleared"/>
        /// objective — the power-up mirror of the placement branch above, as
        /// <see cref="ApplyPowerUpReinforcedCellsCleared"/> is of its own. A clear that took none of
        /// the wanted colour (or a null tally) changes nothing and returns false.
        /// </summary>
        public bool ApplyPowerUpColourCleared(IReadOnlyList<int> destroyedCellCountByColour)
        {
            if (IsComplete || Definition.Type != ObjectiveType.ColourCleared)
            {
                return false;
            }

            int destroyedOfColour = ColourTally.CountOf(destroyedCellCountByColour, Definition.RequiredColourId);
            if (destroyedOfColour <= 0)
            {
                return false;
            }

            int previousValue = CurrentValue;
            CurrentValue = Math.Min(CurrentValue + destroyedOfColour, Definition.TargetValue);
            if (CurrentValue == previousValue)
            {
                return false;
            }

            IsComplete = CurrentValue >= Definition.TargetValue;
            return true;
        }

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
            _recentBurstEvents.Clear();
        }
    }
}
