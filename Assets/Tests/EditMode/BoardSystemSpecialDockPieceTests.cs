using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #129 — the three temporary, run-bound dock pieces: the golden 1x1, the piercing rocket and
    /// the demolition hammer. Covers each trigger's injection point, each piece's effect when used, and
    /// the invariants that keep them out of the inventory and out of the next run.
    /// </summary>
    public class BoardSystemSpecialDockPieceTests
    {
        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<PiercingRocketFiredMessage> _rocketFiredBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _rocketFiredBroker = new TestMessageBroker<PiercingRocketFiredMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();

            // Deliberately unstarted, as BoardSystemLaserTests is: StartNewRun would draw over the board
            // and dock each test lays out by hand.
            _system = new BoardSystem(
                _boardModel,
                _trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                _gameOverBroker,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                _rocketFiredBroker,
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1);
        }

        // --- AC1/AC3: the golden 1x1's trigger overrides one slot and leaves the draw alone ---

        /// <summary>AC1: a requested golden injection lands on the next refill, on the 1x1.</summary>
        [Test]
        public void RequestGoldenPieceInjection_ThenARefill_PutsAGoldenSingleInTheDock()
        {
            _system.RequestGoldenPieceInjection();
            _system.StartNewRun();

            // StartNewRun drops pending injections before refilling, so a request made before a run
            // cannot be paid by it — request it against the run that is now underway instead.
            _system.RequestGoldenPieceInjection();
            PlayOutTheDock();

            Assert.AreEqual(SpecialPieceKind.Golden, _trayModel.GetSpecialKind(0));
            Assert.AreEqual(PieceCatalog.SingleCell.Id, _trayModel.GetPiece(0).Id);
        }

        /// <summary>AC3: only the triggered slot is overridden — the other two are still whatever the
        /// weighted draw produced, and none of them is tagged.</summary>
        [Test]
        public void AGoldenInjection_LeavesTheOtherSlotsAsOrdinaryDrawnPieces()
        {
            _system.StartNewRun();
            _system.RequestGoldenPieceInjection();
            PlayOutTheDock();

            for (int slotIndex = 1; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.IsNotNull(_trayModel.GetPiece(slotIndex));
                Assert.AreEqual(SpecialPieceKind.None, _trayModel.GetSpecialKind(slotIndex));
            }
        }

        /// <summary>AC2: a piece earned in one run is never carried into the next.</summary>
        [Test]
        public void APendingGoldenInjection_DoesNotSurviveIntoTheNextRun()
        {
            _system.RequestGoldenPieceInjection();
            _system.StartNewRun();

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.AreEqual(SpecialPieceKind.None, _trayModel.GetSpecialKind(slotIndex));
            }
        }

        /// <summary>AC5: the golden 1x1 places exactly as a plain one does — it fills its cell and
        /// clears the row that fill completed.</summary>
        [Test]
        public void PlacingAGoldenSingle_FillsTheCellAndClearsACompletedRow()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.Golden);

            Assert.IsTrue(_system.TryPlacePiece(0, gap));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(x, 5)));
            }

            Assert.AreEqual(SpecialPieceKind.None, _trayModel.GetSpecialKind(0));
        }

        // --- AC6: the piercing rocket ---

        /// <summary>AC6: placed, it wipes its whole row and its whole column whether or not either was
        /// full, and goes up with them.</summary>
        [Test]
        public void PlacingAPiercingRocket_WipesTheFullRowAndColumnThroughIt()
        {
            var target = new GridPosition(3, 5);

            // A scattering of blocks on both lines and one off both, so "the wipe is the two lines and
            // nothing else" is actually testable.
            _boardModel.Occupy(new GridPosition(0, 5), 1);
            _boardModel.Occupy(new GridPosition(7, 5), 1);
            _boardModel.Occupy(new GridPosition(3, 0), 1);
            _boardModel.Occupy(new GridPosition(3, 7), 1);
            var bystander = new GridPosition(6, 2);
            _boardModel.Occupy(bystander, 1);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.PiercingRocket);

            // Two more pieces so the dock does not empty on this placement: a refill would restock slot
            // 0 and hide the fact that the rocket consumed it.
            _trayModel.SetSlot(1, PieceCatalog.SingleCell, 1);
            _trayModel.SetSlot(2, PieceCatalog.SingleCell, 1);

            Assert.IsTrue(_system.TryPlacePiece(0, target));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(x, 5)));
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(3, y)));
            }

            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(bystander));

            // Its own cell went with the lines — that is the self-destruct — and the slot is consumed.
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(target));
            Assert.IsNull(_trayModel.GetPiece(0));

            // Five blocks plus the rocket itself: the intersection is counted once.
            Assert.AreEqual(1, _rocketFiredBroker.Published.Count);
            Assert.AreEqual(5, _rocketFiredBroker.Published[0].WipedCellCount);
        }

        /// <summary>AC1: a move that clears three lines at once earns a rocket on the next refill.</summary>
        [Test]
        public void AMoveClearingThreeLines_PutsAPiercingRocketInTheNextDock()
        {
            _system.StartNewRun();

            // Rows 4 and 5 and column 3 all complete on the same 1x1, placed at their one shared gap.
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 4, new GridPosition(3, 4));
            FillRowExcept(y: 5, gap);
            FillColumnExcept(x: 3, gap);
            _boardModel.Occupy(new GridPosition(3, 4), 1);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);
            _trayModel.SetSlot(1, null, Board.EMPTY);
            _trayModel.SetSlot(2, null, Board.EMPTY);

            Assert.IsTrue(_system.TryPlacePiece(0, gap));

            // The dock emptied on that placement, so the refill it triggered is the one that owes it.
            Assert.AreEqual(SpecialPieceKind.PiercingRocket, _trayModel.GetSpecialKind(0));
        }

        // --- AC7: the demolition hammer ---

        /// <summary>AC1/AC7: a board at least 90% full with no legal move left is reprieved rather than
        /// ended, and the reprieve is a hammer in the dock.</summary>
        [Test]
        public void ADeadBoardAtNinetyPercent_IsReprievedWithADemolitionHammer()
        {
            FillBoard();
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);

            _system.RecheckGameOver();

            Assert.IsFalse(_system.IsGameOver);
            Assert.AreEqual(0, _gameOverBroker.Published.Count);
            Assert.IsTrue(HasHammer());
        }

        /// <summary>AC7: the hammer destroys exactly the cell it is aimed at, and is consumed by that
        /// one use — a second attempt on the same slot does nothing.</summary>
        [Test]
        public void UsingTheHammer_DestroysOneCellAndIsConsumed()
        {
            FillBoard();
            _system.RecheckGameOver();
            int hammerSlot = HammerSlot();
            Assert.GreaterOrEqual(hammerSlot, 0);

            var target = new GridPosition(4, 4);
            Assert.IsTrue(_system.TryUseDemolitionHammer(hammerSlot, target));

            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(target));
            Assert.AreEqual(63, _boardModel.Board.OccupiedCellCount());
            Assert.IsFalse(HasHammer());
            Assert.IsFalse(_system.TryUseDemolitionHammer(hammerSlot, new GridPosition(5, 5)));
        }

        /// <summary>An empty cell is not a legal target: nothing is destroyed and, crucially, the
        /// life-line is not spent — the "peek before spend" contract the power-ups follow.</summary>
        [Test]
        public void UsingTheHammerOnAnEmptyCell_ChangesNothingAndSpendsNothing()
        {
            FillBoard();
            _boardModel.Board.Clear(new GridPosition(2, 2));
            _system.RecheckGameOver();
            int hammerSlot = HammerSlot();
            Assert.GreaterOrEqual(hammerSlot, 0);

            Assert.IsFalse(_system.TryUseDemolitionHammer(hammerSlot, new GridPosition(2, 2)));
            Assert.IsTrue(HasHammer());
        }

        /// <summary>The hammer is never dropped on the board: placement is refused for its slot, so the
        /// drag preview refuses it too.</summary>
        [Test]
        public void AHammerSlot_CannotBePlacedOnTheBoard()
        {
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            Assert.IsFalse(_system.CanPlace(0, new GridPosition(0, 0)));
            Assert.IsFalse(_system.TryPlacePiece(0, new GridPosition(0, 0)));
            Assert.IsTrue(HasHammer());
        }

        /// <summary>One reprieve per run, not one per dead end: without this a crowded board could never
        /// reach a game over at all, because the hole the last hammer left re-qualifies immediately.</summary>
        [Test]
        public void ASecondDeadEndInTheSameRun_EndsTheRun()
        {
            FillBoard();
            _system.RecheckGameOver();
            int hammerSlot = HammerSlot();
            Assert.IsTrue(_system.TryUseDemolitionHammer(hammerSlot, new GridPosition(4, 4)));

            // Refill the hole and empty the dock by hand: the second dead end must find no hammer owed.
            _boardModel.Occupy(new GridPosition(4, 4), 1);
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _trayModel.SetSlot(slotIndex, null, Board.EMPTY);
            }

            _system.RecheckGameOver();

            Assert.IsTrue(_system.IsGameOver);
            Assert.AreEqual(GameOverReason.NoMovesLeft, _gameOverBroker.Published[0].Reason);
        }

        /// <summary>AC2: a fresh run opens with three plain pieces and its own, unspent life-line.</summary>
        [Test]
        public void StartingANewRun_RestoresTheHammerAndDropsEveryTag()
        {
            FillBoard();
            _system.RecheckGameOver();
            Assert.IsTrue(HasHammer());

            _system.StartNewRun();

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.AreEqual(SpecialPieceKind.None, _trayModel.GetSpecialKind(slotIndex));
            }
        }

        private bool HasHammer() => HammerSlot() >= 0;

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

        /// <summary>Empties the dock a slot at a time so the last one triggers a refill, which is the
        /// only moment a pending injection is paid.</summary>
        private void PlayOutTheDock()
        {
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _trayModel.SetSlot(slotIndex, null, Board.EMPTY);
            }

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);
            Assert.IsTrue(_system.TryPlacePiece(0, new GridPosition(0, 0)));
        }

        private void FillRowExcept(int y, GridPosition gap)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (!position.Equals(gap))
                {
                    _boardModel.Occupy(position, 1);
                }
            }
        }

        private void FillColumnExcept(int x, GridPosition gap)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                var position = new GridPosition(x, y);
                if (!position.Equals(gap) && _boardModel.GetCell(position) == Board.EMPTY)
                {
                    _boardModel.Occupy(position, 1);
                }
            }
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
    }
}
