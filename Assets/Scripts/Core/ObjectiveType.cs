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

        /// <summary>Count Reroll uses spent while the dock (the 3 offered pieces, not the Hold slot)
        /// had zero legal placements. Because <see cref="MustyBlockBlast.Gameplay.Systems.BoardSystem.CheckGameOver"/>
        /// already ends the run the moment BOTH the dock and the Hold slot are dead, a dead-dock-but-
        /// live-run state can only exist because the Hold slot still has a placeable piece — so this
        /// objective is honestly "spent a Reroll while your held piece was the only thing standing
        /// between the dock and game over", not a genuine rescue from the brink. Never advanced by
        /// <see cref="ObjectiveProgress.ApplyPlacement"/> — a distinct, power-up-sourced event, folded
        /// in only via <see cref="ObjectiveProgress.ApplyPowerUpRerollSave"/>, the same shape
        /// <see cref="BombInducedLineClear"/> uses.</summary>
        RerollSave = 16,

        /// <summary>Count reinforced cells whose hit count reached 0 and were actually removed from the
        /// board. A cell that merely took a hit and survived counts for nothing — only the destruction
        /// does, which is what makes "clear all of them" a finite goal rather than a hit tally.
        /// <para>
        /// The target is not authored: <see cref="MustyBlockBlast.Gameplay.Settings.LevelObjectiveConfig.ToObjectiveDefinition"/>
        /// always forces it to the count of reinforced cells the level authored, so the objective is
        /// always "all of them" and never a subset.
        /// </para>
        /// <para>
        /// A genuinely new shape among the special types: this is the only one advanced by BOTH
        /// <see cref="ObjectiveProgress.ApplyPlacement"/> (a placement's own line clear can finish a
        /// reinforced cell off, arriving as <c>PiecePlacedMessage.ReinforcedCellsFullyClearedCount</c>)
        /// AND a dedicated <see cref="ObjectiveProgress.ApplyPowerUpReinforcedCellsCleared"/> (a spent
        /// power-up can finish one off too, arriving as
        /// <c>PowerUpAppliedMessage.ReinforcedCellsFullyClearedCount</c>). <see cref="BombInducedLineClear"/>
        /// and <see cref="RerollSave"/> are power-up-sourced ONLY, so each needs just the one method;
        /// this type has two real event sources and so needs both paths.
        /// </para>
        /// <para>
        /// Unlike every other counting type, one event can advance it by more than 1: a single cleared
        /// row can finish off two reinforced cells at once, and both destructions must be credited.
        /// </para>
        /// <para>
        /// Known gap, inherited from the mechanic itself: a reinforced cell finished off by a special
        /// cell's own blast/wipe/strike mid-cascade never passes through a clear phase, so it is not
        /// reported on either message and does not advance this objective.
        /// </para></summary>
        ReinforcedCellsCleared = 17,

        /// <summary>
        /// Destroy <c>TargetValue</c> cells of <c>RequiredColourId</c>, by completed lines or by power-up
        /// clears. Counts cells, not events: one placement can advance it by several (issue #147).
        /// </summary>
        ColourCleared = 18,

        /// <summary>
        /// Count <see cref="SpecialCellKind.Timer"/> cells cleared BEFORE their placement countdown
        /// reached 0 — see <see cref="SpecialCellKind.Timer"/> and <see cref="TimerCellClearEffect"/>. A
        /// cell whose countdown expired first and was cleared afterward as an ordinary cell does NOT
        /// count: by the time it clears it no longer carries this kind at all, so it can never produce
        /// the <see cref="SpecialCellTrigger"/> this objective is fed from.
        /// <para>
        /// Counts THINGS DESTROYED, like <see cref="ReinforcedCellsCleared"/> and
        /// <see cref="ColourCleared"/>, not events: one placement whose clear (primary phase or a
        /// cascaded one) took out several timer cells at once credits every one of them.
        /// </para>
        /// <para>
        /// Advanced by BOTH <see cref="ObjectiveProgress.ApplyPlacement"/> (a placement's whole
        /// resolution — arriving as <c>PiecePlacedMessage.TimerCellsClearedInTimeCount</c>, which is
        /// already summed across every destruction path: the primary clear, a cascaded phase, AND a
        /// special cell's own blast/wipe/strike mid-cascade — issue #307 AC11 explicitly closes the gap
        /// <see cref="ReinforcedCellsCleared"/>'s doc comment names, rather than repeating it) AND a
        /// dedicated <see cref="ObjectiveProgress.ApplyPowerUpTimerCellsClearedInTime"/> (a spent
        /// power-up can clear one too, arriving as
        /// <c>PowerUpAppliedMessage.TimerCellsClearedInTimeCount</c>).
        /// </para>
        /// </summary>
        TimerCellsMeltedInTime = 19,

        /// <summary>
        /// Destroy <c>TargetValue</c> <see cref="SpecialCellKind.Diamond"/> cells whose gem colour is
        /// <c>RequiredColourId</c> — colour-scoped exactly as <see cref="ColourCleared"/> is, but by the
        /// diamond's own colour (<see cref="SpecialCellTrigger.DiamondColourId"/>), never by the colour
        /// of the block it rode on. Counts THINGS DESTROYED, like <see cref="ColourCleared"/> and
        /// <see cref="TimerCellsMeltedInTime"/>, not events: one placement that takes out several
        /// diamonds at once credits every one of them. A diamond credits no score — see
        /// <see cref="DiamondClearEffect"/>.
        /// <para>
        /// Advanced by BOTH <see cref="ObjectiveProgress.ApplyPlacement"/> (a placement's whole
        /// resolution — arriving as <c>PiecePlacedMessage.DestroyedDiamondCountByColour</c>, summed
        /// across every destruction path: the primary clear, a cascaded phase, AND a special cell's own
        /// blast/wipe/strike mid-cascade, the same AC11-shaped sum <see cref="TimerCellsMeltedInTime"/>
        /// uses) AND a dedicated <see cref="ObjectiveProgress.ApplyPowerUpDiamondsCleared"/> (a spent
        /// power-up can destroy one too, arriving as
        /// <c>PowerUpAppliedMessage.DestroyedDiamondCountByColour</c>).
        /// </para>
        /// </summary>
        DiamondsCleared = 20,

        /// <summary>
        /// Melt every ice socket the level authors (issue #433 — "Buz hücrelerini eritme"): count the
        /// authored ice positions whose ice level reached 0. A position starts a run EMPTY and playable
        /// with an authored ice level of 1-3 (<see cref="Board.GetIceLevel"/>); each time the block the
        /// player put on it is destroyed, the ice melts by one level and the position is empty again. A
        /// socket that merely lost a level counts for nothing — only reaching 0 does, which is what makes
        /// "melt all of them" a finite goal rather than a melt tally.
        /// <para>
        /// The target is not authored: <see cref="MustyBlockBlast.Gameplay.Settings.LevelObjectiveConfig.ToObjectiveDefinition"/>
        /// always forces it to the count of ice sockets the level authored, exactly as it does for
        /// <see cref="ReinforcedCellsCleared"/>, so the objective is always "all of them" and never a
        /// subset. A level with every occupied cell emptied but one authored socket still above level 0
        /// is therefore not complete (AC7).
        /// </para>
        /// <para>
        /// Counts THINGS MELTED, like <see cref="ReinforcedCellsCleared"/>, not events: one placement
        /// whose resolution takes the last level off two sockets at once credits both. And it counts
        /// them across EVERY destruction path — a direct completed line, a cascaded phase, a power-up
        /// clear, a joker's completed line, and a special cell's own blast/wipe/strike mid-cascade —
        /// because the melt itself happens inside <see cref="Board.TryDamage"/>, the one removal primitive
        /// every path shares, and the count is read as "sockets still icy before minus sockets still icy
        /// after" (<see cref="Board.CountIceCells"/>) over the whole resolution. This is the
        /// <see cref="TimerCellsMeltedInTime"/> "any destruction path counts" framing, and it is
        /// explicitly NOT vulnerable to the Reinforced-Cell cascade-reporting gap referenced in #249: no
        /// resolver or effect has to remember to report an ice melt for it to be counted.
        /// </para>
        /// <para>
        /// Advanced by BOTH <see cref="ObjectiveProgress.ApplyPlacement"/> (a placement's whole
        /// resolution, arriving as <c>PiecePlacedMessage.IceCellsMeltedCount</c>) AND a dedicated
        /// <see cref="ObjectiveProgress.ApplyPowerUpIceCellsMelted"/> (a spent power-up can melt a socket
        /// too, arriving as <c>PowerUpAppliedMessage.IceCellsMeltedCount</c>), the two-source shape
        /// <see cref="ReinforcedCellsCleared"/> established.
        /// </para>
        /// </summary>
        IceCellsCleared = 21,
    }
}
