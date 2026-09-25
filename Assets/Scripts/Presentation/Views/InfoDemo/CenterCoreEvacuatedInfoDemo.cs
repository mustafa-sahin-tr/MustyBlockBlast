using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.CenterCoreEvacuated"/> objective card's demo (issue #453, mockup artboard
    /// "Merkez"), authored from the real rule (<see cref="Board.IsCenterCoreEmpty"/>): a placement counts once
    /// when, after its clears, the board's centred 4x4 (rows 2-5, columns 2-5 on the 8x8) holds no block at
    /// all — the rest of the board may stay as full as it likes. The objective has no parameter, so there is
    /// one demo; its chip is illustrative (0/1 → 1/1).
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 .g......
    /// row2 ......u.
    /// row3 .b......
    /// row4 ggbb.gpu     the centre's only blocks are in row 4, which is full except (4,4)
    /// row5 ......b.
    /// row6 g.......
    /// row7 gg....bb
    /// </code>
    /// The strip holds the progress chip ("CENTER", 0/1) on the left and three tray pieces on the right.
    /// From the start a dashed orange outline marks the centre 4x4 · 0.45 s the single lifts and glides to
    /// (4,4) · ~1.15 s it lands, completing row 4 · 1.3 s row 4 clears · 1.9 s the centre is empty: the dashed
    /// outline gives way to a solid green one · 2.0 s "Center empty!" · 2.1 s the chip ticks 0/1 → 1/1 · hold,
    /// fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class CenterCoreEvacuatedInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        /// <summary>The centred core, as the real <see cref="Board.IsCenterCoreEmpty"/> computes it on 8x8.</summary>
        internal const int CORE_FIRST = 2;
        internal const int CORE_LAST = 5;

        internal const int LAND_ROW = 4;
        internal const int LAND_COLUMN = 4;

        internal const float PLACE_START = 0.45f;
        internal const float CLEAR_START = 1.3f;
        internal const float SOLID_START = 1.9f;
        internal const float SOLID_END = 3.9f;
        internal const float LABEL_START = 2.0f;
        internal const float CHIP_ADVANCE_TIME = 2.1f;

        private const float DASHED_ALPHA = 0.95f;
        private const float OUTLINE_PADDING = 0.14f;

        /// <summary>The solid ring: hugs the four cells (four pitches less one gap) plus the dashed outline's
        /// padding, in board units.</summary>
        private const float RING_SIZE = 4f - (InfoDemoLayout.MOCK_GAP / InfoDemoLayout.MOCK_PITCH) + OUTLINE_PADDING;
        private const float RING_CORNER = 0.3f;
        private const float RING_STROKE = 0.09f;

        internal static readonly string[] Rows =
        {
            "........",
            ".g......",
            "......u.",
            ".b......",
            "ggbb.gpu",
            "......b.",
            "g.......",
            "gg....bb",
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

            chip = InfoDemoChoreography.ProgressChip(builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_CENTER, null, 0, 1);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), scale);
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            // 0. "This area": the centre 4x4, dashed in orange from the start.
            Vector2 coreCentre = InfoDemoLayout.CellSpanCentre(CORE_FIRST, CORE_FIRST, CORE_LAST, CORE_LAST);
            int coreSize = CORE_LAST - CORE_FIRST + 1;
            int dashed = builder.AddOutline(
                coreCentre, new Vector2(coreSize + OUTLINE_PADDING, coreSize + OUTLINE_PADDING), InfoDemoPaint.BLAST, DASHED_ALPHA);

            // 1. The single completes row 4, which holds the centre's last blocks.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);

            // 2. The centre is empty: dashed orange gives way to solid green.
            builder.Fade(dashed, SOLID_START, 0.25f, DASHED_ALPHA, 0f, InfoDemoEasing.EaseInCubic);
            int solid = builder.AddRing(
                coreCentre, new Vector2(RING_SIZE, RING_SIZE), RING_CORNER, RING_STROKE, InfoDemoPaint.SUCCESS);
            builder.Fade(solid, SOLID_START, 0.25f, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Scale(solid, SOLID_START, 0.4f, 1.12f, 1f, InfoDemoEasing.EaseOutBack);
            builder.Fade(solid, SOLID_END, 0.3f, 1f, 0f, InfoDemoEasing.EaseInCubic);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_CENTER_EMPTY, new Vector2(coreCentre.x, coreCentre.y + 0.3f), 0.9f,
                InfoDemoPaint.INK, LABEL_START, 1.3f);

            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);

            return builder.Build();
        }
    }
}
