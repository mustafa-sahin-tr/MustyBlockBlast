using System.Globalization;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.PowerStar"/> info demo (issue #482), authored from the real rule
    /// (<see cref="PowerStarCharging"/>, <see cref="PowerStarEffect"/>): line clears through the star
    /// charge it instead of removing it — here a row and a column at once, +2 — and at
    /// <see cref="Board.POWER_STAR_BURST_CHARGE"/> it bursts, destroying the 3x3 around it.
    /// <para>
    /// Choreography (5.6 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-3 ...X....   column 3 filled down to the star
    /// row4   ..b.....   (4,3) and (4,4) empty — the L lands on (4,3),(4,4),(5,4)
    /// row5   gubS.pbu   the star on (5,3), at 1/3; (5,4) is row 5's one gap
    /// row6   ..pup...
    /// row7   g..g...b
    /// </code>
    /// 0.4 s the L lands, completing row 5 and column 3 at once · 1.25 s both clear around the star,
    /// which stays · the chip ticks 1/3 → 3/3 with a "+2" · ~2.4 s the star bursts: its own cell and the
    /// four blocks left in its 3x3 go · hold, fade out from 5.2 s, loop.
    /// </para>
    /// </summary>
    internal static class PowerStarInfoDemo
    {
        internal const float LOOP_DURATION = 5.6f;

        internal const int STAR_ROW = 5;
        internal const int STAR_COLUMN = 3;

        /// <summary>The charge the star starts the loop at: one short of what a row-and-column clear needs.</summary>
        internal const int START_CHARGE = Board.POWER_STAR_BURST_CHARGE - 2;

        internal const float PLACE_START = 0.4f;
        internal const float CLEAR_START = 1.25f;
        private const float CHARGE_DELAY = 0.15f;
        private const float BURST_DELAY = 0.7f;
        private const float BURST_STAGGER = 0.05f;

        internal const int LAND_ROW = 4;
        internal const int LAND_COLUMN = 3;

        internal static readonly string[] Rows =
        {
            "...b....",
            "...u....",
            "...g....",
            "...p....",
            "..b.....",
            "gubg.pbu",
            "..pup...",
            "g..g...b",
        };

        /// <summary>The L that lands on (4,3), (4,4) and (5,4): x = column, y = row.</summary>
        internal static readonly Vector2Int[] LShape = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>The blocks left standing in the star's 3x3 once its row and column have cleared, which
        /// the burst takes (x = column, y = row).</summary>
        internal static readonly Vector2Int[] BurstCells =
        {
            new Vector2Int(2, 4), new Vector2Int(4, 4), new Vector2Int(2, 6), new Vector2Int(4, 6),
        };

        /// <summary>When the row and the column reach the star and charge it.</summary>
        internal static float ChargeTime() => Mathf.Max(
            InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, STAR_COLUMN),
            InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, STAR_ROW));

        /// <summary>When the fully charged star bursts.</summary>
        internal static float BurstTime() => ChargeTime() + BURST_DELAY;

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoProgressChip chip = InfoDemoChoreography.ProgressChip(
                builder, InfoDemoSprite.SpecialCellIcon, (int)SpecialCellKind.PowerStar, InfoDemoPaint.WHITE,
                START_CHARGE, Board.POWER_STAR_BURST_CHARGE);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 lSlot = InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft);
            int lPiece = builder.AddPiece(LShape, InfoDemoPaint.BLOCK_4, lSlot, scale);
            builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft), scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            int starIcon = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.PowerStar, STAR_ROW, STAR_COLUMN, out int starBlock);
            Vector2 starCell = InfoDemoLayout.Cell(STAR_ROW, STAR_COLUMN);

            // 1. The L completes row 5 and column 3 at once; both clear around the star, which stays.
            InfoDemoChoreography.PlacePiece(
                builder, lPiece, LShape, InfoDemoPaint.BLOCK_4, lSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRowSparing(builder, STAR_ROW, STAR_COLUMN, CLEAR_START);
            InfoDemoChoreography.ClearColumnSparing(builder, STAR_COLUMN, STAR_ROW, CLEAR_START);

            // 2. Two lines through it: +2 charge, 1/3 → 3/3.
            float charged = ChargeTime();
            InfoDemoChoreography.AdvanceChip(builder, chip, charged + CHARGE_DELAY, 2);
            InfoDemoChoreography.FloatText(
                builder, "+2", starCell + new Vector2(0.15f, -0.75f), 0.6f, InfoDemoPaint.SUCCESS, 0.45f, charged, 0.9f);

            // 3. Full: it bursts, taking its own cell and what is left of its 3x3.
            float burst = BurstTime();
            InfoDemoSpecialCellChoreography.GoWithCell(builder, starIcon, starBlock, burst);

            // The clears spared the star's own block; it goes now, with the star, so nothing is left
            // standing under it once the cover ends.
            builder.Paint(starBlock, burst, InfoDemoPaint.NONE);
            InfoDemoChoreography.Burst(
                builder, starCell, InfoDemoPaint.SpecialCellGlow(SpecialCellKind.PowerStar), InfoDemoPaint.NONE,
                1.4f, 3.6f, burst, 0.5f);
            InfoDemoChoreography.ClearCells(builder, BurstCells, starCell, burst + 0.05f, BURST_STAGGER);

            return builder.Build();
        }
    }
}
