using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// One rounded board/tray square, able to render in two looks that are both built once and
    /// toggled (never rebuilt), because cells are reused across redraws, previews and fades:
    /// <list type="bullet">
    /// <item>Flat — two stacked Images (outer shade silhouette, inset inner face). Used for empty
    /// board cells and for the flat preview tint.</item>
    /// <item>Embossed — four rotated triangle facets (top/left lit, right/bottom shaded) with a
    /// symmetrically inset face square drawn on top, which covers the facets' pointed centre tips
    /// and leaves the four trapezoid bevels visible. Used for filled piece cells.</item>
    /// </list>
    /// On top of both sits an independent outline layer (see <see cref="SetHighlight"/>) that is
    /// toggled on its own and never disturbs the colours of the two looks underneath.
    /// Pure visual — it is told a colour, it never decides one.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class CellView : MonoBehaviour
    {
        private const int FACET_COUNT = 4;

        /// <summary>Lower than the shared layers' multiplier (see <see cref="ConfigureSliced"/>) so the
        /// nine-slice border renders noticeably thicker than the cell's other frame lines, keeping the
        /// would-clear outline legible at a glance.</summary>
        private const float HighlightPixelsPerUnitMultiplier = 1.25f;

        /// <summary>How many bevel thicknesses the special-cell icon is inset by, on top of the cell's
        /// own inset. Two keeps the icon clear of the four bevel facets on every side, so it sits on
        /// the block's flat face at any cell size.</summary>
        private const float SPECIAL_ICON_BEVEL_INSET_MULTIPLIER = 2f;

        /// <summary>Facet index order, matching a -90 degree step per index from the top facet.</summary>
        private const int FACET_TOP = 0;
        private const int FACET_RIGHT = 1;
        private const int FACET_BOTTOM = 2;
        private const int FACET_LEFT = 3;

        private readonly Image[] _facetImages = new Image[FACET_COUNT];

        private Image _outerImage;
        private Image _flatFaceImage;
        private GameObject _embossRoot;
        private Image _embossFaceImage;
        private Image _specialIconImage;
        private Image _highlightImage;

        private void Awake() => CacheOuter();

        /// <summary>Creates both layer sets. Called by the builder right after AddComponent.</summary>
        internal void Build(Sprite roundedSprite, float inset, float bevelThickness)
        {
            CacheOuter();
            ConfigureSliced(_outerImage, roundedSprite);

            _flatFaceImage = CreateStretchedImage(transform, "Face");
            ConfigureSliced(_flatFaceImage, roundedSprite);
            SetStretchInsets((RectTransform)_flatFaceImage.transform, inset, inset + bevelThickness, inset, inset);

            var embossObject = new GameObject("Emboss", typeof(RectTransform));
            _embossRoot = embossObject;
            var embossRect = (RectTransform)embossObject.transform;
            embossRect.SetParent(transform, false);
            StretchToParent(embossRect);

            for (int facetIndex = 0; facetIndex < FACET_COUNT; facetIndex++)
            {
                Image facet = CreateStretchedImage(embossRect, $"Facet_{facetIndex}");
                facet.sprite = UiSpriteFactory.TriangleFacet;
                facet.type = Image.Type.Simple;
                facet.raycastTarget = false;

                var facetRect = (RectTransform)facet.transform;
                facetRect.localRotation = Quaternion.Euler(0f, 0f, -90f * facetIndex);
                _facetImages[facetIndex] = facet;
            }

            // Built last so it is the last sibling and therefore drawn over the facet tips.
            _embossFaceImage = CreateStretchedImage(embossRect, "EmbossFace");
            ConfigureSliced(_embossFaceImage, roundedSprite);

            float faceInset = inset + bevelThickness;
            SetStretchInsets((RectTransform)_embossFaceImage.transform, faceInset, faceInset, faceInset, faceInset);

            _embossRoot.SetActive(false);

            // Built after both looks so it draws on top of whichever is active. In practice only an
            // occupied (embossed) cell ever wears one — a special kind belongs to the block standing on
            // the cell — but it is parented to the cell rather than to the emboss root so toggling
            // looks can never take the icon down with it. Inset well inside the bevel so it reads as a
            // mark on the block's face rather than as a second silhouette.
            _specialIconImage = CreateStretchedImage(transform, "SpecialIcon");
            _specialIconImage.sprite = UiSpriteFactory.Starburst;
            _specialIconImage.type = Image.Type.Simple;
            _specialIconImage.raycastTarget = false;
            _specialIconImage.color = Color.clear;

            float iconInset = inset + (bevelThickness * SPECIAL_ICON_BEVEL_INSET_MULTIPLIER);
            SetStretchInsets((RectTransform)_specialIconImage.transform, iconInset, iconInset, iconInset, iconInset);
            _specialIconImage.gameObject.SetActive(false);

            // Built after the emboss root so it is the cell's last sibling and therefore draws over
            // whichever look is active. fillCenter is off, so the sliced sprite renders only its
            // nine-slice border: a constant-thickness rounded frame with a hollow middle. Reusing
            // RoundedSquare keeps it on the same texture as every other cell layer, so the outline
            // costs no extra draw call and its corner radius matches the cell silhouette exactly.
            _highlightImage = CreateStretchedImage(transform, "Highlight");
            ConfigureSliced(_highlightImage, roundedSprite, HighlightPixelsPerUnitMultiplier);
            _highlightImage.fillCenter = false;
            _highlightImage.color = Color.clear;
            _highlightImage.gameObject.SetActive(false);
        }

        /// <summary>Shows the outline frame in <paramref name="colour"/>. Independent of both looks:
        /// it neither reads nor writes the face/fill/shade layers, so a cell can be tinted, embossed
        /// or empty underneath an outline.</summary>
        internal void SetHighlight(Color colour)
        {
            if (_highlightImage == null)
            {
                return;
            }

            _highlightImage.color = colour;

            if (!_highlightImage.gameObject.activeSelf)
            {
                _highlightImage.gameObject.SetActive(true);
            }
        }

        /// <summary>Hides the outline frame. Safe to call on a cell that never had one.</summary>
        internal void ClearHighlight()
        {
            if (_highlightImage == null || !_highlightImage.gameObject.activeSelf)
            {
                return;
            }

            _highlightImage.gameObject.SetActive(false);
        }

        /// <summary>Shows the special-cell icon in <paramref name="colour"/>. Independent of both looks
        /// and of the outline, exactly as <see cref="SetHighlight"/> is: it neither reads nor writes any
        /// other layer, and allocates nothing, so it is safe on any repaint path.</summary>
        internal void SetSpecialIcon(Color colour) => SetSpecialIcon(colour, UiSpriteFactory.Starburst);

        /// <summary>
        /// As <see cref="SetSpecialIcon(Color)"/>, but drawing <paramref name="sprite"/> instead of the
        /// default starburst — the dock plates mark a
        /// <see cref="MustyBlockBlast.Core.SpecialPieceKind"/> with its own glyph rather than with a
        /// tint, because a piece that is tapped rather than dragged has to be told apart by shape.
        /// Allocates nothing, so it is safe on any repaint path.
        /// </summary>
        internal void SetSpecialIcon(Color colour, Sprite sprite)
        {
            if (_specialIconImage == null)
            {
                return;
            }

            if (sprite != null)
            {
                _specialIconImage.sprite = sprite;
            }

            _specialIconImage.color = colour;

            if (!_specialIconImage.gameObject.activeSelf)
            {
                _specialIconImage.gameObject.SetActive(true);
            }
        }

        /// <summary>Hides the special-cell icon. Safe to call on a cell that never had one.</summary>
        internal void ClearSpecialIcon()
        {
            if (_specialIconImage == null || !_specialIconImage.gameObject.activeSelf)
            {
                return;
            }

            _specialIconImage.gameObject.SetActive(false);
        }

        /// <summary>Flat two-layer look: empty cells and the drag preview tint.</summary>
        internal void SetColours(Color face, Color shade)
        {
            CacheOuter();
            _outerImage.enabled = true;
            _outerImage.color = shade;

            if (_flatFaceImage != null)
            {
                _flatFaceImage.gameObject.SetActive(true);
                _flatFaceImage.color = face;
            }

            if (_embossRoot != null)
            {
                _embossRoot.SetActive(false);
            }
        }

        /// <summary>Four-facet embossed look: filled piece cells on the board, tray and ghost.</summary>
        internal void SetEmbossedColours(Color fill, Color highlight, Color shade)
        {
            CacheOuter();
            _outerImage.enabled = false;

            if (_flatFaceImage != null)
            {
                _flatFaceImage.gameObject.SetActive(false);
            }

            if (_embossRoot == null)
            {
                return;
            }

            _embossRoot.SetActive(true);
            _facetImages[FACET_TOP].color = highlight;
            _facetImages[FACET_LEFT].color = highlight;
            _facetImages[FACET_RIGHT].color = shade;
            _facetImages[FACET_BOTTOM].color = shade;
            _embossFaceImage.color = fill;
        }

        /// <summary>Applies one alpha to every layer of both looks, so whichever is showing fades
        /// uniformly and the other cannot come back at a stale opacity.</summary>
        internal void SetAlpha(float alpha)
        {
            CacheOuter();
            ApplyAlpha(_outerImage, alpha);
            ApplyAlpha(_flatFaceImage, alpha);
            ApplyAlpha(_embossFaceImage, alpha);

            // The outline fades with the rest so a highlighted cell cannot stay solid mid-fade. Its
            // own alpha is restored in full by the next SetHighlight call.
            ApplyAlpha(_highlightImage, alpha);

            // The icon likewise: a destroyed special cell fades out as one block, never as a fading
            // block with a solid mark left floating over it. Restored by the next SetSpecialIcon call.
            ApplyAlpha(_specialIconImage, alpha);

            for (int facetIndex = 0; facetIndex < FACET_COUNT; facetIndex++)
            {
                ApplyAlpha(_facetImages[facetIndex], alpha);
            }
        }

        private static void ApplyAlpha(Image image, float alpha)
        {
            if (image == null)
            {
                return;
            }

            Color colour = image.color;
            colour.a = alpha;
            image.color = colour;
        }

        private static void ConfigureSliced(Image image, Sprite roundedSprite, float pixelsPerUnitMultiplier = 3f)
        {
            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            image.raycastTarget = false;
        }

        private static Image CreateStretchedImage(Transform parent, string objectName)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)imageObject.transform;
            rect.SetParent(parent, false);
            StretchToParent(rect);
            return imageObject.GetComponent<Image>();
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetStretchInsets(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void CacheOuter()
        {
            if (_outerImage == null)
            {
                _outerImage = GetComponent<Image>();
            }
        }
    }
}
