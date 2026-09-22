using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #401: the per-cell coin value on <see cref="Board"/> is stored, cleared and copied exactly
    /// as the hit count is — set once by a spawner, reset by the clear that destroys the block, and
    /// carried by the snapshots Undo is built on.
    /// </summary>
    public class BoardCoinValueTests
    {
        private static readonly GridPosition Cell = new GridPosition(3, 4);

        private Board _board;

        [SetUp]
        public void CreateBoard() => _board = new Board();

        [Test]
        public void GetCoinValue_OnAFreshBoard_IsZeroEverywhere()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    Assert.AreEqual(0, _board.GetCoinValue(new GridPosition(x, y)));
                }
            }
        }

        [Test]
        public void SetCoinValue_ThenGet_ReadsItBack()
        {
            _board.Occupy(Cell, 1);
            _board.SetSpecialKind(Cell, SpecialCellKind.Coin);

            _board.SetCoinValue(Cell, 8);

            Assert.AreEqual(8, _board.GetCoinValue(Cell));
        }

        /// <summary>The value is independent of the kind, exactly as the kind is independent of the
        /// colour: pricing a cell does not tag it, and tagging one does not price it.</summary>
        [Test]
        public void SetCoinValue_LeavesTheKindAndColourAlone()
        {
            _board.Occupy(Cell, 2);

            _board.SetCoinValue(Cell, 4);

            Assert.AreEqual(2, _board[Cell]);
            Assert.AreEqual(SpecialCellKind.None, _board.GetSpecialKind(Cell));
        }

        [Test]
        public void SetCoinValue_Negative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _board.SetCoinValue(Cell, -1));
        }

        /// <summary>Clear takes the value with the block, so the next piece to land here inherits
        /// nothing.</summary>
        [Test]
        public void Clear_ResetsTheCoinValue()
        {
            _board.Occupy(Cell, 1);
            _board.SetCoinValue(Cell, 16);

            _board.Clear(Cell);

            Assert.AreEqual(0, _board.GetCoinValue(Cell));
        }

        [Test]
        public void TryDamage_OnAnOrdinaryPricedCell_RemovesItAndResetsTheCoinValue()
        {
            _board.Occupy(Cell, 1);
            _board.SetCoinValue(Cell, 2);

            bool removed = _board.TryDamage(Cell);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, _board.GetCoinValue(Cell));
        }

        [Test]
        public void Clone_CopiesTheCoinValue_AndTheCopyIsIndependent()
        {
            _board.Occupy(Cell, 1);
            _board.SetCoinValue(Cell, 8);

            Board clone = _board.Clone();
            _board.SetCoinValue(Cell, 1);

            Assert.AreEqual(8, clone.GetCoinValue(Cell));
            Assert.AreEqual(1, _board.GetCoinValue(Cell));
        }

        [Test]
        public void CopyFrom_CopiesTheCoinValue()
        {
            _board.Occupy(Cell, 1);
            _board.SetCoinValue(Cell, 4);
            var target = new Board();

            target.CopyFrom(_board);

            Assert.AreEqual(4, target.GetCoinValue(Cell));
        }

        /// <summary>The capture that makes the value survive the clear: the trigger collected before the
        /// cells are destroyed carries it.</summary>
        [Test]
        public void CollectTriggered_OnAPricedCoin_CarriesTheValueOnTheTrigger()
        {
            _board.Occupy(Cell, 1);
            _board.SetSpecialKind(Cell, SpecialCellKind.Coin);
            _board.SetCoinValue(Cell, 16);
            var triggers = new System.Collections.Generic.List<SpecialCellTrigger>();

            SpecialCellDetection.CollectTriggered(_board, new[] { Cell }, triggers);

            Assert.AreEqual(1, triggers.Count);
            Assert.AreEqual(16, triggers[0].CoinValue);
        }
    }
}
