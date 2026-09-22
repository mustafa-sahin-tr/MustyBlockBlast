using System.Collections.Generic;
using MustyBlockBlast.Core;

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
            : this(kind, clearedCellCount, clearedLineCount, emptiedLineCount, wasClutchSave: false)
        {
        }

        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount,
            bool wasClutchSave)
            : this(
                kind, clearedCellCount, clearedLineCount, emptiedLineCount, wasClutchSave,
                destroyedScoreGemCount: 0)
        {
        }

        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount,
            bool wasClutchSave, int destroyedScoreGemCount)
            : this(
                kind, clearedCellCount, clearedLineCount, emptiedLineCount, wasClutchSave,
                destroyedScoreGemCount, reinforcedCellsFullyClearedCount: 0)
        {
        }

        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount,
            bool wasClutchSave, int destroyedScoreGemCount, int reinforcedCellsFullyClearedCount)
            : this(
                kind, clearedCellCount, clearedLineCount, emptiedLineCount, wasClutchSave,
                destroyedScoreGemCount, reinforcedCellsFullyClearedCount, destroyedCellCountByColour: null)
        {
        }

        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount,
            bool wasClutchSave, int destroyedScoreGemCount, int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour)
            : this(
                kind, clearedCellCount, clearedLineCount, emptiedLineCount, wasClutchSave,
                destroyedScoreGemCount, reinforcedCellsFullyClearedCount, destroyedCellCountByColour,
                timerCellsClearedInTimeCount: 0)
        {
        }

        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount,
            bool wasClutchSave, int destroyedScoreGemCount, int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour, int timerCellsClearedInTimeCount)
            : this(
                kind, clearedCellCount, clearedLineCount, emptiedLineCount, wasClutchSave,
                destroyedScoreGemCount, reinforcedCellsFullyClearedCount, destroyedCellCountByColour,
                timerCellsClearedInTimeCount, targetCell: null, clearedCellPositions: null)
        {
        }

        /// <summary>
        /// Issue #332: <paramref name="targetCell"/> and <paramref name="clearedCellPositions"/> are
        /// populated only for <see cref="MustyBlockBlast.Gameplay.PowerUpKind.ColorCleanser"/> — the
        /// only kind the Presentation layer's beam visual reads them for today — and left null by
        /// every other publish site (mirroring how <see cref="WasClutchSave"/> is meaningful only for
        /// <see cref="MustyBlockBlast.Gameplay.PowerUpKind.Reroll"/>). Both are additive-only exposure
        /// of data <c>PowerUpClearResolver.ResolveColorCleanser</c> already computed for its own return
        /// value; nothing about what gets cleared changes.
        /// </summary>
        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount,
            bool wasClutchSave, int destroyedScoreGemCount, int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour, int timerCellsClearedInTimeCount,
            GridPosition? targetCell, IReadOnlyList<GridPosition> clearedCellPositions)
            : this(
                kind, clearedCellCount, clearedLineCount, emptiedLineCount, wasClutchSave,
                destroyedScoreGemCount, reinforcedCellsFullyClearedCount, destroyedCellCountByColour,
                timerCellsClearedInTimeCount, targetCell, clearedCellPositions,
                destroyedDiamondCountByColour: null)
        {
        }

        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount,
            bool wasClutchSave, int destroyedScoreGemCount, int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour, int timerCellsClearedInTimeCount,
            IReadOnlyList<int> destroyedDiamondCountByColour)
            : this(
                kind, clearedCellCount, clearedLineCount, emptiedLineCount, wasClutchSave,
                destroyedScoreGemCount, reinforcedCellsFullyClearedCount, destroyedCellCountByColour,
                timerCellsClearedInTimeCount, targetCell: null, clearedCellPositions: null,
                destroyedDiamondCountByColour: destroyedDiamondCountByColour)
        {
        }

        public PowerUpAppliedMessage(
            PowerUpKind kind, int clearedCellCount, int clearedLineCount, int emptiedLineCount,
            bool wasClutchSave, int destroyedScoreGemCount, int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour, int timerCellsClearedInTimeCount,
            GridPosition? targetCell, IReadOnlyList<GridPosition> clearedCellPositions,
            IReadOnlyList<int> destroyedDiamondCountByColour)
        {
            Kind = kind;
            ClearedCellCount = clearedCellCount;
            ClearedLineCount = clearedLineCount;
            EmptiedLineCount = emptiedLineCount;
            WasClutchSave = wasClutchSave;
            DestroyedScoreGemCount = destroyedScoreGemCount;
            ReinforcedCellsFullyClearedCount = reinforcedCellsFullyClearedCount;
            DestroyedCellCountByColour = destroyedCellCountByColour;
            TimerCellsClearedInTimeCount = timerCellsClearedInTimeCount;
            TargetCell = targetCell;
            ClearedCellPositions = clearedCellPositions;
            DestroyedDiamondCountByColour = destroyedDiamondCountByColour;
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

        /// <summary>
        /// True when the dock had zero legal placements immediately before this Reroll (the Hold
        /// slot's piece, if any, is not part of that check). Because the run only survives that state
        /// at all if the Hold slot already had a placeable piece, this is not a rescue from the brink —
        /// it's a signal that Reroll was spent while the held piece was the only thing keeping the run
        /// alive. Meaningful only for <see cref="MustyBlockBlast.Gameplay.PowerUpKind.Reroll"/>; every
        /// other kind leaves this false.
        /// </summary>
        public bool WasClutchSave { get; }

        /// <summary>
        /// How many <see cref="MustyBlockBlast.Core.SpecialCellKind.ScoreGem"/>s this application's
        /// clear destroyed. Read by
        /// <see cref="MustyBlockBlast.Gameplay.Systems.PowerUpScoreSystem"/> only, which multiplies this
        /// application's whole gain by <see cref="MustyBlockBlast.Core.ScoreRules.SCORE_GEM_FACTOR"/>
        /// when it is non-zero — a gem pays out for the event that destroyed it whether that event was
        /// a placement or a spent power-up.
        /// </summary>
        public int DestroyedScoreGemCount { get; }

        /// <summary>
        /// How many reinforced cells this application's clear finished off — cells that were reinforced
        /// and took their last hit, as distinct from ordinary cells that were never reinforced.
        /// <para>
        /// Data plumbing for issue #154's "clear all reinforced cells" objective, the parallel of
        /// <c>PiecePlacedMessage.ReinforcedCellsFullyClearedCount</c> for the paths that are not a
        /// placement — a reinforced cell finished off by a spent power-up counts exactly as one
        /// finished off by a completed line. Nothing reads it yet.
        /// </para>
        /// </summary>
        public int ReinforcedCellsFullyClearedCount { get; }

        /// <summary>Cells this application destroyed, counted per colour id — see
        /// <c>ColourTally</c>. Null for a kind that clears nothing.</summary>
        public IReadOnlyList<int> DestroyedCellCountByColour { get; }

        /// <summary>
        /// How many <see cref="MustyBlockBlast.Core.SpecialCellKind.Timer"/> cells this application's
        /// direct clear destroyed BEFORE their countdown reached 0, read from
        /// <c>PowerUpClearResult.TriggeredSpecials</c> at publish time. Data plumbing for
        /// <see cref="MustyBlockBlast.Core.ObjectiveType.TimerCellsMeltedInTime"/>, the only thing that
        /// reads it.
        /// <para>
        /// Does NOT include a Timer cell a triggered blast/wipe/strike destroys afterward in
        /// <c>ApplyTriggeredSpecials</c> — that loop runs after this message is published and does not
        /// apply <see cref="MustyBlockBlast.Core.TimerCellClearEffect"/>, the same pre-existing gap
        /// <see cref="ReinforcedCellsFullyClearedCount"/> and <see cref="DestroyedScoreGemCount"/> have
        /// in this power-up path today (issue #307 AC11's cascade-reporting fix applies to a
        /// placement's resolution, not to this secondary power-up-triggered chain).
        /// </para>
        /// </summary>
        public int TimerCellsClearedInTimeCount { get; }

        /// <summary>
        /// Issue #332: the cell the player aimed at, for the one kind that clears a whole-board region
        /// keyed off a single tapped cell rather than a fixed shape. Null for every kind except
        /// <see cref="MustyBlockBlast.Gameplay.PowerUpKind.ColorCleanser"/> — a Bomb/Row/Column target
        /// is a centre or a line index, not a single cell in the same sense, and nothing reads this for
        /// them today.
        /// </summary>
        public GridPosition? TargetCell { get; }

        /// <summary>
        /// Issue #332: exactly <see cref="MustyBlockBlast.Core.PowerUpClearResult.ClearedCells"/> for a
        /// <see cref="MustyBlockBlast.Gameplay.PowerUpKind.ColorCleanser"/> application — the positions
        /// <c>BoardView</c> draws a beam from <see cref="TargetCell"/> to. Null for every other kind;
        /// nothing reads it for them today.
        /// </summary>
        public IReadOnlyList<GridPosition> ClearedCellPositions { get; }

        /// <summary>
        /// <see cref="MustyBlockBlast.Core.SpecialCellKind.Diamond"/> cells this application's direct
        /// clear destroyed, counted per the gem's own colour id (see <c>ColourTally</c>), read from
        /// <c>PowerUpClearResult.TriggeredSpecials</c> at publish time. Data plumbing for
        /// <see cref="MustyBlockBlast.Core.ObjectiveType.DiamondsCleared"/>, the only thing that reads
        /// it. Null for a kind that clears nothing.
        /// <para>
        /// Does NOT include a diamond a triggered blast/wipe/strike destroys afterward in
        /// <c>ApplyTriggeredSpecials</c> — the same pre-existing gap
        /// <see cref="TimerCellsClearedInTimeCount"/> documents for this secondary power-up-triggered
        /// chain.
        /// </para>
        /// </summary>
        public IReadOnlyList<int> DestroyedDiamondCountByColour { get; }
    }
}
