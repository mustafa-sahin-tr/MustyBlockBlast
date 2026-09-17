using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.ExplosiveCore"/>'s effect: destroying one also destroys the 3x3 area
    /// around it, and any explosive core caught in that area detonates in turn.
    /// <para>
    /// The footprint comes from <see cref="PowerUpTargetCells.ForBomb"/> — the same geometry the Bomb
    /// power-up uses, including its clamp at the board edges, so an edge blast covers 6 cells and a
    /// corner blast 4 without this class knowing anything about board shape.
    /// </para>
    /// <para>
    /// Long-lived by design: one instance is owned by each System that resolves clears, and
    /// <see cref="BeginResolution"/> is called before each resolution rather than a fresh instance
    /// being allocated. <see cref="BlastedCells"/> is therefore a buffer this instance owns and
    /// overwrites — a caller that needs it beyond the current resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class ExplosiveCoreEffect : ISpecialCellEffect
    {
        private readonly List<GridPosition> _blastedCells = new List<GridPosition>(Board.SIZE * Board.SIZE);
        private readonly List<GridPosition> _targetBuffer =
            new List<GridPosition>(PowerUpTargetCells.MAX_TARGET_CELLS);

        /// <summary>Centres still to blast in the current chain, as a queue walked by index — never
        /// recursion, which a board-sized chain would drive 64 frames deep.</summary>
        private readonly List<GridPosition> _pendingCenters = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>Which cells have already been used as a blast centre in the current chain, so a
        /// core caught in its own neighbour's blast cannot be detonated twice. Indexed exactly as the
        /// board indexes its own cells, and grown to fit a board bigger than the standard one the first
        /// time such a board is seen — never per call, and never shrunk.</summary>
        private bool[] _detonated = new bool[Board.SIZE * Board.SIZE];

        /// <summary>Every cell this effect emptied since the last <see cref="BeginResolution"/>, in the
        /// order it emptied them. Only cells that actually held a block are listed: an already-empty
        /// cell inside a blast footprint is not "cleared" and must not be scored or repainted as if it
        /// were.</summary>
        public IReadOnlyList<GridPosition> BlastedCells => _blastedCells;

        /// <summary>True when a chain since the last <see cref="BeginResolution"/> stopped because it
        /// reached <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/> detonations rather than
        /// because it ran out of cores. Surfaced for tests and diagnostics; the board is left
        /// consistent either way.</summary>
        public bool StoppedAtBlastCap { get; private set; }

        /// <summary>Starts a new resolution: forgets the previous one's blasted cells. Must be called
        /// before the resolution that will apply this effect, or the two resolutions' cells would be
        /// reported as one.</summary>
        public void BeginResolution()
        {
            _blastedCells.Clear();
            StoppedAtBlastCap = false;
        }

        /// <summary>
        /// Blasts the 3x3 area around <paramref name="trigger"/>, then around every explosive core that
        /// area destroyed, and so on until the chain runs out.
        /// <para>
        /// The trigger's own cell is already empty when this is called (see
        /// <see cref="ISpecialCellEffect"/>), so it is a centre to blast around, never a cell to clear.
        /// </para>
        /// <para>
        /// One chain is bounded at <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/>
        /// detonations. The "never the same centre twice" guard already bounds it at the cell count of
        /// the board, so the cap is belt-and-braces against a future board shape or effect ordering
        /// that breaks that assumption — reaching it is not a failure state: whatever is left standing
        /// is simply left standing, exactly as the cascade loop's own cap leaves a full line standing.
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

            if (_detonated.Length < board.CellCount)
            {
                _detonated = new bool[board.CellCount];
            }

            _pendingCenters.Clear();
            Array.Clear(_detonated, 0, _detonated.Length);

            // A cell that is already empty can never be detonated (only occupied cells are read), so
            // marking the trigger's own — already emptied — position costs nothing and keeps the guard
            // uniform: every centre in the queue was marked on the way in.
            MarkDetonated(board, trigger.Position);
            _pendingCenters.Add(trigger.Position);

            for (int centerIndex = 0; centerIndex < _pendingCenters.Count; centerIndex++)
            {
                if (centerIndex >= CascadeClearResolver.MAX_CASCADE_ITERATIONS)
                {
                    StoppedAtBlastCap = true;
                    return;
                }

                BlastAround(board, _pendingCenters[centerIndex]);
            }
        }

        /// <summary>Clears every occupied cell of one 3x3 footprint, recording them and queueing any
        /// explosive core among them as a further centre. The kind is read before the cell is cleared:
        /// <see cref="Board.Clear"/> resets it, so afterwards a destroyed core is indistinguishable
        /// from an ordinary block.</summary>
        private void BlastAround(Board board, GridPosition center)
        {
            IReadOnlyList<GridPosition> footprint =
                PowerUpTargetCells.ForBomb(board.Shape, center, _targetBuffer);

            for (int i = 0; i < footprint.Count; i++)
            {
                GridPosition cell = footprint[i];
                if (!board.IsOccupied(cell))
                {
                    continue;
                }

                SpecialCellKind kind = board.GetSpecialKind(cell);

                // Through the damage gate, not Board.Clear: a reinforced cell in the footprint spends
                // one hit and stays standing (issue #153 AC5), and a cell that survived is neither a
                // blasted cell nor a core this blast could have set off.
                if (!board.TryDamage(cell))
                {
                    continue;
                }

                _blastedCells.Add(cell);

                if (kind == SpecialCellKind.ExplosiveCore && !IsDetonated(board, cell))
                {
                    MarkDetonated(board, cell);
                    _pendingCenters.Add(cell);
                }
            }
        }

        private bool IsDetonated(Board board, GridPosition position) => _detonated[board.Index(position)];

        private void MarkDetonated(Board board, GridPosition position) => _detonated[board.Index(position)] = true;
    }
}
