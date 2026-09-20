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
    /// End-to-end cover for the placement side of the "Perfect Match" bonus (issue #352): a shaped piece
    /// consumed whole by its own placement spawns an explosive core on a random occupied cell, a single
    /// cell or a straight line never does regardless of how completely it clears, a partial self-clear
    /// never does either, and the reward stacks with the existing cross-clear core reward without the two
    /// ever landing on the same cell.
    /// </summary>
    public class BoardSystemPerfectMatchTests
    {
        private static readonly Piece Square2X2 = new Piece("square_2x2", Rectangle(2, 2));
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });
        private static readonly Piece LineH2 = new Piece("line_h2", HorizontalRun(2));

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();

            // Deliberately unstarted, as BoardSystemExplosiveCoreTests is: StartNewRun would draw over
            // the board and dock each test lays out by hand.
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
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1);
        }

        /// <summary>AC1/AC2: a 2x2 square consumed whole by the two rows it completes spawns an
        /// explosive core, borrowing an already-occupied cell rather than needing one of its own.</summary>
        [Test]
        public void TryPlacePiece_WithAShapedPieceFullyClearedByItsOwnRows_SpawnsAnExplosiveCore()
        {
            FillAllThreeSlots(Square2X2);

            // A single cell outside both cleared rows is the "already-occupied cell" the reward is
            // meant to borrow — without one, both rows clearing entirely leaves nothing on the board
            // for the spawn selector to land on, and the reward is correctly (per AC3) skipped.
            _boardModel.Occupy(new GridPosition(0, 0), 1);

            // Rows 3 and 4 each miss only the piece's own footprint; no column ever fills, so the
            // existing cross-clear reward and the score gem's progress counter both stay untouched.
            FillRowExceptGap(y: 3, gapFromX: 3, gapToX: 4);
            FillRowExceptGap(y: 4, gapFromX: 3, gapToX: 4);

            bool placed = _system.TryPlacePiece(0, new GridPosition(3, 3));

            Assert.IsTrue(placed);
            Assert.AreEqual(1, CountCellsOfKind(SpecialCellKind.ExplosiveCore));
        }

        /// <summary>AC6/negative: a 1x1 piece whose one cell clears its row is still not the "shaped"
        /// move this rewards.</summary>
        [Test]
        public void TryPlacePiece_WithASingleCellPieceFullyCleared_SpawnsNothing()
        {
            FillAllThreeSlots(Single);
            var gap = new GridPosition(3, 5);
            FillRowExceptGap(y: 5, gapFromX: 3, gapToX: 3);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(0, CountCellsOfKind(SpecialCellKind.ExplosiveCore));
        }

        /// <summary>AC7/negative: a straight line fully consumed by its own clear does not qualify,
        /// regardless of length.</summary>
        [Test]
        public void TryPlacePiece_WithAStraightLineFullyCleared_SpawnsNothing()
        {
            FillAllThreeSlots(LineH2);
            FillRowExceptGap(y: 5, gapFromX: 0, gapToX: 1);

            _system.TryPlacePiece(0, new GridPosition(0, 5));

            Assert.AreEqual(0, CountCellsOfKind(SpecialCellKind.ExplosiveCore));
        }

        /// <summary>AC8/negative: only one of the square's two rows clears, so only two of its four own
        /// cells are covered by the placement's clear — not the whole piece.</summary>
        [Test]
        public void TryPlacePiece_WithOnlySomeOfItsOwnCellsCleared_SpawnsNothing()
        {
            FillAllThreeSlots(Square2X2);

            // Only row 3 is one placement away from full; row 4 stays far from complete, so (3,4) and
            // (4,4) are covered by neither a cleared row nor a cleared column.
            FillRowExceptGap(y: 3, gapFromX: 3, gapToX: 4);

            _system.TryPlacePiece(0, new GridPosition(3, 3));

            Assert.AreEqual(0, CountCellsOfKind(SpecialCellKind.ExplosiveCore));
        }

        /// <summary>AC9/negative: a qualifying shape placed on an otherwise-empty board clears nothing at
        /// all, so there is no clear for any of its cells to belong to.</summary>
        [Test]
        public void TryPlacePiece_WithAQualifyingShapeThatClearsNothing_SpawnsNothing()
        {
            FillAllThreeSlots(Square2X2);

            _system.TryPlacePiece(0, new GridPosition(0, 0));

            Assert.AreEqual(0, CountCellsOfKind(SpecialCellKind.ExplosiveCore));
        }

        /// <summary>
        /// AC4/stacking: a placement that closes two rows and two columns at once earns both the
        /// existing cross-clear core (at their intersection) and this placement's own Perfect Match core
        /// (on a surviving occupied cell elsewhere) — two cores, on two different cells, never one
        /// overwriting the other.
        /// </summary>
        [Test]
        public void TryPlacePiece_QualifyingForBothCrossClearAndPerfectMatch_SpawnsTwoCoresOnDifferentCells()
        {
            FillAllThreeSlots(Square2X2);

            // A 2-wide cross through (3..4, 3..4): rows 3 and 4 and columns 3 and 4 are each one 2x2
            // gap away from full. Three extra blocks outside the cross survive the clear as candidates
            // for the Perfect Match spawn.
            FillRowExceptGap(y: 3, gapFromX: 3, gapToX: 4);
            FillRowExceptGap(y: 4, gapFromX: 3, gapToX: 4);
            FillColumnExceptGap(x: 3, gapFromY: 3, gapToY: 4);
            FillColumnExceptGap(x: 4, gapFromY: 3, gapToY: 4);
            var survivorA = new GridPosition(0, 0);
            var survivorB = new GridPosition(1, 1);
            var survivorC = new GridPosition(6, 6);
            _boardModel.Occupy(survivorA, 1);
            _boardModel.Occupy(survivorB, 1);
            _boardModel.Occupy(survivorC, 1);

            bool placed = _system.TryPlacePiece(0, new GridPosition(3, 3));

            Assert.IsTrue(placed);
            Assert.AreEqual(2, CountCellsOfKind(SpecialCellKind.ExplosiveCore), "One per reward.");

            // The cross-clear reward always lands on the first cleared column/row's intersection.
            var crossClearCell = new GridPosition(3, 3);
            Assert.AreEqual(SpecialCellKind.ExplosiveCore, _boardModel.GetSpecialKind(crossClearCell));

            // The Perfect Match reward must have landed on one of the surviving cells, never on the
            // cross-clear reward's own cell.
            bool perfectMatchLandedOnASurvivor =
                _boardModel.GetSpecialKind(survivorA) == SpecialCellKind.ExplosiveCore
                || _boardModel.GetSpecialKind(survivorB) == SpecialCellKind.ExplosiveCore
                || _boardModel.GetSpecialKind(survivorC) == SpecialCellKind.ExplosiveCore;
            Assert.IsTrue(perfectMatchLandedOnASurvivor);
        }

        /// <summary>AC3: a qualifying placement that leaves no occupied, not-yet-special cell once the
        /// cross-clear reward has claimed the only one available is silently skipped — no crash, no
        /// second core squeezed in anywhere.</summary>
        [Test]
        public void TryPlacePiece_WithNoEligibleCellLeftAfterTheCrossClearReward_DoesNotThrowAndSpawnsOnlyOne()
        {
            FillAllThreeSlots(Square2X2);

            // Every occupied cell on the board belongs to the cross that clears; nothing survives.
            FillRowExceptGap(y: 3, gapFromX: 3, gapToX: 4);
            FillRowExceptGap(y: 4, gapFromX: 3, gapToX: 4);
            FillColumnExceptGap(x: 3, gapFromY: 3, gapToY: 4);
            FillColumnExceptGap(x: 4, gapFromY: 3, gapToY: 4);

            Assert.DoesNotThrow(() => _system.TryPlacePiece(0, new GridPosition(3, 3)));

            Assert.AreEqual(1, CountCellsOfKind(SpecialCellKind.ExplosiveCore), "Only the cross-clear reward.");
        }

        /// <summary>All three slots filled: these placements must not empty the dock, because the refill
        /// that follows an emptied one can spawn its own reward on a random occupied cell — including
        /// the one this feature's own spawn just chose.</summary>
        private void FillAllThreeSlots(Piece piece)
        {
            _trayModel.SetSlot(0, piece, 1);
            _trayModel.SetSlot(1, piece, 1);
            _trayModel.SetSlot(2, piece, 1);
        }

        private int CountCellsOfKind(SpecialCellKind kind)
        {
            int count = 0;
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    if (_boardModel.GetSpecialKind(new GridPosition(x, y)) == kind)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private void FillRowExceptGap(int y, int gapFromX, int gapToX)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x >= gapFromX && x <= gapToX)
                {
                    continue;
                }

                _boardModel.Occupy(new GridPosition(x, y), 1);
            }
        }

        private void FillColumnExceptGap(int x, int gapFromY, int gapToY)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                if (y >= gapFromY && y <= gapToY)
                {
                    continue;
                }

                _boardModel.Occupy(new GridPosition(x, y), 1);
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
    }
}
