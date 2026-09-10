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
    /// Pure visual — it is told a colour, it never decides one.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class CellView : MonoBehaviour
    {
        private const int FACET_COUNT = 4;

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

        private static void ConfigureSliced(Image image, Sprite roundedSprite)
        {
            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
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
