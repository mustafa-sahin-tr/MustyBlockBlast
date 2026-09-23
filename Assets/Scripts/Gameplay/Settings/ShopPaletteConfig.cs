using System;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The power-up shop's own colours. Static config, so it lives in a ScriptableObject for the reason
    /// <see cref="PowerUpPriceConfig"/> does: retuning the look is an asset edit, never a code change.
    /// <para>
    /// Deliberately <em>not</em> mapped onto <see cref="ThemeDefinition"/> (issue #234): the shop is a
    /// carnival stall — a night ground, a cream card, a striped awning and glossy 3D buttons — and it
    /// keeps that look under every theme, where every other card restyles itself. A theme-derived
    /// shade would make the stall go pastel under the spring theme and muddy under the winter one.
    /// </para>
    /// <para>
    /// The tile colours are keyed by <see cref="PowerUpKind"/> as the prices are, and for the same
    /// reason: the roster grows, and a table adds a row where named fields would add a field, a
    /// property and a branch. Every field carries the design's value as its default so a freshly
    /// created instance — the asset on first import, or the scope's fallback — already draws the
    /// finished stall rather than a wall of white.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Shop Palette Config", fileName = "ShopPaletteConfig")]
    public sealed class ShopPaletteConfig : ScriptableObject
    {
        [Header("Card")]
        [Tooltip("The stall's cream card face.")]
        [SerializeField] private Color _cardFace = FromHex(0xFFF6E5);

        [Tooltip("The thick warm shadow under the card.")]
        [SerializeField] private Color _cardShadow = FromHex(0xB08A5A);

        [Tooltip("Dark plate the balance sits on, and the toast pill.")]
        [SerializeField] private Color _darkPlate = FromHex(0x1B1540);

        [Tooltip("Coin figure on the dark plate, and the toast text.")]
        [SerializeField] private Color _coinYellow = FromHex(0xFFE27A);

        [Tooltip("Small caption text on the dark plate.")]
        [SerializeField] private Color _softInk = FromHex(0x9D93D6);

        [Header("Coins tab")]
        [Tooltip("The white plate under an ad row or a coin bundle row.")]
        [SerializeField] private Color _lightPlate = FromHex(0xFFFFFF);

        [Tooltip("Ink on a light plate.")]
        [SerializeField] private Color _ink = FromHex(0x3A2A14);

        [Tooltip("Caption ink on a light plate, and the section labels between rows.")]
        [SerializeField] private Color _inkSoft = FromHex(0x8C7658);

        [Tooltip("The 'watch an ad' button and tile.")]
        [SerializeField] private Color _adButton = FromHex(0x2F6FE0);

        [Tooltip("The big coin figure on a bundle row.")]
        [SerializeField] private Color _bundleAmount = FromHex(0xF08A16);

        [Tooltip("Border of the 'convertible' stat plate on the convert panel.")]
        [SerializeField] private Color _statHighlight = FromHex(0xFFE27A);

        [Header("Sub-tabs")]
        [SerializeField] private Color _tabPowerUp = FromHex(0xFFC93C);
        [SerializeField] private Color _tabPowerUpText = FromHex(0x5A3A00);
        [SerializeField] private Color _tabCoin = FromHex(0x9A6FF0);
        [SerializeField] private Color _tabPromotion = FromHex(0xF06AA6);
        [SerializeField] private Color _tabText = FromHex(0xFFFFFF);

        [Tooltip("Multiplied into an inactive tab's colour so the active one reads as lit.")]
        [SerializeField] private Color _inactiveTabTint = new Color(0.72f, 0.72f, 0.78f, 1f);

        [Header("Item cards")]
        [SerializeField] private Color _itemPlate = FromHex(0x5A4FB8);
        [SerializeField] private Color _itemShadow = FromHex(0x2B2455);
        [SerializeField] private Color _itemBand = FromHex(0x7A6EE0);
        [SerializeField] private Color _itemName = FromHex(0xFFFFFF);
        [SerializeField] private Color _itemDescription = FromHex(0xE9E4FF);

        [Header("Buttons")]
        [SerializeField] private Color _buyButton = FromHex(0xF2601C);
        [SerializeField] private Color _buyButtonText = FromHex(0xFFFFFF);
        [SerializeField] private Color _earnButton = FromHex(0x3FAE3F);
        [SerializeField] private Color _unaffordableButton = FromHex(0xC9BFB0);
        [SerializeField] private Color _lockedButton = FromHex(0x2B2455);
        [SerializeField] private Color _lockedText = FromHex(0xC9C0FF);
        [SerializeField] private Color _heldBadge = FromHex(0x2ECC71);
        [SerializeField] private Color _saleBadge = FromHex(0xE0301E);
        [SerializeField] private Color _wasPrice = FromHex(0xFFD9C2);

        [Header("Icon tiles")]
        [Tooltip("Tint of the glossy tile behind each kind's glyph. Every PowerUpKind should have one row; "
            + "a kind without one draws on the item plate colour.")]
        [SerializeField] private TileColour[] _tileColours =
        {
            new TileColour(PowerUpKind.Bomb, FromHex(0xE8413A)),
            new TileColour(PowerUpKind.RowClear, FromHex(0x22A093)),
            new TileColour(PowerUpKind.ColumnClear, FromHex(0x2F6FE0)),
            new TileColour(PowerUpKind.Joker, FromHex(0x7E4FD8)),
            new TileColour(PowerUpKind.ColorCleanser, FromHex(0xE0468A)),
            new TileColour(PowerUpKind.Rotate, FromHex(0x3FAE3F)),
            new TileColour(PowerUpKind.Reroll, FromHex(0xF08A16)),
            new TileColour(PowerUpKind.DoubleMultiplier, FromHex(0xF0A300)),
            new TileColour(PowerUpKind.GhostFit, FromHex(0x22B8D8)),
            new TileColour(PowerUpKind.CoinSower, FromHex(0xE0A72E)),

            // Issue #295. One flat tint like every other row — the approved mockup's rainbow-gradient
            // plate is a follow-up art pass, not something a single Color can carry.
            new TileColour(PowerUpKind.PaintCross, FromHex(0xFF6B35)),
        };

        public Color CardFace => _cardFace;
        public Color CardShadow => _cardShadow;
        public Color DarkPlate => _darkPlate;
        public Color CoinYellow => _coinYellow;
        public Color SoftInk => _softInk;
        public Color LightPlate => _lightPlate;
        public Color Ink => _ink;
        public Color InkSoft => _inkSoft;
        public Color AdButton => _adButton;
        public Color BundleAmount => _bundleAmount;
        public Color StatHighlight => _statHighlight;
        public Color TabPowerUp => _tabPowerUp;
        public Color TabPowerUpText => _tabPowerUpText;
        public Color TabCoin => _tabCoin;
        public Color TabPromotion => _tabPromotion;
        public Color TabText => _tabText;
        public Color InactiveTabTint => _inactiveTabTint;
        public Color ItemPlate => _itemPlate;
        public Color ItemShadow => _itemShadow;
        public Color ItemBand => _itemBand;
        public Color ItemName => _itemName;
        public Color ItemDescription => _itemDescription;
        public Color BuyButton => _buyButton;
        public Color BuyButtonText => _buyButtonText;
        public Color EarnButton => _earnButton;
        public Color UnaffordableButton => _unaffordableButton;
        public Color LockedButton => _lockedButton;
        public Color LockedText => _lockedText;
        public Color HeldBadge => _heldBadge;
        public Color SaleBadge => _saleBadge;
        public Color WasPrice => _wasPrice;

        /// <summary>The tile tint for <paramref name="kind"/>, or the item plate colour when the table
        /// has no row for it — a visible-but-harmless fallback rather than white, which would read as a
        /// missing texture.</summary>
        public Color TileColourFor(PowerUpKind kind)
        {
            if (_tileColours != null)
            {
                for (int rowIndex = 0; rowIndex < _tileColours.Length; rowIndex++)
                {
                    if (_tileColours[rowIndex].Kind == kind)
                    {
                        return _tileColours[rowIndex].Colour;
                    }
                }
            }

            return _itemPlate;
        }

        private static Color FromHex(int rgb)
        {
            float r = ((rgb >> 16) & 0xFF) / 255f;
            float g = ((rgb >> 8) & 0xFF) / 255f;
            float b = (rgb & 0xFF) / 255f;
            return new Color(r, g, b, 1f);
        }

        [Serializable]
        private struct TileColour
        {
            [SerializeField] private PowerUpKind _kind;
            [SerializeField] private Color _colour;

            public TileColour(PowerUpKind kind, Color colour)
            {
                _kind = kind;
                _colour = colour;
            }

            public PowerUpKind Kind => _kind;
            public Color Colour => _colour;
        }
    }
}
