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

        /// <summary>Count placements that clear AT LEAST <see cref="ObjectiveDefinition.RequiredLineCount"/>
        /// lines at once — a "mega clear" goal. Deliberately a separate type from
        /// <see cref="SimultaneousLineClear"/>, which is exact-match only: a 4-line clear does not
        /// retroactively satisfy a "clear exactly 2" objective, so "at least" needs its own type
        /// rather than a flag on the existing one.</summary>
        AtLeastLineClear = 8,

        /// <summary>Count placements of the exact catalog piece named by
        /// <see cref="ObjectiveDefinition.RequiredPieceId"/> — finer-grained than
        /// <see cref="PieceFamilyCount"/>, which cannot distinguish a 2x2 square from a 3x3 one.</summary>
        PieceIdCount = 9,

        /// <summary>Count placements of the exact catalog piece named by
        /// <see cref="ObjectiveDefinition.RequiredPieceId"/> that ALSO cleared at least one line —
        /// e.g. "clear a line using the I5 pentomino".</summary>
        PieceIdLineClear = 10,

        /// <summary>Count placements whose clear touched a board corner — a row or column cleared
        /// that includes coordinate (0,0), (SIZE-1,0), (0,SIZE-1) or (SIZE-1,SIZE-1).</summary>
        FourCornersCleared = 11,

        /// <summary>Count placements that leave the board's centered 4x4 core completely empty.</summary>
        CenterCoreEvacuated = 12,

        /// <summary>Best streak of consecutive placements — every placement, not just clearing ones —
        /// that left zero isolated (unreachable-from-edge) empty cells on the board, evaluated after
        /// that placement's own line clears resolved. High-water mark, same mechanic as
        /// <see cref="StreakThreshold"/>, but driven by board topology instead of the combo streak.</summary>
        NoIsolatedHolesStreak = 13,

        /// <summary>Count lines cleared by placements within a trailing
        /// <see cref="ObjectiveDefinition.WindowSeconds"/>-wide rolling window — e.g. "clear 6 lines
        /// within any 12-second span". Tracks its own timestamped event queue internally; not built
        /// on any Timed-mode timer, so it behaves identically in Endless and Timed mode.</summary>
        RollingLineClearWindow = 14,

        /// <summary>Reach <see cref="ObjectiveDefinition.TargetValue"/> run score within the first
        /// <see cref="ObjectiveDefinition.WindowSeconds"/> seconds of the run — e.g. "2500 points in
        /// the first 60 seconds". Mirrors the live score like <see cref="ScoreInRun"/>, but only while
        /// still inside the deadline; past it, progress freezes rather than completing late.</summary>
        EarlyScoreRush = 15,
    }
}
