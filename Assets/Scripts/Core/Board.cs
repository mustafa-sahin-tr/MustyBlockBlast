using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The 8x8 playing field. Cells hold a colour id; <see cref="EMPTY"/> means unoccupied.
    /// Colour is cosmetic and never affects placement or clearing.
    /// </summary>
    public sealed class Board
    {
        public const int SIZE = 8;
        public const int EMPTY = 0;

        private readonly int[] _cells;

        public Board()
        {
            _cells = new int[SIZE * SIZE];
        }

        private Board(int[] cells)
        {
            _cells = cells;
        }

        public static bool IsInside(GridPosition position)
            => position.X >= 0 && position.X < SIZE && position.Y >= 0 && position.Y < SIZE;

        public bool IsOccupied(GridPosition position) => this[position] != EMPTY;

        public int this[GridPosition position]
        {
            get
            {
                if (!IsInside(position))
                {
                    throw new ArgumentOutOfRangeException(nameof(position), position, "Outside the board.");
                }

                return _cells[Index(position)];
            }
        }

        public void Occupy(GridPosition position, int colourId)
        {
            if (colourId == EMPTY)
            {
                throw new ArgumentException("Use Clear to empty a cell.", nameof(colourId));
            }

            _cells[Index(position)] = colourId;
        }

        public void Clear(GridPosition position) => _cells[Index(position)] = EMPTY;

        public bool IsRowFull(int y)
        {
            for (int x = 0; x < SIZE; x++)
            {
                if (_cells[Index(new GridPosition(x, y))] == EMPTY)
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsColumnFull(int x)
        {
            for (int y = 0; y < SIZE; y++)
            {
                if (_cells[Index(new GridPosition(x, y))] == EMPTY)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Deep copy, used for undo snapshots.</summary>
        public Board Clone()
        {
            int[] copy = new int[_cells.Length];
            Array.Copy(_cells, copy, _cells.Length);
            return new Board(copy);
        }

        /// <summary>Overwrites this board's cells with <paramref name="source"/>'s. Used to reuse a scratch
        /// board across preview queries without allocating a new board each call.</summary>
        public void CopyFrom(Board source)
        {
            Array.Copy(source._cells, _cells, _cells.Length);
        }

        private static int Index(GridPosition position)
        {
            if (!IsInside(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, "Outside the board.");
            }

            return (position.Y * SIZE) + position.X;
        }
    }
}
