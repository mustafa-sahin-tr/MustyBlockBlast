using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.EarlyScoreRush"/> objective card's demo (issue #454, mockup artboard "Erken
    /// puan"), authored from the real rule (<see cref="ObjectiveProgress"/>): progress mirrors the run score,
    /// clamped to the target, but only while the run is still inside its first
    /// <see cref="ObjectiveDefinition.WindowSeconds"/> — past that deadline it freezes rather than completing
    /// late. The demo plays <see cref="ScoreInRunInfoDemo"/>'s move (a 1x3 clearing three rows for
    /// <see cref="ScoreInRunInfoDemo.Gain"/> points under the real <see cref="ScoreRules"/>) against the
    /// deadline.
    /// <para>
    /// The loop starts at the run's start and runs in real time: a bar along the chip's foot drains from full
    /// at one second per second, so a window longer than the loop only ever shows its start (15 s: a little
    /// over a third gone by the loop's end), and the move's clear at 1.3 s lands well inside any window the
    /// demo supports (<see cref="MIN_WINDOW_SECONDS"/> and up).
    /// </para>
    /// <para>
    /// Choreography (5.6 s loop; board as <see cref="ScoreInRunInfoDemo"/>): the strip holds the chip — the
    /// HUD's clock and "15 SEC" over "2820/3000", the deadline bar at its foot — and the same tray.
    /// 0 s the bar starts draining · 0.45 s the 1x3 drops into column 4 · 1.3 s rows 5-7 clear · 1.55 s "+183"
    /// · 1.8 s the chip jumps to "3000/3000" with the check, the bar still well short of empty · 2.1 s "Just in
    /// time!" · hold, fade out from 5.2 s, loop.
    /// </para>
    /// </summary>
    internal static class EarlyScoreRushInfoDemo
    {
        internal const float LOOP_DURATION = 5.6f;

        /// <summary>The shortest deadline (whole seconds, rounded as the card text is) the demo's score lands
        /// inside: the chip reaches the target at <see cref="ScoreInRunInfoDemo.CHIP_ADVANCE_TIME"/>.</summary>
        internal const int MIN_WINDOW_SECONDS = 2;

        /// <summary>The longest deadline the caption is drawn for.</summary>
        internal const int MAX_WINDOW_SECONDS = 999;

        /// <summary>Mockup-unit geometry of the deadline bar along the chip's foot — as the rolling window's.</summary>
        private const float MOCK_BAR_OFFSET_Y = 19.5f;
        private const float MOCK_BAR_SIDE_INSET = 6f;
        private const float MOCK_BAR_THICKNESS = 3f;
        private const float BAR_TRACK_ALPHA = 0.15f;

        /// <summary>True when the demo can show a <paramref name="windowSeconds"/> deadline with a chip counting
        /// to <paramref name="target"/>.</summary>
        internal static bool Supports(float windowSeconds, int target)
        {
            int seconds = WholeSeconds(windowSeconds);
            return seconds >= MIN_WINDOW_SECONDS && seconds <= MAX_WINDOW_SECONDS && ScoreInRunInfoDemo.Supports(target);
        }

        /// <summary><paramref name="windowSeconds"/> as the whole seconds the card text shows.</summary>
        internal static int WholeSeconds(float windowSeconds) => RollingLineClearWindowInfoDemo.WholeSeconds(windowSeconds);

        /// <summary>The fraction of the deadline bar still left when the loop ends for a
        /// <paramref name="seconds"/>-second window — 0 when the whole window fits in the loop.</summary>
        internal static float BarLeftAtLoopEnd(int seconds) => Mathf.Max(0f, 1f - (LOOP_DURATION / seconds));

        internal static InfoDemoTimeline Build(int seconds, int target) => Build(seconds, target, out _);

        /// <summary>As <see cref="Build(int, int)"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(int seconds, int target, out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, ScoreInRunInfoDemo.Rows);

            chip = InfoDemoChoreography.ReadingChip(
                builder, InfoDemoSprite.HudClock, 0, InfoDemoPaint.INK,
                LocalizationKeys.INFO_POPUP_DEMO_CHIP_WINDOW, seconds.ToString(CultureInfo.InvariantCulture),
                new[] { ScoreInRunInfoDemo.ChipStart(target), ScoreInRunInfoDemo.ChipEnd(target) }, target);

            // The deadline, running from the run's start at one second per second.
            Vector2 chipSize = InfoDemoChoreography.ChipSize;
            float drainEnd = Mathf.Min(seconds, LOOP_DURATION);
            InfoDemoHudChoreography.CountdownBar(
                builder,
                InfoDemoLayout.ChipCentre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_BAR_OFFSET_Y)),
                chipSize.x - InfoDemoLayout.FromMockLength(MOCK_BAR_SIDE_INSET * 2f),
                InfoDemoLayout.FromMockLength(MOCK_BAR_THICKNESS),
                InfoDemoPaint.ACCENT,
                InfoDemoPaint.INK,
                BAR_TRACK_ALPHA,
                0f,
                0f,
                drainEnd,
                BarLeftAtLoopEnd(seconds));

            ScoreInRunInfoDemo.PlayMove(builder, chip, target);
            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_JUST_IN_TIME, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 3.2f),
                1.0f, InfoDemoPaint.INK, ScoreInRunInfoDemo.LABEL_START, 1.4f);

            return builder.Build();
        }
    }
}
