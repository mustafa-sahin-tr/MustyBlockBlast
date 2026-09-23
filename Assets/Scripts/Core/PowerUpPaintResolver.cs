using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Result of recolouring a region of the board with a power-up (issue #295). Its own shape rather
    /// than a <see cref="PowerUpClearResult"/> because nothing about a paint is a clear: no cell is
    /// emptied, no line empties out, no special cell is destroyed and no colour is tallied as gone —
    /// every field that struct carries would be meaningless here, and a consumer reading a paint as a
    /// zero-cell clear would be reading it wrong.
    /// </summary>
    public readonly struct PowerUpPaintResult
    {
        public PowerUpPaintResult(IReadOnlyList<GridPosition> paintedCells, int colourId)
        {
            PaintedCells = paintedCells;
            ColourId = colourId;
        }

        /// <summary>Exactly the cells that held a colour inside the targeted region and now hold
        /// <see cref="ColourId"/> — a cell that already wore that colour is included, since it was
        /// occupied and targeted; an empty cell inside the region is not, since nothing stood on it to
        /// paint.</summary>
        public IReadOnlyList<GridPosition> PaintedCells { get; }

        /// <summary>The colour every entry of <see cref="PaintedCells"/> now holds.</summary>
        public int ColourId { get; }

        public int PaintedCellCount => PaintedCells.Count;

        /// <summary>Doubles as the call's legality signal, the way <see cref="PowerUpClearResult.AnyCleared"/>
        /// does for a colour cleanser: false means the region held nothing to paint and the board was
        /// not touched, which the caller must treat as "the player aimed at nothing" and charge for
        /// nothing.</summary>
        public bool AnyPainted => PaintedCellCount > 0;
    }

    /// <summary>
    /// Recolours a region of the board outside the normal placement flow — today, the cross of a
    /// targeted cell's row and column. The counterpart of <see cref="PowerUpClearResolver"/> for the one
    /// kind that changes what is on the board without removing any of it: cells stay in place, keep
    /// every special property they carry (see <see cref="Board.Paint"/>) and are never cleared here —
    /// not even a line the paint happens to make monochrome, because colour never affects clearing.
    /// <para>
    /// Follows <see cref="PowerUpClearResolver"/>'s convention of resolving and applying in one call:
    /// occupancy is read first to settle what the paint will touch, and the mutation follows only when
    /// there is something to touch.
    /// </para>
    /// </summary>
    public static class PowerUpPaintResolver
    {
        /// <summary>Scratch buffer for the targeted geometry handed back by
        /// <see cref="PowerUpTargetCells"/>, reused for the reason <c>PowerUpClearResolver</c> reuses
        /// its own: it never escapes a Resolve call.</summary>
        private static readonly List<GridPosition> TargetBuffer =
            new List<GridPosition>(PowerUpTargetCells.MAX_TARGET_CELLS);

        /// <summary>Shared, never-mutated empty for a rejected call — avoids allocating a fresh empty
        /// list for the "aimed at nothing" case.</summary>
        private static readonly GridPosition[] EmptyCells = new GridPosition[0];

        /// <summary>
        /// Paints every occupied cell of <paramref name="target"/>'s row and column
        /// <paramref name="colourId"/>. Empty cells in the cross are left empty, and a cross with no
        /// occupied cell at all is rejected outright — nothing is read for painting or written — so
        /// <see cref="PowerUpPaintResult.AnyPainted"/> doubles as the call's legality signal, the same
        /// contract <see cref="PowerUpClearResolver.ResolveColorCleanser"/> establishes for an empty
        /// target. The target cell itself need not be occupied: it names the cross, not the block.
        /// </summary>
        public static PowerUpPaintResult ResolvePaintCross(Board board, GridPosition target, int colourId)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (!board.IsInside(target))
            {
                throw new ArgumentOutOfRangeException(nameof(target), target, "Outside the board.");
            }

            if (colourId < 1 || colourId > Board.COLOUR_COUNT)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(colourId), colourId, $"A paint colour id must be between 1 and {Board.COLOUR_COUNT}.");
            }

            IReadOnlyList<GridPosition> targetedCells =
                PowerUpTargetCells.ForPaintCross(board.Shape, target, TargetBuffer);

            // Occupancy is settled before anything is painted, for the reason the clear resolver reads
            // it before clearing: the result must list exactly what was standing there when the tap
            // landed, and nothing else.
            var paintedCells = new List<GridPosition>();
            for (int i = 0; i < targetedCells.Count; i++)
            {
                GridPosition position = targetedCells[i];
                if (board.IsOccupied(position))
                {
                    paintedCells.Add(position);
                }
            }

            if (paintedCells.Count == 0)
            {
                return new PowerUpPaintResult(EmptyCells, colourId);
            }

            for (int i = 0; i < paintedCells.Count; i++)
            {
                board.Paint(paintedCells[i], colourId);
            }

            return new PowerUpPaintResult(paintedCells, colourId);
        }
    }
}
