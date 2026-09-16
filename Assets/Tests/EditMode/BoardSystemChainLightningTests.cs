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
    /// End-to-end cover for the placement side of the chain lightning: which shapes earn one, that a
    /// clear is as necessary as the shape, that a destroyed tile takes the right number of cells and says
    /// so, and that a tile caught in another's strike fires through the same resolution.
    /// </summary>
    public class BoardSystemChainLightningTests
    {
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        /// <summary>The real catalog ids, rebuilt here rather than looked up: the spawn rule is defined
        /// on the id, so a test that used a made-up one would prove nothing about the rule.</summary>
        private static readonly Piece Square3X3 = new Piece("square_3x3", Rectangle(3, 3));
        private static readonly Piece LineH5 = new Piece("line_h5", HorizontalRun(5));
        private static readonly Piece LineV5 = new Piece("line_v5", VerticalRun(5));
        private static readonly Piece LineH4 = new Piece("line_h4", HorizontalRun(4));

        /// <summary>The gap the single-cell placements fill, completing row 5.</summary>
        private static readonly GridPosition Gap = new GridPosition(3, 5);

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<ChainLightningTriggeredMessage> _triggeredBroker;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _triggeredBroker = new TestMessageBroker<ChainLightningTriggeredMessage>();

            // Deliberately unstarted, as the laser and vortex tests are: StartNewRun would draw over the
            // board and dock each test lays out by hand.
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
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                _triggeredBroker,
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1);
        }

        /// <summary>AC1: a 1x5 bar that also clears a line spawns a tile, on a cell the clear emptied.</summary>
        [Test]
        public void TryPlacePiece_WithAHorizontal1X5ThatClearsALine_SpawnsAChainLightning()
        {
            FillAllThreeSlots(LineH5);
            FillRowFrom(y: 5, fromX: 0, toX: 2);

            bool placed = _system.TryPlacePiece(0, new GridPosition(3, 5));

            Assert.IsTrue(placed);
            var spawn = new GridPosition(0, 5);
            Assert.AreEqual(SpecialCellKind.ChainLightning, _boardModel.GetSpecialKind(spawn));
            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(spawn), "The tile needs a block to sit on.");
        }

        [Test]
        public void TryPlacePiece_WithAVertical1X5ThatClearsALine_SpawnsAChainLightning()
        {
            FillAllThreeSlots(LineV5);
            FillColumnFrom(x: 4, fromY: 0, toY: 2);

            _system.TryPlacePiece(0, new GridPosition(4, 3));

            Assert.AreEqual(
                SpecialCellKind.ChainLightning, _boardModel.GetSpecialKind(new GridPosition(4, 0)));
        }

        /// <summary>A 3x3 square whose landing completes a row earns one too — the second qualifying
        /// shape, and the reason the rule is stated on ids rather than on cell count.</summary>
        [Test]
        public void TryPlacePiece_WithASquare3X3ThatClearsALine_SpawnsAChainLightning()
        {
            FillAllThreeSlots(Square3X3);

            // Rows 0, 1 and 2 are filled except their last three columns; the square fills exactly those
            // nine cells, so all three rows complete at once.
            for (int y = 0; y <= 2; y++)
            {
                FillRowFrom(y, fromX: 0, toX: 4);
            }

            _system.TryPlacePiece(0, new GridPosition(5, 0));

            Assert.AreEqual(
                SpecialCellKind.ChainLightning, _boardModel.GetSpecialKind(new GridPosition(0, 0)));
        }

        /// <summary>The shape is half the rule: the same bar that clears nothing earns nothing.</summary>
        [Test]
        public void TryPlacePiece_WithAQualifyingPieceThatClearsNothing_SpawnsNothing()
        {
            FillAllThreeSlots(LineH5);

            _system.TryPlacePiece(0, new GridPosition(0, 5));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(
                    SpecialCellKind.None,
                    _boardModel.GetSpecialKind(new GridPosition(x, 5)),
                    $"({x}, 5) should carry no kind.");
            }
        }

        /// <summary>The clear is the other half: a shorter bar that clears a line earns nothing, because
        /// the reward is for the two awkward big pieces specifically.</summary>
        [Test]
        public void TryPlacePiece_WithANonQualifyingPieceThatClearsALine_SpawnsNothing()
        {
            FillAllThreeSlots(LineH4);
            FillRowFrom(y: 5, fromX: 0, toX: 3);

            _system.TryPlacePiece(0, new GridPosition(4, 5));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(
                    SpecialCellKind.None,
                    _boardModel.GetSpecialKind(new GridPosition(x, 5)),
                    $"({x}, 5) should carry no kind.");
            }
        }

        /// <summary>AC2: destroying one vaporizes five of the board's occupied cells and reports how
        /// many. Six blocks are left standing elsewhere, so the Min picks the constant, not the board.</summary>
        [Test]
        public void TryPlacePiece_DestroyingAChainLightning_VaporizesFiveCellsAndPublishesTheCount()
        {
            FillAllThreeSlots(Single);
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ChainLightning);

            // Six blocks in row 0, which is one short of complete, so nothing else can clear.
            FillRowFrom(y: 0, fromX: 0, toX: 5);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(1, _boardModel.Board.OccupiedCellCount(), "Six standing, five taken.");
            Assert.AreEqual(1, _triggeredBroker.Published.Count);
            Assert.AreEqual(
                ChainLightningEffect.MAX_TARGETS_PER_STRIKE,
                _triggeredBroker.Published[0].VaporizedCellCount);
        }

        /// <summary>AC2's other branch: a board holding fewer blocks than the strike wants loses all of
        /// them, and the count reports what actually happened rather than the constant.</summary>
        [Test]
        public void TryPlacePiece_DestroyingAChainLightningWithFewBlocksLeft_VaporizesOnlyWhatIsThere()
        {
            FillAllThreeSlots(Single);
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ChainLightning);

            FillRowFrom(y: 0, fromX: 0, toX: 1);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(0, _boardModel.Board.OccupiedCellCount());
            Assert.AreEqual(2, _triggeredBroker.Published[0].VaporizedCellCount);
        }

        /// <summary>A strike with nothing left to take is not an event: subscribers read the message
        /// itself as "cells were vaporized", so a zero count must never reach them.</summary>
        [Test]
        public void TryPlacePiece_DestroyingAChainLightningWithNothingElseOnTheBoard_PublishesNothing()
        {
            FillAllThreeSlots(Single);
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ChainLightning);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(0, _triggeredBroker.Published.Count);
        }

        [Test]
        public void TryPlacePiece_WithNoChainLightningInvolved_PublishesNothing()
        {
            FillAllThreeSlots(Single);
            FillRowExcept(y: 5, Gap);

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(0, _triggeredBroker.Published.Count);
        }

        /// <summary>
        /// AC5: a strike that catches a second tile chains, and the whole chain is resolved and reported
        /// as one event — the resolution does not stop after the first five cells.
        /// <para>
        /// This is the chain AC5 can actually produce. Vaporizing only ever <em>empties</em> cells, and a
        /// line clears by being full, so a strike can never complete a new line for
        /// <c>CascadeClearResolver</c> to pick up on its next pass; the loop re-checks fullness after the
        /// effect either way, which is what keeps the board consistent here.
        /// </para>
        /// </summary>
        [Test]
        public void TryPlacePiece_WithASecondTileCaughtInTheStrike_ChainsThroughTheSameResolution()
        {
            FillAllThreeSlots(Single);
            FillRowExcept(y: 5, Gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ChainLightning);

            // Seven blocks outside the clearing row, every one of them a tile. The first strike takes
            // five, each of which fires in turn, so the remaining two go with them — a chain that stopped
            // at the first strike would leave them standing.
            FillRowFrom(y: 0, fromX: 0, toX: 6);
            for (int x = 0; x <= 6; x++)
            {
                _boardModel.SetSpecialKind(new GridPosition(x, 0), SpecialCellKind.ChainLightning);
            }

            _system.TryPlacePiece(0, Gap);

            Assert.AreEqual(0, _boardModel.Board.OccupiedCellCount(), "The chain ran to the end of the board.");
            Assert.AreEqual(7, _triggeredBroker.Published[0].VaporizedCellCount, "One event, every cell.");
        }

        /// <summary>AC6's foundation: a snapshot taken before the placement restores the board exactly,
        /// the vaporized cells included. Rides entirely on <c>Board.Clone</c>/<c>CopyFrom</c> being
        /// kind-agnostic, which holds because the effect mutates nothing outside the board's own cells.
        /// The undo System itself does not exist yet; this pins down the part of AC6 that does.</summary>
        [Test]
        public void ABoardSnapshot_TakenBeforeAPlacementThatVaporizes_RestoresTheBoardExactly()
        {
            FillAllThreeSlots(Single);
            FillRowExcept(y: 5, Gap);
            var tile = new GridPosition(0, 5);
            _boardModel.SetSpecialKind(tile, SpecialCellKind.ChainLightning);
            FillRowFrom(y: 0, fromX: 0, toX: 5);

            Board snapshot = _boardModel.Board.Clone();
            Assert.AreEqual(
                SpecialCellKind.ChainLightning, snapshot.GetSpecialKind(tile), "The clone carries the kind.");

            _system.TryPlacePiece(0, Gap);
            Assert.AreEqual(1, _boardModel.Board.OccupiedCellCount(), "The strike happened.");

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
        }

        /// <summary>All three slots filled: these placements must not empty the dock, because the refill
        /// that follows an emptied one can spawn a score gem of its own on a random occupied cell —
        /// including the one this feature's spawn rule just chose.</summary>
        private void FillAllThreeSlots(Piece piece)
        {
            _trayModel.SetSlot(0, piece, 1);
            _trayModel.SetSlot(1, piece, 1);
            _trayModel.SetSlot(2, piece, 1);
        }

        private void FillRowFrom(int y, int fromX, int toX)
        {
            for (int x = fromX; x <= toX; x++)
            {
                _boardModel.Occupy(new GridPosition(x, y), 1);
            }
        }

        private void FillColumnFrom(int x, int fromY, int toY)
        {
            for (int y = fromY; y <= toY; y++)
            {
                _boardModel.Occupy(new GridPosition(x, y), 1);
            }
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

        private static GridPosition[] Rectangle(int width, int height)
        {
            var offsets = new GridPosition[width * height];
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    offsets[index] = new GridPosition(x, y);
                    index++;
                }
            }

            return offsets;
        }

        private static GridPosition[] HorizontalRun(int length)
        {
            var offsets = new GridPosition[length];
            for (int x = 0; x < length; x++)
            {
                offsets[x] = new GridPosition(x, 0);
            }

            return offsets;
        }

        private static GridPosition[] VerticalRun(int length)
        {
            var offsets = new GridPosition[length];
            for (int y = 0; y < length; y++)
            {
                offsets[y] = new GridPosition(0, y);
            }

            return offsets;
        }
    }
}
