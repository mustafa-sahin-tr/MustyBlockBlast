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
        {
            Kind = kind;
            ClearedCellCount = clearedCellCount;
        }

        public PowerUpKind Kind { get; }

        public int ClearedCellCount { get; }
    }
}
