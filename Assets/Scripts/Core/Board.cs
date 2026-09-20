using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The playing field. Cells hold a colour id; <see cref="EMPTY"/> means unoccupied.
    /// Colour is cosmetic and never affects placement or clearing.
    /// <para>
    /// The field's outline — width, height and which cells are permanently unplayable — comes from a
    /// <see cref="BoardShape"/>. A board built with no shape uses <see cref="BoardShape.Standard"/>,
    /// the <see cref="SIZE"/> x <see cref="SIZE"/> hole-free square this project has always had, so an
    /// unconfigured board behaves exactly as it did before shapes existed.
    /// </para>
    /// <para>
    /// Each cell additionally carries a <see cref="SpecialCellKind"/> — metadata about what happens
    /// when that cell is destroyed. It is orthogonal to occupancy: no occupancy, fullness or
    /// flood-fill query on this class reads it.
    /// </para>
    /// <para>
    /// Each cell also carries a <see cref="CellSkinKind"/> — a purely cosmetic decorative tag,
    /// orthogonal to occupancy, colour AND <see cref="SpecialCellKind"/> alike: no rule on this class,
    /// no clear-detection, no scoring and no special-cell effect reads it. See
    /// <see cref="CellSkinKind"/> for its full contract.
    /// </para>
    /// </summary>
    public sealed class Board
    {
        /// <summary>
        /// The nominal board size: the width and height of <see cref="BoardShape.Standard"/>.
        /// <para>
        /// Deliberately kept, and deliberately <em>not</em> the source of any board's actual
        /// dimensions — those come from <see cref="Width"/>/<see cref="Height"/>. It survives as the
        /// default a shape is built from and as the nominal figure things unrelated to real board
        /// geometry size themselves against (see <c>PowerUpTargetCells.MAX_TARGET_CELLS</c>).
        /// </para>
        /// </summary>
        public const int SIZE = 8;

        public const int EMPTY = 0;

        /// <summary>
        /// How many distinct piece colour ids exist: a cell holds <see cref="EMPTY"/> or a value in
        /// 1..COLOUR_COUNT. The single source of truth for the palette size — the piece draw ranges
        /// over it, every theme authors exactly this many fills, and a colour-count objective is
        /// validated against it — so the three can never disagree (issue #147).
        /// </summary>
        public const int COLOUR_COUNT = 5;

        /// <summary>Width/height of the centered "core" region <see cref="IsCenterCoreEmpty"/> checks.</summary>
        private const int CENTER_CORE_SIZE = 4;

        private readonly BoardShape _shape;

        private readonly int[] _cells;

        /// <summary>Per-cell special kind, indexed exactly like <see cref="_cells"/>. Deliberately a
        /// second array rather than extra bits packed into the colour id: the two are independent
        /// (a cell can be empty, occupied, or occupied-and-special) and every existing read of
        /// <see cref="_cells"/> must keep meaning "is this cell occupied, and in which colour".</summary>
        private readonly SpecialCellKind[] _specialKinds;

        /// <summary>Per-cell decorative skin, indexed exactly like <see cref="_cells"/> and stored the
        /// same way <see cref="_specialKinds"/> is — a fourth (fifth, counting hit counts) independent
        /// parallel array rather than bits on any other one, because a skin is orthogonal to occupancy,
        /// colour AND special kind alike (issue #324). See <see cref="CellSkinKind"/>.</summary>
        private readonly CellSkinKind[] _cellSkins;

        /// <summary>
        /// Per-cell hits still to absorb before the cell can be destroyed, indexed exactly like
        /// <see cref="_cells"/>. <c>0</c> — every cell of every board authored before reinforced cells
        /// existed, and every cell an ordinary placement fills — means "not reinforced": such a cell is
        /// removed by the first <see cref="TryDamage"/> that reaches it, exactly as
        /// <see cref="Clear"/> always removed it.
        /// <para>
        /// A third parallel array rather than bits packed into the colour id or a
        /// <see cref="SpecialCellKind"/> value, for the reason <see cref="_specialKinds"/> is a second
        /// one: the three are independent, and every existing read of <see cref="_cells"/> must keep
        /// meaning "is this cell occupied, and in which colour".
        /// </para>
        /// <para>
        /// <b>Undo (issue #153 AC8) is deliberately not implemented</b> — one-step undo does not exist
        /// anywhere in this project yet, so there is nothing to integrate with. The storage is kept as a
        /// plain parallel array copied by <see cref="Clone"/>/<see cref="CopyFrom"/> exactly as the
        /// special kinds are, so an undo built on those two methods will restore hit counts with no
        /// change here.
        /// </para>
        /// </summary>
        private readonly int[] _hitCounts;

        /// <summary>
        /// Placements still to elapse before a <see cref="SpecialCellKind.Timer"/> cell converts to an
        /// ordinary one, indexed exactly like <see cref="_cells"/>. <c>0</c> — every cell of every board
        /// authored before Timer cells existed, and every cell whose kind is not
        /// <see cref="SpecialCellKind.Timer"/> — means "not a timer cell", exactly as a 0 hit count
        /// means "not reinforced".
        /// <para>
        /// A fourth parallel array, deliberately not <see cref="_hitCounts"/>: that array means "hits
        /// still to absorb", a per-<em>touch</em> quantity, whereas this one means "placements
        /// remaining", a global per-<em>placement</em> quantity that decrements whether or not the cell
        /// was touched at all (issue #307 AC2/AC9). Reusing the same storage for two different meanings
        /// would collide the moment a level ever authored both mechanics on cells that happen to share
        /// an index history.
        /// </para>
        /// <para>
        /// Copied by <see cref="Clone"/>/<see cref="CopyFrom"/> exactly as <see cref="_specialKinds"/>
        /// and <see cref="_hitCounts"/> are, so Undo's full-snapshot restore rewinds a Timer cell's
        /// countdown to its pre-placement value along with everything else (issue #307 AC1/AC10).
        /// </para>
        /// </summary>
        private readonly int[] _timerCountdowns;

        /// <summary>The standard <see cref="SIZE"/> x <see cref="SIZE"/> hole-free board. Delegates to
        /// <see cref="BoardShape.Standard"/> so every level authored before board shapes existed keeps
        /// the exact geometry it was authored against.</summary>
        public Board()
            : this(BoardShape.Standard)
        {
        }

        public Board(BoardShape shape)
        {
            _shape = shape ?? throw new ArgumentNullException(nameof(shape));
            _cells = new int[shape.CellCount];
            _specialKinds = new SpecialCellKind[shape.CellCount];
            _cellSkins = new CellSkinKind[shape.CellCount];
            _hitCounts = new int[shape.CellCount];
            _timerCountdowns = new int[shape.CellCount];
        }

        private Board(
            BoardShape shape, int[] cells, SpecialCellKind[] specialKinds, CellSkinKind[] cellSkins,
            int[] hitCounts, int[] timerCountdowns)
        {
            _shape = shape;
            _cells = cells;
            _specialKinds = specialKinds;
            _cellSkins = cellSkins;
            _hitCounts = hitCounts;
            _timerCountdowns = timerCountdowns;
        }

        /// <summary>The outline this board was built with. Shared, immutable and safe to hand out — a
        /// scratch board built from it is guaranteed to agree with this one about geometry.</summary>
        public BoardShape Shape => _shape;

        public int Width => _shape.Width;

        public int Height => _shape.Height;

        /// <summary>Cells in the bounding rectangle, holes included — the length a per-cell scratch
        /// buffer indexed by <see cref="Index"/> must have.</summary>
        public int CellCount => _shape.CellCount;

        /// <summary>Cells a piece could ever occupy. Equal to <see cref="CellCount"/> on a hole-free
        /// board.</summary>
        public int PlayableCellCount => _shape.PlayableCellCount;

        public bool IsInside(GridPosition position) => _shape.IsInside(position);

        /// <summary>Playable cells in row <paramref name="y"/> — the number of filled cells it takes to
        /// complete that row. Zero for a row made entirely of holes.</summary>
        internal int PlayableCountInRow(int y) => _shape.PlayableCountInRow(y);

        /// <summary>Playable cells in column <paramref name="x"/>.</summary>
        internal int PlayableCountInColumn(int x) => _shape.PlayableCountInColumn(x);

        /// <summary>True when <paramref name="position"/> is inside the board but permanently
        /// unplayable. Off-board is not a hole.</summary>
        public bool IsHole(GridPosition position) => _shape.IsHole(position);

        /// <summary>True when a piece may occupy <paramref name="position"/>: inside the board and not
        /// a hole. The predicate <see cref="PlacementRules.CanPlace"/> and every preview path asks.</summary>
        public bool IsPlayable(GridPosition position) => _shape.IsPlayable(position);

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

            if (_shape.IsHole(position))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position), position, "Cell is a hole and can never be occupied.");
            }

            _cells[Index(position)] = colourId;
        }

        /// <summary>Empties a cell, whatever is on it. Also resets its <see cref="SpecialCellKind"/> and
        /// its hit count: all three belonged to the block that was standing there, so leaving either
        /// behind would hand it to whatever piece happens to land on the cell next. Any caller that
        /// needs to know what was destroyed must read the kind <em>before</em> clearing — see
        /// <see cref="SpecialCellDetection.CollectTriggered"/>.
        /// <para>
        /// Unconditional removal: it spends no hit and asks for none, so a reinforced cell is wiped by
        /// it as readily as an ordinary one. Every path that destroys a cell as the <em>consequence of
        /// a clear</em> must go through <see cref="TryDamage"/> instead; this stays the force-clear
        /// primitive that <see cref="TryDamage"/> itself and a whole-board reset are built on.
        /// </para>
        /// </summary>
        public void Clear(GridPosition position)
        {
            int index = Index(position);
            _cells[index] = EMPTY;
            _specialKinds[index] = SpecialCellKind.None;
            _cellSkins[index] = CellSkinKind.None;
            _hitCounts[index] = 0;
            _timerCountdowns[index] = 0;
        }

        /// <summary>
        /// Removes <paramref name="position"/> if it has no hits left to absorb, or spends exactly one
        /// hit and leaves it occupied otherwise. Returns whether the cell was actually removed — the
        /// caller uses this to decide whether the cell counts towards a clear's score/count and whether
        /// it may trigger a <see cref="SpecialCellKind"/> effect, neither of which a surviving hit may
        /// do.
        /// <para>
        /// <b>The removal entry point every clearing path uses.</b> An ordinary occupied cell has a hit
        /// count of 0 and so takes the removal branch on the very first call — behaviourally identical
        /// to the unconditional <see cref="Clear"/> this replaced at those call sites, which is what
        /// keeps every mechanic that does not involve a reinforced cell exactly as it was.
        /// </para>
        /// </summary>
        public bool TryDamage(GridPosition position)
        {
            int index = Index(position);

            if (_hitCounts[index] <= 1)
            {
                Clear(position);
                return true;
            }

            _hitCounts[index]--;
            return false;
        }

        /// <summary>Hits <paramref name="position"/> must still absorb before it can be destroyed. 0 for
        /// an ordinary or empty cell, which is every cell of a board nothing reinforced.</summary>
        public int GetHitCount(GridPosition position) => _hitCounts[Index(position)];

        /// <summary>
        /// Occupies an empty cell as a reinforced one, for level-start authoring only. Ordinary piece
        /// placement (<see cref="Occupy"/>) never sets a hit count — only this does, because a
        /// reinforced cell is pre-filled by the level rather than put down by the player.
        /// <para>
        /// Reuses <see cref="Occupy"/>, so a hole and the <see cref="EMPTY"/> colour are refused here
        /// exactly as they are for an ordinary block.
        /// </para>
        /// </summary>
        public void OccupyReinforced(GridPosition position, int colourId, int hitCount)
        {
            Occupy(position, colourId);
            _hitCounts[Index(position)] = hitCount;
        }

        /// <summary>The special behaviour the block on <paramref name="position"/> carries.
        /// <see cref="SpecialCellKind.None"/> for an ordinary or empty cell.</summary>
        public SpecialCellKind GetSpecialKind(GridPosition position) => _specialKinds[Index(position)];

        /// <summary>Tags <paramref name="position"/> with a special behaviour. Independent of
        /// <see cref="Occupy"/>, which deliberately leaves the kind alone so a spawner can occupy a
        /// cell and then tag it in two steps; the tag is reset only by <see cref="Clear"/>.</summary>
        public void SetSpecialKind(GridPosition position, SpecialCellKind kind)
            => _specialKinds[Index(position)] = kind;

        /// <summary>The decorative skin the block on <paramref name="position"/> carries.
        /// <see cref="CellSkinKind.None"/> for an ordinary or empty cell, and for every cell of every
        /// board built before this feature existed. See <see cref="CellSkinKind"/>.</summary>
        public CellSkinKind GetCellSkin(GridPosition position) => _cellSkins[Index(position)];

        /// <summary>Tags <paramref name="position"/> with a decorative skin. Independent of
        /// <see cref="Occupy"/> and of <see cref="SetSpecialKind"/> exactly as they are independent of
        /// each other — a cell can be occupied, tagged special, skinned, any combination — and is reset
        /// only by <see cref="Clear"/>.</summary>
        public void SetCellSkin(GridPosition position, CellSkinKind kind)
            => _cellSkins[Index(position)] = kind;

        /// <summary>Placements still to elapse before <paramref name="position"/> converts from
        /// <see cref="SpecialCellKind.Timer"/> to an ordinary cell. 0 for any cell that is not a timer
        /// cell, which is every cell of a board nothing timed.</summary>
        public int GetTimerCountdown(GridPosition position) => _timerCountdowns[Index(position)];

        /// <summary>
        /// Overwrites <paramref name="position"/>'s remaining countdown. Used by
        /// <see cref="TimerCellTick"/> to decrement it once per placement and to zero it on expiry, and
        /// by <see cref="VortexEffect"/> to carry a timer cell's countdown along with it when a pull
        /// relocates the block rather than destroying it.
        /// </summary>
        public void SetTimerCountdown(GridPosition position, int countdown)
            => _timerCountdowns[Index(position)] = countdown;

        /// <summary>
        /// Occupies an empty cell as a <see cref="SpecialCellKind.Timer"/> cell with
        /// <paramref name="startingCountdown"/> placements to live, for level-start authoring only —
        /// the mechanic is level-authored exclusively (issue #307 AC7/AC8), so nothing else ever calls
        /// this. Unlike <see cref="OccupyReinforced"/>, which layers only a hit count onto an ordinary
        /// occupy because "reinforced" carries no <see cref="SpecialCellKind"/> of its own, a timer cell
        /// IS a kind — so this sets the kind and the countdown together, in the one call a seeder needs.
        /// </summary>
        public void OccupyTimer(GridPosition position, int colourId, int startingCountdown)
        {
            Occupy(position, colourId);
            SetSpecialKind(position, SpecialCellKind.Timer);
            _timerCountdowns[Index(position)] = startingCountdown;
        }

        /// <summary>Appends every <em>playable</em> cell of row <paramref name="y"/> to
        /// <paramref name="results"/> (which is not cleared first). Holes are skipped: nothing can ever
        /// stand on one, so a caller walking a line to clear or inspect it must never be handed one.
        /// Exists so callers that need the cells of a line — rather than just its index — go through
        /// the board instead of re-deriving the geometry themselves.</summary>
        public void CollectRowCells(int y, List<GridPosition> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            for (int x = 0; x < Width; x++)
            {
                var position = new GridPosition(x, y);
                if (_shape.IsHole(position))
                {
                    continue;
                }

                results.Add(position);
            }
        }

        /// <summary>Appends every playable cell of column <paramref name="x"/> to
        /// <paramref name="results"/>. Counterpart to <see cref="CollectRowCells"/>, holes skipped for
        /// the same reason.</summary>
        public void CollectColumnCells(int x, List<GridPosition> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            for (int y = 0; y < Height; y++)
            {
                var position = new GridPosition(x, y);
                if (_shape.IsHole(position))
                {
                    continue;
                }

                results.Add(position);
            }
        }

        /// <summary>
        /// True when every playable cell of row <paramref name="y"/> is filled — the line-clear rule.
        /// <para>
        /// Fullness is measured against the row's playable cells, not its width, so a row shortened by
        /// holes clears with fewer filled cells than a full-width one. A row with no playable cells at
        /// all is never full: nothing could ever complete it, so reporting it full would clear it on
        /// every single pass forever.
        /// </para>
        /// </summary>
        public bool IsRowFull(int y)
        {
            if (_shape.PlayableCountInRow(y) == 0)
            {
                return false;
            }

            for (int x = 0; x < Width; x++)
            {
                var position = new GridPosition(x, y);
                if (_shape.IsHole(position))
                {
                    continue;
                }

                if (_cells[Index(position)] == EMPTY)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when row <paramref name="y"/> is missing exactly one occupied playable cell — one
        /// placement short of <see cref="IsRowFull"/>.
        /// <para>
        /// Holes are skipped exactly as <see cref="IsRowFull"/> skips them, so a row shortened by holes
        /// is judged against its own playable count, not the board's width. A row with no playable
        /// cells at all is never reported, for the same reason <see cref="IsRowFull"/> never reports
        /// one: nothing could ever complete it.
        /// </para>
        /// </summary>
        public bool IsRowOneCellFromFull(int y)
        {
            if (_shape.PlayableCountInRow(y) == 0)
            {
                return false;
            }

            int missingCount = 0;
            for (int x = 0; x < Width; x++)
            {
                var position = new GridPosition(x, y);
                if (_shape.IsHole(position))
                {
                    continue;
                }

                if (_cells[Index(position)] == EMPTY)
                {
                    missingCount++;
                    if (missingCount > 1)
                    {
                        return false;
                    }
                }
            }

            return missingCount == 1;
        }

        /// <summary>True when column <paramref name="x"/> is missing exactly one occupied playable
        /// cell. Same rule as <see cref="IsRowOneCellFromFull"/>, including the all-holes and
        /// no-playable-cells cases.</summary>
        public bool IsColumnOneCellFromFull(int x)
        {
            if (_shape.PlayableCountInColumn(x) == 0)
            {
                return false;
            }

            int missingCount = 0;
            for (int y = 0; y < Height; y++)
            {
                var position = new GridPosition(x, y);
                if (_shape.IsHole(position))
                {
                    continue;
                }

                if (_cells[Index(position)] == EMPTY)
                {
                    missingCount++;
                    if (missingCount > 1)
                    {
                        return false;
                    }
                }
            }

            return missingCount == 1;
        }

        /// <summary>True when every playable cell of column <paramref name="x"/> is filled. Same rule
        /// as <see cref="IsRowFull"/>, including the all-holes case.</summary>
        public bool IsColumnFull(int x)
        {
            if (_shape.PlayableCountInColumn(x) == 0)
            {
                return false;
            }

            for (int y = 0; y < Height; y++)
            {
                var position = new GridPosition(x, y);
                if (_shape.IsHole(position))
                {
                    continue;
                }

                if (_cells[Index(position)] == EMPTY)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when every cell of row <paramref name="y"/> is <see cref="EMPTY"/> — used to
        /// detect a line a power-up's clear happened to empty out, as distinct from a normal
        /// line-clear (which fires on the opposite condition, the row becoming full). Holes are always
        /// empty and so never keep a row from qualifying.</summary>
        public bool IsRowEmpty(int y)
        {
            for (int x = 0; x < Width; x++)
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
            for (int y = 0; y < Height; y++)
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
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != EMPTY)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>How many cells are currently occupied. A once-per-placement scan of the whole
        /// board, not a hot-path call — cheap enough to run once per placement, never per frame.</summary>
        public int OccupiedCellCount()
        {
            int count = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != EMPTY)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>True when the board's centered 4x4 "core" is completely empty — a topology goal
        /// distinct from <see cref="IsEmpty"/> (whole board) or any single row/column. The region is
        /// centred on the board's own dimensions and clamped to them, so a board narrower than the core
        /// simply checks what it has.</summary>
        public bool IsCenterCoreEmpty()
        {
            int minX = Math.Max(0, (Width - CENTER_CORE_SIZE) / 2);
            int maxX = Math.Min(Width - 1, minX + CENTER_CORE_SIZE - 1);
            int minY = Math.Max(0, (Height - CENTER_CORE_SIZE) / 2);
            int maxY = Math.Min(Height - 1, minY + CENTER_CORE_SIZE - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
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
        /// True when at least one empty playable cell cannot be reached from the board edge through a
        /// path of empty playable cells (4-directional) — an "isolated hole" trapped behind occupied
        /// cells. Flood-fills from every empty border cell; a shape hole is a wall, exactly as an
        /// occupied cell is, because neither can ever be moved through. Board-sized with two small
        /// scratch arrays, cheap enough to run once per placement (not a per-frame concern, so this is
        /// not held to the Update-path zero-alloc rule).
        /// </summary>
        public bool HasIsolatedEmptyCells()
        {
            var reachable = new bool[CellCount];
            var stack = new int[CellCount];
            int stackCount = 0;

            for (int x = 0; x < Width; x++)
            {
                stackCount = SeedIfEmpty(x, 0, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(x, Height - 1, reachable, stack, stackCount);
            }

            for (int y = 1; y < Height - 1; y++)
            {
                stackCount = SeedIfEmpty(0, y, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(Width - 1, y, reachable, stack, stackCount);
            }

            while (stackCount > 0)
            {
                stackCount--;
                int index = stack[stackCount];
                int x = index % Width;
                int y = index / Width;

                stackCount = SeedIfEmpty(x - 1, y, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(x + 1, y, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(x, y - 1, reachable, stack, stackCount);
                stackCount = SeedIfEmpty(x, y + 1, reachable, stack, stackCount);
            }

            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != EMPTY || reachable[i])
                {
                    continue;
                }

                // A hole is not an empty cell the player could ever use, so one behind a wall is not a
                // trapped pocket — it is simply not part of the board.
                if (_shape.IsHole(new GridPosition(i % Width, i / Width)))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Size of the largest connected component of empty playable cells (4-directional) — "how much
        /// room is left in one piece", as distinct from <see cref="HasIsolatedEmptyCells"/>'s "is every
        /// empty cell reachable from the edge". Both flood-fill with the same explicit int-array stack
        /// (never recursion, which a board-sized fill would drive dozens of frames deep), but they seed
        /// differently: this one treats <em>every</em> unvisited empty cell as the root of a new
        /// component and keeps the largest count found, rather than seeding only from the border and
        /// asking a yes/no question. Holes are walls to both.
        /// <para>
        /// Zero on a board with no empty playable cell at all.
        /// </para>
        /// <para>
        /// The two scratch arrays are supplied by the caller rather than allocated here: the Ghost Fit
        /// search calls this once per legal candidate placement (up to three dock pieces x every
        /// anchor), so a per-call allocation would turn one tap into a burst of garbage. Both must be
        /// at least <see cref="CellCount"/> long; their contents on entry are irrelevant.
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
            if (visitedBuffer.Length < CellCount)
            {
                throw new ArgumentException(
                    $"Scratch buffers must hold at least {CellCount} entries.", nameof(visitedBuffer));
            }

            if (stackBuffer.Length < CellCount)
            {
                throw new ArgumentException(
                    $"Scratch buffers must hold at least {CellCount} entries.", nameof(stackBuffer));
            }

            Array.Clear(visitedBuffer, 0, CellCount);

            int largest = 0;

            for (int rootIndex = 0; rootIndex < _cells.Length; rootIndex++)
            {
                if (_cells[rootIndex] != EMPTY || visitedBuffer[rootIndex])
                {
                    continue;
                }

                int stackCount = SeedIfEmpty(rootIndex % Width, rootIndex / Width, visitedBuffer, stackBuffer, 0);
                int componentSize = 0;

                while (stackCount > 0)
                {
                    stackCount--;
                    int index = stackBuffer[stackCount];
                    componentSize++;

                    int x = index % Width;
                    int y = index / Width;

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

        /// <summary>
        /// Appends every cell of every fully-enclosed pocket of empty, playable cells (4-directional) to
        /// <paramref name="results"/> (not cleared first — the same append convention as
        /// <see cref="CollectRowCells"/>/<see cref="CollectColumnCells"/>) — the geometry
        /// <see cref="VortexEffect"/> fills in on destruction (issue #349).
        /// <para>
        /// <b>Deliberately not <see cref="HasIsolatedEmptyCells"/>'s border-seeded reachability.</b> That
        /// method treats every empty cell literally sitting on the outer edge as automatically
        /// "reachable", whatever is around it — which is exactly wrong for a pocket that happens to run
        /// along the board's last row or column: cells boxed in on every in-board side still get waved
        /// through as "not isolated" purely for being on the edge (see that method's own remarks). This
        /// method instead partitions every empty playable cell into its connected component — the same
        /// 4-directional flood fill <see cref="LargestEmptyRegionSize"/> walks — and calls every
        /// component except the single largest one an island. That classification does not care whether
        /// a component touches the edge, only whether it is connected to the board's one biggest
        /// remaining open area, which is what actually decides whether a piece can ever reach it again.
        /// </para>
        /// <para>
        /// A board whose empty cells form one connected region — however that region is shaped,
        /// edge-hugging or not — has exactly one component, which is trivially "the largest", so nothing
        /// is appended: an island only exists once the empty space has genuinely split into pieces.
        /// Likewise a board with no empty cells at all appends nothing, there being no component to
        /// compare.
        /// </para>
        /// <para>
        /// Ties for largest are broken by scan order (row-major, first found wins) — arbitrary but
        /// fixed, so the same board always resolves the same way. Holes are walls to the fill exactly as
        /// an occupied cell is, for the same reason <see cref="LargestEmptyRegionSize"/> treats them so:
        /// neither can ever hold a block.
        /// </para>
        /// <para>
        /// Allocates two board-sized scratch arrays and a handful of small lists — cheap enough to run
        /// once per vortex trigger (not a per-frame concern, so this is not held to the Update-path
        /// zero-alloc rule, exactly as <see cref="HasIsolatedEmptyCells"/> is not).
        /// </para>
        /// </summary>
        public void CollectEnclosedEmptyIslands(List<GridPosition> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            var visited = new bool[CellCount];
            var stack = new int[CellCount];
            var components = new List<List<GridPosition>>();

            for (int rootIndex = 0; rootIndex < _cells.Length; rootIndex++)
            {
                if (_cells[rootIndex] != EMPTY || visited[rootIndex])
                {
                    continue;
                }

                int stackCount = SeedIfEmpty(rootIndex % Width, rootIndex / Width, visited, stack, 0);
                if (stackCount == 0)
                {
                    // SeedIfEmpty refused the root itself — it is a hole, not a playable empty cell — so
                    // there is no component here to walk.
                    continue;
                }

                var component = new List<GridPosition>();
                while (stackCount > 0)
                {
                    stackCount--;
                    int index = stack[stackCount];
                    int x = index % Width;
                    int y = index / Width;
                    component.Add(new GridPosition(x, y));

                    stackCount = SeedIfEmpty(x - 1, y, visited, stack, stackCount);
                    stackCount = SeedIfEmpty(x + 1, y, visited, stack, stackCount);
                    stackCount = SeedIfEmpty(x, y - 1, visited, stack, stackCount);
                    stackCount = SeedIfEmpty(x, y + 1, visited, stack, stackCount);
                }

                components.Add(component);
            }

            if (components.Count <= 1)
            {
                return;
            }

            int largestIndex = 0;
            for (int i = 1; i < components.Count; i++)
            {
                if (components[i].Count > components[largestIndex].Count)
                {
                    largestIndex = i;
                }
            }

            for (int i = 0; i < components.Count; i++)
            {
                if (i == largestIndex)
                {
                    continue;
                }

                List<GridPosition> component = components[i];
                for (int j = 0; j < component.Count; j++)
                {
                    results.Add(component[j]);
                }
            }
        }

        /// <summary>Marks (x, y) visited and pushes it onto the flood-fill stack, when it is playable,
        /// empty, and not already marked. Returns the updated stack count so callers can chain calls
        /// without a <c>ref</c> parameter. Shared by both flood-fills above, which differ only in how
        /// they seed and what they count — never in what "an empty neighbour" means.</summary>
        private int SeedIfEmpty(int x, int y, bool[] visited, int[] stack, int stackCount)
        {
            var position = new GridPosition(x, y);
            if (!_shape.IsPlayable(position))
            {
                return stackCount;
            }

            int index = Index(position);
            if (_cells[index] != EMPTY || visited[index])
            {
                return stackCount;
            }

            visited[index] = true;
            stack[stackCount] = index;
            return stackCount + 1;
        }

        /// <summary>Deep copy, used for undo snapshots. Shares the shape (immutable, so there is
        /// nothing to copy) and copies the special-cell kinds and the hit counts as well as the colours
        /// — a snapshot that restored the colours but not the other two would silently strip every
        /// special block on the board and un-reinforce every reinforced cell.</summary>
        public Board Clone()
        {
            int[] copy = new int[_cells.Length];
            Array.Copy(_cells, copy, _cells.Length);

            var specialCopy = new SpecialCellKind[_specialKinds.Length];
            Array.Copy(_specialKinds, specialCopy, _specialKinds.Length);

            var cellSkinCopy = new CellSkinKind[_cellSkins.Length];
            Array.Copy(_cellSkins, cellSkinCopy, _cellSkins.Length);

            int[] hitCountCopy = new int[_hitCounts.Length];
            Array.Copy(_hitCounts, hitCountCopy, _hitCounts.Length);

            int[] timerCountdownCopy = new int[_timerCountdowns.Length];
            Array.Copy(_timerCountdowns, timerCountdownCopy, _timerCountdowns.Length);

            return new Board(_shape, copy, specialCopy, cellSkinCopy, hitCountCopy, timerCountdownCopy);
        }

        /// <summary>Overwrites this board's cells with <paramref name="source"/>'s. Used to reuse a scratch
        /// board across preview queries without allocating a new board each call. Copies the
        /// special-cell kinds and the hit counts too, for the same reason <see cref="Clone"/> does.
        /// <para>
        /// The two boards must share a shape. Refused rather than reinterpreted: copying a differently
        /// shaped board's cells across would silently move every block by a row and drop blocks off the
        /// end, and a scratch board that has drifted out of step with the real one is exactly the bug
        /// this check exists to name.
        /// </para>
        /// </summary>
        public void CopyFrom(Board source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (!ReferenceEquals(source._shape, _shape)
                && (source.Width != Width || source.Height != Height))
            {
                throw new ArgumentException(
                    $"Cannot copy a {source.Width}x{source.Height} board onto a {Width}x{Height} one.",
                    nameof(source));
            }

            Array.Copy(source._cells, _cells, _cells.Length);
            Array.Copy(source._specialKinds, _specialKinds, _specialKinds.Length);
            Array.Copy(source._cellSkins, _cellSkins, _cellSkins.Length);
            Array.Copy(source._hitCounts, _hitCounts, _hitCounts.Length);
            Array.Copy(source._timerCountdowns, _timerCountdowns, _timerCountdowns.Length);
        }

        /// <summary>Flat index of <paramref name="position"/> — also the index a caller's own per-cell
        /// scratch buffer (sized <see cref="CellCount"/>) must use to stay in step with this board.</summary>
        internal int Index(GridPosition position)
        {
            if (!IsInside(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, "Outside the board.");
            }

            return _shape.Index(position);
        }
    }
}
