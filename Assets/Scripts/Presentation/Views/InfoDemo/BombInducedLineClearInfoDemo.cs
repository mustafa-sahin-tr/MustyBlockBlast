using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.BombInducedLineClear"/> objective card's demo (issue #452, mockup
    /// artboard "Bomba ile boşaltma"), authored from the real rule: the objective is fed only by a Bomb
    /// power-up clear (<c>ObjectiveSystem.OnPowerUpApplied</c> → <see cref="ObjectiveProgress.ApplyPowerUpLineEmptied"/>)
    /// whose 3x3 blast (<see cref="PowerUpClearResolver.ResolveBombClear"/>) took the <b>last</b> blocks of a
    /// row or column and left it completely <b>empty</b> (<see cref="PowerUpClearResult.EmptiedLineCount"/>)
    /// — the opposite of a normal line clear, which fires on a line becoming full. A full line cleared by a
    /// placement never counts. The objective has no parameter, so there is one demo.
    /// <para>
    /// Choreography (5.2 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 ..g..u..
    /// row2 ...pb...     row 2's only blocks sit inside the blast
    /// row3 ..gubg..     (3,4) is the tapped centre; rows 2-4, columns 3-5 blast
    /// row4 .u.gu.b.
    /// row5 g...bb..
    /// row6 gg..uu.b
    /// row7 ppgg.uub
    /// </code>
    /// The strip holds the progress chip ("EMPTY LINE", 0/1) on the left, the red Bomb button (count 3)
    /// beside it and two idle tray pieces on the right.
    /// 0.5 s a finger taps the button, which pops and arms · 1.3 s a finger taps (3,4) · 1.35 s the 3x3 is
    /// previewed in coral · 1.5 s an orange blast · 1.52 s the seven blocks in the 3x3 go from the centre
    /// outward and the count ticks 3 → 2 · 2.0 s row 2 — now completely empty — pulses a green outline ·
    /// 2.1 s "Row emptied!" · 2.2 s the chip ticks 0/1 → 1/1 and the green check pops in · hold, fade out
    /// from 4.8 s, loop. Every other row and column the blast touched keeps a block outside it, so row 2
    /// is the one line emptied.
    /// </para>
    /// </summary>
    internal static class BombInducedLineClearInfoDemo
    {
        internal const float LOOP_DURATION = 5.2f;

        internal const int TARGET_ROW = 3;
        internal const int TARGET_COLUMN = 4;

        /// <summary>The row the blast leaves completely empty.</summary>
        internal const int EMPTIED_ROW = 2;

        internal const float PRESS_TIME = 0.5f;
        internal const float TAP_TIME = 1.3f;
        internal const float PREVIEW_START = 1.35f;
        internal const float PREVIEW_END = 1.7f;
        internal const float BLAST_START = 1.5f;
        internal const float CLEAR_START = 1.52f;
        internal const float CLEAR_STAGGER = 0.02f;
        internal const float DISARM_TIME = 1.55f;
        internal const float EMPTIED_OUTLINE_START = 2.0f;
        internal const float EMPTIED_OUTLINE_END = 3.4f;
        internal const float LABEL_START = 2.1f;
        internal const float CHIP_ADVANCE_TIME = 2.2f;

        private const int BUTTON_COUNT = 3;
        private const float PREVIEW_ALPHA = 0.55f;

        internal static readonly string[] Rows =
        {
            "........",
            "..g..u..",
            "...pb...",
            "..gubg..",
            ".u.gu.b.",
            "g...bb..",
            "gg..uu.b",
            "ppgg.uub",
        };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        internal static InfoDemoTimeline Build() => Build(out _, out _);

        /// <summary>As <see cref="Build()"/>, handing back the chip and the Bomb button so tests can read
        /// their state.</summary>
        internal static InfoDemoTimeline Build(out InfoDemoProgressChip chip, out InfoDemoPowerUpButton button)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            // Strip: the goal chip on the left, the Bomb beside it, an idle tray to the right.
            chip = InfoDemoChoreography.ProgressChip(builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_EMPTY_LINE, null, 0, 1);
            button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.Bomb, InfoDemoPaint.PLATE_BOMB, BUTTON_COUNT,
                InfoDemoPowerUpChoreography.StripButtonBesideChip);
            builder.AddPiece(
                CornerShape, InfoDemoPaint.BLOCK_2,
                InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            builder.AddPiece(
                DominoShape, InfoDemoPaint.BLOCK_5,
                InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);

            // 1. Arm the bomb, then aim it at (3,4).
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, true, DISARM_TIME);
            InfoDemoChoreography.TapCell(builder, TAP_TIME, TARGET_ROW, TARGET_COLUMN);

            // 2. The 3x3 it will take, then the blast.
            InfoDemoChoreography.RectHighlight(
                builder, TARGET_ROW - 1, TARGET_COLUMN - 1, TARGET_ROW + 1, TARGET_COLUMN + 1,
                InfoDemoPaint.BLAST_PREVIEW, PREVIEW_ALPHA, PREVIEW_START, PREVIEW_END);

            Vector2 centre = InfoDemoLayout.Cell(TARGET_ROW, TARGET_COLUMN);
            InfoDemoChoreography.Burst(builder, centre, InfoDemoPaint.BLAST, InfoDemoPaint.ARMED, 1.2f, 4.2f, BLAST_START, 0.55f);

            // 3. Every block of the 3x3 goes, from the centre outward; the charge is spent.
            InfoDemoChoreography.ClearCells(builder, BlastCells(), centre, CLEAR_START, CLEAR_STAGGER);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, CLEAR_START);

            // 4. Row 2 had nothing outside the blast: it is now completely empty — that is the goal.
            InfoDemoChoreography.PulseOutline(
                builder,
                InfoDemoLayout.CellSpanCentre(EMPTIED_ROW, 0, EMPTIED_ROW, InfoDemoLayout.BOARD_SIZE - 1),
                InfoDemoLayout.BOARD_SIZE,
                1,
                InfoDemoPaint.SUCCESS,
                EMPTIED_OUTLINE_START,
                EMPTIED_OUTLINE_END,
                2);

            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_ROW_EMPTIED,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, EMPTIED_ROW - 0.4f),
                1.0f,
                InfoDemoPaint.INK,
                LABEL_START,
                1.2f);

            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);

            return builder.Build();
        }

        /// <summary>The occupied cells (x = column, y = row) inside the 3x3 around the tapped centre.</summary>
        internal static Vector2Int[] BlastCells()
            => InfoDemoBoardPattern.OccupiedCells(
                Rows, TARGET_ROW - 1, TARGET_COLUMN - 1, TARGET_ROW + 1, TARGET_COLUMN + 1);
    }
}
