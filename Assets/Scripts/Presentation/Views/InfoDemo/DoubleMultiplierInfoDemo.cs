using System.Globalization;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.DoubleMultiplier"/> info demo (issue #449, mockup artboard "2x Çarpan"),
    /// authored from the real rule (<c>PowerUpSystem.TryApplyDoubleMultiplier</c>,
    /// <c>DoubleMultiplierSystem</c>): targetless, applied on the tap; it opens a
    /// <see cref="DoubleMultiplierModel.WINDOW_SECONDS"/>-second window in which every score gain's
    /// finished total is doubled.
    /// <para>
    /// Choreography (5.2 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-3 ........
    /// row4 ......g.
    /// row5 ..u...g.
    /// row6 bb..pgg.
    /// row7 ggb.ppuu     (7,3) empty
    /// </code>
    /// The strip holds the orange button (count 1) and an L corner, a single and a 1x2.
    /// 0.45 s the button is pressed, count 1 → 0 · 0.65 s an orange "2x 15s" pill pops in at the board's
    /// top-right with a thin countdown bar under it that drains linearly for the rest of the loop ·
    /// 1.2 s the single lifts and lands on (7,3) · 2.0 s row 7 clears · 2.05 s "+11" (one placed cell,
    /// plus 10 for one line — <c>ScoreRules</c> with no streak running) · 2.8 s a bigger orange
    /// "2x → +22" · hold, fade out from 4.8 s, loop.
    /// </para>
    /// </summary>
    internal static class DoubleMultiplierInfoDemo
    {
        internal const float LOOP_DURATION = 5.2f;

        internal const float PRESS_TIME = 0.45f;
        internal const float PILL_TIME = 0.65f;
        internal const float DRAIN_START = 0.75f;
        internal const float PLACE_START = 1.2f;
        internal const float CLEAR_START = 2.0f;
        internal const float BASE_SCORE_TIME = 2.05f;
        internal const float DOUBLED_SCORE_TIME = 2.8f;

        internal const int LAND_ROW = 7;
        internal const int LAND_COLUMN = 3;

        /// <summary>The placement's score: one placed cell (1) plus one line cleared with no streak running
        /// (10 x 1 x comboMultiplier(1) = 10) — docs/game-design.md "Scoring".</summary>
        internal const int BASE_SCORE = 11;

        /// <summary><see cref="BASE_SCORE"/> inside the window: the finished total, doubled.</summary>
        internal const int DOUBLED_SCORE = BASE_SCORE * 2;

        private const int BUTTON_COUNT = 1;

        /// <summary>The "2x" pill at the board's top-right (board units): centre, size, icon and text.</summary>
        private static readonly Vector2 PillCentre = new Vector2(5.9f, 0.05f);
        private static readonly Vector2 PillSize = new Vector2(2.5f, 0.8f);
        private const float PILL_ICON_SIZE = 0.62f;
        private const float PILL_ICON_OFFSET_X = -0.72f;
        private const float PILL_TEXT_OFFSET_X = 0.32f;
        private const float PILL_FONT = 0.44f;
        private const float BAR_GAP = 0.2f;
        private const float BAR_THICKNESS = 0.12f;
        private const float BAR_TRACK_ALPHA = 0.18f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "........",
            "......g.",
            "..u...g.",
            "bb..pgg.",
            "ggb.ppuu",
        };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.DoubleMultiplier, InfoDemoPaint.PLATE_DOUBLE, BUTTON_COUNT);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            // 1. Targetless: the tap spends the charge and opens the window.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, false, PRESS_TIME);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, PRESS_TIME);

            // 2. The window: an orange "2x 15s" pill, and a bar draining over the rest of the loop.
            AddWindowPill(builder);

            // 3. A placement inside the window: row 7 clears...
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);

            // 4. ...scoring its usual total, then that total doubled.
            float centreX = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            InfoDemoChoreography.FloatText(
                builder, "+" + BASE_SCORE.ToString(CultureInfo.InvariantCulture), new Vector2(centreX, LAND_ROW - 0.4f),
                0.7f, InfoDemoPaint.INK, 0.6f, BASE_SCORE_TIME, 0.9f);
            InfoDemoChoreography.FloatText(
                builder, "2× → +" + DOUBLED_SCORE.ToString(CultureInfo.InvariantCulture),
                new Vector2(centreX, LAND_ROW - 1.1f), 0.9f, InfoDemoPaint.PLATE_DOUBLE, 0.85f, DOUBLED_SCORE_TIME, 1.6f);

            return builder.Build();
        }

        private static void AddWindowPill(InfoDemoTimelineBuilder builder)
        {
            // Never popped out: the pill stays up until the loop's own fade.
            float neverHidden = LOOP_DURATION + 1f;

            int shadow = builder.AddPanel(
                PillCentre + new Vector2(0f, InfoDemoLayout.FromMockLength(2f)), PillSize, PillSize.y * 0.5f, InfoDemoPaint.INK, 0f);
            int pill = builder.AddPanel(PillCentre, PillSize, PillSize.y * 0.5f, InfoDemoPaint.PLATE_DOUBLE, 0f);
            int icon = builder.AddIcon(
                InfoDemoSprite.PowerUpIcon, (int)PowerUpKind.DoubleMultiplier, PillCentre + new Vector2(PILL_ICON_OFFSET_X, 0f),
                PILL_ICON_SIZE, 0f);
            int seconds = builder.AddLabel(
                LocalizationKeys.FORMAT_SECONDS, PillCentre + new Vector2(PILL_TEXT_OFFSET_X, 0f), PillSize.x * 0.55f, PILL_FONT,
                InfoDemoPaint.WHITE, 0f, DoubleMultiplierModel.WINDOW_SECONDS.ToString("0", CultureInfo.InvariantCulture));

            InfoDemoHudChoreography.PopIn(builder, shadow, 0.14f, PILL_TIME, neverHidden);
            InfoDemoHudChoreography.PopIn(builder, pill, 1f, PILL_TIME, neverHidden);
            InfoDemoHudChoreography.PopIn(builder, icon, 1f, PILL_TIME, neverHidden);
            InfoDemoHudChoreography.PopIn(builder, seconds, 1f, PILL_TIME, neverHidden);

            Vector2 barCentre = PillCentre + new Vector2(0f, (PillSize.y * 0.5f) + BAR_GAP);
            InfoDemoHudChoreography.CountdownBar(
                builder, barCentre, PillSize.x * 0.9f, BAR_THICKNESS, InfoDemoPaint.PLATE_DOUBLE, InfoDemoPaint.INK,
                BAR_TRACK_ALPHA, PILL_TIME, DRAIN_START, LOOP_DURATION);
        }
    }
}
