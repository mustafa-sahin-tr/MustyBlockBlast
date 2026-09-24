using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.RowClear"/> and <see cref="PowerUpKind.ColumnClear"/> info demos
    /// (issue #448, mockup artboard "Satır/Sütun Temizleyici") — one script, two boards — authored from
    /// the real rule (<c>PowerUpSystem.TryApplyRowClear</c>/<c>TryApplyColumnClear</c>): arm it, tap any
    /// cell, and that cell's whole row (column) is destroyed, full or not.
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// Row Clear            Column Clear
    /// row0 ........        ........
    /// row1 ........        ...b....
    /// row2 ...gg...        ..pb....
    /// row3 .u..pp..        ...u.g..
    /// row4 bb.gg.pu        ...g.gg.     tapped cell (4,3)
    /// row5 ..uu....        .uu.....
    /// row6 g....bb.        ...p..b.
    /// row7 gg..pbbu        gg.p.bbb
    /// </code>
    /// The strip holds the blue button (count 3) on the left and an idle tray on the right.
    /// 0.6 s a finger taps the button, which arms · 1.45 s a finger taps (4,3) · 1.5 s a beam runs the
    /// line · 1.55 s the (not full) line clears, left to right (top to bottom), and the count ticks
    /// 3 → 2 · 1.8 s disarm · 2.4 s "Even if not full!" floats up · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class LineClearPowerUpInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int TARGET_ROW = 4;
        internal const int TARGET_COLUMN = 3;

        internal const float PRESS_TIME = 0.6f;
        internal const float DISARM_TIME = 1.8f;
        internal const float TAP_TIME = 1.45f;
        internal const float BEAM_TIME = 1.5f;
        internal const float CLEAR_START = 1.55f;
        internal const float LABEL_START = 2.4f;

        private const int BUTTON_COUNT = 3;

        internal static readonly string[] RowClearRows =
        {
            "........",
            "........",
            "...gg...",
            ".u..pp..",
            "bb.gg.pu",
            "..uu....",
            "g....bb.",
            "gg..pbbu",
        };

        internal static readonly string[] ColumnClearRows =
        {
            "........",
            "...b....",
            "..pb....",
            "...u.g..",
            "...g.gg.",
            ".uu.....",
            "...p..b.",
            "gg.p.bbb",
        };

        /// <summary>The Row Clear demo when <paramref name="isRow"/>, otherwise the Column Clear demo.</summary>
        internal static InfoDemoTimeline Build(bool isRow)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, isRow ? RowClearRows : ColumnClearRows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, isRow ? PowerUpKind.RowClear : PowerUpKind.ColumnClear, InfoDemoPaint.PLATE_LINE_CLEAR, BUTTON_COUNT);
            InfoDemoPowerUpChoreography.FillerTray(builder);

            // 1. Arm it, then tap any cell of the line.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, true, DISARM_TIME);
            InfoDemoChoreography.TapCell(builder, TAP_TIME, TARGET_ROW, TARGET_COLUMN);

            // 2. A beam runs the line and the whole line goes — gaps and all.
            int lineIndex = isRow ? TARGET_ROW : TARGET_COLUMN;
            InfoDemoChoreography.Beam(builder, BEAM_TIME, isRow, lineIndex, InfoDemoPaint.PLATE_LINE_CLEAR);
            if (isRow)
            {
                InfoDemoChoreography.ClearRow(builder, lineIndex, CLEAR_START);
            }
            else
            {
                InfoDemoChoreography.ClearColumn(builder, lineIndex, CLEAR_START);
            }

            InfoDemoPowerUpChoreography.SpendCharge(builder, button, CLEAR_START);

            // 3. The point: it did not need to be full.
            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_EVEN_IF_NOT_FULL,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, TARGET_ROW + 0.1f),
                1.1f,
                InfoDemoPaint.PLATE_LINE_CLEAR,
                LABEL_START,
                1.2f);

            return builder.Build();
        }
    }
}
