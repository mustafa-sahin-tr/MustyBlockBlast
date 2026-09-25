using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.ScoreInMoves"/> objective card's demo (issue #465, mockup artboard "Bilgi
    /// kartı"), authored from the real rules: progress mirrors the run score exactly as
    /// <see cref="ScoreInRunInfoDemo"/>'s does — the demo plays that one move, a 1x3 clearing three rows for
    /// <see cref="ScoreInRunInfoDemo.Gain"/> points under the real <see cref="ScoreRules"/> — and the placement
    /// spends one move of the budget, shown as a round badge on the chip's corner counting
    /// <see cref="MOVES_LEFT_AT_START"/> down by one as the piece lands.
    /// <para>
    /// Choreography (5.0 s loop; board as <see cref="ScoreInRunInfoDemo"/>): the strip holds the chip — the
    /// objective's own icon over "2820/3000", the moves badge "4" on its corner — and the same tray.
    /// 0.45 s the 1x3 lifts and glides into column 4 · ~1.15 s it lands and the badge ticks to "3" · 1.3 s rows
    /// 5-7 clear · 1.55 s "+183" · 1.8 s the chip jumps to "3000/3000" with the check · 2.1 s "Target score!" ·
    /// hold, fade out, loop.
    /// </para>
    /// </summary>
    internal static class ScoreInMovesInfoDemo
    {
        /// <summary>Moves the badge shows before the move — a small budget, so the one move visibly matters.
        /// Not the level's own limit: the card text states that, and the demo only shows what a move costs.</summary>
        internal const int MOVES_LEFT_AT_START = 4;

        private const float MOCK_BADGE_DIAMETER = 20f;
        private const float MOCK_BADGE_RIM = 2.5f;
        private const float MOCK_BADGE_FONT = 12f;
        private const float MOCK_BADGE_INSET = 4f;

        internal static InfoDemoTimeline Build(int target) => Build(target, out _, out _);

        /// <summary>As <see cref="Build(int)"/>, handing back the chip and the moves badge so a test can read them.</summary>
        internal static InfoDemoTimeline Build(int target, out InfoDemoProgressChip chip, out InfoDemoCountBadge movesBadge)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(ScoreInRunInfoDemo.LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, ScoreInRunInfoDemo.Rows);
            chip = InfoDemoChoreography.ReadingChip(
                builder, InfoDemoSprite.ObjectiveIcon, (int)ObjectiveType.ScoreInMoves, InfoDemoPaint.WHITE,
                null, null, new[] { ScoreInRunInfoDemo.ChipStart(target), ScoreInRunInfoDemo.ChipEnd(target) }, target);

            // The budget, hung on the chip's top-right corner over everything else in the strip.
            Vector2 chipSize = InfoDemoChoreography.ChipSize;
            float inset = InfoDemoLayout.FromMockLength(MOCK_BADGE_INSET);
            movesBadge = InfoDemoHudChoreography.CountBadge(
                builder,
                InfoDemoLayout.ChipCentre + new Vector2((chipSize.x * 0.5f) - inset, -((chipSize.y * 0.5f) - inset)),
                MOVES_LEFT_AT_START,
                InfoDemoPaint.ACCENT,
                InfoDemoLayout.FromMockLength(MOCK_BADGE_DIAMETER),
                InfoDemoLayout.FromMockLength(MOCK_BADGE_RIM),
                InfoDemoLayout.FromMockLength(MOCK_BADGE_FONT),
                true);

            ScoreInRunInfoDemo.PlayMove(builder, chip, target);

            // The placement is the move: the badge ticks down the moment the piece lands.
            InfoDemoHudChoreography.TickBadge(builder, movesBadge, ScoreInRunInfoDemo.LandTime);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_TARGET_SCORE, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 3.2f),
                1.0f, InfoDemoPaint.INK, ScoreInRunInfoDemo.LABEL_START, 1.4f);

            return builder.Build();
        }
    }
}
