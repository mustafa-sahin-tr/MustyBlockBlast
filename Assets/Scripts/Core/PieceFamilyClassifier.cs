using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Maps a <see cref="PieceCatalog"/> piece id onto its <see cref="PieceFamily"/>. The catalog ids
    /// are prefixed by shape ("line_h3", "corner2_missing_tr", "t_up"), so the family is a pure
    /// function of the id and no lookup table has to be kept in sync with the catalog.
    /// </summary>
    public static class PieceFamilyClassifier
    {
        /// <summary>
        /// Family for the given catalog id. Unknown or null ids fall back to
        /// <see cref="PieceFamily.Single"/> rather than throwing, so a mis-typed id degrades an
        /// objective instead of breaking a run.
        /// </summary>
        public static PieceFamily Classify(string pieceId)
        {
            if (string.IsNullOrEmpty(pieceId))
            {
                return PieceFamily.Single;
            }

            // Ordered most-specific-first so a longer prefix is never shadowed by a shorter one.
            if (pieceId.StartsWith("single_", StringComparison.Ordinal))
            {
                return PieceFamily.Single;
            }

            if (pieceId.StartsWith("square_", StringComparison.Ordinal))
            {
                return PieceFamily.Square;
            }

            if (pieceId.StartsWith("corner2_", StringComparison.Ordinal)
                || pieceId.StartsWith("corner3_", StringComparison.Ordinal))
            {
                return PieceFamily.Corner;
            }

            if (pieceId.StartsWith("line_", StringComparison.Ordinal))
            {
                return PieceFamily.Line;
            }

            if (pieceId.StartsWith("t_", StringComparison.Ordinal))
            {
                return PieceFamily.TShape;
            }

            if (pieceId.StartsWith("s_", StringComparison.Ordinal))
            {
                return PieceFamily.SShape;
            }

            if (pieceId.StartsWith("z_", StringComparison.Ordinal))
            {
                return PieceFamily.ZShape;
            }

            return PieceFamily.Single;
        }
    }
}
