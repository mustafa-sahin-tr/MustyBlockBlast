using System.Collections.Generic;
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
    /// Covers the inventory contract: holding none is a complete no-op, while any valid application is
    /// charged for — including one that happens to clear nothing — and every change survives a restart.
    /// </summary>
    public class PowerUpSystemTests
    {
        private TestMessageBroker<PowerUpAppliedMessage> _appliedBroker;
        private TestMessageBroker<PowerUpGrantedMessage> _grantedBroker;

        /// <summary>The frenzy window the double multiplier opens. Kept as a field so a test can read
        /// the model behind it without reaching back through the system under test.</summary>
        private DoubleMultiplierModel _doubleMultiplierModel;
        private DoubleMultiplierSystem _doubleMultiplierSystem;

        /// <summary>PowerUpSystem loads the inventory in its constructor, so a count left behind by a
        /// previous test would silently decide whether the next one can spend anything.</summary>
        [SetUp]
        public void ClearPersistedInventory()
        {
            DeleteInventoryKeys();
            _appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _grantedBroker = new TestMessageBroker<PowerUpGrantedMessage>();
            _doubleMultiplierModel = new DoubleMultiplierModel();
            _doubleMultiplierSystem = new DoubleMultiplierSystem(
                _doubleMultiplierModel,
                new RunPauseModel(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        [TearDown]
        public void ClearPersistedInventoryAfterwards()
        {
            DeleteInventoryKeys();
        }

        [Test]
        public void TryApplyBomb_WithNoBombsHeld_ChangesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.BombCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(4, 4)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyBomb_WithABombHeld_SpendsItAndClearsTheArea()
        {
            PersistCount(PowerUpKind.Bomb, 2);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            boardModel.Occupy(new GridPosition(5, 5), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.BombCount.Value);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(4, 4)));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(5, 5)));
            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Bomb, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyBomb_OverAnEmptyArea_StillSpendsItAndReportsZeroCleared()
        {
            // A deliberate, legal application: the player targeted a cell they were allowed to target,
            // so it is charged for even though it found nothing to destroy.
            PersistCount(PowerUpKind.Bomb, 1);
            var boardModel = new BoardModel();
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsTrue(applied);
            Assert.AreEqual(0, model.BombCount.Value);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyBomb_RaisesCellChangedForEveryClearedCell()
        {
            PersistCount(PowerUpKind.Bomb, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            boardModel.Occupy(new GridPosition(1, 1), 1);
            var changed = new List<GridPosition>();
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            boardModel.CellChanged += (position, colourId) => changed.Add(position);

            system.TryApplyBomb(new GridPosition(0, 0));

            CollectionAssert.AreEquivalent(
                new[] { new GridPosition(0, 0), new GridPosition(1, 1) }, changed);
        }

        [Test]
        public void TryApplyRowClear_WithOneHeld_ClearsAPartiallyFilledRow()
        {
            PersistCount(PowerUpKind.RowClear, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(2, 3), 1);
            boardModel.Occupy(new GridPosition(6, 3), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyRowClear(3);

            Assert.IsTrue(applied);
            Assert.AreEqual(0, model.RowClearCount.Value);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(2, 3)));
            Assert.AreEqual(PowerUpKind.RowClear, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyColumnClear_WithNoneHeld_ChangesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(5, 0), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColumnClear(5);

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.ColumnClearCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(5, 0)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyColumnClear_WithOneHeld_ClearsTheColumn()
        {
            PersistCount(PowerUpKind.ColumnClear, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(5, 0), 1);
            boardModel.Occupy(new GridPosition(5, 7), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColumnClear(5);

            Assert.IsTrue(applied);
            Assert.AreEqual(0, model.ColumnClearCount.Value);
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyRowClear_WithAnOutOfBoundsRow_ChangesNothingAndKeepsTheInventory()
        {
            PersistCount(PowerUpKind.RowClear, 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyRowClear(Board.SIZE);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.RowClearCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyBomb_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.Bomb, 3);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel());
            system.TryApplyBomb(new GridPosition(0, 0));

            // Same prefs, fresh objects — i.e. the next launch.
            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(2, reloadedModel.BombCount.Value);
        }

        [Test]
        public void TryApplyColorCleanser_WithNoneHeld_ChangesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(3, 3), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColorCleanser(new GridPosition(3, 3));

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.ColorCleanserCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(3, 3)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyColorCleanser_OnAnOccupiedCell_ClearsEveryCellOfThatColourOnly()
        {
            PersistCount(PowerUpKind.ColorCleanser, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            boardModel.Occupy(new GridPosition(7, 7), 1);
            // A different colour, must survive.
            boardModel.Occupy(new GridPosition(4, 4), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColorCleanser(new GridPosition(0, 0));

            Assert.IsTrue(applied);
            Assert.AreEqual(0, model.ColorCleanserCount.Value);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(0, 0)));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(7, 7)));
            Assert.AreEqual(2, boardModel.GetCell(new GridPosition(4, 4)));
            Assert.AreEqual(PowerUpKind.ColorCleanser, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyColorCleanser_OnAnEmptyCell_IsRejected_KeepsInventoryAndArmedSelection()
        {
            // Mirrors TryApplyJoker's "peek before spend" contract, not the always-spend contract the
            // three region-clearing kinds follow.
            PersistCount(PowerUpKind.ColorCleanser, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(5, 5), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);
            system.Arm(PowerUpKind.ColorCleanser);

            bool applied = system.TryApplyColorCleanser(new GridPosition(2, 2));

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.ColorCleanserCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(5, 5)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.ColorCleanser, model.Armed.Value);
        }

        [Test]
        public void TryApplyColorCleanser_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.ColorCleanser, 2);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.TryApplyColorCleanser(new GridPosition(0, 0));

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.ColorCleanserCount.Value);
        }

        [Test]
        public void TryApplyRotate_OnANonSymmetricalPiece_SwapsInTheRotatedCatalogPieceAndSpendsOne()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            trayModel.SetSlot(1, FindPiece("t_up"), colourId: 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyRotate(1);

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.RotateCount.Value);

            // The slot holds a real catalog piece, so its id still describes its shape.
            Assert.AreEqual("t_right", trayModel.GetPiece(1).Id);
            Assert.AreSame(FindPiece("t_right"), trayModel.GetPiece(1));
            Assert.AreEqual(3, trayModel.GetColourId(1));

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Rotate, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyRotate_LeavesTheOtherSlotsAlone()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h5"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("t_up"), colourId: 2);
            trayModel.SetSlot(2, FindPiece("corner3_bl"), colourId: 3);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), trayModel);

            system.TryApplyRotate(0);

            Assert.AreEqual("line_v5", trayModel.GetPiece(0).Id);
            Assert.AreEqual("t_up", trayModel.GetPiece(1).Id);
            Assert.AreEqual("corner3_bl", trayModel.GetPiece(2).Id);
        }

        [TestCase("single_1x1")]
        [TestCase("square_2x2")]
        [TestCase("square_3x3")]
        public void TryApplyRotate_OnAFullySymmetricalPiece_IsRejected_KeepsThePieceInventoryAndArm(string id)
        {
            // Same "peek before spend" contract as Joker and ColorCleanser: a piece whose rotation is
            // itself is a dead tap, so nothing is spent and the power-up stays armed to aim again.
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            Piece piece = FindPiece(id);
            trayModel.SetSlot(0, piece, colourId: 4);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);

            bool applied = system.TryApplyRotate(0);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreSame(piece, trayModel.GetPiece(0));
            Assert.AreEqual(4, trayModel.GetColourId(0));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Rotate, model.Armed.Value);
        }

        [Test]
        public void TryApplyRotate_WithNoneHeld_ChangesNothing()
        {
            var trayModel = new TrayModel();
            Piece piece = FindPiece("t_up");
            trayModel.SetSlot(0, piece, colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyRotate(0);

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.RotateCount.Value);
            Assert.AreSame(piece, trayModel.GetPiece(0));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [TestCase(-1)]
        [TestCase(TrayModel.SLOT_COUNT)]
        public void TryApplyRotate_WithAnOutOfRangeSlot_ChangesNothingAndKeepsTheInventory(int slotIndex)
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyRotate(slotIndex);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyRotate_OnAnEmptySlot_ChangesNothingAndKeepsTheInventory()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(2, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyRotate(0);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyRotate_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("corner2_missing_tr"), colourId: 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), trayModel);
            system.TryApplyRotate(0);

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.RotateCount.Value);
        }

        [Test]
        public void TryApplyRotate_OnSuccess_DropsTheArmedSelection()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);

            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.IsNull(model.Armed.Value);
        }

        /// <summary>
        /// A rotate changes <em>which shapes</em> the player holds, so unlike a park it can take the
        /// last legal move away. <c>BoardSystem</c> is asked to re-check, and the run ends exactly as
        /// the placement that exhausted the board would have ended it.
        /// </summary>
        [Test]
        public void TryApplyRotate_WhenTheTurnedPieceNoLongerFits_EndsTheRun()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var boardModel = new BoardModel();

            // Everything filled but one horizontal two-cell gap: line_h2 fits it, line_v2 cannot.
            FillBoardExcept(boardModel, new GridPosition(0, 0), new GridPosition(1, 0));

            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h2"), colourId: 1);
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel, trayModel, gameOverBroker);

            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.AreEqual("line_v2", trayModel.GetPiece(0).Id);
            Assert.AreEqual(1, gameOverBroker.Published.Count);
            Assert.AreEqual(GameOverReason.NoMovesLeft, gameOverBroker.Published[0].Reason);
        }

        /// <summary>The other half of the same contract: the re-check is a question, not a verdict, so
        /// a rotate that leaves a move standing must not end anything.</summary>
        [Test]
        public void TryApplyRotate_WhenTheTurnedPieceStillFits_LeavesTheRunAlive()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var boardModel = new BoardModel();

            // The mirror image of the case above: only a vertical gap, so the turned piece is the one
            // that fits and the rotate is what keeps the run alive.
            FillBoardExcept(boardModel, new GridPosition(0, 0), new GridPosition(0, 1));

            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h2"), colourId: 1);
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel, trayModel, gameOverBroker);

            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.AreEqual("line_v2", trayModel.GetPiece(0).Id);
            Assert.AreEqual(0, gameOverBroker.Published.Count);
        }

        /// <summary>
        /// AC1. The expected set is produced by a second draw seeded identically and asked for the same
        /// thing, so the assertion is "all three slots hold the freshly drawn set" without depending on
        /// the order the System happens to pull pieces and colours in.
        /// </summary>
        [Test]
        public void TryApplyReroll_WithOneHeld_ReplacesAllThreeDockPiecesAndSpendsOne()
        {
            const int seed = 12345;
            PersistCount(PowerUpKind.Reroll, 2);

            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("line_h5"), colourId: 2);
            // Deliberately consumed: a reroll restocks every slot, not only the occupied ones.
            trayModel.SetSlot(2, null, Board.EMPTY);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateRerollSystem(
                model,
                boardModel,
                trayModel,
                seed,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            bool applied = system.TryApplyReroll();

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.RerollCount.Value);

            var expectedPieces = new Piece[TrayModel.SLOT_COUNT];
            var expectedColours = new int[TrayModel.SLOT_COUNT];
            new WeightedPieceDraw(seed).TryDrawSolvableSet(
                new BoardModel().Board, expectedPieces, expectedColours);

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.AreSame(expectedPieces[slotIndex], trayModel.GetPiece(slotIndex));
                Assert.AreEqual(expectedColours[slotIndex], trayModel.GetColourId(slotIndex));
            }

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Reroll, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        /// <summary>
        /// AC2. The board is filled but for a 2x2 corner, so most of the catalog cannot be placed at
        /// all — the reroll's guarantee is what makes the set it hands back playable.
        /// </summary>
        [Test]
        public void TryApplyReroll_OnAConstrainedBoard_DrawsASetWithALegalPlacement()
        {
            PersistCount(PowerUpKind.Reroll, 1);

            var boardModel = new BoardModel();
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 1));

            var trayModel = new TrayModel();
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                boardModel,
                trayModel,
                drawSeed: 7,
                new TestMessageBroker<TrayRefilledMessage>(),
                gameOverBroker);

            Assert.IsTrue(system.TryApplyReroll());

            var drawn = new List<Piece>(TrayModel.SLOT_COUNT);
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.IsNotNull(trayModel.GetPiece(slotIndex));
                drawn.Add(trayModel.GetPiece(slotIndex));
            }

            Assert.IsTrue(MoveAvailability.HasAnyMove(boardModel.Board, drawn));

            // The other half of the same claim: a set with a move in it cannot have ended the run.
            Assert.AreEqual(0, gameOverBroker.Published.Count);
        }

        /// <summary>AC3. Holding none is a complete no-op — nothing drawn, nothing spent.</summary>
        [Test]
        public void TryApplyReroll_WithNoneHeld_ChangesNothing()
        {
            var trayModel = new TrayModel();
            Piece first = FindPiece("t_up");
            Piece second = FindPiece("line_h2");
            trayModel.SetSlot(0, first, colourId: 1);
            trayModel.SetSlot(1, second, colourId: 2);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyReroll();

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.RerollCount.Value);
            Assert.AreSame(first, trayModel.GetPiece(0));
            Assert.AreEqual(1, trayModel.GetColourId(0));
            Assert.AreSame(second, trayModel.GetPiece(1));
            Assert.IsNull(trayModel.GetPiece(2));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>
        /// A reroll is a discard, not a dock played out, so it must not masquerade as a refill: the one
        /// subscriber of that message restarts the timed-mode countdown from full on it.
        /// </summary>
        [Test]
        public void TryApplyReroll_DoesNotPublishTrayRefilled()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            var trayRefilledBroker = new TestMessageBroker<TrayRefilledMessage>();
            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                new BoardModel(),
                new TrayModel(),
                drawSeed: 3,
                trayRefilledBroker,
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(0, trayRefilledBroker.Published.Count);
        }

        /// <summary>A parked piece is not on offer, so it is not part of what a reroll discards.</summary>
        [Test]
        public void TryApplyReroll_LeavesTheHoldSlotAlone()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h2"), colourId: 1);
            trayModel.SetHeld(FindPiece("square_3x3"), colourId: 2);

            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                new BoardModel(),
                trayModel,
                drawSeed: 11,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreSame(FindPiece("square_3x3"), trayModel.HeldPiece);
            Assert.AreEqual(2, trayModel.HeldColourId);
        }

        /// <summary>Reroll has no target, so there is nothing to aim it at and it must never become an
        /// armed selection — an armed reroll could only be released onto a cell that means nothing to
        /// it.</summary>
        [Test]
        public void Arm_WithReroll_DoesNotArmIt()
        {
            PersistCount(PowerUpKind.Reroll, 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.Reroll);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.RerollCount.Value);
        }

        /// <summary>Applying a reroll while another kind is armed drops that selection, so the clock
        /// hold an armed kind carries is never stranded behind it.</summary>
        [Test]
        public void TryApplyReroll_WhileAnotherKindIsArmed_DropsThatSelection()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            PersistCount(PowerUpKind.Bomb, 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateRerollSystem(
                model,
                new BoardModel(),
                new TrayModel(),
                drawSeed: 5,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());
            system.Arm(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyReroll());

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(1, model.BombCount.Value);
        }

        [Test]
        public void TryApplyReroll_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.Reroll, 2);
            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                new BoardModel(),
                new TrayModel(),
                drawSeed: 9,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());
            system.TryApplyReroll();

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.RerollCount.Value);
        }

        /// <summary>
        /// AC5, end to end: on a full board no set can satisfy the guarantee, so the bounded draw gives
        /// up and hands back its last attempt. The player still gets three real pieces and is still
        /// charged for the reroll, and the re-check ends the run exactly as an exhausted board would —
        /// the same contract as a rotate that leaves nothing placeable.
        /// </summary>
        [Test]
        public void TryApplyReroll_OnABoardNoPieceFits_IsStillSpentAndEndsTheRun()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            var boardModel = new BoardModel();
            FillBoardExcept(boardModel);

            var trayModel = new TrayModel();
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateRerollSystem(
                model,
                boardModel,
                trayModel,
                drawSeed: 13,
                new TestMessageBroker<TrayRefilledMessage>(),
                gameOverBroker);

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(0, model.RerollCount.Value);
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.IsNotNull(trayModel.GetPiece(slotIndex), "The fallback must still restock the dock.");
            }

            Assert.AreEqual(1, gameOverBroker.Published.Count);
            Assert.AreEqual(GameOverReason.NoMovesLeft, gameOverBroker.Published[0].Reason);
        }

        /// <summary>Issue #95 AC1: the dock has zero legal placements before the reroll, and the
        /// guaranteed-solvable draw rescues it — this is exactly what "clutch" means for the
        /// RerollSave objective.</summary>
        [Test]
        public void TryApplyReroll_DockHadNoLegalMoves_PublishesWasClutchSaveTrue()
        {
            PersistCount(PowerUpKind.Reroll, 1);

            var boardModel = new BoardModel();
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 1));

            var trayModel = new TrayModel();
            // A 3x3 square cannot fit in a 2x2 opening, so none of these three qualify — the dock
            // starts with zero legal placements.
            trayModel.SetSlot(0, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(2, FindPiece("square_3x3"), colourId: 1);

            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                boardModel,
                trayModel,
                drawSeed: 7,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.IsTrue(_appliedBroker.Published[0].WasClutchSave);
        }

        /// <summary>Issue #95 AC2 (negative case): the dock already had a legal placement before the
        /// reroll, so however good the fresh draw is, this was never a rescue.</summary>
        [Test]
        public void TryApplyReroll_DockAlreadyHadALegalMove_PublishesWasClutchSaveFalse()
        {
            PersistCount(PowerUpKind.Reroll, 1);

            var boardModel = new BoardModel();
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 1));

            var trayModel = new TrayModel();
            // A 1x1 fits the 2x2 opening, so the dock already has a legal move before the reroll.
            trayModel.SetSlot(0, FindPiece("single_1x1"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(2, FindPiece("square_3x3"), colourId: 1);

            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                boardModel,
                trayModel,
                drawSeed: 7,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.IsFalse(_appliedBroker.Published[0].WasClutchSave);
        }

        /// <summary>
        /// Pins the Hold-slot exclusion documented in docs/game-design.md: the parked piece is not part
        /// of the "zero legal moves" pre-check, so a dead dock still counts as clutch even when the
        /// pocket held a piece that fits. This is also the only shape of this case a live run can ever
        /// reach — <c>CheckGameOver</c> counts the held piece, so a dead dock with an empty pocket has
        /// already ended the run and <c>TryRerollTray</c> would refuse outright.
        /// </summary>
        [Test]
        public void TryApplyReroll_DockDeadButHeldPieceFits_StillPublishesWasClutchSaveTrue()
        {
            PersistCount(PowerUpKind.Reroll, 1);

            var boardModel = new BoardModel();
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 1));

            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(2, FindPiece("square_3x3"), colourId: 1);
            // Fits the 2x2 opening — but it is parked, not on offer.
            trayModel.SetHeld(FindPiece("single_1x1"), colourId: 2);

            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                boardModel,
                trayModel,
                drawSeed: 7,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.IsTrue(_appliedBroker.Published[0].WasClutchSave);
        }

        /// <summary>A run that is already over refuses the reroll outright: nothing is drawn, the dock
        /// is left as it was and nothing is spent.</summary>
        [Test]
        public void TryApplyReroll_WhenTheRunIsAlreadyOver_ChangesNothing()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            Piece parked = FindPiece("line_h2");
            trayModel.SetSlot(0, parked, colourId: 1);

            BoardSystem boardSystem = CreateBoardSystem(
                boardModel, trayModel, new TestMessageBroker<GameOverMessage>());
            boardSystem.ForceGameOver(GameOverReason.TimeUp);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(
                model, boardModel, trayModel, new StubRewardSource(granted: true), boardSystem);

            Assert.IsFalse(system.TryApplyReroll());

            Assert.AreEqual(1, model.RerollCount.Value);
            Assert.AreSame(parked, trayModel.GetPiece(0));
            Assert.IsNull(trayModel.GetPiece(1));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void GrantRewardAsync_WhenTheSourceGrants_IncrementsPersistsAndPublishes()
        {
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), new StubRewardSource(granted: true));

            bool granted = system.GrantRewardAsync(PowerUpKind.RowClear, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(granted);
            Assert.AreEqual(1, model.RowClearCount.Value);
            Assert.AreEqual(1, PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.RowClear), 0));
            Assert.AreEqual(1, _grantedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.RowClear, _grantedBroker.Published[0].Kind);
            Assert.AreEqual(1, _grantedBroker.Published[0].NewInventoryCount);
        }

        [Test]
        public void GrantRewardAsync_WhenTheSourceRefuses_ChangesNothing()
        {
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), new StubRewardSource(granted: false));

            bool granted = system.GrantRewardAsync(PowerUpKind.Bomb, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(granted);
            Assert.AreEqual(0, model.BombCount.Value);
            Assert.AreEqual(0, _grantedBroker.Published.Count);
        }

        [Test]
        public void GrantRewardAsync_ThenANewSystem_LoadsTheGrantedCount()
        {
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel());
            system.GrantRewardAsync(PowerUpKind.ColumnClear, CancellationToken.None).GetAwaiter().GetResult();

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.ColumnClearCount.Value);
        }

        [Test]
        public void TryApplyDoubleMultiplier_WithNoneHeld_ChangesNothing()
        {
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyDoubleMultiplier();

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.DoubleMultiplierCount.Value);
            Assert.IsFalse(_doubleMultiplierModel.IsActive);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyDoubleMultiplier_WithOneHeld_SpendsItAndOpensAFullWindow()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyDoubleMultiplier();

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.DoubleMultiplierCount.Value);
            Assert.IsTrue(_doubleMultiplierModel.IsActive);
            Assert.AreEqual(
                DoubleMultiplierModel.WINDOW_SECONDS, _doubleMultiplierModel.RemainingSeconds.Value);
        }

        /// <summary>The badge counter that tracks "power-ups used" has to see this kind too, even
        /// though it clears nothing — the same contract Rotate and Reroll follow.</summary>
        [Test]
        public void TryApplyDoubleMultiplier_PublishesAnApplicationThatClearedNothing()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel());

            system.TryApplyDoubleMultiplier();

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.DoubleMultiplier, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        /// <summary>Targetless, so it is never an armed selection — exactly like the reroll.</summary>
        [Test]
        public void Arm_DoubleMultiplier_IsRefusedEvenWhenHeld()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.DoubleMultiplier);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.DoubleMultiplierCount.Value);
        }

        /// <summary>Re-activating restarts the window rather than stacking: the power-up is a doubling,
        /// not a multiplier that compounds with itself.</summary>
        [Test]
        public void TryApplyDoubleMultiplier_Twice_RestartsTheWindowRatherThanExtendingIt()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 2);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel());

            system.TryApplyDoubleMultiplier();
            _doubleMultiplierSystem.Advance(10f);
            system.TryApplyDoubleMultiplier();

            Assert.AreEqual(
                DoubleMultiplierModel.WINDOW_SECONDS, _doubleMultiplierModel.RemainingSeconds.Value);
        }

        [Test]
        public void TryApplyDoubleMultiplier_WithTheRunOver_IsRefused()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 1);
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            BoardSystem boardSystem = CreateBoardSystem(
                boardModel, trayModel, new TestMessageBroker<GameOverMessage>());
            boardSystem.ForceGameOver(GameOverReason.TimeUp);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(
                model, boardModel, trayModel, new StubRewardSource(granted: true), boardSystem);

            bool applied = system.TryApplyDoubleMultiplier();

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.DoubleMultiplierCount.Value);
            Assert.IsFalse(_doubleMultiplierModel.IsActive);
        }

        /// <summary>Builds a system whose reroll draws are reproducible, and hands back the brokers and
        /// board system the reroll tests need to observe.</summary>
        private PowerUpSystem CreateRerollSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            int drawSeed,
            TestMessageBroker<TrayRefilledMessage> trayRefilledBroker,
            TestMessageBroker<GameOverMessage> gameOverBroker)
        {
            BoardSystem boardSystem = CreateBoardSystem(
                boardModel,
                trayModel,
                gameOverBroker,
                new WeightedPieceDraw(drawSeed),
                trayRefilledBroker);

            return CreateSystem(
                model, boardModel, trayModel, new StubRewardSource(granted: true), boardSystem);
        }

        private PowerUpSystem CreateSystem(PowerUpModel model, BoardModel boardModel)
        {
            return CreateSystem(model, boardModel, new StubRewardSource(granted: true));
        }

        private PowerUpSystem CreateSystem(PowerUpModel model, BoardModel boardModel, IRewardSource rewardSource)
        {
            return CreateSystem(model, boardModel, new TrayModel(), rewardSource);
        }

        private PowerUpSystem CreateSystem(PowerUpModel model, BoardModel boardModel, TrayModel trayModel)
        {
            return CreateSystem(model, boardModel, trayModel, new StubRewardSource(granted: true));
        }

        /// <summary>For the game-over re-check tests: routes the board system's game-over messages to a
        /// broker the test can read, which is how it observes whether the rotate ended the run.</summary>
        private PowerUpSystem CreateSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            TestMessageBroker<GameOverMessage> gameOverBroker)
        {
            return CreateSystem(
                model,
                boardModel,
                trayModel,
                new StubRewardSource(granted: true),
                CreateBoardSystem(boardModel, trayModel, gameOverBroker));
        }

        private PowerUpSystem CreateSystem(
            PowerUpModel model, BoardModel boardModel, TrayModel trayModel, IRewardSource rewardSource)
        {
            return CreateSystem(
                model, boardModel, trayModel, rewardSource, CreateBoardSystem(boardModel, trayModel));
        }

        private PowerUpSystem CreateSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            IRewardSource rewardSource,
            BoardSystem boardSystem)
        {
            return new PowerUpSystem(
                model,
                boardModel,
                trayModel,
                boardSystem,
                CreateTimerRunSystem(boardSystem),
                _doubleMultiplierSystem,
                rewardSource,
                _appliedBroker,
                _grantedBroker,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        /// <summary>
        /// A real, unstarted <see cref="BoardSystem"/>: <c>PowerUpSystem</c> reads only its
        /// <c>IsGameOver</c> flag, which is false until the run is started or checked, so the board
        /// each test set up by hand is left exactly as it was.
        /// </summary>
        private static BoardSystem CreateBoardSystem(BoardModel boardModel, TrayModel trayModel)
        {
            return CreateBoardSystem(boardModel, trayModel, new TestMessageBroker<GameOverMessage>());
        }

        private static BoardSystem CreateBoardSystem(
            BoardModel boardModel, TrayModel trayModel, TestMessageBroker<GameOverMessage> gameOverBroker)
        {
            return CreateBoardSystem(
                boardModel,
                trayModel,
                gameOverBroker,
                new WeightedPieceDraw(),
                new TestMessageBroker<TrayRefilledMessage>());
        }

        /// <summary>For the reroll tests, which need a seeded draw (so the set is reproducible) and a
        /// readable tray-refill broker (so "a reroll is not a refill" can be asserted).</summary>
        private static BoardSystem CreateBoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            TestMessageBroker<GameOverMessage> gameOverBroker,
            WeightedPieceDraw pieceDraw,
            TestMessageBroker<TrayRefilledMessage> trayRefilledBroker)
        {
            return new BoardSystem(
                boardModel,
                trayModel,
                pieceDraw,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                gameOverBroker,
                trayRefilledBroker);
        }

        /// <summary>Occupies every board cell except the ones named, so a test can state the one gap it
        /// wants a piece to have to fit into.</summary>
        private static void FillBoardExcept(BoardModel boardModel, params GridPosition[] emptyCells)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (!Contains(emptyCells, position))
                    {
                        boardModel.Occupy(position, 1);
                    }
                }
            }
        }

        private static bool Contains(GridPosition[] cells, GridPosition cell)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                if (cells[index].Equals(cell))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Only ever asked to hold and release the countdown here; it is never ticked.</summary>
        private static TimerRunSystem CreateTimerRunSystem(BoardSystem boardSystem)
        {
            var timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            return new TimerRunSystem(
                new TimerModel(),
                new RunPauseModel(),
                new GameModeSystem(new GameModeModel(), boardSystem),
                new TimedModeSystem(new TimedModeModel(), timedModeConfig),
                boardSystem,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        private static void PersistCount(PowerUpKind kind, int count)
        {
            PlayerPrefs.SetInt(PowerUpInventoryKey.For(kind), count);
        }

        private static void DeleteInventoryKeys()
        {
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Bomb));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.RowClear));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.ColumnClear));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Joker));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.ColorCleanser));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Rotate));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Reroll));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.DoubleMultiplier));
        }

        private static Piece FindPiece(string id)
        {
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                if (piece.Id == id)
                {
                    return piece;
                }
            }

            Assert.Fail($"Piece '{id}' not found in catalog.");
            return null;
        }

        /// <summary>Completes synchronously so these stay plain synchronous EditMode tests.</summary>
        private sealed class StubRewardSource : IRewardSource
        {
            private readonly bool _granted;

            internal StubRewardSource(bool granted)
            {
                _granted = granted;
            }

            public UniTask<RewardResult> RequestRewardAsync(PowerUpKind kind, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(new RewardResult(kind, _granted));
            }
        }
    }
}
