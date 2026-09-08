using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Full-screen soft lavender-to-peach gradient behind everything else.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class BackgroundView : MonoBehaviour
    {
        [Header("Palette")]
        [SerializeField] private BlockPalette _palette;

        private void Awake()
        {
            if (_palette == null)
            {
                _palette = BlockPalette.CreateDefault();
            }

            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            transform.SetAsFirstSibling();

            var image = GetComponent<Image>();
            image.sprite = UiSpriteFactory.CreateVerticalGradient(_palette.BackgroundBottom, _palette.BackgroundTop);
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
        }
    }
}
