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

        /// <summary>
        /// The diamond decoration each slot's piece carries (issue #394), parallel to <see cref="_pieces"/>
        /// exactly as <see cref="_specialKinds"/> is, and stored outside <see cref="Piece"/> for the same
        /// reason: a piece is an immutable shared catalog entry, and the same L is offered plain in one
        /// slot and decorated in another. Per slot, one entry per <see cref="Piece.Offsets"/> index —
        /// the gem's colour id, or <see cref="NO_DIAMOND"/> for an ordinary cell.
        /// <para>
        /// Each buffer is grown to the largest piece the slot has ever held and then reused, so a
        /// refill allocates nothing once every slot has seen the catalog's biggest shape. Entries past
        /// the current piece's cell count are always <see cref="NO_DIAMOND"/>.
        /// </para>
        /// </summary>
        private readonly int[][] _diamondColourIds = new int[SLOT_COUNT][];

        private Piece _heldPiece;
        private int _heldColourId = Board.EMPTY;
        private SpecialPieceKind _heldSpecialKind = SpecialPieceKind.None;
        private int[] _heldDiamondColourIds = System.Array.Empty<int>();

        /// <summary>The decoration value of an ordinary cell: no gem. Same sentinel as
        /// <see cref="Board.GetDiamondColourId"/> reports for a board cell carrying no diamond.</summary>
        public const int NO_DIAMOND = 0;

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

        /// <summary>The colour of the diamond riding on cell <paramref name="offsetIndex"/> (an index
        /// into <see cref="Piece.Offsets"/>) of the piece in <paramref name="slotIndex"/>, or
        /// <see cref="NO_DIAMOND"/> for an ordinary cell, an empty slot, or an index past the piece.</summary>
        public int GetDiamondColourId(int slotIndex, int offsetIndex)
            => ReadDiamondColourId(_diamondColourIds[slotIndex], offsetIndex);

        /// <summary>True when any cell of the piece in <paramref name="slotIndex"/> carries a diamond.
        /// The cheap "is there anything to draw/place" check, so callers need not walk every offset.</summary>
        public bool HasDiamonds(int slotIndex) => HasAnyDiamond(_diamondColourIds[slotIndex]);

        /// <summary>
        /// The whole decoration of <paramref name="slotIndex"/>, indexed by <see cref="Piece.Offsets"/>
        /// position, for handing to a <see cref="SetSlot"/>/<see cref="SetHeld"/> that carries the piece
        /// somewhere else. May be longer than the piece; every entry past its cell count is
        /// <see cref="NO_DIAMOND"/>. A view onto this model's own buffer — read it before the slot is
        /// rewritten, never keep it.
        /// </summary>
        internal IReadOnlyList<int> GetDiamondColourIds(int slotIndex)
            => _diamondColourIds[slotIndex] ?? System.Array.Empty<int>();

        /// <summary>The parked piece, or <c>null</c> when the Hold slot is empty.</summary>
        public Piece HeldPiece => _heldPiece;

        public int HeldColourId => _heldColourId;

        /// <summary>The special behaviour the parked piece carries. Carried across the park so a piece
        /// set aside comes back as the piece that went in, rather than silently losing its tag.</summary>
        public SpecialPieceKind HeldSpecialKind => _heldSpecialKind;

        /// <summary>The Hold-slot counterpart of <see cref="GetDiamondColourId"/>, carried across the
        /// park for the reason <see cref="HeldSpecialKind"/> is.</summary>
        public int GetHeldDiamondColourId(int offsetIndex) => ReadDiamondColourId(_heldDiamondColourIds, offsetIndex);

        /// <summary>The Hold-slot counterpart of <see cref="HasDiamonds"/>.</summary>
        public bool HasHeldDiamonds => HasAnyDiamond(_heldDiamondColourIds);

        /// <summary>The Hold-slot counterpart of <see cref="GetDiamondColourIds"/>, with the same
        /// "read before rewriting, never keep" contract.</summary>
        internal IReadOnlyList<int> HeldDiamondColourIds => _heldDiamondColourIds;

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
        /// <param name="diamondColourIds">The diamond decoration the piece carries, one entry per
        /// <see cref="Piece.Offsets"/> index (issue #394); null — the default every ordinary write keeps
        /// — means "no diamonds", exactly as the default <paramref name="specialKind"/> means "no tag".
        /// Copied, never kept, so a caller may hand in a buffer it is about to reuse.</param>
        internal void SetSlot(
            int slotIndex,
            Piece piece,
            int colourId,
            SpecialPieceKind specialKind = SpecialPieceKind.None,
            IReadOnlyList<int> diamondColourIds = null)
        {
            _pieces[slotIndex] = piece;
            _colourIds[slotIndex] = colourId;
            _specialKinds[slotIndex] = piece == null ? SpecialPieceKind.None : specialKind;
            WriteDiamondColourIds(ref _diamondColourIds[slotIndex], piece, diamondColourIds);
            SlotChanged?.Invoke(slotIndex);
        }

        /// <summary>Empties a slot. The special kind goes with the piece — see <see cref="SetSlot"/>,
        /// which resets it for any write with no piece — so a consumed special piece can never leave its
        /// tag behind for whatever the next refill draws into the slot.</summary>
        internal void ConsumeSlot(int slotIndex) => SetSlot(slotIndex, null, Board.EMPTY);

        /// <summary>Parks <paramref name="piece"/>, replacing whatever was held. The piece is stored by
        /// reference, so its shape is carried over untouched — pieces are immutable and never rotated.</summary>
        internal void SetHeld(
            Piece piece,
            int colourId,
            SpecialPieceKind specialKind = SpecialPieceKind.None,
            IReadOnlyList<int> diamondColourIds = null)
        {
            _heldPiece = piece;
            _heldColourId = piece == null ? Board.EMPTY : colourId;
            _heldSpecialKind = piece == null ? SpecialPieceKind.None : specialKind;
            WriteDiamondColourIds(ref _heldDiamondColourIds, piece, diamondColourIds);
            HeldChanged?.Invoke();
        }

        /// <summary>
        /// Copies <paramref name="source"/>'s first <c>piece.CellCount</c> entries into
        /// <paramref name="buffer"/> and zeroes the rest, growing the buffer only when the piece is
        /// bigger than anything it has held. No piece, or no decoration, leaves every entry
        /// <see cref="NO_DIAMOND"/> — so a consumed or plain slot can never hand a stale gem to whatever
        /// the next refill draws into it (the same guarantee <see cref="SetSlot"/> gives the tag).
        /// </summary>
        private static void WriteDiamondColourIds(ref int[] buffer, Piece piece, IReadOnlyList<int> source)
        {
            int cellCount = piece == null ? 0 : piece.CellCount;
            if (buffer == null || buffer.Length < cellCount)
            {
                buffer = new int[cellCount];
            }
            else
            {
                System.Array.Clear(buffer, 0, buffer.Length);
            }

            if (source == null)
            {
                return;
            }

            int copyCount = source.Count < cellCount ? source.Count : cellCount;
            for (int offsetIndex = 0; offsetIndex < copyCount; offsetIndex++)
            {
                buffer[offsetIndex] = source[offsetIndex];
            }
        }

        private static int ReadDiamondColourId(int[] buffer, int offsetIndex)
        {
            if (buffer == null || offsetIndex < 0 || offsetIndex >= buffer.Length)
            {
                return NO_DIAMOND;
            }

            return buffer[offsetIndex];
        }

        private static bool HasAnyDiamond(int[] buffer)
        {
            if (buffer == null)
            {
                return false;
            }

            for (int offsetIndex = 0; offsetIndex < buffer.Length; offsetIndex++)
            {
                if (buffer[offsetIndex] != NO_DIAMOND)
                {
                    return true;
                }
            }

            return false;
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
