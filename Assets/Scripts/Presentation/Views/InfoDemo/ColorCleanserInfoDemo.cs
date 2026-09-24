using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.ColorCleanser"/> info demo (issue #448, mockup artboard "Renk
    /// Temizleyici"), authored from the real rule (<c>PowerUpSystem.TryApplyColorCleanser</c> /
    /// <c>PowerUpClearResolver.ResolveColorCleanser</c>): arm it, tap an <em>occupied</em> cell, and every
    /// block on the board of that cell's colour is destroyed. (The old card text had the player choose a
    /// colour — the code takes it from the tapped cell.)
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours; the
    /// eleven 'p' cells are the colour that goes):
    /// <code>
    /// row0 ........
    /// row1 ..p..g..
    /// row2 .pbb.p..
    /// row3 ..gpp.u.     tapped cell (3,3)
    /// row4 .u.gbp..
    /// row5 ..p..g..
    /// row6 bp..gg.p
    /// row7 ggpbb.pu
    /// </code>
    /// The strip holds the green button (count 3) on the left and an idle tray on the right.
    /// 0.6 s a finger taps the button, which arms · 1.45 s a finger taps (3,3) · 1.55 s every block of
    /// that colour pulses bright, twice · 1.8 s disarm · 1.95 s they all go, nearest to the tap first,
    /// 45 ms apart, and the count ticks 3 → 2 · 2.9 s "Same colour, gone!" floats up · hold, fade out
    /// from 4.6 s, loop. The label never names the colour, so it stays true under any theme.
    /// </para>
    /// </summary>
    internal static class ColorCleanserInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int TARGET_ROW = 3;
        internal const int TARGET_COLUMN = 3;

        internal const float PRESS_TIME = 0.6f;
        internal const float DISARM_TIME = 1.8f;
        internal const float TAP_TIME = 1.45f;
        internal const float PULSE_START = 1.55f;
        internal const float PULSE_DURATION = 0.4f;
        internal const float CLEAR_START = 1.95f;
        internal const float CLEAR_STAGGER = 0.045f;
        internal const float LABEL_START = 2.9f;

        private const int BUTTON_COUNT = 3;

        internal static readonly string[] Rows =
        {
            "........",
            "..p..g..",
            ".pbb.p..",
            "..gpp.u.",
            ".u.gbp..",
            "..p..g..",
            "bp..gg.p",
            "ggpbb.pu",
        };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.ColorCleanser, InfoDemoPaint.PLATE_CLEANSER, BUTTON_COUNT);
            InfoDemoPowerUpChoreography.FillerTray(builder);

            // 1. Arm it, then tap an occupied cell — its colour is the one that goes.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, true, DISARM_TIME);
            InfoDemoChoreography.TapCell(builder, TAP_TIME, TARGET_ROW, TARGET_COLUMN);

            // 2. Every block of that colour lights up, then goes from the tap outward.
            int targetPaint = InfoDemoBoardPattern.PaintAt(Rows, TARGET_ROW, TARGET_COLUMN);
            Vector2Int[] sameColour = InfoDemoBoardPattern.CellsOfPaint(Rows, targetPaint);
            InfoDemoChoreography.PulseCells(builder, sameColour, PULSE_START, PULSE_DURATION, 0.6f, 2);
            InfoDemoChoreography.ClearCells(
                builder, sameColour, InfoDemoLayout.Cell(TARGET_ROW, TARGET_COLUMN), CLEAR_START, CLEAR_STAGGER);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, CLEAR_START);

            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_SAME_COLOUR,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, TARGET_ROW + 0.6f),
                1.1f,
                InfoDemoPaint.PLATE_CLEANSER,
                LABEL_START,
                1.2f);

            return builder.Build();
        }
    }
}
