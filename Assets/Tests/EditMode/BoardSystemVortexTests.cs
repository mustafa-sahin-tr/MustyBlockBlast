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
    /// End-to-end cover for the placement side of the vortex: the run-wide line-clear count the spawn
    /// rule is read against, a destroyed vortex fills the board's islands (or hands off when there is
    /// none), the work is reported, and a snapshot taken beforehand restores everything.
    /// </summary>
    public class BoardSystemVortexTests
    {
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        /// <summary>The gap every test's placement fills, completing row 5.</summary>
        private static readonly GridPosition Gap = new GridPosition(3, 5);

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<VortexIslandFilledMessage> _islandFilledBroker;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _islandFilledBroker = new TestMessageBroker<VortexIslandFilledMessage>();

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
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                _islandFilledBroker,
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1);

            // All three slots filled, unlike the laser and core tests: these placements must not empty
            // the dock, because the refill that follows an emptied one can spawn a score gem of its own
            // on a random occupied cell — including the one this feature's spawn rule just chose.
            _trayModel.SetSlot(0, Single, 1);
            _trayModel.SetSlot(1, Single, 1);
            _trayModel.SetSlot(2, Single, 1);
        }

        /// <summary>Five separate single-line clears sum to the threshold, and the fifth spawns the
        /// vortex on a cell that clear itself emptied, with no roll deciding whether it appears. The
        /// four before it are checked to carry no kind, so the assertion actually exercises the
        /// threshold rather than a coincidence.</summary>
        [Test]
        public void TryPlacePiece_ReachingFiveClearedLinesTotal_SpawnsAVortexOnTheFifthClear()
        {
            ClearOneLine(new GridPosition(0, 0));
            ClearOneLine(new GridPosition(0, 1));
            ClearOneLine(new GridPosition(0, 2));
            ClearOneLine(new GridPosition(0, 3));
            Assert.IsNull(FindVortex(), "Four cleared lines have not reached the threshold yet.");

            ClearOneLine(new GridPosition(0, 4));

            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(new GridPosition(0, 4)));
        }

        /// <summary>Deliberately not streak-gated: an ordinary placement that clears nothing sits between
        /// two batches of clears and adds zero, so the run-wide total still reaches five on schedule.</summary>
        [Test]
        public void TryPlacePiece_WithANonClearingPlacementBetweenClears_StillReachesFiveAndSpawns()
        {
            ClearOneLine(new GridPosition(0, 0));
            ClearOneLine(new GridPosition(0, 1));
            ClearOneLine(new GridPosition(0, 2));

            _trayModel.SetSlot(0, Single, 1);
            var elsewhere = new GridPosition(7, 7);
            Assert.IsTrue(_system.TryPlacePiece(0, elsewhere), "A lone cell placement completes nothing.");
            Assert.IsNull(FindVortex());

            ClearOneLine(new GridPosition(0, 3));
            Assert.IsNull(FindVortex(), "Still only four real clears.");

            ClearOneLine(new GridPosition(0, 4));
            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(new GridPosition(0, 4)));
        }

        /// <summary>The count resets to zero once it spawns a vortex: earning one costs the run nothing
        /// towards the next, and the next five cleared lines earn another.</summary>
        [Test]
        public void TryPlacePiece_AfterASpawnedVortex_CounterResetsAndEarnsAnotherAtTheNextFive()
        {
            ClearOneLine(new GridPosition(0, 0));
            ClearOneLine(new GridPosition(0, 1));
            ClearOneLine(new GridPosition(0, 2));
            ClearOneLine(new GridPosition(0, 3));
            ClearOneLine(new GridPosition(0, 4));
            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(new GridPosition(0, 4)));

            // A fresh batch of five, skipping row 4: the selector hands a cleared row's vortex to
            // column 0 of that row whenever column 0 is available — which, once a row clears, it always
            // is, gap column aside — and row 4's column 0 is unavailable only because it still carries
            // the first vortex's own tile. Any other row's fifth clear lands at its own column 0.
            ClearOneLine(new GridPosition(0, 0));
            ClearOneLine(new GridPosition(0, 1));
            ClearOneLine(new GridPosition(0, 2));
            ClearOneLine(new GridPosition(0, 3));
            Assert.AreEqual(
                SpecialCellKind.None, _boardModel.GetSpecialKind(new GridPosition(0, 3)),
                "Only four of the new batch have cleared so far.");

            ClearOneLine(new GridPosition(0, 5));
            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(new GridPosition(0, 5)));
        }

        // --- Island fill (issue #349) ---

        /// <summary>AC1: destroying a vortex fills every fully-enclosed island present on the board.</summary>
        [Test]
        public void TryPlacePiece_DestroyingAVortexWithAnIsland_FillsIt()
        {
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Vortex);

            // A one-cell island, boxed in on every in-board side, far from row 5.
            OccupyRing(2, 2, 4, 4);
            var island = new GridPosition(3, 3);

            _system.TryPlacePiece(0, Gap);

            Assert.IsTrue(_boardModel.GetCell(island) != Board.EMPTY, "The island was reclaimed.");
            Assert.AreEqual(1, _islandFilledBroker.Published.Count);
            Assert.AreEqual(1, _islandFilledBroker.Published[0].FilledCells.Count);
            Assert.AreEqual(island, _islandFilledBroker.Published[0].FilledCells[0]);
            Assert.AreEqual(0, _islandFilledBroker.Published[0].HandOffTargets.Count);
        }

        /// <summary>A destroyed vortex with no island on the board hands its tag off instead, and
        /// publishes the hand-off rather than a fill.</summary>
        [Test]
        public void TryPlacePiece_DestroyingAVortexWithNoIsland_HandsOffToAnEligibleCell()
        {
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Vortex);

            var eligible = new GridPosition(0, 0);
            _boardModel.Occupy(eligible, 2);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(eligible));
            Assert.AreEqual(1, _islandFilledBroker.Published.Count);
            Assert.AreEqual(0, _islandFilledBroker.Published[0].FilledCells.Count);
            Assert.AreEqual(1, _islandFilledBroker.Published[0].HandOffTargets.Count);
            Assert.AreEqual(eligible, _islandFilledBroker.Published[0].HandOffTargets[0]);
        }

        /// <summary>No island and no eligible cell either publishes nothing at all — subscribers read the
        /// message itself as "the vortex did something".</summary>
        [Test]
        public void TryPlacePiece_DestroyingAVortexWithNoIslandAndNoEligibleCell_PublishesNothing()
        {
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Vortex);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(0, _islandFilledBroker.Published.Count);
        }

        [Test]
        public void TryPlacePiece_WithNoVortexInvolved_PublishesNothing()
        {
            FillRowExcept(y: 5, Gap);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(0, _islandFilledBroker.Published.Count);
        }

        // --- The Demolition Hammer path (issue #156, AC2 / issue #349) ---

        /// <summary>A vortex destroyed by a hammer fills the board's islands exactly as one destroyed by
        /// a completed line does. A hammer destroys a single cell and completes no line, so nothing but
        /// the fill itself can explain the island having been reclaimed.</summary>
        [Test]
        public void TryUseDemolitionHammer_OnAVortexWithAnIsland_FillsIt()
        {
            var vortex = new GridPosition(4, 4);
            _boardModel.Occupy(vortex, 1);
            _boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            OccupyRing(0, 0, 2, 2);
            var island = new GridPosition(1, 1);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            Assert.IsTrue(_system.TryUseDemolitionHammer(0, vortex));

            Assert.IsTrue(_boardModel.GetCell(island) != Board.EMPTY, "The island was reclaimed.");
            Assert.AreEqual(1, _islandFilledBroker.Published.Count);
            Assert.AreEqual(1, _islandFilledBroker.Published[0].FilledCells.Count);
            Assert.AreEqual(island, _islandFilledBroker.Published[0].FilledCells[0]);
        }

        /// <summary>The same hand-off contract on the hammer path: no island hands the tag off instead.</summary>
        [Test]
        public void TryUseDemolitionHammer_OnAVortexWithNoIsland_HandsOffToAnEligibleCell()
        {
            var vortex = new GridPosition(4, 4);
            _boardModel.Occupy(vortex, 1);
            _boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            var eligible = new GridPosition(0, 0);
            _boardModel.Occupy(eligible, 2);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            Assert.IsTrue(_system.TryUseDemolitionHammer(0, vortex));

            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(eligible));
            Assert.AreEqual(1, _islandFilledBroker.Published.Count);
            Assert.AreEqual(1, _islandFilledBroker.Published[0].HandOffTargets.Count);
        }

        /// <summary>A hammer swung at an ordinary cell reclaims nothing: the effect belongs to the tile,
        /// not to the hammer.</summary>
        [Test]
        public void TryUseDemolitionHammer_OnAnOrdinaryCell_PublishesNothing()
        {
            var target = new GridPosition(4, 4);
            _boardModel.Occupy(target, 1);
            _boardModel.Occupy(new GridPosition(0, 0), 2);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            Assert.IsTrue(_system.TryUseDemolitionHammer(0, target));

            Assert.AreEqual(0, _islandFilledBroker.Published.Count);
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, 0)), "Left alone.");
        }

        /// <summary>
        /// AC3's hammer half, recorded as it actually stands rather than as the acceptance criterion
        /// reads: a hammer destroys a score gem outright, and nothing scores it.
        /// <para>
        /// A hammer swing publishes no score-bearing message at all — no <c>PiecePlacedMessage</c> (it is
        /// not a placement) and no <c>PowerUpAppliedMessage</c> (it is not a power-up, and never enters
        /// the inventory) — so there is no gain for a gem's factor to multiply. The gem's <em>count</em>
        /// is nonetheless available: the swing already collects its triggers through the same
        /// <c>SpecialCellDetection</c> pass every destroying path uses, so a later issue that gives the
        /// hammer a score has the figure in hand. Inventing that score here would be inventing a reward
        /// this issue never asked for, which is why this test asserts the destruction and no more.
        /// </para>
        /// </summary>
        [Test]
        public void TryUseDemolitionHammer_OnAScoreGem_DestroysItAndLeavesTheBoardOtherwiseAlone()
        {
            var gem = new GridPosition(4, 4);
            _boardModel.Occupy(gem, 1);
            _boardModel.SetSpecialKind(gem, SpecialCellKind.ScoreGem);

            var bystander = new GridPosition(0, 0);
            _boardModel.Occupy(bystander, 2);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            Assert.IsTrue(_system.TryUseDemolitionHammer(0, gem));

            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(gem), "The gem cell went.");
            Assert.AreEqual(SpecialCellKind.None, _boardModel.GetSpecialKind(gem), "And so did its kind.");

            // A gem destroys nothing at all, so the bystander is untouched.
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(bystander));
            Assert.AreEqual(0, _islandFilledBroker.Published.Count);
        }

        /// <summary>AC6: a snapshot taken before the placement restores the board exactly, vortex tiles
        /// and reclaimed islands included. Rides entirely on <c>Board.Clone</c>/<c>CopyFrom</c> being
        /// kind-agnostic, which holds because the effect mutates nothing outside the board's own cells;
        /// stated explicitly because the acceptance criterion is about this kind specifically.</summary>
        [Test]
        public void ABoardSnapshot_TakenBeforeAPlacementThatFills_RestoresTheBoardExactly()
        {
            FillRowExcept(y: 5, Gap);
            var vortex = new GridPosition(0, 5);
            _boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            OccupyRing(2, 2, 4, 4);
            var island = new GridPosition(3, 3);

            Board snapshot = _boardModel.Board.Clone();
            Assert.AreEqual(SpecialCellKind.Vortex, snapshot.GetSpecialKind(vortex), "The clone carries the kind.");
            Assert.AreEqual(Board.EMPTY, snapshot[island], "The clone carries the island's empty cell too.");

            _system.TryPlacePiece(0, Gap);
            Assert.IsTrue(_boardModel.GetCell(island) != Board.EMPTY, "The fill happened.");

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

            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(vortex), "The vortex is back.");
            Assert.AreEqual(Board.EMPTY, _boardModel.Board[island], "And the island is empty again.");
        }

        /// <summary>Fills <paramref name="gap"/>'s row around it and places a fresh single there,
        /// completing and clearing that one row — the run-wide-line-count test's one building block.</summary>
        private void ClearOneLine(GridPosition gap)
        {
            FillRowExcept(gap.Y, gap);
            _trayModel.SetSlot(0, Single, 1);
            Assert.IsTrue(_system.TryPlacePiece(0, gap), $"expected {gap} to complete its row.");
        }

        private GridPosition? FindVortex()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_boardModel.GetSpecialKind(position) == SpecialCellKind.Vortex)
                    {
                        return position;
                    }
                }
            }

            return null;
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

        /// <summary>Occupies the border ring of the [minX..maxX] x [minY..maxY] rectangle, leaving its
        /// centre cell empty and boxed in on every side — a one-cell island. The rectangle must be at
        /// least 3x3 for a centre to exist.</summary>
        private void OccupyRing(int minX, int minY, int maxX, int maxY)
        {
            int centerX = (minX + maxX) / 2;
            int centerY = (minY + maxY) / 2;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (x == centerX && y == centerY)
                    {
                        continue;
                    }

                    _boardModel.Occupy(new GridPosition(x, y), 1);
                }
            }
        }
    }
}
