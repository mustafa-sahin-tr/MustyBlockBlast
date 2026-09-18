namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Builds the persistence key for one tutorial step's "seen" flag, and the one key for whether the
    /// already-unlocked-power-ups migration has run. Mirrors <see cref="PowerUpInventoryKey"/>: one key
    /// per step id, so adding a step never migrates or invalidates another's saved flag.
    /// </summary>
    internal static class TutorialSeenKey
    {
        private const string KEY_PREFIX = "Tutorial.Seen.";

        /// <summary>Stable storage key for whether <paramref name="stepId"/> has been shown and
        /// acknowledged, e.g. <c>Tutorial.Seen.PowerUpUnlocked_Bomb</c>.</summary>
        public static string For(string stepId) => KEY_PREFIX + stepId;

        /// <summary>
        /// Whether the one-time "mark already-unlocked power-ups as seen" migration has already run.
        /// Its own flag rather than inferred from the individual seen-flags, so a player who genuinely
        /// dismisses every power-up coach-mark by hand is never mistaken for the migration having
        /// already covered them, and so the migration itself runs exactly once, ever.
        /// </summary>
        public const string ALREADY_UNLOCKED_MIGRATION_DONE = "Tutorial.Migration.AlreadyUnlockedPowerUps.Done";
    }
}
