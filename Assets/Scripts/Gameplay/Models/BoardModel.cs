using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using VContainer;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Observable wrapper around the pure-C# <see cref="Core.Board"/>. Holds state only; every
    /// rule (placement legality, clearing, game over) lives in Core and is driven by Systems.
    /// Mutation is internal so only Gameplay Systems can change it.
    /// </summary>
    public sealed class BoardModel
    {
        private readonly Board _board;

        /// <summary>The standard 8x8 hole-free board — what every level authored so far uses. Marked
        /// for injection explicitly so VContainer can never pick the shape-taking overload, which it has
        /// no shape to supply for.</summary>
        [Inject]
        public BoardModel()
            : this(BoardShape.Standard)
        {
        }

        /// <summary>
        /// Builds the model around an explicit board outline. Not yet wired to level data: per-level
        /// shape selection is a later sub-issue of the board-shapes epic, and this exists so the shape
        /// a board is built with has one owner when that lands, rather than the model hardcoding a
        /// square forever.
        /// </summary>
        internal BoardModel(BoardShape shape)
        {
            _board = new Board(shape);
        }

        /// <summary>Raised for every cell whose colour id changed. Args: position, new colour id
        /// (<see cref="Core.Board.EMPTY"/> when the cell became empty).</summary>
        public event Action<GridPosition, int> CellChanged;

        /// <summary>
        /// Raised when a cell gains a <see cref="SpecialCellKind"/>. Args: position, the new kind.
        /// <para>
        /// Deliberately an "added" signal only, and deliberately not folded into
        /// <see cref="CellChanged"/>, whose args are "position, new colour id" and whose subscribers
        /// all read it as exactly that. A kind being <em>lost</em> needs no signal of its own: a cell
        /// only ever loses one by being destroyed (<see cref="Core.Board.Clear"/> resets the kind along
        /// with the colour), and every path that destroys a cell already announces it through
        /// <see cref="CellChanged"/> — so a View that drops a cell's special look whenever that cell is
        /// emptied is already correct, with no second event to keep in step.
        /// </para>
        /// </summary>
        public event Action<GridPosition, SpecialCellKind> SpecialKindChanged;

        /// <summary>
        /// Raised for a reinforced cell that is still standing and whose remaining hit count may have
        /// changed. Args: position, hits remaining.
        /// <para>
        /// Its own signal because <see cref="CellChanged"/>'s args are "position, new colour id" and a
        /// damaged cell's colour does not change — it is the same block, just closer to breaking. A cell
        /// whose last hit was spent needs no notification here: it was destroyed, which
        /// <see cref="CellChanged"/> already announces, and a View that drops a cell's damage look
        /// whenever that cell empties is correct with no second event to keep in step (the same
        /// reasoning <see cref="SpecialKindChanged"/> is built on).
        /// </para>
        /// </summary>
        public event Action<GridPosition, int> HitCountChanged;

        /// <summary>
        /// Raised for a <see cref="SpecialCellKind.Timer"/> cell that is still standing and whose
        /// remaining countdown may have changed. Args: position, placements remaining. Mirrors
        /// <see cref="HitCountChanged"/> exactly, for the same reason: a ticked-down timer cell is the
        /// same block, just closer to converting.
        /// </summary>
        public event Action<GridPosition, int> TimerCountdownChanged;

        /// <summary>
        /// Raised for a <see cref="SpecialCellKind.Timer"/> cell whose countdown just reached 0 and
        /// converted to an ordinary cell (issue #307 AC4/AC6a) — the cell stays occupied, so
        /// <see cref="CellChanged"/> does not fire for it, and <see cref="SpecialKindChanged"/> is
        /// deliberately an "added" signal only and would never announce a kind being lost either. A View
        /// showing the countdown needs its own, explicit "stop showing it" signal, which this is.
        /// </summary>
        public event Action<GridPosition> TimerCellExpired;

        /// <summary>The board's outline. Read-only and immutable — a View reads width, height and hole
        /// cells off it to lay itself out and to render the holes.</summary>
        public BoardShape Shape => _board.Shape;

        public int Width => _board.Width;

        public int Height => _board.Height;

        /// <summary>True when <paramref name="position"/> is inside the board but permanently
        /// unplayable, so a View can draw it as a gap rather than an empty cell.</summary>
        public bool IsHole(GridPosition position) => _board.IsHole(position);

        /// <summary>True when a piece could ever occupy <paramref name="position"/>.</summary>
        public bool IsPlayable(GridPosition position) => _board.IsPlayable(position);

        /// <summary>Read-only access for Views.</summary>
        public int GetCell(GridPosition position) => _board[position];

        /// <summary>Read-only access for Views, so a full repaint can re-derive every cell's special
        /// look from the model rather than trusting bookkeeping it accumulated from events.</summary>
        public SpecialCellKind GetSpecialKind(GridPosition position) => _board.GetSpecialKind(position);

        /// <summary>Read-only access for Views, for the reason <see cref="GetSpecialKind"/> is: a full
        /// repaint re-derives every cell's damage look from the model rather than trusting bookkeeping
        /// it accumulated from events. 0 for an ordinary or empty cell.</summary>
        public int GetHitCount(GridPosition position) => _board.GetHitCount(position);

        /// <summary>Read-only access for Views, for the reason <see cref="GetHitCount"/> is: a full
        /// repaint re-derives a timer cell's countdown from the model rather than trusting bookkeeping
        /// it accumulated from events. 0 for a cell that is not a <see cref="SpecialCellKind.Timer"/>
        /// cell.</summary>
        public int GetTimerCountdown(GridPosition position) => _board.GetTimerCountdown(position);

        /// <summary>Core board handed to the stateless Core rule helpers. Systems only.</summary>
        internal Board Board => _board;

        internal void Occupy(GridPosition position, int colourId)
        {
            _board.Occupy(position, colourId);
            CellChanged?.Invoke(position, colourId);
        }

        /// <summary>Empties one cell and announces it, exactly as <see cref="Occupy"/> announces a fill
        /// — the single-cell counterpart <see cref="ClearAll"/> does not offer. Debug tooling only
        /// today (see <see cref="Systems.DebugCheatSystem"/>); nothing in the ordinary rules ever
        /// empties one cell in isolation outside a line clear or a special effect, which go through
        /// <see cref="Core.Board"/> directly and notify through their own messages.</summary>
        internal void Clear(GridPosition position)
        {
            _board.Clear(position);
            CellChanged?.Invoke(position, Board.EMPTY);
        }

        /// <summary>Occupies a cell as a reinforced one and announces both halves of it — the block
        /// that appeared, then how much punishment it will take — in the order a View needs them, which
        /// is the same order <see cref="SetSpecialKind"/> establishes for a special cell's icon.
        /// Level-start seeding only; see <see cref="Core.Board.OccupyReinforced"/>.</summary>
        internal void OccupyReinforced(GridPosition position, int colourId, int hitCount)
        {
            _board.OccupyReinforced(position, colourId, hitCount);
            CellChanged?.Invoke(position, colourId);
            HitCountChanged?.Invoke(position, hitCount);
        }

        /// <summary>Occupies a cell as a <see cref="SpecialCellKind.Timer"/> cell and announces all three
        /// halves of it — the block, its kind, and its starting countdown — in the same order
        /// <see cref="OccupyReinforced"/> establishes for the reinforced-cell reads. Level-start seeding
        /// only; see <see cref="Core.Board.OccupyTimer"/>.</summary>
        internal void OccupyTimer(GridPosition position, int colourId, int startingCountdown)
        {
            _board.OccupyTimer(position, colourId, startingCountdown);
            CellChanged?.Invoke(position, colourId);
            SpecialKindChanged?.Invoke(position, SpecialCellKind.Timer);
            TimerCountdownChanged?.Invoke(position, startingCountdown);
        }

        /// <summary>Tags a cell with a special behaviour and announces it. Separate from
        /// <see cref="Occupy"/> exactly as <see cref="Core.Board.SetSpecialKind"/> is separate from
        /// <see cref="Core.Board.Occupy"/>: a spawner occupies a cell and then tags it, and the two
        /// steps notify independently.</summary>
        internal void SetSpecialKind(GridPosition position, SpecialCellKind kind)
        {
            _board.SetSpecialKind(position, kind);
            SpecialKindChanged?.Invoke(position, kind);
        }

        /// <summary>
        /// Raises change notifications for cells emptied by a resolver run. The Core resolver has
        /// already mutated the board when this is called.
        /// <para>
        /// Announces the cells of a cleared line that are <em>actually</em> empty now, read back off the
        /// board rather than assumed: a reinforced cell that spent a hit is still standing in a line
        /// that otherwise cleared, and telling a View it had emptied would fade a block that is still
        /// there. (The same read-back covers a cell an effect refilled before this ran — it reports as
        /// filled, which is what it is.)
        /// </para>
        /// </summary>
        internal void NotifyCleared(LineClearResult result)
        {
            for (int i = 0; i < result.ClearedRows.Count; i++)
            {
                int y = result.ClearedRows[i];
                for (int x = 0; x < _board.Width; x++)
                {
                    NotifyClearedCell(new GridPosition(x, y));
                }
            }

            for (int i = 0; i < result.ClearedColumns.Count; i++)
            {
                int x = result.ClearedColumns[i];
                for (int y = 0; y < _board.Height; y++)
                {
                    NotifyClearedCell(new GridPosition(x, y));
                }
            }
        }

        /// <summary>Announces one cell of a cleared line, if the clear really did empty it.</summary>
        private void NotifyClearedCell(GridPosition position)
        {
            if (_board.IsHole(position) || _board[position] != Board.EMPTY)
            {
                return;
            }

            CellChanged?.Invoke(position, Board.EMPTY);
        }

        /// <summary>
        /// Re-announces the remaining hit count of every reinforced cell still on the board, so a View
        /// repaints the ones a clear just damaged.
        /// <para>
        /// A scan rather than a list of exactly the cells that changed, deliberately. The Core
        /// resolvers and effects that spend hits are pure and report only what they
        /// <em>destroyed</em> — threading a second "and here is what survived" list out of every one of
        /// them, through the cascade loop and into three Systems, would be far more moving parts than
        /// the one thing it buys. This runs once per placement or power-up over the board's cells (64 on
        /// the standard board), never per frame, allocates nothing, and is idempotent: re-announcing an
        /// unchanged count repaints a cell exactly as it already looked.
        /// </para>
        /// </summary>
        internal void NotifyHitCountsRefreshed()
        {
            if (HitCountChanged == null)
            {
                return;
            }

            for (int y = 0; y < _board.Height; y++)
            {
                for (int x = 0; x < _board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    int hitCount = _board.GetHitCount(position);
                    if (hitCount > 0)
                    {
                        HitCountChanged.Invoke(position, hitCount);
                    }
                }
            }
        }

        /// <summary>
        /// Re-announces the remaining countdown of every <see cref="SpecialCellKind.Timer"/> cell still
        /// standing, so a View repaints the ones the per-placement tick just decremented. Mirrors
        /// <see cref="NotifyHitCountsRefreshed"/> exactly, including the "a scan, not a change list"
        /// reasoning: <see cref="Core.TimerCellTick"/> is pure and reports only the cells it converted
        /// (see <see cref="NotifyTimerCellsExpired"/>), not the ones that merely ticked down. Same cost
        /// class as that method: once per placement over the board's cells, never per frame.
        /// </summary>
        internal void NotifyTimerCountdownsRefreshed()
        {
            if (TimerCountdownChanged == null)
            {
                return;
            }

            for (int y = 0; y < _board.Height; y++)
            {
                for (int x = 0; x < _board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_board.GetSpecialKind(position) != SpecialCellKind.Timer)
                    {
                        continue;
                    }

                    TimerCountdownChanged.Invoke(position, _board.GetTimerCountdown(position));
                }
            }
        }

        /// <summary>Announces every cell <see cref="Core.TimerCellTick"/> just converted from
        /// <see cref="SpecialCellKind.Timer"/> to an ordinary cell this placement (issue #307 AC4/AC6a).
        /// The Core tick has already mutated the board when this is called.</summary>
        internal void NotifyTimerCellsExpired(IReadOnlyList<GridPosition> expiredCells)
        {
            if (expiredCells == null || TimerCellExpired == null)
            {
                return;
            }

            for (int i = 0; i < expiredCells.Count; i++)
            {
                TimerCellExpired.Invoke(expiredCells[i]);
            }
        }

        /// <summary>Raises the change notification for a cell a Core resolver has already occupied.
        /// Separate from <see cref="Occupy"/> because the resolver owns the mutation there — calling
        /// Occupy again would be a redundant second write of a cell that is already filled.</summary>
        internal void NotifyFilled(GridPosition position, int colourId)
        {
            CellChanged?.Invoke(position, colourId);
        }

        /// <summary>
        /// Re-announces whatever <see cref="SpecialCellKind"/> <paramref name="position"/> carries right
        /// now. For a Core effect that tags a cell directly on <see cref="Core.Board"/> — an explosive
        /// core's hand-off, exactly as a vortex's pull re-announces the kind it carried across at the
        /// destination — rather than through <see cref="SetSpecialKind"/>, which would tag it a second
        /// time.
        /// <para>
        /// A no-op when the cell reads back <see cref="SpecialCellKind.None"/>: <see cref="SpecialKindChanged"/>
        /// is an "added" signal only, so there is nothing to announce for a kind that was never actually
        /// applied.
        /// </para>
        /// </summary>
        internal void NotifySpecialKindChanged(GridPosition position)
        {
            SpecialCellKind kind = _board.GetSpecialKind(position);
            if (kind != SpecialCellKind.None)
            {
                SpecialKindChanged?.Invoke(position, kind);
            }
        }

        /// <summary>Raises change notifications for an arbitrary set of cells a power-up emptied. The
        /// Core resolver has already mutated the board when this is called. Separate from
        /// <see cref="NotifyCleared"/> because a power-up clears a region, not whole lines.</summary>
        internal void NotifyPowerUpCleared(IReadOnlyList<GridPosition> clearedCells)
        {
            if (clearedCells == null)
            {
                return;
            }

            for (int i = 0; i < clearedCells.Count; i++)
            {
                CellChanged?.Invoke(clearedCells[i], Board.EMPTY);
            }
        }

        /// <summary>
        /// Raises change notifications for the blocks a vortex dragged inwards. The Core effect has
        /// already mutated the board when this is called, so both ends are read back off it rather than
        /// assumed: a cell a later cascade phase went on to clear reports as empty, which is what it is.
        /// <para>
        /// Three notifications per move, in the order a View has to receive them: the source empties,
        /// the destination fills, and — only when the block that moved carried one — the destination
        /// regains its special kind. The kind needs its own signal because
        /// <see cref="SpecialKindChanged"/> is an "added" signal and a View drops a cell's icon whenever
        /// that cell empties; the move empties the source, so the icon has to be re-announced at the
        /// destination rather than assumed to have travelled with it.
        /// </para>
        /// </summary>
        internal void NotifyPulled(IReadOnlyList<VortexPull> pulls)
        {
            if (pulls == null)
            {
                return;
            }

            for (int i = 0; i < pulls.Count; i++)
            {
                VortexPull pull = pulls[i];

                CellChanged?.Invoke(pull.From, Board.EMPTY);
                CellChanged?.Invoke(pull.To, _board[pull.To]);

                SpecialCellKind kind = _board.GetSpecialKind(pull.To);
                if (kind != SpecialCellKind.None)
                {
                    SpecialKindChanged?.Invoke(pull.To, kind);
                }
            }
        }

        /// <summary>
        /// Empties the whole board for the start of a run.
        /// <para>
        /// Deliberately the unconditional <see cref="Core.Board.Clear"/> rather than
        /// <see cref="Core.Board.TryDamage"/>: a new run must not inherit anything from the last one, so
        /// a reinforced cell left over from it is wiped outright — hit count included — rather than
        /// given the chance to survive into a run that never authored it. The run's own reinforced cells
        /// are seeded afterwards, from the level, by <see cref="Systems.BoardSystem.StartNewRun"/>.
        /// </para>
        /// </summary>
        internal void ClearAll()
        {
            for (int y = 0; y < _board.Height; y++)
            {
                for (int x = 0; x < _board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_board[position] == Board.EMPTY)
                    {
                        continue;
                    }

                    _board.Clear(position);
                    CellChanged?.Invoke(position, Board.EMPTY);
                }
            }
        }
    }
}
