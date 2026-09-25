using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The reusable beats every info demo is assembled from (issue #445's "choreography patterns"):
    /// a tray piece dropped onto the board, a full line flashing and clearing left to right, a cell
    /// filling in with a pop, an outline pulsing around a group of cells, a floating label. Each helper
    /// only queues steps on an <see cref="InfoDemoTimelineBuilder"/> and returns the time its beat ends,
    /// so a demo script reads as a sequence of beats with explicit timings.
    /// </summary>
    internal static class InfoDemoChoreography
    {
        /// <summary>How long a tray piece takes to lift before it starts gliding.</summary>
        internal const float LIFT_DURATION = 0.15f;

        /// <summary>How long a lifted piece glides from its tray slot onto the board.</summary>
        internal const float GLIDE_DURATION = 0.55f;

        /// <summary>How long a landed cell's pop (0.86 → 1) lasts.</summary>
        internal const float LAND_POP_DURATION = 0.18f;
        internal const float LAND_POP_START_SCALE = 0.86f;

        /// <summary>How long a clearing line's cells flash towards white before they start to go.</summary>
        internal const float CLEAR_FLASH_DURATION = 0.12f;

        /// <summary>Delay between one clearing cell and the next, left to right (top to bottom for a
        /// column).</summary>
        internal const float CLEAR_STAGGER = 0.025f;

        /// <summary>How long one clearing cell takes to shrink and fade away.</summary>
        internal const float CLEAR_SHRINK_DURATION = 0.22f;

        internal const float FILL_POP_DURATION = 0.26f;

        /// <summary>How long a progress chip's counter crossfades from one value to the next.</summary>
        internal const float CHIP_COUNTER_CROSSFADE = 0.22f;

        /// <summary>How long a progress chip's "done" badge takes to pop in.</summary>
        internal const float CHIP_BADGE_POP_DURATION = 0.3f;

        /// <summary>Mockup-unit geometry of a progress chip's contents (see
        /// <see cref="InfoDemoLayout.MOCK_CHIP_WIDTH"/>): the caption and counter baselines relative to
        /// the chip centre, their font heights, and the done badge.</summary>
        private const float MOCK_CHIP_CORNER = 12f;
        private const float MOCK_CHIP_SHADOW_DROP = 2f;
        private const float MOCK_CHIP_CAPTION_OFFSET_Y = -10f;
        private const float MOCK_CHIP_CAPTION_FONT = 10f;
        private const float MOCK_CHIP_COUNTER_OFFSET_Y = 8f;
        private const float MOCK_CHIP_COUNTER_FONT = 16f;
        private const float MOCK_CHIP_BADGE_DIAMETER = 17f;
        private const float MOCK_CHIP_CHECK_SIZE = 12f;
        private const float CHIP_SHADOW_ALPHA = 0.1f;

        /// <summary>Peak opacity of a clearing line's highlight band — a glow behind the line, never a
        /// slab over the board.</summary>
        private const float CLEAR_BAND_ALPHA = 0.5f;

        /// <summary>How long a demo finger glides in before it touches down (issue #448).</summary>
        internal const float TAP_GLIDE_DURATION = 0.32f;

        /// <summary>How long a tap ring takes to expand and fade.</summary>
        internal const float TAP_RING_DURATION = 0.45f;

        /// <summary>How long a beam takes to grow out from its centre to the full line.</summary>
        internal const float BEAM_GROW_DURATION = 0.2f;

        /// <summary>How long a recoloured cell's flash-and-swell pop lasts.</summary>
        internal const float RECOLOUR_POP_DURATION = 0.3f;

        /// <summary>Mockup-unit geometry of a demo finger: its face diameter, border, soft shadow and the
        /// offset it glides in from (down and to the right of the touch point, where a thumb comes from).</summary>
        private const float MOCK_FINGER_DIAMETER = 22f;
        private const float MOCK_FINGER_BORDER = 1.5f;
        private const float MOCK_FINGER_SHADOW_DIAMETER = 36f;
        private const float MOCK_FINGER_SHADOW_DROP = 3f;
        private const float MOCK_FINGER_START_OFFSET_X = 26f;
        private const float MOCK_FINGER_START_OFFSET_Y = 34f;
        private const float FINGER_BORDER_ALPHA = 0.55f;
        private const float FINGER_SHADOW_ALPHA = 0.35f;
        private const float FINGER_PRESS_SCALE = 0.78f;
        private const float FINGER_PRESS_IN_DURATION = 0.08f;
        private const float FINGER_RELEASE_DURATION = 0.2f;
        private const float FINGER_FADE_DELAY = 0.22f;
        private const float FINGER_FADE_DURATION = 0.2f;

        /// <summary>Mockup-unit tap ring: diameter at scale 1 and wall thickness.</summary>
        private const float MOCK_TAP_RING_DIAMETER = 26f;
        private const float MOCK_TAP_RING_STROKE = 2.5f;
        private const float TAP_RING_START_SCALE = 0.3f;
        private const float TAP_RING_END_SCALE = 1.7f;
        private const float TAP_RING_START_ALPHA = 0.9f;

        /// <summary>A beam's white core bar and coloured halo, in board units (bar) and board-cell widths
        /// (halo) across the line; both run the full line length.</summary>
        private const float BEAM_BAR_LENGTH = InfoDemoLayout.BOARD_SIZE + 0.3f;
        private const float BEAM_BAR_THICKNESS = 0.3f;
        private const float BEAM_GLOW_LENGTH = InfoDemoLayout.BOARD_SIZE + 1.2f;
        private const float BEAM_GLOW_THICKNESS = 1.9f;
        private const float BEAM_GLOW_ALPHA = 0.85f;
        private const float BEAM_HOLD = 0.15f;
        private const float BEAM_FADE_DURATION = 0.3f;

        /// <summary>How long a bolt takes to grow from its source to its target (issue #450).</summary>
        internal const float BOLT_GROW_DURATION = 0.12f;

        /// <summary>How long a bolt holds at full length before it fades, and how long the fade takes.</summary>
        internal const float BOLT_HOLD = 0.14f;
        internal const float BOLT_FADE_DURATION = 0.2f;

        /// <summary>A bolt's bright white core line and the coloured sheath around it, across the line,
        /// in board units.</summary>
        private const float BOLT_CORE_THICKNESS = 0.08f;
        private const float BOLT_GLOW_THICKNESS = 0.24f;
        private const float BOLT_GLOW_ALPHA = 0.85f;
        private const float BOLT_APPEAR_DURATION = 0.04f;

        /// <summary>A burst's dense core ends at this fraction of the bloom's reach (the Vortex burst's
        /// authored 2 of 4.2), over this fraction of its duration.</summary>
        private const float BURST_CORE_REACH_FRACTION = 2f / 4.2f;
        private const float BURST_CORE_DURATION_FRACTION = 0.7f;
        private const float BURST_CORE_ALPHA = 0.9f;

        /// <summary>
        /// Drops piece <paramref name="pieceId"/> (sitting in the tray at <paramref name="trayPosition"/>)
        /// onto the board with its top-left cell at (<paramref name="row"/>, <paramref name="column"/>):
        /// lifts, glides while growing to full cell size, then hands over to the board blocks under it,
        /// which pop in. Returns the landing time.
        /// </summary>
        internal static float PlacePiece(
            InfoDemoTimelineBuilder builder,
            int pieceId,
            Vector2Int[] shape,
            int paint,
            Vector2 trayPosition,
            int row,
            int column,
            float startTime)
        {
            builder.Scale(
                pieceId, startTime, LIFT_DURATION,
                InfoDemoLayout.TRAY_PIECE_SCALE, InfoDemoLayout.LIFTED_PIECE_SCALE, InfoDemoEasing.EaseOutCubic);

            float glideStart = startTime + LIFT_DURATION;
            Vector2 target = InfoDemoLayout.PieceCentre(shape, row, column);
            builder.Move(pieceId, glideStart, GLIDE_DURATION, trayPosition, target, InfoDemoEasing.EaseInOutCubic);
            builder.Scale(
                pieceId, glideStart, GLIDE_DURATION, InfoDemoLayout.LIFTED_PIECE_SCALE, 1f, InfoDemoEasing.EaseInOutCubic);

            float landTime = glideStart + GLIDE_DURATION;
            builder.Fade(pieceId, landTime, 0f, 0f, 0f);

            for (int cellIndex = 0; cellIndex < shape.Length; cellIndex++)
            {
                Vector2Int offset = shape[cellIndex];
                int blockId = InfoDemoLayout.BoardBlockId(row + offset.y, column + offset.x);
                builder.Paint(blockId, landTime, paint);
                builder.ResetLook(blockId, landTime);
                builder.Scale(blockId, landTime, LAND_POP_DURATION, LAND_POP_START_SCALE, 1f, InfoDemoEasing.EaseOutBack);
            }

            return landTime;
        }

        /// <summary>When cell <paramref name="indexAlongLine"/> of a line clear started at
        /// <paramref name="clearStart"/> begins to shrink — so a script can make something riding on
        /// that cell (a special cell's icon) go with it.</summary>
        internal static float ClearCellShrinkStart(float clearStart, int indexAlongLine)
            => clearStart + CLEAR_FLASH_DURATION + (indexAlongLine * CLEAR_STAGGER);

        /// <summary>Clears row <paramref name="row"/>: a highlight band glows, every cell flashes bright,
        /// then the cells shrink and fade left to right. Returns the time the last cell is gone.</summary>
        internal static float ClearRow(InfoDemoTimelineBuilder builder, int row, float startTime)
            => ClearLine(builder, true, row, startTime);

        /// <summary>As <see cref="ClearRow"/>, for a column, top to bottom.</summary>
        internal static float ClearColumn(InfoDemoTimelineBuilder builder, int column, float startTime)
            => ClearLine(builder, false, column, startTime);

        /// <summary>Fills an empty board cell with <paramref name="paint"/> and a springy pop.
        /// Returns the time the pop settles.</summary>
        internal static float FillCell(InfoDemoTimelineBuilder builder, int row, int column, int paint, float startTime)
        {
            int blockId = InfoDemoLayout.BoardBlockId(row, column);
            builder.Paint(blockId, startTime, paint);
            builder.ResetLook(blockId, startTime);
            builder.Scale(blockId, startTime, FILL_POP_DURATION, 0.3f, 1f, InfoDemoEasing.EaseOutBack);
            builder.Fade(blockId, startTime, 0.1f, 0f, 1f);
            return startTime + FILL_POP_DURATION;
        }

        /// <summary>A dashed outline around a <paramref name="columns"/> x <paramref name="rows"/> block
        /// of cells centred on <paramref name="centre"/>: fades in, pulses <paramref name="pulseCount"/>
        /// times, then fades out at <paramref name="endTime"/>.</summary>
        internal static int PulseOutline(
            InfoDemoTimelineBuilder builder,
            Vector2 centre,
            int columns,
            int rows,
            int paint,
            float startTime,
            float endTime,
            int pulseCount)
        {
            const float outlinePadding = 0.14f;
            const float fadeDuration = 0.15f;

            int outlineId = builder.AddOutline(
                centre, new Vector2(columns + outlinePadding, rows + outlinePadding), paint);
            builder.Fade(outlineId, startTime, fadeDuration, 0f, 1f, InfoDemoEasing.EaseOutCubic);

            float pulseWindow = Mathf.Max(0.01f, endTime - startTime - fadeDuration);
            float pulseDuration = pulseWindow / Mathf.Max(1, pulseCount);
            for (int pulseIndex = 0; pulseIndex < pulseCount; pulseIndex++)
            {
                builder.Scale(
                    outlineId, startTime + fadeDuration + (pulseIndex * pulseDuration), pulseDuration,
                    1f, 1.1f, InfoDemoEasing.Pulse);
            }

            builder.Fade(outlineId, endTime, 0.3f, 1f, 0f, InfoDemoEasing.EaseInCubic);
            return outlineId;
        }

        /// <summary>A label that fades in at <paramref name="startTime"/>, rises
        /// <paramref name="rise"/> board units over <paramref name="duration"/>, and fades out over the
        /// last quarter of that. With <paramref name="labelArgument"/> the entry is a format string whose
        /// <c>{0}</c> it fills.</summary>
        internal static int FloatLabel(
            InfoDemoTimelineBuilder builder,
            string localizationKey,
            Vector2 position,
            float rise,
            int paint,
            float startTime,
            float duration,
            string labelArgument = null)
        {
            const float labelWidth = 7.6f;
            const float labelFontHeight = 0.62f;

            int labelId = builder.AddLabel(
                localizationKey, position, labelWidth, labelFontHeight, paint, 0f, labelArgument);
            builder.Fade(labelId, startTime, 0.15f, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Move(labelId, startTime, duration, position, position + new Vector2(0f, -rise), InfoDemoEasing.EaseOutCubic);

            float fadeOutDuration = duration * 0.25f;
            builder.Fade(labelId, startTime + duration - fadeOutDuration, fadeOutDuration, 1f, 0f, InfoDemoEasing.EaseInCubic);
            return labelId;
        }

        /// <summary>As <see cref="FloatLabel"/>, for language-neutral literal text — a score like "+11"
        /// (issue #449). <paramref name="fontHeight"/> is in board units.</summary>
        internal static int FloatText(
            InfoDemoTimelineBuilder builder,
            string literalText,
            Vector2 position,
            float rise,
            int paint,
            float fontHeight,
            float startTime,
            float duration)
        {
            const float labelWidth = 7.6f;

            int labelId = builder.AddText(literalText, position, labelWidth, fontHeight, paint);
            builder.Fade(labelId, startTime, 0.15f, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Scale(labelId, startTime, 0.3f, 0.6f, 1f, InfoDemoEasing.EaseOutBack);
            builder.Move(labelId, startTime, duration, position, position + new Vector2(0f, -rise), InfoDemoEasing.EaseOutCubic);

            float fadeOutDuration = duration * 0.25f;
            builder.Fade(labelId, startTime + duration - fadeOutDuration, fadeOutDuration, 1f, 0f, InfoDemoEasing.EaseInCubic);
            return labelId;
        }

        /// <summary>
        /// A simulated tap at <paramref name="position"/> (board units) landing at <paramref name="time"/>
        /// (issue #448): a white fingertip — soft shadow, thin dark rim — glides in from below-right over
        /// <see cref="TAP_GLIDE_DURATION"/>, presses (1 → 0.78 → 1) and lifts away, while a white ring
        /// expands from the touch point (0.3 → 1.7, fading). Each call adds its own finger, so a demo can
        /// tap as often as it likes. Returns the time the ring has faded.
        /// </summary>
        internal static float Tap(InfoDemoTimelineBuilder builder, float time, Vector2 position)
        {
            float glideStart = Mathf.Max(0f, time - TAP_GLIDE_DURATION);
            Vector2 startOffset = new Vector2(
                InfoDemoLayout.FromMockLength(MOCK_FINGER_START_OFFSET_X),
                InfoDemoLayout.FromMockLength(MOCK_FINGER_START_OFFSET_Y));
            Vector2 shadowDrop = new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_FINGER_SHADOW_DROP));

            AddFinger(builder, position + startOffset, shadowDrop, out int shadow, out int rim, out int face);

            AnimateFingerPart(builder, shadow, glideStart, time, position + startOffset + shadowDrop, position + shadowDrop, FINGER_SHADOW_ALPHA);
            AnimateFingerPart(builder, rim, glideStart, time, position + startOffset, position, FINGER_BORDER_ALPHA);
            AnimateFingerPart(builder, face, glideStart, time, position + startOffset, position, 1f);

            float ringDiameter = InfoDemoLayout.FromMockLength(MOCK_TAP_RING_DIAMETER);
            int ring = builder.AddRing(
                position, new Vector2(ringDiameter, ringDiameter), ringDiameter * 0.5f,
                InfoDemoLayout.FromMockLength(MOCK_TAP_RING_STROKE), InfoDemoPaint.WHITE);
            builder.Scale(ring, time, TAP_RING_DURATION, TAP_RING_START_SCALE, TAP_RING_END_SCALE, InfoDemoEasing.EaseOutCubic);
            builder.Fade(ring, time, TAP_RING_DURATION, TAP_RING_START_ALPHA, 0f, InfoDemoEasing.EaseInCubic);
            builder.BringToFront(ring);

            return time + TAP_RING_DURATION;
        }

        /// <summary>
        /// A simulated drag (issue #449 — a tray piece dragged onto the Hold pocket): the same fingertip as
        /// <see cref="Tap"/> glides in and touches down on <paramref name="from"/> at
        /// <paramref name="grabTime"/>, stays pressed while it travels to <paramref name="to"/> over
        /// [<paramref name="moveStart"/>, <paramref name="moveStart"/> + <paramref name="moveDuration"/>],
        /// then lifts and fades. Move whatever it carries along the same path with the same easing
        /// (<see cref="InfoDemoEasing.EaseInOutCubic"/>). Returns the release time.
        /// </summary>
        internal static float Drag(
            InfoDemoTimelineBuilder builder, float grabTime, Vector2 from, Vector2 to, float moveStart, float moveDuration)
        {
            float glideStart = Mathf.Max(0f, grabTime - TAP_GLIDE_DURATION);
            float glideDuration = Mathf.Max(0.01f, grabTime - glideStart);
            float releaseTime = moveStart + moveDuration;
            Vector2 startOffset = new Vector2(
                InfoDemoLayout.FromMockLength(MOCK_FINGER_START_OFFSET_X),
                InfoDemoLayout.FromMockLength(MOCK_FINGER_START_OFFSET_Y));
            Vector2 shadowDrop = new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_FINGER_SHADOW_DROP));

            AddFinger(builder, from + startOffset, shadowDrop, out int shadow, out int rim, out int face);

            int[] parts = { shadow, rim, face };
            float[] peakAlphas = { FINGER_SHADOW_ALPHA, FINGER_BORDER_ALPHA, 1f };
            Vector2[] offsets = { shadowDrop, Vector2.zero, Vector2.zero };
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                int part = parts[partIndex];
                Vector2 offset = offsets[partIndex];
                float peakAlpha = peakAlphas[partIndex];

                builder.Move(part, glideStart, glideDuration, from + startOffset + offset, from + offset, InfoDemoEasing.EaseOutCubic);
                builder.Fade(part, glideStart, Mathf.Min(0.12f, glideDuration), 0f, peakAlpha, InfoDemoEasing.EaseOutCubic);
                builder.Scale(
                    part, grabTime - FINGER_PRESS_IN_DURATION, FINGER_PRESS_IN_DURATION, 1f, FINGER_PRESS_SCALE,
                    InfoDemoEasing.EaseInCubic);

                builder.Move(part, moveStart, moveDuration, from + offset, to + offset, InfoDemoEasing.EaseInOutCubic);

                builder.Scale(part, releaseTime, FINGER_RELEASE_DURATION, FINGER_PRESS_SCALE, 1f, InfoDemoEasing.EaseOutCubic);
                builder.Fade(
                    part, releaseTime + FINGER_FADE_DELAY, FINGER_FADE_DURATION, peakAlpha, 0f, InfoDemoEasing.EaseInCubic);
            }

            return releaseTime;
        }

        /// <summary><see cref="Tap"/> on the centre of board cell (<paramref name="row"/>, <paramref name="column"/>).</summary>
        internal static float TapCell(InfoDemoTimelineBuilder builder, float time, int row, int column)
            => Tap(builder, time, InfoDemoLayout.Cell(row, column));

        /// <summary>
        /// A power-up beam along row (<paramref name="isRow"/>) or column <paramref name="lineIndex"/>
        /// (issue #448): a bright white bar inside a <paramref name="paint"/> halo, growing out from the
        /// line's centre to its full length over <see cref="BEAM_GROW_DURATION"/>, holding briefly, then
        /// fading. Drawn over the blocks. Returns the time it has faded.
        /// </summary>
        internal static float Beam(InfoDemoTimelineBuilder builder, float time, bool isRow, int lineIndex, int paint)
        {
            float centre = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            Vector2 position = isRow ? new Vector2(centre, lineIndex) : new Vector2(lineIndex, centre);
            Vector2 glowSize = isRow
                ? new Vector2(BEAM_GLOW_LENGTH, BEAM_GLOW_THICKNESS)
                : new Vector2(BEAM_GLOW_THICKNESS, BEAM_GLOW_LENGTH);
            Vector2 barSize = isRow
                ? new Vector2(BEAM_BAR_LENGTH, BEAM_BAR_THICKNESS)
                : new Vector2(BEAM_BAR_THICKNESS, BEAM_BAR_LENGTH);
            Vector2 collapsed = isRow ? new Vector2(0f, 1f) : new Vector2(1f, 0f);

            int glow = builder.AddGlow(position, glowSize, paint);
            int bar = builder.AddPanel(position, barSize, BEAM_BAR_THICKNESS * 0.5f, InfoDemoPaint.WHITE, 0f);

            float fadeStart = time + BEAM_GROW_DURATION + BEAM_HOLD;
            builder.Stretch(glow, time, BEAM_GROW_DURATION, collapsed, Vector2.one, InfoDemoEasing.EaseOutCubic);
            builder.Stretch(bar, time, BEAM_GROW_DURATION, collapsed, Vector2.one, InfoDemoEasing.EaseOutCubic);
            builder.Fade(glow, time, 0.06f, 0f, BEAM_GLOW_ALPHA);
            builder.Fade(bar, time, 0.06f, 0f, 1f);
            builder.Fade(glow, fadeStart, BEAM_FADE_DURATION, BEAM_GLOW_ALPHA, 0f, InfoDemoEasing.EaseInCubic);
            builder.Fade(bar, fadeStart, BEAM_FADE_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            return fadeStart + BEAM_FADE_DURATION;
        }

        /// <summary>
        /// A bolt from <paramref name="from"/> to <paramref name="to"/> (board units) starting at
        /// <paramref name="startTime"/> (issue #450, a chain lightning's strike): a thin bright white line
        /// inside a slightly wider <paramref name="glowPaint"/> sheath, both rotated onto the segment and growing from
        /// the source towards the target over <see cref="BOLT_GROW_DURATION"/> — a per-axis stretch
        /// 0 → 1 paired with a slide from the source to the midpoint on the same easing, so the source end
        /// never moves — then holding for <see cref="BOLT_HOLD"/> and fading over
        /// <see cref="BOLT_FADE_DURATION"/>. Drawn over the blocks. Returns the core line's element id;
        /// the bolt reaches its target at <paramref name="startTime"/> + <see cref="BOLT_GROW_DURATION"/>.
        /// </summary>
        internal static int Bolt(InfoDemoTimelineBuilder builder, Vector2 from, Vector2 to, int glowPaint, float startTime)
        {
            float length = (to - from).magnitude;
            float angle = BoltAngle(from, to);
            Vector2 midpoint = (from + to) * 0.5f;
            Vector2 collapsed = new Vector2(0f, 1f);
            float fadeStart = startTime + BOLT_GROW_DURATION + BOLT_HOLD;

            int glow = builder.AddPanel(
                from, new Vector2(length, BOLT_GLOW_THICKNESS), BOLT_GLOW_THICKNESS * 0.5f, glowPaint, 0f);
            int core = builder.AddPanel(
                from, new Vector2(length, BOLT_CORE_THICKNESS), BOLT_CORE_THICKNESS * 0.5f, InfoDemoPaint.WHITE, 0f);

            AnimateBoltPart(builder, glow, from, midpoint, angle, collapsed, BOLT_GLOW_ALPHA, startTime, fadeStart);
            AnimateBoltPart(builder, core, from, midpoint, angle, collapsed, 1f, startTime, fadeStart);
            return core;
        }

        /// <summary>The Z rotation (degrees) that lays a bar's local x axis along the segment
        /// <paramref name="from"/> → <paramref name="to"/>. Board y runs down the rows while the stage's
        /// pixels run up, so the row delta is negated.</summary>
        internal static float BoltAngle(Vector2 from, Vector2 to)
            => Mathf.Atan2(-(to.y - from.y), to.x - from.x) * Mathf.Rad2Deg;

        private static void AnimateBoltPart(
            InfoDemoTimelineBuilder builder,
            int elementId,
            Vector2 from,
            Vector2 midpoint,
            float angle,
            Vector2 collapsed,
            float peakAlpha,
            float startTime,
            float fadeStart)
        {
            builder.Rotate(elementId, 0f, 0f, angle, angle);
            builder.Stretch(elementId, 0f, 0f, collapsed, collapsed);
            builder.Stretch(elementId, startTime, BOLT_GROW_DURATION, collapsed, Vector2.one, InfoDemoEasing.EaseOutCubic);
            builder.Move(elementId, startTime, BOLT_GROW_DURATION, from, midpoint, InfoDemoEasing.EaseOutCubic);
            builder.Fade(elementId, startTime, BOLT_APPEAR_DURATION, 0f, peakAlpha);
            builder.Fade(elementId, fadeStart, BOLT_FADE_DURATION, peakAlpha, 0f, InfoDemoEasing.EaseInCubic);
        }

        /// <summary>
        /// A radial burst at <paramref name="centre"/>: a wide soft <paramref name="paint"/> bloom
        /// <paramref name="size"/> board-cell widths across growing 0.5 → <paramref name="reach"/> while it
        /// fades, around a denser <paramref name="corePaint"/> core that stops short of it
        /// (<see cref="InfoDemoPaint.NONE"/> for no core). The Vortex's violet burst (#446) and the
        /// power-up blasts (#448). Returns the time the bloom is gone.
        /// </summary>
        internal static float Burst(
            InfoDemoTimelineBuilder builder,
            Vector2 centre,
            int paint,
            int corePaint,
            float size,
            float reach,
            float startTime,
            float duration)
        {
            int bloom = builder.AddGlow(centre, size, paint);
            builder.Scale(bloom, startTime, duration, 0.5f, reach, InfoDemoEasing.EaseOutCubic);
            builder.Fade(bloom, startTime, duration, 1f, 0f, InfoDemoEasing.EaseInCubic);

            if (corePaint != InfoDemoPaint.NONE)
            {
                float coreDuration = duration * BURST_CORE_DURATION_FRACTION;
                int core = builder.AddGlow(centre, size, corePaint);
                builder.Scale(core, startTime, coreDuration, 0.3f, reach * BURST_CORE_REACH_FRACTION, InfoDemoEasing.EaseOutCubic);
                builder.Fade(core, startTime, coreDuration, BURST_CORE_ALPHA, 0f, InfoDemoEasing.EaseInCubic);
            }

            return startTime + duration;
        }

        /// <summary>
        /// A translucent filled highlight over the block of cells rows <paramref name="topRow"/>..
        /// <paramref name="bottomRow"/>, columns <paramref name="leftColumn"/>..<paramref name="rightColumn"/>
        /// (issue #448 — a bomb's 3x3 preview): fades in to <paramref name="peakAlpha"/> at
        /// <paramref name="startTime"/> and out from <paramref name="endTime"/>. Drawn over the blocks.
        /// </summary>
        internal static int RectHighlight(
            InfoDemoTimelineBuilder builder,
            int topRow,
            int leftColumn,
            int bottomRow,
            int rightColumn,
            int paint,
            float peakAlpha,
            float startTime,
            float endTime)
        {
            const float padding = 0.08f;
            const float cornerRadius = 0.3f;
            const float fadeInDuration = 0.15f;
            const float fadeOutDuration = 0.25f;

            Vector2 size = new Vector2(rightColumn - leftColumn + 1 + padding, bottomRow - topRow + 1 + padding);
            int highlight = builder.AddPanel(
                InfoDemoLayout.CellSpanCentre(topRow, leftColumn, bottomRow, rightColumn), size, cornerRadius, paint, 0f);
            builder.Fade(highlight, startTime, fadeInDuration, 0f, peakAlpha, InfoDemoEasing.EaseOutCubic);
            builder.Fade(highlight, endTime, fadeOutDuration, peakAlpha, 0f, InfoDemoEasing.EaseInCubic);
            return highlight;
        }

        /// <summary>Repaints an occupied board cell as <paramref name="paint"/> at <paramref name="time"/>
        /// with a small pop — a white flash and a swell that both settle back (issue #448, Paint Cross).
        /// Returns the time the pop settles.</summary>
        internal static float Recolour(InfoDemoTimelineBuilder builder, int row, int column, int paint, float time)
        {
            int blockId = InfoDemoLayout.BoardBlockId(row, column);
            builder.Paint(blockId, time, paint);
            builder.Flash(blockId, time, RECOLOUR_POP_DURATION, 0f, 0.55f, InfoDemoEasing.Pulse);
            builder.Scale(blockId, time, RECOLOUR_POP_DURATION, 1f, 1.15f, InfoDemoEasing.Pulse);
            return time + RECOLOUR_POP_DURATION;
        }

        /// <summary><see cref="Recolour"/> on every cell of <paramref name="cells"/> (x = column,
        /// y = row), nearest to <paramref name="origin"/> first, <paramref name="stagger"/> apart.
        /// Returns the time the last pop settles.</summary>
        internal static float RecolourCells(
            InfoDemoTimelineBuilder builder, Vector2Int[] cells, Vector2 origin, int paint, float startTime, float stagger)
        {
            Vector2Int[] ordered = OrderByDistance(cells, origin);
            float settled = startTime;
            for (int orderIndex = 0; orderIndex < ordered.Length; orderIndex++)
            {
                Vector2Int cell = ordered[orderIndex];
                settled = Recolour(builder, cell.y, cell.x, paint, startTime + (orderIndex * stagger));
            }

            return settled;
        }

        /// <summary>Flashes every cell of <paramref name="cells"/> (x = column, y = row) bright and back,
        /// <paramref name="pulseCount"/> times over <paramref name="duration"/> — "these ones" (issue
        /// #448, the cells a Color Cleanser is about to take). Returns the end time.</summary>
        internal static float PulseCells(
            InfoDemoTimelineBuilder builder, Vector2Int[] cells, float startTime, float duration, float peakFlash, int pulseCount)
        {
            int pulses = Mathf.Max(1, pulseCount);
            float pulseDuration = duration / pulses;
            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                int blockId = InfoDemoLayout.BoardBlockId(cells[cellIndex].y, cells[cellIndex].x);
                for (int pulseIndex = 0; pulseIndex < pulses; pulseIndex++)
                {
                    builder.Flash(blockId, startTime + (pulseIndex * pulseDuration), pulseDuration, 0f, peakFlash, InfoDemoEasing.Pulse);
                }
            }

            return startTime + duration;
        }

        /// <summary>
        /// Clears an arbitrary set of board cells (x = column, y = row) the way a line clear clears its
        /// line — each flashes bright, then shrinks and fades — but ordered by distance from
        /// <paramref name="origin"/>, nearest first, <paramref name="stagger"/> apart (issue #448: a
        /// bomb's 3x3 from its centre out, a cleanser's colour from the tapped cell out). Returns the time
        /// the last cell is gone.
        /// </summary>
        internal static float ClearCells(
            InfoDemoTimelineBuilder builder, Vector2Int[] cells, Vector2 origin, float startTime, float stagger)
        {
            Vector2Int[] ordered = OrderByDistance(cells, origin);
            float lastCellGone = startTime;
            for (int orderIndex = 0; orderIndex < ordered.Length; orderIndex++)
            {
                Vector2Int cell = ordered[orderIndex];
                int blockId = InfoDemoLayout.BoardBlockId(cell.y, cell.x);
                float cellStart = startTime + (orderIndex * stagger);

                builder.Flash(blockId, cellStart, CLEAR_FLASH_DURATION, 0f, 0.75f, InfoDemoEasing.EaseOutCubic);

                float shrinkStart = cellStart + CLEAR_FLASH_DURATION;
                builder.Scale(blockId, shrinkStart, CLEAR_SHRINK_DURATION, 1f, 0.25f, InfoDemoEasing.EaseInCubic);
                builder.Fade(blockId, shrinkStart, CLEAR_SHRINK_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);

                float cellGone = shrinkStart + CLEAR_SHRINK_DURATION;
                builder.Paint(blockId, cellGone, InfoDemoPaint.NONE);
                builder.ResetLook(blockId, cellGone);
                lastCellGone = Mathf.Max(lastCellGone, cellGone);
            }

            return lastCellGone;
        }

        /// <summary>A copy of <paramref name="cells"/> sorted nearest-to-<paramref name="origin"/> first;
        /// ties keep their given order. Build-time only (it allocates).</summary>
        internal static Vector2Int[] OrderByDistance(Vector2Int[] cells, Vector2 origin)
        {
            Vector2Int[] ordered = new Vector2Int[cells.Length];
            float[] distances = new float[cells.Length];
            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                Vector2Int cell = cells[cellIndex];
                float distance = ((Vector2)cell - origin).sqrMagnitude;

                // Insertion sort, stable: shift strictly farther cells right.
                int insertIndex = cellIndex - 1;
                while (insertIndex >= 0 && distances[insertIndex] > distance)
                {
                    ordered[insertIndex + 1] = ordered[insertIndex];
                    distances[insertIndex + 1] = distances[insertIndex];
                    insertIndex--;
                }

                ordered[insertIndex + 1] = cell;
                distances[insertIndex + 1] = distance;
            }

            return ordered;
        }

        /// <summary>A demo finger's three parts at <paramref name="position"/>, all hidden and drawn over
        /// the tray pieces (a finger touching a tray piece is on top of it). Icon sizes are in board-cell
        /// widths (MOCK_CELL), not pitches. Back to front: shadow, rim, face.</summary>
        private static void AddFinger(
            InfoDemoTimelineBuilder builder, Vector2 position, Vector2 shadowDrop, out int shadow, out int rim, out int face)
        {
            shadow = builder.AddIcon(
                InfoDemoSprite.SoftDisc, 0, position + shadowDrop,
                MOCK_FINGER_SHADOW_DIAMETER / InfoDemoLayout.MOCK_CELL, 0f, InfoDemoPaint.INK);
            rim = builder.AddIcon(
                InfoDemoSprite.Disc, 0, position,
                (MOCK_FINGER_DIAMETER + (MOCK_FINGER_BORDER * 2f)) / InfoDemoLayout.MOCK_CELL, 0f, InfoDemoPaint.INK);
            face = builder.AddIcon(
                InfoDemoSprite.Disc, 0, position,
                MOCK_FINGER_DIAMETER / InfoDemoLayout.MOCK_CELL, 0f, InfoDemoPaint.WHITE);

            builder.BringToFront(shadow);
            builder.BringToFront(rim);
            builder.BringToFront(face);
        }

        private static void AnimateFingerPart(
            InfoDemoTimelineBuilder builder, int elementId, float glideStart, float touchTime, Vector2 from, Vector2 to, float peakAlpha)
        {
            float glideDuration = Mathf.Max(0.01f, touchTime - glideStart);
            builder.Move(elementId, glideStart, glideDuration, from, to, InfoDemoEasing.EaseOutCubic);
            builder.Fade(elementId, glideStart, Mathf.Min(0.12f, glideDuration), 0f, peakAlpha, InfoDemoEasing.EaseOutCubic);

            builder.Scale(
                elementId, touchTime - FINGER_PRESS_IN_DURATION, FINGER_PRESS_IN_DURATION, 1f, FINGER_PRESS_SCALE,
                InfoDemoEasing.EaseInCubic);
            builder.Scale(elementId, touchTime, FINGER_RELEASE_DURATION, FINGER_PRESS_SCALE, 1f, InfoDemoEasing.EaseOutCubic);

            builder.Fade(
                elementId, touchTime + FINGER_FADE_DELAY, FINGER_FADE_DURATION, peakAlpha, 0f, InfoDemoEasing.EaseInCubic);
        }

        private static float ClearLine(InfoDemoTimelineBuilder builder, bool isRow, int lineIndex, float startTime)
        {
            const float bandLength = InfoDemoLayout.BOARD_SIZE + 0.3f;
            const float bandThickness = 1.3f;
            float centre = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;

            Vector2 bandPosition = isRow ? new Vector2(centre, lineIndex) : new Vector2(lineIndex, centre);
            Vector2 bandSize = isRow ? new Vector2(bandLength, bandThickness) : new Vector2(bandThickness, bandLength);
            int bandId = builder.AddBand(bandPosition, bandSize, InfoDemoPaint.LINE_HIGHLIGHT);
            builder.Fade(bandId, startTime, CLEAR_FLASH_DURATION, 0f, CLEAR_BAND_ALPHA, InfoDemoEasing.EaseOutCubic);

            float lastCellGone = startTime;
            for (int indexAlongLine = 0; indexAlongLine < InfoDemoLayout.BOARD_SIZE; indexAlongLine++)
            {
                int blockId = isRow
                    ? InfoDemoLayout.BoardBlockId(lineIndex, indexAlongLine)
                    : InfoDemoLayout.BoardBlockId(indexAlongLine, lineIndex);

                builder.Flash(blockId, startTime, CLEAR_FLASH_DURATION, 0f, 0.75f, InfoDemoEasing.EaseOutCubic);

                float shrinkStart = ClearCellShrinkStart(startTime, indexAlongLine);
                builder.Scale(blockId, shrinkStart, CLEAR_SHRINK_DURATION, 1f, 0.25f, InfoDemoEasing.EaseInCubic);
                builder.Fade(blockId, shrinkStart, CLEAR_SHRINK_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);

                float cellGone = shrinkStart + CLEAR_SHRINK_DURATION;
                builder.Paint(blockId, cellGone, InfoDemoPaint.NONE);
                builder.ResetLook(blockId, cellGone);
                lastCellGone = cellGone;
            }

            // Starts fading as the last cell starts to go, so the band is gone with the line.
            float bandFadeStart = ClearCellShrinkStart(startTime, InfoDemoLayout.BOARD_SIZE - 1);
            builder.Fade(bandId, bandFadeStart, lastCellGone - bandFadeStart, CLEAR_BAND_ALPHA, 0f, InfoDemoEasing.EaseOutCubic);
            return lastCellGone;
        }

        /// <summary>
        /// An objective demo's progress chip in the tray strip's left zone (issue #447 — the mockup's
        /// "hedef çipi"): a white rounded chip with a caption on its top line
        /// (<paramref name="captionKey"/>, its <c>{0}</c> filled with <paramref name="captionArgument"/>
        /// when that is not null) and a "<paramref name="startValue"/>/<paramref name="target"/>"
        /// counter below, plus a hidden green check badge at its top-right. Present from loop time 0;
        /// move the counter on with <see cref="AdvanceChip"/>. Lay the tray out with
        /// <see cref="InfoDemoTrayLayout.ChipLeft"/> so no piece sits under it.
        /// </summary>
        internal static InfoDemoProgressChip ProgressChip(
            InfoDemoTimelineBuilder builder, string captionKey, string captionArgument, int startValue, int target)
        {
            Vector2 centre = InfoDemoLayout.ChipCentre;
            Vector2 chipSize = new Vector2(
                InfoDemoLayout.FromMockLength(InfoDemoLayout.MOCK_CHIP_WIDTH),
                InfoDemoLayout.FromMockLength(InfoDemoLayout.MOCK_CHIP_HEIGHT));
            float corner = InfoDemoLayout.FromMockLength(MOCK_CHIP_CORNER);
            float textWidth = chipSize.x * 0.9f;

            builder.AddPanel(
                centre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_CHIP_SHADOW_DROP)),
                chipSize, corner, InfoDemoPaint.INK, CHIP_SHADOW_ALPHA);
            int panelId = builder.AddPanel(centre, chipSize, corner, InfoDemoPaint.WHITE);

            int captionId = builder.AddLabel(
                captionKey,
                centre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_CHIP_CAPTION_OFFSET_Y)),
                textWidth,
                InfoDemoLayout.FromMockLength(MOCK_CHIP_CAPTION_FONT),
                InfoDemoPaint.SOFT_INK,
                1f,
                captionArgument);

            Vector2 counterPosition = centre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_CHIP_COUNTER_OFFSET_Y));
            float counterFont = InfoDemoLayout.FromMockLength(MOCK_CHIP_COUNTER_FONT);
            int valueCount = Mathf.Max(1, target - startValue + 1);
            int[] counterLabelIds = new int[valueCount];
            for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
            {
                int value = startValue + valueIndex;
                counterLabelIds[valueIndex] = builder.AddText(
                    value + "/" + target, counterPosition, textWidth, counterFont, InfoDemoPaint.INK,
                    valueIndex == 0 ? 1f : 0f);
            }

            Vector2 badgeCentre = centre + new Vector2(
                (chipSize.x * 0.5f) - InfoDemoLayout.FromMockLength(4f),
                -(chipSize.y * 0.5f) + InfoDemoLayout.FromMockLength(3f));
            float badgeDiameter = InfoDemoLayout.FromMockLength(MOCK_CHIP_BADGE_DIAMETER);
            int badgeId = builder.AddPanel(
                badgeCentre, new Vector2(badgeDiameter, badgeDiameter), badgeDiameter * 0.5f, InfoDemoPaint.SUCCESS, 0f);

            // Icon sizes are in board-cell widths (MOCK_CELL), not pitches.
            int checkId = builder.AddIcon(
                InfoDemoSprite.CheckMark, 0, badgeCentre, MOCK_CHIP_CHECK_SIZE / InfoDemoLayout.MOCK_CELL, 0f);

            return new InfoDemoProgressChip(panelId, captionId, counterLabelIds, startValue, target, badgeId, checkId);
        }

        /// <summary>
        /// Moves <paramref name="chip"/>'s counter on by one at <paramref name="time"/>: the old value
        /// fades out as the new one pops in, and when that reaches the target the green check badge pops
        /// in at the chip's top-right. A chip already at its target is left alone. Returns the time the
        /// beat settles.
        /// </summary>
        internal static float AdvanceChip(InfoDemoTimelineBuilder builder, InfoDemoProgressChip chip, float time)
        {
            if (chip.IsComplete)
            {
                return time;
            }

            int oldLabel = chip.CounterLabelId(chip.Value);
            chip.Advance();
            int newLabel = chip.CounterLabelId(chip.Value);

            builder.Fade(oldLabel, time, CHIP_COUNTER_CROSSFADE, 1f, 0f, InfoDemoEasing.EaseInCubic);
            builder.Fade(newLabel, time, CHIP_COUNTER_CROSSFADE, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Scale(newLabel, time, CHIP_COUNTER_CROSSFADE + 0.1f, 1.35f, 1f, InfoDemoEasing.EaseOutBack);

            float settled = time + CHIP_COUNTER_CROSSFADE + 0.1f;
            if (!chip.IsComplete)
            {
                return settled;
            }

            float badgeStart = time + (CHIP_COUNTER_CROSSFADE * 0.5f);
            builder.Fade(chip.BadgeId, badgeStart, 0.1f, 0f, 1f);
            builder.Scale(chip.BadgeId, badgeStart, CHIP_BADGE_POP_DURATION, 0.2f, 1f, InfoDemoEasing.EaseOutBack);
            builder.Fade(chip.CheckId, badgeStart, 0.1f, 0f, 1f);
            builder.Scale(chip.CheckId, badgeStart, CHIP_BADGE_POP_DURATION, 0.2f, 1f, InfoDemoEasing.EaseOutBack);
            return Mathf.Max(settled, badgeStart + CHIP_BADGE_POP_DURATION);
        }
    }
}
