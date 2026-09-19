using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Splash screen mark in the storefront language (issue #267): a card plate with a 3D drop shadow,
    /// a sunken well holding the same L-shaped block mark as the app icon, the "BLOCKIO BLAST" wordmark
    /// in the display face and a gold "TIME RUSH" pill under it. Procedural like the rest of the splash
    /// views — no baked artwork — and painted from constants that mirror the İlkbahar theme, because
    /// the splash scene boots before any theme state exists.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SplashLogoView : MonoBehaviour
    {
        private const float CARD_WIDTH = 830f;
        private const float CARD_HEIGHT = 720f;
        private const float CARD_RADIUS = 80f;
        private const float CARD_SHADOW_DROP = 26f;

        private const float WELL_SIZE = 340f;
        private const float WELL_RADIUS = 60f;
        private const float WELL_Y = 150f;

        private const float TILE_SIZE = 84f;
        private const float TILE_STEP = 96f;
        private const float TILE_RADIUS = 22f;
        private const float TILE_BEVEL = 14f;

        private const float TITLE_Y = -120f;
        private const float TITLE_LINE_GAP = 84f;
        private const int TITLE_FONT_SIZE = 84;

        private const float SUBTITLE_Y = -270f;
        private const float SUBTITLE_WIDTH = 300f;
        private const float SUBTITLE_HEIGHT = 74f;
        private const int SUBTITLE_FONT_SIZE = 34;

        /// <summary>#FDFCFB — İlkbahar cardBackground.</summary>
        private static readonly Color CardColour = new Color32(253, 252, 251, 255);

        /// <summary>rgba(43,38,51,0.16) — the mockup's card drop shadow.</summary>
        private static readonly Color CardShadow = new Color(0.17f, 0.15f, 0.2f, 0.16f);

        /// <summary>#33691E — İlkbahar ink, the wordmark colour.</summary>
        private static readonly Color Ink = new Color32(51, 105, 30, 255);

        /// <summary>#E0A72E — İlkbahar accent, the "TIME RUSH" pill.</summary>
        private static readonly Color Accent = new Color32(224, 167, 46, 255);

        /// <summary>The icon's L mark, in the classic red-yellow-blue palette (issue #327).</summary>
        private static readonly Color RedTile = new Color32(229, 57, 53, 255);
        private static readonly Color BlueTile = new Color32(30, 136, 229, 255);

        [Header("Fonts")]
        [Tooltip("Display face for the wordmark and the TIME RUSH pill (Bowlby One SC). Falls back to the builtin font.")]
        [SerializeField] private Font _displayFont;

        private void Awake()
        {
            var rootRect = (RectTransform)transform;

            RectTransform cardRect = HudChrome.BuildPlate(
                rootRect, "Card", new Vector2(CARD_WIDTH, CARD_HEIGHT), Vector2.zero, CARD_RADIUS, CARD_SHADOW_DROP,
                out Image cardShadow, out Image cardPlate);
            cardShadow.color = CardShadow;
            cardPlate.color = CardColour;

            BuildMark(cardRect);
            BuildWordmark(cardRect);
            BuildSubtitlePill(cardRect);
        }

        /// <summary>The sunken well with the icon's mark: three red tiles in an L plus one blue tile.</summary>
        private static void BuildMark(RectTransform cardRect)
        {
            RectTransform wellRect = HudChrome.BuildWell(
                cardRect, "MarkWell", new Vector2(WELL_SIZE, WELL_SIZE), new Vector2(0f, WELL_Y), WELL_RADIUS,
                out Image lip, out Image face);
            lip.color = HudChrome.WellLipTint(CardColour, Ink);
            face.color = HudChrome.WellTint(CardColour, Ink);

            // Three rows centred on the well: the L's column sits half a step left, its foot half a step right.
            float half = TILE_STEP * 0.5f;
            HudChrome.BuildBlock(wellRect, "Tile_TopLeft", TILE_SIZE, new Vector2(-half, TILE_STEP), TILE_RADIUS, TILE_BEVEL, RedTile);
            HudChrome.BuildBlock(wellRect, "Tile_MidLeft", TILE_SIZE, new Vector2(-half, 0f), TILE_RADIUS, TILE_BEVEL, RedTile);
            HudChrome.BuildBlock(wellRect, "Tile_BottomLeft", TILE_SIZE, new Vector2(-half, -TILE_STEP), TILE_RADIUS, TILE_BEVEL, RedTile);
            HudChrome.BuildBlock(wellRect, "Tile_BottomRight", TILE_SIZE, new Vector2(half, -TILE_STEP), TILE_RADIUS, TILE_BEVEL, BlueTile);
        }

        private void BuildWordmark(RectTransform cardRect)
        {
            Text first = HudChrome.CreateLabel(
                cardRect, "Title_Blockio", TITLE_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, TITLE_Y), _displayFont);
            first.text = "BLOCKIO";
            first.color = Ink;

            Text second = HudChrome.CreateLabel(
                cardRect, "Title_Blast", TITLE_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, TITLE_Y - TITLE_LINE_GAP), _displayFont);
            second.text = "BLAST";
            second.color = Ink;
        }

        /// <summary>The gold "TIME RUSH" pill: accent plate over its darker lip, white display text.</summary>
        private void BuildSubtitlePill(RectTransform cardRect)
        {
            RectTransform pillRect = HudChrome.BuildPill(
                cardRect, "SubtitlePill", new Vector2(SUBTITLE_WIDTH, SUBTITLE_HEIGHT), new Vector2(0f, SUBTITLE_Y),
                out Image lip, out Image plate);
            lip.color = HudChrome.Darken(Accent, HudChrome.LIP_SHADE);
            plate.color = Accent;

            Text subtitle = HudChrome.CreateLabel(
                pillRect, "Subtitle", SUBTITLE_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, _displayFont);
            subtitle.text = "TIME RUSH";
            subtitle.color = Color.white;
        }
    }
}
