using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Shared construction of the runtime placeholder UI pieces (cells, cards).</summary>
    internal static class CellFactory
    {
        internal static CellView CreateCell(
            Transform parent, string cellName, float size, float inset, float bevelThickness)
        {
            var cellObject = new GameObject(cellName, typeof(RectTransform), typeof(Image), typeof(CellView));
            var rect = (RectTransform)cellObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            var cellView = cellObject.GetComponent<CellView>();
            cellView.Build(UiSpriteFactory.RoundedSquare, inset, bevelThickness);
            return cellView;
        }

        /// <summary>
        /// Rounded card with a soft offset shadow behind it. Returns the card rect; content should be
        /// parented to it. Both Images start fully transparent and are handed back so the caller can
        /// paint them from the current theme — cards are built in Awake, before the theme is known.
        /// </summary>
        internal static RectTransform CreateCard(
            RectTransform parent, string cardName, Vector2 size, out Image background, out Image shadow)
        {
            var shadowObject = new GameObject(cardName + "Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(parent, false);
            shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
            shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
            shadowRect.sizeDelta = size + new Vector2(10f, 10f);
            shadowRect.anchoredPosition = new Vector2(0f, -8f);
            shadow = shadowObject.GetComponent<Image>();
            ConfigureCardImage(shadow);

            var cardObject = new GameObject(cardName, typeof(RectTransform), typeof(Image));
            var cardRect = (RectTransform)cardObject.transform;
            cardRect.SetParent(parent, false);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = size;
            background = cardObject.GetComponent<Image>();
            ConfigureCardImage(background);

            return cardRect;
        }

        private static void ConfigureCardImage(Image image)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.4f;
            image.color = Color.clear;
            image.raycastTarget = false;
        }
    }
}
