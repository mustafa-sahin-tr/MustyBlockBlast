using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The tray/dock beats of the info demos (issue #449), on top of <see cref="InfoDemoChoreography"/>:
    /// a dock piece turning 90° about its own centre and becoming the rotated shape, a piece sliding
    /// between the tray and the Hold pocket, the whole dock being discarded and dealt again, a piece
    /// pulsing as a hint, a red "doesn't fit" mark on a piece and a highlight ring round one. Every helper
    /// only queues steps on an <see cref="InfoDemoTimelineBuilder"/>; nothing here runs at play time.
    /// </summary>
    internal static class InfoDemoTrayChoreography
    {
        /// <summary>How long a piece dropping out of the dock takes to fall and fade.</summary>
        internal const float DISCARD_DURATION = 0.32f;

        /// <summary>How long a freshly dealt piece takes to pop in.</summary>
        internal const float DEAL_DURATION = 0.32f;

        /// <summary>How long the rotated shape's arrival pop lasts once it takes over from the turning one.</summary>
        internal const float ROTATE_SWAP_POP_DURATION = 0.22f;

        private const float DISCARD_DROP = 0.8f;
        private const float DISCARD_END_SCALE_FRACTION = 0.6f;
        private const float DEAL_START_SCALE_FRACTION = 0.2f;
        private const float ROTATE_SWAP_POP_FRACTION = 1.12f;

        /// <summary>Mockup-unit geometry of a "doesn't fit" mark: a red disc with a white rim and a white
        /// cross.</summary>
        private const float MOCK_REJECT_DIAMETER = 14f;
        private const float MOCK_REJECT_RIM = 1.5f;
        private const float MOCK_REJECT_BAR_LENGTH = 8f;
        private const float MOCK_REJECT_BAR_THICKNESS = 2f;
        private const float REJECT_POP_DURATION = 0.28f;
        private const float REJECT_FADE_DURATION = 0.2f;

        /// <summary>
        /// Turns piece <paramref name="pieceId"/> 90° clockwise about its own centre over
        /// [<paramref name="startTime"/>, + <paramref name="duration"/>], then at
        /// <paramref name="swapTime"/> hands over to <paramref name="rotatedPieceId"/> — the rotated shape
        /// as its own (hidden) piece at the same spot and scale, just as the real dock slot swaps to the
        /// catalog piece of that orientation (<c>PieceRotator.TryRotateClockwise</c>) — which pops slightly.
        /// Both pieces are centred on their bounding box, so the turned piece and the rotated shape line
        /// up exactly. Returns <paramref name="swapTime"/>.
        /// </summary>
        internal static float RotatePiece(
            InfoDemoTimelineBuilder builder,
            int pieceId,
            int rotatedPieceId,
            float restScale,
            float startTime,
            float duration,
            float swapTime)
        {
            // UI rotation is counter-clockwise-positive, so a clockwise quarter turn is -90.
            builder.Rotate(pieceId, startTime, duration, 0f, -90f, InfoDemoEasing.EaseInOutCubic);

            builder.Fade(pieceId, swapTime, 0f, 0f, 0f);
            builder.Fade(rotatedPieceId, swapTime, 0f, 1f, 1f);
            builder.Scale(
                rotatedPieceId, swapTime, ROTATE_SWAP_POP_DURATION, restScale, restScale * ROTATE_SWAP_POP_FRACTION,
                InfoDemoEasing.Pulse);
            return swapTime;
        }

        /// <summary>Slides piece <paramref name="pieceId"/> from <paramref name="from"/> to
        /// <paramref name="to"/>, rescaling <paramref name="fromScale"/> → <paramref name="toScale"/> on the
        /// way — a piece parked into the Hold pocket, or one swapped back out of it. Returns the arrival
        /// time.</summary>
        internal static float SlidePiece(
            InfoDemoTimelineBuilder builder,
            int pieceId,
            Vector2 from,
            Vector2 to,
            float fromScale,
            float toScale,
            float startTime,
            float duration)
        {
            builder.Move(pieceId, startTime, duration, from, to, InfoDemoEasing.EaseInOutCubic);
            builder.Scale(pieceId, startTime, duration, fromScale, toScale, InfoDemoEasing.EaseInOutCubic);
            return startTime + duration;
        }

        /// <summary>Piece <paramref name="pieceId"/>, resting at <paramref name="position"/> and
        /// <paramref name="restScale"/>, drops out of the dock at <paramref name="time"/>: falls a little,
        /// shrinks and fades (a Reroll discarding the whole dock). Returns the time it is gone.</summary>
        internal static float DiscardPiece(
            InfoDemoTimelineBuilder builder, int pieceId, Vector2 position, float restScale, float time)
        {
            builder.Move(pieceId, time, DISCARD_DURATION, position, position + new Vector2(0f, DISCARD_DROP), InfoDemoEasing.EaseInCubic);
            builder.Scale(
                pieceId, time, DISCARD_DURATION, restScale, restScale * DISCARD_END_SCALE_FRACTION, InfoDemoEasing.EaseInCubic);
            builder.Fade(pieceId, time, DISCARD_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            return time + DISCARD_DURATION;
        }

        /// <summary>A hidden piece (added with alpha 0) pops into its dock slot at <paramref name="time"/>,
        /// growing to <paramref name="restScale"/> with a small overshoot — a freshly drawn piece. Returns the
        /// time it settles.</summary>
        internal static float DealPiece(InfoDemoTimelineBuilder builder, int pieceId, float restScale, float time)
        {
            builder.Fade(pieceId, time, 0.1f, 0f, 1f);
            builder.Scale(
                pieceId, time, DEAL_DURATION, restScale * DEAL_START_SCALE_FRACTION, restScale, InfoDemoEasing.EaseOutBack);
            return time + DEAL_DURATION;
        }

        /// <summary>Swells piece <paramref name="pieceId"/> from <paramref name="restScale"/> to
        /// <paramref name="peakScale"/> and back, once every <paramref name="period"/> seconds from
        /// <paramref name="startTime"/> until <paramref name="endTime"/> — a hint pointing at it (the real
        /// Ghost Fit dock pulse, <c>PieceTrayView.SetHintedSlot</c>).</summary>
        internal static void PulsePiece(
            InfoDemoTimelineBuilder builder, int pieceId, float restScale, float peakScale, float startTime, float endTime, float period)
        {
            for (float pulseStart = startTime; pulseStart + (period * 0.5f) <= endTime; pulseStart += period)
            {
                float pulseDuration = Mathf.Min(period, endTime - pulseStart);
                builder.Scale(pieceId, pulseStart, pulseDuration, restScale, peakScale, InfoDemoEasing.Pulse);
            }
        }

        /// <summary>
        /// A small red "doesn't fit" mark — a white-rimmed red disc with a white cross — popping in at
        /// <paramref name="centre"/> at <paramref name="time"/> and fading at <paramref name="hideTime"/>.
        /// Drawn over the tray pieces (issue #449, Reroll: nothing in the dock fits).
        /// </summary>
        internal static void RejectMark(InfoDemoTimelineBuilder builder, Vector2 centre, float time, float hideTime)
        {
            float diameter = InfoDemoLayout.FromMockLength(MOCK_REJECT_DIAMETER);
            float rimDiameter = InfoDemoLayout.FromMockLength(MOCK_REJECT_DIAMETER + (MOCK_REJECT_RIM * 2f));
            Vector2 barSize = new Vector2(
                InfoDemoLayout.FromMockLength(MOCK_REJECT_BAR_LENGTH), InfoDemoLayout.FromMockLength(MOCK_REJECT_BAR_THICKNESS));

            int rim = builder.AddPanel(centre, new Vector2(rimDiameter, rimDiameter), rimDiameter * 0.5f, InfoDemoPaint.WHITE, 0f);
            int disc = builder.AddPanel(centre, new Vector2(diameter, diameter), diameter * 0.5f, InfoDemoPaint.BADGE_RED, 0f);
            int barA = builder.AddPanel(centre, barSize, barSize.y * 0.5f, InfoDemoPaint.WHITE, 0f);
            int barB = builder.AddPanel(centre, barSize, barSize.y * 0.5f, InfoDemoPaint.WHITE, 0f);
            builder.Rotate(barA, 0f, 0f, 45f, 45f);
            builder.Rotate(barB, 0f, 0f, -45f, -45f);

            int[] parts = { rim, disc, barA, barB };
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                int part = parts[partIndex];
                builder.BringToFront(part);
                builder.Fade(part, time, 0.1f, 0f, 1f);
                builder.Scale(part, time, REJECT_POP_DURATION, 0.3f, 1f, InfoDemoEasing.EaseOutBack);
                builder.Fade(part, hideTime, REJECT_FADE_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            }
        }

        /// <summary>
        /// A rounded <paramref name="paint"/> ring <paramref name="size"/> board units across around a tray
        /// piece at <paramref name="centre"/>: fades in at <paramref name="startTime"/>, breathes
        /// <paramref name="pulseCount"/> times and fades at <paramref name="endTime"/> — "this one" (a
        /// Reroll's newly drawn piece that fits, the Ghost Fit hint ring). Returns the ring's element id.
        /// </summary>
        internal static int HighlightRing(
            InfoDemoTimelineBuilder builder,
            Vector2 centre,
            Vector2 size,
            float strokeWidth,
            int paint,
            float startTime,
            float endTime,
            int pulseCount)
        {
            const float fadeDuration = 0.15f;

            int ring = builder.AddRing(centre, size, Mathf.Min(size.x, size.y) * 0.3f, strokeWidth, paint);
            builder.Fade(ring, startTime, fadeDuration, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Scale(ring, startTime, fadeDuration + 0.1f, 1.2f, 1f, InfoDemoEasing.EaseOutCubic);

            int pulses = Mathf.Max(0, pulseCount);
            if (pulses > 0)
            {
                float pulseStart = startTime + fadeDuration + 0.1f;
                float pulseDuration = Mathf.Max(0.01f, (endTime - pulseStart) / pulses);
                for (int pulseIndex = 0; pulseIndex < pulses; pulseIndex++)
                {
                    builder.Scale(ring, pulseStart + (pulseIndex * pulseDuration), pulseDuration, 1f, 1.08f, InfoDemoEasing.Pulse);
                }
            }

            builder.Fade(ring, endTime, 0.2f, 1f, 0f, InfoDemoEasing.EaseInCubic);
            return ring;
        }
    }
}
