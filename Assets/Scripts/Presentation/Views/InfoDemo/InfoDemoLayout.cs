using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The coordinate space every info demo is authored in: "board units", where one unit is one cell
    /// pitch of the 8x8 mini board, x runs along columns and y down rows — so cell (row, column) sits
    /// exactly at (column, row). The tray strip under the board lives in the same space (its slots sit
    /// at a fractional row below row 7), so a piece can glide from the tray onto the board with one
    /// straight Position step. <see cref="InfoDemoStage"/> maps these to pixels.
    /// <para>
    /// Mockup geometry (Design canvas, info demos, #445): a 257 x 317 stage — 8 cells of 27 with a 3
    /// gap inside a 10 padding, over a 60-tall strip.
    /// </para>
    /// </summary>
    internal static class InfoDemoLayout
    {
        internal const int BOARD_SIZE = 8;
        internal const int BOARD_CELL_COUNT = BOARD_SIZE * BOARD_SIZE;

        internal const float MOCK_CELL = 27f;
        internal const float MOCK_GAP = 3f;
        internal const float MOCK_PAD = 10f;
        internal const float MOCK_PITCH = MOCK_CELL + MOCK_GAP;
        internal const float MOCK_BOARD_EXTENT = (MOCK_PAD * 2f) + (BOARD_SIZE * MOCK_CELL) + ((BOARD_SIZE - 1) * MOCK_GAP);
        internal const float MOCK_STRIP_HEIGHT = 60f;
        internal const float MOCK_STAGE_HEIGHT = MOCK_BOARD_EXTENT + MOCK_STRIP_HEIGHT;

        /// <summary>Resting scale of a piece sitting in a tray slot, relative to a board cell.</summary>
        internal const float TRAY_PIECE_SCALE = 0.42f;

        /// <summary>Scale a tray piece lifts to when picked up, before it glides to the board.</summary>
        internal const float LIFTED_PIECE_SCALE = 0.52f;

        internal const int TRAY_SLOT_COUNT = 3;

        /// <summary>Element id of the board block at (<paramref name="row"/>, <paramref name="column"/>).
        /// Board blocks are always the first 64 elements of every timeline.</summary>
        internal static int BoardBlockId(int row, int column) => (row * BOARD_SIZE) + column;

        /// <summary>Centre of cell (<paramref name="row"/>, <paramref name="column"/>) in board units.</summary>
        internal static Vector2 Cell(int row, int column) => new Vector2(column, row);

        /// <summary>Centre of a group of cells spanning rows <paramref name="topRow"/>..<paramref name="bottomRow"/>
        /// and columns <paramref name="leftColumn"/>..<paramref name="rightColumn"/>, inclusive.</summary>
        internal static Vector2 CellSpanCentre(int topRow, int leftColumn, int bottomRow, int rightColumn)
            => new Vector2((leftColumn + rightColumn) * 0.5f, (topRow + bottomRow) * 0.5f);

        /// <summary>Centre of tray slot <paramref name="slotIndex"/> (0..2) in board units: the strip's
        /// vertical centre, each slot a third of the stage wide.</summary>
        internal static Vector2 TraySlot(int slotIndex)
        {
            float slotCentreX = MOCK_BOARD_EXTENT * ((slotIndex * 2) + 1) / (TRAY_SLOT_COUNT * 2f);
            float stripCentreY = MOCK_BOARD_EXTENT + (MOCK_STRIP_HEIGHT * 0.5f);
            return FromMockPoint(new Vector2(slotCentreX, stripCentreY));
        }

        /// <summary>A point in mockup units (origin at the stage's top-left, y down) in board units.</summary>
        internal static Vector2 FromMockPoint(Vector2 mockPoint)
        {
            float firstCentre = MOCK_PAD + (MOCK_CELL * 0.5f);
            return new Vector2((mockPoint.x - firstCentre) / MOCK_PITCH, (mockPoint.y - firstCentre) / MOCK_PITCH);
        }

        /// <summary>A board-unit point in mockup units (origin at the stage's top-left, y down).</summary>
        internal static Vector2 ToMockPoint(Vector2 boardPoint)
        {
            float firstCentre = MOCK_PAD + (MOCK_CELL * 0.5f);
            return new Vector2(firstCentre + (boardPoint.x * MOCK_PITCH), firstCentre + (boardPoint.y * MOCK_PITCH));
        }

        /// <summary>Centre of a piece whose top-left cell sits at (<paramref name="row"/>,
        /// <paramref name="column"/>) — the midpoint of its shape's bounding box.</summary>
        internal static Vector2 PieceCentre(Vector2Int[] shape, int row, int column)
        {
            ShapeBounds(shape, out Vector2Int min, out Vector2Int max);
            return new Vector2(column + ((min.x + max.x) * 0.5f), row + ((min.y + max.y) * 0.5f));
        }

        /// <summary>Inclusive min/max (column, row) offsets of <paramref name="shape"/>.</summary>
        internal static void ShapeBounds(Vector2Int[] shape, out Vector2Int min, out Vector2Int max)
        {
            min = new Vector2Int(int.MaxValue, int.MaxValue);
            max = new Vector2Int(int.MinValue, int.MinValue);

            for (int cellIndex = 0; cellIndex < shape.Length; cellIndex++)
            {
                Vector2Int offset = shape[cellIndex];
                min = Vector2Int.Min(min, offset);
                max = Vector2Int.Max(max, offset);
            }
        }
    }
}
