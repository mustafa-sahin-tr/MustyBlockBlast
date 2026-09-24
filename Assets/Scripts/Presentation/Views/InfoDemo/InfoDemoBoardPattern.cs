using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// A demo's starting board written as eight pattern rows, top to bottom (one character per column —
    /// see <see cref="InfoDemoPaint.FromPatternChar"/>), and the cell queries a script needs about it
    /// (issue #448): which cells of a region are occupied, which cells hold one colour. Build-time only —
    /// every query allocates its result.
    /// </summary>
    internal static class InfoDemoBoardPattern
    {
        /// <summary>Paints the builder's starting board from <paramref name="rows"/>.</summary>
        internal static void Apply(InfoDemoTimelineBuilder builder, string[] rows)
        {
            int rowCount = Mathf.Min(rows.Length, InfoDemoLayout.BOARD_SIZE);
            for (int row = 0; row < rowCount; row++)
            {
                builder.SetBoardRow(row, rows[row]);
            }
        }

        /// <summary>The paint of cell (<paramref name="row"/>, <paramref name="column"/>) in
        /// <paramref name="rows"/>, <see cref="InfoDemoPaint.NONE"/> when empty or outside the pattern.</summary>
        internal static int PaintAt(string[] rows, int row, int column)
        {
            if (row < 0 || row >= rows.Length || column < 0 || column >= rows[row].Length)
            {
                return InfoDemoPaint.NONE;
            }

            return InfoDemoPaint.FromPatternChar(rows[row][column]);
        }

        /// <summary>Every occupied cell (x = column, y = row) in the inclusive region, row by row.</summary>
        internal static Vector2Int[] OccupiedCells(string[] rows, int topRow, int leftColumn, int bottomRow, int rightColumn)
        {
            List<Vector2Int> cells = new List<Vector2Int>(InfoDemoLayout.BOARD_CELL_COUNT);
            for (int row = Mathf.Max(0, topRow); row <= Mathf.Min(InfoDemoLayout.BOARD_SIZE - 1, bottomRow); row++)
            {
                for (int column = Mathf.Max(0, leftColumn); column <= Mathf.Min(InfoDemoLayout.BOARD_SIZE - 1, rightColumn); column++)
                {
                    if (PaintAt(rows, row, column) != InfoDemoPaint.NONE)
                    {
                        cells.Add(new Vector2Int(column, row));
                    }
                }
            }

            return cells.ToArray();
        }

        /// <summary>Every occupied cell (x = column, y = row) of row <paramref name="row"/> and column
        /// <paramref name="column"/> — the cross through that cell, its centre counted once.</summary>
        internal static Vector2Int[] OccupiedCross(string[] rows, int row, int column)
        {
            List<Vector2Int> cells = new List<Vector2Int>(InfoDemoLayout.BOARD_SIZE * 2);
            for (int crossColumn = 0; crossColumn < InfoDemoLayout.BOARD_SIZE; crossColumn++)
            {
                if (PaintAt(rows, row, crossColumn) != InfoDemoPaint.NONE)
                {
                    cells.Add(new Vector2Int(crossColumn, row));
                }
            }

            for (int crossRow = 0; crossRow < InfoDemoLayout.BOARD_SIZE; crossRow++)
            {
                if (crossRow != row && PaintAt(rows, crossRow, column) != InfoDemoPaint.NONE)
                {
                    cells.Add(new Vector2Int(column, crossRow));
                }
            }

            return cells.ToArray();
        }

        /// <summary>Every cell (x = column, y = row) painted <paramref name="paint"/>, row by row.</summary>
        internal static Vector2Int[] CellsOfPaint(string[] rows, int paint)
        {
            List<Vector2Int> cells = new List<Vector2Int>(InfoDemoLayout.BOARD_CELL_COUNT);
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    if (PaintAt(rows, row, column) == paint)
                    {
                        cells.Add(new Vector2Int(column, row));
                    }
                }
            }

            return cells.ToArray();
        }
    }
}
