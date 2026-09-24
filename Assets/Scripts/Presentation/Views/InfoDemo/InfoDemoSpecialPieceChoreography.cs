using System.Globalization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Special dock piece beats for the info demos (issue #451), on top of
    /// <see cref="InfoDemoChoreography"/>: a tray piece dragged onto the board under a demo finger, a dock
    /// piece armed by a tap (lifted, as <c>PieceTrayView.SetAimedSlot</c> lifts the real plate, inside the
    /// demos' yellow "armed" ring), and the HUD's combo streak pill counting up. Every helper only queues
    /// steps on an <see cref="InfoDemoTimelineBuilder"/>; nothing here runs at play time.
    /// </summary>
    internal static class InfoDemoSpecialPieceChoreography
    {
        /// <summary>How far below the piece the dragging finger touches, in board units — the real drag
        /// ghost rides above the finger, so the finger never hides the piece it carries.</summary>
        internal const float DRAG_FINGER_DROP = 0.75f;

        /// <summary>The tray scale a special single is drawn at: larger than an ordinary tray piece so
        /// its gold fill or dock mark reads at demo size.</summary>
        internal const float SPECIAL_SINGLE_SCALE = 0.62f;

        /// <summary>How much an armed dock piece grows. Deliberately more than the real plate's 6% lift
        /// (<c>PieceTrayView._aimedSlotScale</c>), which would not read at demo size.</summary>
        internal const float ARMED_LIFT = 1.18f;

        private const float ARM_DURATION = 0.18f;
        private const float ARMED_RING_STROKE = 0.1f;

        /// <summary>Mockup-unit geometry of the streak pill: its size, the flame's size and its offset
        /// from the pill centre, the counter's offset and font height.</summary>
        private const float MOCK_PILL_WIDTH = 58f;
        private const float MOCK_PILL_HEIGHT = 28f;
        private const float MOCK_PILL_SHADOW_DROP = 2f;
        private const float MOCK_FLAME_SIZE = 16f;
        private const float MOCK_FLAME_OFFSET_X = -14f;
        private const float MOCK_COUNTER_OFFSET_X = 9f;
        private const float MOCK_COUNTER_WIDTH = 32f;
        private const float MOCK_COUNTER_FONT = 16f;
        private const float PILL_SHADOW_ALPHA = 0.14f;

        /// <summary>
        /// Drags piece <paramref name="pieceId"/> (resting in the tray at <paramref name="trayPosition"/>)
        /// onto the board with its top-left cell at (<paramref name="row"/>, <paramref name="column"/>),
        /// exactly as <see cref="InfoDemoChoreography.PlacePiece"/> drops one — the landed blocks in
        /// <paramref name="boardPaint"/> — while a demo finger touches down just below it and travels with
        /// it on the same path and easing. <paramref name="restScale"/> is the piece's resting tray scale.
        /// Returns the landing time.
        /// </summary>
        internal static float DragPiece(
            InfoDemoTimelineBuilder builder,
            int pieceId,
            Vector2Int[] shape,
            int boardPaint,
            Vector2 trayPosition,
            int row,
            int column,
            float startTime,
            float restScale = InfoDemoLayout.TRAY_PIECE_SCALE)
        {
            Vector2 fingerDrop = new Vector2(0f, DRAG_FINGER_DROP);
            Vector2 target = InfoDemoLayout.PieceCentre(shape, row, column);
            float moveStart = startTime + InfoDemoChoreography.LIFT_DURATION;
            InfoDemoChoreography.Drag(
                builder, startTime, trayPosition + fingerDrop, target + fingerDrop, moveStart,
                InfoDemoChoreography.GLIDE_DURATION);

            return InfoDemoChoreography.PlacePiece(
                builder, pieceId, shape, boardPaint, trayPosition, row, column, startTime, restScale);
        }

        /// <summary>
        /// A tap arms dock piece <paramref name="pieceId"/> at <paramref name="time"/>: it lifts from
        /// <paramref name="restScale"/> by <see cref="ARMED_LIFT"/> and a yellow
        /// (<see cref="InfoDemoPaint.ARMED"/>) ring <paramref name="ringSize"/> board units across fades in
        /// round it, holding until <paramref name="disarmTime"/>, when the ring fades. The piece stays
        /// lifted; the caller decides what becomes of it. Returns the ring's element id.
        /// </summary>
        internal static int ArmPiece(
            InfoDemoTimelineBuilder builder,
            int pieceId,
            Vector2 centre,
            float restScale,
            float ringSize,
            float time,
            float disarmTime)
        {
            InfoDemoChoreography.Tap(builder, time, centre);
            builder.Scale(pieceId, time, ARM_DURATION, restScale, restScale * ARMED_LIFT, InfoDemoEasing.EaseOutBack);

            int ring = builder.AddRing(centre, new Vector2(ringSize, ringSize), ringSize * 0.3f, ARMED_RING_STROKE, InfoDemoPaint.ARMED);
            builder.Fade(ring, time, 0.12f, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Scale(ring, time, ARM_DURATION + 0.1f, 1.25f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Fade(ring, disarmTime, 0.2f, 1f, 0f, InfoDemoEasing.EaseInCubic);
            builder.BringToFront(ring);
            return ring;
        }

        /// <summary>
        /// The HUD's combo streak pill (<c>StreakPillView</c>) in the tray strip's left zone
        /// (<see cref="InfoDemoLayout.ChipCentre"/>): a pill in the theme's first block colour — the colour
        /// the real pill takes — with the HUD's own white flame and a white "x<paramref name="fromStreak"/>"
        /// counter, which <see cref="InfoDemoHudChoreography.AdvanceCounter"/> moves on one step at a time
        /// up to "x<paramref name="toStreak"/>". Present from loop time 0. Lay the tray out with
        /// <see cref="InfoDemoTrayLayout.ChipLeft"/> so no piece sits under it.
        /// </summary>
        internal static InfoDemoCounter StreakPill(InfoDemoTimelineBuilder builder, int fromStreak, int toStreak)
        {
            Vector2 centre = InfoDemoLayout.ChipCentre;
            Vector2 size = new Vector2(
                InfoDemoLayout.FromMockLength(MOCK_PILL_WIDTH), InfoDemoLayout.FromMockLength(MOCK_PILL_HEIGHT));
            float corner = size.y * 0.5f;

            builder.AddPanel(
                centre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_PILL_SHADOW_DROP)),
                size, corner, InfoDemoPaint.INK, PILL_SHADOW_ALPHA);
            builder.AddPanel(centre, size, corner, InfoDemoPaint.BLOCK_1);

            // Icon sizes are in board-cell widths (MOCK_CELL), not pitches.
            builder.AddIcon(
                InfoDemoSprite.StreakFlame, 0,
                centre + new Vector2(InfoDemoLayout.FromMockLength(MOCK_FLAME_OFFSET_X), 0f),
                MOCK_FLAME_SIZE / InfoDemoLayout.MOCK_CELL);

            int valueCount = Mathf.Max(1, toStreak - fromStreak + 1);
            string[] values = new string[valueCount];
            for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
            {
                values[valueIndex] = "x" + (fromStreak + valueIndex).ToString(CultureInfo.InvariantCulture);
            }

            return InfoDemoHudChoreography.Counter(
                builder,
                values,
                centre + new Vector2(InfoDemoLayout.FromMockLength(MOCK_COUNTER_OFFSET_X), 0f),
                InfoDemoLayout.FromMockLength(MOCK_COUNTER_WIDTH),
                InfoDemoLayout.FromMockLength(MOCK_COUNTER_FONT),
                InfoDemoPaint.WHITE);
        }
    }
}
