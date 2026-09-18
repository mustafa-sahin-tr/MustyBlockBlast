namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Builds the persistence key for one info popup's "seen" flag, and the one key for whether the
    /// already-granted-power-ups migration has run. One key per popup id, so adding a subject never
    /// migrates or invalidates another's saved flag.
    /// <para>
    /// Deliberately its own namespace (<c>InfoPopup.Seen.*</c>) rather than reusing the old
    /// <c>Tutorial.Seen.*</c> keys the spotlight/coach-mark system wrote: the two systems decide "first
    /// time" independently, and a player who dismissed a coach-mark under the old scheme should still
    /// see this feature's popup once.
    /// </para>
    /// </summary>
    internal static class InfoPopupSeenKey
    {
        private const string KEY_PREFIX = "InfoPopup.Seen.";

        /// <summary>Stable storage key for whether <paramref name="id"/> has been auto-shown or manually
        /// opened at least once, e.g. <c>InfoPopup.Seen.PowerUp_Bomb</c>.</summary>
        public static string For(string id) => KEY_PREFIX + id;

        /// <summary>
        /// Whether the one-time "mark already-granted power-ups as seen" migration has already run. Its
        /// own flag rather than inferred from the individual seen-flags, so a player who genuinely
        /// dismisses every popup by hand is never mistaken for the migration having already covered
        /// them, and so the migration itself runs exactly once, ever.
        /// </summary>
        public const string ALREADY_GRANTED_POWERUPS_MIGRATION_DONE = "InfoPopup.Migration.AlreadyGrantedPowerUps.Done";
    }
}
