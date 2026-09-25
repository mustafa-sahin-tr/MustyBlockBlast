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
    /// Issue #481: a locked cell that opens "reveals gold" — it pays the Coin cell's base payout once,
    /// through the same message a Coin cell pays through. The board records every lock that opens,
    /// whichever way it opened (<see cref="Board.OpenedLocks"/>); the resolving System pays and forgets
    /// them.
    /// </summary>
    public sealed class LockedCellGoldTests
    {
        private const int COLOUR = 3;
        private const int SKIN = 0;

        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        [Test]
        public void TryDamage_OnTheNeighbourThatOpensALock_RecordsTheLockOnce()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 3);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            board.Occupy(new GridPosition(3, 4), COLOUR);

            board.TryDamage(new GridPosition(3, 4));

            Assert.AreEqual(1, board.OpenedLocks.Count);
            Assert.AreEqual(lockPosition, board.OpenedLocks[0]);
        }

        [Test]
        public void TryDamage_AimedAtALockItOpens_RecordsTheLock()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 3);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);

            board.TryDamage(lockPosition);

            Assert.AreEqual(1, board.OpenedLocks.Count);
            Assert.AreEqual(lockPosition, board.OpenedLocks[0]);
        }

        [Test]
        public void TryDamage_ThatOnlyAdvancesALock_RecordsNothing()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 3);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);
            board.Occupy(new GridPosition(3, 4), COLOUR);

            board.TryDamage(new GridPosition(3, 4));
            board.TryDamage(lockPosition);

            Assert.IsTrue(board.IsLocked(lockPosition));
            Assert.AreEqual(0, board.OpenedLocks.Count);
        }

        [Test]
        public void CopyFrom_ForgetsTheOpenedLocks_SoARestoreNeverRepaysOne()
        {
            var board = new Board();
            Board snapshot = board.Clone();
            var lockPosition = new GridPosition(3, 3);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            board.TryDamage(lockPosition);
            Assert.AreEqual(1, board.OpenedLocks.Count);

            board.CopyFrom(snapshot);

            Assert.AreEqual(0, board.OpenedLocks.Count);
        }

        /// <summary>AC1/AC2 end to end: the placement that opens a lock pays the Coin cell's base payout
        /// through <see cref="CoinCellsClearedMessage"/>, announces where the chest opened, and forgets it.</summary>
        [Test]
        public void TryPlacePiece_ThatOpensALock_PaysTheCoinCellPayoutOnce()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel, out CurrencyConfig currencyConfig,
                out TestMessageBroker<CoinCellsClearedMessage> coinsBroker,
                out TestMessageBroker<LockedCellsOpenedMessage> openedBroker);

            var lockPosition = new GridPosition(Board.SIZE - 1, 2);
            boardModel.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                boardModel.Occupy(new GridPosition(x, 3), COLOUR);
            }

            trayModel.SetSlot(0, Single, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.IsFalse(boardModel.IsLocked(lockPosition));
            Assert.AreEqual(1, coinsBroker.Published.Count);
            Assert.AreEqual(currencyConfig.CoinCellPayout, coinsBroker.Published[0].TotalCoins);
            Assert.AreEqual(1, openedBroker.Published.Count);
            Assert.AreEqual(lockPosition, openedBroker.Published[0].Positions[0]);
            Assert.AreEqual(currencyConfig.CoinCellPayout, openedBroker.Published[0].CoinsPerLock);
            Assert.AreEqual(0, boardModel.Board.OpenedLocks.Count, "Paid locks are forgotten.");

            // AC7: an opened lock is an ordinary cell and never pays a second time.
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                boardModel.Occupy(new GridPosition(x, 3), COLOUR);
            }

            trayModel.SetSlot(0, Single, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));
            Assert.AreEqual(1, coinsBroker.Published.Count);
        }

        /// <summary>AC7: a lock that is only advanced — still standing when the run ends — pays nothing.</summary>
        [Test]
        public void TryPlacePiece_ThatOnlyAdvancesALock_PaysNothing()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel, out _,
                out TestMessageBroker<CoinCellsClearedMessage> coinsBroker,
                out TestMessageBroker<LockedCellsOpenedMessage> openedBroker);

            var lockPosition = new GridPosition(Board.SIZE - 1, 2);
            boardModel.OccupyLocked(lockPosition, COLOUR, 2, SKIN);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                boardModel.Occupy(new GridPosition(x, 3), COLOUR);
            }

            trayModel.SetSlot(0, Single, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.IsTrue(boardModel.IsLocked(lockPosition));
            Assert.AreEqual(0, coinsBroker.Published.Count);
            Assert.AreEqual(0, openedBroker.Published.Count);
        }

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out CurrencyConfig currencyConfig,
            out TestMessageBroker<CoinCellsClearedMessage> coinsBroker,
            out TestMessageBroker<LockedCellsOpenedMessage> openedBroker)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            currencyConfig = ScriptableObject.CreateInstance<CurrencyConfig>();
            coinsBroker = new TestMessageBroker<CoinCellsClearedMessage>();
            openedBroker = new TestMessageBroker<LockedCellsOpenedMessage>();

            // Unstarted, as LockedCellTests' fixture is: StartNewRun would draw over the hand-laid board.
            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                coinsBroker,
                currencyConfig,
                seed: 1,
                lockedCellsOpenedPublisher: openedBroker);
        }
    }
}
