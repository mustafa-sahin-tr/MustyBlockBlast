namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// A power-up was successfully spent on the board. Carries no score: the amount is decided by
    /// <see cref="MustyBlockBlast.Gameplay.Systems.PowerUpScoreSystem"/> from <see cref="Kind"/> and
    /// <see cref="ClearedCellCount"/>, exactly as placements leave scoring to ScoreSystem.
    /// <para>
    /// Published even when <see cref="ClearedCellCount"/> is 0 (the power-up was still consumed), so
    /// consumers must treat zero as "nothing happened on the board".
    /// </para>
    /// </summary>
    public readonly struct PowerUpAppliedMessage
    {
        public PowerUpAppliedMessage(PowerUpKind kind, int clearedCellCount)
            : this(kind, clearedCellCount, 0, 0)
        {
        }

        public PowerUpAppliedMessage(PowerUpKind kind, int clearedCellCount, int clearedLineCount)
            : this(kind, clearedCellCount, clearedLineCount, 0)
        {
        }

        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount)
        {
            Kind = kind;
            ClearedCellCount = clearedCellCount;
            ClearedLineCount = clearedLineCount;
            EmptiedLineCount = emptiedLineCount;
        }

        public PowerUpKind Kind { get; }

        public int ClearedCellCount { get; }

        /// <summary>
        /// How many whole rows/columns became full and cleared. Only <see cref="PowerUpKind.Joker"/>
        /// reports this, because it is the only kind whose clear is conditional on fullness; the other
        /// three clear a region regardless, so "lines" is not a thing they have and they leave it 0.
        /// </summary>
        public int ClearedLineCount { get; }

        /// <summary>
        /// How many whole rows/columns had at least one cell cleared and ended up completely empty —
        /// the opposite condition from <see cref="ClearedLineCount"/> (which is about a line becoming
        /// FULL). Populated for every kind that clears a region (<see cref="MustyBlockBlast.Core.PowerUpClearResult.EmptiedLineCount"/>),
        /// but only meaningful as a "surprise" signal for a kind whose target doesn't already guarantee
        /// it — Row Clear/Column Clear trivially empty their own target line every time, so this only
        /// matters as an achievement signal for <see cref="PowerUpKind.Bomb"/>.
        /// </summary>
        public int EmptiedLineCount { get; }
    }
}
