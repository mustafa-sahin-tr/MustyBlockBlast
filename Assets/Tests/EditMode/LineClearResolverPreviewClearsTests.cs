using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    public class LineClearResolverPreviewClearsTests
    {
        [Test]
        public void PreviewClears_SingleRowCompleted_ReportsRowAndLeavesBoardUnchanged()
        {
            var board = new Board();
            var scratchBoard = new Board();
            var rows = new List<int>();
            var columns = new List<int>();
            FillRow(board, y: 3, colourId: 1);
            board.Clear(new GridPosition(0, 3));

            Piece piece = SingleCell();
            LineClearResult result = LineClearResolver.PreviewClears(
                board, piece, new GridPosition(0, 3), scratchBoard, rows, columns);

            Assert.IsTrue(result.AnyCleared);
            CollectionAssert.AreEqual(new[] { 3 }, result.ClearedRows);
            Assert.AreEqual(0, result.ClearedColumns.Count);

            // Source board must be unchanged.
            Assert.IsFalse(board.IsRowFull(3));
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(0, 3)]);
        }

        [Test]
        public void PreviewClears_SingleColumnCompleted_ReportsColumnAndLeavesBoardUnchanged()
        {
            var board = new Board();
            var scratchBoard = new Board();
            var rows = new List<int>();
            var columns = new List<int>();
            FillColumn(board, x: 5, colourId: 1);
            board.Clear(new GridPosition(5, 0));

            Piece piece = SingleCell();
            LineClearResult result = LineClearResolver.PreviewClears(
                board, piece, new GridPosition(5, 0), scratchBoard, rows, columns);

            Assert.IsTrue(result.AnyCleared);
            CollectionAssert.AreEqual(new[] { 5 }, result.ClearedColumns);
            Assert.AreEqual(0, result.ClearedRows.Count);

            // Source board must be unchanged.
            Assert.IsFalse(board.IsColumnFull(5));
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(5, 0)]);
        }

        [Test]
        public void PreviewClears_RowAndColumnCompletedSimultaneously_ReportsBoth()
        {
            var board = new Board();
            var scratchBoard = new Board();
            var rows = new List<int>();
            var columns = new List<int>();

            // Fill row 2 and column 4 except their shared intersection at (4,2).
            FillRow(board, y: 2, colourId: 1);
            FillColumn(board, x: 4, colourId: 1);
            board.Clear(new GridPosition(4, 2));

            Piece piece = SingleCell();
            LineClearResult result = LineClearResolver.PreviewClears(
                board, piece, new GridPosition(4, 2), scratchBoard, rows, columns);

            Assert.IsTrue(result.AnyCleared);
            CollectionAssert.AreEqual(new[] { 2 }, result.ClearedRows);
            CollectionAssert.AreEqual(new[] { 4 }, result.ClearedColumns);

            // Source board must be unchanged.
            Assert.AreEqual(Board.EMPTY, board[new GridPosition(4, 2)]);
        }

        [Test]
        public void PreviewClears_InvalidPlacement_ReturnsEmptyResultWithoutThrowing()
        {
            var board = new Board();
            var scratchBoard = new Board();
            var rows = new List<int>();
            var columns = new List<int>();
            board.Occupy(new GridPosition(0, 0), 1);

            Piece piece = SingleCell();
            LineClearResult result = LineClearResolver.PreviewClears(
                board, piece, new GridPosition(0, 0), scratchBoard, rows, columns);

            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.ClearedRows.Count);
            Assert.AreEqual(0, result.ClearedColumns.Count);
        }

        [Test]
        public void PreviewClears_PlacementCompletesNoLines_ReturnsEmptyResult()
        {
            var board = new Board();
            var scratchBoard = new Board();
            var rows = new List<int>();
            var columns = new List<int>();

            Piece piece = SingleCell();
            LineClearResult result = LineClearResolver.PreviewClears(
                board, piece, new GridPosition(0, 0), scratchBoard, rows, columns);

            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.ClearedRows.Count);
            Assert.AreEqual(0, result.ClearedColumns.Count);
        }

        private static Piece SingleCell() => new Piece("test_single_cell", new[] { new GridPosition(0, 0) });

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
    }
}
