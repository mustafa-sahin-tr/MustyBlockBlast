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
    /// End-to-end cover for the placement side of the explosive core: a placement that closes a row and
    /// a column spawns one, a placement that closes only one of the two does not, and a core caught in a
    /// completed line wipes the line at right angles to it (issue #398) — only that line, chaining into a
    /// second core the wipe catches, never handing off, and never spawning a fresh core of its own.
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
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                _detonatedBroker,
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
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

        /// <summary>AC1/AC9: a core destroyed by a completed row wipes its full column — including
        /// cells nowhere near the line the player completed — and nothing else: not its own row (already
        /// gone), and not a second axis it is not entitled to.</summary>
        [Test]
        public void TryPlacePiece_CompletingARowThatHoldsACore_WipesOnlyTheCoresColumn()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            var core = new GridPosition(0, 5);
            _boardModel.SetSpecialKind(core, SpecialCellKind.ExplosiveCore);

            // Two bystanders in the core's column, and one outside it.
            _boardModel.Occupy(new GridPosition(0, 1), 2);
            _boardModel.Occupy(new GridPosition(0, 7), 2);
            var survivor = new GridPosition(1, 1);
            _boardModel.Occupy(survivor, 2);

            bool placed = _system.TryPlacePiece(0, gap);

            Assert.IsTrue(placed);
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, 1)));
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, 7)));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(survivor), "Outside the wiped column.");

            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(2, _detonatedBroker.Published[0].WipedCellCount);
        }

        /// <summary>AC1/AC9's other half: destroyed by a column clear, it wipes its full row and only
        /// its row.</summary>
        [Test]
        public void TryPlacePiece_CompletingAColumnThatHoldsACore_WipesOnlyTheCoresRow()
        {
            var gap = new GridPosition(3, 5);
            FillColumnExcept(x: 3, gap);
            _boardModel.SetSpecialKind(new GridPosition(3, 0), SpecialCellKind.ExplosiveCore);

            _boardModel.Occupy(new GridPosition(6, 0), 2);
            var survivor = new GridPosition(6, 1);
            _boardModel.Occupy(survivor, 2);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(6, 0)));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(survivor), "Outside the wiped row.");

            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(1, _detonatedBroker.Published[0].WipedCellCount);
        }

        /// <summary>AC2: a core whose opposite line is already empty does nothing — no wipe reported, and
        /// no hand-off of its kind to some other occupied cell.</summary>
        [Test]
        public void TryPlacePiece_CompletingALineThatHoldsACoreWithAnEmptyOppositeLine_IsANoOpWithNoHandOff()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            var bystander = new GridPosition(7, 7);
            _boardModel.Occupy(bystander, 1);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(0, _detonatedBroker.Published.Count);
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(bystander), "Off the wiped column.");
            AssertNoCoreAnywhere();
        }

        /// <summary>
        /// AC4: a second core caught in the first core's column wipe fires in turn — destroyed by a
        /// column, so down its own row — through the effect's own chain, with one resolution reporting
        /// one wipe total.
        /// </summary>
        [Test]
        public void TryPlacePiece_WhereTheWipeCatchesASecondCore_ChainsAndReportsBothWipes()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            // In the first core's column, so the column wipe destroys it — and it was destroyed by a
            // column, so it fires down its own row.
            var chained = new GridPosition(0, 2);
            _boardModel.Occupy(chained, 2);
            _boardModel.SetSpecialKind(chained, SpecialCellKind.ExplosiveCore);

            var reachedOnlyByTheChain = new GridPosition(7, 2);
            _boardModel.Occupy(reachedOnlyByTheChain, 2);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(
                Board.EMPTY,
                _boardModel.GetCell(reachedOnlyByTheChain),
                "Only the chained row wipe can reach here.");

            Assert.AreEqual(1, _detonatedBroker.Published.Count, "One resolution, one wipe total.");
            Assert.AreEqual(2, _detonatedBroker.Published[0].WipedCellCount);
            AssertNoCoreAnywhere();
        }

        /// <summary>
        /// AC3: the bonus wipe never mints a fresh core. The placement itself closes only a row (no
        /// cross-clear, so no spawn of its own); the core it destroys then empties its whole column,
        /// which leaves "a row and a column both emptied" on the board — the shape of a cross-clear —
        /// but the spawn rule reads the placement's own primary clear only, so nothing is spawned.
        /// </summary>
        [Test]
        public void TryPlacePiece_WhereTheBonusWipeEmptiesAWholeColumn_SpawnsNoNewCore()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            // Every cell of column 0 but one is occupied — one short, so the placement itself cannot
            // close it and only the wipe empties it.
            for (int y = 1; y < Board.SIZE; y++)
            {
                _boardModel.Occupy(new GridPosition(0, y), 2);
            }

            _system.TryPlacePiece(0, gap);

            for (int y = 0; y < Board.SIZE; y++)
            {
                Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, y)), $"Column cell (0, {y}).");
            }

            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(Board.SIZE - 2, _detonatedBroker.Published[0].WipedCellCount, "Rows 1-4, 6, 7 of column 0.");
            AssertNoCoreAnywhere();
        }

        /// <summary>A placement that set nothing off must not publish an empty detonation — subscribers
        /// treat the message itself as "a detonation happened".</summary>
        [Test]
        public void TryPlacePiece_WithNoCoreInvolved_PublishesNoDetonation()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(0, _detonatedBroker.Published.Count);
        }

        /// <summary>A core standing well clear of the completed line is not destroyed, so it does not
        /// fire — and keeps its kind for next time.</summary>
        [Test]
        public void TryPlacePiece_WithACoreOffTheClearedLine_LeavesItStanding()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            var core = new GridPosition(6, 1);
            _boardModel.Occupy(core, 2);
            _boardModel.SetSpecialKind(core, SpecialCellKind.ExplosiveCore);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(SpecialCellKind.ExplosiveCore, _boardModel.GetSpecialKind(core));
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
