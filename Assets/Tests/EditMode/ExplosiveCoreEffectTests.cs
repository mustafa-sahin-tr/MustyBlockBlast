using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the new "finish the job" behaviour: filling every row/column missing exactly one occupied
    /// playable cell, the hand-off when nothing qualifies, and the reporting counters' per-resolution
    /// lifetime.
    /// </summary>
    public class ExplosiveCoreEffectTests
    {
        private ExplosiveCoreEffect _effect;

        [SetUp]
        public void CreateEffect()
        {
            _effect = new ExplosiveCoreEffect(new Random(1));
            _effect.BeginResolution();
        }

        /// <summary>AC1: a single near-complete row is finished off — occupied all the way and no
        /// longer missing a cell.</summary>
        [Test]
        public void Apply_WithOneRowOneCellFromFull_FinishesIt()
        {
            Board board = new Board();
            var gap = new GridPosition(3, 5);
            FillRowExcept(board, y: 5, gap);
            var trigger = new GridPosition(7, 7);
            board.Occupy(trigger, 1);
            board.Clear(trigger);

            _effect.Apply(board, new SpecialCellTrigger(trigger, SpecialCellKind.ExplosiveCore));

            Assert.IsTrue(board.IsRowFull(5));
            Assert.AreEqual(1, _effect.FinishedLineCount);
            Assert.AreEqual(0, _effect.HandOffTargets.Count);
        }

        /// <summary>AC2: a near-complete row AND a near-complete column both finish from the same
        /// detonation, not just whichever is found first.</summary>
        [Test]
        public void Apply_WithARowAndAColumnBothOneCellFromFull_FinishesBoth()
        {
            Board board = new Board();
            FillRowExcept(board, y: 2, new GridPosition(1, 2));
            FillColumnExcept(board, x: 6, new GridPosition(6, 4));

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.ExplosiveCore));

            Assert.IsTrue(board.IsRowFull(2));
            Assert.IsTrue(board.IsColumnFull(6));
            Assert.AreEqual(2, _effect.FinishedLineCount);
        }

        /// <summary>A row and a column that share their one missing cell both finish from a single
        /// fill — the shared gap is filled once, not twice.</summary>
        [Test]
        public void Apply_WithARowAndAColumnSharingTheirGap_FinishesBothFromOneFill()
        {
            Board board = new Board();
            var gap = new GridPosition(3, 5);
            FillRowExcept(board, y: 5, gap);
            FillColumnExcept(board, x: 3, gap);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.ExplosiveCore));

            Assert.IsTrue(board.IsRowFull(5));
            Assert.IsTrue(board.IsColumnFull(3));
            Assert.AreEqual(2, _effect.FinishedLineCount);
        }

        /// <summary>AC6 negative case: nothing full, nothing one cell short — no line finishes and the
        /// core hands off instead, without crashing or looping.</summary>
        [Test]
        public void Apply_WithNothingOneCellFromFull_HandsOffInstead()
        {
            Board board = new Board();
            board.Occupy(new GridPosition(2, 2), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.ExplosiveCore));

            Assert.AreEqual(0, _effect.FinishedLineCount);
            Assert.AreEqual(1, _effect.HandOffTargets.Count);
            Assert.AreEqual(new GridPosition(2, 2), _effect.HandOffTargets[0]);
            Assert.AreEqual(SpecialCellKind.ExplosiveCore, board.GetSpecialKind(new GridPosition(2, 2)));
        }

        /// <summary>AC4's other half: a hand-off never lands on a cell that already carries a special
        /// kind of its own.</summary>
        [Test]
        public void Apply_WithNothingToFinish_HandsOffOnlyToAnOrdinaryOccupiedCell()
        {
            Board board = new Board();
            board.Occupy(new GridPosition(1, 1), 1);
            board.SetSpecialKind(new GridPosition(1, 1), SpecialCellKind.Laser);
            board.Occupy(new GridPosition(6, 6), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.ExplosiveCore));

            Assert.AreEqual(1, _effect.HandOffTargets.Count);
            Assert.AreEqual(new GridPosition(6, 6), _effect.HandOffTargets[0]);
        }

        /// <summary>AC4: no eligible cell at all is a no-op, not a crash.</summary>
        [Test]
        public void Apply_WithNothingToFinishAndNoEligibleCell_IsANoOp()
        {
            Board board = new Board();

            Assert.DoesNotThrow(() => _effect.Apply(
                board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.ExplosiveCore)));

            Assert.AreEqual(0, _effect.FinishedLineCount);
            Assert.AreEqual(0, _effect.HandOffTargets.Count);
        }

        /// <summary>AC6: a board with only fully-full, fully-empty or 2+-short lines is untouched.</summary>
        [Test]
        public void Apply_OnABoardWithNoQualifyingLine_ChangesNothingOnTheBoardItself()
        {
            Board board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(1, 0), 1);
            board.Occupy(new GridPosition(2, 0), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(7, 7), SpecialCellKind.ExplosiveCore));

            Assert.AreEqual(0, _effect.FinishedLineCount);
            for (int x = 3; x < Board.SIZE; x++)
            {
                Assert.IsFalse(board.IsOccupied(new GridPosition(x, 0)), $"({x}, 0) must stay untouched.");
            }
        }

        [Test]
        public void BeginResolution_AfterADetonation_ForgetsThePreviousResolutionsCounts()
        {
            Board board = new Board();
            FillRowExcept(board, y: 0, new GridPosition(0, 0));
            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(7, 7), SpecialCellKind.ExplosiveCore));
            Assert.Greater(_effect.FinishedLineCount, 0);

            _effect.BeginResolution();

            Assert.AreEqual(0, _effect.FinishedLineCount);
            Assert.AreEqual(0, _effect.HandOffTargets.Count);
        }

        /// <summary>Two triggers in one resolution accumulate into the same totals — the caller reports
        /// one detonation total per resolution, not one per trigger.</summary>
        [Test]
        public void Apply_TwiceWithinOneResolution_AccumulatesBothDetonations()
        {
            Board board = new Board();
            FillRowExcept(board, y: 0, new GridPosition(0, 0));
            FillRowExcept(board, y: 1, new GridPosition(0, 1));

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(7, 7), SpecialCellKind.ExplosiveCore));
            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(7, 6), SpecialCellKind.ExplosiveCore));

            Assert.AreEqual(2, _effect.FinishedLineCount);
        }

        /// <summary>The effect is installed for one kind only; anything else must pass straight through
        /// it, so the next kind of the epic can be added without this one reacting to it.</summary>
        [Test]
        public void Apply_WithAnotherKind_DoesNothing()
        {
            Board board = new Board();
            FillRowExcept(board, y: 0, new GridPosition(0, 0));

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(7, 7), (SpecialCellKind)99));

            Assert.AreEqual(0, _effect.FinishedLineCount);
            Assert.IsFalse(board.IsRowFull(0));
        }

        private static void FillRowExcept(Board board, int y, GridPosition gap)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (position.X == gap.X && position.Y == gap.Y)
                {
                    continue;
                }

                board.Occupy(position, 1);
            }
        }

        private static void FillColumnExcept(Board board, int x, GridPosition gap)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                var position = new GridPosition(x, y);
                if (position.X == gap.X && position.Y == gap.Y)
                {
                    continue;
                }

                board.Occupy(position, 1);
            }
        }
    }
}
