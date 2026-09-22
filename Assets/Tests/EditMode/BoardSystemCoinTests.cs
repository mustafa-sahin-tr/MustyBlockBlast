using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// End-to-end cover for the placement side of the coin cell: a coin taken out by a completed line
    /// announces its payout, one taken out at the intersection of a row and a column announces double,
    /// several add up, a coin nobody destroyed announces nothing at all — and destroying the same cell
    /// again pays again.
    /// </summary>
    public class BoardSystemCoinTests
    {
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        /// <summary>Two cells on a diagonal: the one shape that can complete a row and a column at once
        /// while leaving their intersection cell — the only cell a clear can destroy at
        /// <see cref="ClearAxis.Both"/> — occupied by something placed earlier. Not a catalog piece, and
        /// does not need to be: placement only ever asks whether the cells are free.</summary>
        private static readonly Piece Diagonal = new Piece(
            "test_diagonal", new[] { new GridPosition(0, 0), new GridPosition(1, 1) });

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private CurrencyConfig _config;
        private TestMessageBroker<CoinCellsClearedMessage> _coinCellsBroker;
        private BoardSystem _system;

        private int Payout => _config.CoinCellPayout;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _config = ScriptableObject.CreateInstance<CurrencyConfig>();
            _coinCellsBroker = new TestMessageBroker<CoinCellsClearedMessage>();

            // Deliberately unstarted, as the laser and vortex tests are: StartNewRun would draw over the
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
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                _coinCellsBroker,
                _config,
                seed: 1);

            _trayModel.SetSlot(0, Single, 1);
        }

        [TearDown]
        public void DestroyConfig()
        {
            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        /// <summary>The placeholder payout these tests quote against. Not a claim about the economy —
        /// only that a fresh config pays something, so the rest of the file is asserting a real figure.</summary>
        [Test]
        public void CoinCellPayout_OnAFreshConfig_IsPositive()
        {
            Assert.Greater(Payout, 0);
        }

        /// <summary>AC3's first path: a completed row destroys a coin cell, and the payout is announced
        /// once with the base amount.</summary>
        [Test]
        public void TryPlacePiece_CompletingARowThatHoldsACoin_AnnouncesTheBasePayout()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Coin);

            bool placed = _system.TryPlacePiece(0, gap);

            Assert.IsTrue(placed);
            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(Payout, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>Several coins in one clear are announced as one payout, not one message each: they
        /// were earned by a single placement and are credited by a single write.</summary>
        [Test]
        public void TryPlacePiece_CompletingARowThatHoldsThreeCoins_AnnouncesTheirSumOnce()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Coin);
            _boardModel.SetSpecialKind(new GridPosition(4, 5), SpecialCellKind.Coin);
            _boardModel.SetSpecialKind(new GridPosition(7, 5), SpecialCellKind.Coin);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(1, _coinCellsBroker.Published.Count, "AC5: no cap, but still one payout.");
            Assert.AreEqual(Payout * 3, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>
        /// AC2 through a real placement: a coin standing on the intersection of a row and a column
        /// closed by the same placement pays twice. The diagonal piece closes both lines without
        /// touching the intersection itself, which is what lets the coin be there to begin with.
        /// </summary>
        [Test]
        public void TryPlacePiece_ClosingARowAndAColumnOverACoinAtTheirIntersection_AnnouncesDoubleThePayout()
        {
            var intersection = new GridPosition(3, 5);
            FillRowExcept(y: 5, new GridPosition(4, 5));
            FillColumnExcept(x: 3, new GridPosition(3, 4));
            Assert.IsTrue(_boardModel.Board.IsOccupied(intersection), "Test setup: the coin needs a block.");
            _boardModel.SetSpecialKind(intersection, SpecialCellKind.Coin);

            _trayModel.SetSlot(1, Diagonal, 1);

            // Fills (3,4) — column 3's only gap — and (4,5) — row 5's only gap.
            bool placed = _system.TryPlacePiece(1, new GridPosition(3, 4));

            Assert.IsTrue(placed);
            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(Payout * 2, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>The same placement, with the coin one cell along the row instead of on the
        /// intersection: the doubling is about the intersection, not about "two lines cleared".</summary>
        [Test]
        public void TryPlacePiece_ClosingARowAndAColumnOverACoinOffTheirIntersection_AnnouncesTheBasePayout()
        {
            FillRowExcept(y: 5, new GridPosition(4, 5));
            FillColumnExcept(x: 3, new GridPosition(3, 4));
            _boardModel.SetSpecialKind(new GridPosition(6, 5), SpecialCellKind.Coin);

            _trayModel.SetSlot(1, Diagonal, 1);
            _system.TryPlacePiece(1, new GridPosition(3, 4));

            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(Payout, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>AC7: a coin cell nobody destroys grants nothing. The placement lands beside it and
        /// clears nothing at all.</summary>
        [Test]
        public void TryPlacePiece_LeavingACoinStanding_AnnouncesNothing()
        {
            var coin = new GridPosition(0, 5);
            _boardModel.Occupy(coin, 1);
            _boardModel.SetSpecialKind(coin, SpecialCellKind.Coin);

            _system.TryPlacePiece(0, new GridPosition(4, 2));

            Assert.AreEqual(0, _coinCellsBroker.Published.Count);
            Assert.AreEqual(
                SpecialCellKind.Coin, _boardModel.GetSpecialKind(coin), "It is still there, still unpaid.");
        }

        /// <summary>A placement that clears a line holding no coin at all is not a coin event: no
        /// subscriber should ever be handed a zero payout.</summary>
        [Test]
        public void TryPlacePiece_CompletingARowWithNoCoinInIt_AnnouncesNothing()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(0, _coinCellsBroker.Published.Count);
        }

        /// <summary>AC4: the payout is per destruction, not once per run. Two rows, each with their own
        /// coin, each pay in full.</summary>
        [Test]
        public void TryPlacePiece_DestroyingACoinOnTwoSeparatePlacements_AnnouncesBothPayouts()
        {
            var firstGap = new GridPosition(3, 5);
            FillRowExcept(y: 5, firstGap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Coin);
            _system.TryPlacePiece(0, firstGap);

            var secondGap = new GridPosition(3, 2);
            FillRowExcept(y: 2, secondGap);
            _boardModel.SetSpecialKind(new GridPosition(0, 2), SpecialCellKind.Coin);
            _trayModel.SetSlot(2, Single, 1);
            _system.TryPlacePiece(2, secondGap);

            Assert.AreEqual(2, _coinCellsBroker.Published.Count);
            Assert.AreEqual(Payout, _coinCellsBroker.Published[0].TotalCoins);
            Assert.AreEqual(Payout, _coinCellsBroker.Published[1].TotalCoins);
        }

        /// <summary>A coin cell changes nothing on the board: the clear it went with removes exactly the
        /// cells it would have removed anyway (AC2's "destroys nothing extra").</summary>
        [Test]
        public void TryPlacePiece_CompletingARowThatHoldsACoin_DestroysNothingBeyondTheRow()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Coin);

            var bystander = new GridPosition(0, 1);
            _boardModel.Occupy(bystander, 2);

            _system.TryPlacePiece(0, gap);

            Assert.AreNotEqual(Board.EMPTY, _boardModel.GetCell(bystander));
            Assert.AreEqual(1, _boardModel.Board.OccupiedCellCount(), "Only the bystander should remain.");
        }

        /// <summary>The hammer path: a coin cell destroyed by something with no line to it at all pays
        /// the base amount, exactly as one destroyed by a line does.</summary>
        [Test]
        public void TryUseDemolitionHammer_OnACoinCell_AnnouncesTheBasePayout()
        {
            var coin = new GridPosition(2, 2);
            _boardModel.Occupy(coin, 1);
            _boardModel.SetSpecialKind(coin, SpecialCellKind.Coin);
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            bool used = _system.TryUseDemolitionHammer(0, coin);

            Assert.IsTrue(used);
            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(Payout, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>Issue #401 AC5: a coin that carries its own value pays that value, not the configured
        /// default. Priced deliberately at something the default is not.</summary>
        [Test]
        public void TryPlacePiece_CompletingARowThatHoldsAPricedCoin_AnnouncesThatCoinsOwnValue()
        {
            int pricedValue = Payout + 11;
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetCoinCell(new GridPosition(0, 5), pricedValue);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(pricedValue, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>Issue #401 AC6: the intersection doubling applies to the coin's own value.</summary>
        [Test]
        public void TryPlacePiece_ClosingARowAndAColumnOverAPricedCoinAtTheirIntersection_AnnouncesDoubleItsOwnValue()
        {
            int pricedValue = 16;
            var intersection = new GridPosition(3, 5);
            FillRowExcept(y: 5, new GridPosition(4, 5));
            FillColumnExcept(x: 3, new GridPosition(3, 4));
            _boardModel.SetCoinCell(intersection, pricedValue);

            _trayModel.SetSlot(1, Diagonal, 1);
            _system.TryPlacePiece(1, new GridPosition(3, 4));

            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(pricedValue * 2, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>A priced coin and an unpriced (level-authored style) coin in one clear: each pays at
        /// its own rate, and they add up.</summary>
        [Test]
        public void TryPlacePiece_CompletingARowHoldingAPricedAndAnUnpricedCoin_AnnouncesEachAtItsOwnRate()
        {
            var gap = new GridPosition(3, 5);
            FillRowExcept(y: 5, gap);
            _boardModel.SetCoinCell(new GridPosition(0, 5), 8);
            _boardModel.SetSpecialKind(new GridPosition(6, 5), SpecialCellKind.Coin);

            _system.TryPlacePiece(0, gap);

            Assert.AreEqual(8 + Payout, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>The hammer path reads the per-cell value too: every destruction goes through the same
        /// trigger capture.</summary>
        [Test]
        public void TryUseDemolitionHammer_OnAPricedCoinCell_AnnouncesItsOwnValue()
        {
            var coin = new GridPosition(2, 2);
            _boardModel.Occupy(coin, 1);
            _boardModel.SetCoinCell(coin, 4);
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);

            _system.TryUseDemolitionHammer(0, coin);

            Assert.AreEqual(4, _coinCellsBroker.Published[0].TotalCoins);
        }

        private void FillRowExcept(int y, GridPosition gap)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (!position.Equals(gap))
                {
                    _boardModel.Occupy(position, 1);
                }
            }
        }

        private void FillColumnExcept(int x, GridPosition gap)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                var position = new GridPosition(x, y);
                if (!position.Equals(gap) && !_boardModel.Board.IsOccupied(position))
                {
                    _boardModel.Occupy(position, 1);
                }
            }
        }
    }
}
