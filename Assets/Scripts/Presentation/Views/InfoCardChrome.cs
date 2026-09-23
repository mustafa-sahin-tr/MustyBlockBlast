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

        /// <summary>Mirrors <see cref="TOP_PADDING"/> below the description, so the card reads as
        /// symmetrically padded top and bottom whatever height <see cref="Reflow"/> settles on.</summary>
        private const float BOTTOM_PADDING = 60f;

        /// <summary>Horizontal margin the description label wraps within, so a long sentence breaks
        /// onto a second line instead of overflowing past the card's rounded edge.</summary>
        private const float DESCRIPTION_SIDE_PADDING = 70f;

        /// <summary>
        /// The floating circular close button's pieces: the plate, its shadow, the two × bars and a
        /// hit-test rect matching the plate's footprint. Handed back so the owning card can repaint all
        /// of them from its theme and answer "was this tap on close?" itself.
        /// </summary>
        internal sealed class CloseButtonHandles
        {
            /// <summary>Hit-test rect for the close tap — the same footprint as the close plate.</summary>
            internal RectTransform HitRect;

            /// <summary>Transparent graphic filling <see cref="HitRect"/>. Only an EventSystem-driven
            /// card needs it, so it starts disabled and is enabled (and made a raycast target) by such
            /// a card.</summary>
            internal Image HitImage;

            internal Image PlateImage;
            internal Image PlateShadowImage;

            /// <summary>The two rotated bars making the × — theme Ink, same as every other card.</summary>
            internal readonly List<Image> BarImages = new List<Image>(2);
        }

        /// <summary>Every Image and Text this chrome builds, handed back so the caller can parent its
        /// own hero content and repaint everything from the current theme on <c>Refresh</c>.</summary>
        internal sealed class Handles
        {
            internal RectTransform CardRect;
            internal Image CardImage;
            internal Image CardShadowImage;

            /// <summary>The floating circular close button — see <see cref="CloseButtonHandles"/>.</summary>
            internal CloseButtonHandles Close;

            /// <summary>Parent for the caller's own icon/glyph content, pre-sized and centred on the
            /// hero plate so content only has to size itself to <see cref="HERO_CONTENT_SIZE"/>.</summary>
            internal RectTransform HeroContentRect;
            internal Image HeroRingImage;
            internal Image HeroPlateImage;

            /// <summary>The whole hero footprint (ring + plate + content) — repositioned by
            /// <see cref="Reflow"/> whenever the card's measured height changes.</summary>
            internal RectTransform HeroRootRect;

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

            handles.Close = CreateFloatingCloseButton(handles.CardRect);
            CreateHero(handles);

            handles.TitleText = CreateLabel(
                handles.CardRect, "Title", titleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            handles.DescriptionText = CreateLabel(
                handles.CardRect, "Description", descriptionFontSize, FontStyle.Normal, TextAnchor.MiddleCenter);

            // Wrapped rather than left to overflow (UiTextFactory's default): a full-sentence
            // description at this font size routinely runs wider than the card, and an
            // un-clipped overflow would draw straight past the card's rounded edge and, on a
            // narrow phone, off the visible screen entirely. The wrap width only depends on the
            // card's (fixed) width, so it is set once here rather than in Reflow.
            handles.DescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var descriptionRect = (RectTransform)handles.DescriptionText.transform;
            descriptionRect.sizeDelta = new Vector2(
                cardSize.x - (DESCRIPTION_SIDE_PADDING * 2f), descriptionFontSize * 2f);

            // Lays everything out for the placeholder (empty) label text Build() hands back — the
            // caller's first Refresh() immediately re-runs Reflow with the real title/description so
            // the card is never shown at this placeholder height.
            Reflow(handles, cardSize);

            return handles;
        }

        /// <summary>
        /// Recomputes the card's height from the title and description's <i>actual</i> measured
        /// heights (<see cref="Text.preferredHeight"/>, valid only once each label's final text and
        /// wrap width are set) and repositions every piece of chrome that sits below the card's top
        /// edge accordingly. Never shrinks below <paramref name="baseCardSize"/> — that is the size
        /// tuned for a short, one-line description, and stays the floor so a short card never looks
        /// unnecessarily sparse.
        /// <para>
        /// Called once by <see cref="Build"/> with placeholder (empty) text, and again by each caller's
        /// <c>Refresh()</c> after the real localized title/description strings are assigned — this is
        /// the only place a wrapped description can grow the card downward instead of overlapping the
        /// title above it.
        /// </para>
        /// </summary>
        internal static void Reflow(Handles handles, Vector2 baseCardSize)
        {
            float titleHeight = Mathf.Max(handles.TitleText.preferredHeight, handles.TitleText.fontSize * 1.2f);
            float descriptionHeight = Mathf.Max(
                handles.DescriptionText.preferredHeight, handles.DescriptionText.fontSize * 1.2f);

            float contentHeight = TOP_PADDING + HERO_SIZE + HERO_TO_TITLE_GAP + titleHeight
                + TITLE_TO_DESCRIPTION_GAP + descriptionHeight + BOTTOM_PADDING;
            float cardHeight = Mathf.Max(baseCardSize.y, contentHeight);
            var cardSize = new Vector2(baseCardSize.x, cardHeight);

            handles.CardRect.sizeDelta = cardSize;
            ((RectTransform)handles.CardShadowImage.transform).sizeDelta = cardSize + new Vector2(10f, 10f);

            float cardHalfWidth = cardSize.x * 0.5f;
            float cardHalfHeight = cardHeight * 0.5f;

            PositionFloatingCloseButton(handles.Close, new Vector2(cardHalfWidth, cardHalfHeight));

            float cursorY = cardHalfHeight - TOP_PADDING;
            float heroCenterY = cursorY - (HERO_SIZE * 0.5f);
            handles.HeroRootRect.anchoredPosition = new Vector2(0f, heroCenterY);
            cursorY = heroCenterY - (HERO_SIZE * 0.5f) - HERO_TO_TITLE_GAP;

            float titleHalfHeight = titleHeight * 0.5f;
            float titleCenterY = cursorY - titleHalfHeight;
            ((RectTransform)handles.TitleText.transform).anchoredPosition = new Vector2(0f, titleCenterY);
            cursorY = titleCenterY - titleHalfHeight - TITLE_TO_DESCRIPTION_GAP;

            float descriptionHalfHeight = descriptionHeight * 0.5f;
            float descriptionCenterY = cursorY - descriptionHalfHeight;
            var descriptionRect = (RectTransform)handles.DescriptionText.transform;
            descriptionRect.anchoredPosition = new Vector2(0f, descriptionCenterY);
            descriptionRect.sizeDelta = new Vector2(descriptionRect.sizeDelta.x, descriptionHeight);
        }

        /// <summary>
        /// Creates the close button's Images and hit-test rect, parented under
        /// <paramref name="cardRect"/> but not yet positioned —
        /// <see cref="PositionFloatingCloseButton"/> places them once the card's final height for this
        /// repaint is known.
        /// <para>
        /// Exposed (rather than kept private to <see cref="Build"/>) so a card outside this family that
        /// wants the same floating close treatment — the level path overlay, per issue #416 — takes the
        /// button itself instead of hand-rolling a second, square one that then drifts from this spec.
        /// </para>
        /// </summary>
        internal static CloseButtonHandles CreateFloatingCloseButton(RectTransform cardRect)
        {
            var close = new CloseButtonHandles();

            var shadowObject = new GameObject("CloseButtonShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(cardRect, false);
            Centre(shadowRect, new Vector2(CLOSE_PLATE_SIZE, CLOSE_PLATE_SIZE) + new Vector2(10f, 10f));
            close.PlateShadowImage = shadowObject.GetComponent<Image>();
            ConfigureCircle(close.PlateShadowImage);

            var plateObject = new GameObject("CloseButtonPlate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(cardRect, false);
            Centre(plateRect, new Vector2(CLOSE_PLATE_SIZE, CLOSE_PLATE_SIZE));
            close.PlateImage = plateObject.GetComponent<Image>();
            ConfigureCircle(close.PlateImage);

            // The hit rect carries a transparent circle of its own so an EventSystem-driven caller has
            // a graphic to raycast against. It starts disabled — a card hit-tested by BoardInputView,
            // which every card this chrome was written for is, needs no graphic there and should not
            // pay for an invisible one.
            var closeObject = new GameObject("CloseButton", typeof(RectTransform), typeof(Image));
            close.HitRect = (RectTransform)closeObject.transform;
            close.HitRect.SetParent(cardRect, false);
            Centre(close.HitRect, new Vector2(CLOSE_PLATE_SIZE, CLOSE_PLATE_SIZE));
            close.HitImage = closeObject.GetComponent<Image>();
            ConfigureCircle(close.HitImage);
            close.HitImage.enabled = false;

            for (int barIndex = 0; barIndex < 2; barIndex++)
            {
                var barObject = new GameObject($"CloseBar_{barIndex}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform)barObject.transform;
                barRect.SetParent(close.HitRect, false);
                Centre(barRect, new Vector2(CLOSE_CROSS_LENGTH, CLOSE_CROSS_THICKNESS));
                barRect.localRotation = Quaternion.Euler(0f, 0f, barIndex == 0 ? 45f : -45f);

                Image barImage = barObject.GetComponent<Image>();
                ConfigureRoundedBar(barImage);
                close.BarImages.Add(barImage);
            }

            return close;
        }

        /// <summary>
        /// Moves the already-created close button pieces so the plate's centre sits
        /// <see cref="CLOSE_PLATE_CORNER_OVERHANG_FRACTION"/> of its own radius inside the card's
        /// top-right corner, with the × bars centred on top of it — the card's own shadow treatment,
        /// floating partly outside the card. Callable repeatedly as <paramref name="cardCorner"/>
        /// changes with the card's measured height.
        /// </summary>
        internal static void PositionFloatingCloseButton(CloseButtonHandles close, Vector2 cardCorner)
        {
            float plateRadius = CLOSE_PLATE_SIZE * 0.5f;
            float overhang = plateRadius * CLOSE_PLATE_CORNER_OVERHANG_FRACTION;
            var centre = new Vector2(cardCorner.x - overhang, cardCorner.y - overhang);

            ((RectTransform)close.PlateShadowImage.transform).anchoredPosition = centre + new Vector2(0f, -8f);
            ((RectTransform)close.PlateImage.transform).anchoredPosition = centre;
            close.HitRect.anchoredPosition = centre;
        }

        /// <summary>Creates the ring (outer, lighter), the plate (inner, inset) and a content root the
        /// caller parents its own icon/glyph into — see <see cref="HERO_CONTENT_SIZE"/>. Not yet
        /// positioned; <see cref="Reflow"/> places <see cref="Handles.HeroRootRect"/> once the card's
        /// final height for this repaint is known.</summary>
        private static void CreateHero(Handles handles)
        {
            var heroRootObject = new GameObject("HeroIcon", typeof(RectTransform));
            handles.HeroRootRect = (RectTransform)heroRootObject.transform;
            handles.HeroRootRect.SetParent(handles.CardRect, false);
            Centre(handles.HeroRootRect, new Vector2(HERO_SIZE, HERO_SIZE));

            var ringObject = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            var ringRect = (RectTransform)ringObject.transform;
            ringRect.SetParent(handles.HeroRootRect, false);
            Centre(ringRect, new Vector2(HERO_SIZE, HERO_SIZE));
            handles.HeroRingImage = ringObject.GetComponent<Image>();
            ConfigureCircle(handles.HeroRingImage);

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(handles.HeroRootRect, false);
            Centre(plateRect, new Vector2(HERO_PLATE_SIZE, HERO_PLATE_SIZE));
            handles.HeroPlateImage = plateObject.GetComponent<Image>();
            ConfigureCircle(handles.HeroPlateImage);

            var contentObject = new GameObject("Content", typeof(RectTransform));
            handles.HeroContentRect = (RectTransform)contentObject.transform;
            handles.HeroContentRect.SetParent(handles.HeroRootRect, false);
            Centre(handles.HeroContentRect, new Vector2(HERO_PLATE_SIZE, HERO_PLATE_SIZE));
        }

        /// <summary>Builds a wordless label at the origin; the caller's <c>Refresh</c> fills it in from
        /// the model and <see cref="Reflow"/> places it, because every label on this card carries a
        /// value and every label's vertical position depends on measured content height.</summary>
        private static Text CreateLabel(
            RectTransform parent,
            string objectName,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment)
        {
            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, Color.clear);
            text.alignment = alignment;

            var rect = (RectTransform)text.transform;

            // Pivot on the aligned edge so the anchored position is that edge, whatever the string ends
            // up measuring — labels overflow their rect by design (see UiTextFactory).
            float pivotX = alignment == TextAnchor.MiddleRight ? 1f : (alignment == TextAnchor.MiddleLeft ? 0f : 0.5f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.sizeDelta = new Vector2(0f, fontSize * 1.6f);
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
