using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Shared construction of the runtime placeholder UI pieces (cells, cards).</summary>
    internal static class CellFactory
    {
        /// <summary>A cell's corner radius as a fraction of its side: issue #528's near-sharp bevelled
        /// block (option A), shared by empty cells and rings so they stay flush with it. Derived rather
        /// than serialized so a cell at any size — board, tray, pocket, drag ghost — has the same corner.</summary>
        internal const float CORNER_RADIUS_FRACTION = 0.07f;

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
            cellView.Build(UiSpriteFactory.RoundedSquare, inset, bevelThickness, size * CORNER_RADIUS_FRACTION);
            return cellView;
        }

        /// <summary>Default <c>pixelsPerUnitMultiplier</c> a card's corner is sliced at — see
        /// <see cref="CreateCard"/>. Lower reads rounder; this is the corner every card used before the
        /// info-card family (issue mockup pass, 2026-09) asked for a noticeably rounder one of its own.</summary>
        private const float DEFAULT_CARD_CORNER_MULTIPLIER = 1.4f;

        /// <summary>
        /// Rounded card with a soft offset shadow behind it. Returns the card rect; content should be
        /// parented to it. Both Images start fully transparent and are handed back so the caller can
        /// paint them from the current theme — cards are built in Awake, before the theme is known.
        /// </summary>
        /// <param name="cornerRadiusMultiplier">
        /// The <see cref="Image.pixelsPerUnitMultiplier"/> the card's rounded corner is sliced at — a
        /// lower value reads as a rounder corner. Defaults to every other card's corner
        /// (<see cref="DEFAULT_CARD_CORNER_MULTIPLIER"/>); pass a smaller value for a card that should
        /// read rounder than the rest of the family, such as <see cref="InfoCardChrome"/>'s.
        /// </param>
        internal static RectTransform CreateCard(
            RectTransform parent,
            string cardName,
            Vector2 size,
            out Image background,
            out Image shadow,
            float cornerRadiusMultiplier = DEFAULT_CARD_CORNER_MULTIPLIER)
        {
            var shadowObject = new GameObject(cardName + "Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(parent, false);
            shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
            shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
            shadowRect.sizeDelta = size + new Vector2(10f, 10f);
            shadowRect.anchoredPosition = new Vector2(0f, -8f);
            shadow = shadowObject.GetComponent<Image>();
            ConfigureCardImage(shadow, cornerRadiusMultiplier);

            var cardObject = new GameObject(cardName, typeof(RectTransform), typeof(Image));
            var cardRect = (RectTransform)cardObject.transform;
            cardRect.SetParent(parent, false);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = size;
            background = cardObject.GetComponent<Image>();
            ConfigureCardImage(background, cornerRadiusMultiplier);

            return cardRect;
        }

        private static void ConfigureCardImage(Image image, float cornerRadiusMultiplier)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = cornerRadiusMultiplier;
            image.color = Color.clear;
            image.raycastTarget = false;
        }
    }
}
