using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.Joker"/> info demo (issue #448, mockup artboard "Joker"), authored from
    /// the real rule (<c>PowerUpSystem.TryApplyJoker</c> / <c>JokerFillResolver</c>): arm it, tap an
    /// <em>empty</em> cell, the cell fills, and a row or column that fill completed clears exactly as a
    /// placement would. (The old card text described a single block you drag — the code has none.)
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 ........
    /// row2 ........
    /// row3 ...g....
    /// row4 ..uu....
    /// row5 bb.pggbu     one gap at (5,2)
    /// row6 g.......
    /// row7 gg...b..
    /// </code>
    /// The strip holds the violet button (count 3) on the left and an idle tray on the right.
    /// 0.6 s a finger taps the button, which arms · 1.45 s a finger taps the empty (5,2) · 1.55 s it
    /// fills with a small burst, and the count ticks 3 → 2 · 1.7 s disarm · 1.95 s row 5 is
    /// full and clears · 2.5 s "Row complete!" floats up · hold, fade out from 4.6 s, loop.
    /// </para>
    /// <para>
    /// The fill is drawn as <see cref="InfoDemoPaint.BLOCK_1"/> — the real fill takes colour id 1
    /// (<c>PowerUpSystem.JOKER_FILL_COLOUR_ID</c>), the theme's first block colour, so the demo matches it.
    /// </para>
    /// </summary>
    internal static class JokerInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int TARGET_ROW = 5;
        internal const int TARGET_COLUMN = 2;

        internal const float PRESS_TIME = 0.6f;
        internal const float DISARM_TIME = 1.7f;
        internal const float TAP_TIME = 1.45f;
        internal const float FILL_TIME = 1.55f;
        internal const float CLEAR_START = 1.95f;
        internal const float LABEL_START = 2.5f;

        private const int BUTTON_COUNT = 3;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "...g....",
            "..uu....",
            "bb.pggbu",
            "g.......",
            "gg...b..",
        };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.Joker, InfoDemoPaint.PLATE_JOKER, BUTTON_COUNT);
            InfoDemoPowerUpChoreography.FillerTray(builder);

            // 1. Arm it, then tap the one empty cell of row 5.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, true, DISARM_TIME);
            InfoDemoChoreography.TapCell(builder, TAP_TIME, TARGET_ROW, TARGET_COLUMN);

            // 2. The cell fills as the real colour (theme block colour 1), with a small burst; the charge is spent.
            InfoDemoChoreography.FillCell(builder, TARGET_ROW, TARGET_COLUMN, InfoDemoPaint.BLOCK_1, FILL_TIME);
            InfoDemoChoreography.Burst(
                builder, InfoDemoLayout.Cell(TARGET_ROW, TARGET_COLUMN), InfoDemoPaint.BLOCK_1, InfoDemoPaint.NONE,
                1f, 2.6f, FILL_TIME, 0.45f);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, FILL_TIME);

            // 3. The fill completed row 5, which clears like any full line.
            InfoDemoChoreography.ClearRow(builder, TARGET_ROW, CLEAR_START);

            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_ROW_COMPLETE,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, TARGET_ROW + 0.1f),
                1.1f,
                InfoDemoPaint.PLATE_JOKER,
                LABEL_START,
                1.2f);

            return builder.Build();
        }
    }
}
