using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Authoring handle for a demo's colour picker pill (issue #448, Paint Cross), returned by
    /// <see cref="InfoDemoPowerUpChoreography.ColourPicker"/>: where each swatch sits, its element id and
    /// its paint. Used only while a timeline is being built and never touched at play time.
    /// </summary>
    internal sealed class InfoDemoColourPicker
    {
        private readonly Vector2[] _swatchPositions;
        private readonly int[] _swatchIds;
        private readonly int[] _swatchPaints;

        internal InfoDemoColourPicker(Vector2[] swatchPositions, int[] swatchIds, int[] swatchPaints)
        {
            _swatchPositions = swatchPositions;
            _swatchIds = swatchIds;
            _swatchPaints = swatchPaints;
        }

        internal int SwatchCount => _swatchIds.Length;

        /// <summary>Resting centre of swatch <paramref name="swatchIndex"/>, in board units.</summary>
        internal Vector2 SwatchPosition(int swatchIndex) => _swatchPositions[swatchIndex];

        internal int SwatchId(int swatchIndex) => _swatchIds[swatchIndex];

        internal int SwatchPaint(int swatchIndex) => _swatchPaints[swatchIndex];

        /// <summary>Index of the swatch painted <paramref name="paint"/>, or -1.</summary>
        internal int IndexOfPaint(int paint)
        {
            for (int swatchIndex = 0; swatchIndex < _swatchPaints.Length; swatchIndex++)
            {
                if (_swatchPaints[swatchIndex] == paint)
                {
                    return swatchIndex;
                }
            }

            return -1;
        }
    }
}
