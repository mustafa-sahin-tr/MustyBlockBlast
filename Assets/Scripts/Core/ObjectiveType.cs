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
    }
}
