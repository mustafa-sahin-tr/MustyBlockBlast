using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The storefront vocabulary the in-game HUD is drawn in (issue #265): card-coloured plates over a
    /// dropped shadow, glossy kind-tinted pills, the striped awning, and the labels that sit on them.
    /// Lifted out of <see cref="SettingsPanelView"/> and <see cref="HubPanelView"/>, which grew the same
    /// primitives for the hub cards, so the score card, the timer pill and the goal chips are built
    /// from the one set rather than each View re-deriving its own radii and shadow drops.
    /// <para>
    /// Everything here builds hierarchy only — nothing is painted. Every Image is created clear and
    /// with its raycast target off: colour arrives from the caller's theme subscription, and taps
    /// arrive through <see cref="BoardInputView"/>'s pointer action rather than an EventSystem, which
    /// this scene has none of. Every rounded Image shares the one rounded-square sprite, sliced to its
    /// own radius, so the whole HUD batches with the rest of the UI.
    /// </para>
    /// </summary>
    internal static class HudChrome
    {
        /// <summary>How far a plate's shadow drops below it, in reference pixels: the mockup's 4-6px
        /// "3D" lip at the canvas's scale.</summary>
        internal const float PLATE_SHADOW_DROP = 8f;

        /// <summary>A pill's shadow is shallower than a card's: the mockup's 3px against 6px.</summary>
        internal const float PILL_SHADOW_DROP = 6f;

        /// <summary>Slice scale of the shop's glossy button sprite when it is stretched into a pill,
        /// the same value the settings and profile cards use so every glossy surface has the same lip.</summary>
        internal const float GLOSSY_SLICE_SCALE = 2.5f;

        /// <summary>How far the well sinks below the card: Ink over CardBackground, as on the settings card.</summary>
        internal const float WELL_TINT = 0.06f;

        /// <summary>How far an accent or kind colour is pulled toward black for a pill's lip or a badge's rim.</summary>
        internal const float LIP_SHADE = 0.35f;

        /// <summary>Alpha of a pill's or card's drop shadow: the mockup's rgba(43,38,51,0.16).</summary>
        internal const float SHADOW_ALPHA = 0.16f;

        /// <summary>Height of the awning's canvas strip, in reference pixels, and of the scalloped hem
        /// hanging under it. The strip covers the top of the score card the way the hub's awning
        /// covers the top of its header.</summary>
        internal const float AWNING_HEIGHT = 44f;
        internal const float AWNING_SCALLOP_HEIGHT = 16f;

        /// <summary>Height of the darker lip along a well's top edge: the mockup's inset 3px shadow.</summary>
        internal const float WELL_LIP = 8f;

        /// <summary>How far a well's lip sinks below its face: a shade deeper than <see cref="WELL_TINT"/>.</summary>
        private const float WELL_LIP_TINT = 0.14f;

        /// <summary>The card-coloured rim around a count badge: the mockup's 3px border.</summary>
        internal const float BADGE_RIM = 8f;

        /// <summary>Fraction of a badge's inner disc its count glyph may fill.</summary>
        private const float BADGE_FONT_FILL = 0.62f;

        /// <summary>Which theme kind paints a "you hold this many" badge and a filled progress track: the
        /// fifth kind is every season's green.</summary>
        internal const int GREEN_KIND = 5;

        /// <summary>
        /// The pink of an offer badge — the "+" on an empty slot, the "x0" on a pocket with no charge.
        /// Fixed rather than themed, and the same figure <see cref="TimerHudView"/> turns its pill under
        /// the low-time threshold: both are "attention, act now" marks, and no season authors a pink of
        /// its own that could be trusted to read as one against its own block fills.
        /// </summary>
        internal static readonly Color OfferPink = new Color(0.94f, 0.38f, 0.57f, 1f);

        /// <summary>Width of one awning stripe-plus-gap or scallop, in reference pixels: the mockup's
        /// 22px tile at the canvas's horizontal scale.</summary>
        private const float AWNING_TILE_WIDTH = 60f;

        /// <summary>The awning's sprites are authored 64px wide (see <see cref="UiSpriteFactory"/>); this
        /// multiplier rescales a tile to <see cref="AWNING_TILE_WIDTH"/>.</summary>
        private const float AWNING_TILE_SCALE = 64f / AWNING_TILE_WIDTH;

        /// <summary>
        /// The parts of a built awning that repaint on a theme change. The canvas and its hem take the
        /// accent; the stripes stay white, since they are the lighter of the two tones by construction.
        /// </summary>
        internal sealed class Awning
        {
            internal Awning(Image plate, Image foot, Image stripes, Image scallops)
            {
                Plate = plate;
                Foot = foot;
                Stripes = stripes;
                Scallops = scallops;
            }

            private Image Plate { get; }

            private Image Foot { get; }

            private Image Stripes { get; }

            private Image Scallops { get; }

            internal void Paint(Color accent)
            {
                Plate.color = accent;
                Foot.color = accent;
                Scallops.color = accent;
                Stripes.color = Color.white;
            }
        }

        internal static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>An empty, centred rect to hang children off.</summary>
        internal static RectTransform CreateRect(RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition)
        {
            var rectObject = new GameObject(objectName, typeof(RectTransform));
            var rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            Centre(rect, size);
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }

        internal static Image BuildRounded(RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition, float radius)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var imageRect = (RectTransform)imageObject.transform;
            imageRect.SetParent(parent, false);
            Centre(imageRect, size);
            imageRect.anchoredPosition = anchoredPosition;
            return ConfigureRounded(imageObject.GetComponent<Image>(), radius);
        }

        internal static Image BuildCircle(RectTransform parent, string objectName, float diameter, Vector2 anchoredPosition)
            => BuildGlyph(parent, objectName, UiSpriteFactory.Circle, new Vector2(diameter, diameter), anchoredPosition);

        internal static Image BuildGlyph(RectTransform parent, string objectName, Sprite sprite, Vector2 size, Vector2 anchoredPosition)
        {
            var glyphObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(parent, false);
            Centre(glyphRect, size);
            glyphRect.anchoredPosition = anchoredPosition;
            return ConfigureGlyph(glyphObject.GetComponent<Image>(), sprite);
        }

        /// <summary>
        /// A plate: a rounded rect in the card colour over a copy of itself dropped a few pixels in the
        /// theme's shadow. The returned rect is the plate's own; children position relative to it, and
        /// it is the tap target. The caller paints <paramref name="shadow"/> with CardShadow and
        /// <paramref name="plate"/> with CardBackground.
        /// </summary>
        internal static RectTransform BuildPlate(
            RectTransform parent,
            string objectName,
            Vector2 size,
            Vector2 anchoredPosition,
            float radius,
            float shadowDrop,
            out Image shadow,
            out Image plate)
        {
            RectTransform rootRect = CreateRect(parent, objectName, size, anchoredPosition);
            shadow = BuildRounded(rootRect, "Shadow", size, new Vector2(0f, -shadowDrop), radius);
            plate = BuildRounded(rootRect, "Plate", size, Vector2.zero, radius);
            return rootRect;
        }

        /// <summary>
        /// A sunken well: a rounded rect in the well tint with a darker lip along its top edge, the
        /// mockup's "inset 0 3px 0" shadow. The lip is the full-size rect and the face a copy of it
        /// shortened by <see cref="WELL_LIP"/> and hung from the bottom, so the lip shows only at the
        /// top. The caller paints them with <see cref="WellTint"/> and <see cref="WellLipTint"/>.
        /// </summary>
        internal static RectTransform BuildWell(
            RectTransform parent,
            string objectName,
            Vector2 size,
            Vector2 anchoredPosition,
            float radius,
            out Image lip,
            out Image face)
        {
            RectTransform rootRect = CreateRect(parent, objectName, size, anchoredPosition);
            lip = BuildRounded(rootRect, "Lip", size, Vector2.zero, radius);
            face = BuildRounded(
                rootRect, "Face", new Vector2(size.x, size.y - WELL_LIP), new Vector2(0f, -WELL_LIP * 0.5f), radius);
            return rootRect;
        }

        /// <summary>
        /// A hollow rounded frame, <paramref name="thickness"/> reference pixels thick at
        /// <paramref name="radius"/>. Drawn from the outline sprite family rather than by hollowing a
        /// sliced rounded square, whose wall would be as thick as its own corner radius.
        /// </summary>
        internal static Image BuildOutline(
            RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition, float radius, float thickness)
        {
            var outlineObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var outlineRect = (RectTransform)outlineObject.transform;
            outlineRect.SetParent(parent, false);
            Centre(outlineRect, size);
            outlineRect.anchoredPosition = anchoredPosition;

            var image = outlineObject.GetComponent<Image>();
            ConfigureOutline(image, radius, thickness);
            return image;
        }

        /// <summary>Slices the outline sprite of the weight that renders <paramref name="thickness"/>
        /// at <paramref name="radius"/>. Stretching one is safe: the wall lives in the nine-slice border.</summary>
        internal static Image ConfigureOutline(Image image, float radius, float thickness)
        {
            float safeRadius = Mathf.Max(1f, radius);
            int spriteThickness = Mathf.RoundToInt(thickness * UiSpriteFactory.ROUNDED_RADIUS / safeRadius);
            image.sprite = UiSpriteFactory.RoundedOutline(spriteThickness);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / safeRadius;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// A count badge: a disc with a card-coloured rim (<see cref="BADGE_RIM"/>) and a short glyph on
        /// it, the mockup's green "x2" / pink "+" chip. Centre-pivoted at
        /// <paramref name="anchoredPosition"/>, which callers put just inside a plate's top-right
        /// corner. The caller paints the rim with CardBackground, the disc with the green kind or
        /// <see cref="OfferPink"/>, and the glyph white.
        /// </summary>
        internal static RectTransform BuildBadge(
            RectTransform parent,
            string objectName,
            float diameter,
            Vector2 anchoredPosition,
            Font font,
            out Image rim,
            out Image disc,
            out Text glyph)
        {
            RectTransform rootRect = CreateRect(parent, objectName, new Vector2(diameter, diameter), anchoredPosition);
            rim = BuildCircle(rootRect, "Rim", diameter, Vector2.zero);
            float discDiameter = Mathf.Max(1f, diameter - (BADGE_RIM * 2f));
            disc = BuildCircle(rootRect, "Disc", discDiameter, Vector2.zero);

            int fontSize = Mathf.Max(1, Mathf.RoundToInt(discDiameter * BADGE_FONT_FILL));
            glyph = UiTextFactory.Create(rootRect, "Glyph", fontSize, FontStyle.Bold, Color.clear, font);
            ((RectTransform)glyph.transform).sizeDelta = new Vector2(diameter, diameter);
            return rootRect;
        }

        /// <summary>A plate whose corners are fully round: a pill.</summary>
        internal static RectTransform BuildPill(
            RectTransform parent,
            string objectName,
            Vector2 size,
            Vector2 anchoredPosition,
            out Image shadow,
            out Image plate)
            => BuildPlate(parent, objectName, size, anchoredPosition, size.y * 0.5f, PILL_SHADOW_DROP, out shadow, out plate);

        /// <summary>
        /// A glossy pill: the shop's white button sprite, sliced, tinted at paint time with a kind's
        /// fill — its baked highlight and lip supply the bevel. Without the sprite it falls back to a
        /// flat pill in the same tint. The returned Image is also the pill's rect.
        /// </summary>
        internal static Image BuildGlossyPill(
            RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition, Sprite buttonSprite)
        {
            var pillObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var pillRect = (RectTransform)pillObject.transform;
            pillRect.SetParent(parent, false);
            Centre(pillRect, size);
            pillRect.anchoredPosition = anchoredPosition;

            var plate = pillObject.GetComponent<Image>();
            if (buttonSprite != null)
            {
                plate.sprite = buttonSprite;
                plate.type = Image.Type.Sliced;
                plate.pixelsPerUnitMultiplier = GLOSSY_SLICE_SCALE;
                plate.color = Color.clear;
                plate.raycastTarget = false;
            }
            else
            {
                ConfigureRounded(plate, size.y * 0.5f);
            }

            return plate;
        }

        /// <summary>
        /// The striped awning across the top of a card: an accent canvas with its top corners on the
        /// card's radius, lighter stripes tiled over it, and a scalloped hem hanging under its bottom
        /// edge. Hung from the card's top edge, <paramref name="width"/> wide. Built from primitives
        /// rather than the shop's authored awning because that one is baked red, and this one has to
        /// take the season's accent.
        /// </summary>
        internal static Awning BuildAwning(RectTransform cardRect, float width, float cornerRadius)
        {
            var rootObject = new GameObject("Awning", typeof(RectTransform));
            var rootRect = (RectTransform)rootObject.transform;
            rootRect.SetParent(cardRect, false);
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.sizeDelta = new Vector2(width, AWNING_HEIGHT);
            rootRect.anchoredPosition = Vector2.zero;

            // The canvas is a rounded rect for its top corners plus a square-cornered foot over its
            // lower half, so the bottom edge the hem hangs from is straight.
            Image plate = BuildRounded(
                rootRect, "Canvas", new Vector2(width, AWNING_HEIGHT), new Vector2(0f, -AWNING_HEIGHT * 0.5f), cornerRadius);
            float footHeight = AWNING_HEIGHT * 0.5f;
            Image foot = BuildRounded(
                rootRect, "CanvasFoot", new Vector2(width, footHeight), new Vector2(0f, -AWNING_HEIGHT + (footHeight * 0.5f)), 2f);

            // Inset by the corner radius so no stripe pokes out past the rounded top corners.
            float stripesWidth = Mathf.Max(0f, width - (cornerRadius * 2f));
            Image stripes = BuildTiled(
                rootRect, "Stripes", UiSpriteFactory.AwningStripes,
                new Vector2(stripesWidth, AWNING_HEIGHT), new Vector2(0f, -AWNING_HEIGHT * 0.5f));

            // Whole scallops only: a cut lobe at either end would read as a tear in the hem.
            float scallopsWidth = Mathf.Floor(width / AWNING_TILE_WIDTH) * AWNING_TILE_WIDTH;
            Image scallops = BuildTiled(
                rootRect, "Scallops", UiSpriteFactory.AwningScallops,
                new Vector2(scallopsWidth, AWNING_SCALLOP_HEIGHT),
                new Vector2(0f, -AWNING_HEIGHT - (AWNING_SCALLOP_HEIGHT * 0.5f)));

            return new Awning(plate, foot, stripes, scallops);
        }

        /// <summary>
        /// Builds a wordless label. Pivoted on its aligned edge so the anchored position is that edge,
        /// whatever the string ends up measuring — labels overflow their rect by design (see
        /// <see cref="UiTextFactory"/>).
        /// </summary>
        internal static Text CreateLabel(
            RectTransform parent,
            string objectName,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 anchoredPosition,
            Font font)
        {
            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, Color.clear, font);
            text.alignment = alignment;

            var rect = (RectTransform)text.transform;
            float pivotX = alignment == TextAnchor.MiddleRight ? 1f : (alignment == TextAnchor.MiddleLeft ? 0f : 0.5f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.sizeDelta = new Vector2(0f, fontSize * 1.4f);
            rect.anchoredPosition = anchoredPosition;
            return text;
        }

        /// <summary>
        /// A glossy block tile as the board draws one: a shade in the fill's darker tone, the face
        /// lifted off the bottom edge by <paramref name="bevel"/> so the shade reads as the tile's
        /// underside, and a translucent white highlight across the top third. Painted at build time
        /// from <paramref name="fill"/> (alpha kept on every layer), for the splash screen's static
        /// palette; themed tiles stay with <see cref="CellView"/>.
        /// </summary>
        internal static RectTransform BuildBlock(
            RectTransform parent, string objectName, float size, Vector2 anchoredPosition, float radius, float bevel, Color fill)
        {
            RectTransform rootRect = CreateRect(parent, objectName, new Vector2(size, size), anchoredPosition);

            Image shade = BuildRounded(rootRect, "Shade", new Vector2(size, size), Vector2.zero, radius);
            shade.color = Darken(fill, LIP_SHADE);

            Image face = BuildRounded(
                rootRect, "Face", new Vector2(size, size - bevel), new Vector2(0f, bevel * 0.5f), radius);
            face.color = fill;

            float highlightHeight = Mathf.Max(2f, size * 0.32f);
            Image highlight = BuildRounded(
                rootRect, "Highlight", new Vector2(size * 0.76f, highlightHeight),
                new Vector2(0f, (size * 0.5f) - (highlightHeight * 0.5f) - (size * 0.08f)), highlightHeight * 0.5f);
            highlight.color = new Color(1f, 1f, 1f, 0.4f * fill.a);
            return rootRect;
        }

        internal static Image ConfigureRounded(Image image, float radius)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / Mathf.Max(1f, radius);
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A non-interactive picture: aspect kept, no raycast, painted later. The circle,
        /// star and check sprites have no border, so they must never be sliced.</summary>
        internal static Image ConfigureGlyph(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>The sunken well plates sit in: Ink pulled a little over CardBackground.</summary>
        internal static Color WellTint(Color cardBackground, Color ink) => Color.Lerp(cardBackground, ink, WELL_TINT);

        /// <summary>The lip along a well's top edge: a shade deeper than the well itself.</summary>
        internal static Color WellLipTint(Color cardBackground, Color ink) => Color.Lerp(cardBackground, ink, WELL_LIP_TINT);

        /// <summary>The same hue, pulled toward black — a shade of the colour itself, not a blend with
        /// the ink, so a blue-inked season still gets a gold lip rather than an olive one.</summary>
        internal static Color Darken(Color colour, float amount)
            => new Color(colour.r * (1f - amount), colour.g * (1f - amount), colour.b * (1f - amount), colour.a);

        internal static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        private static Image BuildTiled(RectTransform parent, string objectName, Sprite sprite, Vector2 size, Vector2 anchoredPosition)
        {
            var tiledObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var tiledRect = (RectTransform)tiledObject.transform;
            tiledRect.SetParent(parent, false);
            Centre(tiledRect, size);
            tiledRect.anchoredPosition = anchoredPosition;

            var image = tiledObject.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Tiled;
            image.pixelsPerUnitMultiplier = AWNING_TILE_SCALE;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }
    }
}
