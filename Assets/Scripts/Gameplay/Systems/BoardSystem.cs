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
        private readonly List<Piece> _remainingBuffer = new List<Piece>(TrayModel.SLOT_COUNT);

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

            LineClearResult clearResult = LineClearResolver.ResolveClears(_boardModel.Board);
            if (clearResult.AnyCleared)
            {
                _boardModel.NotifyCleared(clearResult);
            }

            _piecePlacedPublisher.Publish(new PiecePlacedMessage(
                piece.Id, anchor, piece.CellCount, colourId, clearResult.LineCount));

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

        public void Dispose()
        {
        }

        private static bool IsValidSlot(int slotIndex)
            => slotIndex >= 0 && slotIndex < TrayModel.SLOT_COUNT;

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
            if (MoveAvailability.HasAnyMove(_boardModel.Board, _remainingBuffer))
            {
                return;
            }

            IsGameOver = true;
            _gameOverPublisher.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));
        }
    }
}
