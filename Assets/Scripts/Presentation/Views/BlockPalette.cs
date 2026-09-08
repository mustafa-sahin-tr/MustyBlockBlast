using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// "Soft &amp; Playful" placeholder colour scheme. Static config, so it lives in a
    /// ScriptableObject; every View takes the same asset. When no asset is assigned the Views fall
    /// back to <see cref="CreateDefault"/>, which holds the approved defaults.
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Block Palette", fileName = "BlockPalette")]
    public sealed class BlockPalette : ScriptableObject
    {
        /// <summary>Piece "kinds" — colour ids are 1..KIND_COUNT and are cosmetic only.</summary>
        public const int KIND_COUNT = 3;

        [Header("Surfaces")]
        [SerializeField] private Color _backgroundTop = FromHex(0xEDE9F5);
        [SerializeField] private Color _backgroundBottom = FromHex(0xF7E9DD);
        [SerializeField] private Color _cardBackground = FromHex(0xFDFCFB);
        [SerializeField] private Color _cardShadow = new Color(0.17f, 0.15f, 0.20f, 0.10f);

        [Header("Cells")]
        [SerializeField] private Color _emptyCellFill = FromHex(0xE6E4EC);
        [SerializeField] private Color _emptyCellOutline = FromHex(0xD6D3E0);

        [Header("Piece kinds (fill / bevel shade)")]
        [SerializeField]
        private Color[] _kindFills =
        {
            FromHex(0xE8785A),
            FromHex(0x6FB8B0),
            FromHex(0xE0C36B),
        };

        [SerializeField]
        private Color[] _kindShades =
        {
            FromHex(0xC25A3F),
            FromHex(0x4C948C),
            FromHex(0xC29F49),
        };

        [Header("Text")]
        [SerializeField] private Color _ink = FromHex(0x2B2733);
        [SerializeField] private Color _softInk = FromHex(0x6E6878);

        [Header("Drag preview")]
        [SerializeField] private Color _validPreview = new Color(0.44f, 0.72f, 0.69f, 0.45f);
        [SerializeField] private Color _invalidPreview = new Color(0.91f, 0.47f, 0.35f, 0.35f);

        public Color BackgroundTop => _backgroundTop;

        public Color BackgroundBottom => _backgroundBottom;

        public Color CardBackground => _cardBackground;

        public Color CardShadow => _cardShadow;

        public Color EmptyCellFill => _emptyCellFill;

        public Color EmptyCellOutline => _emptyCellOutline;

        public Color Ink => _ink;

        public Color SoftInk => _softInk;

        public Color ValidPreview => _validPreview;

        public Color InvalidPreview => _invalidPreview;

        /// <summary>Runtime fallback so a View without an assigned asset still looks right.</summary>
        public static BlockPalette CreateDefault()
        {
            var palette = CreateInstance<BlockPalette>();
            palette.hideFlags = HideFlags.HideAndDontSave;
            return palette;
        }

        /// <summary>Main face colour for a cosmetic colour id (1-based; 0 is empty).</summary>
        public Color GetFill(int colourId) => Pick(_kindFills, colourId, _emptyCellFill);

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
