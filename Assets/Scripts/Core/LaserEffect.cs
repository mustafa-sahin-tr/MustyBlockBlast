using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.Laser"/>'s effect: destroying one wipes the full line running at
    /// right angles to whatever destroyed it, and any laser caught in that wipe fires in turn.
    /// <para>
    /// The axis comes from <see cref="SpecialCellTrigger.Axis"/>, which whatever destroyed the cell
    /// recorded at the one moment it was still known. A row clear took it out, so it wipes its column;
    /// a column clear, so it wipes its row; both at once (it sat on their intersection), so it wipes
    /// both. <see cref="ClearAxis.None"/> — a Bomb, a Colour Cleanser — has no opposite to compute, so
    /// it likewise wipes both: the alternative would be to pick an axis arbitrarily or to drop the
    /// effect entirely, and neither is something the player could read off the board.
    /// </para>
    /// <para>
    /// The cells of a line come from <see cref="Board.CollectRowCells"/>/<see cref="Board.CollectColumnCells"/>
    /// rather than a <see cref="Board.SIZE"/> loop here, so line geometry stays defined in exactly one
    /// place and a differently shaped board has one thing to change.
    /// </para>
    /// <para>
    /// Fullness is irrelevant: a wipe clears every occupied cell of the line whether or not the line
    /// was complete, exactly as a Row Clear / Column Clear power-up does. It deliberately does not
    /// clear a line "because it is full" either — the cascade loop re-checks fullness itself on the
    /// next iteration, which is how a wipe that completes a new line chains.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="ExplosiveCoreEffect"/> is: one instance per System
    /// that resolves clears, with <see cref="BeginResolution"/> called before each resolution rather
    /// than a fresh instance allocated. <see cref="WipedCells"/> is therefore a buffer this instance
    /// owns and overwrites — a caller that needs it beyond the current resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class LaserEffect : ISpecialCellEffect
    {
        private readonly List<GridPosition> _wipedCells = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>The cells of the one line currently being wiped. Filled and consumed inside a single
        /// <see cref="WipeLine"/> call, so one buffer serves the whole chain.</summary>
        private readonly List<GridPosition> _lineBuffer = new List<GridPosition>(Board.SIZE);

        /// <summary>Lasers still to fire in the current chain, as a queue walked by index — never
        /// recursion, which a board-sized chain would drive 64 frames deep. Parallel to
        /// <see cref="_pendingAxes"/>.</summary>
        private readonly List<GridPosition> _pendingOrigins = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>How each queued laser was destroyed, which is what decides the line it wipes. Kept
        /// beside the origin rather than recomputed, because after the wipe that destroyed it the board
        /// no longer knows.</summary>
        private readonly List<ClearAxis> _pendingAxes = new List<ClearAxis>(Board.SIZE * Board.SIZE);

        /// <summary>Which cells have already fired in the current chain, so a laser caught in the wipe
        /// of a laser it itself caught cannot fire twice.</summary>
        private readonly bool[] _fired = new bool[Board.SIZE * Board.SIZE];

        /// <summary>Every cell this effect emptied since the last <see cref="BeginResolution"/>, in the
        /// order it emptied them. Only cells that actually held a block are listed: an already-empty
        /// cell on a wiped line is not "cleared" and must not be scored or repainted as if it were.</summary>
        public IReadOnlyList<GridPosition> WipedCells => _wipedCells;

        /// <summary>True when a chain since the last <see cref="BeginResolution"/> stopped because it
        /// reached <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/> firings rather than because
        /// it ran out of lasers. Surfaced for tests and diagnostics; the board is left consistent either
        /// way.</summary>
        public bool StoppedAtWipeCap { get; private set; }

        /// <summary>Starts a new resolution: forgets the previous one's wiped cells. Must be called
        /// before the resolution that will apply this effect, or the two resolutions' cells would be
        /// reported as one.</summary>
        public void BeginResolution()
        {
            _wipedCells.Clear();
            StoppedAtWipeCap = false;
        }

        /// <summary>
        /// Wipes the line (or lines) opposite to how <paramref name="trigger"/> was destroyed, then the
        /// lines opposite to how every laser that wipe destroyed was destroyed, and so on until the
        /// chain runs out.
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

            if (trigger.Kind != SpecialCellKind.Laser || !Board.IsInside(trigger.Position))
            {
                return;
            }

            _pendingOrigins.Clear();
            _pendingAxes.Clear();
            Array.Clear(_fired, 0, _fired.Length);

            // A cell that is already empty can never be wiped (only occupied cells are read), so marking
            // the trigger's own — already emptied — position costs nothing and keeps the guard uniform:
            // every origin in the queue was marked on the way in.
            MarkFired(trigger.Position);
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
        /// them and queueing any laser among them as a further origin. The kind is read before the cell
        /// is cleared: <see cref="Board.Clear"/> resets it, so afterwards a destroyed laser is
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

            // A laser this wipe destroys was destroyed *by this line*, which is exactly what decides
            // the line it will wipe in turn — so the chain keeps turning ninety degrees each step.
            ClearAxis destroyedBy = wipeRow ? ClearAxis.Row : ClearAxis.Column;

            for (int i = 0; i < _lineBuffer.Count; i++)
            {
                GridPosition cell = _lineBuffer[i];
                if (!board.IsOccupied(cell))
                {
                    continue;
                }

                SpecialCellKind kind = board.GetSpecialKind(cell);
                board.Clear(cell);
                _wipedCells.Add(cell);

                if (kind == SpecialCellKind.Laser && !IsFired(cell))
                {
                    MarkFired(cell);
                    _pendingOrigins.Add(cell);
                    _pendingAxes.Add(destroyedBy);
                }
            }
        }

        private bool IsFired(GridPosition position) => _fired[(position.Y * Board.SIZE) + position.X];

        private void MarkFired(GridPosition position) => _fired[(position.Y * Board.SIZE) + position.X] = true;
    }
}
