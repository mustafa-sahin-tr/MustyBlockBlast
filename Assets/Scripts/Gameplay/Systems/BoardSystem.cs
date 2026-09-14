using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using VContainer.Unity;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns the run: placement, line clearing, tray refill and game-over detection. All rules come
    /// from Core; this System only sequences them and publishes what happened.
    /// </summary>
    public sealed class BoardSystem : IStartable, IDisposable
    {
        /// <summary>How far (in cells) <see cref="ResolvePlacementAnchor"/> will search for a legal
        /// placement when the raw pointer anchor itself is illegal.</summary>
        private const int SnapSearchRadius = 2;

        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;
        private readonly WeightedPieceDraw _pieceDraw;
        private readonly PlacementSnapper _placementSnapper = new PlacementSnapper();
        private readonly IPublisher<RunStartedMessage> _runStartedPublisher;
        private readonly IPublisher<PiecePlacedMessage> _piecePlacedPublisher;
        private readonly IPublisher<LinesClearedMessage> _linesClearedPublisher;
        private readonly IPublisher<GameOverMessage> _gameOverPublisher;
        private readonly IPublisher<TrayRefilledMessage> _trayRefilledPublisher;
        // Sized for the three dock slots plus the parked piece, which CheckGameOver appends.
        private readonly List<Piece> _remainingBuffer = new List<Piece>(TrayModel.SLOT_COUNT + 1);
        private readonly Board _previewScratchBoard = new Board();
        private readonly List<int> _previewRowsBuffer = new List<int>(Board.SIZE);
        private readonly List<int> _previewColumnsBuffer = new List<int>(Board.SIZE);

        public BoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            WeightedPieceDraw pieceDraw,
            IPublisher<RunStartedMessage> runStartedPublisher,
            IPublisher<PiecePlacedMessage> piecePlacedPublisher,
            IPublisher<LinesClearedMessage> linesClearedPublisher,
            IPublisher<GameOverMessage> gameOverPublisher,
            IPublisher<TrayRefilledMessage> trayRefilledPublisher)
        {
            _boardModel = boardModel;
            _trayModel = trayModel;
            _pieceDraw = pieceDraw;
            _runStartedPublisher = runStartedPublisher;
            _piecePlacedPublisher = piecePlacedPublisher;
            _linesClearedPublisher = linesClearedPublisher;
            _gameOverPublisher = gameOverPublisher;
            _trayRefilledPublisher = trayRefilledPublisher;
        }

        public bool IsGameOver { get; private set; }

        void IStartable.Start() => StartNewRun();

        public void StartNewRun()
        {
            _boardModel.ClearAll();

            // A parked piece belongs to the run that parked it; carrying it into the next one would
            // hand the player a free piece they never drew.
            _trayModel.ClearHold();
            RefillTray();
            IsGameOver = false;
            _runStartedPublisher.Publish(new RunStartedMessage());
            CheckGameOver();
        }

        /// <summary>True when the tray piece in <paramref name="slotIndex"/> fits at
        /// <paramref name="anchor"/>. Used by the drag preview.</summary>
        public bool CanPlace(int slotIndex, GridPosition anchor)
        {
            if (IsGameOver || !IsValidSlot(slotIndex))
            {
                return false;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                return false;
            }

            return PlacementRules.CanPlace(_boardModel.Board, piece, anchor);
        }

        /// <summary>Non-mutating query: which rows/columns would clear if the tray piece in
        /// <paramref name="slotIndex"/> were placed at <paramref name="anchor"/>. Returns an empty result
        /// when the placement itself is illegal. Safe to call every drag-update frame — reuses internal
        /// scratch buffers rather than allocating. Used by the drag-preview highlight.</summary>
        public LineClearResult GetWouldClearLines(int slotIndex, GridPosition anchor)
        {
            if (IsGameOver || !IsValidSlot(slotIndex))
            {
                return EmptyPreview();
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                return EmptyPreview();
            }

            return LineClearResolver.PreviewClears(
                _boardModel.Board, piece, anchor, _previewScratchBoard, _previewRowsBuffer, _previewColumnsBuffer);
        }

        private LineClearResult EmptyPreview()
        {
            _previewRowsBuffer.Clear();
            _previewColumnsBuffer.Clear();
            return new LineClearResult(_previewRowsBuffer, _previewColumnsBuffer, 0, 0);
        }

        /// <summary>Starts a new placement-preview session by clearing any sticky lock left over
        /// from a previous drag. Call once when a drag begins.</summary>
        public void BeginPlacementPreview() => _placementSnapper.Reset();

        /// <summary>Resolves the anchor to preview/place at for this frame's raw pointer anchor,
        /// applying the sticky-lock and nearest-candidate snapping on top of it. <paramref name="isValid"/>
        /// is false only when no legal placement exists within the search radius.</summary>
        public GridPosition ResolvePlacementAnchor(int slotIndex, GridPosition rawAnchor, out bool isValid)
        {
            if (IsGameOver || !IsValidSlot(slotIndex))
            {
                isValid = false;
                return rawAnchor;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                isValid = false;
                return rawAnchor;
            }

            SnapResult result = _placementSnapper.Resolve(_boardModel.Board, piece, rawAnchor, SnapSearchRadius);
            isValid = result.IsValid;
            return result.Anchor;
        }

        /// <summary>Places the tray piece if legal, resolves clears, refills the tray when empty and
        /// re-checks game over. Returns false when the placement was illegal (nothing changed).</summary>
        public bool TryPlacePiece(int slotIndex, GridPosition anchor)
        {
            if (!CanPlace(slotIndex, anchor))
            {
                return false;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            int colourId = _trayModel.GetColourId(slotIndex);

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                _boardModel.Occupy(anchor + piece.Offsets[i], colourId);
            }

            _trayModel.ConsumeSlot(slotIndex);

            // Read before ResolveClears mutates the board — once a line clears, its cells are gone and
            // "how full was the board under this placement" can no longer be answered.
            int occupiedCellCountBeforeClear = _boardModel.Board.OccupiedCellCount();

            LineClearResult clearResult = LineClearResolver.ResolveClears(_boardModel.Board);
            if (clearResult.AnyCleared)
            {
                _boardModel.NotifyCleared(clearResult);
            }

            bool anyCornerCleared = AnyCornerTouched(clearResult.ClearedRows, clearResult.ClearedColumns);

            _piecePlacedPublisher.Publish(new PiecePlacedMessage(
                piece.Id, anchor, PieceFamilyClassifier.Classify(piece.Id), piece.CellCount, colourId,
                clearResult.LineCount, clearResult.ClearedRows.Count, clearResult.ClearedColumns.Count,
                clearResult.MonochromeLineCount, _boardModel.Board.IsEmpty(), occupiedCellCountBeforeClear,
                anyCornerCleared, _boardModel.Board.IsCenterCoreEmpty(), _boardModel.Board.HasIsolatedEmptyCells()));

            if (clearResult.AnyCleared)
            {
                _linesClearedPublisher.Publish(new LinesClearedMessage(
                    clearResult.ClearedRows, clearResult.ClearedColumns, clearResult.ClearedCellCount));
            }

            if (_trayModel.IsEmpty)
            {
                RefillTray();
            }

            CheckGameOver();
            return true;
        }

        /// <summary>
        /// Parks the dock piece in <paramref name="slotIndex"/> into the Hold slot, swapping it with
        /// whatever was already parked there. Atomic by construction: the vacated dock slot is
        /// overwritten with the previously held piece (or emptied) in the same call, so no action can
        /// ever leave two pieces in one slot or the same piece in two places.
        /// <para>
        /// This is explicitly <em>not</em> a placement. Nothing is put on the board, so nothing scores,
        /// no line can clear, the combo streak is neither advanced nor broken, and the tray is not
        /// refilled — the pieces involved were already drawn and are merely somewhere else now.
        /// </para>
        /// <para>
        /// Refused when it would leave the dock with nothing to drag. The Hold slot is fed from the
        /// dock and only ever emptied by the swap that refills it, so a dock emptied by parking could
        /// never be refilled (a refill is a placement's consequence) and the run would be stuck with no
        /// piece to move. A swap can never hit this case: the held piece takes the vacated slot.
        /// </para>
        /// Returns false when nothing changed.
        /// </summary>
        public bool TryHoldPiece(int slotIndex)
        {
            if (IsGameOver || !IsValidSlot(slotIndex))
            {
                return false;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                return false;
            }

            if (!_trayModel.IsHoldOccupied && _trayModel.OccupiedSlotCount == 1)
            {
                return false;
            }

            Piece previouslyHeld = _trayModel.HeldPiece;
            int previouslyHeldColourId = _trayModel.HeldColourId;

            _trayModel.SetHeld(piece, _trayModel.GetColourId(slotIndex));
            _trayModel.SetSlot(slotIndex, previouslyHeld, previouslyHeldColourId);

            // No game-over re-check: a park only permutes pieces between the dock and the pocket, so
            // the set of pieces the player can still play is exactly the one CheckGameOver last found
            // a move in.
            return true;
        }

        /// <summary>
        /// Ends the run for a reason the board cannot detect itself — currently only the timed-mode
        /// clock expiring — with the caller supplying that reason. Kept here so the game-over
        /// invariant has exactly one owner; callers must never set their own end-of-run state.
        /// Already-over runs are a no-op.
        /// </summary>
        public void ForceGameOver(GameOverReason reason)
        {
            if (IsGameOver)
            {
                return;
            }

            IsGameOver = true;
            _gameOverPublisher.Publish(new GameOverMessage(reason));
        }

        /// <summary>
        /// Re-runs the no-moves-left check after something outside this system changed which shapes the
        /// player holds — currently only the Rotate power-up, which swaps a dock slot's piece for
        /// another orientation of it.
        /// <para>
        /// Deliberately a request, not a verdict: the caller says "the tray's shapes changed", and this
        /// system alone decides whether that ends the run, so the game-over invariant keeps its single
        /// owner. Re-checking a run that is already over is a no-op.
        /// </para>
        /// </summary>
        internal void RecheckGameOver()
        {
            if (IsGameOver)
            {
                return;
            }

            CheckGameOver();
        }

        public void Dispose()
        {
        }

        private static bool IsValidSlot(int slotIndex)
            => slotIndex >= 0 && slotIndex < TrayModel.SLOT_COUNT;

        /// <summary>
        /// True when this clear touched a board corner. Clearing row 0 or row SIZE-1 alone already
        /// touches two corners (every cell in that row, including columns 0 and SIZE-1, is cleared);
        /// symmetrically for column 0/SIZE-1 — so checking membership of just these four indices,
        /// without cross-referencing specific (row, column) pairs, is sufficient.
        /// </summary>
        private static bool AnyCornerTouched(IReadOnlyList<int> clearedRows, IReadOnlyList<int> clearedColumns)
        {
            return ContainsEdgeIndex(clearedRows) || ContainsEdgeIndex(clearedColumns);
        }

        private static bool ContainsEdgeIndex(IReadOnlyList<int> indices)
        {
            for (int indexPosition = 0; indexPosition < indices.Count; indexPosition++)
            {
                if (indices[indexPosition] == 0 || indices[indexPosition] == Board.SIZE - 1)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefillTray()
        {
            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                _trayModel.SetSlot(i, _pieceDraw.DrawPiece(), _pieceDraw.DrawColourId());
            }

            // Published from here rather than from the two call sites, so the opening draw of a run
            // and every mid-run refill are indistinguishable to subscribers.
            _trayRefilledPublisher.Publish(new TrayRefilledMessage());
        }

        private void CheckGameOver()
        {
            _trayModel.CollectRemaining(_remainingBuffer);

            // The parked piece counts as a move the player still has. Swapping it back into a dock slot
            // is always legal and costs nothing, so a board where only the parked piece fits is not a
            // dead end — without this, pocketing the one piece that fits would end a run the player
            // could still play on from.
            if (_trayModel.HeldPiece != null)
            {
                _remainingBuffer.Add(_trayModel.HeldPiece);
            }

            if (MoveAvailability.HasAnyMove(_boardModel.Board, _remainingBuffer))
            {
                return;
            }

            IsGameOver = true;
            _gameOverPublisher.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));
        }
    }
}
