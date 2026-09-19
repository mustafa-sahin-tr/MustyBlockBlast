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
        /// <summary>#1565C0 — vivid deep blue, baked here because the splash boots before any theme
        /// state exists (issue #267, brightened to a classic red-yellow-blue palette per issue #327).</summary>
        private static readonly Color TopColour = new Color32(21, 101, 192, 255);

        /// <summary>#29B6F6 — vivid sky blue (issue #327).</summary>
        private static readonly Color BottomColour = new Color32(41, 182, 246, 255);

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
