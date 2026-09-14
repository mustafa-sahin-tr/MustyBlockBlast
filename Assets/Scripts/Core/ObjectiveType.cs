namespace MustyBlockBlast.Core
{
    /// <summary>
    /// What an objective measures. Each value maps to exactly one branch in
    /// <see cref="ObjectiveProgress.ApplyPlacement"/>.
    /// </summary>
    public enum ObjectiveType
    {
        /// <summary>Count placements that cleared exactly <see cref="ObjectiveDefinition.RequiredLineCount"/> lines at once.</summary>
        SimultaneousLineClear = 0,

        /// <summary>Count placements of pieces in <see cref="ObjectiveDefinition.RequiredPieceFamily"/>.</summary>
        PieceFamilyCount = 1,

        /// <summary>Track the run score itself against the target — not a counter.</summary>
        ScoreInRun = 2,

        /// <summary>Count placements that left the board completely empty.</summary>
        BoardWipeCount = 3,

        /// <summary>Track the best combo streak reached this run against the target — a high-water
        /// mark, not a live mirror, so a streak reset can never undo progress already made.</summary>
        StreakThreshold = 4,

        /// <summary>Count Bomb power-up clears that happened to leave a row or column completely
        /// empty. Never advanced by <see cref="ObjectiveProgress.ApplyPlacement"/> — a distinct,
        /// power-up-sourced event, folded in only via <see cref="ObjectiveProgress.ApplyPowerUpLineEmptied"/>.</summary>
        BombInducedLineClear = 5,

        /// <summary>Count placements that clear at least one row AND at least one column
        /// simultaneously — distinct from clearing two rows, which <see cref="ObjectivePlacementContext.LinesCleared"/>
        /// alone cannot tell apart from this.</summary>
        RowAndColumnCrossClear = 6,

        /// <summary>Count placements that clear at least one line while the board, immediately before
        /// that clear, held at least <see cref="ObjectiveDefinition.RequiredOccupancyThreshold"/>
        /// occupied cells — a "clutch recovery" under pressure.</summary>
        ClutchRecoveryClear = 7,
    }
}
