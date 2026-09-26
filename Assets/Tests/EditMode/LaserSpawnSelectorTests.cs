using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers where a laser is spawned: always on a block, never on an empty cell, and nowhere at all
    /// on a board that holds none.
    /// </summary>
    public class LaserSpawnSelectorTests
    {
        /// <summary>AC4: an empty board has nothing to convert, which is an ordinary silent outcome
        /// rather than an error.</summary>
        [Test]
        public void SelectSpawnPosition_OnAnEmptyBoard_SelectsNothing()
        {
            Assert.IsNull(LaserSpawnSelector.SelectSpawnPosition(new Board(), new Random(1)));
        }

        [Test]
        public void SelectSpawnPosition_WithExactlyOneOccupiedCell_SelectsIt()
        {
            var board = new Board();
            var only = new GridPosition(6, 2);
            board.Occupy(only, 1);

            for (int seed = 0; seed < 25; seed++)
            {
                Assert.AreEqual(only, LaserSpawnSelector.SelectSpawnPosition(board, new Random(seed)));
            }
        }

        /// <summary>AC1's "a random OCCUPIED cell": whatever the roll, the chosen cell holds a block.</summary>
        [Test]
        public void SelectSpawnPosition_OnASparseBoard_OnlyEverSelectsAnOccupiedCell()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(3, 4), 2);
            board.Occupy(new GridPosition(7, 7), 3);

            for (int seed = 0; seed < 50; seed++)
            {
                GridPosition? selected = LaserSpawnSelector.SelectSpawnPosition(board, new Random(seed));

                Assert.IsNotNull(selected);
                Assert.IsTrue(board.IsOccupied(selected.Value), $"Seed {seed} chose the empty cell {selected.Value}.");
            }
        }

        /// <summary>Seeded, so a test that needs to know which cell was picked can arrange for one.</summary>
        [Test]
        public void SelectSpawnPosition_WithTheSameSeed_SelectsTheSameCell()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 3), 1);
            }

            GridPosition? first = LaserSpawnSelector.SelectSpawnPosition(board, new Random(7));
            GridPosition? second = LaserSpawnSelector.SelectSpawnPosition(board, new Random(7));

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
                GridPosition? selected = LaserSpawnSelector.SelectSpawnPosition(board, new Random(seed));
                sawFirst |= selected.Value.Equals(first);
                sawSecond |= selected.Value.Equals(second);
            }

            Assert.IsTrue(sawFirst);
            Assert.IsTrue(sawSecond);
        }

        /// <summary>Issue #441: a cell that already carries a kind is never a candidate — no special
        /// cell may stack on another.</summary>
        [Test]
        public void SelectSpawnPosition_WithTheOnlyOccupiedCellAlreadySpecial_SelectsNothing()
        {
            var board = new Board();
            var only = new GridPosition(2, 2);
            board.Occupy(only, 1);
            board.SetSpecialKind(only, SpecialCellKind.ExplosiveCore);

            Assert.IsNull(LaserSpawnSelector.SelectSpawnPosition(board, new Random(3)));
        }

        [Test]
        public void SelectSpawnPosition_WithNoBoard_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => LaserSpawnSelector.SelectSpawnPosition(null, new Random(1)));
        }

        [Test]
        public void SelectSpawnPosition_WithNoRandom_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => LaserSpawnSelector.SelectSpawnPosition(new Board(), null));
        }
    }
}
