using MustyBlockBlast.Gameplay;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.Bomb"/> info demo (issue #448, mockup artboard "Bomba"), authored from
    /// the real rule (<c>PowerUpSystem.TryApplyBomb</c> / <c>PowerUpClearResolver.ResolveBombClear</c>):
    /// arm the bomb, tap a cell, and every block in the 3x3 around it is destroyed.
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 ..pp.g..
    /// row2 .bbgg.u.     rows 2-4, columns 2-4 is the blast area
    /// row3 .ugpbbu.     (3,3) is the tapped centre
    /// row4 ..bgpu..
    /// row5 ...uu...
    /// row6 g......b
    /// row7 gg....bb
    /// </code>
    /// The strip holds the red bomb button (count 3) on the left and an idle tray on the right.
    /// 0.6 s a finger taps the button, which pops and arms (yellow ring) · 1.45 s a finger taps (3,3) ·
    /// 1.5 s the 3x3 is previewed in coral · 1.7 s an orange blast · 1.72 s the nine blocks go from the
    /// centre outward, 20 ms apart, and the count ticks 3 → 2 · 1.75 s the button disarms · hold, fade
    /// out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class BombInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int TARGET_ROW = 3;
        internal const int TARGET_COLUMN = 3;

        internal const float PRESS_TIME = 0.6f;
        internal const float DISARM_TIME = 1.75f;
        internal const float TAP_TIME = 1.45f;
        internal const float PREVIEW_START = 1.5f;
        internal const float PREVIEW_END = 1.85f;
        internal const float BLAST_START = 1.7f;
        internal const float CLEAR_START = 1.72f;
        internal const float CLEAR_STAGGER = 0.02f;

        private const int BUTTON_COUNT = 3;
        private const float PREVIEW_ALPHA = 0.55f;

        internal static readonly string[] Rows =
        {
            "........",
            "..pp.g..",
            ".bbgg.u.",
            ".ugpbbu.",
            "..bgpu..",
            "...uu...",
            "g......b",
            "gg....bb",
        };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.Bomb, InfoDemoPaint.PLATE_BOMB, BUTTON_COUNT);
            InfoDemoPowerUpChoreography.FillerTray(builder);

            // 1. Arm the bomb, then aim it at (3,3).
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, true, DISARM_TIME);
            InfoDemoChoreography.TapCell(builder, TAP_TIME, TARGET_ROW, TARGET_COLUMN);

            // 2. The 3x3 it will take, then the blast.
            InfoDemoChoreography.RectHighlight(
                builder, TARGET_ROW - 1, TARGET_COLUMN - 1, TARGET_ROW + 1, TARGET_COLUMN + 1,
                InfoDemoPaint.BLAST_PREVIEW, PREVIEW_ALPHA, PREVIEW_START, PREVIEW_END);

            Vector2 centre = InfoDemoLayout.Cell(TARGET_ROW, TARGET_COLUMN);
            InfoDemoChoreography.Burst(builder, centre, InfoDemoPaint.BLAST, InfoDemoPaint.ARMED, 1.2f, 4.2f, BLAST_START, 0.55f);

            // 3. Every block of the 3x3 goes, from the centre outward; the charge is spent.
            Vector2Int[] blastCells = InfoDemoBoardPattern.OccupiedCells(
                Rows, TARGET_ROW - 1, TARGET_COLUMN - 1, TARGET_ROW + 1, TARGET_COLUMN + 1);
            InfoDemoChoreography.ClearCells(builder, blastCells, centre, CLEAR_START, CLEAR_STAGGER);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, CLEAR_START);

            return builder.Build();
        }
    }
}
