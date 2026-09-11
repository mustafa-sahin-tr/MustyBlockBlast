namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The rule set a run is played under. <see cref="Timed"/> adds a countdown that resets on every
    /// tray refill and ends the run when it expires; <see cref="Endless"/> is never ended by time.
    /// </summary>
    public enum GameMode
    {
        Endless,
        Timed,
    }
}
