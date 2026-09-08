using MustyBlockBlast.Core;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Presentation-only geometry helpers for laying a piece out on screen.</summary>
    internal static class PieceLayout
    {
        /// <summary>Bounding box of the piece's offsets, in cells. Offsets are always non-negative.</summary>
        internal static void GetBounds(Piece piece, out int width, out int height)
        {
            int maxX = 0;
            int maxY = 0;
            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition offset = piece.Offsets[i];
                if (offset.X > maxX)
                {
                    maxX = offset.X;
                }

                if (offset.Y > maxY)
                {
                    maxY = offset.Y;
                }
            }

            width = maxX + 1;
            height = maxY + 1;
        }
    }
}
