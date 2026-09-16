using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// End-to-end cover for the placement side of the explosive core: a placement that closes a row and
    /// a column spawns one, a placement that closes only one of the two does not, and a core caught in
    /// a completed line blasts and is reported.
    /// </summary>
    public class BoardSystemExplosiveCoreTests
    {
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<ExplosiveCoreDetonatedMessage> _detonatedBroker;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _detonatedBroker = new TestMessageBroker<ExplosiveCoreDetonatedMessage>();

            // Deliberately unstarted, as BoardSystemHoldSlotTests is: StartNewRun would draw over the
            // board and dock each test lays out by hand.
            _system = new BoardSystem(
                _boardModel,
                _trayModel,
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                _detonatedBroker,
                new TestMessageBroker<LaserFiredMessage>(),
                seed: 1);

            _trayModel.SetSlot(0, Single, 1);
        }

        /// <summary>AC1 and AC4: one row and one column closed by the same placement always spawns a
        /// core, on their intersection, with no roll deciding whether it appears.</summary>
        [Test]
        public void TryPlacePiece_ClosingARowAndAColumn_SpawnsACoreOnTheirIntersection()
        {
            var gap = new GridPosition(3, 5);
            FillCrossExcept(gap);

            bool placed = _system.TryPlacePiece(0, gap);

            Assert.IsTrue(placed);
            Assert.AreEqual(SpecialCellKind.ExplosiveCore, _boardModel.GetSpecialKind(gap));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(gap), "The core needs a block to sit on.");
        }

        [Test]
        public void TryPlacePiece_ClosingARowAndAColumn_AnnouncesTheNewKindToTheView()
        {
            var gap = new GridPosition(3, 5);
            FillCrossExcept(gap);

            GridPosition announced = default;
            SpecialCellKind announcedKind = SpecialCellKind.None;
            int raised = 0;
            _boardModel.SpecialKindChanged += (position, kind) =>
            {
                announced = position;
                announcedKind = kind;
                raised++;
            };

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(1, raised);
            Assert.AreEqual(gap, announced);
            Assert.AreEqual(SpecialCellKind.ExplosiveCore, announcedKind);
        }

        /// <summary>AC1's other half: a row alone is not the achievement the core rewards.</summary>
        [Test]
        public void TryPlacePiece_ClosingOnlyARow_SpawnsNothing()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);

            _system.TryPlacePiece(0, gap);

            AssertNoCoreAnywhere();
        }

        [Test]
        public void TryPlacePiece_ClosingOnlyAColumn_SpawnsNothing()
        {
            var gap = new GridPosition(3, 5);
            FillColumnExcept(x: 3, gap);

            _system.TryPlacePiece(0, gap);

            AssertNoCoreAnywhere();
        }

        [Test]
        public void TryPlacePiece_ClearingNothing_SpawnsNothing()
        {
            _system.TryPlacePiece(0, new GridPosition(3, 5));

            AssertNoCoreAnywhere();
        }

        /// <summary>AC2: a core destroyed by a completed line blasts the 3x3 around it, and the cells it
        /// emptied are both cleared on the board and reported once.</summary>
        [Test]
        public void TryPlacePiece_CompletingALineThatHoldsACore_BlastsAroundItAndReportsIt()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            // Two bystanders inside the core's footprint, and one outside it.
            _boardModel.Occupy(new GridPosition(0, 4), 2);
            _boardModel.Occupy(new GridPosition(1, 4), 2);
            var survivor = new GridPosition(3, 4);
            _boardModel.Occupy(survivor, 2);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, 4)));
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(1, 4)));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(survivor), "Outside the blast.");

            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(2, _detonatedBroker.Published[0].ClearedCellCount);
        }

        /// <summary>A placement that set nothing off must not publish an empty blast — subscribers
        /// treat the message itself as "a blast happened".</summary>
        [Test]
        public void TryPlacePiece_WithNoCoreInvolved_PublishesNoDetonation()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(0, _detonatedBroker.Published.Count);
        }

        /// <summary>AC7's half that Core owns: a snapshot taken before the placement restores the board
        /// exactly, cores included, so undo cannot silently strip or invent one.</summary>
        [Test]
        public void ABoardSnapshot_TakenBeforeAPlacementThatSpawnsACore_RestoresTheBoardExactly()
        {
            var gap = new GridPosition(3, 5);
            FillCrossExcept(gap);
            Board snapshot = _boardModel.Board.Clone();

            _system.TryPlacePiece(0, gap);
            Assert.AreEqual(SpecialCellKind.ExplosiveCore, _boardModel.GetSpecialKind(gap));

            _boardModel.Board.CopyFrom(snapshot);

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    Assert.AreEqual(snapshot[position], _boardModel.Board[position], $"{position} colour.");
                    Assert.AreEqual(
                        SpecialCellKind.None, _boardModel.GetSpecialKind(position), $"{position} kind.");
                }
            }
        }

        private void AssertNoCoreAnywhere()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    Assert.AreEqual(
                        SpecialCellKind.None,
                        _boardModel.GetSpecialKind(new GridPosition(x, y)),
                        $"({x}, {y}) should carry no special kind.");
                }
            }
        }

        /// <summary>Fills exactly the row and the column through <paramref name="gap"/>, so placing a
        /// single cell there closes both at once — and closes nothing else, which is what makes the
        /// intersection unambiguous. (Filling the whole board would close all sixteen lines, and the
        /// spawn would land on the first pair, (0, 0).)</summary>
        private void FillCrossExcept(GridPosition gap)
        {
            FillRowExcept(gap.Y, gap);
            FillColumnExcept(gap.X, gap);
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
