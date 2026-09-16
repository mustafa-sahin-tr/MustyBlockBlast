using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers where a placement's explosive core is spawned: the intersection in the ordinary case, the
    /// neighbour fallback in the defensive one, and the two ways the answer is "spawn nothing" —
    /// neither of which is an error.
    /// </summary>
    public class ExplosiveCoreSpawnSelectorTests
    {
        private Random _random;

        [SetUp]
        public void CreateRandom() => _random = new Random(1);

        /// <summary>AC1/AC4: a row and a column cleared together always spawn one, at their crossing.</summary>
        [Test]
        public void SelectSpawnPosition_WithARowAndAColumnCleared_ReturnsTheirIntersection()
        {
            var board = new Board();

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new[] { 5 }, _random);

            Assert.IsTrue(spawn.HasValue);
            Assert.AreEqual(new GridPosition(5, 3), spawn.Value);
        }

        [Test]
        public void SelectSpawnPosition_WithSeveralLinesCleared_UsesTheFirstOfEach()
        {
            var board = new Board();

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new[] { 2, 6 }, new[] { 1, 7 }, _random);

            Assert.AreEqual(new GridPosition(1, 2), spawn.Value);
        }

        [Test]
        public void SelectSpawnPosition_WithOnlyRowsCleared_SpawnsNothing()
        {
            var board = new Board();

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new[] { 3 }, new int[0], _random);

            Assert.IsNull(spawn);
        }

        [Test]
        public void SelectSpawnPosition_WithOnlyColumnsCleared_SpawnsNothing()
        {
            var board = new Board();

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new int[0], new[] { 3 }, _random);

            Assert.IsNull(spawn);
        }

        [Test]
        public void SelectSpawnPosition_WithNothingCleared_SpawnsNothing()
        {
            var board = new Board();

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new int[0], new int[0], _random);

            Assert.IsNull(spawn);
        }

        /// <summary>
        /// The neighbour fallback. A real resolved clear can never produce this on the current 8x8
        /// grid — a line only clears because every one of its cells was occupied, so the intersection
        /// is always empty afterwards — so the board is hand-built with the intersection occupied,
        /// exactly as <c>CascadeClearResolverTests</c> hand-builds boards its resolver could not have
        /// produced. It guards the branch that keeps a differently shaped board from crashing.
        /// </summary>
        [Test]
        public void SelectSpawnPosition_WithAnOccupiedIntersection_FallsBackToAnOccupiedNeighbour()
        {
            var board = new Board();
            var intersection = new GridPosition(4, 4);
            board.Occupy(intersection, 1);
            board.Occupy(new GridPosition(5, 5), 2);

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new[] { 4 }, new[] { 4 }, _random);

            Assert.IsTrue(spawn.HasValue);
            Assert.AreEqual(new GridPosition(5, 5), spawn.Value, "The only occupied neighbour.");
        }

        [Test]
        public void SelectSpawnPosition_WithAnOccupiedIntersectionAtACorner_StaysOnTheBoard()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(1, 1), 2);

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new[] { 0 }, new[] { 0 }, _random);

            Assert.AreEqual(new GridPosition(1, 1), spawn.Value);
        }

        /// <summary>AC5: nowhere valid to put one is a silent skip, not an error.</summary>
        [Test]
        public void SelectSpawnPosition_WithAnOccupiedIntersectionAndNoOccupiedNeighbour_SpawnsNothing()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 7), 1);

            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                board, new[] { 7 }, new[] { 0 }, _random);

            Assert.IsNull(spawn);
        }

        /// <summary>Whichever neighbour the tie-break picks, it is always a legal one: on the board,
        /// occupied, and never the intersection itself.</summary>
        [Test]
        public void SelectSpawnPosition_WithSeveralOccupiedNeighbours_AlwaysPicksOneOfThem()
        {
            for (int seed = 0; seed < 25; seed++)
            {
                var board = new Board();
                var intersection = new GridPosition(4, 4);
                board.Occupy(intersection, 1);
                board.Occupy(new GridPosition(3, 3), 1);
                board.Occupy(new GridPosition(4, 5), 1);
                board.Occupy(new GridPosition(5, 4), 1);

                GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                    board, new[] { 4 }, new[] { 4 }, new Random(seed));

                Assert.IsTrue(spawn.HasValue);
                Assert.AreNotEqual(intersection, spawn.Value);
                Assert.IsTrue(Board.IsInside(spawn.Value));
                Assert.IsTrue(board.IsOccupied(spawn.Value));
            }
        }
    }
}
