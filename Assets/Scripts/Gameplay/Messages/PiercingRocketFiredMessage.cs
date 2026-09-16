namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// A <see cref="MustyBlockBlast.Core.SpecialPieceKind.PiercingRocket"/> was placed and its wipe
    /// emptied <see cref="WipedCellCount"/> cells — the full row and the full column through the cell it
    /// landed on, including the rocket's own.
    /// <para>
    /// Its own message rather than <see cref="LaserFiredMessage"/>, which it otherwise resembles: a
    /// laser fires because something destroyed it and a rocket fires because the player placed it, so a
    /// subscriber that reacts to one (a sound, a streak of light along the wiped line) must be able to
    /// tell them apart.
    /// </para>
    /// <para>
    /// Carries no score, for the reason <see cref="LaserFiredMessage"/> carries none: the amount is
    /// decided by <see cref="MustyBlockBlast.Gameplay.Systems.PiercingRocketScoreSystem"/>.
    /// </para>
    /// <para>
    /// Published only when the wipe actually emptied something, so subscribers never have to handle a
    /// zero count.
    /// </para>
    /// </summary>
    public readonly struct PiercingRocketFiredMessage
    {
        public PiercingRocketFiredMessage(int wipedCellCount)
        {
            WipedCellCount = wipedCellCount;
        }

        /// <summary>How many occupied cells the wipe emptied. Never counts a cell that was already
        /// empty, and counts the row/column intersection once.</summary>
        public int WipedCellCount { get; }
    }
}
