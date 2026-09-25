using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.RollingLineClearWindow"/> objective card's demo (issue #452, mockup
    /// artboard "Zaman penceresi"), authored from the real rule (<see cref="ObjectiveProgress"/>): every
    /// placement that clears lines is recorded with its time, entries older than
    /// <see cref="ObjectiveDefinition.WindowSeconds"/> drop out, and progress is the sum of the lines still
    /// inside that trailing window — so quick clears add up. The chip's caption is the objective's own
    /// window, in whole seconds as the card text rounds it.
    /// <para>
    /// Choreography (6.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row4 ...g....
    /// row5 ppgg.bbu     missing (5,4)
    /// row6 uubbg.gp     missing (6,5)
    /// row7 gbbuup.p     missing (7,6)
    /// </code>
    /// The strip holds the progress chip ("15 SEC", 0/3, a draining window bar along its foot) on the left
    /// and three singles (magenta) on the right. 0.35 s the first single glides to (7,6) · ~1.05 s it lands
    /// and row 7 clears, the window bar appears and starts draining, the chip ticks 0/3 → 1/3 · 1.55 s the
    /// second single to (6,5), row 6 clears, 2/3 · 2.75 s the third to (5,4), row 5 clears, 3/3 and the
    /// green check pops in while the bar still has time left · hold, fade out from 5.7 s, loop.
    /// </para>
    /// <para>
    /// The counter is illustrative — three lines — whatever the objective's own target (the card's HUD
    /// row shows that); the demo's three clears land 2.4 s apart end to end, so it is truthful for any
    /// window of <see cref="MIN_WINDOW_SECONDS"/> or more. The authoring default and the debug cheat use a
    /// 15 s window; no level in the catalog uses this type yet. A shorter window gets no demo and the card
    /// keeps its glyph.
    /// </para>
    /// </summary>
    internal static class RollingLineClearWindowInfoDemo
    {
        internal const float LOOP_DURATION = 6.0f;

        /// <summary>The shortest window (whole seconds) the demo's three clears all fit inside.</summary>
        internal const int MIN_WINDOW_SECONDS = 3;

        /// <summary>The longest window the caption is drawn for — far beyond any sensible level.</summary>
        internal const int MAX_WINDOW_SECONDS = 999;

        /// <summary>How many singles drop, one line each — the chip's illustrative target.</summary>
        internal const int CLEAR_COUNT = 3;

        /// <summary>When each single starts to lift, in drop order.</summary>
        internal static readonly float[] PlaceStarts = { 0.35f, 1.55f, 2.75f };

        /// <summary>Where each single lands (x = column, y = row), in drop order: the one gap of rows 7, 6, 5.</summary>
        internal static readonly Vector2Int[] LandCells = { new Vector2Int(6, 7), new Vector2Int(5, 6), new Vector2Int(4, 5) };

        /// <summary>How long after a single lands its row starts to clear, and after that the chip ticks.</summary>
        internal const float CLEAR_DELAY = 0.12f;
        internal const float CHIP_DELAY = 0.3f;

        /// <summary>When the window bar has drained completely — just before the loop fades out.</summary>
        internal const float WINDOW_BAR_DRAIN_END = 5.6f;

        /// <summary>Mockup-unit geometry of the window bar along the chip's foot: its offset below the chip
        /// centre, its inset from the chip's sides and its thickness.</summary>
        private const float MOCK_WINDOW_BAR_OFFSET_Y = 19.5f;
        private const float MOCK_WINDOW_BAR_SIDE_INSET = 6f;
        private const float MOCK_WINDOW_BAR_THICKNESS = 3f;
        private const float WINDOW_BAR_TRACK_ALPHA = 0.15f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "........",
            "...g....",
            "ppgg.bbu",
            "uubbg.gp",
            "gbbuup.p",
        };

        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };

        /// <summary><paramref name="windowSeconds"/> as the whole seconds the card text shows
        /// (<c>ObjectiveDescriptionFormatter</c> rounds the same way).</summary>
        internal static int WholeSeconds(float windowSeconds) => (int)System.Math.Round(windowSeconds);

        /// <summary>Whether a demo exists for a <paramref name="windowSeconds"/>-wide window.</summary>
        internal static bool Supports(float windowSeconds)
        {
            int seconds = WholeSeconds(windowSeconds);
            return seconds >= MIN_WINDOW_SECONDS && seconds <= MAX_WINDOW_SECONDS;
        }

        /// <summary>The loop time single <paramref name="dropIndex"/> lands.</summary>
        internal static float LandTime(int dropIndex)
            => PlaceStarts[dropIndex] + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION;

        /// <summary>The loop time the row single <paramref name="dropIndex"/> completes starts to clear.</summary>
        internal static float ClearTime(int dropIndex) => LandTime(dropIndex) + CLEAR_DELAY;

        internal static InfoDemoTimeline Build(float windowSeconds) => Build(windowSeconds, out _);

        /// <summary>As <see cref="Build(float)"/>, handing back the chip so tests can read its state.</summary>
        internal static InfoDemoTimeline Build(float windowSeconds, out InfoDemoProgressChip chip)
        {
            string secondsText = WholeSeconds(windowSeconds).ToString(CultureInfo.InvariantCulture);

            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            // Strip: the goal chip ("15 SEC", 0/3) on the left, three singles to its right.
            chip = InfoDemoChoreography.ProgressChip(
                builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_WINDOW, secondsText, 0, CLEAR_COUNT);

            int[] singles = new int[CLEAR_COUNT];
            for (int dropIndex = 0; dropIndex < CLEAR_COUNT; dropIndex++)
            {
                singles[dropIndex] = builder.AddPiece(
                    SingleShape, InfoDemoPaint.BLOCK_4,
                    InfoDemoLayout.TraySlot(dropIndex, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            }

            // The window opens with the first clear and drains while the others land inside it.
            Vector2 chipSize = InfoDemoChoreography.ChipSize;
            InfoDemoHudChoreography.CountdownBar(
                builder,
                InfoDemoLayout.ChipCentre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_WINDOW_BAR_OFFSET_Y)),
                chipSize.x - InfoDemoLayout.FromMockLength(MOCK_WINDOW_BAR_SIDE_INSET * 2f),
                InfoDemoLayout.FromMockLength(MOCK_WINDOW_BAR_THICKNESS),
                InfoDemoPaint.ACCENT,
                InfoDemoPaint.INK,
                WINDOW_BAR_TRACK_ALPHA,
                ClearTime(0),
                ClearTime(0),
                WINDOW_BAR_DRAIN_END);

            // Three quick singles, each completing one row: every clear lands inside the window and adds up.
            for (int dropIndex = 0; dropIndex < CLEAR_COUNT; dropIndex++)
            {
                Vector2Int cell = LandCells[dropIndex];
                InfoDemoChoreography.PlacePiece(
                    builder, singles[dropIndex], SingleShape, InfoDemoPaint.BLOCK_4,
                    InfoDemoLayout.TraySlot(dropIndex, InfoDemoTrayLayout.ChipLeft), cell.y, cell.x, PlaceStarts[dropIndex]);

                float clearTime = ClearTime(dropIndex);
                InfoDemoChoreography.ClearRow(builder, cell.y, clearTime);
                InfoDemoChoreography.AdvanceChip(builder, chip, clearTime + CHIP_DELAY);
            }

            return builder.Build();
        }
    }
}
