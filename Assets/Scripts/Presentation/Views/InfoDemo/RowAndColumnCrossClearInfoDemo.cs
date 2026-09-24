using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.RowAndColumnCrossClear"/> objective card's demo (issue #452, mockup
    /// artboard "Çapraz"), authored from the real rule (<see cref="ObjectiveProgress"/>): one placement
    /// must clear at least one row AND at least one column at the same time. The objective has no
    /// parameter, so there is one demo.
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 g.......
    /// row1 u..g....
    /// row2 b..uu...
    /// row3 p.......     column 0 is full except (7,0)
    /// row4 g....b..
    /// row5 u...bb..
    /// row6 b.......
    /// row7 .ppgguub     row 7 is full except (7,0)
    /// </code>
    /// The strip holds the progress chip ("CROSS", 0/1) on the left and three tray pieces on the right:
    /// an L corner (green), the single (magenta) that will be played, a 1x2 (blue).
    /// 0.45 s the single lifts from tray slot 1 and glides to (7,0) · ~1.15 s it lands, completing row 7
    /// and column 0 at once · 1.3 s both lines glow and clear outward from the corner, the two arms in
    /// step · 1.75 s "Row + column!" floats up · 1.8 s the chip ticks 0/1 → 1/1 and the green check pops
    /// in · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class RowAndColumnCrossClearInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int CROSS_ROW = 7;
        internal const int CROSS_COLUMN = 0;

        internal const float PLACE_START = 0.45f;
        internal const float CLEAR_START = 1.3f;
        internal const float LABEL_START = 1.75f;
        internal const float CHIP_ADVANCE_TIME = 1.8f;

        internal static readonly string[] Rows =
        {
            "g.......",
            "u..g....",
            "b..uu...",
            "p.......",
            "g....b..",
            "u...bb..",
            "b.......",
            ".ppgguub",
        };

        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            // Strip: the goal chip on the left, the tray packed to the right of it.
            InfoDemoProgressChip chip = InfoDemoChoreography.ProgressChip(
                builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_CROSS, null, 0, 1);

            builder.AddPiece(
                CornerShape, InfoDemoPaint.BLOCK_2,
                InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            Vector2 singleTrayPosition = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int single = builder.AddPiece(
                SingleShape, InfoDemoPaint.BLOCK_4, singleTrayPosition, InfoDemoLayout.TRAY_PIECE_SCALE);
            builder.AddPiece(
                DominoShape, InfoDemoPaint.BLOCK_5,
                InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);

            // 1. The single drops into the corner both lines are missing.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_4, singleTrayPosition,
                CROSS_ROW, CROSS_COLUMN, PLACE_START);

            // 2. Row 7 and column 0 clear together — one placement, a row AND a column.
            InfoDemoChoreography.ClearCross(builder, CROSS_ROW, CROSS_COLUMN, CLEAR_START);

            // 3. "Row + column!" over the board's middle, and the goal ticks over to done.
            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_ROW_AND_COLUMN,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 4.5f),
                1.3f,
                InfoDemoPaint.INK,
                LABEL_START,
                1.1f);

            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);

            return builder.Build();
        }
    }
}
