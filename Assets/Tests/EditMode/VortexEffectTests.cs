using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the pull itself: which blocks count as isolated (including the hole and off-board cases
    /// that read as "nothing there"), the one-step move and the axis it picks, the cases where a block
    /// stays put, and the chain into a line the pull itself completes.
    /// </summary>
    public class VortexEffectTests
    {
        private VortexEffect _effect;

        [SetUp]
        public void CreateEffect()
        {
            _effect = new VortexEffect();
            _effect.BeginResolution();
        }

        /// <summary>AC2: a lone block with four empty neighbours is isolated and moves one step in.</summary>
        [Test]
        public void Apply_WithOneIsolatedBlock_PullsItOneStepTowardsTheCentre()
        {
            var board = new Board();
            var stray = new GridPosition(0, 4);
            board.Occupy(stray, 1);
            var center = new GridPosition(4, 4);

            _effect.Apply(board, new SpecialCellTrigger(center, SpecialCellKind.Vortex));

            Assert.IsFalse(board.IsOccupied(stray), "The cell it left must be empty.");
            Assert.IsTrue(board.IsOccupied(new GridPosition(1, 4)), "One step, never more.");
            Assert.AreEqual(1, _effect.Pulls.Count);
            Assert.AreEqual(stray, _effect.Pulls[0].From);
            Assert.AreEqual(new GridPosition(1, 4), _effect.Pulls[0].To);
        }

        [Test]
        public void Apply_MovesTheBlocksColourAndSpecialKindWithIt()
        {
            var board = new Board();
            var stray = new GridPosition(0, 0);
            board.Occupy(stray, 5);
            board.SetSpecialKind(stray, SpecialCellKind.Laser);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 0), SpecialCellKind.Vortex));

            var moved = new GridPosition(1, 0);
            Assert.AreEqual(5, board[moved], "A pulled block keeps its colour.");
            Assert.AreEqual(SpecialCellKind.Laser, board.GetSpecialKind(moved), "And its own kind.");
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(stray));
        }

        /// <summary>A block with an occupied orthogonal neighbour is not isolated, and neither is the
        /// neighbour — the whole pair stays exactly where it is.</summary>
        [Test]
        public void Apply_WithAdjacentBlocks_MovesNeither()
        {
            var board = new Board();
            var left = new GridPosition(0, 4);
            var right = new GridPosition(1, 4);
            board.Occupy(left, 1);
            board.Occupy(right, 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Vortex));

            Assert.IsTrue(board.IsOccupied(left));
            Assert.IsTrue(board.IsOccupied(right));
            Assert.AreEqual(0, _effect.Pulls.Count);
        }

        /// <summary>A diagonal neighbour is not an orthogonal one, so both blocks are still isolated.</summary>
        [Test]
        public void Apply_WithDiagonallyAdjacentBlocks_PullsBoth()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(1, 1), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Vortex));

            Assert.AreEqual(2, _effect.Pulls.Count);
        }

        /// <summary>A hole reads as "nothing there" exactly as an empty cell does, so a block whose only
        /// non-empty neighbour is a hole is still isolated.</summary>
        [Test]
        public void Apply_WithAHoleAsANeighbour_StillCountsTheBlockAsIsolated()
        {
            var shape = new BoardShape(Board.SIZE, Board.SIZE, new[] { new GridPosition(3, 4) });
            var board = new Board(shape);
            var stray = new GridPosition(2, 4);
            board.Occupy(stray, 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 4), SpecialCellKind.Vortex));

            Assert.IsFalse(board.IsOccupied(stray));
            Assert.IsTrue(board.IsOccupied(new GridPosition(1, 4)), "Pulled away from the hole, towards the vortex.");
        }

        /// <summary>The board edge reads as "nothing there" too — a corner block has only two real
        /// neighbours and is isolated when both are empty.</summary>
        [Test]
        public void Apply_AtACorner_TreatsOffBoardNeighboursAsEmpty()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 5), SpecialCellKind.Vortex));

            Assert.AreEqual(1, _effect.Pulls.Count);
            Assert.AreEqual(new GridPosition(0, 1), _effect.Pulls[0].To);
        }

        /// <summary>The axis is whichever distance to the vortex is greater — here the vertical one,
        /// even though there is a horizontal gap to close as well.</summary>
        [Test]
        public void Apply_WithTheGreaterGapVertical_PullsVertically()
        {
            var board = new Board();
            board.Occupy(new GridPosition(3, 0), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 6), SpecialCellKind.Vortex));

            Assert.AreEqual(new GridPosition(3, 1), _effect.Pulls[0].To);
        }

        [Test]
        public void Apply_WithTheGreaterGapHorizontal_PullsHorizontally()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 3), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(6, 4), SpecialCellKind.Vortex));

            Assert.AreEqual(new GridPosition(1, 3), _effect.Pulls[0].To);
        }

        /// <summary>The documented tie-break: equal gaps on both axes pull horizontally. Arbitrary but
        /// fixed, so the same board always resolves the same way.</summary>
        [Test]
        public void Apply_WithEqualGapsOnBothAxes_PullsHorizontally()
        {
            var board = new Board();
            board.Occupy(new GridPosition(1, 1), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Vortex));

            Assert.AreEqual(new GridPosition(2, 1), _effect.Pulls[0].To, "The tie goes to the horizontal.");
        }

        /// <summary>Two isolated blocks pulling into the same free cell: the first one there keeps it and
        /// the second stays put rather than overwriting it.</summary>
        [Test]
        public void Apply_WithTwoBlocksPullingIntoTheSameCell_MovesOnlyTheFirst()
        {
            var board = new Board();
            var lower = new GridPosition(4, 2);
            var upper = new GridPosition(4, 6);
            board.Occupy(lower, 1);
            board.Occupy(upper, 2);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Vortex));

            Assert.AreEqual(2, _effect.Pulls.Count, "Both gaps are vertical, so both blocks move one step.");
            Assert.IsTrue(board.IsOccupied(new GridPosition(4, 3)));
            Assert.IsTrue(board.IsOccupied(new GridPosition(4, 5)));

            // Now the two are one cell apart around the empty centre; a second vortex on that centre
            // pulls the lower one into it, and the upper one — scanned later — finds it taken.
            _effect.BeginResolution();
            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Vortex));

            Assert.AreEqual(1, _effect.Pulls.Count, "The second block had nowhere free to go.");
            Assert.AreEqual(new GridPosition(4, 4), _effect.Pulls[0].To);
            Assert.AreEqual(1, board[new GridPosition(4, 4)], "The first block there keeps the cell.");
            Assert.IsTrue(board.IsOccupied(new GridPosition(4, 5)), "The second stayed exactly where it was.");
        }

        /// <summary>A kind that is not a vortex is ignored, exactly as every other effect ignores one.</summary>
        [Test]
        public void Apply_WithAnotherKind_DoesNothing()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Laser));

            Assert.IsTrue(board.IsOccupied(new GridPosition(0, 0)));
            Assert.AreEqual(0, _effect.Pulls.Count);
        }

        [Test]
        public void BeginResolution_ForgetsThePreviousResolutionsPulls()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 0), SpecialCellKind.Vortex));
            Assert.AreEqual(1, _effect.Pulls.Count);

            _effect.BeginResolution();

            Assert.AreEqual(0, _effect.Pulls.Count);
        }

        [Test]
        public void IsIsolated_WithAnEmptyCell_IsFalse()
        {
            var board = new Board();

            Assert.IsFalse(VortexEffect.IsIsolated(board, new GridPosition(3, 3)));
        }

        [Test]
        public void IsIsolated_WithAHoleCell_IsFalse()
        {
            var shape = new BoardShape(Board.SIZE, Board.SIZE, new[] { new GridPosition(3, 3) });
            var board = new Board(shape);

            Assert.IsFalse(VortexEffect.IsIsolated(board, new GridPosition(3, 3)));
        }

        /// <summary>AC5: a pull that completes a line is cleared by the cascade loop, not by the effect —
        /// so the chain happens through the engine that already owns "clear, detect, apply, re-check".</summary>
        [Test]
        public void ResolveCascade_WhenAPullCompletesALine_ChainsIntoASecondPhase()
        {
            var board = new Board();

            // Row 0 is full, so it clears first. Its cell (7, 0) carries the vortex.
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            board.SetSpecialKind(new GridPosition(7, 0), SpecialCellKind.Vortex);

            // Row 2 is one cell short of full: (7, 2) is empty and (7, 3) holds the isolated block that
            // will be pulled down into it. Row 3 is otherwise empty, so that block has no neighbours.
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 2), 1);
            }

            board.Occupy(new GridPosition(7, 3), 1);

            CascadeClearResult result = CascadeClearResolver.ResolveCascade(board, _effect);

            Assert.AreEqual(1, result.Primary.LineCount, "Phase 0 is the row the placement completed.");
            Assert.AreEqual(1, result.CascadePhaseCount, "The pull completed row 2, which is phase 1.");
            Assert.AreEqual(2, result.TotalLineCount);
            Assert.IsFalse(board.IsOccupied(new GridPosition(7, 3)), "The pulled block left this cell...");
            Assert.IsFalse(board.IsOccupied(new GridPosition(7, 2)), "...and row 2 then cleared out from under it.");
        }

        /// <summary>The pull never clears a line itself, even a completed one — that is the cascade
        /// loop's job, and doing it here would double-report it.</summary>
        [Test]
        public void Apply_NeverClearsAnything()
        {
            var board = new Board();
            var isolated = new List<GridPosition> { new GridPosition(0, 0), new GridPosition(4, 6) };
            for (int i = 0; i < isolated.Count; i++)
            {
                board.Occupy(isolated[i], 1);
            }

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Vortex));

            Assert.AreEqual(isolated.Count, board.OccupiedCellCount(), "A pull moves blocks; it destroys none.");
        }
    }
}
