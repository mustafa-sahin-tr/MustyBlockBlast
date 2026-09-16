using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Observable wrapper around the pure-C# <see cref="Core.Board"/>. Holds state only; every
    /// rule (placement legality, clearing, game over) lives in Core and is driven by Systems.
    /// Mutation is internal so only Gameplay Systems can change it.
    /// </summary>
    public sealed class BoardModel
    {
        private readonly Board _board = new Board();

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

        public int Size => Board.SIZE;

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
                for (int x = 0; x < Board.SIZE; x++)
                {
                    CellChanged?.Invoke(new GridPosition(x, y), Board.EMPTY);
                }
            }

            for (int i = 0; i < result.ClearedColumns.Count; i++)
            {
                int x = result.ClearedColumns[i];
                for (int y = 0; y < Board.SIZE; y++)
                {
                    CellChanged?.Invoke(new GridPosition(x, y), Board.EMPTY);
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

        internal void ClearAll()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
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
