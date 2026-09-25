using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.TimerCellsMeltedInTime"/> objective card's demo (issue #453, mockup
    /// artboard "Zamanında erit"), authored from the real rule (<see cref="TimerCellTick"/>,
    /// <c>BoardSystem.TryPlacePiece</c>, <see cref="TimerCellClearEffect"/>): a timer cell counts down by one on
    /// every placement anywhere, after that placement's clears; one destroyed before it reaches 0 counts
    /// (the target is always every timer cell the level authored). One that runs out turns into a plain block
    /// — and in Path mode ends the run. The same rule as the Timer special-cell demo (#450,
    /// <see cref="TimerInfoDemo"/>), whose board it shares; here the cell is drawn exactly as the board draws
    /// it — icon, halo and the countdown number on the cell (<see cref="InfoDemoCellLayer.Timer"/>). The
    /// objective has no parameter, so there is one demo.
    /// <para>
    /// Choreography (5.4 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-2 ........
    /// row3 ....g...
    /// row4 ..uu....
    /// row5 .b...g..
    /// row6 g...gg..
    /// row7 ppgbbb.u     the timer rides on (7,2) with 2 placements left; (7,6) is row 7's gap
    /// </code>
    /// The strip holds the progress chip (the timer icon, 0/1) on the left and three tray pieces on the right.
    /// 0.4 s the 1x2 lands on (2,2)-(2,3), clearing nothing · the countdown ticks 2 → 1 with a "-1" · 2.0 s the
    /// single lands on (7,6) · 2.8 s row 7 clears, taking the timer with one placement to spare · the chip ticks
    /// 0/1 → 1/1 · 3.3 s "Just in time!" · hold, fade out from 5.0 s, loop.
    /// </para>
    /// </summary>
    internal static class TimerCellsMeltedInTimeInfoDemo
    {
        internal const float LOOP_DURATION = 5.4f;

        internal const int TIMER_ROW = 7;
        internal const int TIMER_COLUMN = 2;

        /// <summary>The timer's countdown at loop start (<c>TimerCellAuthoring.MIN_STARTING_COUNTDOWN</c>).</summary>
        internal const int START_COUNTDOWN = 2;

        internal const int FIRST_LAND_ROW = TimerInfoDemo.FIRST_LAND_ROW;
        internal const int FIRST_LAND_COLUMN = TimerInfoDemo.FIRST_LAND_COLUMN;
        internal const int SECOND_LAND_ROW = TimerInfoDemo.SECOND_LAND_ROW;
        internal const int SECOND_LAND_COLUMN = TimerInfoDemo.SECOND_LAND_COLUMN;

        internal const float FIRST_PLACE_START = TimerInfoDemo.FIRST_PLACE_START;
        internal const float SECOND_PLACE_START = TimerInfoDemo.SECOND_PLACE_START;
        internal const float CLEAR_START = TimerInfoDemo.CLEAR_START;
        internal const float CHIP_DELAY = 0.15f;
        internal const float LABEL_TIME = TimerInfoDemo.LABEL_TIME;

        /// <summary>The same board as the Timer special-cell demo.</summary>
        internal static string[] Rows => TimerInfoDemo.Rows;

        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };

        /// <summary>When the first placement lands and the countdown ticks.</summary>
        internal static float TickTime() => TimerInfoDemo.TickTime();

        /// <summary>When the timer goes with row 7's clear.</summary>
        internal static float TimerGoneTime() => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, TIMER_COLUMN);

        internal static InfoDemoTimeline Build() => Build(out _, out _);

        /// <summary>As <see cref="Build()"/>, handing back the chip and the timer layer so a test can read them.</summary>
        internal static InfoDemoTimeline Build(out InfoDemoProgressChip chip, out int timerLayer)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            chip = InfoDemoChoreography.ProgressChip(
                builder, InfoDemoSprite.SpecialCellIcon, (int)SpecialCellKind.Timer, InfoDemoPaint.WHITE, 0, 1);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 dominoSlot = InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft);
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int domino = builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, dominoSlot, scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, scale);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            timerLayer = builder.AddCellLayer(InfoDemoCellLayer.Timer, TIMER_ROW, TIMER_COLUMN, START_COUNTDOWN);

            // 1. A placement anywhere — clearing nothing — ticks the countdown down by one.
            InfoDemoChoreography.PlacePiece(
                builder, domino, DominoShape, InfoDemoPaint.BLOCK_5, dominoSlot, FIRST_LAND_ROW, FIRST_LAND_COLUMN,
                FIRST_PLACE_START);
            float tick = TickTime();
            InfoDemoSpecialCellChoreography.StepLayer(builder, timerLayer, START_COUNTDOWN - 1, tick);
            InfoDemoChoreography.FloatText(
                builder, "-1", InfoDemoLayout.Cell(TIMER_ROW, TIMER_COLUMN) + new Vector2(0.15f, -0.75f), 0.6f,
                InfoDemoPaint.BADGE_RED, 0.45f, tick, 0.9f);

            // 2. The next placement completes row 7 and clears the timer before it runs out: it counts.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, SECOND_LAND_ROW, SECOND_LAND_COLUMN,
                SECOND_PLACE_START);
            InfoDemoChoreography.ClearRow(builder, TIMER_ROW, CLEAR_START);
            float gone = TimerGoneTime();
            InfoDemoSpecialCellChoreography.LayerGoes(builder, timerLayer, gone);
            InfoDemoChoreography.AdvanceChip(builder, chip, gone + CHIP_DELAY);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_JUST_IN_TIME, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 5.8f),
                1f, InfoDemoPaint.SUCCESS, LABEL_TIME, 1.2f);

            return builder.Build();
        }
    }
}
