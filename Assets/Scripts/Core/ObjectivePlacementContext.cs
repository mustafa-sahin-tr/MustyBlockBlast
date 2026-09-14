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
            PieceFamily pieceFamily,
            int currentRunScore,
            bool boardEmptyAfterPlacement,
            int currentStreak)
        {
            LinesCleared = linesCleared;
            PieceFamily = pieceFamily;
            CurrentRunScore = currentRunScore;
            BoardEmptyAfterPlacement = boardEmptyAfterPlacement;
            CurrentStreak = currentStreak;
        }

        /// <summary>Rows plus columns cleared by this placement; zero when nothing cleared.</summary>
        public int LinesCleared { get; }

        /// <summary>Shape family of the piece that was just placed.</summary>
        public PieceFamily PieceFamily { get; }

        /// <summary>Run score as it stands after this placement was scored.</summary>
        public int CurrentRunScore { get; }

        /// <summary>True when, after this placement's line clears resolved, the board had zero occupied
        /// cells — a "perfect clear".</summary>
        public bool BoardEmptyAfterPlacement { get; }

        /// <summary>Combo streak as it stands after this placement was scored.</summary>
        public int CurrentStreak { get; }
    }
}
