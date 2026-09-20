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

        /// <summary>The special behaviour each slot's piece carries, parallel to <see cref="_pieces"/>
        /// exactly as <see cref="_colourIds"/> is. A third array rather than a field on
        /// <see cref="Piece"/>: a piece is an immutable shared catalog entry, and the same 1x1 is
        /// offered plain in one slot and tagged in another.</summary>
        private readonly SpecialPieceKind[] _specialKinds = new SpecialPieceKind[SLOT_COUNT];

        /// <summary>The decorative skin each slot's piece carries, parallel to <see cref="_pieces"/>
        /// exactly as <see cref="_specialKinds"/> is (issue #324) — purely cosmetic, and only ever
        /// non-<see cref="CellSkinKind.None"/> in <see cref="MustyBlockBlast.Gameplay.GameMode.Timed"/>
        /// ("Classic"). Every cell of the piece renders the same skin; there is no per-sub-cell
        /// variation.</summary>
        private readonly CellSkinKind[] _cellSkins = new CellSkinKind[SLOT_COUNT];

        private Piece _heldPiece;
        private int _heldColourId = Board.EMPTY;
        private SpecialPieceKind _heldSpecialKind = SpecialPieceKind.None;

        /// <summary>Raised with the slot index whose content changed.</summary>
        public event Action<int> SlotChanged;

        /// <summary>Raised when the Hold slot's content changed. A plain model event, exactly like
        /// <see cref="SlotChanged"/>: only the View layer reacts to a park, and nothing in the run's
        /// rules does — a parked piece has not been played.</summary>
        public event Action HeldChanged;

        public Piece GetPiece(int slotIndex) => _pieces[slotIndex];

        public int GetColourId(int slotIndex) => _colourIds[slotIndex];

        /// <summary>The special behaviour the piece in <paramref name="slotIndex"/> carries.
        /// <see cref="SpecialPieceKind.None"/> for an ordinary drawn piece or an empty slot.</summary>
        public SpecialPieceKind GetSpecialKind(int slotIndex) => _specialKinds[slotIndex];

        /// <summary>The decorative skin the piece in <paramref name="slotIndex"/> carries.
        /// <see cref="CellSkinKind.None"/> for an ordinary drawn piece, an empty slot, or any piece
        /// drawn outside Classic mode.</summary>
        public CellSkinKind GetCellSkin(int slotIndex) => _cellSkins[slotIndex];

        /// <summary>The parked piece, or <c>null</c> when the Hold slot is empty.</summary>
        public Piece HeldPiece => _heldPiece;

        public int HeldColourId => _heldColourId;

        /// <summary>The special behaviour the parked piece carries. Carried across the park so a piece
        /// set aside comes back as the piece that went in, rather than silently losing its tag.</summary>
        public SpecialPieceKind HeldSpecialKind => _heldSpecialKind;

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

        /// <summary>
        /// Writes a slot. <paramref name="specialKind"/> defaults to
        /// <see cref="SpecialPieceKind.None"/> so every ordinary write — a refill, a reroll, a rotate —
        /// keeps its existing call shape and, in doing so, states the right thing: whatever was in the
        /// slot is gone, tag included. Only a deliberate injection names a kind.
        /// </summary>
        internal void SetSlot(
            int slotIndex, Piece piece, int colourId, SpecialPieceKind specialKind = SpecialPieceKind.None,
            CellSkinKind cellSkin = CellSkinKind.None)
        {
            _pieces[slotIndex] = piece;
            _colourIds[slotIndex] = colourId;
            _specialKinds[slotIndex] = piece == null ? SpecialPieceKind.None : specialKind;
            _cellSkins[slotIndex] = piece == null ? CellSkinKind.None : cellSkin;
            SlotChanged?.Invoke(slotIndex);
        }

        /// <summary>Empties a slot. The special kind goes with the piece — see <see cref="SetSlot"/>,
        /// which resets it for any write with no piece — so a consumed special piece can never leave its
        /// tag behind for whatever the next refill draws into the slot.</summary>
        internal void ConsumeSlot(int slotIndex) => SetSlot(slotIndex, null, Board.EMPTY);

        /// <summary>Parks <paramref name="piece"/>, replacing whatever was held. The piece is stored by
        /// reference, so its shape is carried over untouched — pieces are immutable and never rotated.</summary>
        internal void SetHeld(Piece piece, int colourId, SpecialPieceKind specialKind = SpecialPieceKind.None)
        {
            _heldPiece = piece;
            _heldColourId = piece == null ? Board.EMPTY : colourId;
            _heldSpecialKind = piece == null ? SpecialPieceKind.None : specialKind;
            HeldChanged?.Invoke();
        }

        internal void ClearHold() => SetHeld(null, Board.EMPTY);

        /// <summary>True when any <em>dock</em> slot currently offers <paramref name="kind"/>. Excludes
        /// the Hold slot, as <see cref="IsEmpty"/> does: the question every caller asks is "is one of
        /// these on offer right now".</summary>
        internal bool HasSpecialPiece(SpecialPieceKind kind)
        {
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if (_pieces[i] != null && _specialKinds[i] == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Index of the first consumed dock slot, or -1 when all three still hold a piece. Used
        /// by an injection to prefer a slot that costs the player nothing to overwrite.</summary>
        internal int FindFirstEmptySlot()
        {
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if (_pieces[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

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
