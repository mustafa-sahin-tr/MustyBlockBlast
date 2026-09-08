using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>The three offered pieces. A consumed slot holds <c>null</c> until the next refill.</summary>
    public sealed class TrayModel
    {
        public const int SLOT_COUNT = 3;

        private readonly Piece[] _pieces = new Piece[SLOT_COUNT];
        private readonly int[] _colourIds = new int[SLOT_COUNT];

        /// <summary>Raised with the slot index whose content changed.</summary>
        public event Action<int> SlotChanged;

        public Piece GetPiece(int slotIndex) => _pieces[slotIndex];

        public int GetColourId(int slotIndex) => _colourIds[slotIndex];

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < SLOT_COUNT; i++)
                {
                    if (_pieces[i] != null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        internal void SetSlot(int slotIndex, Piece piece, int colourId)
        {
            _pieces[slotIndex] = piece;
            _colourIds[slotIndex] = colourId;
            SlotChanged?.Invoke(slotIndex);
        }

        internal void ConsumeSlot(int slotIndex) => SetSlot(slotIndex, null, Board.EMPTY);

        /// <summary>Fills <paramref name="buffer"/> with the still-available pieces. The buffer is
        /// owned by the caller and reused, so this allocates nothing per call.</summary>
        internal void CollectRemaining(List<Piece> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if (_pieces[i] != null)
                {
                    buffer.Add(_pieces[i]);
                }
            }
        }
    }
}
