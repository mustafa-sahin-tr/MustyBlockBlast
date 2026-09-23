using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>Published once per legal placement, after the board and any line clears resolved.
    /// Carries <see cref="LinesCleared"/> so scoring is a pure function of this one message.</summary>
    public readonly struct PiecePlacedMessage
    {
        public PiecePlacedMessage(
            string pieceId,
            GridPosition anchor,
            PieceFamily pieceFamily,
            int cellCount,
            int colourId,
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            int monochromeLineCount,
            bool boardEmptyAfterPlacement,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement)
            : this(
                pieceId, anchor, pieceFamily, cellCount, colourId, linesCleared, rowsCleared,
                columnsCleared, monochromeLineCount, boardEmptyAfterPlacement,
                occupiedCellCountBeforeClear, anyCornerCleared, centerCoreEmptyAfterPlacement,
                hasIsolatedHolesAfterPlacement, destroyedScoreGemCount: 0)
        {
        }

        public PiecePlacedMessage(
            string pieceId,
            GridPosition anchor,
            PieceFamily pieceFamily,
            int cellCount,
            int colourId,
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            int monochromeLineCount,
            bool boardEmptyAfterPlacement,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            int destroyedScoreGemCount)
            : this(
                pieceId, anchor, pieceFamily, cellCount, colourId, linesCleared, rowsCleared,
                columnsCleared, monochromeLineCount, boardEmptyAfterPlacement,
                occupiedCellCountBeforeClear, anyCornerCleared, centerCoreEmptyAfterPlacement,
                hasIsolatedHolesAfterPlacement, destroyedScoreGemCount,
                reinforcedCellsFullyClearedCount: 0)
        {
        }

        public PiecePlacedMessage(
            string pieceId,
            GridPosition anchor,
            PieceFamily pieceFamily,
            int cellCount,
            int colourId,
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            int monochromeLineCount,
            bool boardEmptyAfterPlacement,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            int destroyedScoreGemCount,
            int reinforcedCellsFullyClearedCount)
            : this(
                pieceId, anchor, pieceFamily, cellCount, colourId, linesCleared, rowsCleared,
                columnsCleared, monochromeLineCount, boardEmptyAfterPlacement,
                occupiedCellCountBeforeClear, anyCornerCleared, centerCoreEmptyAfterPlacement,
                hasIsolatedHolesAfterPlacement, destroyedScoreGemCount,
                reinforcedCellsFullyClearedCount, destroyedCellCountByColour: null)
        {
        }

        public PiecePlacedMessage(
            string pieceId,
            GridPosition anchor,
            PieceFamily pieceFamily,
            int cellCount,
            int colourId,
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            int monochromeLineCount,
            bool boardEmptyAfterPlacement,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            int destroyedScoreGemCount,
            int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour)
            : this(
                pieceId, anchor, pieceFamily, cellCount, colourId, linesCleared, rowsCleared,
                columnsCleared, monochromeLineCount, boardEmptyAfterPlacement,
                occupiedCellCountBeforeClear, anyCornerCleared, centerCoreEmptyAfterPlacement,
                hasIsolatedHolesAfterPlacement, destroyedScoreGemCount,
                reinforcedCellsFullyClearedCount, destroyedCellCountByColour,
                timerCellsClearedInTimeCount: 0)
        {
        }

        public PiecePlacedMessage(
            string pieceId,
            GridPosition anchor,
            PieceFamily pieceFamily,
            int cellCount,
            int colourId,
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            int monochromeLineCount,
            bool boardEmptyAfterPlacement,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            int destroyedScoreGemCount,
            int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour,
            int timerCellsClearedInTimeCount)
            : this(
                pieceId, anchor, pieceFamily, cellCount, colourId, linesCleared, rowsCleared,
                columnsCleared, monochromeLineCount, boardEmptyAfterPlacement,
                occupiedCellCountBeforeClear, anyCornerCleared, centerCoreEmptyAfterPlacement,
                hasIsolatedHolesAfterPlacement, destroyedScoreGemCount,
                reinforcedCellsFullyClearedCount, destroyedCellCountByColour,
                timerCellsClearedInTimeCount, destroyedDiamondCountByColour: null)
        {
        }

        public PiecePlacedMessage(
            string pieceId,
            GridPosition anchor,
            PieceFamily pieceFamily,
            int cellCount,
            int colourId,
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            int monochromeLineCount,
            bool boardEmptyAfterPlacement,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            int destroyedScoreGemCount,
            int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour,
            int timerCellsClearedInTimeCount,
            IReadOnlyList<int> destroyedDiamondCountByColour)
            : this(
                pieceId, anchor, pieceFamily, cellCount, colourId, linesCleared, rowsCleared,
                columnsCleared, monochromeLineCount, boardEmptyAfterPlacement,
                occupiedCellCountBeforeClear, anyCornerCleared, centerCoreEmptyAfterPlacement,
                hasIsolatedHolesAfterPlacement, destroyedScoreGemCount,
                reinforcedCellsFullyClearedCount, destroyedCellCountByColour,
                timerCellsClearedInTimeCount, destroyedDiamondCountByColour, iceCellsMeltedCount: 0)
        {
        }

        public PiecePlacedMessage(
            string pieceId,
            GridPosition anchor,
            PieceFamily pieceFamily,
            int cellCount,
            int colourId,
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            int monochromeLineCount,
            bool boardEmptyAfterPlacement,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            int destroyedScoreGemCount,
            int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour,
            int timerCellsClearedInTimeCount,
            IReadOnlyList<int> destroyedDiamondCountByColour,
            int iceCellsMeltedCount)
        {
            PieceId = pieceId;
            Anchor = anchor;
            PieceFamily = pieceFamily;
            CellCount = cellCount;
            ColourId = colourId;
            LinesCleared = linesCleared;
            RowsCleared = rowsCleared;
            ColumnsCleared = columnsCleared;
            MonochromeLineCount = monochromeLineCount;
            BoardEmptyAfterPlacement = boardEmptyAfterPlacement;
            OccupiedCellCountBeforeClear = occupiedCellCountBeforeClear;
            AnyCornerCleared = anyCornerCleared;
            CenterCoreEmptyAfterPlacement = centerCoreEmptyAfterPlacement;
            HasIsolatedHolesAfterPlacement = hasIsolatedHolesAfterPlacement;
            DestroyedScoreGemCount = destroyedScoreGemCount;
            ReinforcedCellsFullyClearedCount = reinforcedCellsFullyClearedCount;
            DestroyedCellCountByColour = destroyedCellCountByColour;
            TimerCellsClearedInTimeCount = timerCellsClearedInTimeCount;
            DestroyedDiamondCountByColour = destroyedDiamondCountByColour;
            IceCellsMeltedCount = iceCellsMeltedCount;
        }

        public string PieceId { get; }

        public GridPosition Anchor { get; }

        /// <summary>Shape family of <see cref="PieceId"/>, classified once at publish time so objective
        /// tracking never has to re-parse the id.</summary>
        public PieceFamily PieceFamily { get; }

        public int CellCount { get; }

        public int ColourId { get; }

        /// <summary>Rows plus columns cleared by this placement — <see cref="RowsCleared"/> +
        /// <see cref="ColumnsCleared"/>.</summary>
        public int LinesCleared { get; }

        /// <summary>Of <see cref="LinesCleared"/>, how many were rows. Split out from the summed total
        /// so an objective can tell "2 rows" apart from "1 row + 1 column", which the summed count
        /// alone cannot.</summary>
        public int RowsCleared { get; }

        /// <summary>Of <see cref="LinesCleared"/>, how many were columns.</summary>
        public int ColumnsCleared { get; }

        /// <summary>Of <see cref="LinesCleared"/>, how many were entirely one colour.</summary>
        public int MonochromeLineCount { get; }

        /// <summary>True when, after this placement's line clears resolved, the board had zero occupied
        /// cells — a "perfect clear".</summary>
        public bool BoardEmptyAfterPlacement { get; }

        /// <summary>How many cells were occupied right after this piece landed, before any of its line
        /// clears resolved — the board's "under pressure" reading a clutch objective checks against.</summary>
        public int OccupiedCellCountBeforeClear { get; }

        /// <summary>True when this placement's clear touched a board corner cell.</summary>
        public bool AnyCornerCleared { get; }

        /// <summary>True when, after this placement's line clears resolved, the board's centered 4x4
        /// core was completely empty.</summary>
        public bool CenterCoreEmptyAfterPlacement { get; }

        /// <summary>True when, after this placement's line clears resolved, at least one empty cell on
        /// the board is unreachable from the edge through other empty cells.</summary>
        public bool HasIsolatedHolesAfterPlacement { get; }

        /// <summary>
        /// How many <see cref="SpecialCellKind.ScoreGem"/>s this placement's whole resolution destroyed
        /// — the primary clear and every cascaded phase alike, because a gem is a property of the event
        /// that destroyed it rather than of the line the player happened to line up.
        /// <para>
        /// Read by <see cref="MustyBlockBlast.Gameplay.Systems.ScoreSystem"/> only, which multiplies
        /// this placement's whole gain by <see cref="ScoreRules.SCORE_GEM_FACTOR"/> when it is non-zero.
        /// </para>
        /// </summary>
        public int DestroyedScoreGemCount { get; }

        /// <summary>
        /// How many reinforced cells this placement's clear phases finished off — cells that were
        /// reinforced and took their last hit, as distinct from ordinary cells that were never
        /// reinforced at all.
        /// <para>
        /// Data plumbing for issue #154's "clear all reinforced cells" objective, which is the only
        /// thing that will ever read it; nothing does today, and no
        /// <c>ObjectiveType</c> for it exists yet (deliberately — that is #154's work, not this
        /// issue's). Read off
        /// <see cref="MustyBlockBlast.Core.CascadeClearResult.TotalReinforcedCellsFullyClearedCount"/>,
        /// so see that property for exactly which destructions are counted.
        /// </para>
        /// </summary>
        public int ReinforcedCellsFullyClearedCount { get; }

        /// <summary>Cells destroyed by this placement's clears, every cascade phase included, counted
        /// per colour id — see <see cref="ColourTally"/>. Null when the publisher tallied nothing.</summary>
        public IReadOnlyList<int> DestroyedCellCountByColour { get; }

        /// <summary>
        /// How many <see cref="SpecialCellKind.Timer"/> cells this placement's whole resolution
        /// destroyed BEFORE their countdown reached 0 — the primary clear, every cascaded phase, AND a
        /// special cell's own blast/wipe/strike mid-cascade, summed. Unlike
        /// <see cref="ReinforcedCellsFullyClearedCount"/>, which is read straight off
        /// <see cref="CascadeClearResult"/> and inherits its blast/wipe/strike gap, this figure is
        /// deliberately assembled from every destruction path so it does not (issue #307 AC11).
        /// <para>
        /// Data plumbing for <see cref="ObjectiveType.TimerCellsMeltedInTime"/>, the only thing that
        /// reads it.
        /// </para>
        /// </summary>
        public int TimerCellsClearedInTimeCount { get; }

        /// <summary>
        /// <see cref="SpecialCellKind.Diamond"/> cells this placement's whole resolution destroyed —
        /// the primary clear, every cascaded phase, AND a special cell's own blast/wipe/strike
        /// mid-cascade, summed the same way <see cref="TimerCellsClearedInTimeCount"/> is — counted per
        /// the gem's own colour id (see <see cref="ColourTally"/>), never the block's. Null when the
        /// publisher tallied nothing.
        /// <para>
        /// Data plumbing for <see cref="ObjectiveType.DiamondsCleared"/>, the only thing that reads it.
        /// Deliberately not read by any scoring System: a diamond carries no bonus (issue #393 AC4).
        /// </para>
        /// </summary>
        public IReadOnlyList<int> DestroyedDiamondCountByColour { get; }

        /// <summary>
        /// How many ice sockets (issue #433) this placement's whole resolution melted to level 0 — the
        /// primary clear, every cascaded phase, the rocket's wipe AND a special cell's own
        /// blast/wipe/strike mid-cascade, all of them, because the melt happens inside
        /// <see cref="Board.TryDamage"/> and this figure is the difference of
        /// <see cref="Board.CountIceCells"/> taken before and after the resolution. Unlike
        /// <see cref="ReinforcedCellsFullyClearedCount"/> it therefore inherits no blast/wipe/strike gap.
        /// <para>
        /// Data plumbing for <see cref="ObjectiveType.IceCellsCleared"/>, the only thing that reads it.
        /// </para>
        /// </summary>
        public int IceCellsMeltedCount { get; }
    }
}
