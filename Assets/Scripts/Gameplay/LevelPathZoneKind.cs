namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// Which decorative scenery band a stretch of the level path trail falls in. Independent of
    /// <see cref="MustyBlockBlast.Gameplay.Settings.ThemeDefinition"/>'s season: a zone is a purely
    /// visual backdrop keyed to the level number, not to which theme the player happens to have
    /// active, so "kış mevsimi" scenery can show up while the active theme is summer.
    /// </summary>
    public enum LevelPathZoneKind
    {
        Meadow,
        Winter,
        City,
        Neighborhood,
    }
}
