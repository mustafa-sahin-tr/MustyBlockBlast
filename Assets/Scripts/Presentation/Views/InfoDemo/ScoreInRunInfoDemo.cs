using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.ScoreInRun"/> objective card's demo (issue #454, mockup artboard "Koşuda
    /// puan"), authored from the real rule (<see cref="ObjectiveProgress"/>): progress mirrors the run score,
    /// clamped to the target — the HUD chip reads "3000/3000", never "3013/3000". The demo's one move is
    /// scored by the real <see cref="ScoreRules"/>: a 1x3 completes three rows at once for
    /// <see cref="Gain"/> points (3 placed cells + 10 x 3 lines x the 3-line combo multiplier 6), and the chip
    /// starts just far enough below the objective's own target that this one move crosses it.
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-2 ........
    /// row3 ....g...
    /// row4 ..uu....
    /// row5 ppgg.bbu
    /// row6 uubb.ggp
    /// row7 gbbu.upp
    /// </code>
    /// The strip holds the chip — the objective's own trophy icon over "2820/3000" — on the left and a tray of
    /// an L corner (green), a vertical 1x3 (magenta) and a 1x2 (blue) on the right.
    /// 0.45 s the 1x3 lifts from slot 1 and glides into column 4 · ~1.15 s it lands, completing rows 5-7 ·
    /// 1.3 s they clear together · 1.55 s "+183" · 1.8 s the chip jumps to "3000/3000" and the green check
    /// pops in · 2.1 s "Target score!" · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class ScoreInRunInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        /// <summary>The largest target the chip is drawn for — a reading of five digits a side still fits it.</summary>
        internal const int MAX_TARGET = 99999;

        internal const int LAND_ROW = 5;
        internal const int LAND_COLUMN = 4;
        internal const int CLEARED_LINE_COUNT = 3;

        internal const float PLACE_START = 0.45f;
        internal const float CLEAR_START = 1.3f;
        internal const float GAIN_TEXT_START = 1.55f;
        internal const float CHIP_ADVANCE_TIME = 1.8f;
        internal const float LABEL_START = 2.1f;

        /// <summary>The chip's start reading is rounded down to a multiple of this, so it reads like a score.</summary>
        private const int CHIP_START_STEP = 10;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "....g...",
            "..uu....",
            "ppgg.bbu",
            "uubb.ggp",
            "gbbu.upp",
        };

        internal static readonly Vector2Int[] BarShape = InfoDemoLayout.Rectangle(1, CLEARED_LINE_COUNT);

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>What the move scores under the real rules, on a run with no streak going: the placed cells plus
        /// the three-line clear. No bonus rule pays on top — no cleared line is one colour, it is the run's first
        /// multi-line clear (no milestone, no multi-clear streak) and the board is not wiped.</summary>
        internal static readonly int Gain =
            ScoreRules.PlacementScore(CLEARED_LINE_COUNT) + ScoreRules.ClearScore(CLEARED_LINE_COUNT, 0);

        /// <summary>True when the chip can honestly show <paramref name="target"/>.</summary>
        internal static bool Supports(int target) => target >= 1 && target <= MAX_TARGET;

        /// <summary>The chip's reading at loop start for <paramref name="target"/>: as high as it can be, rounded
        /// down to a tidy score, while <see cref="Gain"/> still carries it to the target.</summary>
        internal static int ChipStart(int target)
        {
            int lowest = target - Gain + CHIP_START_STEP;
            return Mathf.Max(0, Mathf.FloorToInt(lowest / (float)CHIP_START_STEP) * CHIP_START_STEP);
        }

        /// <summary>The chip's reading after the move: the run score, clamped to the target as the real progress is.</summary>
        internal static int ChipEnd(int target) => Mathf.Min(ChipStart(target) + Gain, target);

        /// <summary>When the 1x3 lands.</summary>
        internal static float LandTime => PLACE_START + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION;

        internal static InfoDemoTimeline Build(int target) => Build(target, out _);

        /// <summary>As <see cref="Build(int)"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(int target, out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);
            chip = InfoDemoChoreography.ReadingChip(
                builder, InfoDemoSprite.ObjectiveIcon, (int)ObjectiveType.ScoreInRun, InfoDemoPaint.WHITE,
                null, null, new[] { ChipStart(target), ChipEnd(target) }, target);

            PlayMove(builder, chip, target);
            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_TARGET_SCORE, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 3.2f),
                1.0f, InfoDemoPaint.INK, LABEL_START, 1.4f);

            return builder.Build();
        }

        /// <summary>
        /// The move itself, shared with <see cref="EarlyScoreRushInfoDemo"/>: the tray, the 1x3 dropping into
        /// column 4, rows 5-7 clearing together, "+<see cref="Gain"/>" and <paramref name="chip"/> jumping to
        /// <see cref="ChipEnd"/>.
        /// </summary>
        internal static void PlayMove(InfoDemoTimelineBuilder builder, InfoDemoProgressChip chip, int target)
        {
            builder.AddPiece(
                CornerShape, InfoDemoPaint.BLOCK_2,
                InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            Vector2 barSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int bar = builder.AddPiece(BarShape, InfoDemoPaint.BLOCK_4, barSlot, InfoDemoLayout.TRAY_PIECE_SCALE);
            builder.AddPiece(
                DominoShape, InfoDemoPaint.BLOCK_5,
                InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);

            // 1. The 1x3 completes rows 5-7 at once; they clear together.
            InfoDemoChoreography.PlacePiece(builder, bar, BarShape, InfoDemoPaint.BLOCK_4, barSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            for (int row = LAND_ROW; row < LAND_ROW + CLEARED_LINE_COUNT; row++)
            {
                InfoDemoChoreography.ClearRow(builder, row, CLEAR_START);
            }

            // 2. The points it scored, and the chip reading the run score — clamped at the target.
            InfoDemoChoreography.FloatText(
                builder, "+" + Gain.ToString(CultureInfo.InvariantCulture), new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 6f),
                1.2f, InfoDemoPaint.INK, 0.8f, GAIN_TEXT_START, 1.3f);
            InfoDemoChoreography.AdvanceChipTo(builder, chip, ChipEnd(target), CHIP_ADVANCE_TIME);
        }
    }
}
