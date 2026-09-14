using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The cells one power-up targets, as pure geometry: no occupancy is read and nothing is mutated.
    /// <para>
    /// This is the single definition of "what a power-up hits". <see cref="PowerUpClearResolver"/>
    /// filters these cells by occupancy to decide what actually clears, and the drag-time preview
    /// tints exactly the same set — so the preview can never promise a region the application would
    /// not touch (notably the 3x3 bomb clamp at the board edges).
    /// </para>
    /// <para>
    /// Every method fills a caller-owned buffer instead of allocating, because the preview calls them
    /// on every pointer-move frame.
    /// </para>
    /// </summary>
    public static class PowerUpTargetCells
    {
        /// <summary>Cells reached in each direction from a bomb's centre, before the board clamp.</summary>
        private const int BOMB_RADIUS = 1;

        /// <summary>Cells in an unclamped bomb footprint.</summary>
        private const int BOMB_CELL_COUNT = ((BOMB_RADIUS * 2) + 1) * ((BOMB_RADIUS * 2) + 1);

        /// <summary>
        /// The most cells any one power-up can target — the larger of a full line and an unclamped
        /// bomb. Buffers passed to these methods should be created with at least this capacity, or the
        /// first call that exceeds their capacity grows the list: an allocation on the per-frame aim
        /// path, which the caller-owned-buffer design exists to avoid.
        /// </summary>
        public const int MAX_TARGET_CELLS = BOMB_CELL_COUNT > Board.SIZE ? BOMB_CELL_COUNT : Board.SIZE;

        /// <summary>
        /// The 3x3 area centred on <paramref name="center"/>, clamped to the board — a corner centre
        /// therefore yields 4 cells and an edge centre 6. An off-board centre yields nothing.
        /// </summary>
        public static IReadOnlyList<GridPosition> ForBomb(GridPosition center, List<GridPosition> buffer)
        {
            Prepare(buffer);

            if (!Board.IsInside(center))
            {
                return buffer;
            }

            int minX = Math.Max(0, center.X - BOMB_RADIUS);
            int maxX = Math.Min(Board.SIZE - 1, center.X + BOMB_RADIUS);
            int minY = Math.Max(0, center.Y - BOMB_RADIUS);
            int maxY = Math.Min(Board.SIZE - 1, center.Y + BOMB_RADIUS);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    buffer.Add(new GridPosition(x, y));
                }
            }

            return buffer;
        }

        /// <summary>Every cell of <paramref name="row"/>. An off-board row yields nothing.</summary>
        public static IReadOnlyList<GridPosition> ForRow(int row, List<GridPosition> buffer)
        {
            Prepare(buffer);

            if (!IsValidLineIndex(row))
            {
                return buffer;
            }

            for (int x = 0; x < Board.SIZE; x++)
            {
                buffer.Add(new GridPosition(x, row));
            }

            return buffer;
        }

        /// <summary>Every cell of <paramref name="column"/>. An off-board column yields nothing.</summary>
        public static IReadOnlyList<GridPosition> ForColumn(int column, List<GridPosition> buffer)
        {
            Prepare(buffer);

            if (!IsValidLineIndex(column))
            {
                return buffer;
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                buffer.Add(new GridPosition(column, y));
            }

            return buffer;
        }

        /// <summary>
        /// The single cell a joker fills. Listed through the same buffer-filling shape as the other
        /// kinds so the aim preview has one uniform way to ask "what does the armed power-up hit?".
        /// An off-board target yields nothing.
        /// </summary>
        public static IReadOnlyList<GridPosition> ForJoker(GridPosition target, List<GridPosition> buffer)
        {
            Prepare(buffer);

            if (!Board.IsInside(target))
            {
                return buffer;
            }

            buffer.Add(target);
            return buffer;
        }

        /// <summary>True when <paramref name="index"/> names a row/column that exists.</summary>
        public static bool IsValidLineIndex(int index) => index >= 0 && index < Board.SIZE;

        private static void Prepare(List<GridPosition> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();
        }
    }
}
