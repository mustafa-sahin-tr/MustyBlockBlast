using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>One block a vortex dragged one cell inwards: where it stood and where it now stands.
    /// Both ends are kept because the board no longer knows either once the move is done — the source
    /// is empty and the destination is indistinguishable from a block that was always there — and
    /// Presentation needs both to animate the slide.</summary>
    public readonly struct VortexPull
    {
        public VortexPull(GridPosition from, GridPosition to)
        {
            From = from;
            To = to;
        }

        /// <summary>The cell the block left, which is empty afterwards.</summary>
        public GridPosition From { get; }

        /// <summary>The cell the block now occupies, one step nearer the vortex.</summary>
        public GridPosition To { get; }
    }

    /// <summary>
    /// <see cref="SpecialCellKind.Vortex"/>'s effect: destroying one drags every <em>isolated</em>
    /// block on the board one cell towards where the vortex stood, tidying the stragglers a
    /// nearly-full board leaves scattered about.
    /// <para>
    /// <b>Isolation</b> is a single-pass, per-cell test and deliberately not a flood fill: a cell is
    /// isolated when it is occupied and each of its four orthogonal neighbours is off-board, a hole, or
    /// unoccupied. Off-board and hole count as "nothing there" for the same reason they do everywhere
    /// else — neither can ever hold a block — and the whole test goes through
    /// <see cref="Board.IsPlayable"/>/<see cref="Board.IsOccupied"/>, so it makes no assumption about
    /// board size or shape.
    /// </para>
    /// <para>
    /// <b>The pull is exactly one step</b>, along whichever axis is farther from the vortex (on a tie,
    /// horizontally — an arbitrary but fixed choice, so the same board always resolves the same way and
    /// a snapshot of it replays identically). Isolation already guarantees all four neighbours are
    /// free, so that one step never has to be blocked, path-walked or slid: there is nothing in the
    /// way by construction. The one case that still has to be checked is two isolated blocks pulling
    /// into the <em>same</em> free cell — the second one stays put rather than overwriting the first.
    /// </para>
    /// <para>
    /// Isolation is measured once, before anything moves, and the moves are then applied to that
    /// snapshot. Re-testing as it went would make each block's fate depend on how far down the scan the
    /// previous one happened to be, which is the sort of order-dependence a player cannot read off the
    /// board.
    /// </para>
    /// <para>
    /// Nothing is cleared and nothing is created: a pull moves a block's colour <em>and</em> its own
    /// special kind, so a pulled laser is still a laser. It deliberately never clears a completed line
    /// either — <see cref="CascadeClearResolver"/> re-checks fullness on the next iteration, which is
    /// how a pull that completes a line chains.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="ExplosiveCoreEffect"/> and <see cref="LaserEffect"/>
    /// are: one instance per System that resolves clears, with <see cref="BeginResolution"/> called
    /// before each resolution rather than a fresh instance allocated. <see cref="Pulls"/> is therefore
    /// a buffer this instance owns and overwrites — a caller that needs it beyond the current
    /// resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class VortexEffect : ISpecialCellEffect
    {
        private readonly List<VortexPull> _pulls = new List<VortexPull>(Board.SIZE * Board.SIZE);

        /// <summary>The isolated cells of the scan currently being applied. Filled and consumed inside a
        /// single <see cref="Apply"/> call, so one buffer serves every vortex in a resolution.</summary>
        private readonly List<GridPosition> _isolatedBuffer = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>Every move this effect made since the last <see cref="BeginResolution"/>, in the
        /// order it made them. A block that was isolated but had nowhere free to go is not listed: it
        /// did not move, so nothing about it changed and nothing should be animated for it.</summary>
        public IReadOnlyList<VortexPull> Pulls => _pulls;

        /// <summary>
        /// True when <paramref name="position"/> holds a block whose four orthogonal neighbours are all
        /// empty, holes, or off the board. Public because it is the feature's one new board reading and
        /// is what the spawn rule, the tests and any later effect all have to agree on.
        /// </summary>
        public static bool IsIsolated(Board board, GridPosition position)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (!board.IsPlayable(position) || !board.IsOccupied(position))
            {
                return false;
            }

            return !HoldsBlock(board, new GridPosition(position.X - 1, position.Y))
                && !HoldsBlock(board, new GridPosition(position.X + 1, position.Y))
                && !HoldsBlock(board, new GridPosition(position.X, position.Y - 1))
                && !HoldsBlock(board, new GridPosition(position.X, position.Y + 1));
        }

        /// <summary>Starts a new resolution: forgets the previous one's pulls. Must be called before the
        /// resolution that will apply this effect, or the two resolutions' moves would be reported as
        /// one — and Presentation would animate a slide that already finished.</summary>
        public void BeginResolution() => _pulls.Clear();

        /// <summary>
        /// Drags every isolated block one cell towards <paramref name="trigger"/>'s position.
        /// <para>
        /// The trigger's own cell is already empty when this is called (see
        /// <see cref="ISpecialCellEffect"/>), so it is the centre to pull towards — and, being empty, it
        /// is never itself one of the blocks that move.
        /// </para>
        /// <para>
        /// Needs no iteration cap of its own, unlike the blast and wipe chains: one call is one scan
        /// followed by at most one move per cell, with nothing queued and nothing revisited, so it
        /// terminates on the board's cell count by construction.
        /// </para>
        /// </summary>
        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.Vortex || !board.IsInside(trigger.Position))
            {
                return;
            }

            CollectIsolated(board);

            for (int i = 0; i < _isolatedBuffer.Count; i++)
            {
                GridPosition from = _isolatedBuffer[i];
                if (!TryResolveTarget(board, from, trigger.Position, out GridPosition to))
                {
                    continue;
                }

                Move(board, from, to);
            }
        }

        /// <summary>True when <paramref name="position"/> is a cell that currently holds a block. False
        /// for an empty cell, a hole and anything off the board alike — the three cases isolation
        /// treats identically, because none of them is something a block is touching.</summary>
        private static bool HoldsBlock(Board board, GridPosition position)
            => board.IsPlayable(position) && board.IsOccupied(position);

        /// <summary>
        /// Where <paramref name="from"/>'s block goes, or false when it stays put.
        /// <para>
        /// The axis is whichever of the two distances to <paramref name="center"/> is greater, so a
        /// block always closes the larger of its two gaps first; a tie goes to the horizontal, chosen
        /// arbitrarily and fixed here so the rule is stated exactly once and always resolves the same
        /// way.
        /// </para>
        /// <para>
        /// The target is still checked for being playable and free: isolation guarantees the four
        /// neighbours held no block <em>when the scan ran</em>, and an earlier pull in the same scan may
        /// since have taken this one's destination. That block was there first and keeps it.
        /// </para>
        /// </summary>
        private static bool TryResolveTarget(
            Board board, GridPosition from, GridPosition center, out GridPosition target)
        {
            target = from;

            int deltaX = center.X - from.X;
            int deltaY = center.Y - from.Y;
            if (deltaX == 0 && deltaY == 0)
            {
                return false;
            }

            target = Math.Abs(deltaX) >= Math.Abs(deltaY)
                ? new GridPosition(from.X + Math.Sign(deltaX), from.Y)
                : new GridPosition(from.X, from.Y + Math.Sign(deltaY));

            return board.IsPlayable(target) && !board.IsOccupied(target);
        }

        /// <summary>Appends every isolated cell of the board to <see cref="_isolatedBuffer"/>, which is
        /// cleared first. One pass, one test per cell, no queue and no fill.</summary>
        private void CollectIsolated(Board board)
        {
            _isolatedBuffer.Clear();

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (IsIsolated(board, position))
                    {
                        _isolatedBuffer.Add(position);
                    }
                }
            }
        }

        /// <summary>Moves one block, colour and special kind together. The kind is read before the
        /// source is cleared: <see cref="Board.Clear"/> resets it, so afterwards the board no longer
        /// knows a pulled laser was ever a laser.</summary>
        private void Move(Board board, GridPosition from, GridPosition to)
        {
            int colourId = board[from];
            SpecialCellKind kind = board.GetSpecialKind(from);

            board.Clear(from);
            board.Occupy(to, colourId);
            board.SetSpecialKind(to, kind);

            _pulls.Add(new VortexPull(from, to));
        }
    }
}
