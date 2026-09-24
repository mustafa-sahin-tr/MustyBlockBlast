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

        /// <summary>Peak opacity of a clearing line's highlight band — a glow behind the line, never a
        /// slab over the board.</summary>
        private const float CLEAR_BAND_ALPHA = 0.5f;

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
        /// last quarter of that.</summary>
        internal static int FloatLabel(
            InfoDemoTimelineBuilder builder,
            string localizationKey,
            Vector2 position,
            float rise,
            int paint,
            float startTime,
            float duration)
        {
            const float labelWidth = 7.6f;
            const float labelFontHeight = 0.62f;

            int labelId = builder.AddLabel(localizationKey, position, labelWidth, labelFontHeight, paint);
            builder.Fade(labelId, startTime, 0.15f, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            builder.Move(labelId, startTime, duration, position, position + new Vector2(0f, -rise), InfoDemoEasing.EaseOutCubic);

            float fadeOutDuration = duration * 0.25f;
            builder.Fade(labelId, startTime + duration - fadeOutDuration, fadeOutDuration, 1f, 0f, InfoDemoEasing.EaseInCubic);
            return labelId;
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
    }
}
