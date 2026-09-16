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
        /// The nominal capacity a target buffer should be created with — the larger of a standard-board
        /// line and an unclamped bomb. A hint, not a bound: <see cref="List{T}"/> grows, so a board
        /// wider than this costs one growth on the first aim frame of that level and nothing after.
        /// Nothing is ever truncated to it.
        /// </summary>
        public const int MAX_TARGET_CELLS = BOMB_CELL_COUNT > Board.SIZE ? BOMB_CELL_COUNT : Board.SIZE;

        /// <summary>
        /// The 3x3 area centred on <paramref name="center"/>, clamped to the board and with hole cells
        /// dropped — a corner centre therefore yields 4 cells and an edge centre 6. An off-board centre
        /// yields nothing.
        /// </summary>
        public static IReadOnlyList<GridPosition> ForBomb(
            BoardShape shape, GridPosition center, List<GridPosition> buffer)
        {
            Prepare(shape, buffer);

            if (!shape.IsInside(center))
            {
                return buffer;
            }

            int minX = Math.Max(0, center.X - BOMB_RADIUS);
            int maxX = Math.Min(shape.Width - 1, center.X + BOMB_RADIUS);
            int minY = Math.Max(0, center.Y - BOMB_RADIUS);
            int maxY = Math.Min(shape.Height - 1, center.Y + BOMB_RADIUS);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    AddIfPlayable(shape, new GridPosition(x, y), buffer);
                }
            }

            return buffer;
        }

        /// <summary>Every playable cell of <paramref name="row"/>. An off-board row yields nothing.</summary>
        public static IReadOnlyList<GridPosition> ForRow(
            BoardShape shape, int row, List<GridPosition> buffer)
        {
            Prepare(shape, buffer);

            if (!IsValidRowIndex(shape, row))
            {
                return buffer;
            }

            for (int x = 0; x < shape.Width; x++)
            {
                AddIfPlayable(shape, new GridPosition(x, row), buffer);
            }

            return buffer;
        }

        /// <summary>Every playable cell of <paramref name="column"/>. An off-board column yields nothing.</summary>
        public static IReadOnlyList<GridPosition> ForColumn(
            BoardShape shape, int column, List<GridPosition> buffer)
        {
            Prepare(shape, buffer);

            if (!IsValidColumnIndex(shape, column))
            {
                return buffer;
            }

            for (int y = 0; y < shape.Height; y++)
            {
                AddIfPlayable(shape, new GridPosition(column, y), buffer);
            }

            return buffer;
        }

        /// <summary>
        /// The single cell a joker fills. Listed through the same buffer-filling shape as the other
        /// kinds so the aim preview has one uniform way to ask "what does the armed power-up hit?".
        /// An off-board target yields nothing.
        /// </summary>
        public static IReadOnlyList<GridPosition> ForJoker(
            BoardShape shape, GridPosition target, List<GridPosition> buffer)
        {
            Prepare(shape, buffer);
            AddIfPlayable(shape, target, buffer);
            return buffer;
        }

        /// <summary>
        /// The single cell a colour cleanser tap aims at. Deliberately NOT the full set of cells that
        /// would actually clear (which depends on board content, not just position, and could be up to
        /// the whole board) — scanning that live on every pointer-move frame would need its own
        /// board-sized buffer distinct from every other kind's small fixed-geometry one. This is a
        /// lightweight aim reticle, not a full preview; <see cref="PowerUpClearResolver.ResolveColorCleanser"/>
        /// is still the sole authority on what actually clears when the tap lands.
        /// </summary>
        public static IReadOnlyList<GridPosition> ForColorCleanser(
            BoardShape shape, GridPosition target, List<GridPosition> buffer)
        {
            Prepare(shape, buffer);
            AddIfPlayable(shape, target, buffer);
            return buffer;
        }

        /// <summary>
        /// The single cell a <see cref="SpecialPieceKind.DemolitionHammer"/> tap aims at. Not a
        /// power-up — the hammer is a dock piece and never enters the inventory — but it is armed and
        /// aimed exactly as one is, so its reticle belongs with the others rather than being a second
        /// definition of "one cell" somewhere in Presentation. An off-board target yields nothing.
        /// </summary>
        public static IReadOnlyList<GridPosition> ForDemolitionHammer(
            BoardShape shape, GridPosition target, List<GridPosition> buffer)
        {
            Prepare(shape, buffer);
            AddIfPlayable(shape, target, buffer);
            return buffer;
        }

        /// <summary>True when <paramref name="index"/> names a row that exists on
        /// <paramref name="shape"/>.</summary>
        public static bool IsValidRowIndex(BoardShape shape, int index)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            return index >= 0 && index < shape.Height;
        }

        /// <summary>True when <paramref name="index"/> names a column that exists on
        /// <paramref name="shape"/>. Separate from <see cref="IsValidRowIndex"/> because a board need
        /// not be square.</summary>
        public static bool IsValidColumnIndex(BoardShape shape, int index)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            return index >= 0 && index < shape.Width;
        }

        /// <summary>Appends <paramref name="cell"/> unless it is off the board or a hole. Every kind
        /// funnels through this, so no aim preview can ever tint a cell the application would refuse.</summary>
        private static void AddIfPlayable(BoardShape shape, GridPosition cell, List<GridPosition> buffer)
        {
            if (!shape.IsPlayable(cell))
            {
                return;
            }

            buffer.Add(cell);
        }

        private static void Prepare(BoardShape shape, List<GridPosition> buffer)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();
        }
    }
}
