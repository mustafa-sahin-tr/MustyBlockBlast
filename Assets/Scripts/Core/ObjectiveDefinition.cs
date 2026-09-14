namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Immutable description of a single objective: what it measures, how far it has to go and
    /// whether it survives a run. Pure data — the mutable side lives in <see cref="ObjectiveProgress"/>.
    /// </summary>
    public sealed class ObjectiveDefinition
    {
        public ObjectiveDefinition(
            string id,
            ObjectiveType type,
            ObjectiveScope scope,
            int targetValue,
            int requiredLineCount = 0,
            PieceFamily requiredPieceFamily = PieceFamily.Single,
            int requiredOccupancyThreshold = 0,
            string requiredPieceId = null,
            float windowSeconds = 0f)
        {
            if (targetValue <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(targetValue), targetValue, "An objective with a non-positive target can never complete.");
            }

            // ScoreInRun mirrors the live run score rather than counting events, so a Cumulative one
            // would visibly drop every time a new run starts with a lower score — never a coherent design.
            if (type == ObjectiveType.ScoreInRun && scope == ObjectiveScope.Cumulative)
            {
                throw new System.ArgumentException("ScoreInRun objectives cannot be Cumulative — they always reset with the run.", nameof(scope));
            }

            // EarlyScoreRush mirrors the live score exactly like ScoreInRun (see above) — the same
            // "would visibly drop every new run" incoherence applies.
            if (type == ObjectiveType.EarlyScoreRush && scope == ObjectiveScope.Cumulative)
            {
                throw new System.ArgumentException("EarlyScoreRush objectives cannot be Cumulative — they always reset with the run.", nameof(scope));
            }

            // RollingLineClearWindow's window is measured against elapsed-time-since-run-start, a clock that
            // resets to 0 every run. A Cumulative one would compare timestamps across a run boundary
            // as if they were on the same clock, which they are not — the window would silently span
            // runs it has no business spanning.
            if (type == ObjectiveType.RollingLineClearWindow && scope == ObjectiveScope.Cumulative)
            {
                throw new System.ArgumentException("RollingLineClearWindow objectives cannot be Cumulative — their window is measured against a per-run clock.", nameof(scope));
            }

            Id = id;
            Type = type;
            Scope = scope;
            TargetValue = targetValue;
            RequiredLineCount = requiredLineCount;
            RequiredPieceFamily = requiredPieceFamily;
            RequiredOccupancyThreshold = requiredOccupancyThreshold;
            RequiredPieceId = requiredPieceId;
            WindowSeconds = windowSeconds;
        }

        /// <summary>Stable identifier; carried by the progress/completion messages so views can key off it.</summary>
        public string Id { get; }

        public ObjectiveType Type { get; }

        public ObjectiveScope Scope { get; }

        /// <summary>Value <see cref="ObjectiveProgress.CurrentValue"/> must reach for the objective to complete.</summary>
        public int TargetValue { get; }

        /// <summary>Line count a placement must clear to qualify — exact match when <see cref="Type"/>
        /// is <see cref="ObjectiveType.SimultaneousLineClear"/>, a minimum ("at least") when it is
        /// <see cref="ObjectiveType.AtLeastLineClear"/>. Meaningless for every other type.</summary>
        public int RequiredLineCount { get; }

        /// <summary>Shape family a placed piece must belong to in order to qualify. Meaningful only when
        /// <see cref="Type"/> is <see cref="ObjectiveType.PieceFamilyCount"/>.</summary>
        public PieceFamily RequiredPieceFamily { get; }

        /// <summary>Minimum board occupancy (cells occupied immediately before this placement's line
        /// clears resolved) a qualifying placement must meet. Meaningful only when <see cref="Type"/>
        /// is <see cref="ObjectiveType.ClutchRecoveryClear"/>.</summary>
        public int RequiredOccupancyThreshold { get; }

        /// <summary>Exact catalog piece id (e.g. <c>"square_3x3"</c>) a placement must use to qualify —
        /// finer-grained than <see cref="RequiredPieceFamily"/>, which cannot tell a 2x2 square from a
        /// 3x3 one. Meaningful only when <see cref="Type"/> is <see cref="ObjectiveType.PieceIdCount"/>
        /// or <see cref="ObjectiveType.PieceIdLineClear"/>.</summary>
        public string RequiredPieceId { get; }

        /// <summary>Width of the rolling window (seconds) for <see cref="ObjectiveType.RollingLineClearWindow"/>,
        /// or the deadline (seconds from run start) for <see cref="ObjectiveType.EarlyScoreRush"/>.
        /// Meaningless for every other type.</summary>
        public float WindowSeconds { get; }
    }
}
