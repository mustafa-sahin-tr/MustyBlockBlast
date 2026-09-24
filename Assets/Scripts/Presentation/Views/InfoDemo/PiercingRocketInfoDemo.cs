using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialPieceKind.PiercingRocket"/> info demo (issue #451), authored from the real
    /// rule: <c>BoardSystem</c> owes a rocket to the next refill after a placement that clears three or
    /// more lines (<c>ROCKET_TRIGGER_LINE_COUNT</c>) and deals it as a 1x1 into slot 0; placed, it
    /// occupies its cell and <see cref="PiercingRocketEffect"/> empties the <em>whole</em> row and the
    /// <em>whole</em> column through it — full or not, its own cell included — before the placement's
    /// ordinary line resolution. The dock plate is an ordinary colour wearing the rocket mark
    /// (<see cref="SpecialPieceVisuals"/>).
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 ....b...
    /// row2 ..g.u...
    /// row3 .pu..gb.     the rocket lands on (3,4); neither row 3 nor column 4 is full
    /// row4 ....g...
    /// row5 ..bb.u..
    /// row6 g...p...
    /// row7 gg..uubb
    /// </code>
    /// The dock starts empty. 0.4 s the refill deals the rocket into slot 0, with an orange flare, then
    /// an L corner and a 1x2 · 1.0 s a finger drags the rocket onto (3,4), landing at 1.7 s · 1.75 s two
    /// orange beams run along row 3 and down column 4 · 1.85 s every block on both lines goes, nearest
    /// the rocket first, the rocket's own cell with them · 3.0 s "Row + column!" · hold, fade out from
    /// 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class PiercingRocketInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const float DEAL_START = 0.4f;
        internal const float DEAL_STAGGER = 0.08f;
        internal const float DRAG_START = 1.0f;
        internal const float BEAM_DELAY = 0.05f;
        internal const float WIPE_DELAY = 0.15f;
        internal const float WIPE_STAGGER = 0.04f;
        internal const float LABEL_TIME = 3.0f;

        /// <summary>The slot the rocket is dealt into — slot 0, which <c>BoardSystem</c> claims first for
        /// an owed injection.</summary>
        internal const int ROCKET_SLOT = 0;

        internal const int LAND_ROW = 3;
        internal const int LAND_COLUMN = 4;

        /// <summary>The rocket plate's own colour, which is also what its cell lands as.</summary>
        internal const int ROCKET_PAINT = InfoDemoPaint.BLOCK_1;

        internal static readonly string[] Rows =
        {
            "........",
            "....b...",
            "..g.u...",
            ".pu..gb.",
            "....g...",
            "..bb.u..",
            "g...p...",
            "gg..uubb",
        };

        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>When the rocket lands on the board.</summary>
        internal static float LandTime() => DRAG_START + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION;

        /// <summary>Every block the rocket takes (x = column, y = row): each occupied cell of its row and
        /// column on the starting board, plus the rocket's own cell.</summary>
        internal static Vector2Int[] WipedCells()
        {
            Vector2Int[] cross = InfoDemoBoardPattern.OccupiedCross(Rows, LAND_ROW, LAND_COLUMN);
            List<Vector2Int> wiped = new List<Vector2Int>(cross.Length + 1) { new Vector2Int(LAND_COLUMN, LAND_ROW) };
            wiped.AddRange(cross);
            return wiped.ToArray();
        }

        internal static InfoDemoTimeline Build() => Build(out _);

        /// <summary>As <see cref="Build"/>, handing back the rocket piece's element id for tests.</summary>
        internal static InfoDemoTimeline Build(out int rocketPiece)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            // 1. The refill pays the rocket a big clear earned: slot 0, then an ordinary deal.
            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            float specialScale = InfoDemoSpecialPieceChoreography.SPECIAL_SINGLE_SCALE;
            Vector2 rocketSlot = InfoDemoLayout.TraySlot(ROCKET_SLOT);
            rocketPiece = builder.AddSpecialPiece(
                SpecialPieceKind.PiercingRocket, SingleShape, ROCKET_PAINT, rocketSlot, specialScale, 0f);
            int corner = builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(1), scale, 0f);
            int domino = builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2), scale, 0f);

            int firePaint = InfoDemoPaint.SpecialPieceIdentity(SpecialPieceKind.PiercingRocket);
            InfoDemoTrayChoreography.DealPiece(builder, rocketPiece, specialScale, DEAL_START);
            InfoDemoChoreography.Burst(builder, rocketSlot, firePaint, InfoDemoPaint.WHITE, 1f, 2.6f, DEAL_START, 0.6f);
            InfoDemoTrayChoreography.DealPiece(builder, corner, scale, DEAL_START + DEAL_STAGGER);
            InfoDemoTrayChoreography.DealPiece(builder, domino, scale, DEAL_START + (DEAL_STAGGER * 2f));

            // 2. Placed, it fires both ways at once.
            float landTime = InfoDemoSpecialPieceChoreography.DragPiece(
                builder, rocketPiece, SingleShape, ROCKET_PAINT, rocketSlot, LAND_ROW, LAND_COLUMN, DRAG_START, specialScale);

            Vector2 rocketCell = InfoDemoLayout.Cell(LAND_ROW, LAND_COLUMN);
            InfoDemoChoreography.Burst(builder, rocketCell, firePaint, InfoDemoPaint.WHITE, 1.2f, 3.2f, landTime, 0.5f);
            InfoDemoChoreography.Beam(builder, landTime + BEAM_DELAY, true, LAND_ROW, firePaint);
            InfoDemoChoreography.Beam(builder, landTime + BEAM_DELAY, false, LAND_COLUMN, firePaint);

            // 3. Both lines are wiped end to end, though neither was full — the rocket's own cell too.
            InfoDemoChoreography.ClearCells(builder, WipedCells(), rocketCell, landTime + WIPE_DELAY, WIPE_STAGGER);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_ROW_AND_COLUMN,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 5.2f), 1f, firePaint, LABEL_TIME, 1.2f);

            return builder.Build();
        }
    }
}
