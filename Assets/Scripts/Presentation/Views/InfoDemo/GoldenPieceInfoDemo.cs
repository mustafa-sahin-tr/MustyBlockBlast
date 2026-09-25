using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialPieceKind.Golden"/> info demo (issue #451), authored from the real rule:
    /// <see cref="GoldenPieceTriggerSystem"/> arms a golden 1x1 when the combo streak reaches exactly
    /// <see cref="GoldenPieceTriggerSystem.TRIGGER_STREAK"/>, and <c>BoardSystem</c> pays it at the
    /// <em>next refill</em>, claiming slot 0 (<c>ApplyPendingInjections</c>). It then plays exactly like an
    /// ordinary single — the gold is the dock plate's (<see cref="SpecialPieceVisuals"/>), cosmetic to the
    /// rules, and the block it lands as is an ordinary board block.
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-2 ........
    /// row3   ....g...
    /// row4   ..uu....
    /// row5   .b...g..
    /// row6   g...bb..
    /// row7   ppgbb.uu     (7,5) is the row's one gap
    /// </code>
    /// The strip holds the HUD's combo pill on the left ("x4") and an empty dock — the last piece of the
    /// previous deal was just played. 0.6 s the pill ticks to "x5" · 1.0 s the refill deals three
    /// pieces: the golden single in slot 0, with a gold sparkle, then an L corner and a 1x2 · 2.0 s a
    /// finger drags the golden single onto (7,5) · 2.8 s row 7 is full and clears like any other ·
    /// hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class GoldenPieceInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const float STREAK_TIME = 0.6f;
        internal const float DEAL_START = 1.0f;
        internal const float DEAL_STAGGER = 0.08f;
        internal const float DRAG_START = 2.0f;
        internal const float CLEAR_DELAY = 0.1f;

        /// <summary>The slot the golden single is dealt into — slot 0, which <c>BoardSystem</c> claims
        /// first for an owed injection.</summary>
        internal const int GOLDEN_SLOT = 0;

        internal const int LAND_ROW = 7;
        internal const int LAND_COLUMN = 5;

        /// <summary>The block colour the golden single lands as: an ordinary colour, since the gold is
        /// only ever the dock plate's.</summary>
        internal const int LANDED_PAINT = InfoDemoPaint.BLOCK_3;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "....g...",
            "..uu....",
            ".b...g..",
            "g...bb..",
            "ppgbb.uu",
        };

        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>When the golden single lands on the board.</summary>
        internal static float LandTime() => DRAG_START + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION;

        /// <summary>When row 7 starts to clear.</summary>
        internal static float ClearStart() => LandTime() + CLEAR_DELAY;

        internal static InfoDemoTimeline Build() => Build(out _, out _);

        /// <summary>As <see cref="Build"/>, handing back the combo pill's counter and the golden piece's
        /// element id for tests.</summary>
        internal static InfoDemoTimeline Build(out InfoDemoCounter streak, out int goldenPiece)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            // 1. The fifth clear in a row: the streak reaches the trigger.
            int triggerStreak = GoldenPieceTriggerSystem.TRIGGER_STREAK;
            streak = InfoDemoSpecialPieceChoreography.StreakPill(builder, triggerStreak - 1, triggerStreak);
            InfoDemoHudChoreography.AdvanceCounter(builder, streak, STREAK_TIME);

            // 2. The next refill pays it: the golden single takes slot 0, the rest of the deal is ordinary.
            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            float specialScale = InfoDemoSpecialPieceChoreography.SPECIAL_SINGLE_SCALE;
            Vector2 goldenSlot = InfoDemoLayout.TraySlot(GOLDEN_SLOT, InfoDemoTrayLayout.ChipLeft);
            goldenPiece = builder.AddSpecialPiece(
                SpecialPieceKind.Golden, SingleShape, InfoDemoPaint.GOLDEN_PIECE, goldenSlot, specialScale, 0f);
            int corner = builder.AddPiece(
                CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft), scale, 0f);
            int domino = builder.AddPiece(
                DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale, 0f);

            int goldPaint = InfoDemoPaint.SpecialPieceIdentity(SpecialPieceKind.Golden);
            InfoDemoTrayChoreography.DealPiece(builder, goldenPiece, specialScale, DEAL_START);
            InfoDemoChoreography.Burst(builder, goldenSlot, goldPaint, InfoDemoPaint.WHITE, 1f, 2.6f, DEAL_START, 0.6f);
            InfoDemoTrayChoreography.DealPiece(builder, corner, scale, DEAL_START + DEAL_STAGGER);
            InfoDemoTrayChoreography.DealPiece(builder, domino, scale, DEAL_START + (DEAL_STAGGER * 2f));

            // 3. It plays like any single: dropped into row 7's gap, the row clears.
            InfoDemoSpecialPieceChoreography.DragPiece(
                builder, goldenPiece, SingleShape, LANDED_PAINT, goldenSlot, LAND_ROW, LAND_COLUMN, DRAG_START, specialScale);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, ClearStart());

            return builder.Build();
        }
    }
}
