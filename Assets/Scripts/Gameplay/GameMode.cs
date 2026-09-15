namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The rule set a run is played under. <see cref="Timed"/> adds a countdown that resets on every
    /// tray refill and ends the run when it expires; <see cref="Endless"/> is never ended by time;
    /// <see cref="Path"/> bounds a run to one authored level and ends it the moment that level's
    /// objective is met.
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
