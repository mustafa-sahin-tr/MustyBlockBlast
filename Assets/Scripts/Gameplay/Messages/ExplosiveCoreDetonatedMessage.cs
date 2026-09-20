namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// An <see cref="MustyBlockBlast.Core.SpecialCellKind.ExplosiveCore"/> cell detonated. Published by
    /// whichever System resolved the destruction — a placement's cascade, today the only source — so a
    /// detonation reads the same to every subscriber whatever set it off.
    /// <para>
    /// Carries no score itself: the amount is decided by
    /// <see cref="MustyBlockBlast.Gameplay.Systems.ExplosiveCoreScoreSystem"/> from
    /// <see cref="FinishedLineCount"/> alone, using the same formula an ordinary completed line scores
    /// with.
    /// </para>
    /// <para>
    /// Published only when a detonation actually did something — finished at least one line, or handed
    /// its kind off to another cell — so subscribers never have to handle a message that changed
    /// nothing.
    /// </para>
    /// </summary>
    public readonly struct ExplosiveCoreDetonatedMessage
    {
        public ExplosiveCoreDetonatedMessage(int finishedLineCount, int handOffCount)
        {
            FinishedLineCount = finishedLineCount;
            HandOffCount = handOffCount;
        }

        /// <summary>How many rows and columns this detonation (and any chained detonation) finished —
        /// found missing exactly one occupied playable cell and completed. The "lines" term
        /// <see cref="MustyBlockBlast.Core.ScoreRules.ClearScore"/> takes, exactly as a placement's own
        /// completed-line count is.</summary>
        public int FinishedLineCount { get; }

        /// <summary>How many times this detonation (and any chained detonation) found nothing to finish
        /// and transferred <see cref="MustyBlockBlast.Core.SpecialCellKind.ExplosiveCore"/> to another
        /// cell instead. Usually 0 or 1; higher only when more than one core detonated in the same
        /// resolution and more than one of them found nothing left to finish.</summary>
        public int HandOffCount { get; }
    }
}
