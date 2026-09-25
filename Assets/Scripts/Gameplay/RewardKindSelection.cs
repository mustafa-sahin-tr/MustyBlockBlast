namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// How a <see cref="Settings.RewardRuleConfig"/> picks the power-up kind it pays (issue #464).
    /// Serialized by value in the rule catalog asset — append only.
    /// </summary>
    public enum RewardKindSelection
    {
        /// <summary>
        /// A kind drawn from those unlocked once the level is cleared, preferring one the level's own
        /// reward did not already pay — see <see cref="LevelCompletionRewards.DrawBonusKind(int, int)"/>.
        /// </summary>
        DrawUnlocked,

        /// <summary>
        /// Always the rule's authored kind. Falls back to a drawn kind while that kind is still locked,
        /// so a rule never pays into a slot the strip does not show yet.
        /// </summary>
        Fixed,
    }
}
