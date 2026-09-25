using MustyBlockBlast.Gameplay;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The power-up beats of issue #445's choreography patterns ("tap the icon — yellow armed ring —
    /// tap the target — the effect plays"), on top of <see cref="InfoDemoChoreography"/>: the power-up
    /// button in the tray strip's left zone, pressing (and arming) it, spending a charge off its count
    /// badge, the filler tray beside it, and Paint Cross's colour picker pill. Every helper only queues
    /// steps on an <see cref="InfoDemoTimelineBuilder"/>; nothing here runs at play time.
    /// </summary>
    internal static class InfoDemoPowerUpChoreography
    {
        /// <summary>How long a pressed button's plate takes to pop back from 0.88 to full size.</summary>
        internal const float BUTTON_PRESS_POP_DURATION = 0.25f;
        internal const float BUTTON_PRESS_SCALE = 0.88f;

        /// <summary>How long the armed ring and glow take to fade in, and to fade out on disarm.</summary>
        internal const float ARM_FADE_IN_DURATION = 0.2f;
        internal const float ARM_FADE_OUT_DURATION = 0.25f;

        /// <summary>How long the colour picker takes to pop in, and to shrink away.</summary>
        internal const float PICKER_POP_IN_DURATION = 0.28f;
        internal const float PICKER_POP_OUT_DURATION = 0.18f;

        /// <summary>Mockup-unit geometry of the power-up button (centred where an objective demo's chip
        /// sits, <see cref="InfoDemoLayout.ChipCentre"/>): plate, icon, armed ring and glow, count badge.</summary>
        private const float MOCK_PLATE_SIZE = 46f;
        private const float MOCK_PLATE_CORNER = 12f;
        private const float MOCK_PLATE_SHADOW_DROP = 2.5f;
        private const float MOCK_ICON_SIZE = 30f;
        private const float MOCK_ARMED_RING_SIZE = 56f;
        private const float MOCK_ARMED_RING_CORNER = 16f;
        private const float MOCK_ARMED_RING_STROKE = 3f;
        private const float MOCK_ARMED_GLOW_SIZE = 90f;
        private const float MOCK_BADGE_DIAMETER = 17f;
        private const float MOCK_BADGE_RIM = 2f;
        private const float MOCK_BADGE_INSET = 4f;
        private const float MOCK_BADGE_FONT = 10f;
        private const float PLATE_SHADOW_ALPHA = 0.14f;
        private const float ARMED_GLOW_ALPHA = 0.75f;
        private const float ARMED_RING_START_SCALE = 1.15f;

        /// <summary>Mockup x of a power-up button placed beside a progress chip (issue #452): the chip ends
        /// at 67, the plate spans ±23 around this and tray slot 1 starts near 150.</summary>
        private const float MOCK_BUTTON_BESIDE_CHIP_X = 102f;

        /// <summary>Mockup-unit geometry of the colour picker pill: its swatches (rounded squares, like the
        /// real picker's), their pitch, the pill's padding and how far above the tapped cell it floats.</summary>
        private const float MOCK_SWATCH_SIZE = 18f;
        private const float MOCK_SWATCH_CORNER = 5f;
        private const float MOCK_SWATCH_PITCH = 23f;
        private const float MOCK_PILL_PADDING = 8f;
        private const float MOCK_PILL_SHADOW_DROP = 2.5f;
        private const float MOCK_PILL_GAP_ABOVE_CELL = 5f;
        private const float MOCK_SELECTION_RING_SIZE = 25f;
        private const float MOCK_SELECTION_RING_STROKE = 2.5f;
        private const float PILL_SHADOW_ALPHA = 0.16f;
        private const float PICKER_START_SCALE = 0.6f;

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };

        /// <summary>
        /// The strip's power-up button for <paramref name="kind"/>, present from loop time 0 in the
        /// strip's left zone: a <paramref name="platePaint"/> rounded plate with the power-up's own icon
        /// (<see cref="InfoDemoSprite.PowerUpIcon"/>) and a small red count badge reading
        /// <paramref name="count"/> at its top-right, plus a hidden yellow armed ring and glow. Lay the tray
        /// out with <see cref="InfoDemoTrayLayout.ChipLeft"/> (or use <see cref="FillerTray"/>) so no piece
        /// sits under it.
        /// </summary>
        internal static InfoDemoPowerUpButton PowerUpButton(
            InfoDemoTimelineBuilder builder, PowerUpKind kind, int platePaint, int count)
            => PowerUpButton(builder, InfoDemoSprite.PowerUpIcon, (int)kind, platePaint, count);

        /// <summary>
        /// As <see cref="PowerUpButton(InfoDemoTimelineBuilder, PowerUpKind, int, int)"/>, with any icon
        /// sprite on the plate — for a power-up that has no strip icon of its own (Coin Sower, issue
        /// #449, whose charges are spent at the level-start picker rather than from the strip, so it is
        /// drawn with the coin face instead).
        /// </summary>
        internal static InfoDemoPowerUpButton PowerUpButton(
            InfoDemoTimelineBuilder builder, InfoDemoSprite iconSprite, int iconParameter, int platePaint, int count)
            => PowerUpButton(builder, iconSprite, iconParameter, platePaint, count, InfoDemoLayout.ChipCentre);

        /// <summary>
        /// As <see cref="PowerUpButton(InfoDemoTimelineBuilder, PowerUpKind, int, int)"/>, centred on
        /// <paramref name="centre"/> rather than in the strip's left zone — for an objective demo whose
        /// progress chip already holds that zone and needs the power-up beside it (issue #452, the Bomb
        /// behind the Bomb-induced line clear objective). <see cref="StripButtonBesideChip"/> is the spot
        /// just right of the chip.
        /// </summary>
        internal static InfoDemoPowerUpButton PowerUpButton(
            InfoDemoTimelineBuilder builder, PowerUpKind kind, int platePaint, int count, Vector2 centre)
            => PowerUpButton(builder, InfoDemoSprite.PowerUpIcon, (int)kind, platePaint, count, centre);

        /// <summary>The strip spot just right of an objective demo's progress chip (mockup x
        /// <see cref="MOCK_BUTTON_BESIDE_CHIP_X"/>), where a power-up button sits clear of both the chip and
        /// tray slots 1 and 2 of <see cref="InfoDemoTrayLayout.ChipLeft"/>.</summary>
        internal static Vector2 StripButtonBesideChip => InfoDemoLayout.StripPoint(MOCK_BUTTON_BESIDE_CHIP_X);

        private static InfoDemoPowerUpButton PowerUpButton(
            InfoDemoTimelineBuilder builder,
            InfoDemoSprite iconSprite,
            int iconParameter,
            int platePaint,
            int count,
            Vector2 centre)
        {
            float plateSide = InfoDemoLayout.FromMockLength(MOCK_PLATE_SIZE);
            Vector2 plateSize = new Vector2(plateSide, plateSide);
            float plateCorner = InfoDemoLayout.FromMockLength(MOCK_PLATE_CORNER);

            // Back to front within each layer: glow (glow layer), shadow then plate (panel layer), icon,
            // ring (ring layer), badge discs (panel layer, after the plate), count (label layer).
            int armedGlow = builder.AddGlow(centre, MOCK_ARMED_GLOW_SIZE / InfoDemoLayout.MOCK_CELL, InfoDemoPaint.ARMED);
            int shadow = builder.AddPanel(
                centre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_PLATE_SHADOW_DROP)),
                plateSize, plateCorner, InfoDemoPaint.INK, PLATE_SHADOW_ALPHA);
            int plate = builder.AddPanel(centre, plateSize, plateCorner, platePaint);
            int icon = builder.AddIcon(iconSprite, iconParameter, centre, MOCK_ICON_SIZE / InfoDemoLayout.MOCK_CELL);

            float ringSide = InfoDemoLayout.FromMockLength(MOCK_ARMED_RING_SIZE);
            int armedRing = builder.AddRing(
                centre, new Vector2(ringSide, ringSide), InfoDemoLayout.FromMockLength(MOCK_ARMED_RING_CORNER),
                InfoDemoLayout.FromMockLength(MOCK_ARMED_RING_STROKE), InfoDemoPaint.ARMED);

            float badgeOffset = InfoDemoLayout.FromMockLength((MOCK_PLATE_SIZE * 0.5f) - MOCK_BADGE_INSET);
            Vector2 badgeCentre = centre + new Vector2(badgeOffset, -badgeOffset);
            InfoDemoCountBadge badge = InfoDemoHudChoreography.CountBadge(
                builder,
                badgeCentre,
                count,
                InfoDemoPaint.BADGE_RED,
                InfoDemoLayout.FromMockLength(MOCK_BADGE_DIAMETER),
                InfoDemoLayout.FromMockLength(MOCK_BADGE_RIM),
                InfoDemoLayout.FromMockLength(MOCK_BADGE_FONT));

            return new InfoDemoPowerUpButton(centre, shadow, armedGlow, armedRing, plate, icon, badge);
        }

        /// <summary>
        /// A finger taps <paramref name="button"/> at <paramref name="time"/> and its plate pops (0.88 → 1).
        /// With <paramref name="arm"/> the yellow armed ring and glow fade in around it and fade out at
        /// <paramref name="disarmTime"/> — when the power-up has been applied. Returns the time the pop
        /// settles.
        /// </summary>
        internal static float PressButton(
            InfoDemoTimelineBuilder builder, InfoDemoPowerUpButton button, float time, bool arm, float disarmTime)
        {
            InfoDemoChoreography.Tap(builder, time, button.Centre);

            builder.Scale(button.PlateId, time, BUTTON_PRESS_POP_DURATION, BUTTON_PRESS_SCALE, 1f, InfoDemoEasing.EaseOutBack);
            builder.Scale(button.ShadowId, time, BUTTON_PRESS_POP_DURATION, BUTTON_PRESS_SCALE, 1f, InfoDemoEasing.EaseOutBack);
            builder.Scale(button.IconId, time, BUTTON_PRESS_POP_DURATION, BUTTON_PRESS_SCALE, 1f, InfoDemoEasing.EaseOutBack);

            if (arm)
            {
                builder.Fade(button.ArmedGlowId, time, ARM_FADE_IN_DURATION, 0f, ARMED_GLOW_ALPHA, InfoDemoEasing.EaseOutCubic);
                builder.Fade(button.ArmedRingId, time, ARM_FADE_IN_DURATION, 0f, 1f, InfoDemoEasing.EaseOutCubic);
                builder.Scale(
                    button.ArmedRingId, time, ARM_FADE_IN_DURATION + 0.1f, ARMED_RING_START_SCALE, 1f, InfoDemoEasing.EaseOutCubic);

                builder.Fade(
                    button.ArmedGlowId, disarmTime, ARM_FADE_OUT_DURATION, ARMED_GLOW_ALPHA, 0f, InfoDemoEasing.EaseInCubic);
                builder.Fade(button.ArmedRingId, disarmTime, ARM_FADE_OUT_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            }

            return time + BUTTON_PRESS_POP_DURATION;
        }

        /// <summary>The count badge ticks down by one at <paramref name="time"/> — the charge the
        /// application just spent. Call it once per charge: a Coin Sower sowing three coins ticks 3 → 2 →
        /// 1 → 0 (issue #449). Returns the time the crossfade settles.</summary>
        internal static float SpendCharge(InfoDemoTimelineBuilder builder, InfoDemoPowerUpButton button, float time)
            => InfoDemoHudChoreography.AdvanceCounter(builder, button.Badge.Counter, time);

        /// <summary>The idle tray beside a power-up button — an L corner (green), a 1x2 (blue) and a
        /// single (purple) in the strip's right-zone slots. Nothing is played from it: it is there so the
        /// strip reads as the real HUD.</summary>
        internal static void FillerTray(InfoDemoTimelineBuilder builder)
        {
            builder.AddPiece(
                CornerShape, InfoDemoPaint.BLOCK_2,
                InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            builder.AddPiece(
                DominoShape, InfoDemoPaint.BLOCK_5,
                InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
            builder.AddPiece(
                SingleShape, InfoDemoPaint.BLOCK_3,
                InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), InfoDemoLayout.TRAY_PIECE_SCALE);
        }

        /// <summary>
        /// A white pill of <paramref name="swatchPaints"/> swatches floating just above board cell
        /// (<paramref name="row"/>, <paramref name="column"/>) — Paint Cross's colour picker (issue #448).
        /// Pops in (0.6 → 1, swatches spreading out from its centre) at <paramref name="openTime"/> and
        /// shrinks away at <paramref name="closeTime"/>. Drawn over the blocks.
        /// </summary>
        internal static InfoDemoColourPicker ColourPicker(
            InfoDemoTimelineBuilder builder, int row, int column, int[] swatchPaints, float openTime, float closeTime)
        {
            int swatchCount = swatchPaints.Length;
            float swatchSide = InfoDemoLayout.FromMockLength(MOCK_SWATCH_SIZE);
            float swatchPitch = InfoDemoLayout.FromMockLength(MOCK_SWATCH_PITCH);
            float padding = InfoDemoLayout.FromMockLength(MOCK_PILL_PADDING);
            Vector2 pillSize = new Vector2(
                ((swatchCount - 1) * swatchPitch) + swatchSide + (padding * 2f), swatchSide + (padding * 2f));

            // Centred over the cell, clamped inside the board, floating a small gap above it.
            float halfSpan = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            float maxOffset = Mathf.Max(0f, halfSpan + 0.5f - (pillSize.x * 0.5f));
            float pillX = Mathf.Clamp(column, halfSpan - maxOffset, halfSpan + maxOffset);
            float pillY = row - 0.5f - InfoDemoLayout.FromMockLength(MOCK_PILL_GAP_ABOVE_CELL) - (pillSize.y * 0.5f);
            Vector2 pillCentre = new Vector2(pillX, pillY);

            int shadow = builder.AddPanel(
                pillCentre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_PILL_SHADOW_DROP)),
                pillSize, pillSize.y * 0.5f, InfoDemoPaint.INK, 0f);
            int pill = builder.AddPanel(pillCentre, pillSize, pillSize.y * 0.5f, InfoDemoPaint.WHITE, 0f);
            PopPart(builder, shadow, pillCentre, pillCentre, PILL_SHADOW_ALPHA, openTime, closeTime);
            PopPart(builder, pill, pillCentre, pillCentre, 1f, openTime, closeTime);

            Vector2[] positions = new Vector2[swatchCount];
            int[] ids = new int[swatchCount];
            int[] paints = new int[swatchCount];
            float firstX = pillCentre.x - ((swatchCount - 1) * swatchPitch * 0.5f);
            float swatchCorner = InfoDemoLayout.FromMockLength(MOCK_SWATCH_CORNER);

            for (int swatchIndex = 0; swatchIndex < swatchCount; swatchIndex++)
            {
                Vector2 position = new Vector2(firstX + (swatchIndex * swatchPitch), pillCentre.y);
                int swatch = builder.AddPanel(
                    position, new Vector2(swatchSide, swatchSide), swatchCorner, swatchPaints[swatchIndex], 0f);
                PopPart(builder, swatch, pillCentre, position, 1f, openTime, closeTime);

                positions[swatchIndex] = position;
                ids[swatchIndex] = swatch;
                paints[swatchIndex] = swatchPaints[swatchIndex];
            }

            return new InfoDemoColourPicker(positions, ids, paints);
        }

        /// <summary>A finger taps swatch <paramref name="swatchIndex"/> of <paramref name="picker"/> at
        /// <paramref name="time"/>: it swells and a dark selection ring pops round it, both gone by
        /// <paramref name="closeTime"/> (the picker's own close). Returns the tap ring's end.</summary>
        internal static float PickSwatch(
            InfoDemoTimelineBuilder builder, InfoDemoColourPicker picker, int swatchIndex, float time, float closeTime)
        {
            Vector2 position = picker.SwatchPosition(swatchIndex);
            float tapEnd = InfoDemoChoreography.Tap(builder, time, position);

            builder.Scale(picker.SwatchId(swatchIndex), time, 0.3f, 1f, 1.2f, InfoDemoEasing.Pulse);

            float ringSide = InfoDemoLayout.FromMockLength(MOCK_SELECTION_RING_SIZE);
            int selection = builder.AddRing(
                position, new Vector2(ringSide, ringSide), InfoDemoLayout.FromMockLength(MOCK_SWATCH_CORNER + 2f),
                InfoDemoLayout.FromMockLength(MOCK_SELECTION_RING_STROKE), InfoDemoPaint.INK);
            builder.Fade(selection, time, 0.1f, 0f, 1f);
            builder.Scale(selection, time, 0.25f, 0.7f, 1f, InfoDemoEasing.EaseOutBack);
            builder.Fade(selection, closeTime, PICKER_POP_OUT_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            builder.Scale(selection, closeTime, PICKER_POP_OUT_DURATION, 1f, 0.7f, InfoDemoEasing.EaseInCubic);
            return tapEnd;
        }

        /// <summary>One part of a popping group: fades and scales in with its position spreading from the
        /// group's <paramref name="groupCentre"/> to <paramref name="restPosition"/>, then back on close —
        /// so the group reads as one thing popping rather than each part swelling in place.</summary>
        private static void PopPart(
            InfoDemoTimelineBuilder builder,
            int elementId,
            Vector2 groupCentre,
            Vector2 restPosition,
            float peakAlpha,
            float openTime,
            float closeTime)
        {
            Vector2 squeezed = groupCentre + ((restPosition - groupCentre) * PICKER_START_SCALE);

            builder.Fade(elementId, openTime, 0.12f, 0f, peakAlpha, InfoDemoEasing.EaseOutCubic);
            builder.Scale(elementId, openTime, PICKER_POP_IN_DURATION, PICKER_START_SCALE, 1f, InfoDemoEasing.EaseOutBack);
            builder.Move(elementId, openTime, PICKER_POP_IN_DURATION, squeezed, restPosition, InfoDemoEasing.EaseOutBack);

            builder.Fade(elementId, closeTime, PICKER_POP_OUT_DURATION, peakAlpha, 0f, InfoDemoEasing.EaseInCubic);
            builder.Scale(elementId, closeTime, PICKER_POP_OUT_DURATION, 1f, PICKER_START_SCALE, InfoDemoEasing.EaseInCubic);
            builder.Move(elementId, closeTime, PICKER_POP_OUT_DURATION, restPosition, squeezed, InfoDemoEasing.EaseInCubic);
        }
    }
}
