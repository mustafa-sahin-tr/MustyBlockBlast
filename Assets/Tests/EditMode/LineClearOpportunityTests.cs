using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// The per-piece line-clear question <see cref="LineClearOpportunity"/> answers: whether any legal
    /// placement of one piece clears a line, and the most lines any single placement clears. Every
    /// board here is built by hand so the expected answer is readable off the setup.
    /// </summary>
    public class LineClearOpportunityTests
    {
        private static readonly Piece Single = new Piece("single", new[] { new GridPosition(0, 0) });

        private static readonly Piece VerticalDomino = new Piece(
            "domino_v", new[] { new GridPosition(0, 0), new GridPosition(0, 1) });

        [Test]
        public void OnAnEmptyBoard_NoPieceCanClearALine()
        {
            var sut = new LineClearOpportunity();

            Assert.IsFalse(sut.CanClearAnyLine(new Board(), Single));
            Assert.AreEqual(0, sut.MaxLinesAnyPlacementClears(new Board(), VerticalDomino));
        }

        [Test]
        public void RowMissingOneCell_SingleCellPieceClearsExactlyOneLine()
        {
            Board board = BoardWithRowMissing(y: 0, missingX: 3);
            var sut = new LineClearOpportunity();

            Assert.IsTrue(sut.CanClearAnyLine(board, Single));
            Assert.AreEqual(1, sut.MaxLinesAnyPlacementClears(board, Single));
        }

        /// <summary>The maximum is over placements, not a count of clearable lines: a row and a column
        /// each one cell short, meeting at the same empty cell, are cleared together by the one
        /// placement that fills it.</summary>
        [Test]
        public void RowAndColumnMeetingAtOneGap_ReportsTwoSimultaneousLines()
        {
            var board = new Board();
            for (int i = 0; i < Board.SIZE; i++)
            {
                if (i != 4)
                {
                    board.Occupy(new GridPosition(i, 2), 1);
                    board.Occupy(new GridPosition(4, i), 1);
                }
            }

            Assert.AreEqual(2, new LineClearOpportunity().MaxLinesAnyPlacementClears(board, Single));
        }

        /// <summary>A piece that fits somewhere but never on the gap that would complete a line: the
        /// row's gap is a single cell and the domino needs two stacked, but the rest of the board is
        /// wide open — placeable, yet no placement clears.</summary>
        [Test]
        public void PieceThatFitsButCannotReachTheGap_ReportsZero()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x != 3)
                {
                    board.Occupy(new GridPosition(x, 0), 1);
                }
            }

            // Wall off the gap from above so a vertical domino cannot stand in it.
            board.Occupy(new GridPosition(3, 1), 1);

            var sut = new LineClearOpportunity();
            Assert.IsTrue(MoveAvailability.CanPlaceAnywhere(board, VerticalDomino));
            Assert.AreEqual(0, sut.MaxLinesAnyPlacementClears(board, VerticalDomino));
        }

        [Test]
        public void PieceWithNoLegalPlacement_ReportsZero()
        {
            var board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            var sut = new LineClearOpportunity();
            Assert.IsFalse(sut.CanClearAnyLine(board, Single));
            Assert.AreEqual(0, sut.MaxLinesAnyPlacementClears(board, Single));
        }

        /// <summary>Same instance, boards of different shapes, in either order: the scratch board is
        /// re-pointed at the new shape rather than left describing the old one.</summary>
        [Test]
        public void ReusedAcrossBoardShapes_StillAnswersCorrectly()
        {
            var sut = new LineClearOpportunity();
            Board standard = BoardWithRowMissing(y: 0, missingX: 0);

            var holes = new List<GridPosition> { new GridPosition(7, 0) };
            var shortened = new Board(new BoardShape(Board.SIZE, Board.SIZE, holes));
            for (int x = 1; x < Board.SIZE - 1; x++)
            {
                shortened.Occupy(new GridPosition(x, 0), 1);
            }

            Assert.AreEqual(1, sut.MaxLinesAnyPlacementClears(standard, Single));
            Assert.AreEqual(1, sut.MaxLinesAnyPlacementClears(shortened, Single));
            Assert.AreEqual(1, sut.MaxLinesAnyPlacementClears(standard, Single));
        }

        [Test]
        public void NullArguments_AreRejected()
        {
            var sut = new LineClearOpportunity();

            Assert.Throws<System.ArgumentNullException>(() => sut.MaxLinesAnyPlacementClears(null, Single));
            Assert.Throws<System.ArgumentNullException>(() => sut.MaxLinesAnyPlacementClears(new Board(), null));
        }

        private static Board BoardWithRowMissing(int y, int missingX)
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x != missingX)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            return board;
        }
    }
}
