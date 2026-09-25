using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.IceCellsCleared"/> objective card's demo (issue #453, mockup artboard
    /// "Buz"), authored from the real rule (issue #433, <see cref="Board.TryDamage"/>): an ice socket starts
    /// the run EMPTY and playable with an authored level of 1-3. Each time a block standing on it is
    /// destroyed, the ice melts by one level and the socket is empty again; the objective counts sockets that
    /// reach level 0 (its target is always every socket the level authored). The socket is drawn exactly as
    /// the board draws it — the frost plate and ice ring of <c>CellView.SetIceOverlay</c>, thinner at the last
    /// level. The objective has no parameter, so there is one demo.
    /// <para>
    /// Choreography (5.6 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ....u...
    /// row1 ..g.b...
    /// row2 ....g.u.
    /// row3 .b..p...
    /// row4 ....u.g.     column 4 is full down to row 5
    /// row5 ...gb...
    /// row6 g....b..
    /// row7 ppgb.buu     (7,4) is an empty ice socket at level 2 — row 7's gap
    /// </code>
    /// The strip holds the progress chip ("ICE", 0/1) on the left and three tray pieces on the right: a single,
    /// a vertical 1x2 and an L corner.
    /// 0.4 s the single fills the socket · 1.25 s row 7 clears, the block on the socket goes and the ice melts
    /// to level 1 (thin ring) · 2.2 s the vertical 1x2 drops onto (6,4)-(7,4), completing column 4 · 3.05 s
    /// column 4 clears and the last level melts away · the chip ticks 0/1 → 1/1 · 3.7 s "Ice melted!" · hold,
    /// fade out from 5.2 s, loop.
    /// </para>
    /// </summary>
    internal static class IceCellsClearedInfoDemo
    {
        internal const float LOOP_DURATION = 5.6f;

        internal const int ICE_ROW = 7;
        internal const int ICE_COLUMN = 4;

        /// <summary>The socket's authored ice level at loop start (authored levels run 1-3).</summary>
        internal const int START_ICE_LEVEL = 2;

        internal const float FIRST_PLACE_START = 0.4f;
        internal const float FIRST_CLEAR_START = 1.25f;
        internal const float SECOND_PLACE_START = 2.2f;
        internal const float SECOND_CLEAR_START = 3.05f;
        internal const float CHIP_DELAY = 0.15f;
        internal const float LABEL_START = 3.7f;

        internal static readonly string[] Rows =
        {
            "....u...",
            "..g.b...",
            "....g.u.",
            ".b..p...",
            "....u.g.",
            "...gb...",
            "g....b..",
            "ppgb.buu",
        };

        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        internal static readonly Vector2Int[] VerticalDominoShape = { new Vector2Int(0, 0), new Vector2Int(0, 1) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };

        /// <summary>Where the vertical 1x2 lands (its top cell).</summary>
        internal const int SECOND_LAND_ROW = ICE_ROW - 1;

        /// <summary>When the first clear takes the block off the socket and the ice drops a level.</summary>
        internal static float FirstMeltTime() => InfoDemoChoreography.ClearCellShrinkStart(FIRST_CLEAR_START, ICE_COLUMN);

        /// <summary>When the column clear takes the second block off the socket and the last level goes.</summary>
        internal static float SecondMeltTime() => InfoDemoChoreography.ClearCellShrinkStart(SECOND_CLEAR_START, ICE_ROW);

        internal static InfoDemoTimeline Build() => Build(out _, out _);

        /// <summary>As <see cref="Build()"/>, handing back the chip and the ice layer so a test can read them.</summary>
        internal static InfoDemoTimeline Build(out InfoDemoProgressChip chip, out int iceLayer)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            chip = InfoDemoChoreography.ProgressChip(builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_ICE, null, 0, 1);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft);
            Vector2 dominoSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, scale);
            int domino = builder.AddPiece(VerticalDominoShape, InfoDemoPaint.BLOCK_1, dominoSlot, scale);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            iceLayer = builder.AddCellLayer(InfoDemoCellLayer.IceSocket, ICE_ROW, ICE_COLUMN, START_ICE_LEVEL);

            // 1. A block on the socket, then row 7 clears it: one level of ice melts with it.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, ICE_ROW, ICE_COLUMN, FIRST_PLACE_START);
            InfoDemoChoreography.ClearRow(builder, ICE_ROW, FIRST_CLEAR_START);
            InfoDemoSpecialCellChoreography.StepLayer(builder, iceLayer, START_ICE_LEVEL - 1, FirstMeltTime());

            // 2. Another block on it, cleared by column 4: the last level goes, and the socket counts.
            InfoDemoChoreography.PlacePiece(
                builder, domino, VerticalDominoShape, InfoDemoPaint.BLOCK_1, dominoSlot, SECOND_LAND_ROW, ICE_COLUMN,
                SECOND_PLACE_START);
            InfoDemoChoreography.ClearColumn(builder, ICE_COLUMN, SECOND_CLEAR_START);
            float melted = SecondMeltTime();
            InfoDemoSpecialCellChoreography.LayerGoes(builder, iceLayer, melted);
            InfoDemoChoreography.AdvanceChip(builder, chip, melted + CHIP_DELAY);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_ICE_MELTED, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 5.6f),
                1.0f, InfoDemoPaint.INK, LABEL_START, 1.3f);

            return builder.Build();
        }
    }
}
