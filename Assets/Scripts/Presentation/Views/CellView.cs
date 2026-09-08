using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// One rounded board/tray square. Two stacked Images: the outer one is the bevel/outline shade,
    /// the inner one (inset, with a deeper bottom inset) is the face. Pure visual — it is told a
    /// colour, it never decides one.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class CellView : MonoBehaviour
    {
        private Image _outerImage;
        private Image _innerImage;

        private void Awake() => CacheOuter();

        /// <summary>Creates the inner face. Called by the builder right after AddComponent.</summary>
        internal void Build(Sprite roundedSprite, float inset, float bevelThickness)
        {
            CacheOuter();
            _outerImage.sprite = roundedSprite;
            _outerImage.type = Image.Type.Sliced;
            _outerImage.pixelsPerUnitMultiplier = 3f;
            _outerImage.raycastTarget = false;

            var innerObject = new GameObject("Face", typeof(RectTransform), typeof(Image));
            var innerRect = (RectTransform)innerObject.transform;
            innerRect.SetParent(transform, false);
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(inset, inset + bevelThickness);
            innerRect.offsetMax = new Vector2(-inset, -inset);

            _innerImage = innerObject.GetComponent<Image>();
            _innerImage.sprite = roundedSprite;
            _innerImage.type = Image.Type.Sliced;
            _innerImage.pixelsPerUnitMultiplier = 3f;
            _innerImage.raycastTarget = false;
        }

        internal void SetColours(Color face, Color shade)
        {
            CacheOuter();
            _outerImage.color = shade;
            if (_innerImage != null)
            {
                _innerImage.color = face;
            }
        }

        internal void SetAlpha(float alpha)
        {
            CacheOuter();
            Color outer = _outerImage.color;
            outer.a = alpha;
            _outerImage.color = outer;

            if (_innerImage != null)
            {
                Color inner = _innerImage.color;
                inner.a = alpha;
                _innerImage.color = inner;
            }
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
