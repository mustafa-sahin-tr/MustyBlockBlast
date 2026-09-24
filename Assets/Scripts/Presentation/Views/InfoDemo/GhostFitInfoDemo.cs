using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.GhostFit"/> info demo (issue #449, mockup artboard "Hayalet Yerleşim"),
    /// authored from the real rule (<c>PowerUpSystem.TryApplyGhostFit</c>, <c>GhostFitSearch</c>,
    /// <c>GhostFitView</c>): targetless, applied on the tap — an exact search over every dock piece and
    /// every anchor picks the move clearing the most lines at once, shown as a breathing silhouette on
    /// the cells it would fill plus a pulse on its dock piece. The silhouette is drawn as the real one
    /// is (<c>BoardView.ShowGhostFitSilhouette</c>): the theme's block colour 2 as a translucent fill in
    /// a ring of the same colour. It changes nothing; the player still plays the piece.
    /// <para>
    /// Choreography (5.4 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-3 ........
    /// row4 ......u.
    /// row5 .....uu.
    /// row6 ggbbu..p     (6,5),(6,6) empty
    /// row7 uuppg..b     (7,5),(7,6) empty
    /// </code>
    /// The strip holds the violet Ghost Fit button (count 1) and a vertical 1x3, a 2x2 and a single.
    /// 0.5 s the button is pressed, count 1 → 0 · 0.85–2.6 s the silhouette breathes on
    /// (6,5),(6,6),(7,5),(7,6) and the 2x2 pulses in its slot in step · 1.0 s "Best move" · 2.7 s the
    /// silhouette is gone and the 2x2 lifts and lands there · 3.5 s rows 6 and 7 clear · hold, fade out
    /// from 5.0 s, loop. <c>InfoDemoPowerUpTests</c> checks the silhouette against the real search.
    /// </para>
    /// </summary>
    internal static class GhostFitInfoDemo
    {
        internal const float LOOP_DURATION = 5.4f;

        internal const float PRESS_TIME = 0.5f;
        internal const float HINT_START = 0.85f;
        internal const float HINT_END = 2.6f;
        internal const float LABEL_START = 1.0f;
        internal const float PLACE_START = 2.7f;
        internal const float CLEAR_START = 3.5f;

        /// <summary>The suggested move: the 2x2 in dock slot 1, top-left cell on (6,5).</summary>
        internal const int SUGGESTED_SLOT = 1;
        internal const int LAND_ROW = 6;
        internal const int LAND_COLUMN = 5;

        /// <summary>One breath of the silhouette and the dock pulse. The real hint breathes at 0.8 Hz; a
        /// little quicker here so the 1.75 s window shows two full breaths.</summary>
        internal const float PULSE_PERIOD = 0.875f;

        /// <summary>The real silhouette's fill opacity at full breath (<c>BoardView.GHOST_FILL_ALPHA</c>) and
        /// the bottom of its breath (<c>GhostFitView</c>'s minimum pulse).</summary>
        private const float GHOST_FILL_ALPHA = 0.3f;
        private const float MINIMUM_BREATH = 0.25f;
        private const float HINT_PIECE_PEAK_FRACTION = 1.15f;

        private const int BUTTON_COUNT = 1;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "........",
            "......u.",
            ".....uu.",
            "ggbbu..p",
            "uuppg..b",
        };

        /// <summary>The dock, slot by slot, as (column, row) offsets with row 0 at the top.</summary>
        internal static readonly Vector2Int[][] DockShapes =
        {
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
            new[] { new Vector2Int(0, 0) },
        };

        private static readonly int[] DockPaints = { InfoDemoPaint.BLOCK_5, InfoDemoPaint.BLOCK_1, InfoDemoPaint.BLOCK_2 };

        /// <summary>The cells (x = column, y = row) the silhouette covers — exactly where the suggested
        /// piece lands.</summary>
        internal static Vector2Int[] SilhouetteCells()
        {
            Vector2Int[] shape = DockShapes[SUGGESTED_SLOT];
            Vector2Int[] cells = new Vector2Int[shape.Length];
            for (int cellIndex = 0; cellIndex < shape.Length; cellIndex++)
            {
                cells[cellIndex] = new Vector2Int(LAND_COLUMN + shape[cellIndex].x, LAND_ROW + shape[cellIndex].y);
            }

            return cells;
        }

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.GhostFit, InfoDemoPaint.PLATE_GHOST, BUTTON_COUNT);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            int[] pieces = new int[DockShapes.Length];
            for (int slotIndex = 0; slotIndex < DockShapes.Length; slotIndex++)
            {
                pieces[slotIndex] = builder.AddPiece(
                    DockShapes[slotIndex], DockPaints[slotIndex], InfoDemoLayout.TraySlot(slotIndex, InfoDemoTrayLayout.ChipLeft), scale);
            }

            // 1. Targetless: the tap spends the charge and shows the best move.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, false, PRESS_TIME);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, PRESS_TIME);

            // 2. The silhouette breathes on the landing cells, and the suggested dock piece in step.
            Vector2Int[] silhouette = SilhouetteCells();
            for (int cellIndex = 0; cellIndex < silhouette.Length; cellIndex++)
            {
                AddSilhouetteCell(builder, InfoDemoLayout.Cell(silhouette[cellIndex].y, silhouette[cellIndex].x));
            }

            Vector2 suggestedSlot = InfoDemoLayout.TraySlot(SUGGESTED_SLOT, InfoDemoTrayLayout.ChipLeft);
            InfoDemoTrayChoreography.PulsePiece(
                builder, pieces[SUGGESTED_SLOT], scale, scale * HINT_PIECE_PEAK_FRACTION, HINT_START, HINT_END, PULSE_PERIOD);
            InfoDemoTrayChoreography.HighlightRing(
                builder, suggestedSlot, new Vector2(1.3f, 1.3f), InfoDemoLayout.FromMockLength(2f), InfoDemoPaint.GHOST_FIT,
                HINT_START, HINT_END, 0);

            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_BEST_MOVE,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, LAND_ROW - 1.6f),
                0.8f,
                InfoDemoPaint.PLATE_GHOST,
                LABEL_START,
                1.5f);

            // 3. The player takes the hint: the 2x2 clears rows 6 and 7 at once.
            InfoDemoChoreography.PlacePiece(
                builder, pieces[SUGGESTED_SLOT], DockShapes[SUGGESTED_SLOT], DockPaints[SUGGESTED_SLOT], suggestedSlot,
                LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW + 1, CLEAR_START);

            return builder.Build();
        }

        /// <summary>One silhouette cell, drawn like the real one: a translucent ghost-colour fill inside a
        /// ring of the same colour, both breathing from <see cref="HINT_START"/> to <see cref="HINT_END"/>.</summary>
        private static void AddSilhouetteCell(InfoDemoTimelineBuilder builder, Vector2 centre)
        {
            const float cellSize = 0.9f;
            const float corner = 0.2f;

            int fill = builder.AddPanel(centre, new Vector2(cellSize, cellSize), corner, InfoDemoPaint.GHOST_FIT, 0f);
            int ring = builder.AddRing(
                centre, new Vector2(cellSize, cellSize), corner, InfoDemoLayout.FromMockLength(2f), InfoDemoPaint.GHOST_FIT);

            builder.Fade(fill, HINT_START, 0.15f, 0f, GHOST_FILL_ALPHA * MINIMUM_BREATH, InfoDemoEasing.EaseOutCubic);
            builder.Fade(ring, HINT_START, 0.15f, 0f, MINIMUM_BREATH, InfoDemoEasing.EaseOutCubic);
            Breathe(builder, fill, GHOST_FILL_ALPHA, HINT_START + 0.15f, HINT_END);
            Breathe(builder, ring, 1f, HINT_START + 0.15f, HINT_END);
            builder.Fade(fill, HINT_END, 0.1f, GHOST_FILL_ALPHA * MINIMUM_BREATH, 0f);
            builder.Fade(ring, HINT_END, 0.1f, MINIMUM_BREATH, 0f);
        }

        /// <summary>Alpha breathes from <see cref="MINIMUM_BREATH"/> of <paramref name="peakAlpha"/> up to
        /// it and back, once per <see cref="PULSE_PERIOD"/>.</summary>
        private static void Breathe(InfoDemoTimelineBuilder builder, int elementId, float peakAlpha, float startTime, float endTime)
        {
            for (float pulseStart = startTime; pulseStart + (PULSE_PERIOD * 0.5f) <= endTime; pulseStart += PULSE_PERIOD)
            {
                float pulseDuration = Mathf.Min(PULSE_PERIOD, endTime - pulseStart);
                builder.Fade(elementId, pulseStart, pulseDuration, peakAlpha * MINIMUM_BREATH, peakAlpha, InfoDemoEasing.Pulse);
            }
        }
    }
}
