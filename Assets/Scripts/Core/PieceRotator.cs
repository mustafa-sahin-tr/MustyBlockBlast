using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Turns a piece 90 degrees clockwise by <em>looking the result up in</em> <see cref="PieceCatalog"/>,
    /// never by synthesising a new <see cref="Piece"/>.
    /// <para>
    /// The catalog already authors every orientation of every asymmetric shape as its own entry
    /// ("line_h5"/"line_v5", the four "t_*", the four "corner3_*", and so on), so the rotation of any
    /// catalog piece is itself a catalog piece. Rotating therefore means swapping one catalog reference
    /// for another, and a piece's <see cref="Piece.Id"/> never stops describing its geometry — which is
    /// what keeps every id-keyed consumer (<see cref="PieceFamilyClassifier"/>, id-matched objectives,
    /// catalog validation) correct by construction. A piece carrying rotated offsets under its old id
    /// would silently break all three.
    /// </para>
    /// <para>
    /// Symmetry falls out of the same mechanism instead of being a hardcoded list of ids: a piece is
    /// fully symmetrical exactly when its own rotation reproduces its own shape — which is true of 1x1,
    /// 2x2 and 3x3 and of nothing else — so the rule can never drift out of sync with the catalog.
    /// </para>
    /// <para>
    /// Allocates nothing: rotated offsets are computed one at a time as <see cref="GridPosition"/>
    /// values and compared in place, never gathered into a buffer. The aim preview asks whether the
    /// piece under the pointer can be turned on every frame of a drag, so this has to be free of GC.
    /// </para>
    /// </summary>
    public static class PieceRotator
    {
        /// <summary>
        /// True when turning <paramref name="piece"/> 90 degrees clockwise reproduces its own shape, so
        /// there is no rotation to offer. Derived from the offsets, not from the id.
        /// </summary>
        public static bool IsFullySymmetrical(Piece piece)
        {
            if (piece == null)
            {
                return false;
            }

            return IsRotationOf(piece.Offsets, piece.Offsets);
        }

        /// <summary>
        /// The catalog piece whose shape is <paramref name="piece"/> turned 90 degrees clockwise.
        /// <para>
        /// False — with <paramref name="rotated"/> left null — when the piece is fully symmetrical (the
        /// rotation would be a no-op), and defensively also when no catalog entry matches the rotated
        /// shape, so a shape from outside the catalog degrades into a refusal rather than into a
        /// fabricated piece.
        /// </para>
        /// </summary>
        public static bool TryRotateClockwise(Piece piece, out Piece rotated)
        {
            rotated = null;
            if (piece == null || IsRotationOf(piece.Offsets, piece.Offsets))
            {
                return false;
            }

            IReadOnlyList<Piece> catalog = PieceCatalog.AllPieces;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (IsRotationOf(piece.Offsets, catalog[i].Offsets))
                {
                    rotated = catalog[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> is exactly <paramref name="source"/> turned 90 degrees
        /// clockwise.
        /// <para>
        /// Compared as sets, not as sequences: a piece's offsets are authored in whatever order reads
        /// best in the catalog, so two identical shapes routinely list their cells in different orders.
        /// A piece never repeats an offset, so equal counts plus containment is set equality.
        /// </para>
        /// </summary>
        private static bool IsRotationOf(
            IReadOnlyList<GridPosition> source, IReadOnlyList<GridPosition> candidate)
        {
            if (source.Count != candidate.Count)
            {
                return false;
            }

            GetRotationOrigin(source, out int minY, out int maxX);

            for (int i = 0; i < source.Count; i++)
            {
                if (!Contains(candidate, RotateClockwise(source[i], minY, maxX)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// One offset turned 90 degrees clockwise and re-normalised back to a non-negative origin.
        /// <para>
        /// The board's origin is bottom-left (Y-up), which makes <c>(x,y) -> (y,-x)</c> the clockwise
        /// turn. That result is partly negative, so it has to be translated back before it can be
        /// compared against catalog offsets, which are always non-negative: the turned set's minimum X
        /// is the original's minimum Y, and its minimum Y is the negated original maximum X — which
        /// folds the whole translation into the two terms below.
        /// </para>
        /// </summary>
        private static GridPosition RotateClockwise(GridPosition offset, int minY, int maxX)
            => new GridPosition(offset.Y - minY, maxX - offset.X);

        /// <summary>The two extents <see cref="RotateClockwise"/> needs to re-normalise with. Read in
        /// one pass rather than assumed: nothing guarantees a piece's offsets start at the origin.</summary>
        private static void GetRotationOrigin(IReadOnlyList<GridPosition> offsets, out int minY, out int maxX)
        {
            minY = int.MaxValue;
            maxX = int.MinValue;

            for (int i = 0; i < offsets.Count; i++)
            {
                GridPosition offset = offsets[i];
                if (offset.Y < minY)
                {
                    minY = offset.Y;
                }

                if (offset.X > maxX)
                {
                    maxX = offset.X;
                }
            }
        }

        private static bool Contains(IReadOnlyList<GridPosition> offsets, GridPosition offset)
        {
            for (int i = 0; i < offsets.Count; i++)
            {
                if (offsets[i].Equals(offset))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
