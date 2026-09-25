using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.StreakThreshold"/> objective card's demo (issue #454, mockup artboard
    /// "Kombo"), authored from the real rule (<see cref="ObjectiveProgress"/>, <c>ScoreSystem</c>): the combo
    /// streak grows by one with every placement that clears a line and drops to 0 on one that clears nothing,
    /// and progress is the best streak reached. The HUD writes the streak "x1", "x2", ... on its pill
    /// (<c>StreakPillView</c>, <see cref="ScoreRules.ComboStreakBonus"/>); the demo floats the same reading
    /// over each clear.
    /// <para>
    /// Built for the objective's own target <c>T</c> (1 to <see cref="MAX_TARGET"/>): the bottom <c>T</c> rows
    /// are each one cell short, and <c>T</c> singles fill them one after another from the bottom up, every one
    /// clearing its row — so the chip (the HUD's flame over "0/T") ticks once per clear up to "T/T". A target
    /// past five would need more placements than the loop has room for, and gets the static glyph instead.
    /// </para>
    /// <para>
    /// Choreography for T = 3 (6.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-3 ........
    /// row4 ...g....
    /// row5 ppgg.bbu
    /// row6 uubbg.gp
    /// row7 gbbuup.p
    /// </code>
    /// The strip holds the chip on the left and three singles on the right. 0.35 s a single drops into (7,6)
    /// and row 7 clears · "x1" · the chip ticks · 1.35 s the next fills (6,5), row 6 clears · "x2" · 2.35 s the
    /// last fills (5,4), row 5 clears · "x3" · the chip reaches 3/3 with the check · "Combo x3!" · hold, fade
    /// out, loop. For T = 4 and 5 the singles come 0.85 s apart (the loop stretching to at most 6.5 s), and
    /// the dock refills with three new singles once the first three are played, as the real dock does.
    /// </para>
    /// </summary>
    internal static class StreakThresholdInfoDemo
    {
        internal const int MIN_TARGET = 1;

        /// <summary>The longest streak the loop has room to play out.</summary>
        internal const int MAX_TARGET = 5;

        internal const float FIRST_PLACE_START = 0.35f;
        internal const float CLEAR_DELAY = 0.12f;
        internal const float CHIP_DELAY = 0.3f;
        internal const float STREAK_TEXT_DELAY = 0.35f;
        internal const float LABEL_DELAY = 0.45f;
        internal const float REFILL_DELAY = 0.3f;
        internal const float REFILL_STAGGER = 0.08f;

        private const float SHORT_LOOP_DURATION = 6.0f;
        private const float LABEL_HOLD = 1.4f;
        private const int SHORT_STREAK = 3;
        private const float SHORT_INTERVAL = 1.0f;
        private const float LONG_INTERVAL = 0.85f;

        /// <summary>Each row's fill, bottom (row 7) upward, with its one gap — the gaps stepping one column left
        /// per row, so no column ever fills.</summary>
        private static readonly string[] BandRowsBottomUp = { "gbbuup.p", "uubbg.gp", "ppgg.bbu", "bgu.pugg", "gp.ubbgu" };

        /// <summary>A little loose decoration on the row above the band.</summary>
        private const string DECORATION_ROW = "...g....";

        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };

        internal static bool Supports(int target) => target >= MIN_TARGET && target <= MAX_TARGET;

        /// <summary>The gap single <paramref name="placementIndex"/> fills (x = column, y = row): the bottom row's
        /// first, then upward.</summary>
        internal static Vector2Int LandCell(int placementIndex)
        {
            int row = InfoDemoLayout.BOARD_SIZE - 1 - placementIndex;
            return new Vector2Int(BandRowsBottomUp[placementIndex].IndexOf('.'), row);
        }

        /// <summary>The demo's starting board for a streak of <paramref name="target"/>, as eight pattern rows.</summary>
        internal static string[] Rows(int target)
        {
            string[] rows = new string[InfoDemoLayout.BOARD_SIZE];
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                rows[row] = "........";
            }

            for (int bandIndex = 0; bandIndex < target; bandIndex++)
            {
                rows[InfoDemoLayout.BOARD_SIZE - 1 - bandIndex] = BandRowsBottomUp[bandIndex];
            }

            int decorationRow = InfoDemoLayout.BOARD_SIZE - 1 - target;
            if (decorationRow >= 0)
            {
                rows[decorationRow] = DECORATION_ROW;
            }

            return rows;
        }

        /// <summary>Seconds between one single lifting and the next.</summary>
        internal static float Interval(int target) => target <= SHORT_STREAK ? SHORT_INTERVAL : LONG_INTERVAL;

        internal static float PlaceStart(int target, int placementIndex) => FIRST_PLACE_START + (placementIndex * Interval(target));

        internal static float LandTime(int target, int placementIndex)
            => PlaceStart(target, placementIndex) + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION;

        internal static float ClearTime(int target, int placementIndex) => LandTime(target, placementIndex) + CLEAR_DELAY;

        internal static float LabelStart(int target) => ClearTime(target, target - 1) + LABEL_DELAY;

        internal static float LoopDuration(int target) => Mathf.Max(SHORT_LOOP_DURATION, LabelStart(target) + LABEL_HOLD);

        /// <summary>Where the closing label starts: well above the last "xN", which is still floating up from the
        /// top cleared row, but never above row 1.2 so it stays on the board as it rises.</summary>
        internal static float LabelRow(int target) => Mathf.Max(1.2f, InfoDemoLayout.BOARD_SIZE - 1 - target - 1.8f);

        internal static InfoDemoTimeline Build(int target) => Build(target, out _);

        /// <summary>As <see cref="Build(int)"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(int target, out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LoopDuration(target));
            InfoDemoBoardPattern.Apply(builder, Rows(target));

            // Strip: the goal chip — the HUD's flame in the streak pill's colour — on the left, the dock to its right.
            chip = InfoDemoChoreography.ProgressChip(builder, InfoDemoSprite.StreakFlame, 0, InfoDemoPaint.BLOCK_1, 0, target);

            int[] singles = new int[target];
            for (int placementIndex = 0; placementIndex < target; placementIndex++)
            {
                int slotIndex = placementIndex % InfoDemoLayout.TRAY_SLOT_COUNT;
                bool isRefill = placementIndex >= InfoDemoLayout.TRAY_SLOT_COUNT;
                singles[placementIndex] = builder.AddPiece(
                    SingleShape, InfoDemoPaint.BLOCK_4, InfoDemoLayout.TraySlot(slotIndex, InfoDemoTrayLayout.ChipLeft),
                    InfoDemoLayout.TRAY_PIECE_SCALE, isRefill ? 0f : 1f);
            }

            // The dock runs dry after three placements and is refilled with three new pieces, as the real one is;
            // only the ones the streak still needs are played.
            if (target > InfoDemoLayout.TRAY_SLOT_COUNT)
            {
                float refillTime = LandTime(target, InfoDemoLayout.TRAY_SLOT_COUNT - 1) + REFILL_DELAY;
                for (int slotIndex = 0; slotIndex < InfoDemoLayout.TRAY_SLOT_COUNT; slotIndex++)
                {
                    int dealt = InfoDemoLayout.TRAY_SLOT_COUNT + slotIndex;
                    int pieceId = dealt < target
                        ? singles[dealt]
                        : builder.AddPiece(
                            SingleShape, InfoDemoPaint.BLOCK_4, InfoDemoLayout.TraySlot(slotIndex, InfoDemoTrayLayout.ChipLeft),
                            InfoDemoLayout.TRAY_PIECE_SCALE, 0f);
                    InfoDemoTrayChoreography.DealPiece(
                        builder, pieceId, InfoDemoLayout.TRAY_PIECE_SCALE, refillTime + (slotIndex * REFILL_STAGGER));
                }
            }

            // One clearing placement after another: the streak grows with each, and the chip with it.
            float centreX = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            for (int placementIndex = 0; placementIndex < target; placementIndex++)
            {
                Vector2Int cell = LandCell(placementIndex);
                int slotIndex = placementIndex % InfoDemoLayout.TRAY_SLOT_COUNT;
                InfoDemoChoreography.PlacePiece(
                    builder, singles[placementIndex], SingleShape, InfoDemoPaint.BLOCK_4,
                    InfoDemoLayout.TraySlot(slotIndex, InfoDemoTrayLayout.ChipLeft), cell.y, cell.x,
                    PlaceStart(target, placementIndex));

                float clearTime = ClearTime(target, placementIndex);
                InfoDemoChoreography.ClearRow(builder, cell.y, clearTime);

                int streak = placementIndex + 1;
                InfoDemoChoreography.FloatText(
                    builder, "x" + streak.ToString(CultureInfo.InvariantCulture), new Vector2(centreX, cell.y - 0.3f),
                    0.9f, InfoDemoPaint.BLOCK_1, 0.8f, clearTime + STREAK_TEXT_DELAY, 0.8f);
                InfoDemoChoreography.AdvanceChip(builder, chip, clearTime + CHIP_DELAY);
            }

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_COMBO,
                new Vector2(centreX, LabelRow(target)), 1.0f, InfoDemoPaint.INK,
                LabelStart(target), LABEL_HOLD - 0.1f, target.ToString(CultureInfo.InvariantCulture));

            return builder.Build();
        }
    }
}
