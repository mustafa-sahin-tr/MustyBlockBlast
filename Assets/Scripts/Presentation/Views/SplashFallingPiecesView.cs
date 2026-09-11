using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Decorative layer of block-piece silhouettes drifting down behind the splash logo. Every piece
    /// and every cell Image is built once in <see cref="Awake"/>; <see cref="Update"/> only mutates
    /// cached <see cref="RectTransform"/> state, so the layer never allocates per frame.
    /// <para>
    /// Purely cosmetic: nothing is injected, nothing is published, and every Image is a non-raycast
    /// target so tap-to-skip is unaffected.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SplashFallingPiecesView : MonoBehaviour
    {
        private const int PIECE_COUNT = 12;
        private const int MAX_CELLS_PER_PIECE = 4;

        /// <summary>Side of one cell, in canvas units at the 1080x1920 reference resolution.</summary>
        private const float CELL_SIZE = 34f;

        /// <summary>Cell pitch — <see cref="CELL_SIZE"/> plus the gap that keeps cells reading as a grid.</summary>
        private const float CELL_STEP = 38f;

        /// <summary>How far above the top / below the bottom a piece spawns and despawns.</summary>
        private const float VERTICAL_MARGIN = 240f;

        private const float MAX_TILT_DEGREES = 14f;

        /// <summary>Piece palette from the approved mockup: pink, green, purple, orange.</summary>
        private static readonly Color[] PieceColours =
        {
            new Color32(240, 98, 156, 255),
            new Color32(102, 187, 106, 255),
            new Color32(186, 104, 200, 255),
            new Color32(251, 140, 0, 255),
        };

        /// <summary>
        /// Cell offsets (column, row) for each silhouette: 2x2 square, 3/4-cell lines in both
        /// orientations, and L/J shapes cut out of a 2x2 or 3x2 grid.
        /// </summary>
        private static readonly Vector2Int[][] Shapes =
        {
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(0, 3) },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(1, 2) },
            new[] { new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(1, 2), new Vector2Int(0, 2) },
        };

        /// <summary>Scattered horizontal positions (0 = left edge, 1 = right edge) from the mockup.</summary>
        private static readonly float[] HorizontalPositions =
        {
            0f, 0.15f, 0.22f, 0.26f, 0.34f, 0.45f, 0.5f, 0.56f, 0.66f, 0.76f, 0.88f, 0.95f,
        };

        /// <summary>Staggered fall durations so no two pieces travel in lockstep.</summary>
        private static readonly float[] FallDurations =
        {
            3.2f, 2.4f, 3.8f, 2.8f, 3.4f, 2.2f, 3.6f, 2.6f, 4f, 3f, 2.9f, 3.5f,
        };

        private readonly RectTransform[] _pieceRects = new RectTransform[PIECE_COUNT];
        private readonly float[] _pieceProgress = new float[PIECE_COUNT];
        private readonly float[] _pieceTiltSign = new float[PIECE_COUNT];

        private RectTransform _layerRect;

        private void Awake()
        {
            var rootRect = (RectTransform)transform;
            Stretch(rootRect);

            // Own nested Canvas so the per-frame motion below rebuilds only this layer's mesh and
            // never the splash canvas that holds the logo and the captions.
            var layerObject = new GameObject("FallingPiecesLayer", typeof(RectTransform), typeof(Canvas));
            _layerRect = (RectTransform)layerObject.transform;
            _layerRect.SetParent(rootRect, false);
            Stretch(_layerRect);

            for (int pieceIndex = 0; pieceIndex < PIECE_COUNT; pieceIndex++)
            {
                BuildPiece(pieceIndex);
            }

            Layout();
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            for (int pieceIndex = 0; pieceIndex < PIECE_COUNT; pieceIndex++)
            {
                float progress = _pieceProgress[pieceIndex] + (deltaTime / FallDurations[pieceIndex]);
                while (progress >= 1f)
                {
                    progress -= 1f;
                }

                _pieceProgress[pieceIndex] = progress;
            }

            Layout();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void BuildPiece(int pieceIndex)
        {
            var pieceObject = new GameObject($"FallingPiece_{pieceIndex}", typeof(RectTransform));
            var pieceRect = (RectTransform)pieceObject.transform;
            pieceRect.SetParent(_layerRect, false);
            pieceRect.anchorMin = new Vector2(0.5f, 0.5f);
            pieceRect.anchorMax = new Vector2(0.5f, 0.5f);
            pieceRect.pivot = new Vector2(0.5f, 0.5f);
            pieceRect.sizeDelta = Vector2.zero;

            Vector2Int[] shape = Shapes[pieceIndex % Shapes.Length];
            Color colour = PieceColours[pieceIndex % PieceColours.Length];

            // 0.35 .. 0.50, the opacity band the mockup uses for the background pieces.
            colour.a = 0.35f + ((pieceIndex % 4) * 0.05f);

            int maxColumn = 0;
            int maxRow = 0;
            for (int cellIndex = 0; cellIndex < shape.Length; cellIndex++)
            {
                maxColumn = Mathf.Max(maxColumn, shape[cellIndex].x);
                maxRow = Mathf.Max(maxRow, shape[cellIndex].y);
            }

            float centreColumn = maxColumn * 0.5f;
            float centreRow = maxRow * 0.5f;

            for (int cellIndex = 0; cellIndex < MAX_CELLS_PER_PIECE; cellIndex++)
            {
                if (cellIndex >= shape.Length)
                {
                    break;
                }

                var cellObject = new GameObject($"Cell_{cellIndex}", typeof(RectTransform), typeof(Image));
                var cellRect = (RectTransform)cellObject.transform;
                cellRect.SetParent(pieceRect, false);
                cellRect.anchorMin = new Vector2(0.5f, 0.5f);
                cellRect.anchorMax = new Vector2(0.5f, 0.5f);
                cellRect.pivot = new Vector2(0.5f, 0.5f);
                cellRect.sizeDelta = new Vector2(CELL_SIZE, CELL_SIZE);
                cellRect.anchoredPosition = new Vector2(
                    (shape[cellIndex].x - centreColumn) * CELL_STEP,
                    -(shape[cellIndex].y - centreRow) * CELL_STEP);

                var image = cellObject.GetComponent<Image>();
                image.sprite = UiSpriteFactory.RoundedSquare;
                image.type = Image.Type.Sliced;
                // ~11 canvas-unit corner radius on a 34-unit cell — the mockup's small rounded square.
                image.pixelsPerUnitMultiplier = 1.5f;
                image.color = colour;
                image.raycastTarget = false;
            }

            _pieceRects[pieceIndex] = pieceRect;

            // Deterministic stagger: an irrational-ish step spreads the start phases without ever
            // repeating across the twelve pieces.
            _pieceProgress[pieceIndex] = (pieceIndex * 0.37f) % 1f;
            _pieceTiltSign[pieceIndex] = (pieceIndex % 2 == 0) ? 1f : -1f;
        }

        private void Layout()
        {
            Rect layerRect = _layerRect.rect;
            float width = layerRect.width;
            float topY = (layerRect.height * 0.5f) + VERTICAL_MARGIN;
            float bottomY = -topY;

            for (int pieceIndex = 0; pieceIndex < PIECE_COUNT; pieceIndex++)
            {
                float progress = _pieceProgress[pieceIndex];
                float x = (HorizontalPositions[pieceIndex] - 0.5f) * width;
                float y = Mathf.Lerp(topY, bottomY, progress);

                RectTransform pieceRect = _pieceRects[pieceIndex];
                pieceRect.anchoredPosition = new Vector2(x, y);

                float tilt = Mathf.Lerp(-MAX_TILT_DEGREES, MAX_TILT_DEGREES, progress)
                    * _pieceTiltSign[pieceIndex];
                pieceRect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
        }
    }
}
