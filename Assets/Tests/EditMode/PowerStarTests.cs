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
    /// Issue #482: a power star is charged — not removed — by the line clears through it, +1 per line
    /// (+2 where a cleared row and column cross on it), and at <see cref="Board.POWER_STAR_BURST_CHARGE"/>
    /// it is destroyed and bursts the 3x3 around it. Destroyed outright any other way, it bursts at once.
    /// </summary>
    public sealed class PowerStarTests
    {
        private const int COLOUR = 2;

        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        [Test]
        public void ResolveClears_ThroughAStar_ChargesItByOne_AndLeavesItStanding()
        {
            var board = new Board();
            var star = new GridPosition(3, 0);
            board.OccupyPowerStar(star, COLOUR);
            FillRow(board, 0);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(1, result.LineCount);
            Assert.IsTrue(board.IsOccupied(star));
            Assert.AreEqual(SpecialCellKind.PowerStar, board.GetSpecialKind(star));
            Assert.AreEqual(1, board.GetPowerStarCharge(star));
            Assert.AreEqual(Board.SIZE - 1, result.ClearedCellCount, "The star is not a destroyed cell.");
            Assert.IsFalse(board.IsOccupied(new GridPosition(0, 0)));
        }

        [Test]
        public void ResolveClears_WithARowAndAColumnCrossingOnAStar_ChargesItByTwo()
        {
            var board = new Board();
            var star = new GridPosition(3, 3);
            board.OccupyPowerStar(star, COLOUR);
            FillRow(board, 3);
            FillColumn(board, 3);

            LineClearResolver.ResolveClears(board);

            Assert.IsTrue(board.IsOccupied(star));
            Assert.AreEqual(2, board.GetPowerStarCharge(star));
        }

        [Test]
        public void ResolveClears_NotTouchingTheStar_ChargesNothing()
        {
            var board = new Board();
            var star = new GridPosition(3, 3);
            board.OccupyPowerStar(star, COLOUR);
            FillRow(board, 5);

            LineClearResolver.ResolveClears(board);

            Assert.AreEqual(0, board.GetPowerStarCharge(star));
        }

        [Test]
        public void ResolveCascade_ThatBringsAStarToFullCharge_BurstsItsThreeByThree()
        {
            var board = new Board();
            var star = new GridPosition(3, 3);
            board.OccupyPowerStar(star, COLOUR);
            board.ChargePowerStar(star, Board.POWER_STAR_BURST_CHARGE - 1);
            FillRow(board, 3);
            board.Occupy(new GridPosition(2, 2), COLOUR);
            board.Occupy(new GridPosition(4, 4), COLOUR);
            board.Occupy(new GridPosition(3, 4), COLOUR);
            var outside = new GridPosition(5, 5);
            board.Occupy(outside, COLOUR);

            PowerStarEffect effect = CreateEffect();
            CascadeClearResolver.ResolveCascade(board, effect);

            Assert.IsFalse(board.IsOccupied(star));
            Assert.IsFalse(board.IsOccupied(new GridPosition(2, 2)));
            Assert.IsFalse(board.IsOccupied(new GridPosition(4, 4)));
            Assert.IsFalse(board.IsOccupied(new GridPosition(3, 4)));
            Assert.IsTrue(board.IsOccupied(outside), "Only the 3x3 bursts.");
            Assert.AreEqual(3, effect.BurstCells.Count);
        }

        [Test]
        public void Apply_ToAStarDestroyedOutright_BurstsAtOnceWhateverItsCharge_AndSkipsOffBoardCells()
        {
            var board = new Board();
            var corner = new GridPosition(0, 0);
            board.Occupy(new GridPosition(1, 1), COLOUR);
            board.Occupy(new GridPosition(0, 1), COLOUR);

            PowerStarEffect effect = CreateEffect();
            effect.Apply(board, new SpecialCellTrigger(corner, SpecialCellKind.PowerStar));

            Assert.IsFalse(board.IsOccupied(new GridPosition(1, 1)));
            Assert.IsFalse(board.IsOccupied(new GridPosition(0, 1)));
            Assert.AreEqual(2, effect.BurstCells.Count);
        }

        [Test]
        public void Apply_WhenABurstDestroysASecondStar_ItBurstsInTurn()
        {
            var board = new Board();
            var second = new GridPosition(4, 4);
            board.OccupyPowerStar(second, COLOUR);
            var reachedOnlyBySecond = new GridPosition(5, 5);
            board.Occupy(reachedOnlyBySecond, COLOUR);

            PowerStarEffect effect = CreateEffect();
            effect.Apply(board, new SpecialCellTrigger(new GridPosition(3, 3), SpecialCellKind.PowerStar));

            Assert.IsFalse(board.IsOccupied(second));
            Assert.IsFalse(board.IsOccupied(reachedOnlyBySecond));
        }

        [Test]
        public void CloneAndCopyFrom_CarryTheCharge()
        {
            var board = new Board();
            var star = new GridPosition(3, 3);
            board.OccupyPowerStar(star, COLOUR);
            board.ChargePowerStar(star, 2);

            Board copy = board.Clone();
            Assert.AreEqual(2, copy.GetPowerStarCharge(star));

            var restored = new Board();
            restored.CopyFrom(board);
            Assert.AreEqual(2, restored.GetPowerStarCharge(star));

            board.Clear(star);
            Assert.AreEqual(0, board.GetPowerStarCharge(star), "The charge goes with the star.");
            Assert.AreEqual(2, copy.GetPowerStarCharge(star), "The copy is independent.");
        }

        /// <summary>End to end through a real placement: the row that brings a 2/3 star to full charge
        /// clears, and the burst empties the star's 3x3 on the board the player sees.</summary>
        [Test]
        public void TryPlacePiece_CompletingARowThroughAChargedStar_BurstsIt()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel);
            var star = new GridPosition(3, 3);
            boardModel.OccupyPowerStar(star, COLOUR);
            boardModel.Board.ChargePowerStar(star, Board.POWER_STAR_BURST_CHARGE - 1);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                if (x != star.X)
                {
                    boardModel.Occupy(new GridPosition(x, 3), COLOUR);
                }
            }

            var neighbour = new GridPosition(3, 4);
            boardModel.Occupy(neighbour, COLOUR);

            trayModel.SetSlot(0, Single, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(star));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(neighbour));
        }

        private static PowerStarEffect CreateEffect()
        {
            var effect = new PowerStarEffect();
            effect.SetChain(new CompositeSpecialCellEffect(effect));
            effect.BeginResolution();
            return effect;
        }

        private static void FillRow(Board board, int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (!board.IsOccupied(position))
                {
                    board.Occupy(position, COLOUR);
                }
            }
        }

        private static void FillColumn(Board board, int x)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                var position = new GridPosition(x, y);
                if (!board.IsOccupied(position))
                {
                    board.Occupy(position, COLOUR);
                }
            }
        }

        private static BoardSystem CreateSystem(out BoardModel boardModel, out TrayModel trayModel)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
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
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1);
        }
    }
}
