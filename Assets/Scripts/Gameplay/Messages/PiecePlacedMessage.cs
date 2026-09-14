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
    }
}
