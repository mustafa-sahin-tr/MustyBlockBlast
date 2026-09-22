using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.ChainLightning"/>'s effect: destroying one arcs to
    /// <see cref="MAX_TARGETS_PER_STRIKE"/> occupied cells anywhere on the board, chosen at random, and
    /// vaporizes them where they stand.
    /// <para>
    /// <b>The one effect whose targets are random.</b> Every other kind's footprint is geometry — a
    /// ring, a line, a pull towards a point — and is readable off the board before it fires; this one
    /// deliberately is not, which is the whole of its character. The <em>spawn</em> rule stays fully
    /// deterministic (see <see cref="ChainLightningSpawnSelector"/>): the player always knows they
    /// earned a tile, never which cells it will take.
    /// </para>
    /// <para>
    /// <b>Bounded by the board, not by the constant.</b> A strike takes
    /// <c>Min(MAX_TARGETS_PER_STRIKE, occupied cells)</c> targets, so a board holding three blocks loses
    /// three and no attempt is ever made to pick a cell that is not there. Sampling is a partial
    /// Fisher-Yates shuffle over the collected cells, which is what makes the picks uniform <em>and</em>
    /// repeat-free in one pass, with no second collection to remember what has already been taken.
    /// </para>
    /// <para>
    /// <b>A tile caught in its own strike fires in turn</b>, exactly as a second explosive core caught in
    /// a blast does, and through the same "never the same centre twice" guard plus iteration cap. Each
    /// chained strike samples afresh against the occupancy it finds — the board shrinks as the chain
    /// proceeds, so a later strike genuinely has fewer cells to choose from. No <em>other</em> kind
    /// caught among the vaporized cells is re-triggered here: this effect only ever sets off its own.
    /// </para>
    /// <para>
    /// Nothing is cleared as a line: a strike that completes a line leaves it standing, and
    /// <see cref="CascadeClearResolver"/> re-checks fullness on the next iteration — which is how a
    /// vaporization that completes a line chains (AC5).
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="ExplosiveCoreEffect"/>, <see cref="LaserEffect"/> and
    /// <see cref="VortexEffect"/> are: one instance per System that resolves clears, with
    /// <see cref="BeginResolution"/> called before each resolution rather than a fresh instance
    /// allocated. <see cref="VaporizedCells"/> is therefore a buffer this instance owns and overwrites —
    /// a caller that needs it beyond the current resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class ChainLightningEffect : ISpecialCellEffect
    {
        /// <summary>How many cells one strike arcs to at most. The issue's figure, stated once here so
        /// the effect and the tests that pin it down can never disagree about it.</summary>
        public const int MAX_TARGETS_PER_STRIKE = 5;

        private readonly Random _random;

        private readonly List<GridPosition> _vaporizedCells = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>Every occupied cell of the board as the strike currently being applied found it.
        /// Refilled at the start of each strike and consumed within it, so one buffer serves the whole
        /// chain — and so a chained strike measures the board the strikes before it left behind.</summary>
        private readonly List<GridPosition> _occupiedBuffer = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>Tiles still to fire in the current chain, as a queue walked by index — never
        /// recursion, which a board-sized chain would drive dozens of frames deep.</summary>
        private readonly List<GridPosition> _pendingCenters = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>Which cells have already fired in the current chain, so a tile caught in another
        /// tile's strike cannot fire twice. Indexed exactly as the board indexes its own cells, and
        /// grown to fit a board bigger than the standard one the first time such a board is seen — never
        /// per call, and never shrunk.</summary>
        private bool[] _struck = new bool[Board.SIZE * Board.SIZE];

        /// <summary>
        /// The random stream the targets are drawn from, supplied rather than created so the System that
        /// owns the run owns its randomness: one seeded stream per run makes a run reproducible, which a
        /// stream this class created for itself could never be.
        /// </summary>
        public ChainLightningEffect(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>Every cell this effect emptied since the last <see cref="BeginResolution"/>, in the
        /// order it emptied them, across every strike of every chain in that resolution. Only cells that
        /// actually held a block are listed — the sample is drawn from occupied cells only, so an empty
        /// cell can never appear here.</summary>
        public IReadOnlyList<GridPosition> VaporizedCells => _vaporizedCells;

        /// <summary>True when a chain since the last <see cref="BeginResolution"/> stopped because it
        /// reached <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/> strikes rather than because
        /// it ran out of tiles. Surfaced for tests and diagnostics; the board is left consistent either
        /// way.</summary>
        public bool StoppedAtChainCap { get; private set; }

        /// <summary>Of <see cref="VaporizedCells"/>, how many were <see cref="SpecialCellKind.Timer"/>
        /// cells — destroyed mid-strike rather than through a normal clear phase, which is exactly the
        /// shape of the reinforced-cell reporting gap issue #307 AC11 requires this mechanic not repeat:
        /// unlike a reinforced cell finished off by a strike (which is silently uncounted), a timer cell
        /// caught in one is counted here and summed by the caller into the placement's "cleared in time"
        /// total.</summary>
        public int TimerCellsDestroyedCount { get; private set; }

        private readonly int[] _diamondsDestroyedCountByColour = new int[ColourTally.LENGTH];

        /// <summary>Of <see cref="VaporizedCells"/>, how many were <see cref="SpecialCellKind.Diamond"/>
        /// cells, per gem colour — destroyed mid-strike rather than through a normal clear phase, so
        /// they never pass through <see cref="DiamondClearEffect"/>. Counted here and summed by the
        /// caller into the placement's per-colour diamond total, exactly as
        /// <see cref="TimerCellsDestroyedCount"/> is (issue #393 AC3). A buffer this instance overwrites
        /// on the next <see cref="BeginResolution"/>.</summary>
        public IReadOnlyList<int> DiamondsDestroyedCountByColour => _diamondsDestroyedCountByColour;

        /// <summary>Starts a new resolution: forgets the previous one's vaporized cells. Must be called
        /// before the resolution that will apply this effect, or the two resolutions' cells would be
        /// reported as one — and Presentation would sweep cells that vanished a move ago.</summary>
        public void BeginResolution()
        {
            _vaporizedCells.Clear();
            TimerCellsDestroyedCount = 0;
            Array.Clear(_diamondsDestroyedCountByColour, 0, _diamondsDestroyedCountByColour.Length);
            StoppedAtChainCap = false;
        }

        /// <summary>
        /// Vaporizes up to <see cref="MAX_TARGETS_PER_STRIKE"/> random occupied cells, then does the same
        /// for every chain lightning tile that strike destroyed, and so on until the chain runs out.
        /// <para>
        /// The trigger's own cell is already empty when this is called (see
        /// <see cref="ISpecialCellEffect"/>), so it can never be one of its own targets — the sample only
        /// ever contains occupied cells — and it is not counted among the cells reported as vaporized:
        /// whatever destroyed it already reported it.
        /// </para>
        /// <para>
        /// One chain is bounded at <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/> strikes. The
        /// "never the same centre twice" guard already bounds it at the cell count of the board, so the
        /// cap is belt-and-braces against a future board shape or effect ordering that breaks that
        /// assumption — reaching it is not a failure state: whatever is left standing is simply left
        /// standing, exactly as the cascade loop's own cap leaves a full line standing.
        /// </para>
        /// </summary>
        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.ChainLightning || !board.IsInside(trigger.Position))
            {
                return;
            }

            if (_struck.Length < board.CellCount)
            {
                _struck = new bool[board.CellCount];
            }

            _pendingCenters.Clear();
            Array.Clear(_struck, 0, _struck.Length);

            // A cell that is already empty can never be sampled (only occupied cells are collected), so
            // marking the trigger's own — already emptied — position costs nothing and keeps the guard
            // uniform: every centre in the queue was marked on the way in.
            MarkStruck(board, trigger.Position);
            _pendingCenters.Add(trigger.Position);

            for (int centerIndex = 0; centerIndex < _pendingCenters.Count; centerIndex++)
            {
                if (centerIndex >= CascadeClearResolver.MAX_CASCADE_ITERATIONS)
                {
                    StoppedAtChainCap = true;
                    return;
                }

                Strike(board);
            }
        }

        /// <summary>
        /// One tile's arc: samples the board's occupied cells and empties the ones it drew.
        /// <para>
        /// The occupancy is re-read here rather than once per chain, because each strike genuinely acts
        /// on the board the strike before it left — a chain on a board holding six blocks takes five and
        /// then one, not five and then five of a board that no longer exists.
        /// </para>
        /// <para>
        /// Each cell's kind is read before the cell is cleared: <see cref="Board.Clear"/> resets it, so
        /// afterwards a destroyed tile is indistinguishable from an ordinary block.
        /// </para>
        /// </summary>
        private void Strike(Board board)
        {
            CollectOccupied(board);

            int targetCount = Math.Min(MAX_TARGETS_PER_STRIKE, _occupiedBuffer.Count);

            for (int i = 0; i < targetCount; i++)
            {
                // Partial Fisher-Yates: swap a uniformly chosen cell from the untaken tail into slot i
                // and take it. Every cell is equally likely and none can be drawn twice, in one pass
                // over the buffer and with nothing allocated to remember what has gone.
                int pick = i + _random.Next(_occupiedBuffer.Count - i);
                GridPosition cell = _occupiedBuffer[pick];
                _occupiedBuffer[pick] = _occupiedBuffer[i];
                _occupiedBuffer[i] = cell;

                SpecialCellKind kind = board.GetSpecialKind(cell);

                // Read before the damage gate for the same reason the kind is: Board.Clear wipes it.
                int diamondColourId = kind == SpecialCellKind.Diamond ? board.GetDiamondColourId(cell) : 0;

                // Through the damage gate, not Board.Clear: a reinforced cell the arc happened to pick
                // spends one hit and stays standing (issue #153 AC5), and a cell that survived is
                // neither a vaporized cell nor a tile this strike could have set off. The target was
                // still spent on it — an arc that hits armour has hit something.
                if (!board.TryDamage(cell))
                {
                    continue;
                }

                _vaporizedCells.Add(cell);

                if (kind == SpecialCellKind.Timer)
                {
                    TimerCellsDestroyedCount++;
                }

                if (kind == SpecialCellKind.Diamond)
                {
                    ColourTally.Increment(_diamondsDestroyedCountByColour, diamondColourId);
                }

                if (kind == SpecialCellKind.ChainLightning && !IsStruck(board, cell))
                {
                    MarkStruck(board, cell);
                    _pendingCenters.Add(cell);
                }
            }
        }

        /// <summary>Appends every occupied cell of the board to <see cref="_occupiedBuffer"/>, which is
        /// cleared first. Goes through <see cref="Board.IsPlayable"/> as well as
        /// <see cref="Board.IsOccupied"/> so it makes no assumption about board shape — a hole can never
        /// hold a block, and a cell that cannot hold one is not a target.</summary>
        private void CollectOccupied(Board board)
        {
            _occupiedBuffer.Clear();

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (board.IsPlayable(position) && board.IsOccupied(position))
                    {
                        _occupiedBuffer.Add(position);
                    }
                }
            }
        }

        private bool IsStruck(Board board, GridPosition position) => _struck[board.Index(position)];

        private void MarkStruck(Board board, GridPosition position) => _struck[board.Index(position)] = true;
    }
}
