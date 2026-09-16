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

        /// <summary>Core board handed to the stateless Core rule helpers. Systems only.</summary>
        internal Board Board => _board;

        internal void Occupy(GridPosition position, int colourId)
        {
            _board.Occupy(position, colourId);
            CellChanged?.Invoke(position, colourId);
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

        /// <summary>Raises change notifications for cells emptied by a resolver run. The Core
        /// resolver has already mutated the board when this is called.</summary>
        internal void NotifyCleared(LineClearResult result)
        {
            for (int i = 0; i < result.ClearedRows.Count; i++)
            {
                int y = result.ClearedRows[i];
                for (int x = 0; x < _board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_board.IsHole(position))
                    {
                        continue;
                    }

                    CellChanged?.Invoke(position, Board.EMPTY);
                }
            }

            for (int i = 0; i < result.ClearedColumns.Count; i++)
            {
                int x = result.ClearedColumns[i];
                for (int y = 0; y < _board.Height; y++)
                {
                    var position = new GridPosition(x, y);
                    if (_board.IsHole(position))
                    {
                        continue;
                    }

                    CellChanged?.Invoke(position, Board.EMPTY);
                }
            }
        }

        /// <summary>Raises the change notification for a cell a Core resolver has already occupied.
        /// Separate from <see cref="Occupy"/> because the resolver owns the mutation there — calling
        /// Occupy again would be a redundant second write of a cell that is already filled.</summary>
        internal void NotifyFilled(GridPosition position, int colourId)
        {
            CellChanged?.Invoke(position, colourId);
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
