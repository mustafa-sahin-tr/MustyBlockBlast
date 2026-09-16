namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// One or more <see cref="MustyBlockBlast.Core.SpecialCellKind.Laser"/> cells fired and their wipe
    /// emptied <see cref="WipedCellCount"/> cells. Published by whichever System resolved the
    /// destruction — a placement's cascade or a spent power-up — so a wipe reads the same to every
    /// subscriber whatever set it off.
    /// <para>
    /// Carries no score, for the reason <see cref="PowerUpAppliedMessage"/> carries none: the amount is
    /// decided by <see cref="MustyBlockBlast.Gameplay.Systems.LaserScoreSystem"/>.
    /// </para>
    /// <para>
    /// Published only when a wipe actually emptied something — a laser whose whole line was already
    /// empty is not an event, so subscribers never have to handle a zero count.
    /// </para>
    /// </summary>
    public readonly struct LaserFiredMessage
    {
        public LaserFiredMessage(int wipedCellCount)
        {
            WipedCellCount = wipedCellCount;
        }

        /// <summary>How many occupied cells the wipe (and any chained wipe) emptied. Never counts a cell
        /// that was already empty, and never counts the firing laser's own cell — that was emptied by
        /// whatever destroyed it, and is already reported by that clear.</summary>
        public int WipedCellCount { get; }
    }
}
