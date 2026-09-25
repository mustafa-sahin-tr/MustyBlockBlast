using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.ClutchRecoveryClear"/> objective card's demo (issue #454, mockup artboard
    /// "Son anda kurtarış"), authored from the real rule (<see cref="ObjectiveProgress"/>, <c>BoardSystem</c>):
    /// a placement counts when it clears at least one line and the board — read the moment the piece has
    /// landed, <em>before</em> its own clears resolve — held at least
    /// <see cref="ObjectiveDefinition.RequiredOccupancyThreshold"/> blocks. A packed board that clears nothing
    /// does not count.
    /// <para>
    /// The board holds <see cref="OccupiedBeforeLanding"/> blocks with no full row or column (a real board
    /// never holds one: a line clears the moment it fills). Every row has a gap and so does every column —
    /// column 6 two, so the single that completes row 7 completes no column. Once it lands the board holds
    /// <see cref="OccupiedAfterLanding"/> blocks, which is what the rule reads, so the demo exists for any
    /// threshold up to that (the levels' authoring default is 52); a higher one gets the static glyph.
    /// </para>
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ggbbu.pp
    /// row1 u.bggup.
    /// row2 gpp.bbgu
    /// row3 bbgguu.g
    /// row4 pugg.bbp
    /// row5 gg.bpupu
    /// row6 .uppggbu
    /// row7 bbuugg.p
    /// </code>
    /// The strip holds the chip ("SAVE") on the left and a tray of an L corner (green), a single (magenta) and
    /// a 1x2 (blue) on the right. 0.1 s a red warning frame pulses around the packed board · 0.45 s the
    /// single lifts from slot 1 · ~1.15 s it lands on (7,6) · 1.3 s row 7 clears and the frame fades · 1.75 s
    /// the chip ticks with the check · 1.9 s "Close call!" on a white pill · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class ClutchRecoveryClearInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int LAND_ROW = 7;
        internal const int LAND_COLUMN = 6;

        internal const float FRAME_START = 0.1f;
        internal const float PLACE_START = 0.45f;
        internal const float CLEAR_START = 1.3f;
        internal const float CHIP_ADVANCE_TIME = 1.75f;
        internal const float PILL_START = 1.9f;
        internal const float PILL_END = 4.3f;

        /// <summary>The largest target the chip can print without crowding it.</summary>
        internal const int MAX_TARGET = 999;

        internal static readonly string[] Rows =
        {
            "ggbbu.pp",
            "u.bggup.",
            "gpp.bbgu",
            "bbgguu.g",
            "pugg.bbp",
            "gg.bpupu",
            ".uppggbu",
            "bbuugg.p",
        };

        /// <summary>Blocks on the board before the single lands.</summary>
        internal static readonly int OccupiedBeforeLanding = CountOccupied(Rows);

        /// <summary>Blocks on the board the moment the single has landed, before row 7 clears — the reading the
        /// real rule compares with the threshold.</summary>
        internal static readonly int OccupiedAfterLanding = OccupiedBeforeLanding + 1;

        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>True when the demo's board honestly reaches <paramref name="threshold"/> and the chip can show
        /// <paramref name="target"/>.</summary>
        internal static bool Supports(int threshold, int target)
            => threshold >= 1 && threshold <= OccupiedAfterLanding && target >= 1 && target <= MAX_TARGET;

        /// <summary>The chip's value at loop start for <paramref name="target"/>: one short of it.</summary>
        internal static int ChipStart(int target) => Mathf.Max(0, target - 1);

        internal static InfoDemoTimeline Build(int target) => Build(target, out _);

        /// <summary>As <see cref="Build(int)"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(int target, out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            chip = InfoDemoChoreography.ProgressChip(
                builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_SAVE, null, ChipStart(target), target);

            builder.AddPiece(
                CornerShape, InfoDemoPaint.BLOCK_2,
                InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, InfoDemoLayout.TRAY_PIECE_SCALE);
            builder.AddPiece(
                DominoShape, InfoDemoPaint.BLOCK_5,
                InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);

            // 1. The board is almost full: a red warning frame pulses around it.
            float centre = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            InfoDemoChoreography.PulseOutline(
                builder, new Vector2(centre, centre), InfoDemoLayout.BOARD_SIZE, InfoDemoLayout.BOARD_SIZE,
                InfoDemoPaint.BADGE_RED, FRAME_START, CLEAR_START, 3);

            // 2. The single lands in row 7's last gap — the board at its fullest — and the row clears.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_4, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);

            // 3. That clear counts: the chip ticks, and the save is called out on a pill.
            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);
            InfoDemoHudChoreography.LabelPill(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_LAST_MOMENT,
                new Vector2(centre, 3.5f),
                new Vector2(4.6f, 1f),
                0.55f,
                InfoDemoPaint.INK,
                PILL_START,
                PILL_END);

            return builder.Build();
        }

        private static int CountOccupied(string[] rows)
        {
            int occupied = 0;
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    if (InfoDemoPaint.FromPatternChar(rows[row][column]) != InfoDemoPaint.NONE)
                    {
                        occupied++;
                    }
                }
            }

            return occupied;
        }
    }
}
