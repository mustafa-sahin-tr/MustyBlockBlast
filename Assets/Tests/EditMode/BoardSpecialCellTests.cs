using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the per-cell <see cref="SpecialCellKind"/> storage added to <see cref="Board"/>: that it
    /// is orthogonal to occupancy (nothing about emptiness, occupancy counting or line fullness reads
    /// it), that <see cref="Board.Clear"/> resets it, and that <see cref="Board.Clone"/> and
    /// <see cref="Board.CopyFrom"/> carry it across — the last of which has no production consumer
    /// yet, so this is the only thing proving the copy is correct.
    /// </summary>
    public class BoardSpecialCellTests
    {
        /// <summary>Stands in for the first real special kind, which a later sub-issue of the Special
        /// Cells epic names. Detection is "anything that is not None", so an as-yet-unnamed value
        /// exercises it exactly as a real one will, without adding a placeholder to the shipped enum.</summary>
        private const SpecialCellKind StubKind = (SpecialCellKind)1;

        private const SpecialCellKind OtherStubKind = (SpecialCellKind)2;

        [Test]
        public void NewBoard_EveryCellHasNoSpecialKind()
        {
            var board = new Board();

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(new GridPosition(x, y)));
                }
            }
        }

        [Test]
        public void SetSpecialKind_OnAnOccupiedCell_LeavesColourAndOccupancyUntouched()
        {
            var board = new Board();
            var position = new GridPosition(2, 6);
            board.Occupy(position, 3);

            board.SetSpecialKind(position, StubKind);

            Assert.AreEqual(3, board[position]);
            Assert.IsTrue(board.IsOccupied(position));
            Assert.AreEqual(1, board.OccupiedCellCount());
            Assert.AreEqual(StubKind, board.GetSpecialKind(position));
        }

        /// <summary>A kind is metadata about a cell, not occupancy — an empty cell tagged with one is
        /// still empty, so the whole board is still "empty" for a perfect-clear check.</summary>
        [Test]
        public void SetSpecialKind_OnAnEmptyCell_DoesNotMakeTheBoardOccupied()
        {
            var board = new Board();

            board.SetSpecialKind(new GridPosition(4, 4), StubKind);

            Assert.IsTrue(board.IsEmpty());
            Assert.AreEqual(0, board.OccupiedCellCount());
        }

        [Test]
        public void SetSpecialKind_DoesNotAffectLineFullness()
        {
            var board = new Board();
            FillRow(board, y: 1, colourId: 1);
            board.SetSpecialKind(new GridPosition(0, 1), StubKind);

            Assert.IsTrue(board.IsRowFull(1));
            Assert.IsFalse(board.IsRowEmpty(1));
        }

        [Test]
        public void Clear_ResetsTheSpecialKind()
        {
            var board = new Board();
            var position = new GridPosition(5, 5);
            board.Occupy(position, 2);
            board.SetSpecialKind(position, StubKind);

            board.Clear(position);

            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
        }

        [Test]
        public void Clone_CopiesSpecialKinds()
        {
            var board = new Board();
            var first = new GridPosition(0, 0);
            var second = new GridPosition(7, 3);
            board.Occupy(first, 1);
            board.Occupy(second, 2);
            board.SetSpecialKind(first, StubKind);
            board.SetSpecialKind(second, OtherStubKind);

            Board clone = board.Clone();

            Assert.AreEqual(StubKind, clone.GetSpecialKind(first));
            Assert.AreEqual(OtherStubKind, clone.GetSpecialKind(second));
            Assert.AreEqual(SpecialCellKind.None, clone.GetSpecialKind(new GridPosition(4, 4)));
            Assert.AreEqual(1, clone[first]);
            Assert.AreEqual(2, clone[second]);
        }

        /// <summary>The copy must be deep in both arrays: a snapshot that shared the kinds array would
        /// have every later board change leak into the "saved" state.</summary>
        [Test]
        public void Clone_SpecialKindsAreIndependentOfTheOriginal()
        {
            var board = new Board();
            var position = new GridPosition(3, 3);
            board.Occupy(position, 1);
            board.SetSpecialKind(position, StubKind);

            Board clone = board.Clone();
            board.Clear(position);

            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
            Assert.AreEqual(StubKind, clone.GetSpecialKind(position), "The clone must not follow later edits.");
        }

        [Test]
        public void CopyFrom_OverwritesSpecialKindsIncludingClearingStaleOnes()
        {
            var source = new Board();
            var kept = new GridPosition(1, 1);
            source.Occupy(kept, 1);
            source.SetSpecialKind(kept, StubKind);

            var destination = new Board();
            var stale = new GridPosition(6, 6);
            destination.Occupy(stale, 4);
            destination.SetSpecialKind(stale, OtherStubKind);

            destination.CopyFrom(source);

            Assert.AreEqual(StubKind, destination.GetSpecialKind(kept));
            Assert.AreEqual(SpecialCellKind.None, destination.GetSpecialKind(stale));
            Assert.AreEqual(Board.EMPTY, destination[stale]);
        }

        [Test]
        public void CollectRowCells_AppendsEveryCellOfThatRow()
        {
            var board = new Board();
            var results = new List<GridPosition>();

            board.CollectRowCells(2, results);

            Assert.AreEqual(Board.SIZE, results.Count);
            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(new GridPosition(x, 2), results[x]);
            }
        }

        [Test]
        public void CollectColumnCells_AppendsWithoutClearingExistingEntries()
        {
            var board = new Board();
            var results = new List<GridPosition> { new GridPosition(0, 0) };

            board.CollectColumnCells(4, results);

            Assert.AreEqual(Board.SIZE + 1, results.Count);
            Assert.AreEqual(new GridPosition(0, 0), results[0]);
            Assert.AreEqual(new GridPosition(4, 0), results[1]);
        }

        private static void FillRow(Board board, int y, int colourId)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, y), colourId);
            }
        }
    }
}
