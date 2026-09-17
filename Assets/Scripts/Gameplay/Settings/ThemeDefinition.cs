using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// A single selectable visual theme. Static config, so it lives in a ScriptableObject; one asset
    /// per theme (Yaz, Kış, ...) and the selected one is held by <c>SettingsModel</c>.
    /// <para>
    /// This is the single source of colour for every View: background, cards, cells, piece kinds,
    /// text and drag previews. Views subscribe to <c>SettingsModel.CurrentTheme</c> and repaint
    /// whenever the selected theme changes.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Theme Definition", fileName = "ThemeDefinition")]
    public sealed class ThemeDefinition : ScriptableObject
    {
        /// <summary>Piece "kinds" — colour ids are 1..KIND_COUNT and are cosmetic only.</summary>
        /// <summary>How many piece colours a theme authors. An alias of the palette's single source of
        /// truth, <see cref="MustyBlockBlast.Core.Board.COLOUR_COUNT"/>, kept under this name for the
        /// Views that size their kind rows from it.</summary>
        public const int KIND_COUNT = MustyBlockBlast.Core.Board.COLOUR_COUNT;

        [Header("Identity")]
        [Tooltip("Stable id persisted to PlayerPrefs. Must be unique and must never change once shipped.")]
        [SerializeField] private int _id;

        [Tooltip("Editor-facing fallback name, used only if TranslationKey is unset. Player-facing " +
            "text should come from TranslationKey via LocalizationSystem instead.")]
        [SerializeField] private string _displayName;

        [Tooltip("LocalizationKeys constant resolved by LocalizationSystem.Translate to get the " +
            "player-facing, locale-specific theme name.")]
        [SerializeField] private string _translationKey;

        [Header("Surfaces")]
        [SerializeField] private Color _backgroundTop = FromHex(0xEDE9F5);
        [SerializeField] private Color _backgroundBottom = FromHex(0xF7E9DD);
        [SerializeField] private Color _cardBackground = FromHex(0xFDFCFB);
        [SerializeField] private Color _cardShadow = new Color(0.17f, 0.15f, 0.20f, 0.10f);

        [Header("Cells")]
        [SerializeField] private Color _emptyCellFill = FromHex(0xE6E4EC);
        [SerializeField] private Color _emptyCellOutline = FromHex(0xD6D3E0);

        [Header("Piece kinds (fill / bevel highlight / bevel shade)")]
        [SerializeField]
        private Color[] _kindFills =
        {
            FromHex(0xE8785A),
            FromHex(0x6FB8B0),
            FromHex(0xE0C36B),
            FromHex(0x8E7CC3),
            FromHex(0x7BC47F),
        };

        [SerializeField]
        private Color[] _kindHighlights =
        {
            FromHex(0xF6A48D),
            FromHex(0x9AD8D1),
            FromHex(0xF0DDA0),
            FromHex(0xB5A8DC),
            FromHex(0xA6DBA8),
        };

        [SerializeField]
        private Color[] _kindShades =
        {
            FromHex(0xC25A3F),
            FromHex(0x4C948C),
            FromHex(0xC29F49),
            FromHex(0x6A58A3),
            FromHex(0x559E5B),
        };

        [Header("Text")]
        [SerializeField] private Color _ink = FromHex(0x2B2733);
        [SerializeField] private Color _softInk = FromHex(0x6E6878);

        [Tooltip("Score label highlight colour — distinct from Ink so the score reads as an accent, not flat body text.")]
        [SerializeField] private Color _accent = FromHex(0xE0A72E);

        [Header("Level Path Trail")]
        [Tooltip("The winding ribbon the level nodes sit on. This is the colour that carries the " +
            "season on the level path card, so it should read as this theme's ground: snow, sand, " +
            "leaf litter, new grass.")]
        [SerializeField] private Color _trailPathColor = FromHex(0xD8D2E8);

        [Tooltip("Small decorative specks scattered along the trail — snow, sun motes, falling " +
            "leaves, blossom. Must read against TrailPathColor, since it is drawn on top of it.")]
        [SerializeField] private Color _trailWeatherAccentColor = FromHex(0xFFFFFF);

        [Tooltip("Plate behind a level node's number on the trail. Sits on TrailPathColor rather " +
            "than on CardBackground, so it is authored per theme instead of derived from the card.")]
        [SerializeField] private Color _trailNodePlateColor = FromHex(0xF2EFF8);

        [Header("Drag preview")]
        [SerializeField] private Color _validPreview = new Color(0.44f, 0.72f, 0.69f, 0.45f);
        [SerializeField] private Color _invalidPreview = new Color(0.91f, 0.47f, 0.35f, 0.35f);

        [Tooltip("Outline drawn around every cell of a row/column the current drag would clear. Bright and celebratory — it sits on top of the valid-preview tint, so it must read against it.")]
        [SerializeField] private Color _wouldClearHighlight = new Color(1f, 0.85f, 0.30f, 0.95f);

        /// <summary>Stable identity used by <c>SettingsSystem.SetTheme</c> and PlayerPrefs, not the array index.</summary>
        public int Id => _id;

        public string DisplayName => _displayName;

        /// <summary>Key into <c>LocalizationKeys</c>/the GameStrings table for this theme's player-facing name.</summary>
        public string TranslationKey => _translationKey;

        public Color BackgroundTop => _backgroundTop;

        public Color BackgroundBottom => _backgroundBottom;

        public Color CardBackground => _cardBackground;

        public Color CardShadow => _cardShadow;

        public Color EmptyCellFill => _emptyCellFill;

        public Color EmptyCellOutline => _emptyCellOutline;

        public Color Ink => _ink;

        public Color SoftInk => _softInk;

        public Color Accent => _accent;

        /// <summary>Ribbon colour of the winding trail on the level path card.</summary>
        public Color TrailPathColor => _trailPathColor;

        /// <summary>Seasonal specks scattered along that trail.</summary>
        public Color TrailWeatherAccentColor => _trailWeatherAccentColor;

        /// <summary>Plate behind a level node's number on that trail.</summary>
        public Color TrailNodePlateColor => _trailNodePlateColor;

        public Color ValidPreview => _validPreview;

        public Color InvalidPreview => _invalidPreview;

        /// <summary>Outline colour for the rows/columns the in-flight drag would clear.</summary>
        public Color WouldClearHighlight => _wouldClearHighlight;

        /// <summary>Main face colour for a cosmetic colour id (1-based; 0 is empty).</summary>
        public Color GetFill(int colourId) => Pick(_kindFills, colourId, _emptyCellFill);

        /// <summary>Lit bevel facet colour (top/left) for a cosmetic colour id.</summary>
        public Color GetHighlight(int colourId) => Pick(_kindHighlights, colourId, _emptyCellFill);

        /// <summary>Darker bevel/outline colour for a cosmetic colour id.</summary>
        public Color GetShade(int colourId) => Pick(_kindShades, colourId, _emptyCellOutline);

        private static Color Pick(Color[] colours, int colourId, Color fallback)
        {
            if (colours == null || colours.Length == 0 || colourId <= 0)
            {
                return fallback;
            }

            return colours[(colourId - 1) % colours.Length];
        }

        private static Color FromHex(uint rgb)
        {
            float r = ((rgb >> 16) & 0xFF) / 255f;
            float g = ((rgb >> 8) & 0xFF) / 255f;
            float b = (rgb & 0xFF) / 255f;
            return new Color(r, g, b, 1f);
        }
    }
}
