using System;
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

        public int Size => Board.SIZE;

        /// <summary>Read-only access for Views.</summary>
        public int GetCell(GridPosition position) => _board[position];

        /// <summary>Core board handed to the stateless Core rule helpers. Systems only.</summary>
        internal Board Board => _board;

        internal void Occupy(GridPosition position, int colourId)
        {
            _board.Occupy(position, colourId);
            CellChanged?.Invoke(position, colourId);
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
