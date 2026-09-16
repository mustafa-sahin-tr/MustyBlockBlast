using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// End-to-end cover for the placement side of the laser: a laser taken out by a completed row wipes
    /// its column (and vice versa), the wipe is reported, a laser the wipe catches fires in turn, and a
    /// snapshot taken beforehand restores everything.
    /// </summary>
    public class BoardSystemLaserTests
    {
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<LaserFiredMessage> _laserFiredBroker;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _laserFiredBroker = new TestMessageBroker<LaserFiredMessage>();

            // Deliberately unstarted, as BoardSystemExplosiveCoreTests is: StartNewRun would draw over
            // the board and dock each test lays out by hand.
            _system = new BoardSystem(
                _boardModel,
                _trayModel,
                new PerfectRoundModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                _laserFiredBroker,
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                seed: 1);

            _trayModel.SetSlot(0, Single, 1);
        }

        /// <summary>AC2: destroyed by a row clear, it wipes its full column — including cells nowhere
        /// near the line the player completed.</summary>
        [Test]
        public void TryPlacePiece_CompletingARowThatHoldsALaser_WipesTheLasersColumn()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            var laser = new GridPosition(0, 5);
            _boardModel.SetSpecialKind(laser, SpecialCellKind.Laser);

            // Two bystanders in the laser's column, and one outside it.
            _boardModel.Occupy(new GridPosition(0, 1), 2);
            _boardModel.Occupy(new GridPosition(0, 7), 2);
            var survivor = new GridPosition(1, 1);
            _boardModel.Occupy(survivor, 2);

            bool placed = _system.TryPlacePiece(0, gap);

            Assert.IsTrue(placed);
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, 1)));
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, 7)));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(survivor), "Outside the wiped column.");

            Assert.AreEqual(1, _laserFiredBroker.Published.Count);
            Assert.AreEqual(2, _laserFiredBroker.Published[0].WipedCellCount);
        }

        /// <summary>AC2's other half: destroyed by a column clear, it wipes its full row.</summary>
        [Test]
        public void TryPlacePiece_CompletingAColumnThatHoldsALaser_WipesTheLasersRow()
        {
            var gap = new GridPosition(3, 5);
            FillColumnExcept(x: 3, gap);
            _boardModel.SetSpecialKind(new GridPosition(3, 0), SpecialCellKind.Laser);

            _boardModel.Occupy(new GridPosition(6, 0), 2);
            var survivor = new GridPosition(6, 1);
            _boardModel.Occupy(survivor, 2);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(6, 0)));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(survivor), "Outside the wiped row.");

            Assert.AreEqual(1, _laserFiredBroker.Published.Count);
            Assert.AreEqual(1, _laserFiredBroker.Published[0].WipedCellCount);
        }

        /// <summary>
        /// AC6: the resolution chains and settles. A wipe only ever removes cells, so it can never make
        /// a line full and therefore never opens a further <see cref="CascadeClearResolver"/> phase —
        /// the chaining a laser actually produces is same-kind, inside the effect, and this is it
        /// running as part of a real placement rather than in isolation.
        /// </summary>
        [Test]
        public void TryPlacePiece_WhereTheWipeCatchesASecondLaser_ChainsAndReportsBothWipes()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Laser);

            // In the first laser's column, so the column wipe destroys it — and it was destroyed by a
            // column, so it fires down its own row.
            var chained = new GridPosition(0, 2);
            _boardModel.Occupy(chained, 2);
            _boardModel.SetSpecialKind(chained, SpecialCellKind.Laser);

            var reachedOnlyByTheChain = new GridPosition(7, 2);
            _boardModel.Occupy(reachedOnlyByTheChain, 2);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(
                Board.EMPTY,
                _boardModel.GetCell(reachedOnlyByTheChain),
                "Only the chained row wipe can reach here.");

            Assert.AreEqual(1, _laserFiredBroker.Published.Count, "One resolution, one wipe total.");
            Assert.AreEqual(2, _laserFiredBroker.Published[0].WipedCellCount);
        }

        /// <summary>A placement that set nothing off must not publish an empty wipe — subscribers treat
        /// the message itself as "a laser fired".</summary>
        [Test]
        public void TryPlacePiece_WithNoLaserInvolved_PublishesNothing()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(0, _laserFiredBroker.Published.Count);
        }

        /// <summary>A laser standing well clear of the completed line is not destroyed, so it does not
        /// fire — and keeps its kind for next time.</summary>
        [Test]
        public void TryPlacePiece_WithALaserOffTheClearedLine_LeavesItStanding()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            var laser = new GridPosition(6, 1);
            _boardModel.Occupy(laser, 2);
            _boardModel.SetSpecialKind(laser, SpecialCellKind.Laser);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(SpecialCellKind.Laser, _boardModel.GetSpecialKind(laser));
            Assert.AreEqual(0, _laserFiredBroker.Published.Count);
        }

        /// <summary>AC7: a snapshot taken before the placement restores the board exactly, lasers
        /// included, so undo cannot silently strip or invent one. Rides entirely on
        /// <c>Board.Clone</c>/<c>CopyFrom</c> being kind-agnostic; stated explicitly because the
        /// acceptance criterion is about this kind specifically.</summary>
        [Test]
        public void ABoardSnapshot_TakenBeforeAPlacementThatFiresALaser_RestoresTheBoardExactly()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            var laser = new GridPosition(0, 5);
            _boardModel.SetSpecialKind(laser, SpecialCellKind.Laser);
            _boardModel.Occupy(new GridPosition(0, 1), 2);

            Board snapshot = _boardModel.Board.Clone();
            Assert.AreEqual(SpecialCellKind.Laser, snapshot.GetSpecialKind(laser), "The clone carries the kind.");

            _system.TryPlacePiece(0, gap);
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, 1)), "The wipe happened.");

            _boardModel.Board.CopyFrom(snapshot);

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    Assert.AreEqual(snapshot[position], _boardModel.Board[position], $"{position} colour.");
                    Assert.AreEqual(
                        snapshot.GetSpecialKind(position),
                        _boardModel.GetSpecialKind(position),
                        $"{position} kind.");
                }
            }

            Assert.AreEqual(SpecialCellKind.Laser, _boardModel.GetSpecialKind(laser), "The laser is back.");
        }

        private void FillRowExcept(int y, GridPosition gap)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (position.X == gap.X && position.Y == gap.Y)
                {
                    continue;
                }

                _boardModel.Occupy(position, 1);
            }
        }

        private void FillColumnExcept(int x, GridPosition gap)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                var position = new GridPosition(x, y);
                if (position.X == gap.X && position.Y == gap.Y)
                {
                    continue;
                }

                _boardModel.Occupy(position, 1);
            }
        }
    }
}
