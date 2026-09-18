using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Full-bleed vertical gradient behind the whole splash screen. Deliberately static rather than
    /// reactive: the splash scene boots before any settings/theme state exists, so the two brand
    /// colours from the approved splash mockup are baked in here.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class SplashBackgroundView : MonoBehaviour
    {
        /// <summary>#A5D6A7 — the İlkbahar theme\'s backgroundTop (Ilkbahar.asset), baked here because the
        /// splash boots before any theme state exists (issue #267).</summary>
        private static readonly Color TopColour = new Color32(165, 214, 167, 255);

        /// <summary>#FFECB3 — the İlkbahar theme\'s backgroundBottom.</summary>
        private static readonly Color BottomColour = new Color32(255, 236, 179, 255);

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            transform.SetAsFirstSibling();

            var image = GetComponent<Image>();
            image.sprite = UiSpriteFactory.CreateVerticalGradient(BottomColour, TopColour);
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
        }
    }
}
