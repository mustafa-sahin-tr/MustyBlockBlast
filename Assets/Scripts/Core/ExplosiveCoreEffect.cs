using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.ExplosiveCore"/>'s effect (issue #398): destroying one wipes the full
    /// line running at right angles to whatever destroyed it, and any core caught in that wipe fires in
    /// turn — the bonus-wipe payoff that actually matches the cross-clear which earns a core.
    /// <para>
    /// The axis comes from <see cref="SpecialCellTrigger.Axis"/>, which whatever destroyed the cell
    /// recorded at the one moment it was still known. A row clear took it out, so it wipes its column;
    /// a column clear, so it wipes its row; both at once (it sat on their intersection), so it wipes
    /// both. <see cref="ClearAxis.None"/> — a Bomb, a Colour Cleanser, a hammer — has no opposite to
    /// compute, so it likewise wipes both: the alternative would be to pick an axis arbitrarily or to
    /// drop the effect entirely, and neither is something the player could read off the board.
    /// </para>
    /// <para>
    /// The cells of a line come from <see cref="Board.CollectRowCells"/>/<see cref="Board.CollectColumnCells"/>
    /// rather than a width/height loop here, so line geometry — including which cells of a line are
    /// holes and therefore not there at all — stays defined in exactly one place.
    /// </para>
    /// <para>
    /// Fullness is irrelevant: a wipe clears every occupied cell of the line whether or not the line
    /// was complete, exactly as a Row Clear / Column Clear power-up does. A wipe whose opposite line is
    /// already empty is simply a no-op — no hand-off, no fallback (AC2). It deliberately does not clear
    /// a line "because it is full" either — the cascade loop re-checks fullness itself on the next
    /// iteration. A wipe only ever removes cells, so it can never complete a line, and the spawn rule
    /// (<see cref="ExplosiveCoreSpawnSelector"/>) reads the placement's own primary clear only — which
    /// is what keeps a bonus wipe from ever minting a fresh core (AC3).
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="LaserEffect"/> is (the algorithm here is a port of
    /// that effect's, keyed on this kind): one instance per System that resolves clears, with
    /// <see cref="BeginResolution"/> called before each resolution rather than a fresh instance
    /// allocated. <see cref="WipedCells"/> is therefore a buffer this instance owns and overwrites — a
    /// caller that needs it beyond the current resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class ExplosiveCoreEffect : ISpecialCellEffect
    {
        private readonly List<GridPosition> _wipedCells = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>The cells of the one line currently being wiped. Filled and consumed inside a single
        /// <see cref="WipeLine"/> call, so one buffer serves the whole chain.</summary>
        private readonly List<GridPosition> _lineBuffer = new List<GridPosition>(Board.SIZE);

        /// <summary>Cores still to fire in the current chain, as a queue walked by index — never
        /// recursion, which a board-sized chain would drive 64 frames deep. Parallel to
        /// <see cref="_pendingAxes"/>.</summary>
        private readonly List<GridPosition> _pendingOrigins = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>How each queued core was destroyed, which is what decides the line it wipes. Kept
        /// beside the origin rather than recomputed, because after the wipe that destroyed it the board
        /// no longer knows.</summary>
        private readonly List<ClearAxis> _pendingAxes = new List<ClearAxis>(Board.SIZE * Board.SIZE);

        /// <summary>Which cells have already fired in the current chain, so a core caught in the wipe of
        /// a core it itself caught cannot fire twice. Indexed exactly as the board indexes its own cells,
        /// and grown to fit a board bigger than the standard one the first time such a board is seen —
        /// never per call, and never shrunk.</summary>
        private bool[] _fired = new bool[Board.SIZE * Board.SIZE];

        /// <summary>Every cell this effect emptied since the last <see cref="BeginResolution"/>, in the
        /// order it emptied them. Only cells that actually held a block are listed: an already-empty
        /// cell on a wiped line is not "cleared" and must not be scored or repainted as if it were.</summary>
        public IReadOnlyList<GridPosition> WipedCells => _wipedCells;

        /// <summary>True when a chain since the last <see cref="BeginResolution"/> stopped because it
        /// reached <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/> firings rather than because
        /// it ran out of cores. Surfaced for tests and diagnostics; the board is left consistent either
        /// way.</summary>
        public bool StoppedAtWipeCap { get; private set; }

        /// <summary>Of <see cref="WipedCells"/>, how many were <see cref="SpecialCellKind.Timer"/> cells
        /// — destroyed mid-wipe rather than through a normal clear phase, so they never pass through
        /// <see cref="TimerCellClearEffect"/>. Counted here and summed by the caller into the placement's
        /// "cleared in time" total, exactly as <see cref="LaserEffect.TimerCellsDestroyedCount"/> is
        /// (issue #307 AC11).</summary>
        public int TimerCellsDestroyedCount { get; private set; }

        private readonly int[] _diamondsDestroyedCountByColour = new int[Collectibles.TALLY_LENGTH];

        /// <summary>Of <see cref="WipedCells"/>, how many were <see cref="SpecialCellKind.Diamond"/>
        /// cells, per gem colour — destroyed mid-wipe rather than through a normal clear phase, so they
        /// never pass through <see cref="DiamondClearEffect"/>. Counted here and summed by the caller
        /// into the placement's per-colour diamond total, exactly as
        /// <see cref="LaserEffect.DiamondsDestroyedCountByColour"/> is (issue #393 AC3). A buffer this
        /// instance overwrites on the next <see cref="BeginResolution"/>.</summary>
        public IReadOnlyList<int> DiamondsDestroyedCountByColour => _diamondsDestroyedCountByColour;

        /// <summary>Starts a new resolution: forgets the previous one's wiped cells. Must be called
        /// before the resolution that will apply this effect, or the two resolutions' cells would be
        /// reported as one.</summary>
        public void BeginResolution()
        {
            _wipedCells.Clear();
            TimerCellsDestroyedCount = 0;
            Array.Clear(_diamondsDestroyedCountByColour, 0, _diamondsDestroyedCountByColour.Length);
            StoppedAtWipeCap = false;
        }

        /// <summary>
        /// Wipes the line (or lines) opposite to how <paramref name="trigger"/> was destroyed, then the
        /// lines opposite to how every core that wipe destroyed was destroyed, and so on until the chain
        /// runs out.
        /// <para>
        /// The trigger's own cell is already empty when this is called (see
        /// <see cref="ISpecialCellEffect"/>), so it is an origin to wipe through, never a cell to clear.
        /// </para>
        /// <para>
        /// One chain is bounded at <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/> firings.
        /// The "never the same origin twice" guard already bounds it at the cell count of the board, so
        /// the cap is belt-and-braces against a future board shape or effect ordering that breaks that
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

            if (trigger.Kind != SpecialCellKind.ExplosiveCore || !board.IsInside(trigger.Position))
            {
                return;
            }

            if (_fired.Length < board.CellCount)
            {
                _fired = new bool[board.CellCount];
            }

            _pendingOrigins.Clear();
            _pendingAxes.Clear();
            Array.Clear(_fired, 0, _fired.Length);

            // A cell that is already empty can never be wiped (only occupied cells are read), so marking
            // the trigger's own — already emptied — position costs nothing and keeps the guard uniform:
            // every origin in the queue was marked on the way in.
            MarkFired(board, trigger.Position);
            _pendingOrigins.Add(trigger.Position);
            _pendingAxes.Add(trigger.Axis);

            for (int originIndex = 0; originIndex < _pendingOrigins.Count; originIndex++)
            {
                if (originIndex >= CascadeClearResolver.MAX_CASCADE_ITERATIONS)
                {
                    StoppedAtWipeCap = true;
                    return;
                }

                GridPosition origin = _pendingOrigins[originIndex];
                ClearAxis destroyedBy = _pendingAxes[originIndex];

                // Opposite-axis, stated as two exclusions rather than a switch: destroyed along a row
                // means "not the row", destroyed along a column means "not the column", and the two
                // remaining cases — Both and None — are excluded by neither and so wipe both lines.
                if (destroyedBy != ClearAxis.Row)
                {
                    WipeLine(board, origin, wipeRow: true);
                }

                if (destroyedBy != ClearAxis.Column)
                {
                    WipeLine(board, origin, wipeRow: false);
                }
            }
        }

        /// <summary>Clears every occupied cell of one line through <paramref name="origin"/>, recording
        /// them and queueing any core among them as a further origin. The kind is read before the cell
        /// is cleared: <see cref="Board.Clear"/> resets it, so afterwards a destroyed core is
        /// indistinguishable from an ordinary block.</summary>
        private void WipeLine(Board board, GridPosition origin, bool wipeRow)
        {
            _lineBuffer.Clear();

            if (wipeRow)
            {
                board.CollectRowCells(origin.Y, _lineBuffer);
            }
            else
            {
                board.CollectColumnCells(origin.X, _lineBuffer);
            }

            // A core this wipe destroys was destroyed *by this line*, which is exactly what decides the
            // line it will wipe in turn — so the chain keeps turning ninety degrees each step.
            ClearAxis destroyedBy = wipeRow ? ClearAxis.Row : ClearAxis.Column;

            for (int i = 0; i < _lineBuffer.Count; i++)
            {
                GridPosition cell = _lineBuffer[i];
                if (!board.IsOccupied(cell))
                {
                    continue;
                }

                SpecialCellKind kind = board.GetSpecialKind(cell);

                // Read before the damage gate for the same reason the kind is: Board.Clear wipes it.
                int diamondColourId = kind == SpecialCellKind.Diamond ? board.GetDiamondColourId(cell) : 0;

                // Through the damage gate, not Board.Clear: a reinforced cell on the wiped line spends
                // one hit and stays standing (issue #153 AC5), and a cell that survived is neither a
                // wiped cell nor a core this wipe could have set off.
                if (!board.TryDamage(cell))
                {
                    continue;
                }

                _wipedCells.Add(cell);

                if (kind == SpecialCellKind.Timer)
                {
                    TimerCellsDestroyedCount++;
                }

                if (kind == SpecialCellKind.Diamond)
                {
                    Collectibles.Increment(_diamondsDestroyedCountByColour, diamondColourId);
                }

                if (kind == SpecialCellKind.ExplosiveCore && !IsFired(board, cell))
                {
                    MarkFired(board, cell);
                    _pendingOrigins.Add(cell);
                    _pendingAxes.Add(destroyedBy);
                }
            }
        }

        private bool IsFired(Board board, GridPosition position) => _fired[board.Index(position)];

        private void MarkFired(Board board, GridPosition position) => _fired[board.Index(position)] = true;
    }
}
