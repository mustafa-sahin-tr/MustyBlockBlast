using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>Published once per legal placement, after the board and any line clears resolved.
    /// Carries <see cref="LinesCleared"/> so scoring is a pure function of this one message.</summary>
    public readonly struct PiecePlacedMessage
    {
        public PiecePlacedMessage(string pieceId, GridPosition anchor, int cellCount, int colourId, int linesCleared)
        {
            PieceId = pieceId;
            Anchor = anchor;
            CellCount = cellCount;
            ColourId = colourId;
            LinesCleared = linesCleared;
        }

        public string PieceId { get; }

        public GridPosition Anchor { get; }

        public int CellCount { get; }

        public int ColourId { get; }

        public int LinesCleared { get; }
    }
}
