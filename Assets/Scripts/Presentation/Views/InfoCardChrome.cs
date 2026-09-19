using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Shared visual chrome for the "info card" family of modals — <see cref="InfoPopupView"/> and
    /// <see cref="ObjectiveInfoPopupView"/>. Both cards read as one family, matched against the same
    /// mockup: a noticeably rounder card than the rest of the overlay family, a large circular hero
    /// icon plate with a soft lighter ring behind it, a bold title centred directly under that icon,
    /// a smaller regular-weight description below the title, and a close button that floats as its
    /// own circular plate — with the card's own shadow treatment — partly outside the card's top-right
    /// corner.
    /// <para>
    /// Factored out so the two Views, which used to build near-identical procedural UI independently,
    /// can never drift apart on this shared spec again. Each View still owns its own hero icon
    /// <i>content</i> — illustrated art, a procedural glyph, or a sprite borrowed from another View —
    /// since that part is genuinely different between the two; only the surrounding chrome (card, ring,
    /// plate, close button, title/description labels) lives here.
    /// </para>
    /// </summary>
    internal static class InfoCardChrome
    {
        /// <summary>Corner radius multiplier this card family slices at — rounder than the
        /// <see cref="CellFactory"/> default every other card (badges, settings, level path, ...)
        /// uses, per the mockup's more pronounced rounded-rect look.</summary>
        private const float CARD_CORNER_MULTIPLIER = 0.85f;

        /// <summary>Outer diameter of the hero icon's soft ring — the whole hero footprint. Noticeably
        /// bigger relative to the card than the old flat icon plate.</summary>
        internal const float HERO_SIZE = 260f;

        /// <summary>Diameter of the hero icon's inner plate, inset from the ring so the ring reads as a
        /// visible border rather than being fully hidden behind the plate.</summary>
        internal const float HERO_PLATE_SIZE = HERO_SIZE * 0.86f;

        /// <summary>Footprint a caller's icon/glyph content should target inside
        /// <see cref="Handles.HeroContentRect"/>, matching the old glyph-to-plate ratio.</summary>
        internal const float HERO_CONTENT_SIZE = HERO_PLATE_SIZE * 0.58f;

        private const float CLOSE_PLATE_SIZE = 100f;
        private const float CLOSE_CROSS_LENGTH = 40f;
        private const float CLOSE_CROSS_THICKNESS = 7f;

        /// <summary>How far the close plate's centre sits inside the card's top-right corner, as a
        /// fraction of the plate's own radius — small enough that roughly half the plate floats
        /// outside the card on each axis, matching the mockup's floating close button.</summary>
        private const float CLOSE_PLATE_CORNER_OVERHANG_FRACTION = 0.45f;

        private const float TOP_PADDING = 60f;
        private const float HERO_TO_TITLE_GAP = 20f;
        private const float TITLE_TO_DESCRIPTION_GAP = 12f;

        /// <summary>Horizontal margin the description label wraps within, so a long sentence breaks
        /// onto a second line instead of overflowing past the card's rounded edge.</summary>
        private const float DESCRIPTION_SIDE_PADDING = 70f;

        /// <summary>Every Image and Text this chrome builds, handed back so the caller can parent its
        /// own hero content and repaint everything from the current theme on <c>Refresh</c>.</summary>
        internal sealed class Handles
        {
            internal RectTransform CardRect;
            internal Image CardImage;
            internal Image CardShadowImage;

            /// <summary>Hit-test rect for the close tap — the same footprint as the close plate.</summary>
            internal RectTransform CloseButtonRect;
            internal Image ClosePlateImage;
            internal Image ClosePlateShadowImage;

            /// <summary>The two rotated bars making the × — theme Ink, same as every other card.</summary>
            internal readonly List<Image> CloseBarImages = new List<Image>(2);

            /// <summary>Parent for the caller's own icon/glyph content, pre-sized and centred on the
            /// hero plate so content only has to size itself to <see cref="HERO_CONTENT_SIZE"/>.</summary>
            internal RectTransform HeroContentRect;
            internal Image HeroRingImage;
            internal Image HeroPlateImage;

            internal Text TitleText;
            internal Text DescriptionText;
        }

        /// <summary>
        /// Builds the whole shared chrome — card, close button, hero ring/plate and the title/description
        /// labels stacked under it — parented to <paramref name="panelRoot"/>. Every Image starts fully
        /// transparent (this runs before the theme is known), and every Text starts as an empty
        /// wordless label — the caller's own <c>Refresh</c> paints both from the model and the theme.
        /// </summary>
        internal static Handles Build(
            RectTransform panelRoot, string cardName, Vector2 cardSize, int titleFontSize, int descriptionFontSize)
        {
            var handles = new Handles();

            handles.CardRect = CellFactory.CreateCard(
                panelRoot, cardName, cardSize, out handles.CardImage, out handles.CardShadowImage,
                CARD_CORNER_MULTIPLIER);

            float cardHalfWidth = cardSize.x * 0.5f;
            float cardHalfHeight = cardSize.y * 0.5f;

            BuildCloseButton(handles, new Vector2(cardHalfWidth, cardHalfHeight));

            float cursorY = cardHalfHeight - TOP_PADDING;
            float heroCenterY = cursorY - (HERO_SIZE * 0.5f);
            BuildHero(handles, new Vector2(0f, heroCenterY));
            cursorY = heroCenterY - (HERO_SIZE * 0.5f) - HERO_TO_TITLE_GAP;

            float titleHalfHeight = titleFontSize * 0.6f;
            float titleCenterY = cursorY - titleHalfHeight;
            handles.TitleText = CreateLabel(
                handles.CardRect, "Title", titleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, titleCenterY));
            cursorY = titleCenterY - titleHalfHeight - TITLE_TO_DESCRIPTION_GAP;

            float descriptionHalfHeight = descriptionFontSize * 0.9f;
            float descriptionCenterY = cursorY - descriptionHalfHeight;
            handles.DescriptionText = CreateLabel(
                handles.CardRect, "Description", descriptionFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, descriptionCenterY));

            // Wrapped rather than left to overflow (UiTextFactory's default): a full-sentence
            // description at this font size routinely runs wider than the card, and an
            // un-clipped overflow would draw straight past the card's rounded edge and, on a
            // narrow phone, off the visible screen entirely.
            handles.DescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var descriptionRect = (RectTransform)handles.DescriptionText.transform;
            descriptionRect.sizeDelta = new Vector2(
                cardSize.x - (DESCRIPTION_SIDE_PADDING * 2f), descriptionFontSize * 2f);

            return handles;
        }

        /// <summary>
        /// A separate circular plate — with the same offset shadow <see cref="CellFactory.CreateCard"/>
        /// gives the main card — floating so its centre sits <see cref="CLOSE_PLATE_CORNER_OVERHANG_FRACTION"/>
        /// of its own radius inside the card's top-right corner, with the × bars centred on top of it.
        /// </summary>
        private static void BuildCloseButton(Handles handles, Vector2 cardCorner)
        {
            float plateRadius = CLOSE_PLATE_SIZE * 0.5f;
            float overhang = plateRadius * CLOSE_PLATE_CORNER_OVERHANG_FRACTION;
            var centre = new Vector2(cardCorner.x - overhang, cardCorner.y - overhang);

            var shadowObject = new GameObject("CloseButtonShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(handles.CardRect, false);
            Centre(shadowRect, new Vector2(CLOSE_PLATE_SIZE, CLOSE_PLATE_SIZE) + new Vector2(10f, 10f));
            shadowRect.anchoredPosition = centre + new Vector2(0f, -8f);
            handles.ClosePlateShadowImage = shadowObject.GetComponent<Image>();
            ConfigureCircle(handles.ClosePlateShadowImage);

            var plateObject = new GameObject("CloseButtonPlate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(handles.CardRect, false);
            Centre(plateRect, new Vector2(CLOSE_PLATE_SIZE, CLOSE_PLATE_SIZE));
            plateRect.anchoredPosition = centre;
            handles.ClosePlateImage = plateObject.GetComponent<Image>();
            ConfigureCircle(handles.ClosePlateImage);

            var closeObject = new GameObject("CloseButton", typeof(RectTransform));
            handles.CloseButtonRect = (RectTransform)closeObject.transform;
            handles.CloseButtonRect.SetParent(handles.CardRect, false);
            Centre(handles.CloseButtonRect, new Vector2(CLOSE_PLATE_SIZE, CLOSE_PLATE_SIZE));
            handles.CloseButtonRect.anchoredPosition = centre;

            for (int barIndex = 0; barIndex < 2; barIndex++)
            {
                var barObject = new GameObject($"CloseBar_{barIndex}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform)barObject.transform;
                barRect.SetParent(handles.CloseButtonRect, false);
                Centre(barRect, new Vector2(CLOSE_CROSS_LENGTH, CLOSE_CROSS_THICKNESS));
                barRect.localRotation = Quaternion.Euler(0f, 0f, barIndex == 0 ? 45f : -45f);

                Image barImage = barObject.GetComponent<Image>();
                ConfigureRoundedBar(barImage);
                handles.CloseBarImages.Add(barImage);
            }
        }

        /// <summary>The ring (outer, lighter), the plate (inner, inset) and a content root the caller
        /// parents its own icon/glyph into — see <see cref="HERO_CONTENT_SIZE"/>.</summary>
        private static void BuildHero(Handles handles, Vector2 anchoredPosition)
        {
            var heroRootObject = new GameObject("HeroIcon", typeof(RectTransform));
            var heroRootRect = (RectTransform)heroRootObject.transform;
            heroRootRect.SetParent(handles.CardRect, false);
            Centre(heroRootRect, new Vector2(HERO_SIZE, HERO_SIZE));
            heroRootRect.anchoredPosition = anchoredPosition;

            var ringObject = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            var ringRect = (RectTransform)ringObject.transform;
            ringRect.SetParent(heroRootRect, false);
            Centre(ringRect, new Vector2(HERO_SIZE, HERO_SIZE));
            handles.HeroRingImage = ringObject.GetComponent<Image>();
            ConfigureCircle(handles.HeroRingImage);

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(heroRootRect, false);
            Centre(plateRect, new Vector2(HERO_PLATE_SIZE, HERO_PLATE_SIZE));
            handles.HeroPlateImage = plateObject.GetComponent<Image>();
            ConfigureCircle(handles.HeroPlateImage);

            var contentObject = new GameObject("Content", typeof(RectTransform));
            handles.HeroContentRect = (RectTransform)contentObject.transform;
            handles.HeroContentRect.SetParent(heroRootRect, false);
            Centre(handles.HeroContentRect, new Vector2(HERO_PLATE_SIZE, HERO_PLATE_SIZE));
        }

        /// <summary>Builds a wordless label; the caller's <c>Refresh</c> fills it in from the model,
        /// because every label on this card carries a value.</summary>
        private static Text CreateLabel(
            RectTransform parent,
            string objectName,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 anchoredPosition)
        {
            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, Color.clear);
            text.alignment = alignment;

            var rect = (RectTransform)text.transform;

            // Pivot on the aligned edge so the anchored position is that edge, whatever the string ends
            // up measuring — labels overflow their rect by design (see UiTextFactory).
            float pivotX = alignment == TextAnchor.MiddleRight ? 1f : (alignment == TextAnchor.MiddleLeft ? 0f : 0.5f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.sizeDelta = new Vector2(0f, fontSize * 1.6f);
            rect.anchoredPosition = anchoredPosition;
            return text;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>A perfect circle rather than a sliced rounded square — the mockup's hero plate and
        /// close button are both round, not just round-cornered.</summary>
        private static void ConfigureCircle(Image image)
        {
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.clear;
            image.raycastTarget = false;
        }

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not
        // through an EventSystem, and this scene has none.
        private static void ConfigureRoundedBar(Image image)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = Color.clear;
            image.raycastTarget = false;
        }
    }
}
