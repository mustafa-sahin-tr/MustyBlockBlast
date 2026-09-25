using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.BoardWipeCount"/> objective card's demo (issue #453, mockup artboard
    /// "Tahta temizleme"), authored from the real rule (<see cref="ObjectiveProgress"/>,
    /// <c>ObjectivePlacementContext.BoardEmptyAfterPlacement</c>): a placement whose clears leave the whole
    /// board empty counts once. It is not "fill all 64 cells" — the board only has to end the move empty.
    /// The objective has no parameter, so there is one demo; its chip is illustrative (0/1 → 1/1).
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-6 ........
    /// row7   ppggb.uu     the board's last blocks; (7,5) is row 7's gap
    /// </code>
    /// The strip holds the progress chip ("EMPTY BOARD", 0/1) on the left and three tray pieces on the right:
    /// an L corner, the single (magenta) that will be played, a 1x2.
    /// 0.45 s the single lifts and glides to (7,5) · ~1.15 s it lands, completing row 7 · 1.3 s row 7 clears ·
    /// 1.9 s a solid green ring frames the now-empty board · 2.0 s "Board cleared!" · 2.1 s the chip ticks
    /// 0/1 → 1/1 and the green check pops in · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class BoardWipeInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int LAND_ROW = 7;
        internal const int LAND_COLUMN = 5;

        internal const float PLACE_START = 0.45f;
        internal const float CLEAR_START = 1.3f;
        internal const float OUTLINE_START = 1.9f;
        internal const float OUTLINE_END = 3.9f;
        internal const float LABEL_START = 2.0f;
        internal const float CHIP_ADVANCE_TIME = 2.1f;

        /// <summary>The ring round the empty board: eight pitches less one gap, plus a little padding, in
        /// board units.</summary>
        private const float RING_SIZE = InfoDemoLayout.BOARD_SIZE - (InfoDemoLayout.MOCK_GAP / InfoDemoLayout.MOCK_PITCH) + 0.14f;
        private const float RING_CORNER = 0.35f;
        private const float RING_STROKE = 0.09f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "ppggb.uu",
        };

        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        internal static InfoDemoTimeline Build() => Build(out _);

        /// <summary>As <see cref="Build()"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            chip = InfoDemoChoreography.ProgressChip(builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_BOARD_WIPE, null, 0, 1);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), scale);
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            // 1. The single fills row 7's gap — the board's last line.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);

            // 2. Nothing is left anywhere: the whole board is outlined in green.
            float boardCentre = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            // A solid ring hugging the board rather than a pulsing dashed outline: a pulse past 8x8 would be
            // clipped by the stage edge.
            int ring = builder.AddRing(
                new Vector2(boardCentre, boardCentre), new Vector2(RING_SIZE, RING_SIZE), RING_CORNER, RING_STROKE,
                InfoDemoPaint.SUCCESS);
            builder.Fade(ring, OUTLINE_START, 0.25f, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Scale(ring, OUTLINE_START, 0.4f, 1.03f, 1f, InfoDemoEasing.EaseOutBack);
            builder.Fade(ring, OUTLINE_END, 0.3f, 1f, 0f, InfoDemoEasing.EaseInCubic);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_BOARD_CLEARED, new Vector2(boardCentre, 4.2f), 1.0f,
                InfoDemoPaint.INK, LABEL_START, 1.3f);

            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);

            return builder.Build();
        }
    }
}
