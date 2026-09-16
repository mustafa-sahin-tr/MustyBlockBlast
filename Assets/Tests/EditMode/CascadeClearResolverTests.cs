using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the multi-phase resolution loop: that a board with no special cells resolves in exactly
    /// one phase and reports exactly what the old single-pass resolver reported (the regression
    /// guard), that a special cell's effect can chain into further phases, and that an effect which
    /// would chain forever is stopped by <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/>
    /// instead of hanging.
    /// </summary>
    public class CascadeClearResolverTests
    {
        /// <summary>Stands in for a real special kind. Detection is "anything that is not None", so an
        /// unnamed value exercises the loop exactly as a real kind will — and deliberately names no
        /// shipped kind, so these tests stay about the loop rather than about any one effect.</summary>
        private const SpecialCellKind StubKind = (SpecialCellKind)99;

        [Test]
        public void ResolveCascade_EmptyBoard_ReportsOnePhaseThatClearedNothing()
        {
            var board = new Board();

            CascadeClearResult result = CascadeClearResolver.ResolveCascade(board);

            Assert.AreEqual(1, result.Phases.Count);
            Assert.IsFalse(result.Primary.AnyCleared);
            Assert.IsFalse(result.AnyCascaded);
            Assert.IsFalse(result.StoppedAtIterationCap);
            Assert.AreEqual(0, result.TotalLineCount);
        }

        /// <summary>The regression guard, stated directly: with every cell at None, the cascade's
        /// primary phase and the resulting board are indistinguishable from the single-pass resolver's.</summary>
        [Test]
        public void ResolveCascade_WithNoSpecialCells_MatchesTheSinglePassResolverExactly()
        {
            Board cascadeBoard = BuildMixedScenario();
            Board singlePassBoard = BuildMixedScenario();

            CascadeClearResult cascade = CascadeClearResolver.ResolveCascade(cascadeBoard);
            LineClearResult singlePass = LineClearResolver.ResolveClears(singlePassBoard);

            Assert.AreEqual(1, cascade.Phases.Count, "Nothing can chain when no cell is special.");
            CollectionAssert.AreEqual(singlePass.ClearedRows, cascade.Primary.ClearedRows);
            CollectionAssert.AreEqual(singlePass.ClearedColumns, cascade.Primary.ClearedColumns);
            Assert.AreEqual(singlePass.ClearedCellCount, cascade.Primary.ClearedCellCount);
            Assert.AreEqual(singlePass.MonochromeLineCount, cascade.Primary.MonochromeLineCount);
            Assert.AreEqual(singlePass.LineCount, cascade.Primary.LineCount);

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    Assert.AreEqual(
                        singlePassBoard[position], cascadeBoard[position], $"{position} differs after resolving.");
                }
            }
        }

        [Test]
        public void ResolveCascade_SpecialCellClearedButNoEffectInstalled_DoesNotChain()
        {
            var board = new Board();
            FillRow(board, y: 3, colourId: 1);
            board.SetSpecialKind(new GridPosition(0, 3), StubKind);

            CascadeClearResult result = CascadeClearResolver.ResolveCascade(board);

            Assert.AreEqual(1, result.Phases.Count);
            CollectionAssert.AreEqual(new[] { 3 }, result.Primary.ClearedRows);
            Assert.IsFalse(result.StoppedAtIterationCap);
        }

        [Test]
        public void ResolveCascade_ClearingASpecialCell_HandsItsPositionAndKindToTheEffect()
        {
            var board = new Board();
            FillRow(board, y: 3, colourId: 1);
            var specialPosition = new GridPosition(6, 3);
            board.SetSpecialKind(specialPosition, StubKind);
            var effect = new RecordingEffect();

            CascadeClearResolver.ResolveCascade(board, effect);

            Assert.AreEqual(1, effect.Triggers.Count);
            Assert.AreEqual(specialPosition, effect.Triggers[0].Position);
            Assert.AreEqual(StubKind, effect.Triggers[0].Kind);
        }

        /// <summary>An ordinary cell in a cleared line must not be reported as a trigger, and the
        /// row/column intersection must not be reported twice.</summary>
        [Test]
        public void ResolveCascade_IntersectingLinesWithOneSpecialAtTheIntersection_TriggersItOnce()
        {
            var board = new Board();
            FillRow(board, y: 2, colourId: 1);
            FillColumn(board, x: 4, colourId: 1);
            var intersection = new GridPosition(4, 2);
            board.SetSpecialKind(intersection, StubKind);
            var effect = new RecordingEffect();

            CascadeClearResolver.ResolveCascade(board, effect);

            Assert.AreEqual(1, effect.Triggers.Count);
            Assert.AreEqual(intersection, effect.Triggers[0].Position);
        }

        [Test]
        public void ResolveCascade_EffectCompletesAnotherLine_ResolvesItAsASecondPhase()
        {
            var board = new Board();
            FillRow(board, y: 3, colourId: 1);
            board.SetSpecialKind(new GridPosition(0, 3), StubKind);

            CascadeClearResult result = CascadeClearResolver.ResolveCascade(board, new FillRowOnceEffect(row: 6));

            Assert.AreEqual(2, result.Phases.Count);
            Assert.IsTrue(result.AnyCascaded);
            Assert.AreEqual(1, result.CascadePhaseCount);
            CollectionAssert.AreEqual(new[] { 3 }, result.Primary.ClearedRows);
            CollectionAssert.AreEqual(new[] { 6 }, result.Phases[1].ClearedRows);
            Assert.AreEqual(2, result.TotalLineCount);
            Assert.AreEqual(Board.SIZE * 2, result.TotalClearedCellCount);
            Assert.IsFalse(result.StoppedAtIterationCap);
            Assert.IsTrue(board.IsEmpty(), "Both phases should have emptied their lines.");
        }

        /// <summary>The primary phase keeps reporting only what the placement itself cleared, even when
        /// the cascade went on to clear more — the contract every later sub-issue inherits.</summary>
        [Test]
        public void ResolveCascade_WithACascade_PrimaryStillReportsOnlyTheFirstPhase()
        {
            var board = new Board();
            FillRow(board, y: 3, colourId: 1);
            board.SetSpecialKind(new GridPosition(0, 3), StubKind);

            CascadeClearResult result = CascadeClearResolver.ResolveCascade(board, new FillRowOnceEffect(row: 6));

            Assert.AreEqual(1, result.Primary.LineCount);
            Assert.AreEqual(Board.SIZE, result.Primary.ClearedCellCount);
        }

        [Test]
        public void ResolveCascade_EffectThatKeepsRebuildingItsOwnLine_StopsAtTheIterationCap()
        {
            var board = new Board();
            FillRow(board, y: 3, colourId: 1);
            board.SetSpecialKind(new GridPosition(0, 3), StubKind);

            CascadeClearResult result = CascadeClearResolver.ResolveCascade(board, new SelfRebuildingRowEffect());

            Assert.IsTrue(result.StoppedAtIterationCap);
            Assert.AreEqual(CascadeClearResolver.MAX_CASCADE_ITERATIONS, result.Phases.Count);
            Assert.AreEqual(CascadeClearResolver.MAX_CASCADE_ITERATIONS, result.TotalLineCount);
        }

        /// <summary>Hitting the cap is not a failure: the board is left consistent, and whatever the
        /// last effect rebuilt is simply still standing for the next placement's first phase to clear.</summary>
        [Test]
        public void ResolveCascade_StoppedAtTheCap_LeavesAConsistentBoardTheNextResolveCanFinish()
        {
            var board = new Board();
            FillRow(board, y: 3, colourId: 1);
            board.SetSpecialKind(new GridPosition(0, 3), StubKind);

            CascadeClearResolver.ResolveCascade(board, new SelfRebuildingRowEffect());

            Assert.IsTrue(board.IsRowFull(3), "The last applied effect's rebuild is left standing.");

            LineClearResult next = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(1, next.LineCount);
            Assert.IsTrue(board.IsEmpty());
        }

        /// <summary>A board holding two full rows and one full column, one row monochrome and one not —
        /// enough moving parts that any divergence between the two resolvers would show up.</summary>
        private static Board BuildMixedScenario()
        {
            var board = new Board();
            FillRow(board, y: 0, colourId: 1);
            FillRow(board, y: 5, colourId: 2);
            board.Occupy(new GridPosition(3, 5), 3);
            FillColumn(board, x: 7, colourId: 1);
            board.Occupy(new GridPosition(2, 2), 2);
            return board;
        }

        private static void FillRow(Board board, int y, int colourId)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, y), colourId);
            }
        }

        private static void FillColumn(Board board, int x, int colourId)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                board.Occupy(new GridPosition(x, y), colourId);
            }
        }

        /// <summary>Observes what the loop detected without touching the board, so nothing can chain.</summary>
        private sealed class RecordingEffect : ISpecialCellEffect
        {
            public List<SpecialCellTrigger> Triggers { get; } = new List<SpecialCellTrigger>();

            public void Apply(Board board, SpecialCellTrigger trigger) => Triggers.Add(trigger);
        }

        /// <summary>Completes one other row, the first time it fires only — a chain of exactly one
        /// extra phase.</summary>
        private sealed class FillRowOnceEffect : ISpecialCellEffect
        {
            private readonly int _row;
            private bool _hasFired;

            public FillRowOnceEffect(int row)
            {
                _row = row;
            }

            public void Apply(Board board, SpecialCellTrigger trigger)
            {
                if (_hasFired)
                {
                    return;
                }

                _hasFired = true;
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, _row), 1);
                }
            }
        }

        /// <summary>Rebuilds the row it was destroyed in, special cell and all — a chain that would run
        /// forever if nothing bounded it. This is the pathological case the cap exists for.</summary>
        private sealed class SelfRebuildingRowEffect : ISpecialCellEffect
        {
            public void Apply(Board board, SpecialCellTrigger trigger)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, trigger.Position.Y), 1);
                }

                board.SetSpecialKind(trigger.Position, trigger.Kind);
            }
        }
    }
}
