using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.Vortex"/> info demo (issue #446), authored from the real rule
    /// (<see cref="VortexEffect"/>): a Vortex destroyed by a line clear fills every fully-enclosed
    /// island of empty cells, and a fill that completes a line clears through the cascade.
    /// <para>
    /// Choreography (5.2 s loop, board rows top to bottom; '.' empty, letters are block colours, the
    /// Vortex sits on row 7 column 1):
    /// <code>
    /// row0 ........
    /// row1 ........
    /// row2 ..ggg...
    /// row3 ..g.g...     (3,3) is an enclosed island
    /// row4 ..ggg.uu
    /// row5 uu...u.u     (5,2)-(5,4) and (5,6) are enclosed islands
    /// row6 bbbbbbb.
    /// row7 pppp.ppp     one gap at (7,4); the Vortex is (7,1)
    /// </code>
    /// 0.45 s the single pink piece lifts from tray slot 1 and glides into (7,4) · ~1.15 s it lands ·
    /// 1.3 s row 7 is full and clears left to right, the Vortex going with it · ~1.65 s a violet burst
    /// where it stood · 1.95 s dashed outlines pulse on the three islands · 2.45 s the five island cells
    /// fill one by one in the Vortex's indigo · 2.7 s "Holes filled!" floats up · 3.15 s row 5 is now
    /// full and clears (the cascade) · hold, fade out from 4.8 s, loop.
    /// </para>
    /// </summary>
    internal static class VortexInfoDemo
    {
        internal const float LOOP_DURATION = 5.2f;

        private const int VORTEX_ROW = 7;
        private const int VORTEX_COLUMN = 1;

        /// <summary>Degrees per second the Vortex icon slowly spins at while it waits.</summary>
        private const float SPIN_DEGREES_PER_SECOND = -100f;

        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);

            builder.SetBoardRow(2, "..ggg...");
            builder.SetBoardRow(3, "..g.g...");
            builder.SetBoardRow(4, "..ggg.uu");
            builder.SetBoardRow(5, "uu...u.u");
            builder.SetBoardRow(6, "bbbbbbb.");
            builder.SetBoardRow(7, "pppp.ppp");

            // Tray: a small L corner, the single that will be played, a 1x2.
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(0), InfoDemoLayout.TRAY_PIECE_SCALE);
            Vector2 singleTrayPosition = InfoDemoLayout.TraySlot(1);
            int singlePiece = builder.AddPiece(
                SingleShape, InfoDemoPaint.BLOCK_1, singleTrayPosition, InfoDemoLayout.TRAY_PIECE_SCALE);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_3, InfoDemoLayout.TraySlot(2), InfoDemoLayout.TRAY_PIECE_SCALE);

            // The Vortex on (7,1): its halo and its (slowly spinning) icon over the pink block.
            Vector2 vortexCell = InfoDemoLayout.Cell(VORTEX_ROW, VORTEX_COLUMN);
            int halo = builder.AddGlow(vortexCell, 1.35f, InfoDemoPaint.VORTEX_GLOW, 0.55f);
            int vortexIcon = builder.AddIcon(
                InfoDemoSprite.SpecialCellIcon, (int)SpecialCellKind.Vortex, vortexCell, 0.8f);

            // 1. Play the single into the one gap in row 7.
            InfoDemoChoreography.PlacePiece(
                builder, singlePiece, SingleShape, InfoDemoPaint.BLOCK_1, singleTrayPosition, VORTEX_ROW, 4, 0.45f);

            // 2. Row 7 is full: it clears, and the Vortex goes with its cell.
            const float rowSevenClear = 1.3f;
            InfoDemoChoreography.ClearRow(builder, VORTEX_ROW, rowSevenClear);

            float vortexGone = InfoDemoChoreography.ClearCellShrinkStart(rowSevenClear, VORTEX_COLUMN);
            const float vortexExitDuration = 0.3f;
            float spunBeforeExit = vortexGone * SPIN_DEGREES_PER_SECOND;
            builder.Rotate(vortexIcon, 0f, vortexGone, 0f, spunBeforeExit);
            builder.Rotate(
                vortexIcon, vortexGone, vortexExitDuration,
                spunBeforeExit, spunBeforeExit + (vortexExitDuration * SPIN_DEGREES_PER_SECOND * 2f));
            builder.Scale(vortexIcon, vortexGone, vortexExitDuration, 1f, 1.7f, InfoDemoEasing.EaseOutCubic);
            builder.Fade(vortexIcon, vortexGone, vortexExitDuration, 1f, 0f, InfoDemoEasing.EaseInCubic);
            builder.Fade(halo, vortexGone, 0.2f, 0.55f, 0f);

            // 3. Its effect: a violet burst where it stood — a wide soft bloom around a denser indigo core.
            InfoDemoChoreography.Burst(
                builder, vortexCell, InfoDemoPaint.VORTEX_GLOW, InfoDemoPaint.VORTEX_BLOCK, 1.2f, 4.2f, 1.65f, 0.6f);

            // 4. The three enclosed islands are called out (in the fill's own indigo, so outline and fill
            // read as one idea)...
            const float outlineStart = 1.95f;
            const float fillStart = 2.45f;
            InfoDemoChoreography.PulseOutline(
                builder, InfoDemoLayout.Cell(3, 3), 1, 1, InfoDemoPaint.VORTEX_BLOCK, outlineStart, fillStart, 2);
            InfoDemoChoreography.PulseOutline(
                builder, InfoDemoLayout.CellSpanCentre(5, 2, 5, 4), 3, 1, InfoDemoPaint.VORTEX_BLOCK, outlineStart, fillStart, 2);
            InfoDemoChoreography.PulseOutline(
                builder, InfoDemoLayout.Cell(5, 6), 1, 1, InfoDemoPaint.VORTEX_BLOCK, outlineStart, fillStart, 2);

            // 5. ...and filled one by one.
            const float fillStagger = 0.08f;
            InfoDemoChoreography.FillCell(builder, 3, 3, InfoDemoPaint.VORTEX_BLOCK, fillStart);
            InfoDemoChoreography.FillCell(builder, 5, 2, InfoDemoPaint.VORTEX_BLOCK, fillStart + fillStagger);
            InfoDemoChoreography.FillCell(builder, 5, 3, InfoDemoPaint.VORTEX_BLOCK, fillStart + (fillStagger * 2f));
            InfoDemoChoreography.FillCell(builder, 5, 4, InfoDemoPaint.VORTEX_BLOCK, fillStart + (fillStagger * 3f));
            InfoDemoChoreography.FillCell(builder, 5, 6, InfoDemoPaint.VORTEX_BLOCK, fillStart + (fillStagger * 4f));

            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_VORTEX_FILLED,
                new Vector2(3.5f, 4.1f),
                1.1f,
                InfoDemoPaint.VORTEX_BLOCK,
                2.7f,
                0.95f);

            // 6. The fill completed row 5: it clears through the cascade.
            InfoDemoChoreography.ClearRow(builder, 5, 3.15f);

            return builder.Build();
        }
    }
}
