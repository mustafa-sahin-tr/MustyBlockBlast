using System;

namespace MustyBlockBlast.Core
{
    /// <summary>Anchor a drag should preview/place at, and whether it is a legal placement.</summary>
    public readonly struct SnapResult
    {
        public GridPosition Anchor { get; }

        public bool IsValid { get; }

        public SnapResult(GridPosition anchor, bool isValid)
        {
            Anchor = anchor;
            IsValid = isValid;
        }
    }

    /// <summary>
    /// Sticky/hysteresis snapping for the placement preview. The raw pointer anchor always wins when
    /// it is itself legal — there is no stickiness against a merely-nearby valid spot. Hysteresis only
    /// kicks in once the raw anchor stops being legal: the previously locked anchor is kept as long as
    /// the piece still overlaps it enough, so small hand tremor at the edge of a legal spot does not
    /// flip the preview straight to invalid. Otherwise the nearest legal placement nearby is searched.
    /// Pure C#, one instance per drag session — call <see cref="Reset"/> when a drag starts.
    /// </summary>
    public sealed class PlacementSnapper
    {
        private const float OverlapThreshold = 0.2f;

        private GridPosition _anchor;
        private bool _hasAnchor;

        /// <summary>Clears the lock. Call when a new drag begins.</summary>
        public void Reset()
        {
            _hasAnchor = false;
        }

        /// <summary>Resolves the anchor to use for this frame's raw pointer anchor.</summary>
        public SnapResult Resolve(Board board, Piece piece, GridPosition rawAnchor, int searchRadius)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (piece == null)
            {
                throw new ArgumentNullException(nameof(piece));
            }

            if (PlacementRules.CanPlace(board, piece, rawAnchor))
            {
                _anchor = rawAnchor;
                _hasAnchor = true;
                return new SnapResult(rawAnchor, true);
            }

            if (_hasAnchor
                && OverlapFraction(piece, _anchor, rawAnchor) >= OverlapThreshold
                && PlacementRules.CanPlace(board, piece, _anchor))
            {
                return new SnapResult(_anchor, true);
            }

            if (TryFindNearestCandidate(board, piece, rawAnchor, searchRadius, out GridPosition candidate))
            {
                _anchor = candidate;
                _hasAnchor = true;
                return new SnapResult(candidate, true);
            }

            _hasAnchor = false;
            return new SnapResult(rawAnchor, false);
        }

        /// <summary>Fraction (0..1) of the piece's cells that occupy the same board cells whether
        /// anchored at <paramref name="anchorA"/> or <paramref name="anchorB"/>.</summary>
        private static float OverlapFraction(Piece piece, GridPosition anchorA, GridPosition anchorB)
        {
            int overlapCount = 0;
            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition cellA = anchorA + piece.Offsets[i];
                for (int j = 0; j < piece.Offsets.Count; j++)
                {
                    if (cellA.Equals(anchorB + piece.Offsets[j]))
                    {
                        overlapCount++;
                        break;
                    }
                }
            }

            return (float)overlapCount / piece.CellCount;
        }

        /// <summary>Nearest legal anchor within <paramref name="searchRadius"/> cells of
        /// <paramref name="rawAnchor"/> (Euclidean, ties broken toward the anchor already locked
        /// before this call, if it is one of the tied candidates).</summary>
        private bool TryFindNearestCandidate(
            Board board, Piece piece, GridPosition rawAnchor, int searchRadius, out GridPosition candidate)
        {
            candidate = default;
            bool found = false;
            int bestDistanceSq = int.MaxValue;
            bool bestIsCurrentAnchor = false;
            int radiusSq = searchRadius * searchRadius;

            for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
            {
                for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
                {
                    int distanceSq = (offsetX * offsetX) + (offsetY * offsetY);
                    if (distanceSq > radiusSq)
                    {
                        continue;
                    }

                    var testAnchor = new GridPosition(rawAnchor.X + offsetX, rawAnchor.Y + offsetY);
                    if (!PlacementRules.CanPlace(board, piece, testAnchor))
                    {
                        continue;
                    }

                    bool isCurrentAnchor = _hasAnchor && testAnchor.Equals(_anchor);
                    bool isBetter = !found
                        || distanceSq < bestDistanceSq
                        || (distanceSq == bestDistanceSq && isCurrentAnchor && !bestIsCurrentAnchor);

                    if (!isBetter)
                    {
                        continue;
                    }

                    found = true;
                    bestDistanceSq = distanceSq;
                    bestIsCurrentAnchor = isCurrentAnchor;
                    candidate = testAnchor;
                }
            }

            return found;
        }
    }
}
