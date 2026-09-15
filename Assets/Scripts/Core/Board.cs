using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The 8x8 playing field. Cells hold a colour id; <see cref="EMPTY"/> means unoccupied.
    /// Colour is cosmetic and never affects placement or clearing.
    /// <para>
    /// Each cell additionally carries a <see cref="SpecialCellKind"/> — metadata about what happens
    /// when that cell is destroyed. It is orthogonal to occupancy: no occupancy, fullness or
    /// flood-fill query on this class reads it.
    /// </para>
    /// </summary>
    public sealed class Board
    {
        public const int SIZE = 8;
        public const int EMPTY = 0;

        /// <summary>Width/height of the centered "core" region <see cref="IsCenterCoreEmpty"/> checks.</summary>
        private const int CENTER_CORE_SIZE = 4;

        private readonly int[] _cells;

        /// <summary>Per-cell special kind, indexed exactly like <see cref="_cells"/>. Deliberately a
        /// second array rather than extra bits packed into the colour id: the two are independent
        /// (a cell can be empty, occupied, or occupied-and-special) and every existing read of
        /// <see cref="_cells"/> must keep meaning "is this cell occupied, and in which colour".</summary>
        private readonly SpecialCellKind[] _specialKinds;

        public Board()
        {
            _cells = new int[SIZE * SIZE];
            _specialKinds = new SpecialCellKind[SIZE * SIZE];
        }

        private Board(int[] cells, SpecialCellKind[] specialKinds)
        {
            _cells = cells;
            _specialKinds = specialKinds;
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

        /// <summary>Empties a cell. Also resets its <see cref="SpecialCellKind"/>: the special
        /// behaviour belonged to the block that was standing there, so leaving it behind would hand it
        /// to whatever piece happens to land on the cell next. Any caller that needs to know what was
        /// destroyed must read the kind <em>before</em> clearing — see
        /// <see cref="SpecialCellDetection.CollectTriggered"/>.</summary>
        public void Clear(GridPosition position)
        {
            int index = Index(position);
            _cells[index] = EMPTY;
            _specialKinds[index] = SpecialCellKind.None;
        }

        /// <summary>The special behaviour the block on <paramref name="position"/> carries.
        /// <see cref="SpecialCellKind.None"/> for an ordinary or empty cell.</summary>
        public SpecialCellKind GetSpecialKind(GridPosition position) => _specialKinds[Index(position)];

        /// <summary>Tags <paramref name="position"/> with a special behaviour. Independent of
        /// <see cref="Occupy"/>, which deliberately leaves the kind alone so a spawner can occupy a
        /// cell and then tag it in two steps; the tag is reset only by <see cref="Clear"/>.</summary>
        public void SetSpecialKind(GridPosition position, SpecialCellKind kind)
            => _specialKinds[Index(position)] = kind;

        /// <summary>Appends every cell of row <paramref name="y"/> to <paramref name="results"/>
        /// (which is not cleared first). Exists so callers that need the cells of a line — rather than
        /// just its index — go through the board instead of re-deriving the geometry from
        /// <see cref="SIZE"/> themselves, which is what a non-rectangular board would have to change.</summary>
        public void CollectRowCells(int y, List<GridPosition> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            for (int x = 0; x < SIZE; x++)
            {
                results.Add(new GridPosition(x, y));
            }
        }

        /// <summary>Appends every cell of column <paramref name="x"/> to <paramref name="results"/>.
        /// Counterpart to <see cref="CollectRowCells"/>, and shape-agnostic for the same reason.</summary>
        public void CollectColumnCells(int x, List<GridPosition> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            for (int y = 0; y < SIZE; y++)
            {
                results.Add(new GridPosition(x, y));
            }
        }

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

        /// <summary>True when every cell of row <paramref name="y"/> is <see cref="EMPTY"/> — used to
        /// detect a line a power-up's clear happened to empty out, as distinct from a normal
        /// line-clear (which fires on the opposite condition, the row becoming full).</summary>
        public bool IsRowEmpty(int y)
        {
            for (int x = 0; x < SIZE; x++)
            {
                if (_cells[Index(new GridPosition(x, y))] != EMPTY)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when every cell of column <paramref name="x"/> is <see cref="EMPTY"/>.</summary>
        public bool IsColumnEmpty(int x)
        {
            for (int y = 0; y < SIZE; y++)
            {
                if (_cells[Index(new GridPosition(x, y))] != EMPTY)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when every cell on the board is <see cref="EMPTY"/> — a "perfect clear".</summary>
        public bool IsEmpty()
        {
            for (int i = 0; i < SIZE * SIZE; i++)
            {
                if (_cells[i] != EMPTY)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>How many cells are currently occupied. A once-per-placement O(64) scan, not a
        /// hot-path call — cheap enough to run once per placement, never per frame.</summary>
        public int OccupiedCellCount()
        {
            int count = 0;
            for (int i = 0; i < SIZE * SIZE; i++)
            {
                if (_cells[i] != EMPTY)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>True when the board's centered 4x4 "core" is completely empty — a topology goal
        /// distinct from <see cref="IsEmpty"/> (whole board) or any single row/column.</summary>
        public bool IsCenterCoreEmpty()
        {
            int min = (SIZE - CENTER_CORE_SIZE) / 2;
            int max = min + CENTER_CORE_SIZE - 1;

            for (int y = min; y <= max; y++)
            {
                for (int x = min; x <= max; x++)
                {
                    if (_cells[Index(new GridPosition(x, y))] != EMPTY)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// True when at least one empty cell cannot be reached from the board edge through a path of
        /// empty cells (4-directional) — an "isolated hole" trapped behind occupied cells. Flood-fills
        /// from every empty border cell; O(64) with two small scratch arrays, cheap enough to run once
        /// per placement (not a per-frame concern, so this is not held to the Update-path zero-alloc rule).
        /// </summary>
        public bool HasIsolatedEmptyCells()
        {
            var reachable = new bool[SIZE * SIZE];
            var stack = new int[SIZE * SIZE];
            int stackCount = 0;

            for (int x = 0; x < SIZE; x++)
            {
                stackCount = SeedIfEmpty(x, 0, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(x, SIZE - 1, reachable, stack, stackCount);
            }

            for (int y = 1; y < SIZE - 1; y++)
            {
                stackCount = SeedIfEmpty(0, y, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(SIZE - 1, y, reachable, stack, stackCount);
            }

            while (stackCount > 0)
            {
                stackCount--;
                int index = stack[stackCount];
                int x = index % SIZE;
                int y = index / SIZE;

                stackCount = SeedIfEmpty(x - 1, y, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(x + 1, y, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(x, y - 1, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(x, y + 1, reachable, stack, stackCount);
            }

            for (int i = 0; i < SIZE * SIZE; i++)
            {
                if (_cells[i] == EMPTY && !reachable[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Size of the largest connected component of empty cells (4-directional) — "how much room is
        /// left in one piece", as distinct from <see cref="HasIsolatedEmptyCells"/>'s "is every empty
        /// cell reachable from the edge". Both flood-fill with the same explicit int-array stack (never
        /// recursion, which an 8x8 fill would drive 64 frames deep), but they seed differently: this one
        /// treats <em>every</em> unvisited empty cell as the root of a new component and keeps the
        /// largest count found, rather than seeding only from the border and asking a yes/no question.
        /// <para>
        /// Zero on a board with no empty cell at all.
        /// </para>
        /// <para>
        /// The two scratch arrays are supplied by the caller rather than allocated here: the Ghost Fit
        /// search calls this once per legal candidate placement (up to three dock pieces x 64 anchors),
        /// so a per-call allocation would turn one tap into a burst of garbage. Both must be at least
        /// <see cref="SIZE"/> x <see cref="SIZE"/> long; their contents on entry are irrelevant.
        /// </para>
        /// </summary>
        public int LargestEmptyRegionSize(bool[] visitedBuffer, int[] stackBuffer)
        {
            if (visitedBuffer == null)
            {
                throw new ArgumentNullException(nameof(visitedBuffer));
            }

            if (stackBuffer == null)
            {
                throw new ArgumentNullException(nameof(stackBuffer));
            }

            // Named separately so the exception points at the buffer that is actually too short: a
            // caller that sized one of the two wrong is told which one.
            if (visitedBuffer.Length < SIZE * SIZE)
            {
                throw new ArgumentException(
                    $"Scratch buffers must hold at least {SIZE * SIZE} entries.", nameof(visitedBuffer));
            }

            if (stackBuffer.Length < SIZE * SIZE)
            {
                throw new ArgumentException(
                    $"Scratch buffers must hold at least {SIZE * SIZE} entries.", nameof(stackBuffer));
            }

            Array.Clear(visitedBuffer, 0, SIZE * SIZE);

            int largest = 0;

            for (int rootIndex = 0; rootIndex < SIZE * SIZE; rootIndex++)
            {
                if (_cells[rootIndex] != EMPTY || visitedBuffer[rootIndex])
                {
                    continue;
                }

                int stackCount = SeedIfEmpty(rootIndex % SIZE, rootIndex / SIZE, visitedBuffer, stackBuffer, 0);
                int componentSize = 0;

                while (stackCount > 0)
                {
                    stackCount--;
                    int index = stackBuffer[stackCount];
                    componentSize++;

                    int x = index % SIZE;
                    int y = index / SIZE;

                    stackCount = SeedIfEmpty(x - 1, y, visitedBuffer, stackBuffer, stackCount);
                    stackCount = SeedIfEmpty(x + 1, y, visitedBuffer, stackBuffer, stackCount);
                    stackCount = SeedIfEmpty(x, y - 1, visitedBuffer, stackBuffer, stackCount);
                    stackCount = SeedIfEmpty(x, y + 1, visitedBuffer, stackBuffer, stackCount);
                }

                if (componentSize > largest)
                {
                    largest = componentSize;
                }
            }

            return largest;
        }

        /// <summary>Marks (x, y) visited and pushes it onto the flood-fill stack, when it is in-bounds,
        /// empty, and not already marked. Returns the updated stack count so callers can chain calls
        /// without a <c>ref</c> parameter. Shared by both flood-fills above, which differ only in how
        /// they seed and what they count — never in what "an empty neighbour" means.</summary>
        private int SeedIfEmpty(int x, int y, bool[] visited, int[] stack, int stackCount)
        {
            if (x < 0 || x >= SIZE || y < 0 || y >= SIZE)
            {
                return stackCount;
            }

            int index = (y * SIZE) + x;
            if (_cells[index] != EMPTY || visited[index])
            {
                return stackCount;
            }

            visited[index] = true;
            stack[stackCount] = index;
            return stackCount + 1;
        }

        /// <summary>Deep copy, used for undo snapshots. Copies the special-cell kinds as well as the
        /// colours — a snapshot that restored the colours but not the kinds would silently strip every
        /// special block on the board.</summary>
        public Board Clone()
        {
            int[] copy = new int[_cells.Length];
            Array.Copy(_cells, copy, _cells.Length);

            var specialCopy = new SpecialCellKind[_specialKinds.Length];
            Array.Copy(_specialKinds, specialCopy, _specialKinds.Length);

            return new Board(copy, specialCopy);
        }

        /// <summary>Overwrites this board's cells with <paramref name="source"/>'s. Used to reuse a scratch
        /// board across preview queries without allocating a new board each call. Copies the
        /// special-cell kinds too, for the same reason <see cref="Clone"/> does.</summary>
        public void CopyFrom(Board source)
        {
            Array.Copy(source._cells, _cells, _cells.Length);
            Array.Copy(source._specialKinds, _specialKinds, _specialKinds.Length);
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
