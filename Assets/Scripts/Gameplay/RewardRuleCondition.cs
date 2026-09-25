namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// What a <see cref="Settings.RewardRuleConfig"/> watches for (issue #464). Serialized by value in
    /// the rule catalog asset, so a new condition is only ever appended — reordering would silently
    /// turn every authored rule into a different one.
    /// </summary>
    public enum RewardRuleCondition
    {
        /// <summary>
        /// The player's run of Path levels cleared on the first attempt, one after another. Pays each
        /// time the streak reaches a multiple of the rule's threshold — see
        /// <see cref="Systems.RewardRuleSystem"/> for what extends and what breaks the streak.
        /// </summary>
        ConsecutiveFirstTryClears,
    }
}
