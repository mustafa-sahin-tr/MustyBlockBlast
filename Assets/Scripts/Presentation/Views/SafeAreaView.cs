using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Constrains its own <see cref="RectTransform"/> to <see cref="Screen.safeArea"/> so content
    /// parented underneath it stays clear of notches, cut-outs and rounded corners. Passive layout
    /// component: no Model, no System, nothing injected.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaView : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            // Struct comparisons only — no allocation. Screen size is checked alongside the safe
            // area because the normalised anchors depend on it, and a resize can leave the safe
            // area rect numerically unchanged while the anchors it maps to have moved.
            if (Screen.safeArea == _lastSafeArea
                && Screen.width == _lastScreenWidth
                && Screen.height == _lastScreenHeight)
            {
                return;
            }

            Apply();
        }

        private void Apply()
        {
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            _lastSafeArea = safeArea;
            _lastScreenWidth = screenWidth;
            _lastScreenHeight = screenHeight;

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x /= screenWidth;
            anchorMin.y /= screenHeight;
            anchorMax.x /= screenWidth;
            anchorMax.y /= screenHeight;

            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
        }
    }
}
