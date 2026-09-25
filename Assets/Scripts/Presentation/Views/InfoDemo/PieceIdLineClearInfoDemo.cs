using System.Collections.Generic;
using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.PieceIdLineClear"/> objective card's demo (issue #452, mockup artboard
    /// "3x3 ile temizlik"), authored from the real rule (<see cref="ObjectiveProgress"/>): a placement of
    /// the exact catalog piece <see cref="ObjectiveDefinition.RequiredPieceId"/> that clears at least one
    /// line. The demo is built for the objective's own piece — its shape read from the real
    /// <see cref="PieceCatalog"/> — so the card never shows a different piece than its own text names.
    /// <para>
    /// Board: the piece's footprint sits in the bottom-left corner, every other cell of the rows it spans
    /// is filled, and a little loose decoration sits above. For <c>square_3x3</c> (rows top to bottom,
    /// '.' empty, letters are block colours):
    /// <code>
    /// row2 ....g...
    /// row3 ...uu...
    /// row4 .....b..
    /// row5 ...gbbuu
    /// row6 ...uuggp
    /// row7 ...bbgpu
    /// </code>
    /// The strip holds the progress chip — a miniature of the required piece where other chips have a
    /// caption, 0/1 — on the left and three tray pieces on the right: an L corner (green), the required
    /// piece (magenta), a 1x2 (blue).
    /// </para>
    /// <para>
    /// Choreography (5.0 s loop): 0.45 s the piece lifts from tray slot 1 and glides into the corner ·
    /// ~1.15 s it lands, completing every row it spans · 1.3 s they clear together · 1.7 s "N lines!" (or
    /// "Row complete!" for a one-row piece) · 1.75 s the chip ticks 0/1 → 1/1 and the green check pops in
    /// · hold, fade out from 4.6 s, loop.
    /// </para>
    /// <para>
    /// Any catalog piece id is supported (every one fits the 8x8 corner and completes its rows); no level
    /// in the catalog uses this type yet, and <c>square_3x3</c> is the authoring default. An id that is not
    /// in the catalog gets no demo and the card keeps its glyph.
    /// </para>
    /// </summary>
    internal static class PieceIdLineClearInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        /// <summary>The column the piece's footprint starts at.</summary>
        internal const int LAND_COLUMN = 0;

        internal const float PLACE_START = 0.45f;
        internal const float CLEAR_START = 1.3f;
        internal const float LABEL_START = 1.7f;
        internal const float CHIP_ADVANCE_TIME = 1.75f;

        /// <summary>The widest / tallest a resting tray piece may be, in mockup units, inside its slot of
        /// the chip-left strip (slots are ~50 apart, the strip 60 tall).</summary>
        private const float MOCK_TRAY_MAX_PIECE_WIDTH = 46f;
        private const float MOCK_TRAY_MAX_PIECE_HEIGHT = 50f;

        /// <summary>Fill for the rows the piece spans, bottom (row 7) upward; the piece's own cells are
        /// left empty. The tallest catalog piece spans five rows.</summary>
        private static readonly string[] FillRowsBottomUp = { "ubgbbgpu", "gpbuuggp", "pbugbbuu", "ggpuubgp", "upbgpubg" };

        /// <summary>Loose decoration above the filled rows, nearest row first — never completing a line.</summary>
        private static readonly string[] DecorRowsBottomUp = { ".....b..", "...uu...", "....g..." };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>Whether a demo exists for an objective naming <paramref name="pieceId"/>.</summary>
        internal static bool Supports(string pieceId) => FindPiece(pieceId) != null;

        /// <summary>The catalog piece <paramref name="pieceId"/> names, or null when it names none.</summary>
        internal static Piece FindPiece(string pieceId)
        {
            if (string.IsNullOrEmpty(pieceId))
            {
                return null;
            }

            IReadOnlyList<Piece> pieces = PieceCatalog.AllPieces;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                if (pieces[pieceIndex].Id == pieceId)
                {
                    return pieces[pieceIndex];
                }
            }

            return null;
        }

        /// <summary><paramref name="piece"/>'s cells as demo (column, row) offsets — row 0 at the top, so
        /// the catalog's Y-up offsets flip — with the footprint's top-left at (0, 0).</summary>
        internal static Vector2Int[] DemoShape(Piece piece)
        {
            int maxY = 0;
            int minX = int.MaxValue;
            for (int offsetIndex = 0; offsetIndex < piece.Offsets.Count; offsetIndex++)
            {
                maxY = Mathf.Max(maxY, piece.Offsets[offsetIndex].Y);
                minX = Mathf.Min(minX, piece.Offsets[offsetIndex].X);
            }

            Vector2Int[] shape = new Vector2Int[piece.Offsets.Count];
            for (int offsetIndex = 0; offsetIndex < piece.Offsets.Count; offsetIndex++)
            {
                GridPosition offset = piece.Offsets[offsetIndex];
                shape[offsetIndex] = new Vector2Int(offset.X - minX, maxY - offset.Y);
            }

            return shape;
        }

        /// <summary>How many rows a piece of <paramref name="shape"/> spans — the rows the demo fills and
        /// clears.</summary>
        internal static int RowSpan(Vector2Int[] shape)
        {
            InfoDemoLayout.ShapeBounds(shape, out Vector2Int min, out Vector2Int max);
            return max.y - min.y + 1;
        }

        /// <summary>The topmost row the piece lands in (its footprint's top-left is at this row,
        /// <see cref="LAND_COLUMN"/>).</summary>
        internal static int LandRow(Vector2Int[] shape) => InfoDemoLayout.BOARD_SIZE - RowSpan(shape);

        /// <summary>The demo's starting board for <paramref name="shape"/>, as eight pattern rows.</summary>
        internal static string[] Rows(Vector2Int[] shape)
        {
            int landRow = LandRow(shape);
            int rowSpan = RowSpan(shape);

            char[][] cells = new char[InfoDemoLayout.BOARD_SIZE][];
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                cells[row] = new string('.', InfoDemoLayout.BOARD_SIZE).ToCharArray();
            }

            for (int spanIndex = 0; spanIndex < rowSpan; spanIndex++)
            {
                FillRowsBottomUp[spanIndex].CopyTo(0, cells[InfoDemoLayout.BOARD_SIZE - 1 - spanIndex], 0, InfoDemoLayout.BOARD_SIZE);
            }

            for (int cellIndex = 0; cellIndex < shape.Length; cellIndex++)
            {
                cells[landRow + shape[cellIndex].y][LAND_COLUMN + shape[cellIndex].x] = '.';
            }

            for (int decorIndex = 0; decorIndex < DecorRowsBottomUp.Length; decorIndex++)
            {
                int row = landRow - 1 - decorIndex;
                if (row >= 0)
                {
                    DecorRowsBottomUp[decorIndex].CopyTo(0, cells[row], 0, InfoDemoLayout.BOARD_SIZE);
                }
            }

            string[] rows = new string[InfoDemoLayout.BOARD_SIZE];
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                rows[row] = new string(cells[row]);
            }

            return rows;
        }

        /// <summary>The demo for <paramref name="pieceId"/>, or null when it names no catalog piece.</summary>
        internal static InfoDemoTimeline Build(string pieceId)
        {
            Piece piece = FindPiece(pieceId);
            if (piece == null)
            {
                return null;
            }

            Vector2Int[] shape = DemoShape(piece);
            int landRow = LandRow(shape);
            int rowSpan = RowSpan(shape);

            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows(shape));

            // Strip: the goal chip — the required piece in miniature — on the left, the tray to its right.
            InfoDemoProgressChip chip = InfoDemoChoreography.ProgressChip(builder, shape, InfoDemoPaint.BLOCK_4, 0, 1);

            float trayScale = TrayScale(shape);
            builder.AddPiece(
                CornerShape, InfoDemoPaint.BLOCK_2,
                InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            Vector2 pieceTrayPosition = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int requiredPiece = builder.AddPiece(shape, InfoDemoPaint.BLOCK_4, pieceTrayPosition, trayScale);
            builder.AddPiece(
                DominoShape, InfoDemoPaint.BLOCK_5,
                InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);

            // 1. The required piece drops into the corner, completing every row it spans.
            InfoDemoChoreography.PlacePiece(
                builder, requiredPiece, shape, InfoDemoPaint.BLOCK_4, pieceTrayPosition, landRow, LAND_COLUMN, PLACE_START, trayScale);

            // 2. Those rows clear together.
            for (int row = landRow; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                InfoDemoChoreography.ClearRow(builder, row, CLEAR_START);
            }

            // 3. The label over the cleared band, and the goal ticks over to done.
            Vector2 labelPosition = new Vector2(
                (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, landRow + ((rowSpan - 1) * 0.5f));
            if (rowSpan > 1)
            {
                InfoDemoChoreography.FloatLabel(
                    builder, LocalizationKeys.INFO_POPUP_DEMO_LINES_CLEARED, labelPosition, 1.3f, InfoDemoPaint.INK,
                    LABEL_START, 1.1f, rowSpan.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                InfoDemoChoreography.FloatLabel(
                    builder, LocalizationKeys.INFO_POPUP_DEMO_ROW_COMPLETE, labelPosition, 1.3f, InfoDemoPaint.INK,
                    LABEL_START, 1.1f);
            }

            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);

            return builder.Build();
        }

        /// <summary>The required piece's resting tray scale: the usual one, shrunk for a long piece so it
        /// stays inside its slot.</summary>
        private static float TrayScale(Vector2Int[] shape)
        {
            InfoDemoLayout.ShapeBounds(shape, out Vector2Int min, out Vector2Int max);
            float widest = InfoDemoLayout.FromMockLength(MOCK_TRAY_MAX_PIECE_WIDTH) / (max.x - min.x + 1);
            float tallest = InfoDemoLayout.FromMockLength(MOCK_TRAY_MAX_PIECE_HEIGHT) / (max.y - min.y + 1);
            return Mathf.Min(InfoDemoLayout.TRAY_PIECE_SCALE, Mathf.Min(widest, tallest));
        }
    }
}
