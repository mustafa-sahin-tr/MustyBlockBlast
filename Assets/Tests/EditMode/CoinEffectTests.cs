using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers what a destroyed coin cell is worth: the base payout for an ordinary destruction, double
    /// for one at the intersection of a cleared row and column, nothing at all for a cell of another
    /// kind — and nothing on the board, ever.
    /// </summary>
    public class CoinEffectTests
    {
        private const int PAYOUT = 5;

        private Board _board;
        private CoinEffect _effect;

        [SetUp]
        public void CreateEffect()
        {
            _board = new Board();
            _effect = new CoinEffect(PAYOUT);
            _effect.BeginResolution();
        }

        [TestCase(ClearAxis.None)]
        [TestCase(ClearAxis.Row)]
        [TestCase(ClearAxis.Column)]
        public void Apply_ToACoinDestroyedAlongOneAxisOrNone_AwardsTheBaseValue(ClearAxis axis)
        {
            _effect.Apply(_board, Trigger(SpecialCellKind.Coin, axis));

            Assert.AreEqual(PAYOUT, _effect.TotalCoinsAwarded);
        }

        /// <summary>AC2: the intersection of a row and a column closed at once is the one destruction
        /// the player had to set up twice over, and it pays twice.</summary>
        [Test]
        public void Apply_ToACoinDestroyedAtBothAxes_AwardsDoubleTheBaseValue()
        {
            _effect.Apply(_board, Trigger(SpecialCellKind.Coin, ClearAxis.Both));

            Assert.AreEqual(PAYOUT * 2, _effect.TotalCoinsAwarded);
        }

        /// <summary>Several coins in one resolution accumulate, each at its own axis's rate.</summary>
        [Test]
        public void Apply_ToSeveralCoins_SumsTheirIndividualPayouts()
        {
            _effect.Apply(_board, Trigger(SpecialCellKind.Coin, ClearAxis.Row));
            _effect.Apply(_board, Trigger(SpecialCellKind.Coin, ClearAxis.Both));
            _effect.Apply(_board, Trigger(SpecialCellKind.Coin, ClearAxis.Column));

            Assert.AreEqual(PAYOUT + (PAYOUT * 2) + PAYOUT, _effect.TotalCoinsAwarded);
        }

        /// <summary>AC4: an instance pays every time it is destroyed. Nothing here remembers a position,
        /// so the same cell destroyed again in a later resolution is a fresh payout.</summary>
        [Test]
        public void Apply_ToTheSameCellInTwoResolutions_PaysBothTimes()
        {
            _effect.Apply(_board, Trigger(SpecialCellKind.Coin, ClearAxis.Row));
            Assert.AreEqual(PAYOUT, _effect.TotalCoinsAwarded);

            _effect.BeginResolution();
            _effect.Apply(_board, Trigger(SpecialCellKind.Coin, ClearAxis.Row));

            Assert.AreEqual(PAYOUT, _effect.TotalCoinsAwarded, "A coin cell is not a one-time payout.");
        }

        [TestCase(SpecialCellKind.None)]
        [TestCase(SpecialCellKind.ExplosiveCore)]
        [TestCase(SpecialCellKind.Laser)]
        [TestCase(SpecialCellKind.ScoreGem)]
        [TestCase(SpecialCellKind.Vortex)]
        [TestCase(SpecialCellKind.ChainLightning)]
        public void Apply_ToAnyOtherKind_AwardsNothing(SpecialCellKind kind)
        {
            _effect.Apply(_board, Trigger(kind, ClearAxis.Both));

            Assert.AreEqual(0, _effect.TotalCoinsAwarded);
        }

        /// <summary>The reset every resolution depends on: without it two placements' coins would be
        /// reported as one and the player would be paid twice for the first.</summary>
        [Test]
        public void BeginResolution_ForgetsThePreviousResolutionsTotal()
        {
            _effect.Apply(_board, Trigger(SpecialCellKind.Coin, ClearAxis.Both));

            _effect.BeginResolution();

            Assert.AreEqual(0, _effect.TotalCoinsAwarded);
        }

        /// <summary>A coin destroys nothing: the board it is handed comes back exactly as it went in.</summary>
        [Test]
        public void Apply_LeavesTheBoardCompletelyUntouched()
        {
            var occupied = new GridPosition(4, 4);
            _board.Occupy(occupied, 2);

            _effect.Apply(_board, new SpecialCellTrigger(occupied, SpecialCellKind.Coin, ClearAxis.Both));

            Assert.AreEqual(1, _board.OccupiedCellCount());
            Assert.IsTrue(_board.IsOccupied(occupied));
        }

        /// <summary>A zero payout is a legitimate configuration (an economy tuned to pay nothing yet)
        /// and must not become an announced event: nothing is awarded, doubling or not.</summary>
        [Test]
        public void Apply_WithAZeroPayout_AwardsNothingEvenAtBothAxes()
        {
            var freeCoins = new CoinEffect(0);
            freeCoins.BeginResolution();

            freeCoins.Apply(_board, Trigger(SpecialCellKind.Coin, ClearAxis.Both));

            Assert.AreEqual(0, freeCoins.TotalCoinsAwarded);
        }

        /// <summary>
        /// The seam every clear path shares: the cascade loop collects the triggers a phase destroyed
        /// and hands each of them to the installed effect. Every phase goes through this same call, so
        /// a coin destroyed by a cascaded clear pays exactly as one destroyed by the placement's own
        /// clear does (AC3).
        /// </summary>
        [Test]
        public void ResolveCascade_OverAFullRowHoldingACoin_PaysThroughTheEffect()
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                _board.Occupy(new GridPosition(x, 4), 1);
            }

            _board.SetSpecialKind(new GridPosition(2, 4), SpecialCellKind.Coin);

            CascadeClearResult result = CascadeClearResolver.ResolveCascade(_board, _effect);

            Assert.AreEqual(1, result.Primary.LineCount);
            Assert.AreEqual(PAYOUT, _effect.TotalCoinsAwarded);
        }

        [Test]
        public void Apply_WithNoBoard_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _effect.Apply(null, Trigger(SpecialCellKind.Coin, ClearAxis.Row)));
        }

        private static SpecialCellTrigger Trigger(SpecialCellKind kind, ClearAxis axis)
            => new SpecialCellTrigger(new GridPosition(2, 3), kind, axis);
    }
}
