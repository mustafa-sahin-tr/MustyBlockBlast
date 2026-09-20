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
    /// completed line finishes off a near-complete line elsewhere, hands off when nothing qualifies, and
    /// re-triggers a second core it chains into.
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
                new TestMessageBroker<VortexPulledMessage>(),
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

        /// <summary>AC1/AC3: a core destroyed by a completed line finishes off a different row that was
        /// one cell short, and the detonation is reported once with the line it finished.</summary>
        [Test]
        public void TryPlacePiece_CompletingALineThatHoldsACore_FinishesANearCompleteLineElsewhereAndReportsIt()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            // Row 0 is one cell short of full; nothing else on the board is.
            FillRowExcept(y: 0, new GridPosition(2, 0));

            _system.TryPlacePiece(0, gap);

            Assert.IsFalse(_boardModel.Board.IsOccupied(new GridPosition(1, 0)), "Row 0 was finished, then cleared.");
            Assert.IsFalse(_boardModel.Board.IsOccupied(new GridPosition(2, 0)), "The gap the core filled.");

            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(1, _detonatedBroker.Published[0].FinishedLineCount);
            Assert.AreEqual(0, _detonatedBroker.Published[0].HandOffCount);
        }

        /// <summary>AC4: a core destroyed with nothing on the board one cell short hands its kind off to
        /// the one other occupied, not-yet-special cell instead of doing nothing.</summary>
        [Test]
        public void TryPlacePiece_CompletingALineThatHoldsACoreWithNothingToFinish_HandsOffInstead()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            var handOffTarget = new GridPosition(7, 7);
            _boardModel.Occupy(handOffTarget, 1);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(SpecialCellKind.ExplosiveCore, _boardModel.GetSpecialKind(handOffTarget));
            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(0, _detonatedBroker.Published[0].FinishedLineCount);
            Assert.AreEqual(1, _detonatedBroker.Published[0].HandOffCount);
        }

        /// <summary>
        /// AC5: a second core caught inside the line the first core's fill completes detonates in turn —
        /// through <see cref="CascadeClearResolver"/>'s ordinary mechanism, with nothing here re-invoking
        /// the effect by hand. Proven by the second core's own consequence actually happening: with
        /// nothing left on the board to finish once row 0 clears, it hands off to the one remaining
        /// occupied cell. If the chain never fired, that cell would still carry no special kind at all.
        /// </summary>
        [Test]
        public void TryPlacePiece_WithASecondCoreChainedIntoTheFinishedLine_ReTriggersItsOwnEffect()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            // Row 0 is one cell short of full, and carries a second core of its own.
            FillRowExcept(y: 0, new GridPosition(2, 0));
            _boardModel.SetSpecialKind(new GridPosition(0, 0), SpecialCellKind.ExplosiveCore);

            // The one cell left standing anywhere once both rows are gone.
            var handOffTarget = new GridPosition(7, 7);
            _boardModel.Occupy(handOffTarget, 1);

            _system.TryPlacePiece(0, gap);

            Assert.IsTrue(_boardModel.Board.IsEmpty() == false, "The hand-off target is still standing.");
            Assert.AreEqual(SpecialCellKind.ExplosiveCore, _boardModel.GetSpecialKind(handOffTarget));

            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(1, _detonatedBroker.Published[0].FinishedLineCount, "Row 0, finished by the first core.");
            Assert.AreEqual(1, _detonatedBroker.Published[0].HandOffCount, "The second core's own consequence.");
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

        /// <summary>AC6 negative case: a board with no full-or-one-short line anywhere else is left
        /// completely alone by the core's own scan, beyond the line the placement itself completed.</summary>
        [Test]
        public void TryPlacePiece_CompletingALineThatHoldsACoreWithNoQualifyingLineAndNoHandOffTarget_DoesNotThrow()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            Assert.DoesNotThrow(() => _system.TryPlacePiece(0, gap));

            Assert.AreEqual(0, _detonatedBroker.Published.Count);
            Assert.IsTrue(_boardModel.Board.IsEmpty());
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
