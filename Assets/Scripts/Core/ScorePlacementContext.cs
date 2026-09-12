namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Everything a <see cref="IScoreRule"/> needs to know about a single placement. Pure data — no
    /// Unity types — so rules stay testable without the engine.
    /// </summary>
    public readonly struct ScorePlacementContext
    {
        public ScorePlacementContext(
            int cellCount,
            int linesCleared,
            int streakBeforePlacement,
            int monochromeLineCount,
            int multiClearStreakBeforePlacement,
            int cumulativeMultiClearCountBeforePlacement)
        {
            CellCount = cellCount;
            LinesCleared = linesCleared;
            StreakBeforePlacement = streakBeforePlacement;
            MonochromeLineCount = monochromeLineCount;
            MultiClearStreakBeforePlacement = multiClearStreakBeforePlacement;
            CumulativeMultiClearCountBeforePlacement = cumulativeMultiClearCountBeforePlacement;
        }

        /// <summary>Number of cells occupied by the piece that was just placed.</summary>
        public int CellCount { get; }

        /// <summary>Rows plus columns cleared by this placement; zero when nothing cleared.</summary>
        public int LinesCleared { get; }

        /// <summary>Streak value as it stood before this placement's increment/reset.</summary>
        public int StreakBeforePlacement { get; }

        /// <summary>Of <see cref="LinesCleared"/>, how many were entirely one colour.</summary>
        public int MonochromeLineCount { get; }

        /// <summary>Consecutive-multi-clear streak as it stood before this placement's increment/reset.</summary>
        public int MultiClearStreakBeforePlacement { get; }

        /// <summary>Run-scoped count of placements that cleared 2+ lines, as it stood before this placement's
        /// increment — consecutive or not.</summary>
        public int CumulativeMultiClearCountBeforePlacement { get; }
    }
}
