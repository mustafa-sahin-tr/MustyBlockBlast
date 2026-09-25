using System.Globalization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// HUD-furniture beats for the info demos (issue #449), on top of <see cref="InfoDemoChoreography"/>:
    /// a counter that ticks from one literal value to the next, a round charge badge counting down, a
    /// thin countdown bar draining over a window, a sprite flying in an arc from one spot to another (a
    /// coin sown onto a block, a coin flying to the wallet), and a white label pill. Every helper only
    /// queues steps on an <see cref="InfoDemoTimelineBuilder"/>; nothing here runs at play time.
    /// </summary>
    internal static class InfoDemoHudChoreography
    {
        /// <summary>How long a counter crossfades from one value to the next, and how far the new value
        /// swells before it settles.</summary>
        internal const float COUNTER_CROSSFADE_DURATION = 0.22f;
        internal const float COUNTER_POP_SCALE = 1.4f;

        /// <summary>How long a flying sprite fades in at its start and out at its landing.</summary>
        internal const float FLY_FADE_IN_DURATION = 0.08f;
        internal const float FLY_FADE_OUT_DURATION = 0.12f;
        private const float FLY_START_SCALE = 0.7f;
        private const float FLY_APEX_SCALE = 1.15f;

        /// <summary>How long a label pill takes to pop in and to fade away.</summary>
        internal const float PILL_POP_IN_DURATION = 0.28f;
        internal const float PILL_FADE_OUT_DURATION = 0.25f;
        private const float PILL_START_SCALE = 0.6f;
        private const float PILL_SHADOW_ALPHA = 0.14f;
        private const float MOCK_PILL_SHADOW_DROP = 2f;

        /// <summary>
        /// A counter at <paramref name="position"/>: one literal label per entry of
        /// <paramref name="values"/>, all stacked there, the first showing from loop time 0 and the rest
        /// hidden until <see cref="AdvanceCounter"/> brings them in, in order.
        /// </summary>
        internal static InfoDemoCounter Counter(
            InfoDemoTimelineBuilder builder, string[] values, Vector2 position, float width, float fontHeight, int paint)
        {
            int[] labelIds = new int[values.Length];
            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                labelIds[valueIndex] = builder.AddText(
                    values[valueIndex], position, width, fontHeight, paint, valueIndex == 0 ? 1f : 0f);
            }

            return new InfoDemoCounter(labelIds);
        }

        /// <summary>Moves <paramref name="counter"/> on to its next value at <paramref name="time"/>: the
        /// old value fades out as the new one pops in. A counter already on its last value is left alone.
        /// Returns the time the beat settles.</summary>
        internal static float AdvanceCounter(InfoDemoTimelineBuilder builder, InfoDemoCounter counter, float time)
        {
            if (counter.IsAtEnd)
            {
                return time;
            }

            int oldLabel = counter.LabelId(counter.Index);
            counter.Advance();
            int newLabel = counter.LabelId(counter.Index);

            builder.Fade(oldLabel, time, COUNTER_CROSSFADE_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            builder.Fade(newLabel, time, COUNTER_CROSSFADE_DURATION, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Scale(
                newLabel, time, COUNTER_CROSSFADE_DURATION + 0.1f, COUNTER_POP_SCALE, 1f, InfoDemoEasing.EaseOutBack);
            return time + COUNTER_CROSSFADE_DURATION + 0.1f;
        }

        /// <summary>
        /// A round charge badge at <paramref name="centre"/> reading <paramref name="count"/>: a white rim
        /// disc <paramref name="rimWidth"/> wider than a <paramref name="discPaint"/> disc
        /// <paramref name="diameter"/> across (board units), and a white number that
        /// <see cref="TickBadge"/> counts down to 0. With <paramref name="onTop"/> it is drawn over the
        /// tray pieces (a badge hung on a pocket that a piece is parked in).
        /// </summary>
        internal static InfoDemoCountBadge CountBadge(
            InfoDemoTimelineBuilder builder,
            Vector2 centre,
            int count,
            int discPaint,
            float diameter,
            float rimWidth,
            float fontHeight,
            bool onTop = false)
        {
            float rimDiameter = diameter + (rimWidth * 2f);
            int rim = builder.AddPanel(centre, new Vector2(rimDiameter, rimDiameter), rimDiameter * 0.5f, InfoDemoPaint.WHITE);
            int disc = builder.AddPanel(centre, new Vector2(diameter, diameter), diameter * 0.5f, discPaint);

            int valueCount = Mathf.Max(1, count + 1);
            string[] values = new string[valueCount];
            for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
            {
                values[valueIndex] = (count - valueIndex).ToString(CultureInfo.InvariantCulture);
            }

            InfoDemoCounter counter = Counter(builder, values, centre, diameter, fontHeight, InfoDemoPaint.WHITE);

            if (onTop)
            {
                builder.BringToFront(rim);
                builder.BringToFront(disc);
                for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
                {
                    builder.BringToFront(counter.LabelId(valueIndex));
                }
            }

            return new InfoDemoCountBadge(centre, rim, disc, count, counter);
        }

        /// <summary>
        /// <paramref name="badge"/> counts one charge down at <paramref name="time"/>. When that empties it
        /// and <paramref name="emptyDiscPaint"/> is not <see cref="InfoDemoPaint.NONE"/>, the disc turns
        /// that colour — the real Hold pocket's badge going pink at 0. Returns the time it settles.
        /// </summary>
        internal static float TickBadge(
            InfoDemoTimelineBuilder builder, InfoDemoCountBadge badge, float time, int emptyDiscPaint = InfoDemoPaint.NONE)
        {
            float settled = AdvanceCounter(builder, badge.Counter, time);
            if (badge.Count == 0 && emptyDiscPaint != InfoDemoPaint.NONE)
            {
                builder.Paint(badge.DiscId, time, emptyDiscPaint);
            }

            builder.Scale(badge.DiscId, time, COUNTER_CROSSFADE_DURATION + 0.1f, 1.2f, 1f, InfoDemoEasing.EaseOutBack);
            return settled;
        }

        /// <summary>
        /// <paramref name="badge"/> goes with whatever it is hung on at <paramref name="time"/> (issue #450 —
        /// a timer cell's countdown badge leaving with its cleared cell): its rim, disc and the number it
        /// shows at that point shrink and fade over <paramref name="duration"/>. Queue it after every
        /// <see cref="TickBadge"/> that plays before <paramref name="time"/>, so the right number is taken.
        /// Returns the time it is gone.
        /// </summary>
        internal static float HideBadge(InfoDemoTimelineBuilder builder, InfoDemoCountBadge badge, float time, float duration)
        {
            int[] parts = { badge.RimId, badge.DiscId, badge.LabelIdForCount(badge.Count) };
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                builder.Scale(parts[partIndex], time, duration, 1f, 0.3f, InfoDemoEasing.EaseInCubic);
                builder.Fade(parts[partIndex], time, duration, 1f, 0f, InfoDemoEasing.EaseInCubic);
            }

            return time + duration;
        }

        /// <summary>
        /// A thin countdown bar (issue #449, the 2x window): a faint <paramref name="trackPaint"/> track
        /// and a <paramref name="fillPaint"/> fill, both <paramref name="length"/> x
        /// <paramref name="thickness"/> board units and centred on <paramref name="centre"/>, fading in at
        /// <paramref name="appearTime"/>. The fill drains linearly towards its left end between
        /// <paramref name="drainStart"/> and <paramref name="drainEnd"/> — a per-axis stretch plus a matching
        /// slide, so its left edge never moves. Returns the fill's element id.
        /// </summary>
        internal static int CountdownBar(
            InfoDemoTimelineBuilder builder,
            Vector2 centre,
            float length,
            float thickness,
            int fillPaint,
            int trackPaint,
            float trackAlpha,
            float appearTime,
            float drainStart,
            float drainEnd)
        {
            Vector2 size = new Vector2(length, thickness);
            float corner = thickness * 0.5f;
            int track = builder.AddPanel(centre, size, corner, trackPaint, 0f);
            int fill = builder.AddPanel(centre, size, corner, fillPaint, 0f);

            builder.Fade(track, appearTime, 0.15f, 0f, trackAlpha, InfoDemoEasing.EaseOutCubic);
            builder.Fade(fill, appearTime, 0.15f, 0f, 1f, InfoDemoEasing.EaseOutCubic);

            float drainDuration = Mathf.Max(0.01f, drainEnd - drainStart);
            builder.Stretch(fill, drainStart, drainDuration, Vector2.one, new Vector2(0f, 1f));
            builder.Move(fill, drainStart, drainDuration, centre, centre - new Vector2(length * 0.5f, 0f));
            return fill;
        }

        /// <summary>
        /// A <paramref name="sprite"/> icon <paramref name="size"/> board-cell widths across flying from
        /// <paramref name="from"/> to <paramref name="to"/> over <paramref name="duration"/> from
        /// <paramref name="startTime"/>, in an arc <paramref name="arcHeight"/> board units above the
        /// straight line (a toss: it rises out, then drops in), swelling on the way and fading out as it
        /// lands. Drawn over the tray pieces. Returns the icon's element id; it lands at
        /// <paramref name="startTime"/> + <paramref name="duration"/>.
        /// </summary>
        internal static int FlyTo(
            InfoDemoTimelineBuilder builder,
            InfoDemoSprite sprite,
            int spriteParameter,
            float size,
            Vector2 from,
            Vector2 to,
            float startTime,
            float duration,
            float arcHeight,
            int paint = InfoDemoPaint.WHITE)
        {
            int icon = builder.AddIcon(sprite, spriteParameter, from, size, 0f, paint);
            builder.BringToFront(icon);

            float half = duration * 0.5f;
            Vector2 apex = ((from + to) * 0.5f) + new Vector2(0f, -arcHeight);
            builder.Move(icon, startTime, half, from, apex, InfoDemoEasing.EaseOutCubic);
            builder.Move(icon, startTime + half, half, apex, to, InfoDemoEasing.EaseInCubic);

            builder.Scale(icon, startTime, half, FLY_START_SCALE, FLY_APEX_SCALE, InfoDemoEasing.EaseOutCubic);
            builder.Scale(icon, startTime + half, half, FLY_APEX_SCALE, 1f, InfoDemoEasing.EaseInCubic);

            float landTime = startTime + duration;
            builder.Fade(icon, startTime, FLY_FADE_IN_DURATION, 0f, 1f);
            builder.Fade(icon, landTime - FLY_FADE_OUT_DURATION, FLY_FADE_OUT_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            return icon;
        }

        /// <summary>
        /// A localized label on a white rounded pill (issue #449 — "Nothing fits"): a pill
        /// <paramref name="size"/> board units across with a soft shadow, the text in
        /// <paramref name="textPaint"/>, popping in at <paramref name="startTime"/> and fading at
        /// <paramref name="endTime"/>. Drawn over the tray pieces. Returns the label's element id.
        /// </summary>
        internal static int LabelPill(
            InfoDemoTimelineBuilder builder,
            string localizationKey,
            Vector2 centre,
            Vector2 size,
            float fontHeight,
            int textPaint,
            float startTime,
            float endTime)
        {
            float corner = size.y * 0.5f;
            int shadow = builder.AddPanel(
                centre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_PILL_SHADOW_DROP)), size, corner, InfoDemoPaint.INK, 0f);
            int pill = builder.AddPanel(centre, size, corner, InfoDemoPaint.WHITE, 0f);
            int label = builder.AddLabel(localizationKey, centre, size.x * 0.9f, fontHeight, textPaint);

            PopIn(builder, shadow, PILL_SHADOW_ALPHA, startTime, endTime);
            PopIn(builder, pill, 1f, startTime, endTime);
            PopIn(builder, label, 1f, startTime, endTime);
            builder.BringToFront(shadow);
            builder.BringToFront(pill);
            return label;
        }

        /// <summary>One part of a popping group: fades in to <paramref name="peakAlpha"/> while scaling
        /// 0.6 → 1 with a small overshoot at <paramref name="startTime"/>, fades out at
        /// <paramref name="endTime"/> (never, when that is at or past the loop's end).</summary>
        internal static void PopIn(
            InfoDemoTimelineBuilder builder, int elementId, float peakAlpha, float startTime, float endTime)
        {
            builder.Fade(elementId, startTime, 0.12f, 0f, peakAlpha, InfoDemoEasing.EaseOutCubic);
            builder.Scale(elementId, startTime, PILL_POP_IN_DURATION, PILL_START_SCALE, 1f, InfoDemoEasing.EaseOutBack);
            builder.Fade(elementId, endTime, PILL_FADE_OUT_DURATION, peakAlpha, 0f, InfoDemoEasing.EaseInCubic);
        }
    }
}
