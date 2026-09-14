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
            BoardSystem boardSystem = CreateBoardSystem(boardModel);
            return new PowerUpSystem(
                model,
                boardModel,
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
        private static BoardSystem CreateBoardSystem(BoardModel boardModel)
        {
            return new BoardSystem(
                boardModel,
                new TrayModel(),
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>());
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
