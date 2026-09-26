using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// A hollow rounded frame hugging its rect, drawn as one vertex strip that can be revealed only
    /// part of the way round (issue #517): <see cref="Progress"/> 0 draws nothing, 1 the whole frame,
    /// anything between the stretch from the top-left corner clockwise — the special-cell border's
    /// light-trace draw-in. The sliced outline sprites the other cell rings use cannot be cut along
    /// their perimeter, hence a mesh of its own.
    /// <para>
    /// Samples a single opaque texel of <see cref="UiSpriteFactory.RoundedSquare"/> — the texture the
    /// cells' own blocks are drawn from — rather than the default white texture, so every border on
    /// the board batches in with the cells instead of costing a draw call of its own. The path is
    /// rebuilt only when the rect or the shape changes; a progress change only re-emits vertices, so
    /// animating it allocates nothing.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class PerimeterBorderGraphic : MaskableGraphic
    {
        /// <summary>Straight segments each rounded corner is approximated with — smooth at a cell's
        /// on-screen size.</summary>
        private const int ARC_SEGMENTS = 6;

        private const int POINTS_PER_CORNER = ARC_SEGMENTS + 1;
        private const int POINT_COUNT = POINTS_PER_CORNER * 4;

        private readonly Vector2[] _outerPoints = new Vector2[POINT_COUNT];
        private readonly Vector2[] _innerPoints = new Vector2[POINT_COUNT];

        /// <summary>Distance along the frame's centre line from the start to each point; the extra
        /// last entry is the full loop back to the start.</summary>
        private readonly float[] _cumulativeLengths = new float[POINT_COUNT + 1];

        private float _cornerRadius;
        private float _thicknessFraction;
        private float _progress = 1f;
        private Rect _pathRect;
        private bool _pathValid;
        private Vector2 _texelUv;

        public override Texture mainTexture => UiSpriteFactory.RoundedSquare.texture;

        /// <summary>How much of the frame is drawn, 0..1, from the top-left corner clockwise.</summary>
        internal float Progress
        {
            get => _progress;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, _progress))
                {
                    return;
                }

                _progress = clamped;
                SetVerticesDirty();
            }
        }

        /// <summary>Where the drawn stretch currently ends, on the frame's centre line, in this
        /// graphic's local space — where the trace's spark of light sits.</summary>
        internal Vector2 HeadPosition
        {
            get
            {
                EnsurePath();
                float target = _progress * _cumulativeLengths[POINT_COUNT];
                for (int pointIndex = 0; pointIndex < POINT_COUNT; pointIndex++)
                {
                    float segmentEnd = _cumulativeLengths[pointIndex + 1];
                    if (segmentEnd < target)
                    {
                        continue;
                    }

                    float segmentLength = segmentEnd - _cumulativeLengths[pointIndex];
                    float fraction = segmentLength > 0f ? (target - _cumulativeLengths[pointIndex]) / segmentLength : 0f;
                    return Vector2.Lerp(MidPoint(pointIndex), MidPoint((pointIndex + 1) % POINT_COUNT), fraction);
                }

                return MidPoint(0);
            }
        }

        /// <summary>Sets the frame's shape: <paramref name="cornerRadius"/> of its outer edge, in this
        /// graphic's local units, and a wall <paramref name="thicknessFraction"/> of the rect's shorter side
        /// wide — relative rather than absolute so a small tray block wears the same weight of frame as a
        /// board cell.</summary>
        internal void Configure(float cornerRadius, float thicknessFraction)
        {
            _cornerRadius = Mathf.Max(0f, cornerRadius);
            _thicknessFraction = Mathf.Max(0f, thicknessFraction);
            _pathValid = false;
            raycastTarget = false;

            Sprite texelSource = UiSpriteFactory.RoundedSquare;
            Rect textureRect = texelSource.textureRect;
            Texture2D texture = texelSource.texture;
            _texelUv = new Vector2(textureRect.center.x / texture.width, textureRect.center.y / texture.height);

            SetVerticesDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            _pathValid = false;
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_progress <= 0f)
            {
                return;
            }

            EnsurePath();

            Color32 vertexColour = color;
            float target = _progress * _cumulativeLengths[POINT_COUNT];

            AddPair(vertexHelper, _outerPoints[0], _innerPoints[0], vertexColour);
            int pairCount = 1;

            for (int pointIndex = 0; pointIndex < POINT_COUNT; pointIndex++)
            {
                int nextIndex = (pointIndex + 1) % POINT_COUNT;
                float segmentStart = _cumulativeLengths[pointIndex];
                float segmentEnd = _cumulativeLengths[pointIndex + 1];

                if (segmentEnd <= target)
                {
                    AddPair(vertexHelper, _outerPoints[nextIndex], _innerPoints[nextIndex], vertexColour);
                }
                else
                {
                    float segmentLength = segmentEnd - segmentStart;
                    float fraction = segmentLength > 0f ? (target - segmentStart) / segmentLength : 0f;
                    AddPair(
                        vertexHelper,
                        Vector2.Lerp(_outerPoints[pointIndex], _outerPoints[nextIndex], fraction),
                        Vector2.Lerp(_innerPoints[pointIndex], _innerPoints[nextIndex], fraction),
                        vertexColour);
                }

                int previousPair = (pairCount - 1) * 2;
                int currentPair = pairCount * 2;
                vertexHelper.AddTriangle(previousPair, previousPair + 1, currentPair + 1);
                vertexHelper.AddTriangle(previousPair, currentPair + 1, currentPair);
                pairCount++;

                if (segmentEnd >= target)
                {
                    break;
                }
            }
        }

        private void AddPair(VertexHelper vertexHelper, Vector2 outer, Vector2 inner, Color32 vertexColour)
        {
            vertexHelper.AddVert(outer, vertexColour, _texelUv);
            vertexHelper.AddVert(inner, vertexColour, _texelUv);
        }

        private Vector2 MidPoint(int pointIndex) => (_outerPoints[pointIndex] + _innerPoints[pointIndex]) * 0.5f;

        /// <summary>Lays the four rounded corners out clockwise from the top-left one, each sampled
        /// outer and inner at the same angles so the wall stays one width round the
        /// bend. The outer radius is held at least as wide as the wall so the inner corner never folds
        /// over itself.</summary>
        private void EnsurePath()
        {
            Rect rect = rectTransform.rect;
            if (_pathValid && rect == _pathRect)
            {
                return;
            }

            _pathRect = rect;
            _pathValid = true;

            float halfShortSide = Mathf.Min(rect.width, rect.height) * 0.5f;
            float thickness = Mathf.Max(0.5f, halfShortSide * 2f * _thicknessFraction);
            float outerRadius = Mathf.Min(Mathf.Max(_cornerRadius, thickness), halfShortSide);
            float innerRadius = Mathf.Max(0f, outerRadius - thickness);

            for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                Vector2 centre;
                float startDegrees;
                switch (cornerIndex)
                {
                    case 0:
                        centre = new Vector2(rect.xMin + outerRadius, rect.yMax - outerRadius);
                        startDegrees = 180f;
                        break;
                    case 1:
                        centre = new Vector2(rect.xMax - outerRadius, rect.yMax - outerRadius);
                        startDegrees = 90f;
                        break;
                    case 2:
                        centre = new Vector2(rect.xMax - outerRadius, rect.yMin + outerRadius);
                        startDegrees = 0f;
                        break;
                    default:
                        centre = new Vector2(rect.xMin + outerRadius, rect.yMin + outerRadius);
                        startDegrees = -90f;
                        break;
                }

                for (int segmentIndex = 0; segmentIndex < POINTS_PER_CORNER; segmentIndex++)
                {
                    float radians = (startDegrees - (90f * segmentIndex / ARC_SEGMENTS)) * Mathf.Deg2Rad;
                    var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                    int pointIndex = (cornerIndex * POINTS_PER_CORNER) + segmentIndex;
                    _outerPoints[pointIndex] = centre + (direction * outerRadius);
                    _innerPoints[pointIndex] = centre + (direction * innerRadius);
                }
            }

            _cumulativeLengths[0] = 0f;
            for (int pointIndex = 0; pointIndex < POINT_COUNT; pointIndex++)
            {
                float segmentLength = Vector2.Distance(MidPoint(pointIndex), MidPoint((pointIndex + 1) % POINT_COUNT));
                _cumulativeLengths[pointIndex + 1] = _cumulativeLengths[pointIndex] + segmentLength;
            }
        }
    }
}
