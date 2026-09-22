using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the bonus wipe issue #398 gave the core: the opposite-axis rule for every
    /// <see cref="ClearAxis"/> (AC1, and its AC9 negative — never both, never the line it died with),
    /// the both-axes fallback for a destruction that had no line to it, the no-op when the opposite line
    /// is already empty (AC2, no hand-off), the chain into a second core bounded by the cascade cap
    /// (AC4), and the reporting buffer's per-resolution lifetime.
    /// </summary>
    public class ExplosiveCoreEffectTests
    {
        private ExplosiveCoreEffect _effect;

        [SetUp]
        public void CreateEffect()
        {
            _effect = new ExplosiveCoreEffect();
            _effect.BeginResolution();
        }

        /// <summary>AC1/AC9: destroyed by a row clear, so it wipes its column and only its column — the
        /// row it died with is left exactly as it was.</summary>
        [Test]
        public void Apply_DestroyedByARowClear_WipesOnlyItsColumn()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            _effect.Apply(board, Trigger(origin, ClearAxis.Row));

            for (int y = 0; y < Board.SIZE; y++)
            {
                Assert.IsFalse(board.IsOccupied(new GridPosition(3, y)), $"Column cell (3, {y}).");
            }

            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x == origin.X)
                {
                    continue;
                }

                Assert.IsTrue(board.IsOccupied(new GridPosition(x, 5)), $"Row cell ({x}, 5) is not re-wiped.");
            }

            Assert.AreEqual(Board.SIZE - 1, _effect.WipedCells.Count, "The origin was already empty.");
        }

        /// <summary>AC1/AC9's other half: destroyed by a column clear, so it wipes its row and only its row.</summary>
        [Test]
        public void Apply_DestroyedByAColumnClear_WipesOnlyItsRow()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            _effect.Apply(board, Trigger(origin, ClearAxis.Column));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.IsFalse(board.IsOccupied(new GridPosition(x, 5)), $"Row cell ({x}, 5).");
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                if (y == origin.Y)
                {
                    continue;
                }

                Assert.IsTrue(board.IsOccupied(new GridPosition(3, y)), $"Column cell (3, {y}) is not re-wiped.");
            }

            Assert.AreEqual(Board.SIZE - 1, _effect.WipedCells.Count);
        }

        /// <summary>Sitting on the intersection of a cleared row and a cleared column, it was destroyed
        /// by both — so there is no single opposite and it wipes both.</summary>
        [Test]
        public void Apply_DestroyedByARowAndAColumnAtOnce_WipesBothLines()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            _effect.Apply(board, Trigger(origin, ClearAxis.Both));

            AssertCrossIsEmpty(board, origin);
            Assert.AreEqual((Board.SIZE - 1) * 2, _effect.WipedCells.Count);
        }

        /// <summary>AC1's axis-less case: a Bomb, a Colour Cleanser or a hammer destroyed it, so there
        /// is no opposite to compute and both lines go.</summary>
        [Test]
        public void Apply_DestroyedWithNoAxis_WipesBothLines()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            _effect.Apply(board, Trigger(origin, ClearAxis.None));

            AssertCrossIsEmpty(board, origin);
            Assert.AreEqual((Board.SIZE - 1) * 2, _effect.WipedCells.Count);
        }

        /// <summary>The two-argument trigger means "no axis", so it must behave exactly as an explicit
        /// <see cref="ClearAxis.None"/> does.</summary>
        [Test]
        public void Apply_WithAnAxislessTrigger_BehavesAsClearAxisNone()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            _effect.Apply(board, new SpecialCellTrigger(origin, SpecialCellKind.ExplosiveCore));

            AssertCrossIsEmpty(board, origin);
        }

        /// <summary>AC4: the chain, and the turn it takes — a core caught in a column wipe was destroyed
        /// by a column, so it fires down its own row.</summary>
        [Test]
        public void Apply_WithASecondCoreInTheWipe_FiresItAtRightAnglesInTurn()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            var chained = new GridPosition(3, 2);
            board.SetSpecialKind(chained, SpecialCellKind.ExplosiveCore);

            _effect.Apply(board, Trigger(origin, ClearAxis.Row));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.IsFalse(board.IsOccupied(new GridPosition(x, 2)), $"The chained row wipe should reach ({x}, 2).");
            }

            Assert.IsTrue(board.IsOccupied(new GridPosition(0, 0)), "Outside both lines.");

            // Column 3 minus the already-empty origin (7), then row 2 minus the cell that column wipe
            // already took (7).
            Assert.AreEqual(14, _effect.WipedCells.Count);
            Assert.IsFalse(_effect.StoppedAtWipeCap, "Two cores is not a pathological chain.");
        }

        /// <summary>AC4: a chained wipe is same-kind only — a laser caught in a core's wipe is destroyed
        /// like any block but does not fire; that is <see cref="LaserEffect"/>'s job, not this one's.</summary>
        [Test]
        public void Apply_WithALaserInTheWipe_DestroysItWithoutFiringIt()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);
            board.SetSpecialKind(new GridPosition(3, 2), SpecialCellKind.Laser);

            _effect.Apply(board, Trigger(origin, ClearAxis.Row));

            Assert.IsFalse(board.IsOccupied(new GridPosition(3, 2)), "Wiped like any other block.");
            Assert.IsTrue(board.IsOccupied(new GridPosition(7, 2)), "Row 2 is untouched: no laser chain here.");
            Assert.AreEqual(Board.SIZE - 1, _effect.WipedCells.Count);
        }

        /// <summary>The chain must not re-fire a core it already used, or two cores on the same line
        /// would bounce between each other forever.</summary>
        [Test]
        public void Apply_WithTwoCoresOnTheSameLine_Terminates()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);
            board.SetSpecialKind(new GridPosition(3, 1), SpecialCellKind.ExplosiveCore);
            board.SetSpecialKind(new GridPosition(3, 6), SpecialCellKind.ExplosiveCore);

            _effect.Apply(board, Trigger(origin, ClearAxis.Row));

            Assert.IsFalse(_effect.StoppedAtWipeCap);
            Assert.IsFalse(board.IsOccupied(new GridPosition(7, 1)), "Reached by the first chained wipe.");
            Assert.IsFalse(board.IsOccupied(new GridPosition(7, 6)), "Reached by the second chained wipe.");
        }

        /// <summary>AC4's bound: a board that is nothing but cores must come back bounded by
        /// <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/> rather than hang, and leave the
        /// board consistent.</summary>
        [Test]
        public void Apply_OnABoardOfNothingButCores_StopsAtTheWipeCap()
        {
            Board board = FullBoard();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.SetSpecialKind(new GridPosition(x, y), SpecialCellKind.ExplosiveCore);
                }
            }

            var origin = new GridPosition(4, 4);
            board.Clear(origin);

            _effect.Apply(board, Trigger(origin, ClearAxis.Row));

            Assert.IsTrue(_effect.StoppedAtWipeCap);
            Assert.LessOrEqual(
                _effect.WipedCells.Count,
                CascadeClearResolver.MAX_CASCADE_ITERATIONS * Board.SIZE * 2,
                "At most two lines per firing, and at most the cap's worth of firings.");
        }

        /// <summary>AC2: an opposite line that is already empty is a no-op — nothing wiped, and no
        /// hand-off of the core's kind to some other cell.</summary>
        [Test]
        public void Apply_WithAnAlreadyEmptyOppositeLine_IsANoOpWithNoHandOff()
        {
            var board = new Board();
            var bystander = new GridPosition(0, 0);
            board.Occupy(bystander, 1);

            _effect.Apply(board, Trigger(new GridPosition(4, 4), ClearAxis.Row));

            Assert.AreEqual(0, _effect.WipedCells.Count);
            Assert.IsTrue(board.IsOccupied(bystander), "Off the wiped column, untouched.");
            Assert.AreEqual(
                SpecialCellKind.None, board.GetSpecialKind(bystander), "No hand-off: the core is simply spent.");
        }

        /// <summary>Only occupied cells count as wiped: the buffer is what scoring and the repaint both
        /// read, and neither may be handed a cell that was already empty.</summary>
        [Test]
        public void WipedCells_ListsExactlyTheCellsThatHeldABlock()
        {
            var board = new Board();
            board.Occupy(new GridPosition(4, 1), 1);
            board.Occupy(new GridPosition(4, 7), 1);
            board.Occupy(new GridPosition(0, 0), 1);

            _effect.Apply(board, Trigger(new GridPosition(4, 4), ClearAxis.Row));

            Assert.AreEqual(2, _effect.WipedCells.Count);
            CollectionAssert.Contains(_effect.WipedCells, new GridPosition(4, 1));
            CollectionAssert.Contains(_effect.WipedCells, new GridPosition(4, 7));
            CollectionAssert.DoesNotContain(_effect.WipedCells, new GridPosition(0, 0));
        }

        [Test]
        public void BeginResolution_AfterAWipe_ForgetsThePreviousResolutionsCells()
        {
            Board board = FullBoard();
            board.Clear(new GridPosition(4, 4));
            _effect.Apply(board, Trigger(new GridPosition(4, 4), ClearAxis.Row));
            Assert.Greater(_effect.WipedCells.Count, 0);

            _effect.BeginResolution();

            Assert.AreEqual(0, _effect.WipedCells.Count);
            Assert.IsFalse(_effect.StoppedAtWipeCap);
        }

        /// <summary>Two triggers in one resolution accumulate into the same buffer — the caller reports
        /// one wipe total per resolution, not one per trigger.</summary>
        [Test]
        public void Apply_TwiceWithinOneResolution_AccumulatesBothWipes()
        {
            Board board = FullBoard();
            board.Clear(new GridPosition(1, 1));
            board.Clear(new GridPosition(6, 6));

            _effect.Apply(board, Trigger(new GridPosition(1, 1), ClearAxis.Row));
            _effect.Apply(board, Trigger(new GridPosition(6, 6), ClearAxis.Row));

            // Column 1 minus its empty origin (7), then column 6 minus its own empty origin (7): the two
            // columns are disjoint.
            Assert.AreEqual(14, _effect.WipedCells.Count);
        }

        /// <summary>The effect is installed for one kind only; anything else must pass straight through
        /// it, which is what lets several effects share the cascade loop without dispatching on kind.</summary>
        [Test]
        public void Apply_WithAnotherKind_DoesNothing()
        {
            Board board = FullBoard();
            board.Clear(new GridPosition(4, 4));

            _effect.Apply(
                board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Laser, ClearAxis.Row));

            Assert.AreEqual(0, _effect.WipedCells.Count);
            Assert.IsTrue(board.IsOccupied(new GridPosition(4, 0)));
        }

        private static SpecialCellTrigger Trigger(GridPosition position, ClearAxis axis)
            => new SpecialCellTrigger(position, SpecialCellKind.ExplosiveCore, axis);

        private static void AssertCrossIsEmpty(Board board, GridPosition origin)
        {
            for (int i = 0; i < Board.SIZE; i++)
            {
                Assert.IsFalse(board.IsOccupied(new GridPosition(origin.X, i)), $"Column cell ({origin.X}, {i}).");
                Assert.IsFalse(board.IsOccupied(new GridPosition(i, origin.Y)), $"Row cell ({i}, {origin.Y}).");
            }

            Assert.IsTrue(board.IsOccupied(new GridPosition(0, 0)), "Off both lines, untouched.");
        }

        private static Board FullBoard()
        {
            var board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            return board;
        }
    }
}
