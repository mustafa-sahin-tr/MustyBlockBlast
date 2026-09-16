using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the Hold slot ("pocket"): parking a dock piece, swapping with an already-parked one, and
    /// the two things a park must deliberately <em>not</em> do — hold off the tray refill, or look like
    /// a placement to the scoring rules.
    /// </summary>
    public class BoardSystemHoldSlotTests
    {
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        private static readonly Piece HorizontalPair = new Piece(
            "test_pair_h", new[] { new GridPosition(0, 0), new GridPosition(1, 0) });

        private static readonly Piece VerticalTriple = new Piece(
            "test_triple_v",
            new[] { new GridPosition(0, 0), new GridPosition(0, 1), new GridPosition(0, 2) });

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private TestMessageBroker<TrayRefilledMessage> _trayRefilledBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _trayRefilledBroker = new TestMessageBroker<TrayRefilledMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();

            // Deliberately unstarted: StartNewRun would draw three random pieces over the dock each
            // test lays out by hand. IsGameOver is false until a run is started or checked.
            _system = new BoardSystem(
                _boardModel,
                _trayModel,
                new PerfectRoundModel(),
                new WeightedPieceDraw(seed: 1),
                _runStartedBroker,
                _piecePlacedBroker,
                new TestMessageBroker<LinesClearedMessage>(),
                _gameOverBroker,
                _trayRefilledBroker,
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>());
        }

        // --- AC1: parking into an empty pocket ---

        [Test]
        public void TryHoldPiece_WithAnEmptyHold_EmptiesThatDockSlot()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);

            bool held = _system.TryHoldPiece(1);

            Assert.IsTrue(held);
            Assert.IsNull(_trayModel.GetPiece(1));
            Assert.AreEqual(Board.EMPTY, _trayModel.GetColourId(1));
            Assert.AreSame(Single, _trayModel.GetPiece(0), "The other dock slots must be untouched.");
            Assert.AreSame(VerticalTriple, _trayModel.GetPiece(2), "The other dock slots must be untouched.");
        }

        [Test]
        public void TryHoldPiece_WithAnEmptyHold_ParksTheSamePieceAndColourUnchanged()
        {
            FillDock(Single, VerticalTriple, HorizontalPair);
            _trayModel.SetSlot(1, VerticalTriple, 7);

            _system.TryHoldPiece(1);

            Assert.IsTrue(_trayModel.IsHoldOccupied);
            // Same instance, so the shape and its orientation cannot have been rebuilt or normalised.
            Assert.AreSame(VerticalTriple, _trayModel.HeldPiece);
            CollectionAssert.AreEqual(VerticalTriple.Offsets, _trayModel.HeldPiece.Offsets);
            Assert.AreEqual(7, _trayModel.HeldColourId);
        }

        // --- AC2: parking into an occupied pocket swaps, atomically ---

        [Test]
        public void TryHoldPiece_WithAnOccupiedHold_SwapsTheTwoPieces()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);
            _system.TryHoldPiece(0);

            bool swapped = _system.TryHoldPiece(2);

            Assert.IsTrue(swapped);
            Assert.AreSame(VerticalTriple, _trayModel.HeldPiece);
            Assert.AreEqual(3, _trayModel.HeldColourId);
            Assert.AreSame(Single, _trayModel.GetPiece(2), "The previously held piece takes the vacated slot.");
            Assert.AreEqual(1, _trayModel.GetColourId(2), "The previously held colour comes back with it.");
        }

        [Test]
        public void TryHoldPiece_WithAnOccupiedHold_NeverLeavesTheDockHoldingBothPieces()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);
            _system.TryHoldPiece(0);
            int dockCountBefore = CountDockPieces();

            _system.TryHoldPiece(2);

            // A swap is one piece in, one piece out — the dock count cannot move, and neither piece
            // may end up in two places at once.
            Assert.AreEqual(dockCountBefore, CountDockPieces());
            CollectionAssert.AreEquivalent(
                new[] { Single, HorizontalPair, VerticalTriple }, CollectEveryPieceInPlay());
        }

        // --- AC3: the pocket is invisible to the refill rule ---

        [Test]
        public void TryHoldPiece_DoesNotRefillTheTray()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);

            _system.TryHoldPiece(0);

            Assert.AreEqual(0, _trayRefilledBroker.Published.Count);
            Assert.IsNull(_trayModel.GetPiece(0), "The vacated slot must stay empty until a real refill.");
        }

        [Test]
        public void IsEmpty_WithAParkedPieceAndNoDockPieces_IsStillTrue()
        {
            // The dock is what the refill rule asks about. A piece sitting in the pocket must not make
            // the dock look occupied.
            _trayModel.SetSlot(0, Single, 1);
            _trayModel.SetSlot(1, HorizontalPair, 1);
            _system.TryHoldPiece(0);
            _trayModel.ConsumeSlot(1);

            Assert.IsTrue(_trayModel.IsHoldOccupied);
            Assert.IsTrue(_trayModel.IsEmpty);
        }

        [Test]
        public void TryPlacePiece_EmptyingTheLastDockSlot_StillRefills_EvenWithAnOccupiedHold()
        {
            // The negative case for AC3: the refill must not be held off because the pocket is full.
            FillDock(Single, Single, Single);
            _system.TryHoldPiece(0);
            _system.TryPlacePiece(1, new GridPosition(0, 0));

            bool placed = _system.TryPlacePiece(2, new GridPosition(1, 0));

            Assert.IsTrue(placed);
            Assert.AreEqual(1, _trayRefilledBroker.Published.Count);
            Assert.IsFalse(_trayModel.IsEmpty, "The refill must have restocked all three dock slots.");
        }

        // --- AC4: a park is not a placement ---

        [Test]
        public void TryHoldPiece_PublishesNoPlacement()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);

            _system.TryHoldPiece(0);
            _system.TryHoldPiece(1);

            Assert.AreEqual(0, _piecePlacedBroker.Published.Count);
        }

        [Test]
        public void TryHoldPiece_LeavesTheScoreAndStreakUntouched()
        {
            // Wired to a live ScoreSystem on the very broker BoardSystem publishes placements to: any
            // placement reaching it would reset a streak this park must preserve.
            var scoreModel = new ScoreModel();
            using ScoreSystem scoreSystem = CreateScoreSystem(scoreModel);
            scoreModel.Streak.Value = 4;
            scoreModel.Score.Value = 250;

            // Seeded non-zero on purpose: a placement reaching ScoreSystem with no lines cleared resets
            // Streak outright and leaves MultiClearStreak frozen, so only a non-zero starting value can
            // tell "the park was ignored" apart from "the field happened to already be 0".
            scoreModel.MultiClearStreak.Value = 3;
            FillDock(Single, HorizontalPair, VerticalTriple);

            _system.TryHoldPiece(0);
            _system.TryHoldPiece(1);

            Assert.AreEqual(4, scoreModel.Streak.Value);
            Assert.AreEqual(250, scoreModel.Score.Value);
            Assert.AreEqual(3, scoreModel.MultiClearStreak.Value);
        }

        // --- Guards ---

        [Test]
        public void TryHoldPiece_WithAnOutOfRangeSlot_ChangesNothing()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);

            Assert.IsFalse(_system.TryHoldPiece(-1));
            Assert.IsFalse(_system.TryHoldPiece(TrayModel.SLOT_COUNT));
            Assert.IsFalse(_trayModel.IsHoldOccupied);
        }

        [Test]
        public void TryHoldPiece_OnAnAlreadyConsumedSlot_ChangesNothing()
        {
            _trayModel.SetSlot(0, Single, 1);
            _trayModel.SetSlot(1, HorizontalPair, 1);

            Assert.IsFalse(_system.TryHoldPiece(2));
            Assert.IsFalse(_trayModel.IsHoldOccupied);
        }

        [Test]
        public void TryHoldPiece_WithTheLastDockPieceAndAnEmptyHold_IsRefused()
        {
            // Parking it would leave nothing to drag and no way to refill — the dock is only ever
            // restocked as a placement's consequence.
            _trayModel.SetSlot(0, Single, 1);

            bool held = _system.TryHoldPiece(0);

            Assert.IsFalse(held);
            Assert.AreSame(Single, _trayModel.GetPiece(0));
            Assert.IsFalse(_trayModel.IsHoldOccupied);
        }

        [Test]
        public void TryHoldPiece_WithTheLastDockPieceButAnOccupiedHold_IsAllowed()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);
            _system.TryHoldPiece(0);
            _trayModel.ConsumeSlot(1);

            bool held = _system.TryHoldPiece(2);

            Assert.IsTrue(held, "A swap hands the vacated slot the previously held piece, so it is safe.");
            Assert.AreSame(VerticalTriple, _trayModel.HeldPiece);
            Assert.AreSame(Single, _trayModel.GetPiece(2));
        }

        [Test]
        public void TryHoldPiece_OnceTheRunIsOver_ChangesNothing()
        {
            // Parking mirrors TryPlacePiece/CanPlace: an ended run accepts no further moves. Safe as a
            // blanket refusal because a park is a permutation of dock and pocket, so it cannot change
            // whether a move exists — an over run can never be rescued by one.
            FillDock(Single, HorizontalPair, VerticalTriple);
            _system.ForceGameOver(GameOverReason.TimeUp);

            bool held = _system.TryHoldPiece(0);

            Assert.IsFalse(held);
            Assert.IsFalse(_trayModel.IsHoldOccupied);
            Assert.AreSame(Single, _trayModel.GetPiece(0), "The dock must be left exactly as it was.");
        }

        // --- The model event the pocket's View repaints from ---

        [Test]
        public void HeldChanged_FiresOnEveryTransitionTheViewMustRepaintFor()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);
            int raised = 0;
            _trayModel.HeldChanged += () => raised++;

            _system.TryHoldPiece(0);
            Assert.AreEqual(1, raised, "Parking into an empty pocket must repaint it.");

            _system.TryHoldPiece(1);
            Assert.AreEqual(2, raised, "A swap replaces what is parked, so it must repaint too.");

            _system.StartNewRun();
            Assert.AreEqual(3, raised, "Clearing the hold for a new run must repaint it.");
        }

        [Test]
        public void HeldChanged_DoesNotFireForARefusedPark()
        {
            _trayModel.SetSlot(0, Single, 1);
            int raised = 0;
            _trayModel.HeldChanged += () => raised++;

            Assert.IsFalse(_system.TryHoldPiece(0));
            Assert.IsFalse(_system.TryHoldPiece(2));

            Assert.AreEqual(0, raised, "A refusal changes nothing, so there is nothing to repaint.");
        }

        // --- Run lifecycle and move availability ---

        [Test]
        public void StartNewRun_EmptiesTheHoldSlot()
        {
            FillDock(Single, HorizontalPair, VerticalTriple);
            _system.TryHoldPiece(0);

            _system.StartNewRun();

            Assert.IsFalse(_trayModel.IsHoldOccupied);
            Assert.IsNull(_trayModel.HeldPiece);
        }

        [Test]
        public void CheckGameOver_WithADeadDockButAPlayableParkedPiece_DoesNotEndTheRun()
        {
            // Swapping the parked piece back into a dock slot is always legal and free, so a board that
            // only the parked piece fits is not a dead end.
            FillBoardLeavingTwoFreeCellsPerColumn();
            _trayModel.SetSlot(0, Single, 1);
            _trayModel.SetSlot(1, VerticalTriple, 2);
            _trayModel.SetSlot(2, VerticalTriple, 3);
            _system.TryHoldPiece(0);

            // Both remaining dock pieces are three cells tall and no column has three free cells in a
            // row, so after this placement only the parked single still fits anywhere.
            _trayModel.SetSlot(0, Single, 1);
            bool placed = _system.TryPlacePiece(0, new GridPosition(0, 0));

            Assert.IsTrue(placed);
            Assert.IsFalse(_system.IsGameOver);
            Assert.AreEqual(0, _gameOverBroker.Published.Count);
        }

        private void FillDock(Piece first, Piece second, Piece third)
        {
            _trayModel.SetSlot(0, first, 1);
            _trayModel.SetSlot(1, second, 2);
            _trayModel.SetSlot(2, third, 3);
        }

        /// <summary>
        /// Fills the board except for two free cells in every column — <c>(x, x)</c> and
        /// <c>(x, x + 1)</c>. That leaves every row and every column two cells short of full, so no
        /// placement in this fixture can trigger a line clear, and no column ever has three free cells
        /// in a row, so a three-tall piece fits nowhere.
        /// </summary>
        private void FillBoardLeavingTwoFreeCellsPerColumn()
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                for (int y = 0; y < Board.SIZE; y++)
                {
                    if (y == x || y == (x + 1) % Board.SIZE)
                    {
                        continue;
                    }

                    _boardModel.Occupy(new GridPosition(x, y), 1);
                }
            }
        }

        private int CountDockPieces()
        {
            int count = 0;
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                if (_trayModel.GetPiece(slotIndex) != null)
                {
                    count++;
                }
            }

            return count;
        }

        private List<Piece> CollectEveryPieceInPlay()
        {
            var pieces = new List<Piece>(TrayModel.SLOT_COUNT + 1);
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                if (_trayModel.GetPiece(slotIndex) != null)
                {
                    pieces.Add(_trayModel.GetPiece(slotIndex));
                }
            }

            if (_trayModel.HeldPiece != null)
            {
                pieces.Add(_trayModel.HeldPiece);
            }

            return pieces;
        }

        /// <summary>
        /// A real <see cref="ScoreSystem"/> on this test's placement broker. No score rules are
        /// registered: the streak is moved by the System itself, not by a rule, so an empty rule set
        /// keeps the arithmetic out of the assertion without weakening it.
        /// </summary>
        private ScoreSystem CreateScoreSystem(ScoreModel scoreModel)
        {
            PlayerPrefs.DeleteKey("Score.HighScore");
            return new ScoreSystem(
                scoreModel,
                new GameModeSystem(new GameModeModel(), _system),
                new DoubleMultiplierModel(),
                new List<IScoreRule>(),
                _piecePlacedBroker,
                _runStartedBroker,
                new TestMessageBroker<ScoreChangedMessage>(),
                new TestMessageBroker<NewRecordMessage>(),
                new TestMessageBroker<BonusScoredMessage>());
        }
    }
}
