using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Authoring API for an <see cref="InfoDemoTimeline"/>. A demo script creates one, lays out the
    /// starting board (<see cref="SetBoardRow"/>), adds the extra elements it needs (tray pieces,
    /// icons, glows, outlines, bands, labels), queues steps against them, and calls <see cref="Build"/>
    /// once. Every demo shares this one builder, and the reusable choreography beats (a tray piece
    /// dropped onto the board, a line clearing left to right, a cell filling in) live on
    /// <see cref="InfoDemoChoreography"/> on top of it — so a new demo is just a new script.
    /// <para>
    /// The 64 board blocks are always created first, as ids 0..63
    /// (<see cref="InfoDemoLayout.BoardBlockId"/>), all empty.
    /// </para>
    /// </summary>
    internal sealed class InfoDemoTimelineBuilder
    {
        internal const float DEFAULT_FADE_IN = 0.25f;
        internal const float DEFAULT_FADE_OUT = 0.4f;

        private readonly float _duration;
        private readonly float _fadeInDuration;
        private readonly float _fadeOutDuration;
        private readonly List<InfoDemoElement> _elements = new List<InfoDemoElement>(96);
        private readonly List<InfoDemoStep> _steps = new List<InfoDemoStep>(256);

        internal InfoDemoTimelineBuilder(
            float duration, float fadeInDuration = DEFAULT_FADE_IN, float fadeOutDuration = DEFAULT_FADE_OUT)
        {
            _duration = duration;
            _fadeInDuration = fadeInDuration;
            _fadeOutDuration = fadeOutDuration;

            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    AddElement(
                        InfoDemoElementKind.BoardBlock,
                        Vector2.one,
                        InfoDemoSprite.None,
                        0,
                        null,
                        null,
                        null,
                        0f,
                        0f,
                        InfoDemoElementState.At(InfoDemoLayout.Cell(row, column), InfoDemoPaint.NONE, 1f));
                }
            }
        }

        /// <summary>Sets the starting paint of every cell in <paramref name="row"/> from a pattern
        /// string, one character per column — see <see cref="InfoDemoPaint.FromPatternChar"/>.</summary>
        internal void SetBoardRow(int row, string pattern)
        {
            int count = Mathf.Min(pattern.Length, InfoDemoLayout.BOARD_SIZE);
            for (int column = 0; column < count; column++)
            {
                SetBoardCell(row, column, InfoDemoPaint.FromPatternChar(pattern[column]));
            }
        }

        internal void SetBoardCell(int row, int column, int paint)
        {
            _elements[InfoDemoLayout.BoardBlockId(row, column)].Initial.Paint = paint;
        }

        /// <summary>A multi-cell piece, all cells in <paramref name="paint"/>, centred on
        /// <paramref name="position"/> at <paramref name="scale"/> (1 = board cell size).</summary>
        internal int AddPiece(Vector2Int[] shape, int paint, Vector2 position, float scale, float alpha = 1f)
        {
            InfoDemoElementState initial = InfoDemoElementState.At(position, paint, alpha);
            initial.Scale = scale;
            return AddElement(InfoDemoElementKind.Piece, Vector2.one, InfoDemoSprite.None, 0, shape, null, null, 0f, 0f, initial);
        }

        /// <summary>
        /// As <see cref="AddPiece"/>, for a dock piece carrying special piece <paramref name="kind"/>
        /// (issue #451): every cell wears the dock's own mark for it (<c>SpecialPieceVisuals.ApplyGlyph</c>
        /// — the rocket's arrow, the hammer). Paint a golden piece <see cref="InfoDemoPaint.GOLDEN_PIECE"/>,
        /// the dock's gold emboss; the other kinds keep an ordinary block colour, as in the dock.
        /// </summary>
        internal int AddSpecialPiece(
            SpecialPieceKind kind, Vector2Int[] shape, int paint, Vector2 position, float scale, float alpha = 1f)
        {
            InfoDemoElementState initial = InfoDemoElementState.At(position, paint, alpha);
            initial.Scale = scale;
            return AddElement(
                InfoDemoElementKind.Piece, Vector2.one, InfoDemoSprite.None, (int)kind, shape, null, null, 0f, 0f, initial);
        }

        /// <summary>A sprite <paramref name="size"/> board-cell widths square, tinted as
        /// <see cref="IInfoDemoResources"/> resolves it and multiplied by <paramref name="paint"/> (white
        /// leaves the resolved tint as it is; a plain white shape sprite takes the paint outright).</summary>
        internal int AddIcon(
            InfoDemoSprite sprite, int spriteParameter, Vector2 position, float size, float alpha = 1f,
            int paint = InfoDemoPaint.WHITE)
        {
            return AddElement(
                InfoDemoElementKind.Icon,
                new Vector2(size, size),
                sprite,
                spriteParameter,
                null,
                null,
                null,
                0f,
                0f,
                InfoDemoElementState.At(position, paint, alpha));
        }

        internal int AddGlow(Vector2 position, float size, int paint, float alpha = 0f)
            => AddGlow(position, new Vector2(size, size), paint, alpha);

        /// <summary>A soft radial glow stretched to <paramref name="size"/> board-cell widths — an
        /// elongated glow (a beam's halo) when the two axes differ.</summary>
        internal int AddGlow(Vector2 position, Vector2 size, int paint, float alpha = 0f)
        {
            return AddElement(
                InfoDemoElementKind.Glow,
                size,
                InfoDemoSprite.None,
                0,
                null,
                null,
                null,
                0f,
                0f,
                InfoDemoElementState.At(position, paint, alpha));
        }

        /// <summary>A solid hollow rounded frame <paramref name="size"/> board units across (exactly,
        /// like a panel) with <paramref name="cornerRadius"/> corners and a
        /// <paramref name="strokeWidth"/> wall, all in board units. A radius of half the size draws a
        /// circle ring (issue #448).</summary>
        internal int AddRing(Vector2 position, Vector2 size, float cornerRadius, float strokeWidth, int paint, float alpha = 0f)
        {
            return AddElement(
                InfoDemoElementKind.Ring, size, InfoDemoSprite.None, 0, null, null, null, cornerRadius, strokeWidth,
                InfoDemoElementState.At(position, paint, alpha));
        }

        /// <summary>A dashed outline hugging a <paramref name="size"/> block of cells (board units).</summary>
        internal int AddOutline(Vector2 position, Vector2 size, int paint, float alpha = 0f)
        {
            return AddElement(
                InfoDemoElementKind.Outline, size, InfoDemoSprite.None, 0, null, null, null, 0f, 0f,
                InfoDemoElementState.At(position, paint, alpha));
        }

        internal int AddBand(Vector2 position, Vector2 size, int paint, float alpha = 0f)
        {
            return AddElement(
                InfoDemoElementKind.Band, size, InfoDemoSprite.None, 0, null, null, null, 0f, 0f,
                InfoDemoElementState.At(position, paint, alpha));
        }

        /// <summary>A solid rounded rectangle <paramref name="size"/> board units across (exactly — no
        /// cell gap is subtracted, unlike a band) with <paramref name="cornerRadius"/> board-unit
        /// corners. A radius of half the size draws a disc.</summary>
        internal int AddPanel(Vector2 position, Vector2 size, float cornerRadius, int paint, float alpha = 1f)
        {
            return AddElement(
                InfoDemoElementKind.Panel, size, InfoDemoSprite.None, 0, null, null, null, cornerRadius, 0f,
                InfoDemoElementState.At(position, paint, alpha));
        }

        /// <summary>A localized label: <paramref name="width"/> board units wide, a font
        /// <paramref name="fontHeight"/> board units tall (shrunk to fit on one line if the translation
        /// is wider). With <paramref name="argument"/> the entry is a format string and the argument
        /// fills its <c>{0}</c>.</summary>
        internal int AddLabel(
            string localizationKey, Vector2 position, float width, float fontHeight, int paint, float alpha = 0f,
            string argument = null)
        {
            return AddElement(
                InfoDemoElementKind.Label, new Vector2(width, fontHeight), InfoDemoSprite.None, 0, null, localizationKey,
                argument, 0f, 0f, InfoDemoElementState.At(position, paint, alpha));
        }

        /// <summary>A label showing <paramref name="literalText"/> as-is — language-neutral text such
        /// as a "0/1" counter. Sized like <see cref="AddLabel"/>.</summary>
        internal int AddText(string literalText, Vector2 position, float width, float fontHeight, int paint, float alpha = 0f)
        {
            return AddElement(
                InfoDemoElementKind.Label, new Vector2(width, fontHeight), InfoDemoSprite.None, 0, null, null,
                literalText, 0f, 0f, InfoDemoElementState.At(position, paint, alpha));
        }

        /// <summary>Draws <paramref name="elementId"/> in the stage's overlay layer, over every tray piece
        /// (issue #449) — a finger pressing a piece, a badge on a piece. Overlay elements keep their
        /// relative order (element id order), so a tap ring added after its finger still draws over it.</summary>
        internal void BringToFront(int elementId)
        {
            if (elementId < 0 || elementId >= _elements.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(elementId), elementId, "No such demo element.");
            }

            _elements[elementId].MarkOnTop();
        }

        /// <summary>Queues one step. Steps may be added in any order; <see cref="Build"/> sorts them.</summary>
        internal void Animate(
            int elementId,
            InfoDemoProperty property,
            float startTime,
            float duration,
            Vector4 from,
            Vector4 to,
            InfoDemoEasing easing = InfoDemoEasing.Linear)
        {
            if (elementId < 0 || elementId >= _elements.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(elementId), elementId, "No such demo element.");
            }

            _steps.Add(new InfoDemoStep(elementId, property, startTime, duration, from, to, easing));
        }

        internal void Fade(int elementId, float startTime, float duration, float from, float to, InfoDemoEasing easing = InfoDemoEasing.Linear)
            => Animate(elementId, InfoDemoProperty.Alpha, startTime, duration, new Vector4(from, 0f), new Vector4(to, 0f), easing);

        internal void Scale(int elementId, float startTime, float duration, float from, float to, InfoDemoEasing easing = InfoDemoEasing.Linear)
            => Animate(elementId, InfoDemoProperty.Scale, startTime, duration, new Vector4(from, 0f), new Vector4(to, 0f), easing);

        internal void Move(int elementId, float startTime, float duration, Vector2 from, Vector2 to, InfoDemoEasing easing = InfoDemoEasing.Linear)
            => Animate(elementId, InfoDemoProperty.Position, startTime, duration, from, to, easing);

        /// <summary>Drives the per-axis <see cref="InfoDemoElementState.Stretch"/> — (0, 1) → (1, 1)
        /// grows a horizontal bar out from its centre.</summary>
        internal void Stretch(int elementId, float startTime, float duration, Vector2 from, Vector2 to, InfoDemoEasing easing = InfoDemoEasing.Linear)
            => Animate(elementId, InfoDemoProperty.Stretch, startTime, duration, from, to, easing);

        internal void Rotate(int elementId, float startTime, float duration, float fromDegrees, float toDegrees, InfoDemoEasing easing = InfoDemoEasing.Linear)
            => Animate(elementId, InfoDemoProperty.Rotation, startTime, duration, new Vector4(fromDegrees, 0f), new Vector4(toDegrees, 0f), easing);

        internal void Flash(int elementId, float startTime, float duration, float from, float to, InfoDemoEasing easing = InfoDemoEasing.Linear)
            => Animate(elementId, InfoDemoProperty.Flash, startTime, duration, new Vector4(from, 0f), new Vector4(to, 0f), easing);

        /// <summary>Switches the element's paint at <paramref name="time"/>, instantly.</summary>
        internal void Paint(int elementId, float time, int paint)
            => Animate(elementId, InfoDemoProperty.Paint, time, 0f, new Vector4(paint, 0f), new Vector4(paint, 0f));

        /// <summary>Snaps the element's alpha, scale and flash back to rest at <paramref name="time"/>
        /// — used after a board block clears, so a later fill of that cell starts from a clean state.</summary>
        internal void ResetLook(int elementId, float time)
        {
            Fade(elementId, time, 0f, 1f, 1f);
            Scale(elementId, time, 0f, 1f, 1f);
            Flash(elementId, time, 0f, 0f, 0f);
        }

        /// <summary>Freezes everything queued so far into an immutable timeline, steps stably sorted by
        /// start time (steps sharing a start time keep the order they were queued in, so a later
        /// <see cref="Animate"/> call wins).</summary>
        internal InfoDemoTimeline Build()
        {
            InfoDemoElement[] elements = _elements.ToArray();
            InfoDemoStep[] steps = _steps.ToArray();

            // Insertion sort: stable, and a one-off cost at build time on a few hundred steps.
            for (int stepIndex = 1; stepIndex < steps.Length; stepIndex++)
            {
                InfoDemoStep current = steps[stepIndex];
                int insertIndex = stepIndex - 1;
                while (insertIndex >= 0 && steps[insertIndex].StartTime > current.StartTime)
                {
                    steps[insertIndex + 1] = steps[insertIndex];
                    insertIndex--;
                }

                steps[insertIndex + 1] = current;
            }

            return new InfoDemoTimeline(_duration, _fadeInDuration, _fadeOutDuration, elements, steps);
        }

        private int AddElement(
            InfoDemoElementKind kind,
            Vector2 size,
            InfoDemoSprite sprite,
            int spriteParameter,
            Vector2Int[] shape,
            string labelKey,
            string labelArgument,
            float cornerRadius,
            float strokeWidth,
            InfoDemoElementState initial)
        {
            _elements.Add(new InfoDemoElement(
                kind, size, sprite, spriteParameter, shape, labelKey, labelArgument, cornerRadius, strokeWidth, initial));
            return _elements.Count - 1;
        }
    }
}
