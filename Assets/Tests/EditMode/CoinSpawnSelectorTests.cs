using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers where a coin cell is spawned: always on a block, never on an empty cell, and nowhere at
    /// all on a board that holds none. Shaped like <see cref="LaserSpawnSelectorTests"/> because the
    /// selector is — and because all three of the coin cell's sources (a level's authored count, the
    /// streak trigger, and the Coin Sower power-up still to come) go through this one function.
    /// </summary>
    public class CoinSpawnSelectorTests
    {
        /// <summary>An empty board has nothing to convert, which is an ordinary silent outcome rather
        /// than an error. Load-bearing for the level-authored count, which is armed at run start on
        /// exactly such a board.</summary>
        [Test]
        public void SelectSpawnPosition_OnAnEmptyBoard_SelectsNothing()
        {
            Assert.IsNull(CoinSpawnSelector.SelectSpawnPosition(new Board(), new Random(1)));
        }

        [Test]
        public void SelectSpawnPosition_WithExactlyOneOccupiedCell_SelectsIt()
        {
            var board = new Board();
            var only = new GridPosition(6, 2);
            board.Occupy(only, 1);

            for (int seed = 0; seed < 25; seed++)
            {
                Assert.AreEqual(only, CoinSpawnSelector.SelectSpawnPosition(board, new Random(seed)));
            }
        }

        /// <summary>A coin is a property of a block: whatever the roll, the chosen cell holds one.</summary>
        [Test]
        public void SelectSpawnPosition_OnASparseBoard_OnlyEverSelectsAnOccupiedCell()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(3, 4), 2);
            board.Occupy(new GridPosition(7, 7), 3);

            for (int seed = 0; seed < 50; seed++)
            {
                GridPosition? selected = CoinSpawnSelector.SelectSpawnPosition(board, new Random(seed));

                Assert.IsNotNull(selected);
                Assert.IsTrue(
                    board.IsOccupied(selected.Value), $"Seed {seed} chose the empty cell {selected.Value}.");
            }
        }

        /// <summary>Seeded, so a test — and a seeded System — that needs to know which cell was picked
        /// can arrange for one.</summary>
        [Test]
        public void SelectSpawnPosition_WithTheSameSeed_SelectsTheSameCell()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 3), 1);
            }

            GridPosition? first = CoinSpawnSelector.SelectSpawnPosition(board, new Random(7));
            GridPosition? second = CoinSpawnSelector.SelectSpawnPosition(board, new Random(7));

            Assert.AreEqual(first, second);
        }

        /// <summary>Every occupied cell must be reachable, or the "uniform" claim is only nominal.</summary>
        [Test]
        public void SelectSpawnPosition_AcrossManySeeds_ReachesEveryOccupiedCell()
        {
            var board = new Board();
            var first = new GridPosition(1, 1);
            var second = new GridPosition(5, 6);
            board.Occupy(first, 1);
            board.Occupy(second, 1);

            bool sawFirst = false;
            bool sawSecond = false;

            for (int seed = 0; seed < 50; seed++)
            {
                GridPosition? selected = CoinSpawnSelector.SelectSpawnPosition(board, new Random(seed));
                sawFirst |= selected.Value.Equals(first);
                sawSecond |= selected.Value.Equals(second);
            }

            Assert.IsTrue(sawFirst);
            Assert.IsTrue(sawSecond);
        }

        /// <summary>A cell that already carries a kind is still a candidate: "already special" is not a
        /// reason to drop a reward the player earned. AC5's "no cap" rests on this — several coin cells
        /// can coexist, and one landing on another is a legitimate outcome rather than a blocked spawn.</summary>
        [Test]
        public void SelectSpawnPosition_OverAnAlreadySpecialCell_StillSelectsIt()
        {
            var board = new Board();
            var only = new GridPosition(2, 2);
            board.Occupy(only, 1);
            board.SetSpecialKind(only, SpecialCellKind.Coin);

            Assert.AreEqual(only, CoinSpawnSelector.SelectSpawnPosition(board, new Random(3)));
        }

        /// <summary>It never writes: tagging the chosen cell belongs to the caller, so the power-up and
        /// the two triggers each stay the one place their own spawn is recorded.</summary>
        [Test]
        public void SelectSpawnPosition_TagsNothingItself()
        {
            var board = new Board();
            var only = new GridPosition(2, 2);
            board.Occupy(only, 1);

            CoinSpawnSelector.SelectSpawnPosition(board, new Random(3));

            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(only));
        }

        [Test]
        public void SelectSpawnPosition_WithNoBoard_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => CoinSpawnSelector.SelectSpawnPosition(null, new Random(1)));
        }

        [Test]
        public void SelectSpawnPosition_WithNoRandom_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => CoinSpawnSelector.SelectSpawnPosition(new Board(), null));
        }
    }
}
