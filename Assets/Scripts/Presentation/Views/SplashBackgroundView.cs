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
        /// <summary>#4A3A8C — violet-blue at the top of the screen.</summary>
        private static readonly Color TopColour = new Color32(74, 58, 140, 255);

        /// <summary>#211F49 — deep indigo at the bottom of the screen.</summary>
        private static readonly Color BottomColour = new Color32(33, 31, 73, 255);

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
