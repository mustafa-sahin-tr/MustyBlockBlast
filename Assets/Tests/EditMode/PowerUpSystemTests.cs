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

        /// <summary>PowerUpSystem loads the inventory in its constructor, so a count left behind by a
        /// previous test would silently decide whether the next one can spend anything.</summary>
        [SetUp]
        public void ClearPersistedInventory()
        {
            DeleteInventoryKeys();
            _appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _grantedBroker = new TestMessageBroker<PowerUpGrantedMessage>();
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
            return new BoardSystem(
                boardModel,
                trayModel,
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                gameOverBroker,
                new TestMessageBroker<TrayRefilledMessage>());
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
