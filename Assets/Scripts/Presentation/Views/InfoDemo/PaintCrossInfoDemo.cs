using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.PaintCross"/> info demo (issue #448, mockup artboard "Boya Haçı"),
    /// authored from the real rule (<c>PowerUpSystem.TryApplyPaintCross</c> /
    /// <c>PowerUpPaintResolver.ResolvePaintCross</c>): arm it, tap a cell, pick a colour from the picker,
    /// and every <em>occupied</em> cell of that cell's row and column is repainted. Empty cells stay
    /// empty and nothing clears — not even a line the paint happens to make one colour.
    /// <para>
    /// Choreography (5.4 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 ....g...
    /// row2 ..b.u...
    /// row3 .gu.bgb.     tapped cell (3,4); row 3 and column 4 are the cross
    /// row4 ....g...
    /// row5 ..u.b.g.
    /// row6 ....u...
    /// row7 g.bbg.uu
    /// </code>
    /// The strip holds the pink button (count 3) on the left and an idle tray on the right.
    /// 0.6 s a finger taps the button, which arms · 1.35 s a finger taps (3,4) and the cross glows
    /// behind the board · 1.55 s the colour picker pops in above it, one swatch per block colour like
    /// the real picker · 2.1 s a finger taps the first (pink) swatch · 2.35 s the picker closes ·
    /// 2.4 s disarm · 2.45 s the eleven occupied cross cells turn pink, nearest to the tap first, 35 ms
    /// apart, and the count ticks 3 → 2 · 3.6 s "Nothing is cleared" floats up · hold, fade out from
    /// 5.0 s, loop.
    /// </para>
    /// </summary>
    internal static class PaintCrossInfoDemo
    {
        internal const float LOOP_DURATION = 5.4f;

        internal const int TARGET_ROW = 3;
        internal const int TARGET_COLUMN = 4;

        /// <summary>The colour the demo paints with — the picker's first swatch.</summary>
        internal const int PAINT = InfoDemoPaint.BLOCK_1;

        internal const float PRESS_TIME = 0.6f;
        internal const float DISARM_TIME = 2.4f;
        internal const float TAP_TIME = 1.35f;
        internal const float PICKER_OPEN_TIME = 1.55f;
        internal const float SWATCH_TAP_TIME = 2.1f;
        internal const float PICKER_CLOSE_TIME = 2.35f;
        internal const float RECOLOUR_START = 2.45f;
        internal const float RECOLOUR_STAGGER = 0.035f;
        internal const float LABEL_START = 3.6f;

        private const int BUTTON_COUNT = 3;
        private const float CROSS_BAND_ALPHA = 0.45f;
        private const float CROSS_BAND_END = 2.9f;

        /// <summary>One swatch per block colour, in colour-id order — the real picker offers the game's
        /// whole palette (<c>Board.COLOUR_COUNT</c>), not only the colours on the board.</summary>
        private static readonly int[] SwatchPaints =
        {
            InfoDemoPaint.BLOCK_1, InfoDemoPaint.BLOCK_2, InfoDemoPaint.BLOCK_3, InfoDemoPaint.BLOCK_4, InfoDemoPaint.BLOCK_5,
        };

        internal static readonly string[] Rows =
        {
            "........",
            "....g...",
            "..b.u...",
            ".gu.bgb.",
            "....g...",
            "..u.b.g.",
            "....u...",
            "g.bbg.uu",
        };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.PaintCross, InfoDemoPaint.PLATE_PAINT, BUTTON_COUNT);
            InfoDemoPowerUpChoreography.FillerTray(builder);

            // 1. Arm it, then tap the cell whose row and column will be painted; the cross glows.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, true, DISARM_TIME);
            InfoDemoChoreography.TapCell(builder, TAP_TIME, TARGET_ROW, TARGET_COLUMN);
            CrossBand(builder, true, TARGET_ROW);
            CrossBand(builder, false, TARGET_COLUMN);

            // 2. The picker pops in above the cell; the pink swatch is picked; the picker closes.
            InfoDemoColourPicker picker = InfoDemoPowerUpChoreography.ColourPicker(
                builder, TARGET_ROW, TARGET_COLUMN, SwatchPaints, PICKER_OPEN_TIME, PICKER_CLOSE_TIME);
            InfoDemoPowerUpChoreography.PickSwatch(
                builder, picker, picker.IndexOfPaint(PAINT), SWATCH_TAP_TIME, PICKER_CLOSE_TIME);

            // 3. Every occupied cell of the cross turns pink, from the tap outward. Nothing clears.
            Vector2Int[] crossCells = InfoDemoBoardPattern.OccupiedCross(Rows, TARGET_ROW, TARGET_COLUMN);
            InfoDemoChoreography.RecolourCells(
                builder, crossCells, InfoDemoLayout.Cell(TARGET_ROW, TARGET_COLUMN), PAINT, RECOLOUR_START, RECOLOUR_STAGGER);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, RECOLOUR_START);

            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_NOTHING_CLEARED,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, TARGET_ROW + 1.6f),
                1.1f,
                InfoDemoPaint.PLATE_PAINT,
                LABEL_START,
                1.2f);

            return builder.Build();
        }

        /// <summary>A soft highlight band behind one arm of the cross, from the tap until the paint lands.</summary>
        private static void CrossBand(InfoDemoTimelineBuilder builder, bool isRow, int lineIndex)
        {
            const float bandLength = InfoDemoLayout.BOARD_SIZE + 0.3f;
            const float bandThickness = 1.3f;
            float centre = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;

            int band = builder.AddBand(
                isRow ? new Vector2(centre, lineIndex) : new Vector2(lineIndex, centre),
                isRow ? new Vector2(bandLength, bandThickness) : new Vector2(bandThickness, bandLength),
                InfoDemoPaint.LINE_HIGHLIGHT);
            builder.Fade(band, TAP_TIME, 0.2f, 0f, CROSS_BAND_ALPHA, InfoDemoEasing.EaseOutCubic);
            builder.Fade(band, CROSS_BAND_END, 0.3f, CROSS_BAND_ALPHA, 0f, InfoDemoEasing.EaseInCubic);
        }
    }
}
