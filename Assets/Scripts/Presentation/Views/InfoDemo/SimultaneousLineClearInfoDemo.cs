using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.SimultaneousLineClear"/> objective card's demo (issue #447, mockup
    /// artboard "Çoklu Satır Temizleme"), authored from the real rule (<see cref="ObjectiveProgress"/>):
    /// one placement must clear <b>exactly</b> the objective's required line count. The demo is built
    /// for that count N (2..4 — every count the level catalog uses), so the card never shows a
    /// different N than its own text.
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom for N = 2, '.' empty, letters are block
    /// colours; the bottom N rows are full except column 4):
    /// <code>
    /// row3 ....uu..
    /// row4 ...uu...
    /// row5 g.......
    /// row6 ppgg.bbu
    /// row7 uubb.ggp
    /// </code>
    /// The strip holds the progress chip ("EXACTLY N", 0/1) on the left and three tray pieces on the
    /// right: an L corner (green), the vertical 1xN (magenta) that will be played, a 1x2 (blue).
    /// 0.45 s the 1xN lifts from tray slot 1 and glides into column 4 · ~1.15 s it lands, completing
    /// N rows · 1.3 s all N rows flash and clear together, each left to right · 1.7 s "N lines!" floats
    /// up · 1.75 s the chip's counter ticks 0/1 → 1/1 and the green check pops in · hold, fade out from
    /// 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class SimultaneousLineClearInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int MIN_LINE_COUNT = 2;
        internal const int MAX_LINE_COUNT = 4;

        /// <summary>The column the vertical piece fills — the one gap in each of the bottom rows.</summary>
        internal const int GAP_COLUMN = 4;

        internal const float PLACE_START = 0.45f;
        internal const float CLEAR_START = 1.3f;
        internal const float LABEL_START = 1.7f;
        internal const float CHIP_ADVANCE_TIME = 1.75f;

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>The full rows, bottom (row 7) upward; only the bottom N are used.</summary>
        private static readonly string[] FullRowsBottomUp = { "uubb.ggp", "ppgg.bbu", "bbpu.ggu", "ggbb.upp" };

        /// <summary>Loose decoration above the full rows, nearest row first — never completing a line.</summary>
        private static readonly string[] DecorRowsBottomUp = { "g.......", "...uu...", "....uu.." };

        /// <summary>Whether a demo exists for an objective requiring <paramref name="lineCount"/> lines.</summary>
        internal static bool Supports(int lineCount) => lineCount >= MIN_LINE_COUNT && lineCount <= MAX_LINE_COUNT;

        /// <summary>The first (topmost) of the N rows that clear.</summary>
        internal static int FirstClearedRow(int lineCount) => InfoDemoLayout.BOARD_SIZE - lineCount;

        internal static InfoDemoTimeline Build(int lineCount)
        {
            int lines = Mathf.Clamp(lineCount, MIN_LINE_COUNT, MAX_LINE_COUNT);
            string lineCountText = lines.ToString(CultureInfo.InvariantCulture);
            int firstClearedRow = FirstClearedRow(lines);

            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);

            for (int lineIndex = 0; lineIndex < lines; lineIndex++)
            {
                builder.SetBoardRow(InfoDemoLayout.BOARD_SIZE - 1 - lineIndex, FullRowsBottomUp[lineIndex]);
            }

            for (int decorIndex = 0; decorIndex < DecorRowsBottomUp.Length; decorIndex++)
            {
                builder.SetBoardRow(firstClearedRow - 1 - decorIndex, DecorRowsBottomUp[decorIndex]);
            }

            // Strip: the goal chip on the left, the tray packed to the right of it.
            InfoDemoProgressChip chip = InfoDemoChoreography.ProgressChip(
                builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_EXACTLY, lineCountText, 0, 1);

            Vector2Int[] verticalShape = VerticalShape(lines);
            builder.AddPiece(
                CornerShape, InfoDemoPaint.BLOCK_2,
                InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            Vector2 verticalTrayPosition = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int verticalPiece = builder.AddPiece(
                verticalShape, InfoDemoPaint.BLOCK_4, verticalTrayPosition, InfoDemoLayout.TRAY_PIECE_SCALE);
            builder.AddPiece(
                DominoShape, InfoDemoPaint.BLOCK_5,
                InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);

            // 1. The 1xN drops into the gap column, completing the bottom N rows at once.
            InfoDemoChoreography.PlacePiece(
                builder, verticalPiece, verticalShape, InfoDemoPaint.BLOCK_4, verticalTrayPosition,
                firstClearedRow, GAP_COLUMN, PLACE_START);

            // 2. All N rows clear together — one placement, N lines.
            for (int row = firstClearedRow; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                InfoDemoChoreography.ClearRow(builder, row, CLEAR_START);
            }

            // 3. "N lines!" over the cleared band, and the goal ticks over to done.
            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_LINES_CLEARED,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, firstClearedRow + ((lines - 1) * 0.5f)),
                1.3f,
                InfoDemoPaint.INK,
                LABEL_START,
                1.1f,
                lineCountText);

            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);

            return builder.Build();
        }

        private static Vector2Int[] VerticalShape(int length)
        {
            Vector2Int[] shape = new Vector2Int[length];
            for (int cellIndex = 0; cellIndex < length; cellIndex++)
            {
                shape[cellIndex] = new Vector2Int(0, cellIndex);
            }

            return shape;
        }
    }
}
