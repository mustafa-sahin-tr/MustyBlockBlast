using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The three offered pieces. A consumed slot holds <c>null</c> until the next refill.
    /// <para>
    /// Alongside them sits the Hold slot ("pocket"): a single parked piece the player set aside. It is
    /// deliberately stored outside <see cref="_pieces"/>, so every question the refill rule asks — most
    /// of all <see cref="IsEmpty"/> — is answered by the three dock slots alone and a parked piece can
    /// never keep the dock looking occupied.
    /// </para>
    /// </summary>
    public sealed class TrayModel
    {
        public const int SLOT_COUNT = 3;

        private readonly Piece[] _pieces = new Piece[SLOT_COUNT];
        private readonly int[] _colourIds = new int[SLOT_COUNT];

        private Piece _heldPiece;
        private int _heldColourId = Board.EMPTY;

        /// <summary>Raised with the slot index whose content changed.</summary>
        public event Action<int> SlotChanged;

        /// <summary>Raised when the Hold slot's content changed. A plain model event, exactly like
        /// <see cref="SlotChanged"/>: only the View layer reacts to a park, and nothing in the run's
        /// rules does — a parked piece has not been played.</summary>
        public event Action HeldChanged;

        public Piece GetPiece(int slotIndex) => _pieces[slotIndex];

        public int GetColourId(int slotIndex) => _colourIds[slotIndex];

        /// <summary>The parked piece, or <c>null</c> when the Hold slot is empty.</summary>
        public Piece HeldPiece => _heldPiece;

        public int HeldColourId => _heldColourId;

        public bool IsHoldOccupied => _heldPiece != null;

        /// <summary>True when all three <em>dock</em> slots are consumed. A parked piece is not counted:
        /// it is not on offer, so it must not hold off the refill.</summary>
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

        /// <summary>How many dock slots still hold a piece. Excludes the Hold slot, as
        /// <see cref="IsEmpty"/> does.</summary>
        internal int OccupiedSlotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SLOT_COUNT; i++)
                {
                    if (_pieces[i] != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        internal void SetSlot(int slotIndex, Piece piece, int colourId)
        {
            _pieces[slotIndex] = piece;
            _colourIds[slotIndex] = colourId;
            SlotChanged?.Invoke(slotIndex);
        }

        internal void ConsumeSlot(int slotIndex) => SetSlot(slotIndex, null, Board.EMPTY);

        /// <summary>Parks <paramref name="piece"/>, replacing whatever was held. The piece is stored by
        /// reference, so its shape is carried over untouched — pieces are immutable and never rotated.</summary>
        internal void SetHeld(Piece piece, int colourId)
        {
            _heldPiece = piece;
            _heldColourId = piece == null ? Board.EMPTY : colourId;
            HeldChanged?.Invoke();
        }

        internal void ClearHold() => SetHeld(null, Board.EMPTY);

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
