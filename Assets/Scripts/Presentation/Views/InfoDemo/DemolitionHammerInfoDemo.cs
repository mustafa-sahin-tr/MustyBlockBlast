using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialPieceKind.DemolitionHammer"/> info demo (issue #451), authored from the real
    /// rule: when no dock piece fits a board at least 90% full, <c>BoardSystem.TryInjectDemolitionHammer</c>
    /// hands over a hammer — once per run — into the first empty dock slot. It is never dragged: a tap on
    /// its slot arms it (<c>BoardInputView.ArmHammer</c> lifts the plate), a tap on an occupied cell aims
    /// it (the cell takes the valid-target tint) and <c>BoardSystem.TryUseDemolitionHammer</c> destroys
    /// exactly that one cell and consumes the hammer. The plate is an ordinary colour wearing the hammer
    /// mark (<see cref="SpecialPieceVisuals"/>).
    /// <para>
    /// A hammer that only ever opens a single, otherwise-useless hole would teach the wrong lesson — the
    /// choreography instead shows the hammer earning its keep: it takes the one cell beside the board's
    /// existing gap, and the piece that gap alone could never fit now completes a line.
    /// </para>
    /// <para>
    /// Choreography (5.8 s loop; board rows top to bottom, '.' empty, letters are block colours — 55 of
    /// 64 cells full, every row and column keeping the one gap that stops it being a completed line
    /// already sitting there uncleared, and nothing in the dock fits):
    /// <code>
    /// row0 .gubpgub
    /// row1 g.bpgubp
    /// row2 ub.gubpg
    /// row3 bpg.bp.u
    /// row4 pgub.gub
    /// row5 gubpg.bp
    /// row6 ubpgubp.
    /// row7 bpgubp.u     the hammer takes (7,5), beside the row's existing gap at (7,6)
    /// </code>
    /// The dock holds a 3x3 (slot 0) and a horizontal 1x2 (slot 2); slot 1 is empty. Neither fits the
    /// crowded board — the 1x2 needs two horizontally adjacent empty cells and every row's gaps are
    /// isolated. Column 6 keeps a second gap at row 3 so completing it at row 7 does not also complete
    /// the column. 0.15 s a red "doesn't fit" mark on each · 0.5 s the hammer is dealt into slot 1 with a
    /// steel flare · 0.9 s "Last resort!" · 1.6 s a tap on the hammer arms it: it lifts inside a yellow
    /// ring · 2.35 s a tap on (7,5), which takes the valid-target tint · 2.45 s a steel burst and that one
    /// block is gone; the hammer leaves the dock · 2.9 s a finger drags the 1x2 out of slot 2 onto the
    /// two-cell gap it just opened, (7,5)-(7,6) · 3.7 s row 7 is full and clears like any other · hold,
    /// fade out from ~5.3 s, loop.
    /// </para>
    /// </summary>
    internal static class DemolitionHammerInfoDemo
    {
        internal const float LOOP_DURATION = 5.8f;

        internal const float REJECT_TIME = 0.15f;
        internal const float DEAL_TIME = 0.5f;
        internal const float PILL_START = 0.9f;
        internal const float PILL_END = 1.5f;
        internal const float ARM_TIME = 1.6f;
        internal const float TARGET_TAP_TIME = 2.35f;
        internal const float TARGET_HIGHLIGHT_LEAD = 0.07f;
        internal const float SMASH_TIME = 2.45f;

        /// <summary>When the two-cell gap the hammer opened is dragged shut by the 1x2.</summary>
        internal const float PLACE_START = 2.9f;

        /// <summary>Gap between the 1x2 landing and row 7 starting to clear.</summary>
        internal const float CLEAR_DELAY = 0.1f;

        /// <summary>The empty slot the hammer is dealt into — the dock's first empty slot, where
        /// <c>TryInjectDemolitionHammer</c> puts it.</summary>
        internal const int HAMMER_SLOT = 1;

        /// <summary>The cell the hammer destroys — occupied on the starting board, directly beside the
        /// row's one existing gap at (7,6), so removing it leaves a two-cell hole nothing but a 1x2 could
        /// have used.</summary>
        internal const int TARGET_ROW = 7;
        internal const int TARGET_COLUMN = 5;

        /// <summary>Where the dock's 1x2 lands — its top-left cell — filling the hammer's gap and the
        /// row's original one in a single piece.</summary>
        internal const int PLACE_ROW = TARGET_ROW;
        internal const int PLACE_COLUMN = TARGET_COLUMN;

        private const float BIG_PIECE_SCALE = 0.34f;
        private const int HAMMER_PAINT = InfoDemoPaint.BLOCK_2;
        private const int TWO_PIECE_PAINT = InfoDemoPaint.BLOCK_1;
        private const float EXIT_DURATION = 0.3f;

        internal static readonly string[] Rows =
        {
            ".gubpgub",
            "g.bpgubp",
            "ub.gubpg",
            "bpg.bp.u",
            "pgub.gub",
            "gubpg.bp",
            "ubpgubp.",
            "bpgubp.u",
        };

        /// <summary>The dock beside the hammer (neither fits the board above): a 3x3 in slot 0 and a
        /// horizontal 1x2 in slot 2, as (column, row) offsets.</summary>
        internal static readonly Vector2Int[] BigShape = InfoDemoLayout.Rectangle(3, 3);
        internal static readonly Vector2Int[] TwoShape = InfoDemoLayout.Rectangle(2, 1);
        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };

        /// <summary>When the 1x2 lands on the board.</summary>
        internal static float LandTime() => PLACE_START + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION;

        /// <summary>When row 7 starts to clear.</summary>
        internal static float ClearStart() => LandTime() + CLEAR_DELAY;

        internal static InfoDemoTimeline Build() => Build(out _);

        /// <summary>As <see cref="Build"/>, handing back the hammer piece's element id for tests.</summary>
        internal static InfoDemoTimeline Build(out int hammerPiece)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            // 1. A dead end: nothing in the dock fits the crowded board.
            Vector2 bigSlot = InfoDemoLayout.TraySlot(0);
            Vector2 twoSlot = InfoDemoLayout.TraySlot(2);
            builder.AddPiece(BigShape, InfoDemoPaint.BLOCK_3, bigSlot, BIG_PIECE_SCALE);
            int twoPiece = builder.AddPiece(TwoShape, TWO_PIECE_PAINT, twoSlot, InfoDemoLayout.TRAY_PIECE_SCALE);
            InfoDemoTrayChoreography.RejectMark(
                builder, InfoDemoLayout.TopRightCorner(BigShape, bigSlot, BIG_PIECE_SCALE), REJECT_TIME, ARM_TIME);
            InfoDemoTrayChoreography.RejectMark(
                builder, InfoDemoLayout.TopRightCorner(TwoShape, twoSlot, InfoDemoLayout.TRAY_PIECE_SCALE),
                REJECT_TIME + 0.05f, ARM_TIME);

            // 2. The life-line: a hammer in the empty slot.
            float scale = InfoDemoSpecialPieceChoreography.SPECIAL_SINGLE_SCALE;
            Vector2 hammerSlot = InfoDemoLayout.TraySlot(HAMMER_SLOT);
            hammerPiece = builder.AddSpecialPiece(
                SpecialPieceKind.DemolitionHammer, SingleShape, HAMMER_PAINT, hammerSlot, scale, 0f);
            int steelPaint = InfoDemoPaint.SpecialPieceIdentity(SpecialPieceKind.DemolitionHammer);
            InfoDemoTrayChoreography.DealPiece(builder, hammerPiece, scale, DEAL_TIME);
            InfoDemoChoreography.Burst(builder, hammerSlot, steelPaint, InfoDemoPaint.WHITE, 1f, 2.6f, DEAL_TIME, 0.6f);

            InfoDemoHudChoreography.LabelPill(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_LAST_RESORT,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 3.5f),
                new Vector2(5.4f, 1f),
                0.55f,
                InfoDemoPaint.INK,
                PILL_START,
                PILL_END);

            // 3. Tap to arm it — it is never dragged — then tap the one block to destroy.
            InfoDemoSpecialPieceChoreography.ArmPiece(builder, hammerPiece, hammerSlot, scale, 1f, ARM_TIME, SMASH_TIME);
            InfoDemoChoreography.TapCell(builder, TARGET_TAP_TIME, TARGET_ROW, TARGET_COLUMN);
            InfoDemoChoreography.RectHighlight(
                builder, TARGET_ROW, TARGET_COLUMN, TARGET_ROW, TARGET_COLUMN, InfoDemoPaint.VALID_PREVIEW, 0.85f,
                TARGET_TAP_TIME - TARGET_HIGHLIGHT_LEAD, SMASH_TIME);

            // 4. That one cell goes — opening a two-cell gap beside (7,6) — and the hammer is spent.
            Vector2 target = InfoDemoLayout.Cell(TARGET_ROW, TARGET_COLUMN);
            InfoDemoChoreography.Burst(builder, target, steelPaint, InfoDemoPaint.INK, 1.2f, 3f, SMASH_TIME, 0.55f);
            InfoDemoChoreography.ClearCells(builder, new[] { new Vector2Int(TARGET_COLUMN, TARGET_ROW) }, target, SMASH_TIME, 0f);

            float liftedScale = scale * InfoDemoSpecialPieceChoreography.ARMED_LIFT;
            builder.Scale(hammerPiece, SMASH_TIME, EXIT_DURATION, liftedScale, liftedScale * 0.4f, InfoDemoEasing.EaseInCubic);
            builder.Fade(hammerPiece, SMASH_TIME, EXIT_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);

            // 5. The gap it opened is exactly what the 1x2 needed: dragged in, row 7 fills and clears.
            InfoDemoSpecialPieceChoreography.DragPiece(
                builder, twoPiece, TwoShape, TWO_PIECE_PAINT, twoSlot, PLACE_ROW, PLACE_COLUMN, PLACE_START,
                InfoDemoLayout.TRAY_PIECE_SCALE);
            InfoDemoChoreography.ClearRow(builder, PLACE_ROW, ClearStart());

            return builder.Build();
        }
    }
}
