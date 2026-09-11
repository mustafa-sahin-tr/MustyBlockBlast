namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The rule set a run is played under. <see cref="Timed"/> is a placeholder: it currently plays
    /// exactly like <see cref="Endless"/> and only exists so the selection UI has a second entry.
    /// </summary>
    public enum GameMode
    {
        Endless,
        Timed,
    }
}
