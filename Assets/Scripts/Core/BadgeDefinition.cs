namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Immutable description of one lifetime achievement: which lifetime counter it watches and how
    /// high that counter has to get. Pure data — the mutable side lives in <see cref="BadgeProgress"/>,
    /// mirroring the <see cref="ObjectiveDefinition"/>/<see cref="ObjectiveProgress"/> pair.
    /// <para>
    /// Deliberately carries no reward. The reward is a <c>PowerUpKind</c>, which lives in the Gameplay
    /// assembly, and Core must not depend on Gameplay — so the reward is authored alongside the
    /// threshold in <c>BadgeConfig</c> and paired back up with this definition by <c>Id</c>. The rule
    /// ("has the counter reached the threshold") stays here; the payout stays content.
    /// </para>
    /// </summary>
    public sealed class BadgeDefinition
    {
        public BadgeDefinition(string id, BadgeStatType statType, long threshold)
        {
            if (threshold <= 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(threshold), threshold, "A badge with a non-positive threshold would unlock instantly.");
            }

            Id = id;
            StatType = statType;
            Threshold = threshold;
        }

        /// <summary>Stable identifier. This is what persistence stores for an unlocked badge, so it
        /// must survive retuning the threshold or renaming the badge.</summary>
        public string Id { get; }

        /// <summary>Which lifetime counter this badge watches.</summary>
        public BadgeStatType StatType { get; }

        /// <summary>Value the watched counter must reach for the badge to unlock. Always positive.</summary>
        public long Threshold { get; }
    }
}
