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
    /// End-to-end cover for the placement side of the vortex: the occupancy the spawn rule is read
    /// against is the pre-clear one, a destroyed vortex drags the board's strays inwards, the pulls are
    /// reported, and a snapshot taken beforehand restores everything.
    /// </summary>
    public class BoardSystemVortexTests
    {
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        /// <summary>The gap every test's placement fills, completing row 5.</summary>
        private static readonly GridPosition Gap = new GridPosition(3, 5);

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<VortexPulledMessage> _pulledBroker;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _pulledBroker = new TestMessageBroker<VortexPulledMessage>();

            // Deliberately unstarted, as BoardSystemLaserTests is: StartNewRun would draw over the board
            // and dock each test lays out by hand.
            _system = new BoardSystem(
                _boardModel,
                _trayModel,
                new ScoreGemProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                _pulledBroker,
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

        /// <summary>AC1/AC3: a clear on a board that was more than 80% full always spawns a vortex, on a
        /// cell the clear itself emptied, with no roll deciding whether it appears.</summary>
        [Test]
        public void TryPlacePiece_ClearingALineOnACrowdedBoard_SpawnsAVortex()
        {
            LayOutCrowdedBoard();

            bool placed = _system.TryPlacePiece(0, Gap);

            Assert.IsTrue(placed);
            var spawn = new GridPosition(0, 5);
            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(spawn));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(spawn), "The tile needs a block to sit on.");
        }

        /// <summary>The occupancy that matters is the one from before the clear. Measuring it afterwards
        /// would read the board the reward exists to rescue the player from, which by then is exactly
        /// the board they are no longer on — and the reward would never fire.</summary>
        [Test]
        public void TryPlacePiece_OnACrowdedBoard_ReadsOccupancyFromBeforeTheClear()
        {
            LayOutCrowdedBoard();

            _system.TryPlacePiece(0, Gap);

            int occupiedAfter = _boardModel.Board.OccupiedCellCount();
            float threshold = VortexSpawnSelector.OCCUPANCY_THRESHOLD * _boardModel.Board.PlayableCellCount;
            Assert.IsTrue(
                occupiedAfter <= threshold,
                $"The post-clear board holds {occupiedAfter} of {threshold:0.0}, so only a pre-clear reading can have fired.");
            Assert.AreEqual(SpecialCellKind.Vortex, _boardModel.GetSpecialKind(new GridPosition(0, 5)));
        }

        /// <summary>The other side of the threshold: the same clear on a board that was nowhere near full
        /// earns nothing.</summary>
        [Test]
        public void TryPlacePiece_ClearingALineOnASparseBoard_SpawnsNothing()
        {
            FillRowExcept(y: 5, Gap);

            _system.TryPlacePiece(0, Gap);

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(
                    SpecialCellKind.None,
                    _boardModel.GetSpecialKind(new GridPosition(x, 5)),
                    $"({x}, 5) should carry no kind.");
            }
        }

        /// <summary>A crowded board is not enough on its own: the reward is for a clear.</summary>
        [Test]
        public void TryPlacePiece_OnACrowdedBoardWithoutClearingALine_SpawnsNothing()
        {
            LayOutCrowdedBoard();

            // Row 4 keeps its other empty cell and column 5 keeps its other one, so filling this
            // completes nothing.
            var elsewhere = new GridPosition(5, 4);

            _system.TryPlacePiece(0, elsewhere);

            Assert.AreEqual(SpecialCellKind.None, _boardModel.GetSpecialKind(elsewhere));
            Assert.AreEqual(SpecialCellKind.None, _boardModel.GetSpecialKind(new GridPosition(0, 5)));
        }

        /// <summary>AC2: destroying one drags every isolated block one cell towards where it stood, and
        /// empties the cell each of them vacated.</summary>
        [Test]
        public void TryPlacePiece_DestroyingAVortex_PullsTheIsolatedBlocksInwards()
        {
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Vortex);

            var stray = new GridPosition(0, 0);
            _boardModel.Occupy(stray, 2);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(stray), "The cell it left is empty.");
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(new GridPosition(0, 1)), "One step inwards.");

            Assert.AreEqual(1, _pulledBroker.Published.Count);
            Assert.AreEqual(1, _pulledBroker.Published[0].Pulls.Count);
            Assert.AreEqual(stray, _pulledBroker.Published[0].Pulls[0].From);
            Assert.AreEqual(new GridPosition(0, 1), _pulledBroker.Published[0].Pulls[0].To);
        }

        /// <summary>A block with an occupied neighbour is not isolated and is left exactly where it is,
        /// so a vortex that finds nothing to move publishes nothing — subscribers treat the message
        /// itself as "blocks moved".</summary>
        [Test]
        public void TryPlacePiece_DestroyingAVortexWithNothingIsolated_PublishesNothing()
        {
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Vortex);

            var left = new GridPosition(0, 0);
            var right = new GridPosition(1, 0);
            _boardModel.Occupy(left, 2);
            _boardModel.Occupy(right, 2);

            _system.TryPlacePiece(0, Gap);

            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(left));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(right));
            Assert.AreEqual(0, _pulledBroker.Published.Count);
        }

        [Test]
        public void TryPlacePiece_WithNoVortexInvolved_PublishesNothing()
        {
            FillRowExcept(y: 5, Gap);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(0, _pulledBroker.Published.Count);
        }

        // --- The Demolition Hammer path (issue #156, AC2) ---

        /// <summary>AC2: a vortex destroyed by a hammer drags the board's isolated blocks inwards exactly
        /// as one destroyed by a completed line does. A hammer destroys a single cell and completes no
        /// line, so nothing but the pull itself can explain the stray having moved.</summary>
        [Test]
        public void TryUseDemolitionHammer_OnAVortex_PullsTheIsolatedBlocksInwards()
        {
            var vortex = new GridPosition(4, 4);
            _boardModel.Occupy(vortex, 1);
            _boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            var stray = new GridPosition(0, 0);
            _boardModel.Occupy(stray, 2);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            Assert.IsTrue(_system.TryUseDemolitionHammer(0, vortex));

            // Equal distances on both axes, and the tie goes to the horizontal — VortexEffect's fixed
            // rule, so the same board always resolves the same way whatever destroyed the tile.
            var pulledTo = new GridPosition(1, 0);
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(stray), "The cell it left is empty.");
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(pulledTo), "One step inwards.");

            Assert.AreEqual(1, _pulledBroker.Published.Count);
            Assert.AreEqual(1, _pulledBroker.Published[0].Pulls.Count);
            Assert.AreEqual(stray, _pulledBroker.Published[0].Pulls[0].From);
            Assert.AreEqual(pulledTo, _pulledBroker.Published[0].Pulls[0].To);
        }

        /// <summary>The same "the message means blocks moved" contract on the hammer path: a vortex that
        /// found nothing isolated publishes nothing.</summary>
        [Test]
        public void TryUseDemolitionHammer_OnAVortexWithNothingIsolated_PublishesNothing()
        {
            var vortex = new GridPosition(4, 4);
            _boardModel.Occupy(vortex, 1);
            _boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            var left = new GridPosition(0, 0);
            var right = new GridPosition(1, 0);
            _boardModel.Occupy(left, 2);
            _boardModel.Occupy(right, 2);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            Assert.IsTrue(_system.TryUseDemolitionHammer(0, vortex));

            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(left));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(right));
            Assert.AreEqual(0, _pulledBroker.Published.Count);
        }

        /// <summary>A hammer swung at an ordinary cell pulls nothing: the pull belongs to the tile, not
        /// to the hammer.</summary>
        [Test]
        public void TryUseDemolitionHammer_OnAnOrdinaryCell_PublishesNothing()
        {
            var target = new GridPosition(4, 4);
            _boardModel.Occupy(target, 1);
            _boardModel.Occupy(new GridPosition(0, 0), 2);

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            Assert.IsTrue(_system.TryUseDemolitionHammer(0, target));

            Assert.AreEqual(0, _pulledBroker.Published.Count);
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

            // A gem destroys nothing at all, so the bystander is untouched — and, being the only other
            // block, it is isolated, which is what would have moved had a vortex been involved.
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(bystander));
            Assert.AreEqual(0, _pulledBroker.Published.Count);
        }

        /// <summary>AC6: a snapshot taken before the placement restores the board exactly, vortex tiles
        /// and pulled blocks included. Rides entirely on <c>Board.Clone</c>/<c>CopyFrom</c> being
        /// kind-agnostic, which holds because the effect mutates nothing outside the board's own cells;
        /// stated explicitly because the acceptance criterion is about this kind specifically.</summary>
        [Test]
        public void ABoardSnapshot_TakenBeforeAPlacementThatPulls_RestoresTheBoardExactly()
        {
            FillRowExcept(y: 5, Gap);
            var vortex = new GridPosition(0, 5);
            _boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            var stray = new GridPosition(0, 0);
            _boardModel.Occupy(stray, 2);

            Board snapshot = _boardModel.Board.Clone();
            Assert.AreEqual(SpecialCellKind.Vortex, snapshot.GetSpecialKind(vortex), "The clone carries the kind.");

            _system.TryPlacePiece(0, Gap);
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(stray), "The pull happened.");

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
        }

        /// <summary>
        /// A board 54/64 full with exactly one line one cell short of complete.
        /// <para>
        /// The ten empty cells are placed so that every row other than 5 and every column keeps at
        /// least one of them: filling <see cref="Gap"/> must complete row 5 and nothing else, or a second
        /// line clearing would change which cells the spawn rule has to choose from.
        /// </para>
        /// </summary>
        private void LayOutCrowdedBoard()
        {
            var empties = new[]
            {
                Gap,
                new GridPosition(3, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 2),
                new GridPosition(2, 3),
                new GridPosition(4, 4),
                new GridPosition(5, 4),
                new GridPosition(5, 6),
                new GridPosition(6, 7),
                new GridPosition(7, 7),
            };

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (!Contains(empties, position))
                    {
                        _boardModel.Occupy(position, 1);
                    }
                }
            }
        }

        private static bool Contains(GridPosition[] cells, GridPosition position)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].X == position.X && cells[i].Y == position.Y)
                {
                    return true;
                }
            }

            return false;
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
    }
}
