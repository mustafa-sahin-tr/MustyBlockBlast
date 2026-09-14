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
            : this(kind, clearedCellCount, 0)
        {
        }

        public PowerUpAppliedMessage(PowerUpKind kind, int clearedCellCount, int clearedLineCount)
        {
            Kind = kind;
            ClearedCellCount = clearedCellCount;
            ClearedLineCount = clearedLineCount;
        }

        public PowerUpKind Kind { get; }

        public int ClearedCellCount { get; }

        /// <summary>
        /// How many whole rows/columns became full and cleared. Only <see cref="PowerUpKind.Joker"/>
        /// reports this, because it is the only kind whose clear is conditional on fullness; the other
        /// three clear a region regardless, so "lines" is not a thing they have and they leave it 0.
        /// </summary>
        public int ClearedLineCount { get; }
    }
}
