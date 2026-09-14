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
            int occupiedCellCountBeforeClear)
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
    }
}
