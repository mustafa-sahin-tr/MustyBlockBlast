using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The playable outline of a board: its bounding rectangle plus the cells inside that rectangle
    /// which are permanently unplayable ("holes"). Immutable and shared — one instance describes the
    /// shape every board of that level uses, so a scratch board and the real board are guaranteed to
    /// agree on geometry by pointing at the same object rather than by copying numbers around.
    /// <para>
    /// Deliberately separate from <see cref="Board"/>: occupancy changes every placement, shape never
    /// changes within a level. Keeping them apart is what lets <see cref="Board.Clone"/> and
    /// <see cref="Board.CopyFrom"/> copy a board's contents without copying — or risking disagreeing
    /// about — its outline.
    /// </para>
    /// <para>
    /// A hole is modelled as a per-cell flag in a flat array, matching the
    /// <see cref="SpecialCellKind"/> array <see cref="Board"/> already keeps: the two are orthogonal
    /// per-cell facts, and a flat array indexed the same way as the colour cells keeps every lookup a
    /// single bounds-checked read rather than a hash. The per-row/per-column playable counts are
    /// precomputed here for the same reason — "is this row full" runs on every preview frame and must
    /// not re-derive the row's length each time.
    /// </para>
    /// </summary>
    public sealed class BoardShape
    {
        /// <summary>The shape every level authored before board shapes existed uses: a full
        /// <see cref="Board.SIZE"/> x <see cref="Board.SIZE"/> square with no holes. Referenced by the
        /// parameterless <see cref="Board"/> constructor, so an unconfigured board is bit-for-bit the
        /// board this project has always had.</summary>
        public static readonly BoardShape Standard = new BoardShape(Board.SIZE, Board.SIZE, null);

        private readonly bool[] _holes;
        private readonly int[] _playableInRow;
        private readonly int[] _playableInColumn;

        /// <summary>
        /// Builds a shape <paramref name="width"/> x <paramref name="height"/> with the cells in
        /// <paramref name="holes"/> marked unplayable. A null or empty hole list yields a plain
        /// rectangle. Hole entries outside the rectangle are rejected rather than ignored: a level
        /// author who mistypes a coordinate must hear about it at load time, not discover it as a hole
        /// that silently is not there.
        /// </summary>
        public BoardShape(int width, int height, IReadOnlyList<GridPosition> holes)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "A board needs at least one column.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "A board needs at least one row.");
            }

            Width = width;
            Height = height;

            _holes = new bool[width * height];
            _playableInRow = new int[height];
            _playableInColumn = new int[width];

            if (holes != null)
            {
                for (int i = 0; i < holes.Count; i++)
                {
                    GridPosition hole = holes[i];
                    if (!IsInside(hole))
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(holes), hole, "Hole cell is outside the board's bounding rectangle.");
                    }

                    _holes[(hole.Y * width) + hole.X] = true;
                }
            }

            int playableCellCount = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (_holes[(y * width) + x])
                    {
                        continue;
                    }

                    playableCellCount++;
                    _playableInRow[y]++;
                    _playableInColumn[x]++;
                }
            }

            PlayableCellCount = playableCellCount;
            HasHoles = playableCellCount != _holes.Length;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Cells in the bounding rectangle, holes included — the length every per-cell buffer
        /// indexed by <see cref="Index"/> must have.</summary>
        public int CellCount => _holes.Length;

        /// <summary>Cells a piece could ever occupy. Equal to <see cref="CellCount"/> on a hole-free
        /// shape, which is what makes an occupancy ratio measured against it unchanged on the standard
        /// board.</summary>
        public int PlayableCellCount { get; }

        /// <summary>True when at least one cell of the bounding rectangle is a hole. Lets a caller skip
        /// hole handling entirely on the standard shape.</summary>
        public bool HasHoles { get; }

        public bool IsInside(GridPosition position)
            => position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height;

        /// <summary>True when <paramref name="position"/> is inside the bounding rectangle but
        /// permanently unplayable. False for anything outside it — off-board is not a hole, and callers
        /// that care about both ask <see cref="IsPlayable"/> instead.</summary>
        public bool IsHole(GridPosition position)
            => IsInside(position) && _holes[(position.Y * Width) + position.X];

        /// <summary>True when a piece may occupy <paramref name="position"/>: inside the bounding
        /// rectangle and not a hole. The single predicate every placement, clear and preview path
        /// asks — "is this cell part of the playable shape".</summary>
        public bool IsPlayable(GridPosition position)
            => IsInside(position) && !_holes[(position.Y * Width) + position.X];

        /// <summary>Playable cells in row <paramref name="y"/>. Zero for a row made entirely of holes,
        /// which is what stops such a row ever being reported full.</summary>
        internal int PlayableCountInRow(int y) => _playableInRow[y];

        /// <summary>Playable cells in column <paramref name="x"/>.</summary>
        internal int PlayableCountInColumn(int x) => _playableInColumn[x];

        /// <summary>Flat index of <paramref name="position"/> in a <see cref="CellCount"/>-long buffer.
        /// Unchecked — callers inside <see cref="Board"/> have already validated the position.</summary>
        internal int Index(GridPosition position) => (position.Y * Width) + position.X;
    }
}
