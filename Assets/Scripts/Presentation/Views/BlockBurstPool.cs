using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Fixed-size pool of small square Images used by <see cref="LineClearBurstView"/>. Every square
    /// is created once up front and toggled with <see cref="GameObject.SetActive"/>; nothing is ever
    /// instantiated or destroyed while a burst plays.
    /// </summary>
    internal sealed class BlockBurstPool
    {
        private readonly RectTransform[] _rects;
        private readonly Image[] _images;

        private int _activeCount;

        internal BlockBurstPool(RectTransform parent, int capacity, Sprite sprite)
        {
            _rects = new RectTransform[capacity];
            _images = new Image[capacity];

            for (int i = 0; i < capacity; i++)
            {
                var particleObject = new GameObject($"BurstParticle_{i}", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)particleObject.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;

                var image = particleObject.GetComponent<Image>();
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.raycastTarget = false;

                particleObject.SetActive(false);

                _rects[i] = rect;
                _images[i] = image;
            }
        }

        internal RectTransform GetRect(int index) => _rects[index];

        internal Image GetImage(int index) => _images[index];

        /// <summary>Shows the first <paramref name="count"/> squares and hides the rest.</summary>
        internal void Activate(int count)
        {
            int clamped = Mathf.Clamp(count, 0, _rects.Length);
            for (int i = 0; i < _rects.Length; i++)
            {
                _rects[i].gameObject.SetActive(i < clamped);
            }

            _activeCount = clamped;
        }

        internal void DeactivateAll()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                _rects[i].gameObject.SetActive(false);
            }

            _activeCount = 0;
        }
    }
}
