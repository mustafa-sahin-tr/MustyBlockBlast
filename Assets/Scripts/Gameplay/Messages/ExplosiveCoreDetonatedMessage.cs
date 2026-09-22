namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// One or more <see cref="MustyBlockBlast.Core.SpecialCellKind.ExplosiveCore"/> cells detonated and
    /// their bonus wipe emptied <see cref="WipedCellCount"/> cells. Published by whichever System
    /// resolved the destruction — a placement's cascade, a spent power-up or a hammer — so a detonation
    /// reads the same to every subscriber whatever set it off.
    /// <para>
    /// Carries no score, for the reason <see cref="LaserFiredMessage"/> carries none: the amount is
    /// decided by <see cref="MustyBlockBlast.Gameplay.Systems.ExplosiveCoreScoreSystem"/>.
    /// </para>
    /// <para>
    /// Published only when a wipe actually emptied something — a core whose whole opposite line was
    /// already empty is not an event, so subscribers never have to handle a zero count.
    /// </para>
    /// </summary>
    public readonly struct ExplosiveCoreDetonatedMessage
    {
        public ExplosiveCoreDetonatedMessage(int wipedCellCount)
        {
            WipedCellCount = wipedCellCount;
        }

        /// <summary>How many occupied cells the wipe (and any chained wipe) emptied. Never counts a cell
        /// that was already empty, and never counts the detonating core's own cell — that was emptied by
        /// whatever destroyed it, and is already reported by that clear.</summary>
        public int WipedCellCount { get; }
    }
}
