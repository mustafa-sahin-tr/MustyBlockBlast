using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.FourCornersCleared"/> objective card's demo (issue #453, mockup artboard
    /// "Dört köşe"), authored from the real rule (<c>ObjectivePlacementContext.AnyCornerCleared</c>): a
    /// placement counts once when a row or column it clears runs through ANY corner cell — row 0 or 7,
    /// column 0 or 7. One such clear already takes two corners; all four are never needed. The objective has
    /// no parameter, so there is one demo; its chip is illustrative (0/1 → 1/1).
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ppg.buug     row 0 is full except (0,3)
    /// row1 ..u.....
    /// row2 ...gg...
    /// row3 .b......
    /// row4 ....u...
    /// row5 ..gg....
    /// row6 g......b
    /// row7 gg....bb
    /// </code>
    /// The strip holds the progress chip ("CORNER", 0/1) on the left and three tray pieces on the right.
    /// From the start dashed orange markers sit on the four corner cells · 0.45 s the single lifts and glides to
    /// (0,3), and the markers fade · ~1.15 s it lands, completing row 0 · 1.3 s row 0 clears · 1.85 s the two top
    /// corners it took pulse green · 2.0 s "Corner cleared!" · 2.1 s the chip ticks 0/1 → 1/1 · hold, fade out
    /// from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class FourCornersClearedInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int LAND_ROW = 0;
        internal const int LAND_COLUMN = 3;

        internal const float PLACE_START = 0.45f;
        internal const float MARKER_FADE_START = 0.9f;
        internal const float CLEAR_START = 1.3f;
        internal const float CORNER_GLOW_START = 1.85f;
        internal const float CORNER_GLOW_END = 3.8f;
        internal const float LABEL_START = 2.0f;
        internal const float CHIP_ADVANCE_TIME = 2.1f;

        private const float MARKER_ALPHA = 0.95f;
        private const float MARKER_PADDING = 0.14f;

        internal static readonly string[] Rows =
        {
            "ppg.buug",
            "..u.....",
            "...gg...",
            ".b......",
            "....u...",
            "..gg....",
            "g......b",
            "gg....bb",
        };

        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>The four corner cells (x = column, y = row).</summary>
        internal static readonly Vector2Int[] Corners =
        {
            new Vector2Int(0, 0),
            new Vector2Int(InfoDemoLayout.BOARD_SIZE - 1, 0),
            new Vector2Int(0, InfoDemoLayout.BOARD_SIZE - 1),
            new Vector2Int(InfoDemoLayout.BOARD_SIZE - 1, InfoDemoLayout.BOARD_SIZE - 1),
        };

        internal static InfoDemoTimeline Build() => Build(out _);

        /// <summary>As <see cref="Build()"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            chip = InfoDemoChoreography.ProgressChip(builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_CORNER, null, 0, 1);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), scale);
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            // 0. "These four": a dashed orange marker on each corner cell, fading as the move starts.
            Vector2 markerSize = new Vector2(1f + MARKER_PADDING, 1f + MARKER_PADDING);
            for (int cornerIndex = 0; cornerIndex < Corners.Length; cornerIndex++)
            {
                Vector2Int corner = Corners[cornerIndex];
                int marker = builder.AddOutline(InfoDemoLayout.Cell(corner.y, corner.x), markerSize, InfoDemoPaint.BLAST, MARKER_ALPHA);
                builder.Fade(marker, MARKER_FADE_START, 0.3f, MARKER_ALPHA, 0f, InfoDemoEasing.EaseInCubic);
            }

            // 1. The single completes row 0 — a line through two corners.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);

            // 2. The corners that clear took glow green.
            InfoDemoChoreography.PulseOutline(
                builder, InfoDemoLayout.Cell(LAND_ROW, 0), 1, 1, InfoDemoPaint.SUCCESS, CORNER_GLOW_START, CORNER_GLOW_END, 2);
            InfoDemoChoreography.PulseOutline(
                builder, InfoDemoLayout.Cell(LAND_ROW, InfoDemoLayout.BOARD_SIZE - 1), 1, 1, InfoDemoPaint.SUCCESS,
                CORNER_GLOW_START, CORNER_GLOW_END, 2);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_CORNER_CLEARED,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 2.2f), 1.0f, InfoDemoPaint.INK, LABEL_START, 1.3f);

            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);

            return builder.Build();
        }
    }
}
