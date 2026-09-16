using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="GhostFitModel"/>: running the Ghost Fit search and holding — or dropping — the
    /// suggestion it produced. <see cref="PowerUpSystem"/> spends the charge and calls in here, exactly
    /// as it calls <see cref="DoubleMultiplierSystem"/> for the 2x window, so the inventory keeps its
    /// single spender.
    /// <para>
    /// The suggestion has no clock. Unlike the 2x window it is not a timed effect at all: it is a
    /// statement about the board as it stands, and it stays on screen until either the player acts or
    /// the thing it describes stops being true. So this class is not an <c>ITickable</c> — it only ever
    /// reacts.
    /// </para>
    /// <para>
    /// A suggestion is never refreshed in place, always dropped: it is computed from the board and dock
    /// at the moment it is asked for, and every reachable way either of those can change afterwards ends
    /// in a dismissal here — a placement (<see cref="PiecePlacedMessage"/>), another power-up's
    /// application (<see cref="PowerUpAppliedMessage"/>, which covers Bomb and friends mutating the
    /// board as well as Reroll and Rotate rewriting the dock), and both run boundaries. Nothing else in
    /// the game touches board or tray, so a stale suggestion cannot outlive what it described.
    /// </para>
    /// <para>
    /// The player's own input dismisses it too, but that arrives as a direct <see cref="Dismiss"/> call
    /// from the input View rather than as a message: "the player touched the board" is not an event the
    /// rest of the game has any reason to hear about.
    /// </para>
    /// </summary>
    public sealed class GhostFitSystem : IDisposable
    {
        private readonly GhostFitModel _ghostFitModel;
        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;
        private readonly ScoreModel _scoreModel;
        private readonly GhostFitSearch _search = new GhostFitSearch();

        // The three dock slots as the search wants them: index-addressable, nulls and all, so a slot
        // index in the result is a dock slot index. Owned and reused, so a search allocates nothing.
        private readonly Piece[] _dockBuffer = new Piece[TrayModel.SLOT_COUNT];

        private readonly IDisposable _subscriptions;

        public GhostFitSystem(
            GhostFitModel ghostFitModel,
            BoardModel boardModel,
            TrayModel trayModel,
            ScoreModel scoreModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<PowerUpAppliedMessage> powerUpAppliedSubscriber)
        {
            _ghostFitModel = ghostFitModel;
            _boardModel = boardModel;
            _trayModel = trayModel;
            _scoreModel = scoreModel;

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            gameOverSubscriber.Subscribe(OnGameOver).AddTo(bag);
            piecePlacedSubscriber.Subscribe(OnPiecePlaced).AddTo(bag);
            powerUpAppliedSubscriber.Subscribe(OnPowerUpApplied).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        /// <summary>
        /// Searches the current board and dock for the optimal move and shows it. Returns false — with
        /// the hint left reporting <see cref="GhostFitHintState.NoPlacements"/> rather than cleared —
        /// when no dock piece fits anywhere. That is the "no placements possible" answer the caller
        /// refuses to charge for: a report, not a failure.
        /// </summary>
        internal bool TryShowSuggestion()
        {
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                // A hammer is aimed at an occupied cell, never dropped on the board, and
                // BoardSystem.CanPlace refuses it outright — so offering it as the best move would be a
                // suggestion the player cannot act on. Handed to the search as an empty slot instead,
                // which it already knows to skip.
                _dockBuffer[slotIndex] =
                    _trayModel.GetSpecialKind(slotIndex) == SpecialPieceKind.DemolitionHammer
                        ? null
                        : _trayModel.GetPiece(slotIndex);
            }

            // The streak, not the score: ranking criterion (2) is about a combo that is currently
            // running, which is exactly what a non-zero streak means.
            bool isStreakActive = _scoreModel.Streak.Value > 0;

            if (!_search.TryFindBestMove(_boardModel.Board, _dockBuffer, isStreakActive, out GhostFitMove move))
            {
                _ghostFitModel.Hint.Value = GhostFitHint.NoPlacements();
                return false;
            }

            _ghostFitModel.Hint.Value = GhostFitHint.ForMove(move);
            return true;
        }

        /// <summary>Drops whatever the hint was saying. The single exit — every dismissal reason lands
        /// here — and a no-op when there was nothing on screen.</summary>
        public void Dismiss() => _ghostFitModel.Hint.Value = GhostFitHint.None;

        /// <summary>
        /// Dismisses unless the player is reaching for the very piece being suggested. Picking up a
        /// different piece is the acceptance criterion's "begins dragging a different piece" and drops
        /// the hint at once; picking up the suggested one is the player acting <em>on</em> the hint, so
        /// the silhouette stays up for them to aim at and is dropped by the placement that follows.
        /// </summary>
        public void DismissUnlessSuggestedSlot(int slotIndex)
        {
            if (_ghostFitModel.IsSuggesting && _ghostFitModel.SuggestedSlotIndex == slotIndex)
            {
                return;
            }

            Dismiss();
        }

        private void OnRunStarted(RunStartedMessage message) => Dismiss();

        private void OnGameOver(GameOverMessage message) => Dismiss();

        private void OnPiecePlaced(PiecePlacedMessage message) => Dismiss();

        /// <summary>
        /// Any other power-up's application invalidates the suggestion — it changed the board or the
        /// dock the suggestion was computed from. Ghost Fit's own application is the exception it must
        /// skip: that message is published right after the hint was set, and dismissing on it would
        /// erase the suggestion the tap just paid for.
        /// </summary>
        private void OnPowerUpApplied(PowerUpAppliedMessage message)
        {
            if (message.Kind == PowerUpKind.GhostFit)
            {
                return;
            }

            Dismiss();
        }
    }
}
