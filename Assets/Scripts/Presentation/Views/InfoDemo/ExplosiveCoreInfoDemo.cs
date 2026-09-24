using System.Collections.Generic;
using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.ExplosiveCore"/> info demo (issue #450), authored from the real rule
    /// (<see cref="ExplosiveCoreEffect"/>, issue #398): a core wipes the whole line at right angles to
    /// whatever destroyed it — taken out by a column clear it wipes its row, full or not, edge to edge —
    /// and every wiped cell scores a +1 bonus (<c>ExplosiveCoreScoreSystem</c>,
    /// <see cref="ScoreRules.PlacementScore"/>). It is earned by a placement that clears a row and a column
    /// at once (<see cref="ExplosiveCoreSpawnSelector"/>), which the card's body says.
    /// <para>
    /// Deliberately the column-clear case, where the Laser demo shows the row-clear case: the two kinds
    /// share one effect since #398, and between them the two cards show both directions.
    /// </para>
    /// <para>
    /// Choreography (5.2 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ..g.....
    /// row1 ..p..u..
    /// row2 .bu.....
    /// row3 ggbuu.pb     the core rides on (3,2); (3,5) is a hole, so row 3 is not full
    /// row4 ..g.....
    /// row5 ..u.g...
    /// row6 .pb..bu.
    /// row7 u...gb.p     (7,2) is column 2's one gap
    /// </code>
    /// The tray holds an L corner, the single that will be played and a 1x2.
    /// 0.4 s the single lifts and lands on (7,2) · 1.2 s column 2 is full and clears top to bottom, the
    /// core going with it · ~1.55 s a crimson burst where it stood · 1.75 s a crimson beam runs along
    /// row 3 · 1.85 s row 3's six blocks clear from the core's cell outwards · 2.3 s "+6" (one bonus
    /// point per wiped cell) · 2.9 s "Bonus line!" · hold, fade out from 4.8 s, loop.
    /// </para>
    /// </summary>
    internal static class ExplosiveCoreInfoDemo
    {
        internal const float LOOP_DURATION = 5.2f;

        internal const float PLACE_START = 0.4f;
        internal const float CLEAR_START = 1.2f;
        internal const float BEAM_TIME = 1.75f;
        internal const float WIPE_START = 1.85f;
        internal const float WIPE_STAGGER = 0.05f;
        internal const float BONUS_TIME = 2.3f;
        internal const float LABEL_TIME = 2.9f;

        internal const int CORE_ROW = 3;
        internal const int CORE_COLUMN = 2;

        /// <summary>Where the single lands: column 2's one gap.</summary>
        internal const int LAND_ROW = 7;
        internal const int LAND_COLUMN = CORE_COLUMN;

        private const float BURST_DELAY = 0.15f;

        internal static readonly string[] Rows =
        {
            "..g.....",
            "..p..u..",
            ".bu.....",
            "ggbuu.pb",
            "..g.....",
            "..u.g...",
            ".pb..bu.",
            "u...gb.p",
        };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>The blocks the bonus wipe takes (x = column, y = row): every occupied cell of the core's
        /// row other than the core's own, which the column clear already took.</summary>
        internal static Vector2Int[] WipedCells()
        {
            Vector2Int[] row = InfoDemoBoardPattern.OccupiedCells(Rows, CORE_ROW, 0, CORE_ROW, InfoDemoLayout.BOARD_SIZE - 1);
            List<Vector2Int> wiped = new List<Vector2Int>(row.Length);
            for (int cellIndex = 0; cellIndex < row.Length; cellIndex++)
            {
                if (row[cellIndex].x != CORE_COLUMN)
                {
                    wiped.Add(row[cellIndex]);
                }
            }

            return wiped.ToArray();
        }

        /// <summary>The bonus the wipe scores: one point per wiped cell, as <c>ExplosiveCoreScoreSystem</c>
        /// pays it (no 2x window running).</summary>
        internal static int BonusScore() => ScoreRules.PlacementScore(WipedCells().Length);

        /// <summary>When the core starts to go with column 2's clear.</summary>
        internal static float CoreGoneTime() => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, CORE_ROW);

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0), scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_1, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2), scale);

            int coreIcon = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.ExplosiveCore, CORE_ROW, CORE_COLUMN, out int coreHalo);
            int corePaint = InfoDemoPaint.SpecialCellGlow(SpecialCellKind.ExplosiveCore);
            Vector2 coreCell = InfoDemoLayout.Cell(CORE_ROW, CORE_COLUMN);

            // 1. The single completes column 2, which clears — a column clear, taking the core with it.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_1, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearColumn(builder, CORE_COLUMN, CLEAR_START);

            float coreGone = CoreGoneTime();
            InfoDemoSpecialCellChoreography.GoWithCell(builder, coreIcon, coreHalo, coreGone);
            InfoDemoChoreography.Burst(
                builder, coreCell, corePaint, InfoDemoPaint.BLAST, 1.2f, 4f, coreGone + BURST_DELAY, 0.6f);

            // 2. Its effect: the line at right angles — row 3 — is wiped end to end, though it was not full.
            InfoDemoChoreography.Beam(builder, BEAM_TIME, true, CORE_ROW, corePaint);
            InfoDemoChoreography.ClearCells(builder, WipedCells(), coreCell, WIPE_START, WIPE_STAGGER);

            // 3. Every wiped cell pays a bonus point.
            float centreX = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            InfoDemoChoreography.FloatText(
                builder, "+" + BonusScore().ToString(CultureInfo.InvariantCulture), new Vector2(centreX, CORE_ROW - 0.2f),
                0.7f, corePaint, 0.7f, BONUS_TIME, 1.2f);
            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_BONUS_LINE, new Vector2(centreX, 5f), 1f, corePaint,
                LABEL_TIME, 1.1f);

            return builder.Build();
        }
    }
}
