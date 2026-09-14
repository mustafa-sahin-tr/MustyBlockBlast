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
            PieceFamily requiredPieceFamily = PieceFamily.Single)
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
        }

        /// <summary>Stable identifier; carried by the progress/completion messages so views can key off it.</summary>
        public string Id { get; }

        public ObjectiveType Type { get; }

        public ObjectiveScope Scope { get; }

        /// <summary>Value <see cref="ObjectiveProgress.CurrentValue"/> must reach for the objective to complete.</summary>
        public int TargetValue { get; }

        /// <summary>Exact simultaneous line count a placement must clear to qualify. Meaningful only when
        /// <see cref="Type"/> is <see cref="ObjectiveType.SimultaneousLineClear"/>.</summary>
        public int RequiredLineCount { get; }

        /// <summary>Shape family a placed piece must belong to in order to qualify. Meaningful only when
        /// <see cref="Type"/> is <see cref="ObjectiveType.PieceFamilyCount"/>.</summary>
        public PieceFamily RequiredPieceFamily { get; }
    }
}
