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
            string requiredPieceId = null)
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

            Id = id;
            Type = type;
            Scope = scope;
            TargetValue = targetValue;
            RequiredLineCount = requiredLineCount;
            RequiredPieceFamily = requiredPieceFamily;
            RequiredOccupancyThreshold = requiredOccupancyThreshold;
            RequiredPieceId = requiredPieceId;
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
    }
}
