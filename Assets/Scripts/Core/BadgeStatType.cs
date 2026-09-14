namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The lifetime counters a badge can be hung off. Unlike <see cref="ObjectiveType"/> these never
    /// reset: they accumulate across every run and every app launch, which is exactly what makes a
    /// badge an achievement rather than a level goal.
    /// </summary>
    public enum BadgeStatType
    {
        /// <summary>Every piece ever legally placed on the board.</summary>
        TotalPiecesPlaced,

        /// <summary>Every row and column ever cleared, summed across all runs.</summary>
        TotalLinesCleared,

        /// <summary>Every placement that left the board with zero occupied cells.</summary>
        TotalBoardWipes,

        /// <summary>The single highest run score ever reached. A high-water mark, not a sum.</summary>
        HighestScoreEver,

        /// <summary>Every run ever started.</summary>
        TotalRunsPlayed,

        /// <summary>Every power-up ever spent on the board.</summary>
        TotalPowerUpsApplied,
    }
}
