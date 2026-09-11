using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Bottom caption cluster of the splash screen: three loading dots, the "tap to skip" hint and
    /// the note that the boot jingle follows the device sound setting. Built once in
    /// <see cref="Awake"/> and never animated — the mockup's dots are static, only their opacity
    /// steps down left to right.
    /// <para>
    /// Purely informative: it neither reads nor drives <see cref="SplashSystem"/>, so the skip hint
    /// can never desynchronise the transition it describes.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SplashCaptionView : MonoBehaviour
    {
        private const int DOT_COUNT = 3;
        private const float DOT_SIZE = 18f;
        private const float DOT_SPACING = 34f;

        private const float CLUSTER_HEIGHT = 380f;
        private const float DOTS_Y = 262f;
        private const float SKIP_TEXT_Y = 180f;
        private const float JINGLE_TEXT_Y = 92f;

        private const int SKIP_FONT_SIZE = 40;
        private const int JINGLE_FONT_SIZE = 30;

        /// <summary>#2A6920 — the dark green used by the dots and the skip hint.</summary>
        private static readonly Color DarkGreen = new Color32(42, 105, 32, 255);

        /// <summary>#79A568 — the lighter sage used by the jingle note.</summary>
        private static readonly Color SageGreen = new Color32(121, 165, 104, 255);

        /// <summary>Dot opacities, fading left to right exactly as in the mockup.</summary>
        private static readonly float[] DotAlphas = { 0.85f, 0.55f, 0.3f };

        private void Awake()
        {
            var rootRect = (RectTransform)transform;
            rootRect.anchorMin = new Vector2(0f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            rootRect.sizeDelta = new Vector2(0f, CLUSTER_HEIGHT);
            rootRect.anchoredPosition = Vector2.zero;

            BuildDots(rootRect);
            BuildCaption(rootRect, "SkipHint", "Geçmek için dokun", SKIP_FONT_SIZE, FontStyle.Bold, DarkGreen, 0.75f, SKIP_TEXT_Y);
            BuildCaption(rootRect, "JingleNote", "Açılış cıngılı ses ayarınıza uyar", JINGLE_FONT_SIZE, FontStyle.Normal, SageGreen, 1f, JINGLE_TEXT_Y);
        }

        private static void BuildDots(RectTransform parent)
        {
            var rowObject = new GameObject("LoadingDots", typeof(RectTransform));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(parent, false);
            rowRect.anchorMin = new Vector2(0.5f, 0f);
            rowRect.anchorMax = new Vector2(0.5f, 0f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.sizeDelta = Vector2.zero;
            rowRect.anchoredPosition = new Vector2(0f, DOTS_Y);

            float firstX = -DOT_SPACING * (DOT_COUNT - 1) * 0.5f;

            for (int dotIndex = 0; dotIndex < DOT_COUNT; dotIndex++)
            {
                var dotObject = new GameObject($"Dot_{dotIndex}", typeof(RectTransform), typeof(Image));
                var dotRect = (RectTransform)dotObject.transform;
                dotRect.SetParent(rowRect, false);
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.sizeDelta = new Vector2(DOT_SIZE, DOT_SIZE);
                dotRect.anchoredPosition = new Vector2(firstX + (dotIndex * DOT_SPACING), 0f);

                Color colour = DarkGreen;
                colour.a = DotAlphas[dotIndex];

                var image = dotObject.GetComponent<Image>();
                image.sprite = UiSpriteFactory.Circle;
                image.type = Image.Type.Simple;
                image.color = colour;
                image.raycastTarget = false;
            }
        }

        private static void BuildCaption(
            RectTransform parent,
            string objectName,
            string content,
            int fontSize,
            FontStyle fontStyle,
            Color colour,
            float alpha,
            float y)
        {
            colour.a = alpha;

            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, colour);
            text.text = content;

            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, fontSize * 1.4f);
            rect.anchoredPosition = new Vector2(0f, y);
        }
    }
}
