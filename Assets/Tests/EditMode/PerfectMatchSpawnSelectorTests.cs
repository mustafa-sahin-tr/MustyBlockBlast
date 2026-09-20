using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers where a qualifying Perfect Match placement's reward lands: uniformly among occupied,
    /// not-yet-special cells, excluding a cell already claimed by another kind, and the ways the answer
    /// is "spawn nothing" — none of which is an error (issue #352).
    /// </summary>
    public class PerfectMatchSpawnSelectorTests
    {
        /// <summary>AC2: with exactly one eligible cell, that cell is always the answer.</summary>
        [Test]
        public void SelectSpawnPosition_WithOneEligibleCell_ReturnsIt()
        {
            var board = new Board();
            var only = new GridPosition(2, 2);
            board.Occupy(only, 1);

            GridPosition? spawn = PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(1));

            Assert.AreEqual(only, spawn.Value);
        }

        /// <summary>AC3: an empty board has nothing to convert — silently skipped, not an error.</summary>
        [Test]
        public void SelectSpawnPosition_WithNoOccupiedCells_ReturnsNull()
        {
            var board = new Board();

            GridPosition? spawn = PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(1));

            Assert.IsNull(spawn);
        }

        /// <summary>A cell already carrying a kind — claimed earlier in the same placement, or seeded by
        /// the level — is excluded rather than overwritten, so the same reward never lands twice on one
        /// cell and never displaces one already granted.</summary>
        [Test]
        public void SelectSpawnPosition_WithTheOnlyOccupiedCellAlreadySpecial_ReturnsNull()
        {
            var board = new Board();
            var claimed = new GridPosition(2, 2);
            board.Occupy(claimed, 1);
            board.SetSpecialKind(claimed, SpecialCellKind.ExplosiveCore);

            GridPosition? spawn = PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(1));

            Assert.IsNull(spawn);
        }

        /// <summary>Only the unclaimed candidate is ever returned, however many times the seed is
        /// varied.</summary>
        [Test]
        public void SelectSpawnPosition_WithOneClaimedAndOneFreeCell_AlwaysReturnsTheFreeOne()
        {
            var board = new Board();
            var claimed = new GridPosition(1, 1);
            var free = new GridPosition(6, 6);
            board.Occupy(claimed, 1);
            board.SetSpecialKind(claimed, SpecialCellKind.Vortex);
            board.Occupy(free, 1);

            for (int seed = 0; seed < 10; seed++)
            {
                GridPosition? spawn = PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(seed));
                Assert.AreEqual(free, spawn.Value);
            }
        }

        /// <summary>Deterministic given the seed: the same board and the same seeded stream always pick
        /// the same cell, which is what keeps a replay reproducible (issue #352's determinism
        /// requirement).</summary>
        [Test]
        public void SelectSpawnPosition_CalledTwiceWithTheSameSeed_ReturnsTheSameCell()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(3, 3), 1);
            board.Occupy(new GridPosition(7, 7), 1);

            GridPosition? first = PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(42));
            GridPosition? second = PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(42));

            Assert.AreEqual(first, second);
        }

        /// <summary>Whichever cell a given seed picks, it is always a legal candidate: occupied and free
        /// of any special kind.</summary>
        [Test]
        public void SelectSpawnPosition_WithSeveralEligibleCells_AlwaysPicksOneOfThem()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(3, 3), 1);
            var claimed = new GridPosition(7, 7);
            board.Occupy(claimed, 1);
            board.SetSpecialKind(claimed, SpecialCellKind.ChainLightning);

            for (int seed = 0; seed < 25; seed++)
            {
                GridPosition? spawn = PerfectMatchSpawnSelector.SelectSpawnPosition(board, new Random(seed));

                Assert.IsTrue(spawn.HasValue);
                Assert.AreNotEqual(claimed, spawn.Value);
                Assert.IsTrue(board.IsOccupied(spawn.Value));
                Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(spawn.Value));
            }
        }

        [Test]
        public void SelectSpawnPosition_WithNullBoard_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PerfectMatchSpawnSelector.SelectSpawnPosition(null, new Random(1)));
        }

        [Test]
        public void SelectSpawnPosition_WithNullRandom_Throws()
        {
            var board = new Board();

            Assert.Throws<ArgumentNullException>(
                () => PerfectMatchSpawnSelector.SelectSpawnPosition(board, null));
        }
    }
}
