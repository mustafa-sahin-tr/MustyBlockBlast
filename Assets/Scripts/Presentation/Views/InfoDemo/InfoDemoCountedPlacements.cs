using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The shared body of the two "every placement of <i>this</i> counts" objective demos (issue #454 —
    /// <see cref="PieceFamilyCountInfoDemo"/> and <see cref="PieceIdCountInfoDemo"/>): a chip showing the
    /// counted piece in miniature on the left of the strip, a tray of a counted piece, a piece that does not
    /// count and a second counted piece, and two placements that each tick the chip once — no clear needed.
    /// The two demos differ only in which pieces they deal, where those land and the closing label.
    /// <para>
    /// Timing (5.0 s loop): 0.45 s the first counted piece lifts from slot 0 · ~1.25 s the chip ticks ·
    /// 1.75 s the second lifts from slot 2 · ~2.55 s the chip ticks again · 2.8 s the label · hold, fade out
    /// from 4.6 s, loop. The chip counts from <c>target - 2</c> (from 0 when that is under 2) to the target.
    /// </para>
    /// </summary>
    internal static class InfoDemoCountedPlacements
    {
        internal const float LOOP_DURATION = 5.0f;

        /// <summary>How many counted pieces are placed, each counting one.</summary>
        internal const int PLACEMENT_COUNT = 2;

        /// <summary>The largest target the chip can print without crowding it.</summary>
        internal const int MAX_TARGET = 999;

        internal const float CHIP_DELAY = 0.1f;
        internal const float LABEL_START = 2.8f;

        /// <summary>The tray slot the piece that does not count rests in, between the two counted ones.</summary>
        internal const int OTHER_SLOT = 1;

        /// <summary>When each counted piece lifts off the tray, in play order.</summary>
        internal static readonly float[] PlaceStarts = { 0.45f, 1.75f };

        /// <summary>The tray slot each counted piece rests in, in play order.</summary>
        internal static readonly int[] TraySlots = { 0, 2 };

        /// <summary>True when a chip can honestly count to <paramref name="target"/>.</summary>
        internal static bool SupportsTarget(int target) => target >= 1 && target <= MAX_TARGET;

        /// <summary>The chip's value at loop start for <paramref name="target"/>.</summary>
        internal static int ChipStart(int target) => Mathf.Max(0, target - PLACEMENT_COUNT);

        /// <summary>When counted piece <paramref name="placementIndex"/> lands.</summary>
        internal static float LandTime(int placementIndex)
            => PlaceStarts[placementIndex] + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION;

        /// <summary>
        /// Builds the demo on <paramref name="rows"/>: counted pieces <paramref name="shapes"/> (demo shapes, in
        /// play order) land with their footprint top-left on <paramref name="landCells"/> (x = column, y = row),
        /// <paramref name="otherShape"/> stays in the tray, the chip shows <paramref name="chipGlyph"/> and counts
        /// to <paramref name="target"/>, and <paramref name="labelKey"/> floats up at the end.
        /// </summary>
        internal static InfoDemoTimeline Build(
            string[] rows,
            Vector2Int[] chipGlyph,
            Vector2Int[][] shapes,
            Vector2Int[] landCells,
            Vector2Int[] otherShape,
            string labelKey,
            float labelRow,
            int target,
            out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, rows);

            // Strip: the goal chip — the counted piece in miniature — on the left, the tray to its right.
            chip = InfoDemoChoreography.ProgressChip(builder, chipGlyph, InfoDemoPaint.BLOCK_4, ChipStart(target), target);

            int[] pieces = new int[PLACEMENT_COUNT];
            Vector2[] slots = new Vector2[PLACEMENT_COUNT];
            float[] scales = new float[PLACEMENT_COUNT];
            for (int placementIndex = 0; placementIndex < PLACEMENT_COUNT; placementIndex++)
            {
                slots[placementIndex] = InfoDemoLayout.TraySlot(TraySlots[placementIndex], InfoDemoTrayLayout.ChipLeft);
                scales[placementIndex] = PieceIdLineClearInfoDemo.TrayScale(shapes[placementIndex]);
                pieces[placementIndex] = builder.AddPiece(
                    shapes[placementIndex], InfoDemoPaint.BLOCK_4, slots[placementIndex], scales[placementIndex]);
            }

            builder.AddPiece(
                otherShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(OTHER_SLOT, InfoDemoTrayLayout.ChipLeft),
                PieceIdLineClearInfoDemo.TrayScale(otherShape));

            // Two counted pieces land; each one counts on landing, with no clear needed.
            for (int placementIndex = 0; placementIndex < PLACEMENT_COUNT; placementIndex++)
            {
                Vector2Int land = landCells[placementIndex];
                InfoDemoChoreography.PlacePiece(
                    builder, pieces[placementIndex], shapes[placementIndex], InfoDemoPaint.BLOCK_4, slots[placementIndex],
                    land.y, land.x, PlaceStarts[placementIndex], scales[placementIndex]);
                InfoDemoChoreography.AdvanceChip(builder, chip, LandTime(placementIndex) + CHIP_DELAY);
            }

            InfoDemoChoreography.FloatLabel(
                builder, labelKey, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, labelRow),
                1.0f, InfoDemoPaint.INK, LABEL_START, 1.4f);

            return builder.Build();
        }
    }
}
