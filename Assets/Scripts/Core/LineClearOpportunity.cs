using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Answers, for one candidate piece against the board as it stands, "could any legal placement of
    /// this clear a line, and how many at once at best?" — the line-clear-aware counterpart of
    /// <see cref="MoveAvailability.CanPlaceAnywhere"/>, which only asks whether the piece fits at all.
    /// <para>
    /// Every legal anchor is projected through <see cref="LineClearResolver.PreviewClears"/>, the same
    /// non-mutating primitive the drag preview and <see cref="GhostFitSearch"/> use, so "how many lines
    /// does this placement clear" keeps its single project-wide definition (holes, reinforced cells and
    /// all). Nothing here re-derives fullness.
    /// </para>
    /// <para>
    /// An instance owns its scratch board and line buffers and reuses them across calls, exactly as
    /// <see cref="GhostFitSearch"/> does, so a caller that evaluates dozens of candidate pieces in a
    /// row — a bounded retry draw, say — allocates nothing after the first call on a given board shape.
    /// </para>
    /// </summary>
    public sealed class LineClearOpportunity
    {
        private readonly List<int> _rowsBuffer = new List<int>(Board.SIZE);
        private readonly List<int> _columnsBuffer = new List<int>(Board.SIZE);

        /// <summary>Rebuilt only when the board being evaluated has a different shape from the one the
        /// last call saw — a shape changes at most once per level, so repeated calls on the same board
        /// stay allocation-free.</summary>
        private BoardShape _scratchShape;
        private Board _previewBoard;

        /// <summary>True when at least one legal placement of <paramref name="piece"/> on
        /// <paramref name="board"/> would complete at least one row or column.</summary>
        public bool CanClearAnyLine(Board board, Piece piece)
            => MaxLinesAnyPlacementClears(board, piece) > 0;

        /// <summary>
        /// The most rows + columns any single legal placement of <paramref name="piece"/> on
        /// <paramref name="board"/> would clear simultaneously. Zero when no placement clears a line —
        /// which includes the case where the piece has no legal placement at all, so a positive result
        /// always implies the piece fits somewhere.
        /// </summary>
        public int MaxLinesAnyPlacementClears(Board board, Piece piece)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (piece == null)
            {
                throw new ArgumentNullException(nameof(piece));
            }

            EnsureScratchFor(board);

            int best = 0;
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var anchor = new GridPosition(x, y);
                    if (!PlacementRules.CanPlace(board, piece, anchor))
                    {
                        continue;
                    }

                    LineClearResult clears = LineClearResolver.PreviewClears(
                        board, piece, anchor, _previewBoard, _rowsBuffer, _columnsBuffer);

                    if (clears.LineCount > best)
                    {
                        best = clears.LineCount;
                    }
                }
            }

            return best;
        }

        private void EnsureScratchFor(Board board)
        {
            if (ReferenceEquals(_scratchShape, board.Shape))
            {
                return;
            }

            _scratchShape = board.Shape;
            _previewBoard = new Board(_scratchShape);
        }
    }
}
