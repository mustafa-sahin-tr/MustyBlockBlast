using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #370 — the ad-gated no-moves rescue: when the Demolition Hammer cannot save a dead board,
    /// the <see cref="GameOverReason.NoMovesLeft"/> ending is marked rescue-available, and accepting
    /// replaces the dock with a fresh biased-solvable set instead of ending the run.
    /// </summary>
    public sealed class BoardSystemNoMovesRescueTests
    {
        /// <summary>A 3x3 hole in the corner of an otherwise full board: 55 of 64 cells occupied, which
        /// is below the hammer's 90% threshold, so a dead dock reaches the rescue rather than the
        /// hammer. Row 0 is three cells short, so a fresh set has an obvious line to clear.</summary>
        private const int CORNER_HOLE_SIZE = 3;

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private PowerUpModel _powerUpModel;
        private GameModeModel _gameModeModel;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<RunRescuedMessage> _runRescuedBroker;
        private TestMessageBroker<TrayRefilledMessage> _trayRefilledBroker;
        private CurrencyConfig _currencyConfig;

        [SetUp]
        public void CreateModels()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _powerUpModel = new PowerUpModel();
            _gameModeModel = new GameModeModel();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _runRescuedBroker = new TestMessageBroker<RunRescuedMessage>();
            _trayRefilledBroker = new TestMessageBroker<TrayRefilledMessage>();
            _currencyConfig = ScriptableObject.CreateInstance<CurrencyConfig>();
        }

        [TearDown]
        public void DestroyConfig()
        {
            if (_currencyConfig != null)
            {
                UnityEngine.Object.DestroyImmediate(_currencyConfig);
            }
        }

        // --- AC1: the hammer comes first ---

        /// <summary>AC1: a board that is both dead and at least 90% full gets the hammer, not the
        /// rescue — no ending is announced and the ad is never asked for.</summary>
        [Test]
        public void ADeadBoardTheHammerCanSave_IsReprievedByTheHammer_AndNeverOffersTheRescue()
        {
            var rewardSource = new StubRescueRewardSource(granted: true);
            BoardSystem system = CreateSystem(rewardSource);
            FillBoard();
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);

            system.RecheckGameOver();

            Assert.IsFalse(system.IsGameOver);
            Assert.AreEqual(0, _gameOverBroker.Published.Count);
            Assert.IsTrue(_trayModel.HasSpecialPiece(SpecialPieceKind.DemolitionHammer));
            Assert.AreEqual(0, rewardSource.RequestCount, "The hammer saved the run; no ad was needed.");
        }

        /// <summary>AC1: with the run's one hammer already spent, the same dead board falls through to
        /// the rescue.</summary>
        [Test]
        public void ADeadBoardAfterTheHammerIsSpent_EndsTheRunRescueAvailable()
        {
            BoardSystem system = CreateSystem(new StubRescueRewardSource(granted: true));
            FillBoard();
            system.RecheckGameOver();
            int hammerSlot = HammerSlot();
            Assert.IsTrue(system.TryUseDemolitionHammer(hammerSlot, new GridPosition(4, 4)));
            _boardModel.Occupy(new GridPosition(4, 4), 1);
            EmptyTheDock();

            system.RecheckGameOver();

            Assert.IsTrue(system.IsGameOver);
            Assert.AreEqual(1, _gameOverBroker.Published.Count);
            Assert.AreEqual(GameOverReason.NoMovesLeft, _gameOverBroker.Published[0].Reason);
            Assert.IsTrue(_gameOverBroker.Published[0].IsRescueAvailable);
        }

        // --- AC2: rescue-available on NoMovesLeft in every mode, and on nothing else ---

        [Test]
        public void ANoMovesEnding_IsRescueAvailable_InEveryMode(
            [Values(GameMode.Endless, GameMode.Timed, GameMode.Path)] GameMode mode)
        {
            _gameModeModel.CurrentMode.Value = mode;
            BoardSystem system = CreateSystem(new StubRescueRewardSource(granted: true));
            LayOutADeadBoardBelowTheHammerThreshold();

            system.RecheckGameOver();

            Assert.IsTrue(system.IsGameOver);
            Assert.AreEqual(GameOverReason.NoMovesLeft, _gameOverBroker.Published[0].Reason);
            Assert.IsTrue(_gameOverBroker.Published[0].IsRescueAvailable);
        }

        [Test]
        public void AForcedEnding_IsNeverRescueAvailable(
            [Values(GameOverReason.TimeUp, GameOverReason.ObjectiveMissed, GameOverReason.LevelCompleted)]
            GameOverReason reason)
        {
            BoardSystem system = CreateSystem(new StubRescueRewardSource(granted: true));

            system.ForceGameOver(reason);

            Assert.AreEqual(reason, _gameOverBroker.Published[0].Reason);
            Assert.IsFalse(_gameOverBroker.Published[0].IsRescueAvailable);
        }

        /// <summary>Without a source to pay for it there is no offer: every pre-#370 construction ends
        /// the run exactly as it always did.</summary>
        [Test]
        public void WithoutARewardSource_ANoMovesEnding_IsNotRescueAvailable()
        {
            BoardSystem system = CreateSystem(rewardSource: null);
            LayOutADeadBoardBelowTheHammerThreshold();

            system.RecheckGameOver();

            Assert.IsTrue(system.IsGameOver);
            Assert.IsFalse(_gameOverBroker.Published[0].IsRescueAvailable);
            Assert.IsFalse(Rescue(system));
        }

        // --- AC3: accepting replaces the dock and resumes the run ---

        [Test]
        public void AcceptingTheRescue_ReplacesTheDock_ResumesTheRun_AndPublishesNoSecondEnding()
        {
            var rewardSource = new StubRescueRewardSource(granted: true);
            BoardSystem system = CreateSystem(rewardSource);
            LayOutADeadBoardBelowTheHammerThreshold();
            Piece[] deadDock = SnapshotDock();
            system.RecheckGameOver();
            Assert.IsTrue(system.IsGameOver);

            Assert.IsTrue(Rescue(system));

            Assert.AreEqual(1, rewardSource.RequestCount);
            Assert.IsFalse(system.IsGameOver, "The fresh set has an obvious 1x1 fit; the run is live again.");
            Assert.AreEqual(1, _gameOverBroker.Published.Count, "No GameOverMessage is re-published for this attempt.");
            Assert.AreEqual(1, _runRescuedBroker.Published.Count);
            Assert.AreEqual(0, _trayRefilledBroker.Published.Count, "A rescue is a discard, not a refill.");
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.IsNotNull(_trayModel.GetPiece(slotIndex), "Every dock slot is dealt.");
                Assert.AreEqual(SpecialPieceKind.None, _trayModel.GetSpecialKind(slotIndex));
            }

            Assert.IsTrue(
                MoveAvailability.HasAnyMove(_boardModel.Board, SnapshotDock()),
                "The replacement set fits the board.");
            Assert.IsFalse(
                SameDock(deadDock, SnapshotDock()),
                "The dead set is gone, whole — nothing of it is worth keeping.");
        }

        [Test]
        public void AcceptingTheRescue_LeavesTheHoldSlotUntouched()
        {
            BoardSystem system = CreateSystem(new StubRescueRewardSource(granted: true));
            LayOutADeadBoardBelowTheHammerThreshold();
            Piece parked = FindPiece("line_h5");
            _trayModel.SetHeld(parked, 3);
            system.RecheckGameOver();
            Assert.IsTrue(system.IsGameOver, "A parked 1x5 fits nowhere in a 3x3 hole either.");

            Assert.IsTrue(Rescue(system));

            Assert.AreSame(parked, _trayModel.HeldPiece);
            Assert.AreEqual(3, _trayModel.HeldColourId);
        }

        // --- AC4: accepting on a board nothing fits ends the run again, rescue-available again ---

        [Test]
        public void AcceptingOnABoardNothingFits_EndsTheRunAgain_StillRescueAvailable()
        {
            var rewardSource = new StubRescueRewardSource(granted: true);
            BoardSystem system = CreateSystem(rewardSource);
            SpendTheHammerOnAFullBoard(system);
            EmptyTheDock();
            system.RecheckGameOver();
            Assert.IsTrue(system.IsGameOver);
            Assert.IsTrue(_gameOverBroker.Published[0].IsRescueAvailable);

            Assert.IsTrue(Rescue(system), "The ad was granted and the set applied, even though it did not save the run.");

            Assert.IsTrue(system.IsGameOver);
            Assert.AreEqual(1, _runRescuedBroker.Published.Count);
            Assert.AreEqual(2, _gameOverBroker.Published.Count);
            Assert.AreEqual(GameOverReason.NoMovesLeft, _gameOverBroker.Published[1].Reason);
            Assert.IsTrue(_gameOverBroker.Published[1].IsRescueAvailable, "Unlimited, not capped.");
            Assert.IsFalse(
                _trayModel.HasSpecialPiece(SpecialPieceKind.DemolitionHammer),
                "One hammer per run: a second dead end does not earn another.");

            // And the second offer is a real one: it can be taken up again.
            Assert.IsTrue(Rescue(system));
            Assert.AreEqual(2, rewardSource.RequestCount);
            Assert.AreEqual(3, _gameOverBroker.Published.Count);
        }

        // --- AC5: declining, refusing and erroring all leave the run ended ---

        [Test]
        public void ARefusedAd_LeavesTheRunEnded_WithTheDockUntouched()
        {
            var rewardSource = new StubRescueRewardSource(granted: false);
            BoardSystem system = CreateSystem(rewardSource);
            LayOutADeadBoardBelowTheHammerThreshold();
            Piece[] deadDock = SnapshotDock();
            system.RecheckGameOver();

            Assert.IsFalse(Rescue(system));

            Assert.AreEqual(1, rewardSource.RequestCount);
            Assert.IsTrue(system.IsGameOver);
            Assert.AreEqual(1, _gameOverBroker.Published.Count);
            Assert.AreEqual(0, _runRescuedBroker.Published.Count);
            Assert.IsTrue(SameDock(deadDock, SnapshotDock()));

            // Nothing left pending: the offer was consumed by the refusal.
            Assert.IsFalse(Rescue(system));
            Assert.AreEqual(1, rewardSource.RequestCount);
        }

        [Test]
        public void DecliningTheOffer_ConsumesIt_AndChangesNothingElse()
        {
            var rewardSource = new StubRescueRewardSource(granted: true);
            BoardSystem system = CreateSystem(rewardSource);
            LayOutADeadBoardBelowTheHammerThreshold();
            Piece[] deadDock = SnapshotDock();
            system.RecheckGameOver();

            system.DeclineNoMovesRescue();

            Assert.IsTrue(system.IsGameOver);
            Assert.IsFalse(Rescue(system), "A declined offer cannot be taken up afterwards.");
            Assert.AreEqual(0, rewardSource.RequestCount);
            Assert.AreEqual(1, _gameOverBroker.Published.Count);
            Assert.AreEqual(0, _runRescuedBroker.Published.Count);
            Assert.IsTrue(SameDock(deadDock, SnapshotDock()));
        }

        [Test]
        public void ASourceThatErrors_LeavesTheRunEnded_AndTheOfferConsumed()
        {
            var rewardSource = new StubRescueRewardSource(new InvalidOperationException("no fill"));
            BoardSystem system = CreateSystem(rewardSource);
            LayOutADeadBoardBelowTheHammerThreshold();
            system.RecheckGameOver();

            Assert.Throws<InvalidOperationException>(() => Rescue(system));

            Assert.IsTrue(system.IsGameOver);
            Assert.AreEqual(1, _gameOverBroker.Published.Count);
            Assert.AreEqual(0, _runRescuedBroker.Published.Count);
            Assert.IsFalse(Rescue(system));
            Assert.AreEqual(1, rewardSource.RequestCount);
        }

        [Test]
        public void ACancelledRequest_LeavesTheRunEnded()
        {
            var rewardSource = new StubRescueRewardSource(new OperationCanceledException());
            BoardSystem system = CreateSystem(rewardSource);
            LayOutADeadBoardBelowTheHammerThreshold();
            system.RecheckGameOver();

            Assert.IsFalse(Rescue(system));

            Assert.IsTrue(system.IsGameOver);
            Assert.AreEqual(0, _runRescuedBroker.Published.Count);
        }

        [Test]
        public void TheRescue_IsRefusedWhileTheRunIsLive()
        {
            var rewardSource = new StubRescueRewardSource(granted: true);
            BoardSystem system = CreateSystem(rewardSource);
            system.StartNewRun();
            Assert.IsFalse(system.IsGameOver);

            Assert.IsFalse(Rescue(system));
            Assert.AreEqual(0, rewardSource.RequestCount);
        }

        [Test]
        public void ANewRun_DropsTheOffer()
        {
            var rewardSource = new StubRescueRewardSource(granted: true);
            BoardSystem system = CreateSystem(rewardSource);
            LayOutADeadBoardBelowTheHammerThreshold();
            system.RecheckGameOver();
            Assert.IsTrue(system.IsGameOver);

            system.StartNewRun();
            system.ForceGameOver(GameOverReason.TimeUp);

            Assert.IsFalse(Rescue(system), "The old run's offer must not buy back a TimeUp ending.");
            Assert.AreEqual(0, rewardSource.RequestCount);
        }

        /// <summary>A second tap while the ad is still up must not open a second request.</summary>
        [Test]
        public void ASecondRequestWhileOneIsInFlight_IsRefused()
        {
            var rewardSource = new PendingRescueRewardSource();
            BoardSystem system = CreateSystem(rewardSource);
            LayOutADeadBoardBelowTheHammerThreshold();
            system.RecheckGameOver();

            UniTask<bool> first = system.TryApplyNoMovesRescueAsync(CancellationToken.None);
            Assert.AreEqual(1, rewardSource.RequestCount);

            Assert.IsFalse(Rescue(system));
            Assert.AreEqual(1, rewardSource.RequestCount);

            rewardSource.Complete(granted: true);
            Assert.IsTrue(first.GetAwaiter().GetResult());
            Assert.IsFalse(system.IsGameOver);
        }

        // --- AC6: no interaction with Reroll's inventory ---

        [Test]
        public void TheRescue_NeitherReadsNorSpendsRerollCharges(
            [Values(0, 3)] int rerollCount)
        {
            _powerUpModel.RerollCount.Value = rerollCount;
            var rewardSource = new StubRescueRewardSource(granted: true);
            BoardSystem system = CreateSystem(rewardSource);
            LayOutADeadBoardBelowTheHammerThreshold();
            system.RecheckGameOver();

            Assert.IsTrue(Rescue(system), "Available with no Reroll charges at all.");

            Assert.AreEqual(rerollCount, _powerUpModel.RerollCount.Value);
            Assert.AreEqual(1, rewardSource.RequestCount, "The ad is the whole price.");
        }

        // --- helpers ---

        private static bool Rescue(BoardSystem system)
            => system.TryApplyNoMovesRescueAsync(CancellationToken.None).GetAwaiter().GetResult();

        private BoardSystem CreateSystem(IRescueRewardSource rewardSource)
        {
            // Deliberately unstarted, as BoardSystemSpecialDockPieceTests is: StartNewRun would draw
            // over the board and dock each test lays out by hand.
            return new BoardSystem(
                _boardModel,
                _trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                _gameOverBroker,
                _trayRefilledBroker,
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                _currencyConfig,
                seed: 1,
                powerUpModel: _powerUpModel,
                gameModeModel: _gameModeModel,
                rescueRewardSource: rewardSource,
                runRescuedPublisher: _runRescuedBroker);
        }

        /// <summary>A full board except a 3x3 corner hole, and a dock of three 1x5 bars that cannot fit
        /// it: dead, and below the hammer's occupancy threshold.</summary>
        private void LayOutADeadBoardBelowTheHammerThreshold()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    if (x >= CORNER_HOLE_SIZE || y >= CORNER_HOLE_SIZE)
                    {
                        _boardModel.Occupy(new GridPosition(x, y), 1);
                    }
                }
            }

            Piece bar = FindPiece("line_h5");
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _trayModel.SetSlot(slotIndex, bar, 1);
            }

            Assert.IsFalse(MoveAvailability.HasAnyMove(_boardModel.Board, SnapshotDock()));
        }

        private void SpendTheHammerOnAFullBoard(BoardSystem system)
        {
            FillBoard();
            system.RecheckGameOver();
            int hammerSlot = HammerSlot();
            Assert.GreaterOrEqual(hammerSlot, 0);
            Assert.IsTrue(system.TryUseDemolitionHammer(hammerSlot, new GridPosition(4, 4)));
            _boardModel.Occupy(new GridPosition(4, 4), 1);
        }

        private void FillBoard()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    _boardModel.Occupy(new GridPosition(x, y), 1);
                }
            }
        }

        private void EmptyTheDock()
        {
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _trayModel.SetSlot(slotIndex, null, Board.EMPTY);
            }
        }

        private int HammerSlot()
        {
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                if (_trayModel.GetSpecialKind(slotIndex) == SpecialPieceKind.DemolitionHammer)
                {
                    return slotIndex;
                }
            }

            return -1;
        }

        private Piece[] SnapshotDock()
        {
            var dock = new Piece[TrayModel.SLOT_COUNT];
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                dock[slotIndex] = _trayModel.GetPiece(slotIndex);
            }

            return dock;
        }

        private static bool SameDock(Piece[] left, Piece[] right)
        {
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                if (!ReferenceEquals(left[slotIndex], right[slotIndex]))
                {
                    return false;
                }
            }

            return true;
        }

        private static Piece FindPiece(string id)
        {
            for (int pieceIndex = 0; pieceIndex < PieceCatalog.AllPieces.Count; pieceIndex++)
            {
                if (PieceCatalog.AllPieces[pieceIndex].Id == id)
                {
                    return PieceCatalog.AllPieces[pieceIndex];
                }
            }

            Assert.Fail($"Piece '{id}' not found in catalog.");
            return null;
        }

        /// <summary>Completes synchronously so these stay plain synchronous EditMode tests; counts the
        /// requests so a test can tell "refused before asking" from "asked and turned down".</summary>
        private sealed class StubRescueRewardSource : IRescueRewardSource
        {
            private readonly bool _granted;
            private readonly Exception _failure;

            internal StubRescueRewardSource(bool granted)
            {
                _granted = granted;
            }

            internal StubRescueRewardSource(Exception failure)
            {
                _failure = failure;
            }

            internal int RequestCount { get; private set; }

            public UniTask<RescueRewardResult> RequestRescueRewardAsync(CancellationToken cancellationToken)
            {
                RequestCount++;
                if (_failure != null)
                {
                    return UniTask.FromException<RescueRewardResult>(_failure);
                }

                return UniTask.FromResult(new RescueRewardResult(_granted));
            }
        }

        /// <summary>Stays pending until the test completes it, so the in-flight window is observable.</summary>
        private sealed class PendingRescueRewardSource : IRescueRewardSource
        {
            private UniTaskCompletionSource<RescueRewardResult> _pending;

            internal int RequestCount { get; private set; }

            public UniTask<RescueRewardResult> RequestRescueRewardAsync(CancellationToken cancellationToken)
            {
                RequestCount++;
                _pending = new UniTaskCompletionSource<RescueRewardResult>();
                return _pending.Task;
            }

            internal void Complete(bool granted) => _pending.TrySetResult(new RescueRewardResult(granted));
        }
    }
}
