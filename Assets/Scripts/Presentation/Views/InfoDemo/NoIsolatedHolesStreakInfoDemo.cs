using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.NoIsolatedHolesStreak"/> objective card's demo (issue #453, mockup
    /// artboard "Delik yok serisi"), authored from the real rule (<see cref="Board.HasIsolatedEmptyCells"/>,
    /// <see cref="ObjectiveProgress"/>): EVERY placement — clearing or not — extends the streak when, after its
    /// clears, every empty cell can still be reached from the board's edge through empty cells; one that walls
    /// a cell off resets it to 0. Progress is the best streak reached. Each of the demo's three placements
    /// keeps every gap open to the edge, so the chip moves on with each one.
    /// <para>
    /// The chip shows the objective's own target (<see cref="Build(int)"/>): the three placements take it
    /// from <c>target - 3</c> to <c>target</c> (from 0, and only as far as the target, when that is under 3) —
    /// so it reads "2/5 → 5/5" for a target of 5, never a count the objective doesn't ask for.
    /// </para>
    /// <para>
    /// Choreography (5.6 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-4 ........
    /// row5 ......g.
    /// row6 gg....g.
    /// row7 ggb.....
    /// </code>
    /// The strip holds the progress chip ("NO HOLES") on the left and three tray pieces on the right: a 2x2
    /// (purple), a vertical 1x2 (pink), a horizontal 1x2 (blue).
    /// 0.4 s the 2x2 drops onto (6,3)-(7,4) · 1.65 s the vertical 1x2 onto (6,5)-(7,5) · 2.9 s the horizontal
    /// 1x2 onto (5,3)-(5,4) — the gap at (6,2) stays open to the top · the chip ticks with each landing ·
    /// 3.8 s "No holes!" · hold, fade out from 5.2 s, loop. Nothing clears.
    /// </para>
    /// </summary>
    internal static class NoIsolatedHolesStreakInfoDemo
    {
        internal const float LOOP_DURATION = 5.6f;

        /// <summary>How many placements the demo plays, each one extending the streak.</summary>
        internal const int PLACEMENT_COUNT = 3;

        /// <summary>The largest target the chip can print without crowding it.</summary>
        internal const int MAX_TARGET = 999;

        internal const float FIRST_PLACE_START = 0.4f;
        internal const float PLACE_INTERVAL = 1.25f;
        internal const float CHIP_DELAY = 0.1f;
        internal const float LABEL_START = 3.8f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "........",
            "........",
            "......g.",
            "gg....g.",
            "ggb.....",
        };

        internal static readonly Vector2Int[] SquareShape =
            { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };

        internal static readonly Vector2Int[] VerticalDominoShape = { new Vector2Int(0, 0), new Vector2Int(0, 1) };
        internal static readonly Vector2Int[] HorizontalDominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>Each placement's shape, in play order.</summary>
        internal static readonly Vector2Int[][] Shapes = { SquareShape, VerticalDominoShape, HorizontalDominoShape };

        /// <summary>Each placement's top-left landing cell (x = column, y = row), in play order.</summary>
        internal static readonly Vector2Int[] LandCells = { new Vector2Int(3, 6), new Vector2Int(5, 6), new Vector2Int(3, 5) };

        private static readonly int[] Paints = { InfoDemoPaint.BLOCK_3, InfoDemoPaint.BLOCK_1, InfoDemoPaint.BLOCK_5 };

        /// <summary>True when a chip can honestly show <paramref name="target"/>.</summary>
        internal static bool Supports(int target) => target >= 1 && target <= MAX_TARGET;

        /// <summary>The chip's value at loop start for <paramref name="target"/>.</summary>
        internal static int ChipStart(int target) => Mathf.Max(0, target - PLACEMENT_COUNT);

        /// <summary>When placement <paramref name="placementIndex"/> lifts off the tray.</summary>
        internal static float PlaceStart(int placementIndex) => FIRST_PLACE_START + (placementIndex * PLACE_INTERVAL);

        /// <summary>When placement <paramref name="placementIndex"/> lands.</summary>
        internal static float LandTime(int placementIndex)
            => PlaceStart(placementIndex) + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION;

        internal static InfoDemoTimeline Build(int target) => Build(target, out _);

        /// <summary>As <see cref="Build(int)"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(int target, out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            chip = InfoDemoChoreography.ProgressChip(
                builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_NO_HOLES, null, ChipStart(target), target);

            int[] pieces = new int[PLACEMENT_COUNT];
            Vector2[] slots = new Vector2[PLACEMENT_COUNT];
            for (int placementIndex = 0; placementIndex < PLACEMENT_COUNT; placementIndex++)
            {
                slots[placementIndex] = InfoDemoLayout.TraySlot(placementIndex, InfoDemoTrayLayout.ChipLeft);
                pieces[placementIndex] = builder.AddPiece(
                    Shapes[placementIndex], Paints[placementIndex], slots[placementIndex], InfoDemoLayout.TRAY_PIECE_SCALE);
            }

            // Three placements, none of which walls an empty cell off: the streak grows with each.
            for (int placementIndex = 0; placementIndex < PLACEMENT_COUNT; placementIndex++)
            {
                Vector2Int land = LandCells[placementIndex];
                InfoDemoChoreography.PlacePiece(
                    builder, pieces[placementIndex], Shapes[placementIndex], Paints[placementIndex], slots[placementIndex],
                    land.y, land.x, PlaceStart(placementIndex));
                InfoDemoChoreography.AdvanceChip(builder, chip, LandTime(placementIndex) + CHIP_DELAY);
            }

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_NO_HOLES, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 3.4f),
                1.0f, InfoDemoPaint.INK, LABEL_START, 1.3f);

            return builder.Build();
        }
    }
}
