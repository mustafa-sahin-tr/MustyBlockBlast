namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// One or more <see cref="MustyBlockBlast.Core.SpecialCellKind.ExplosiveCore"/> cells detonated and
    /// their blast emptied <see cref="ClearedCellCount"/> cells. Published by whichever System resolved
    /// the destruction — a placement's cascade or a spent power-up — so a blast reads the same to every
    /// subscriber whatever set it off.
    /// <para>
    /// Carries no score, for the reason <see cref="PowerUpAppliedMessage"/> carries none: the amount is
    /// decided by <see cref="MustyBlockBlast.Gameplay.Systems.ExplosiveCoreScoreSystem"/>.
    /// </para>
    /// <para>
    /// Published only when a blast actually emptied something — a core whose whole footprint was
    /// already empty is not an event, so subscribers never have to handle a zero count.
    /// </para>
    /// </summary>
    public readonly struct ExplosiveCoreDetonatedMessage
    {
        public ExplosiveCoreDetonatedMessage(int clearedCellCount)
        {
            ClearedCellCount = clearedCellCount;
        }

        /// <summary>How many occupied cells the blast (and any chained blast) emptied. Never counts a
        /// cell that was already empty, and never counts the detonating core's own cell — that was
        /// emptied by whatever destroyed it, and is already reported by that clear.</summary>
        public int ClearedCellCount { get; }
    }
}
