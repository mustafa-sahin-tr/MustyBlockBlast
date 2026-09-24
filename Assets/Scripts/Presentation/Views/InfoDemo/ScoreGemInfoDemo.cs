using System.Globalization;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.ScoreGem"/> info demo (issue #450), authored from the real rule
    /// (<see cref="ScoreRules.ScoreGemMultiplied"/>, applied by <c>ScoreSystem</c>): a gem destroys nothing
    /// — the event that destroys it has its whole finished score multiplied by
    /// <see cref="ScoreRules.SCORE_GEM_FACTOR"/>. It is earned by every second placement that clears a
    /// row and a column at once (<c>ScoreGemProgressModel</c>), which the card's body says.
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-2 ........
    /// row3 ...g....
    /// row4 ..uu....
    /// row5 .g...b..
    /// row6 g...bb..
    /// row7 ggpbuu.p     the gem rides on (7,2); (7,6) is row 7's one gap
    /// </code>
    /// The tray holds an L corner, the single that will be played and a 1x2.
    /// 0.4 s the single lifts and lands on (7,6) · 1.2 s row 7 clears left to right, the gem going with
    /// it · ~1.45 s a small burst in the gem's green · 1.5 s "+11" (one placed cell, plus 10 for one line
    /// with no streak running) · 2.2 s a bigger "×3 → +33" · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class ScoreGemInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const float PLACE_START = 0.4f;
        internal const float CLEAR_START = 1.2f;
        internal const float BASE_SCORE_TIME = 1.5f;
        internal const float MULTIPLIED_SCORE_TIME = 2.2f;

        internal const int GEM_ROW = 7;
        internal const int GEM_COLUMN = 2;

        /// <summary>Where the single lands: row 7's one gap.</summary>
        internal const int LAND_ROW = GEM_ROW;
        internal const int LAND_COLUMN = 6;

        /// <summary>The placement's usual score: one placed cell (1) plus one line cleared with no streak
        /// running (10 x 1 x comboMultiplier(1) = 10) — docs/game-design.md "Scoring".</summary>
        internal const int BASE_SCORE = 11;

        /// <summary><see cref="BASE_SCORE"/> with the gem destroyed in the same event.</summary>
        internal const int MULTIPLIED_SCORE = BASE_SCORE * ScoreRules.SCORE_GEM_FACTOR;

        private const float BURST_DELAY = 0.08f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "...g....",
            "..uu....",
            ".g...b..",
            "g...bb..",
            "ggpbuu.p",
        };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>When the gem starts to go with row 7's clear.</summary>
        internal static float GemGoneTime() => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, GEM_COLUMN);

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_1, InfoDemoLayout.TraySlot(0), scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_5, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_3, InfoDemoLayout.TraySlot(2), scale);

            int gemIcon = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.ScoreGem, GEM_ROW, GEM_COLUMN, out int gemHalo);
            int gemPaint = InfoDemoPaint.SpecialCellGlow(SpecialCellKind.ScoreGem);

            // 1. The single completes row 7, which clears, taking the gem with it.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_5, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, GEM_ROW, CLEAR_START);

            float gemGone = GemGoneTime();
            InfoDemoSpecialCellChoreography.GoWithCell(builder, gemIcon, gemHalo, gemGone);
            InfoDemoChoreography.Burst(
                builder, InfoDemoLayout.Cell(GEM_ROW, GEM_COLUMN), gemPaint, InfoDemoPaint.NONE, 1f, 2.6f,
                gemGone + BURST_DELAY, 0.45f);

            // 2. It destroys nothing: the move's own score, then that score tripled.
            float centreX = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            InfoDemoChoreography.FloatText(
                builder, "+" + BASE_SCORE.ToString(CultureInfo.InvariantCulture), new Vector2(centreX, GEM_ROW - 0.4f),
                0.7f, InfoDemoPaint.INK, 0.6f, BASE_SCORE_TIME, 0.9f);
            InfoDemoChoreography.FloatText(
                builder,
                "×" + ScoreRules.SCORE_GEM_FACTOR.ToString(CultureInfo.InvariantCulture) + " → +"
                    + MULTIPLIED_SCORE.ToString(CultureInfo.InvariantCulture),
                new Vector2(centreX, GEM_ROW - 1.1f), 0.9f, InfoDemoPaint.SUCCESS, 0.85f, MULTIPLIED_SCORE_TIME, 1.6f);

            return builder.Build();
        }
    }
}
