using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Everything <see cref="ObjectiveProgress"/> needs to know about a single placement. Pure data —
    /// no Unity types — so objective tracking stays testable without the engine. Deliberately separate
    /// from <see cref="ScorePlacementContext"/>: the two answer different questions and neither should
    /// grow fields only the other cares about.
    /// </summary>
    public readonly struct ObjectivePlacementContext
    {
        public ObjectivePlacementContext(
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            PieceFamily pieceFamily,
            string pieceId,
            int currentRunScore,
            bool boardEmptyAfterPlacement,
            int currentStreak,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            float elapsedRunSeconds,
            int reinforcedCellsFullyCleared)
            : this(
                linesCleared, rowsCleared, columnsCleared, pieceFamily, pieceId, currentRunScore,
                boardEmptyAfterPlacement, currentStreak, occupiedCellCountBeforeClear, anyCornerCleared,
                centerCoreEmptyAfterPlacement, hasIsolatedHolesAfterPlacement, elapsedRunSeconds,
                reinforcedCellsFullyCleared, destroyedCellCountByColour: null)
        {
        }

        public ObjectivePlacementContext(
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            PieceFamily pieceFamily,
            string pieceId,
            int currentRunScore,
            bool boardEmptyAfterPlacement,
            int currentStreak,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            float elapsedRunSeconds,
            int reinforcedCellsFullyCleared,
            IReadOnlyList<int> destroyedCellCountByColour)
            : this(
                linesCleared, rowsCleared, columnsCleared, pieceFamily, pieceId, currentRunScore,
                boardEmptyAfterPlacement, currentStreak, occupiedCellCountBeforeClear, anyCornerCleared,
                centerCoreEmptyAfterPlacement, hasIsolatedHolesAfterPlacement, elapsedRunSeconds,
                reinforcedCellsFullyCleared, destroyedCellCountByColour, timerCellsClearedInTime: 0)
        {
        }

        public ObjectivePlacementContext(
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            PieceFamily pieceFamily,
            string pieceId,
            int currentRunScore,
            bool boardEmptyAfterPlacement,
            int currentStreak,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            float elapsedRunSeconds,
            int reinforcedCellsFullyCleared,
            IReadOnlyList<int> destroyedCellCountByColour,
            int timerCellsClearedInTime)
            : this(
                linesCleared, rowsCleared, columnsCleared, pieceFamily, pieceId, currentRunScore,
                boardEmptyAfterPlacement, currentStreak, occupiedCellCountBeforeClear, anyCornerCleared,
                centerCoreEmptyAfterPlacement, hasIsolatedHolesAfterPlacement, elapsedRunSeconds,
                reinforcedCellsFullyCleared, destroyedCellCountByColour, timerCellsClearedInTime,
                destroyedDiamondCountByColour: null)
        {
        }

        public ObjectivePlacementContext(
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            PieceFamily pieceFamily,
            string pieceId,
            int currentRunScore,
            bool boardEmptyAfterPlacement,
            int currentStreak,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            float elapsedRunSeconds,
            int reinforcedCellsFullyCleared,
            IReadOnlyList<int> destroyedCellCountByColour,
            int timerCellsClearedInTime,
            IReadOnlyList<int> destroyedDiamondCountByColour)
            : this(
                linesCleared, rowsCleared, columnsCleared, pieceFamily, pieceId, currentRunScore,
                boardEmptyAfterPlacement, currentStreak, occupiedCellCountBeforeClear, anyCornerCleared,
                centerCoreEmptyAfterPlacement, hasIsolatedHolesAfterPlacement, elapsedRunSeconds,
                reinforcedCellsFullyCleared, destroyedCellCountByColour, timerCellsClearedInTime,
                destroyedDiamondCountByColour, iceCellsMelted: 0)
        {
        }

        public ObjectivePlacementContext(
            int linesCleared,
            int rowsCleared,
            int columnsCleared,
            PieceFamily pieceFamily,
            string pieceId,
            int currentRunScore,
            bool boardEmptyAfterPlacement,
            int currentStreak,
            int occupiedCellCountBeforeClear,
            bool anyCornerCleared,
            bool centerCoreEmptyAfterPlacement,
            bool hasIsolatedHolesAfterPlacement,
            float elapsedRunSeconds,
            int reinforcedCellsFullyCleared,
            IReadOnlyList<int> destroyedCellCountByColour,
            int timerCellsClearedInTime,
            IReadOnlyList<int> destroyedDiamondCountByColour,
            int iceCellsMelted)
        {
            LinesCleared = linesCleared;
            RowsCleared = rowsCleared;
            ColumnsCleared = columnsCleared;
            PieceFamily = pieceFamily;
            PieceId = pieceId;
            CurrentRunScore = currentRunScore;
            BoardEmptyAfterPlacement = boardEmptyAfterPlacement;
            CurrentStreak = currentStreak;
            OccupiedCellCountBeforeClear = occupiedCellCountBeforeClear;
            AnyCornerCleared = anyCornerCleared;
            CenterCoreEmptyAfterPlacement = centerCoreEmptyAfterPlacement;
            HasIsolatedHolesAfterPlacement = hasIsolatedHolesAfterPlacement;
            ElapsedRunSeconds = elapsedRunSeconds;
            ReinforcedCellsFullyCleared = reinforcedCellsFullyCleared;
            DestroyedCellCountByColour = destroyedCellCountByColour;
            TimerCellsClearedInTime = timerCellsClearedInTime;
            DestroyedDiamondCountByColour = destroyedDiamondCountByColour;
            IceCellsMelted = iceCellsMelted;
        }

        /// <summary>Rows plus columns cleared by this placement; zero when nothing cleared.</summary>
        public int LinesCleared { get; }

        /// <summary>Of <see cref="LinesCleared"/>, how many were rows. Split out from the summed total
        /// so an objective can tell "2 rows" apart from "1 row + 1 column".</summary>
        public int RowsCleared { get; }

        /// <summary>Of <see cref="LinesCleared"/>, how many were columns.</summary>
        public int ColumnsCleared { get; }

        /// <summary>Shape family of the piece that was just placed.</summary>
        public PieceFamily PieceFamily { get; }

        /// <summary>Catalog id of the piece that was just placed (e.g. <c>"square_3x3"</c>). Finer-
        /// grained than <see cref="PieceFamily"/>, which groups every size of a shape together — an
        /// objective that cares about one specific size or orientation needs this instead.</summary>
        public string PieceId { get; }

        /// <summary>Run score as it stands after this placement was scored.</summary>
        public int CurrentRunScore { get; }

        /// <summary>True when, after this placement's line clears resolved, the board had zero occupied
        /// cells — a "perfect clear".</summary>
        public bool BoardEmptyAfterPlacement { get; }

        /// <summary>Combo streak as it stands after this placement was scored.</summary>
        public int CurrentStreak { get; }

        /// <summary>How many cells were occupied immediately after the piece was placed but before any
        /// line clears from this placement resolved — the board's "under pressure" reading a clutch
        /// objective checks against.</summary>
        public int OccupiedCellCountBeforeClear { get; }

        /// <summary>True when this placement's clear touched a board corner cell — its row or column
        /// (whichever cleared) included coordinate (0,0), (SIZE-1,0), (0,SIZE-1) or (SIZE-1,SIZE-1).</summary>
        public bool AnyCornerCleared { get; }

        /// <summary>True when, after this placement's line clears resolved, the board's centered 4x4
        /// core was completely empty.</summary>
        public bool CenterCoreEmptyAfterPlacement { get; }

        /// <summary>True when, after this placement's line clears resolved, at least one empty cell on
        /// the board is unreachable from the edge through other empty cells.</summary>
        public bool HasIsolatedHolesAfterPlacement { get; }

        /// <summary>Wall-clock seconds elapsed since the current run started, as of this placement.
        /// Independent of any Timed-mode countdown — it runs identically in Endless and Timed mode and
        /// is never paused by a modal being open.</summary>
        public float ElapsedRunSeconds { get; }

        /// <summary>How many reinforced cells this placement's line clears fully removed (hit count
        /// reached 0) — zero for an ordinary placement, and zero for a reinforced cell that merely took a
        /// hit and survived. See <see cref="ObjectiveType.ReinforcedCellsCleared"/>.</summary>
        public int ReinforcedCellsFullyCleared { get; }

        /// <summary>
        /// How many cells of each colour this placement's clears destroyed, indexed by colour id (index
        /// 0 is unused). Null when nothing was tallied — read through <see cref="DestroyedCountOf"/>,
        /// which treats null as "none".
        /// </summary>
        public IReadOnlyList<int> DestroyedCellCountByColour { get; }

        /// <summary>Cells of <paramref name="colourId"/> this placement destroyed, or zero when none were
        /// tallied or the id is outside the tally.</summary>
        public int DestroyedCountOf(int colourId)
            => ColourTally.CountOf(DestroyedCellCountByColour, colourId);

        /// <summary>How many <see cref="SpecialCellKind.Timer"/> cells this placement's whole resolution
        /// destroyed BEFORE their countdown reached 0 — the primary clear, every cascaded phase, and a
        /// special cell's own blast/wipe/strike mid-cascade alike. See
        /// <see cref="ObjectiveType.TimerCellsMeltedInTime"/>.</summary>
        public int TimerCellsClearedInTime { get; }

        /// <summary>
        /// How many <see cref="SpecialCellKind.Diamond"/> cells this placement's whole resolution
        /// destroyed — the primary clear, every cascaded phase, and a special cell's own
        /// blast/wipe/strike mid-cascade alike — indexed by the gem's own colour id (index 0 unused),
        /// never by the block's. Null when nothing was tallied — read through
        /// <see cref="DestroyedDiamondCountOf"/>, which treats null as "none". See
        /// <see cref="ObjectiveType.DiamondsCleared"/>.
        /// </summary>
        public IReadOnlyList<int> DestroyedDiamondCountByColour { get; }

        /// <summary>Diamonds of gem colour <paramref name="colourId"/> this placement destroyed, or zero
        /// when none were tallied or the id is outside the tally.</summary>
        public int DestroyedDiamondCountOf(int colourId)
            => ColourTally.CountOf(DestroyedDiamondCountByColour, colourId);

        /// <summary>How many ice sockets this placement's whole resolution melted to level 0 — every
        /// destruction path included, because the melt lives in <see cref="Board.TryDamage"/> and the
        /// count is a before/after scan of <see cref="Board.CountIceCells"/>. Zero for an ordinary
        /// placement, and zero for a socket that merely lost a level and is still icy. See
        /// <see cref="ObjectiveType.IceCellsCleared"/>.</summary>
        public int IceCellsMelted { get; }
    }
}
