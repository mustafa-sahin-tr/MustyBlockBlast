using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the wipe itself: the opposite-axis rule for every <see cref="ClearAxis"/>, the
    /// both-axes fallback for a destruction that had no line to it, the chain into a second laser, the
    /// cap that bounds a pathological arrangement, and the reporting buffer's per-resolution lifetime.
    /// </summary>
    public class LaserEffectTests
    {
        private LaserEffect _effect;

        [SetUp]
        public void CreateEffect()
        {
            _effect = new LaserEffect();
            _effect.BeginResolution();
        }

        /// <summary>AC2: destroyed by a row clear, so it wipes its column and leaves its row alone.</summary>
        [Test]
        public void Apply_DestroyedByARowClear_WipesItsColumn()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            _effect.Apply(board, Trigger(origin, ClearAxis.Row));

            for (int y = 0; y < Board.SIZE; y++)
            {
                Assert.IsFalse(board.IsOccupied(new GridPosition(3, y)), $"Column cell (3, {y}).");
            }

            Assert.IsTrue(board.IsOccupied(new GridPosition(0, 5)), "The row it was destroyed by is not re-wiped.");
            Assert.AreEqual(Board.SIZE - 1, _effect.WipedCells.Count, "The origin was already empty.");
        }

        /// <summary>AC2's other half: destroyed by a column clear, so it wipes its row.</summary>
        [Test]
        public void Apply_DestroyedByAColumnClear_WipesItsRow()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            _effect.Apply(board, Trigger(origin, ClearAxis.Column));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.IsFalse(board.IsOccupied(new GridPosition(x, 5)), $"Row cell ({x}, 5).");
            }

            Assert.IsTrue(board.IsOccupied(new GridPosition(3, 0)), "The column it was destroyed by is not re-wiped.");
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

        /// <summary>The axis-less fallback: a Bomb or a Colour Cleanser destroyed it, so there is no
        /// opposite to compute and both lines go.</summary>
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

            _effect.Apply(board, new SpecialCellTrigger(origin, SpecialCellKind.Laser));

            AssertCrossIsEmpty(board, origin);
        }

        /// <summary>The chain, and the turn it takes: a laser caught in a column wipe was destroyed by a
        /// column, so it fires down its own row.</summary>
        [Test]
        public void Apply_WithASecondLaserInTheWipe_FiresItAtRightAnglesInTurn()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);

            var chained = new GridPosition(3, 2);
            board.SetSpecialKind(chained, SpecialCellKind.Laser);

            _effect.Apply(board, Trigger(origin, ClearAxis.Row));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.IsFalse(board.IsOccupied(new GridPosition(x, 2)), $"The chained row wipe should reach ({x}, 2).");
            }

            Assert.IsTrue(board.IsOccupied(new GridPosition(0, 0)), "Outside both lines.");

            // Column 3 minus the already-empty origin (7), then row 2 minus the cell that column wipe
            // already took (7).
            Assert.AreEqual(14, _effect.WipedCells.Count);
            Assert.IsFalse(_effect.StoppedAtWipeCap, "Two lasers is not a pathological chain.");
        }

        /// <summary>The chain must not re-fire a laser it already used, or two lasers on the same line
        /// would bounce between each other forever.</summary>
        [Test]
        public void Apply_WithTwoLasersOnTheSameLine_Terminates()
        {
            Board board = FullBoard();
            var origin = new GridPosition(3, 5);
            board.Clear(origin);
            board.SetSpecialKind(new GridPosition(3, 1), SpecialCellKind.Laser);
            board.SetSpecialKind(new GridPosition(3, 6), SpecialCellKind.Laser);

            _effect.Apply(board, Trigger(origin, ClearAxis.Row));

            Assert.IsFalse(_effect.StoppedAtWipeCap);
            Assert.IsFalse(board.IsOccupied(new GridPosition(7, 1)), "Reached by the first chained wipe.");
            Assert.IsFalse(board.IsOccupied(new GridPosition(7, 6)), "Reached by the second chained wipe.");
        }

        /// <summary>The pathological case the cap exists for: a board that is nothing but lasers. It must
        /// come back bounded rather than hang, and leave the board consistent.</summary>
        [Test]
        public void Apply_OnABoardOfNothingButLasers_StopsAtTheWipeCap()
        {
            Board board = FullBoard();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.SetSpecialKind(new GridPosition(x, y), SpecialCellKind.Laser);
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

        [Test]
        public void Apply_WithAnAlreadyEmptyLine_ReportsNothing()
        {
            var board = new Board();

            _effect.Apply(board, Trigger(new GridPosition(4, 4), ClearAxis.Row));

            Assert.AreEqual(0, _effect.WipedCells.Count);
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

            // Column 1 minus its empty origin (7), then column 6 minus its own empty origin and minus
            // the one cell column 1's wipe never touched — (6, 1) went with column 6, (1, 6) with
            // column 1, so the two columns are disjoint: 7 + 7.
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
                board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.ExplosiveCore, ClearAxis.Row));

            Assert.AreEqual(0, _effect.WipedCells.Count);
            Assert.IsTrue(board.IsOccupied(new GridPosition(4, 0)));
        }

        private static SpecialCellTrigger Trigger(GridPosition position, ClearAxis axis)
            => new SpecialCellTrigger(position, SpecialCellKind.Laser, axis);

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
