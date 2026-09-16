using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the blast itself: the clamped 3x3 footprint, the chain into a second core, the cap that
    /// bounds a pathological arrangement, and the reporting buffer's per-resolution lifetime.
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

        [Test]
        public void Apply_AtTheBoardCentre_ClearsTheWholeThreeByThree()
        {
            Board board = FullBoard();
            var center = new GridPosition(4, 4);
            board.Clear(center);

            _effect.Apply(board, new SpecialCellTrigger(center, SpecialCellKind.ExplosiveCore));

            Assert.AreEqual(8, _effect.BlastedCells.Count, "The centre was already empty, so 8 remain.");
            for (int y = 3; y <= 5; y++)
            {
                for (int x = 3; x <= 5; x++)
                {
                    Assert.IsFalse(board.IsOccupied(new GridPosition(x, y)), $"({x}, {y}) should be empty.");
                }
            }

            Assert.IsTrue(board.IsOccupied(new GridPosition(2, 4)), "Outside the footprint, untouched.");
            Assert.IsTrue(board.IsOccupied(new GridPosition(6, 4)), "Outside the footprint, untouched.");
        }

        [Test]
        public void Apply_AtAnEdge_ClampsToTheBoard()
        {
            Board board = FullBoard();
            var center = new GridPosition(0, 4);
            board.Clear(center);

            _effect.Apply(board, new SpecialCellTrigger(center, SpecialCellKind.ExplosiveCore));

            // A 2x3 column band survives the clamp; the centre was already empty, so 5 cells clear.
            Assert.AreEqual(5, _effect.BlastedCells.Count);
            Assert.IsFalse(board.IsOccupied(new GridPosition(1, 3)));
            Assert.IsFalse(board.IsOccupied(new GridPosition(1, 5)));
            Assert.IsTrue(board.IsOccupied(new GridPosition(2, 4)));
        }

        [Test]
        public void Apply_AtACorner_ClampsToTheBoard()
        {
            Board board = FullBoard();
            var center = new GridPosition(0, 0);
            board.Clear(center);

            _effect.Apply(board, new SpecialCellTrigger(center, SpecialCellKind.ExplosiveCore));

            // 2x2 at the corner, one of which was already empty.
            Assert.AreEqual(3, _effect.BlastedCells.Count);
            Assert.IsFalse(board.IsOccupied(new GridPosition(1, 1)));
            Assert.IsTrue(board.IsOccupied(new GridPosition(2, 0)));
            Assert.IsTrue(board.IsOccupied(new GridPosition(0, 2)));
        }

        /// <summary>AC3: a second core within blast range detonates too, and its own footprint clears.</summary>
        [Test]
        public void Apply_WithASecondCoreInRange_DetonatesItAndClearsItsOwnFootprintToo()
        {
            Board board = FullBoard();
            var center = new GridPosition(2, 2);
            board.Clear(center);

            var chained = new GridPosition(3, 3);
            board.SetSpecialKind(chained, SpecialCellKind.ExplosiveCore);

            _effect.Apply(board, new SpecialCellTrigger(center, SpecialCellKind.ExplosiveCore));

            // (4, 4) is outside the first blast but inside the chained one's.
            Assert.IsFalse(board.IsOccupied(new GridPosition(4, 4)), "The chained blast should reach here.");
            Assert.IsFalse(board.IsOccupied(new GridPosition(4, 2)));
            Assert.IsTrue(board.IsOccupied(new GridPosition(5, 5)), "Outside both footprints.");

            // Union of the two 3x3s minus the already-empty centre: 9 + 9 - 4 overlap - 1 = 13.
            Assert.AreEqual(13, _effect.BlastedCells.Count);
        }

        /// <summary>The chain must not re-detonate a centre it already used, or a pair of adjacent
        /// cores would bounce between each other forever.</summary>
        [Test]
        public void Apply_WithTwoAdjacentCores_Terminates()
        {
            Board board = FullBoard();
            var center = new GridPosition(4, 4);
            board.Clear(center);
            board.SetSpecialKind(new GridPosition(5, 4), SpecialCellKind.ExplosiveCore);

            _effect.Apply(board, new SpecialCellTrigger(center, SpecialCellKind.ExplosiveCore));

            Assert.IsFalse(_effect.StoppedAtBlastCap, "Two cores is not a pathological chain.");
            Assert.IsFalse(board.IsOccupied(new GridPosition(6, 4)), "Reached by the chained blast.");
        }

        /// <summary>The pathological case the cap exists for: a board that is nothing but cores. It must
        /// come back bounded rather than hang, and leave the board consistent.</summary>
        [Test]
        public void Apply_OnABoardOfNothingButCores_StopsAtTheBlastCap()
        {
            Board board = FullBoard();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.SetSpecialKind(new GridPosition(x, y), SpecialCellKind.ExplosiveCore);
                }
            }

            var center = new GridPosition(4, 4);
            board.Clear(center);

            _effect.Apply(board, new SpecialCellTrigger(center, SpecialCellKind.ExplosiveCore));

            Assert.IsTrue(_effect.StoppedAtBlastCap);
            Assert.LessOrEqual(
                _effect.BlastedCells.Count,
                CascadeClearResolver.MAX_CASCADE_ITERATIONS * 9,
                "At most nine cells per detonation, and at most the cap's worth of detonations.");
            Assert.IsFalse(board.IsEmpty(), "Cells beyond the cap are simply left standing.");
        }

        [Test]
        public void Apply_WithAnAlreadyEmptyFootprint_ReportsNothing()
        {
            var board = new Board();

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.ExplosiveCore));

            Assert.AreEqual(0, _effect.BlastedCells.Count);
        }

        /// <summary>Only occupied cells count as blasted: the buffer is what scoring and the repaint
        /// both read, and neither may be handed a cell that was already empty.</summary>
        [Test]
        public void BlastedCells_ListsExactlyTheCellsThatHeldABlock()
        {
            var board = new Board();
            board.Occupy(new GridPosition(3, 4), 1);
            board.Occupy(new GridPosition(5, 5), 1);
            board.Occupy(new GridPosition(7, 7), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.ExplosiveCore));

            Assert.AreEqual(2, _effect.BlastedCells.Count);
            CollectionAssert.Contains(_effect.BlastedCells, new GridPosition(3, 4));
            CollectionAssert.Contains(_effect.BlastedCells, new GridPosition(5, 5));
            CollectionAssert.DoesNotContain(_effect.BlastedCells, new GridPosition(7, 7));
        }

        [Test]
        public void BeginResolution_AfterABlast_ForgetsThePreviousResolutionsCells()
        {
            Board board = FullBoard();
            board.Clear(new GridPosition(4, 4));
            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.ExplosiveCore));
            Assert.Greater(_effect.BlastedCells.Count, 0);

            _effect.BeginResolution();

            Assert.AreEqual(0, _effect.BlastedCells.Count);
            Assert.IsFalse(_effect.StoppedAtBlastCap);
        }

        /// <summary>Two triggers in one resolution accumulate into the same buffer — the caller reports
        /// one blast total per resolution, not one per trigger.</summary>
        [Test]
        public void Apply_TwiceWithinOneResolution_AccumulatesBothBlasts()
        {
            Board board = FullBoard();
            board.Clear(new GridPosition(1, 1));
            board.Clear(new GridPosition(6, 6));

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(1, 1), SpecialCellKind.ExplosiveCore));
            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(6, 6), SpecialCellKind.ExplosiveCore));

            Assert.AreEqual(16, _effect.BlastedCells.Count, "Two disjoint 3x3s, each missing its centre.");
        }

        /// <summary>The effect is installed for one kind only; anything else must pass straight through
        /// it, so the next kind of the epic can be added without this one reacting to it.</summary>
        [Test]
        public void Apply_WithAnotherKind_DoesNothing()
        {
            Board board = FullBoard();
            board.Clear(new GridPosition(4, 4));

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), (SpecialCellKind)99));

            Assert.AreEqual(0, _effect.BlastedCells.Count);
            Assert.IsTrue(board.IsOccupied(new GridPosition(3, 3)));
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
