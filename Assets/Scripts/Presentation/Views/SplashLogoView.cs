using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Splash screen mark: three overlapping colour blocks plus the "Blockio Blast" / "TIME RUSH"
    /// wordmark, matching the approved splash mockup. Procedural like the rest of the splash views —
    /// no baked artwork, so it never goes stale when the palette or name changes again.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SplashLogoView : MonoBehaviour
    {
        private const float CLUSTER_Y = 210f;
        private const float TITLE_Y = -40f;
        private const float SUBTITLE_Y = -140f;

        /// <summary>#5E96FF — blue block, drawn first (bottom of the cluster).</summary>
        private static readonly Color32 BlueBlock = new Color32(94, 150, 255, 255);

        /// <summary>#6ED68C — green block, drawn second.</summary>
        private static readonly Color32 GreenBlock = new Color32(110, 214, 140, 255);

        /// <summary>#FF9E50 — orange block, drawn last (top of the cluster).</summary>
        private static readonly Color32 OrangeBlock = new Color32(255, 158, 80, 255);

        /// <summary>#FFD678 — warm accent used by the "TIME RUSH" subtitle.</summary>
        private static readonly Color32 SubtitleColour = new Color32(255, 214, 120, 255);

        private void Awake()
        {
            var rootRect = (RectTransform)transform;

            BuildBlock(rootRect, "Block_Blue", BlueBlock, 190f, new Vector2(-8f, CLUSTER_Y + 26f), -8f);
            BuildBlock(rootRect, "Block_Green", GreenBlock, 140f, new Vector2(-120f, CLUSTER_Y - 40f), -14f);
            BuildBlock(rootRect, "Block_Orange", OrangeBlock, 150f, new Vector2(96f, CLUSTER_Y - 30f), 12f);

            Text title = UiTextFactory.Create(rootRect, "Title", 96, FontStyle.Bold, Color.white);
            title.text = "Blockio Blast";
            var titleRect = (RectTransform)title.transform;
            titleRect.anchoredPosition = new Vector2(0f, TITLE_Y);
            titleRect.sizeDelta = new Vector2(860f, 130f);

            Text subtitle = UiTextFactory.Create(rootRect, "Subtitle", 40, FontStyle.Bold, SubtitleColour);
            subtitle.text = "T I M E   R U S H";
            var subtitleRect = (RectTransform)subtitle.transform;
            subtitleRect.anchoredPosition = new Vector2(0f, SUBTITLE_Y);
            subtitleRect.sizeDelta = new Vector2(860f, 60f);
        }

        private static void BuildBlock(RectTransform parent, string objectName, Color colour, float size, Vector2 position, float rotationDegrees)
        {
            var blockObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var blockRect = (RectTransform)blockObject.transform;
            blockRect.SetParent(parent, false);
            blockRect.anchorMin = new Vector2(0.5f, 0.5f);
            blockRect.anchorMax = new Vector2(0.5f, 0.5f);
            blockRect.pivot = new Vector2(0.5f, 0.5f);
            blockRect.sizeDelta = new Vector2(size, size);
            blockRect.anchoredPosition = position;
            blockRect.localRotation = Quaternion.Euler(0f, 0f, rotationDegrees);

            var image = blockObject.GetComponent<Image>();
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = colour;
            image.raycastTarget = false;
        }
    }
}
