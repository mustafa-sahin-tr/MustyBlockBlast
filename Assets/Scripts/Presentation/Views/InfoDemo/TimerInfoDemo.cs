using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.Timer"/> info demo (issue #450), authored from the real rule
    /// (<see cref="TimerCellTick"/>, <c>BoardSystem.TryPlacePiece</c>): a timer cell counts down by one on
    /// every successful placement anywhere on the board, and the tick runs after that placement's clears
    /// — so a placement that clears the timer takes it before it can tick again. Cleared before zero it is
    /// simply credited; reaching zero converts it to a plain block (in Path mode that ends the run), which
    /// the card's body says. Starting countdowns are 2–4 (<c>TimerCellAuthoring</c>); the demo's is 3.
    /// <para>
    /// Choreography (5.4 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-2 ........
    /// row3 ....g...
    /// row4 ..uu....
    /// row5 .b...g..
    /// row6 g...gg..
    /// row7 ppgbbb.u     the timer rides on (7,3), a green "3" badge at its top-right; (7,6) is row 7's gap
    /// </code>
    /// The tray holds a 1x2, a single and an L corner.
    /// 0.4 s the 1x2 lifts and lands on (2,2)-(2,3), clearing nothing · the badge ticks 3 → 2 with a small
    /// "-1" · 2.0 s the single lifts and lands on (7,6) · 2.8 s row 7 clears left to right, the timer and
    /// its badge going with it · 3.3 s "Just in time!" · hold, fade out from 5.0 s, loop.
    /// </para>
    /// </summary>
    internal static class TimerInfoDemo
    {
        internal const float LOOP_DURATION = 5.4f;

        internal const float FIRST_PLACE_START = 0.4f;
        internal const float SECOND_PLACE_START = 2.0f;
        internal const float CLEAR_START = 2.8f;
        internal const float LABEL_TIME = 3.3f;

        internal const int TIMER_ROW = 7;
        internal const int TIMER_COLUMN = 3;

        /// <summary>The timer's countdown at loop start (authored countdowns run 2–4).</summary>
        internal const int START_COUNTDOWN = 3;

        /// <summary>Where the 1x2 lands (its left cell): nowhere near a full line.</summary>
        internal const int FIRST_LAND_ROW = 2;
        internal const int FIRST_LAND_COLUMN = 2;

        /// <summary>Where the single lands: row 7's one gap.</summary>
        internal const int SECOND_LAND_ROW = TIMER_ROW;
        internal const int SECOND_LAND_COLUMN = 6;

        /// <summary>The badge ticks this long after the first placement lands.</summary>
        private const float TICK_DELAY = 0.05f;

        /// <summary>The countdown badge at the timer cell's top-right, in board units.</summary>
        private static readonly Vector2 BadgeOffset = new Vector2(0.36f, -0.36f);
        private const float BADGE_DIAMETER = 0.44f;
        private const float BADGE_RIM = 0.05f;
        private const float BADGE_FONT = 0.32f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "....g...",
            "..uu....",
            ".b...g..",
            "g...gg..",
            "ppgbbb.u",
        };

        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };

        internal static Vector2 BadgeCentre => InfoDemoLayout.Cell(TIMER_ROW, TIMER_COLUMN) + BadgeOffset;

        /// <summary>When the first placement lands and the countdown ticks.</summary>
        internal static float TickTime()
            => FIRST_PLACE_START + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION + TICK_DELAY;

        /// <summary>When the timer starts to go with row 7's clear.</summary>
        internal static float TimerGoneTime() => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, TIMER_COLUMN);

        internal static InfoDemoTimeline Build() => Build(out InfoDemoCountBadge _);

        /// <summary>As <see cref="Build"/>, handing back the countdown badge so a test can read it.</summary>
        internal static InfoDemoTimeline Build(out InfoDemoCountBadge badge)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 dominoSlot = InfoDemoLayout.TraySlot(0);
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1);
            int domino = builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, dominoSlot, scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, scale);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(2), scale);

            int timerIcon = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.Timer, TIMER_ROW, TIMER_COLUMN, out int timerHalo);
            badge = InfoDemoHudChoreography.CountBadge(
                builder, BadgeCentre, START_COUNTDOWN, InfoDemoPaint.SUCCESS, BADGE_DIAMETER, BADGE_RIM, BADGE_FONT);

            // 1. A placement anywhere — clearing nothing — ticks the countdown down by one.
            InfoDemoChoreography.PlacePiece(
                builder, domino, DominoShape, InfoDemoPaint.BLOCK_5, dominoSlot, FIRST_LAND_ROW, FIRST_LAND_COLUMN,
                FIRST_PLACE_START);
            float tick = TickTime();
            InfoDemoHudChoreography.TickBadge(builder, badge, tick);
            InfoDemoChoreography.FloatText(
                builder, "-1", BadgeCentre + new Vector2(0.1f, -0.45f), 0.6f, InfoDemoPaint.BADGE_RED, 0.45f, tick, 0.9f);

            // 2. The next placement completes row 7, which clears the timer before it runs out.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, SECOND_LAND_ROW, SECOND_LAND_COLUMN,
                SECOND_PLACE_START);
            InfoDemoChoreography.ClearRow(builder, TIMER_ROW, CLEAR_START);

            float timerGone = TimerGoneTime();
            InfoDemoSpecialCellChoreography.GoWithCell(builder, timerIcon, timerHalo, timerGone);
            InfoDemoHudChoreography.HideBadge(builder, badge, timerGone, InfoDemoSpecialCellChoreography.EXIT_DURATION);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_JUST_IN_TIME, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 5.8f),
                1f, InfoDemoPaint.SUCCESS, LABEL_TIME, 1.2f);

            return builder.Build();
        }
    }
}
