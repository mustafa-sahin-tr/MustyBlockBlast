namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The rule set a run is played under.
    /// <para>
    /// Display names (issue #355) have drifted from these member identifiers, which are kept exactly
    /// as they are so <c>GameModeSystem</c>'s persisted <c>(int)mode</c> PlayerPrefs value never needs
    /// a save-data migration:
    /// <list type="bullet">
    /// <item><see cref="Endless"/> is shown to the player as "Şölen Modu". Its ruleset is unchanged:
    /// no goal, special cells and power-ups both enabled, never ended by time.</item>
    /// <item><see cref="Timed"/> is shown to the player as "Klasik Mod" ("Classic"). Its ruleset has
    /// changed: <see cref="Gameplay.Models.GameModeModel.ExtrasEnabled"/> is false for this member —
    /// no special cells, no power-ups — whatever duration is selected, including the "Sınırsız"
    /// (endless) sentinel duration that plays it forever exactly like <see cref="Endless"/> does,
    /// minus the extras. A real duration still adds the countdown this member has always had, which
    /// resets on every tray refill and ends the run when it expires.</item>
    /// <item><see cref="Path"/> is shown to the player as "Macera Modu". Its ruleset is unchanged: it
    /// bounds a run to one authored level and ends it the moment that level's objective is met.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Append-only. The value is not persisted today (see <c>GameModeModel</c>), but it is compared
    /// by name across the codebase and every existing check tests for a specific member rather than
    /// "not Endless"/"not Timed", so a new member is inert everywhere it is not explicitly handled.
    /// </para>
    /// </summary>
    public enum GameMode
    {
        Endless,
        Timed,

        /// <summary>
        /// Level-bounded runs. The run plays exactly one authored level; completing that level's
        /// objective ends the run as a success, and running out of legal moves first ends it as a
        /// failure. Levels are chosen from the level path overlay rather than advanced into.
        /// </summary>
        Path,
    }
}
