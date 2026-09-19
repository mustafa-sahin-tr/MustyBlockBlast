using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The power-up shop, drawn as a carnival stall (issue #234) under the hub's striped awning: a cream card,
    /// the balance on a dark plate, three chunky sub-tabs, and a scrolling two-column grid of item
    /// cards — one per <see cref="PowerUpKind"/> — each with a glossy tinted tile, the kind's glyph,
    /// a one-line description, a held-count badge and a 3D price button. Shown as the shop tab of
    /// <see cref="HubPanelView"/>, which is its only opener.
    /// <para>
    /// Holds no logic, like every other card here. It never writes a model: the balance and the nine
    /// counters are observed, so the cards repaint whether this screen or something else moved them —
    /// including <see cref="PowerUpInventoryView"/>'s earn-by-ad taps on the strip underneath. A tap
    /// on a price button is one call to <see cref="CurrencySystem.TryPurchasePowerUp"/> and nothing
    /// more; whether it is refused, and why, is the System's answer and not this card's guess.
    /// </para>
    /// <para>
    /// Prices are quoted through <see cref="CurrencySystem.QuotePriceFor"/> rather than read out of the
    /// config directly, so the figure shown and the figure charged come from the same arithmetic. A
    /// live promotion reaches this card the same way — the quote arrives discounted — and whether to
    /// <em>say</em> so (the struck-through standard price and the sale badge) is decided by comparing
    /// that quote against <see cref="PowerUpPriceConfig.GetPrice"/>, never by reading
    /// <see cref="PromotionConfig"/> here.
    /// </para>
    /// <para>
    /// The Coins tab is the other half of the stall (issue #219, and the Coins artboard of #234's
    /// canvas): the convert-score panel, a watch-an-ad row and one row per real-money
    /// <see cref="CoinBundle"/>. Every one of those is a faucet the System already owns —
    /// <see cref="CurrencySystem.ConvertScoreToCoins"/>, <see cref="CurrencySystem.GrantCoinsFromAdAsync"/>
    /// and <see cref="CurrencySystem.PurchaseCoinBundleAsync"/> — so this tab only names what the
    /// player tapped, and the balance strip repaints itself through the model when the System agrees.
    /// </para>
    /// <para>
    /// The one card that does not follow the theme. Its colours come from
    /// <see cref="ShopPaletteConfig"/>, because a stall that went pastel in spring and muddy in winter
    /// would not be a stall; see that config for the argument.
    /// </para>
    /// <para>
    /// Taps are split two ways, as <see cref="LevelPathPanelView"/>'s are. The grid scrolls, so the
    /// buttons inside the card are EventSystem targets (<see cref="LevelPathNodeButton"/>): only uGUI
    /// tells a tap from the first frame of a drag correctly. The close cross and the dismissing scrim
    /// stay with <see cref="BoardInputView"/>'s manual routing through <see cref="HandleTap"/>, which
    /// is what keeps this card in the hub's gate chain. A press inside the card is therefore seen by
    /// both pipelines, and <see cref="HandleTap"/> deliberately does nothing with it so the button
    /// underneath is the only thing that acts.
    /// </para>
    /// <para>
    /// While it is open it is modal and holds the run's clock through
    /// <see cref="TimerRunSystem.SetMenuPaused"/> — see the gate chain in <see cref="BoardInputView"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerUpShopView : MonoBehaviour
    {
        /// <summary>
        /// One item card per <see cref="PowerUpKind"/>, in the same display order
        /// <see cref="PowerUpInventoryView"/> draws its strip in. Deliberately a second copy of that
        /// order rather than a shared one: the strip's is private to it, and a shop card and a HUD
        /// slot are free to diverge later without either having to ask the other's permission.
        /// </summary>
        private static readonly PowerUpKind[] ItemKinds =
        {
            PowerUpKind.Bomb,
            PowerUpKind.RowClear,
            PowerUpKind.ColumnClear,
            PowerUpKind.Joker,
            PowerUpKind.ColorCleanser,
            PowerUpKind.Rotate,
            PowerUpKind.Reroll,
            PowerUpKind.DoubleMultiplier,
            PowerUpKind.GhostFit,
        };

        private static readonly int ItemCount = ItemKinds.Length;

        /// <summary>How many of a kind one tap buys. Fixed at one: a quantity picker is a design pass
        /// this screen has not had.</summary>
        private const int PURCHASE_QUANTITY = 1;

        /// <summary>The three sub-tabs, each drawing its own content in the grid's place. Internal
        /// rather than private so <see cref="HubPanelView"/> can ask this card to open landed on a
        /// specific one (issue #271) without a second, parallel enum to keep in sync with it.</summary>
        internal enum ShopTab
        {
            PowerUps,
            Coins,
            Deals,
        }

        /// <summary>The bundle row's coin pile grows with the bundle: the rows are drawn in config order,
        /// so the n-th row takes the n-th pile and the last pile serves every row past it.</summary>
        private const int COIN_PILE_COUNT = 4;

        /// <summary>How far one stepper tap moves the convert amount, in points. The same step the old
        /// conversion card used, so the arithmetic the player learnt there still holds.</summary>
        private const int AMOUNT_STEP = 50;

        /// <summary>The score figure the rate caption is quoted for: "100 pts = 10 coins" reads where
        /// "1 pt = 0.1 coins" does not.</summary>
        private const int RATE_SAMPLE_SCORE = 100;

        // Layout, in canvas reference pixels, on the 880-wide card the other hub cards share. Offsets
        // are measured down from the card's top edge; TopY turns them into anchored positions.
        private const float SIDE_INSET = 24f;
        // No awning of its own any more: the stall's canopy is the hub's, drawn once above the tab
        // bar for every tab (issue #235), so the card starts straight at the balance strip.
        private const float BALANCE_TOP = 24f;
        private const float BALANCE_HEIGHT = 80f;
        private const float BALANCE_CORNER_RADIUS = 18f;
        private const float BALANCE_COIN_SIZE = 56f;
        private const float BALANCE_PADDING = 14f;
        private const float EARN_BUTTON_WIDTH = 300f;
        private const float EARN_BUTTON_HEIGHT = 60f;
        private const float TABS_TOP = 120f;
        private const float TAB_HEIGHT = 72f;
        private const float TAB_GAP = 12f;
        private const int TAB_COUNT = 3;
        private const float VIEWPORT_TOP = 208f;
        private const float VIEWPORT_BOTTOM_INSET = 24f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float HEADER_INSET = 84f;

        // The grid: two columns of item cards, scrolled vertically.
        private const int GRID_COLUMNS = 3;
        private const float GRID_GAP = 16f;
        private const float GRID_TOP_PADDING = 8f;
        private const float GRID_BOTTOM_PADDING = 96f;
        private const float ITEM_HEIGHT = 276f;
        private const float ITEM_CORNER_RADIUS = 18f;
        private const float ITEM_SHADOW_DROP = 8f;
        private const float ITEM_PADDING = 10f;
        private const float ITEM_INNER_GAP = 12f;
        private const float BAND_HEIGHT = 40f;
        private const float TILE_SIZE = 104f;
        private const float GLYPH_SIZE = 62f;

        /// <summary>The tile sprite's glossy face sits above a darker lip, so the glyph is lifted off
        /// the tile's geometric centre to sit on the face.</summary>
        private const float GLYPH_RISE = 8f;
        private const float DESCRIPTION_HEIGHT = 44f;
        private const float BUY_BUTTON_HEIGHT = 56f;
        private const float BUY_BUTTON_CORNER_RADIUS = 16f;
        private const float HELD_BADGE_SIZE = 36f;
        private const float HELD_BADGE_BORDER = 4f;
        private const float SALE_BADGE_WIDTH = 60f;
        private const float SALE_BADGE_HEIGHT = 26f;
        private const float SALE_BADGE_CORNER_RADIUS = 12f;
        private const float COIN_GLYPH_SIZE = 30f;
        private const float LOCK_GLYPH_SIZE = 26f;
        private const float STRIKETHROUGH_THICKNESS = 3f;

        /// <summary>
        /// Where the coin and the figure sit inside the price button, in the button's local space. Two
        /// layouts: centred when the price stands alone, shifted right when a struck-through standard
        /// price sits to its left.
        /// </summary>
        private const float PRICE_TEXT_WIDTH = 120f;
        private const float COIN_X_PLAIN = -34f;
        private const float PRICE_X_PLAIN = 46f;
        private const float WAS_PRICE_WIDTH = 70f;
        private const float WAS_PRICE_X = -78f;
        private const float COIN_X_DISCOUNTED = -14f;
        private const float PRICE_X_DISCOUNTED = 66f;

        private const float TOAST_HEIGHT = 56f;
        private const float TOAST_BOTTOM = 44f;
        private const float TOAST_PADDING = 48f;
        private const float TOAST_CORNER_RADIUS = 28f;

        // The Coins tab, top to bottom: a section label, the convert panel, the ad row, a second
        // section label and the bundle rows.
        private const float SECTION_LABEL_HEIGHT = 36f;
        private const float SECTION_GAP = 12f;
        private const float PANEL_PADDING = 20f;
        private const float PANEL_CORNER_RADIUS = 22f;
        private const float PANEL_TITLE_HEIGHT = 44f;
        private const float STAT_PLATE_HEIGHT = 84f;
        private const float STAT_PLATE_GAP = 12f;
        private const float STAT_PLATE_CORNER_RADIUS = 14f;
        private const float STAT_PLATE_BORDER = 3f;
        private const float STEPPER_HEIGHT = 60f;
        private const float STEPPER_BUTTON_WIDTH = 96f;
        private const float STEPPER_AMOUNT_WIDTH = 200f;
        private const float ACTION_BUTTON_HEIGHT = 64f;
        private const float CONVERT_PANEL_HEIGHT = (PANEL_PADDING * 2f) + PANEL_TITLE_HEIGHT + STAT_PLATE_HEIGHT
            + STEPPER_HEIGHT + ACTION_BUTTON_HEIGHT + (ITEM_INNER_GAP * 3f);
        private const float ROW_HEIGHT = 112f;
        private const float ROW_GAP = 12f;
        private const float ROW_PADDING = 16f;
        private const float ROW_CORNER_RADIUS = 20f;
        private const float ROW_SHADOW_DROP = 5f;
        private const float ROW_TILE_SIZE = 76f;
        private const float ROW_TILE_CORNER_RADIUS = 18f;
        private const float ROW_TILE_GLYPH_SIZE = 40f;
        private const float ROW_BUTTON_WIDTH = 170f;
        private const float ROW_BUTTON_HEIGHT = 66f;

        /// <summary>
        /// The button sprite is authored at 512×249 with a deep bottom lip. Sliced at its native
        /// scale the lip alone would be taller than a 72-pixel button, so the slices are shrunk
        /// uniformly and the lip reads as a lip rather than as the whole button.
        /// </summary>
        private const float BUTTON_SLICE_SCALE = 2.5f;

        /// <summary>Alpha applied to every part of a card whose kind is behind its level gate. This is
        /// a full catalog — every kind gets a card regardless of level — so "locked" is a visual state
        /// here where the strip simply has no slot.</summary>
        private const float LOCKED_ITEM_ALPHA = 0.55f;

        /// <summary>Alpha of the struck-through standard price, well under the sale price's so the eye
        /// lands on what the player would pay.</summary>
        private const float WAS_PRICE_ALPHA = 0.7f;

        // Every other display string on this card is a plain non-linguistic symbol — "+"/"-" on the
        // stepper, the held-count "x2" badge — or comes from the String Table via LocalizationKeys'
        // SHOP_* section (issue #255). HELD_COUNT_PREFIX stays a literal for the same reason "+"/"-"
        // do: it is notation, not a word, so no language spells it differently.
        private const string HELD_COUNT_PREFIX = "x";

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1200f);
        [SerializeField] private int _headerFontSize = 52;
        [SerializeField] private int _balanceFontSize = 40;
        [SerializeField] private int _tabFontSize = 26;
        [SerializeField] private int _earnFontSize = 24;
        [FormerlySerializedAs("_rowNameFontSize")]
        [SerializeField] private int _itemNameFontSize = 22;
        [FormerlySerializedAs("_rowDescriptionFontSize")]
        [SerializeField] private int _itemDescriptionFontSize = 17;
        [FormerlySerializedAs("_rowPriceFontSize")]
        [SerializeField] private int _itemPriceFontSize = 26;
        [SerializeField] private int _wasPriceFontSize = 16;
        [SerializeField] private int _badgeFontSize = 17;
        [SerializeField] private int _toastFontSize = 26;
        [SerializeField] private int _placeholderFontSize = 30;
        [SerializeField] private int _sectionLabelFontSize = 20;
        [SerializeField] private int _panelTitleFontSize = 30;
        [SerializeField] private int _statLabelFontSize = 18;
        [SerializeField] private int _statValueFontSize = 30;
        [SerializeField] private int _stepperFontSize = 36;
        [SerializeField] private int _rowTitleFontSize = 26;
        [SerializeField] private int _rowCaptionFontSize = 20;
        [SerializeField] private int _bundleAmountFontSize = 40;

        [Header("Art")]
        [Tooltip("The chunky display face for names, prices and tab labels. Falls back to the built-in "
            + "runtime font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("White 9-sliced glossy button with a darker bottom lip. Tinted at runtime.")]
        [SerializeField] private Sprite _buttonSprite;

        [Tooltip("White glossy rounded tile behind each glyph. Tinted at runtime per kind.")]
        [SerializeField] private Sprite _tileSprite;

        [Tooltip("White-on-transparent padlock glyph for a locked card's button.")]
        [SerializeField] private Sprite _lockSprite;

        [Tooltip("The coin drawn beside every price and the balance.")]
        [SerializeField] private Sprite _coinSprite;

        [Tooltip("White-on-transparent play glyph for the watch-an-ad row.")]
        [SerializeField] private Sprite _adSprite;

        [Tooltip("Coin piles for the bundle rows, smallest first. The last one serves every bundle past it.")]
        [SerializeField] private Sprite[] _coinPileSprites = new Sprite[COIN_PILE_COUNT];

        [Tooltip("White-on-transparent glyphs, one per item in display order (Bomb, Row Clear, Column "
            + "Clear, Joker, Colour Cleanser, Rotate, Reroll, Double Score, Ghost Fit).")]
        [SerializeField] private Sprite[] _rowIcons = new Sprite[ItemCount];

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(48);
        private readonly ItemWidgets[] _items = new ItemWidgets[ItemCount];

        /// <summary>Held counts, mirrored from <see cref="PowerUpModel"/> so a repaint never has to
        /// reach back through the model. Written only by the count subscriptions.</summary>
        private readonly int[] _counts = new int[ItemCount];

        private ProfileModel _profileModel;
        private PowerUpModel _powerUpModel;
        private CoinBundlePriceModel _bundlePriceModel;
        private LevelProgressionModel _levelProgressionModel;
        private CurrencySystem _currencySystem;
        private TimerRunSystem _timerRunSystem;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private InfoPopupSystem _infoPopupSystem;
        private CoinBundleConfig _bundleConfig;
        private ShopPaletteConfig _palette;

        /// <summary>Read for one purpose only: the standard price to strike through when the System's
        /// quote comes back lower than it. Never used to charge or to quote.</summary>
        private PowerUpPriceConfig _priceConfig;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private RectTransform _closeButtonRect;
        private Image _closeBarA;
        private Image _closeBarB;
        private Text _headerText;

        private Image _balancePlate;
        private Image _balanceCoin;
        private Text _balanceText;
        private Image _earnButtonPlate;
        private Text _earnButtonText;

        private Image _tabPowerUpsPlate;
        private Text _tabPowerUpsText;
        private Image _tabCoinsPlate;
        private Text _tabCoinsText;
        private Image _tabDealsPlate;
        private Text _tabDealsText;

        private GameObject _viewportObject;
        private ScrollRect _scrollRect;

        private GameObject _coinsViewportObject;
        private ScrollRect _coinsScrollRect;
        private Text _freeSectionText;
        private Text _bundlesSectionText;
        private Image _convertPanelPlate;
        private Text _convertTitleText;
        private Text _rateText;
        private Image _totalStatPlate;
        private Text _totalStatLabel;
        private Text _totalStatValue;
        private Image _convertibleStatBorder;
        private Image _convertibleStatPlate;
        private Text _convertibleStatLabel;
        private Text _convertibleStatValue;
        private Image _minusPlate;
        private Text _minusText;
        private Image _plusPlate;
        private Text _plusText;
        private Text _amountText;
        private Image _convertButtonPlate;
        private Text _convertButtonText;
        private Image _adRowShadow;
        private Image _adRowPlate;
        private Image _adTile;
        private Image _adGlyph;
        private Text _adTitleText;
        private Text _adCaptionText;
        private Image _adButtonPlate;
        private Text _adButtonText;
        private Image _adButtonCoin;
        private BundleWidgets[] _bundles = new BundleWidgets[0];
        private Text _noBundlesText;
        private GameObject _dealsPlaceholder;
        private Image _dealsPlate;
        private Text _dealsText;

        private RectTransform _toastRect;
        private Image _toastPlate;
        private Text _toastText;

        private ShopTab _activeTab = ShopTab.PowerUps;
        private int _currentLevelNumber = 1;
        private string _message = string.Empty;

        /// <summary>How much of the convertible pool the next Convert tap will sell. Clamped to
        /// <see cref="CurrencySystem.AvailableToConvert"/> on every repaint, so the pool shrinking under
        /// it can never leave it asking for more than there is.</summary>
        private int _pendingAmount;

        /// <summary>The ad and the store both hand control to something outside the game and come back
        /// later; one in flight at a time, or a double tap would request two.</summary>
        private bool _isRequestingAd;
        private bool _isPurchasingBundle;

        [Inject]
        public void Construct(
            ProfileModel profileModel,
            PowerUpModel powerUpModel,
            CoinBundlePriceModel bundlePriceModel,
            LevelProgressionModel levelProgressionModel,
            CurrencySystem currencySystem,
            TimerRunSystem timerRunSystem,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            PowerUpPriceConfig priceConfig,
            CoinBundleConfig bundleConfig,
            ShopPaletteConfig palette,
            InfoPopupSystem infoPopupSystem)
        {
            _profileModel = profileModel;
            _powerUpModel = powerUpModel;
            _bundlePriceModel = bundlePriceModel;
            _levelProgressionModel = levelProgressionModel;
            _currencySystem = currencySystem;
            _timerRunSystem = timerRunSystem;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _priceConfig = priceConfig;
            _bundleConfig = bundleConfig;
            _palette = palette;
            _infoPopupSystem = infoPopupSystem;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_profileModel == null || _powerUpModel == null || _bundlePriceModel == null
                || _levelProgressionModel == null || _currencySystem == null || _timerRunSystem == null
                || _localizationModel == null || _localizationSystem == null || _priceConfig == null
                || _bundleConfig == null || _palette == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpShopView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Built here rather than in Awake with the rest of the card: the bundle rows come from an
            // injected config, and injection has only certainly happened by now.
            BuildCoinsTab();

            // Painted once: the palette is static config, not an observed model.
            PaintChrome();

            // Names and descriptions come from the string tables, so a language change repaints them.
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);

            // Subscribed rather than read once on open, for the reason the strip subscribes: reaching a
            // kind's unlock level must reveal it in the run that got the player there.
            _levelProgressionModel.CurrentLevelNumber.Subscribe(OnLevelChanged).AddTo(_disposables);

            // Same argument for the balance and the counts: an ad grant, a conversion or a spent
            // power-up all move these while the card is showing.
            _profileModel.CoinBalance.Subscribe(OnCountChanged).AddTo(_disposables);
            _profileModel.TotalScoreEarned.Subscribe(OnCountChanged).AddTo(_disposables);
            _profileModel.ScoreConverted.Subscribe(OnCountChanged).AddTo(_disposables);

            // The daily coin-ad cap (issue #257, AC3): a grant, or the day itself rolling over, moves
            // this while the card is showing, exactly as the balance does.
            _currencySystem.RemainingAdGrantsToday.Subscribe(OnCountChanged).AddTo(_disposables);

            // The bundle rows' store prices (issue #256): arrives asynchronously, any time after the
            // Coins tab's own warm-up asks for it, and this is the one thing that repaints a row's
            // button from "BUY" to the real price with no polling — see RefreshBundleRows.
            _bundlePriceModel.SkuToLocalizedPrice.Subscribe(OnCoinBundlePricesChanged).AddTo(_disposables);

            WatchCount(_powerUpModel.BombCount, PowerUpKind.Bomb);
            WatchCount(_powerUpModel.RowClearCount, PowerUpKind.RowClear);
            WatchCount(_powerUpModel.ColumnClearCount, PowerUpKind.ColumnClear);
            WatchCount(_powerUpModel.JokerCount, PowerUpKind.Joker);
            WatchCount(_powerUpModel.ColorCleanserCount, PowerUpKind.ColorCleanser);
            WatchCount(_powerUpModel.RotateCount, PowerUpKind.Rotate);
            WatchCount(_powerUpModel.RerollCount, PowerUpKind.Reroll);
            WatchCount(_powerUpModel.DoubleMultiplierCount, PowerUpKind.DoubleMultiplier);
            WatchCount(_powerUpModel.GhostFitCount, PowerUpKind.GhostFit);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>The card's frame, for <see cref="HubPanelView"/> to seat its bar above.</summary>
        internal RectTransform CardRect => _cardRect;

        /// <summary>The card's own close cross, which the hub hides in favour of its own.</summary>
        internal RectTransform CloseButtonRect => _closeButtonRect;

        /// <summary>The card's own header, which the hub hides in favour of its own.</summary>
        internal Text HeaderTitleText => _headerText;

        /// <summary>Opens the card landed on its default sub-tab, PowerUps.</summary>
        internal void Open() => Open(ShopTab.PowerUps);

        /// <summary>
        /// Opens the card landed on <paramref name="tab"/> — <see cref="HubPanelView"/>'s way of
        /// landing directly on the Coins sub-tab from the coin pill's "+" disc (issue #271), reusing
        /// this same entry point rather than duplicating what it does.
        /// </summary>
        internal void Open(ShopTab tab)
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            _message = string.Empty;
            _pendingAmount = _currencySystem.AvailableToConvert;
            SelectTab(tab);
            _scrollRect.verticalNormalizedPosition = 1f;
            _coinsScrollRect.verticalNormalizedPosition = 1f;
            Refresh();
            _panel.SetActive(true);
            transform.SetAsLastSibling();
            _timerRunSystem.SetMenuPaused(true);
        }

        /// <summary>
        /// Routes a tap while the panel is open. The close cross wins; the card then swallows anything
        /// else, because everything tappable on it is an EventSystem target that has already acted or
        /// will. Only a tap on the scrim outside the card closes.
        /// </summary>
        internal void HandleTap(Vector2 screenPosition)
        {
            if (!IsOpen)
            {
                return;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, screenPosition, eventCamera))
            {
                Close();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        /// <summary>
        /// Shuts the card and releases the menu pause. Reachable by <c>HubPanelView</c>, which shuts the
        /// outgoing card when the player switches tabs; every other caller is this class's own dismiss
        /// paths.
        /// </summary>
        internal void Close()
        {
            _panel.SetActive(false);
            _timerRunSystem.SetMenuPaused(false);
        }

        /// <summary>
        /// Asks the System to buy one of this card's kind and reports what it said. Every outcome —
        /// including both refusals — is routed through the System rather than pre-empted here. A
        /// locked card and an unaffordable one are already drawn as such, so a refusal cannot normally
        /// happen; when it does, the refusal the player is shown is the one the System actually gave.
        /// </summary>
        private void Buy(int itemIndex)
        {
            PowerUpPurchaseResult result = _currencySystem.TryPurchasePowerUp(
                ItemKinds[itemIndex], PURCHASE_QUANTITY);

            switch (result)
            {
                case PowerUpPurchaseResult.Success:
                    _message = _localizationSystem.Translate(LocalizationKeys.SHOP_TOAST_PURCHASED);
                    break;
                case PowerUpPurchaseResult.Locked:
                    _message = _localizationSystem.Translate(LocalizationKeys.SHOP_TOAST_LOCKED);
                    break;
                default:
                    // InsufficientCoins, and InvalidQuantity — which this card cannot produce, since
                    // PURCHASE_QUANTITY is a positive constant.
                    _message = _localizationSystem.Translate(LocalizationKeys.SHOP_TOAST_INSUFFICIENT_COINS);
                    break;
            }

            // The count and balance subscriptions repaint on a success; a refusal moves neither, so the
            // toast has to be painted here either way.
            Refresh();
        }

        /// <summary>The balance strip's earn button: the same place the Coins tab goes, so the coins and
        /// the way to get more sit together.</summary>
        private void OpenCoins() => SelectTab(ShopTab.Coins);

        private void SelectTab(ShopTab tab)
        {
            _activeTab = tab;
            _viewportObject.SetActive(tab == ShopTab.PowerUps);
            _coinsViewportObject.SetActive(tab == ShopTab.Coins);
            _dealsPlaceholder.SetActive(tab == ShopTab.Deals);

            if (tab == ShopTab.Coins)
            {
                // The Coins tab's warm-up (issue #256, AC1): asks the System to connect to the store and
                // fetch its catalog, so the bundle rows have a chance to show a real price without the
                // player ever having tapped a buy button. CurrencySystem.WarmUpCoinCatalog is idempotent
                // — landing here on every reopen of this tab is exactly the point, and it never produces
                // a second fetch.
                _currencySystem.WarmUpCoinCatalog(this.GetCancellationTokenOnDestroy());
            }

            // A purchase message belongs to the grid it was answered on; it would be a non sequitur
            // over the deals plate.
            _message = string.Empty;
            RefreshToast();
            PaintTabs();
        }

        private void WatchCount(ReactiveProperty<int> counter, PowerUpKind kind)
        {
            int itemIndex = ItemIndexOf(kind);
            if (itemIndex < 0)
            {
                return;
            }

            counter.Subscribe(count =>
            {
                _counts[itemIndex] = count;
                Refresh();
            }).AddTo(_disposables);
        }

        /// <summary>The card drawing <paramref name="kind"/>, or -1 when none does.</summary>
        private static int ItemIndexOf(PowerUpKind kind)
        {
            for (int itemIndex = 0; itemIndex < ItemCount; itemIndex++)
            {
                if (ItemKinds[itemIndex] == kind)
                {
                    return itemIndex;
                }
            }

            return -1;
        }

        private void OnCountChanged(int value) => Refresh();

        /// <summary>The bundle rows' store-price repaint (issue #256, AC6): fires whenever
        /// <see cref="CoinBundlePriceModel.SkuToLocalizedPrice"/> changes, including the immediate call
        /// every subscription gets with whatever it already held — which is how a Coins tab opened after
        /// the catalog already arrived once (a second tab reopen) still shows the price on first
        /// paint.</summary>
        private void OnCoinBundlePricesChanged(IReadOnlyDictionary<string, string> prices) => RefreshCoinsTab();

        private void OnLocaleChanged(LocaleDefinition locale) => Refresh();

        private void OnLevelChanged(int currentLevelNumber)
        {
            _currentLevelNumber = currentLevelNumber;
            Refresh();
        }

        /// <summary>
        /// Repaints the balance, every card and the toast from the models. Cheap enough to be the only
        /// repaint path: it runs on an open, a purchase, a grant, a level change and a language change —
        /// never per frame.
        /// </summary>
        private void Refresh()
        {
            if (_panel == null || _palette == null || _currencySystem == null)
            {
                return;
            }

            _balanceText.text = _profileModel.CoinBalance.Value.ToString();

            for (int itemIndex = 0; itemIndex < ItemCount; itemIndex++)
            {
                RefreshItem(itemIndex);
            }

            RefreshCoinsTab();
            RefreshToast();

            // The card's static chrome — title, tabs, section labels, panel copy — also comes from the
            // String Table now (issue #255). Repainted alongside everything else rather than through a
            // second subscription: cheap enough, and Refresh already runs on the locale-change path via
            // OnLocaleChanged.
            RepaintLocalizedChrome();
        }

        /// <summary>
        /// Sets every static caption that is neither per-item, per-count nor per-price: the header, the
        /// three sub-tabs, the earn button, the deals placeholder, and the Coins tab's section labels and
        /// panel copy. Called once from <see cref="Refresh"/>'s first pass (the locale subscription fires
        /// immediately on Start) and again on every later locale change, so it never needs a subscription
        /// of its own.
        /// </summary>
        private void RepaintLocalizedChrome()
        {
            _headerText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_TITLE);
            _tabPowerUpsText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_TAB_POWER_UPS);
            _tabCoinsText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_TAB_COINS);
            _tabDealsText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_TAB_DEALS);
            _earnButtonText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_EARN_BUTTON);
            _dealsText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_DEALS_PLACEHOLDER);

            _freeSectionText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_SECTION_FREE);
            _bundlesSectionText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_SECTION_BUNDLES);
            _convertTitleText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_CONVERT_TITLE);
            _totalStatLabel.text = _localizationSystem.Translate(LocalizationKeys.SHOP_TOTAL_SCORE_LABEL);
            _convertibleStatLabel.text = _localizationSystem.Translate(LocalizationKeys.SHOP_CONVERTIBLE_LABEL);
            _convertButtonText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_CONVERT_BUTTON);
            _adTitleText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_AD_TITLE);

            // The ad row's caption depends on the daily cap's remaining count (issue #257), and the
            // bundle rows' buttons depend on the store's fetched prices (issue #256) — both move far more
            // often than the locale does, so both are repainted by RefreshCoinsTab (via RefreshAdRow and
            // RefreshBundleRows) instead, which this method's caller already runs right alongside it on
            // every Refresh, locale change included.

            if (_noBundlesText != null)
            {
                _noBundlesText.text = _localizationSystem.Translate(LocalizationKeys.SHOP_NO_BUNDLES);
            }
        }

        // ------------------------------------------------------------------------- the Coins tab

        /// <summary>
        /// Repaints the convert panel's figures, the ad row's caption and button colour (see
        /// <see cref="RefreshAdRow"/>), and the bundle rows' buttons (see
        /// <see cref="RefreshBundleRows"/>). The rows themselves are still built once — a bundle's size
        /// and SKU are config — only what a row's button says moves, and now for two reasons instead of
        /// one: a locale change, and a store price arriving (issue #256, AC6).
        /// </summary>
        private void RefreshCoinsTab()
        {
            int available = _currencySystem.AvailableToConvert;
            _pendingAmount = Mathf.Clamp(_pendingAmount, 0, available);

            _totalStatValue.text = _profileModel.TotalScoreEarned.Value.ToString();
            _convertibleStatValue.text = available.ToString();
            _amountText.text = _pendingAmount.ToString();

            _rateText.text = _localizationSystem.Format(
                LocalizationKeys.SHOP_RATE_FORMAT,
                RATE_SAMPLE_SCORE.ToString(),
                _currencySystem.QuoteCoinsFor(RATE_SAMPLE_SCORE).ToString());

            // Convert is live only when the tap would do something, as a buy button is; the stepper
            // stays tappable either way because stepping a zero pool is harmless and clamps to zero.
            _convertButtonPlate.color = _pendingAmount > 0 ? _palette.EarnButton : _palette.UnaffordableButton;

            RefreshAdRow();
            RefreshBundleRows();
        }

        /// <summary>
        /// Each bundle row's button: the store's localized price when
        /// <see cref="CoinBundlePriceModel.SkuToLocalizedPrice"/> has one for that row's SKU, the plain
        /// "BUY" label otherwise (issue #256, AC2 and AC4). Never touches
        /// <see cref="ICoinPurchaseService"/> or any store SDK type — everything it reads comes off the
        /// Model <see cref="CurrencySystem.CoinBundlePrices"/> exposes, which is exactly what keeps this
        /// card within the same seam every other read here already respects (AC7).
        /// <para>
        /// A missing entry — an unknown SKU, a catalog that has not arrived yet, a fetch failure, or a
        /// dropped connection with nothing ever fetched — is indistinguishable here from "no price", and
        /// deliberately so: this card has no way to tell those apart, and the fallback is the same "BUY"
        /// for all of them (AC5), never a stale value left over from a previous repaint, because the
        /// label is always fully reassigned rather than left alone when the lookup misses.
        /// </para>
        /// </summary>
        private void RefreshBundleRows()
        {
            IReadOnlyDictionary<string, string> prices = _bundlePriceModel.SkuToLocalizedPrice.Value;
            string buyLabel = _localizationSystem.Translate(LocalizationKeys.SHOP_BUNDLE_BUTTON);

            for (int bundleIndex = 0; bundleIndex < _bundles.Length; bundleIndex++)
            {
                BundleWidgets bundle = _bundles[bundleIndex];
                bundle.ButtonText.text = prices != null
                    && prices.TryGetValue(bundle.Sku, out string localizedPrice)
                    && !string.IsNullOrEmpty(localizedPrice)
                        ? localizedPrice
                        : buyLabel;
            }
        }

        /// <summary>
        /// The watch-an-ad row's live half (issue #257): the caption and the button's colour, both
        /// driven by <see cref="CurrencySystem.RemainingAdGrantsToday"/> and nothing computed here. The
        /// getter itself rolls the daily counter over when the stored day has passed, so this read is
        /// also what lets a tab reopened after midnight show the fresh cap — this card does no date math
        /// of its own, it only asks the System what is left.
        /// </summary>
        private void RefreshAdRow()
        {
            int remaining = _currencySystem.RemainingAdGrantsToday.Value;
            bool hasGrantsRemaining = remaining > 0;

            _adButtonPlate.color = hasGrantsRemaining ? _palette.AdButton : _palette.UnaffordableButton;
            _adCaptionText.text = hasGrantsRemaining
                ? _localizationSystem.Format(LocalizationKeys.SHOP_AD_CAPTION_FORMAT, remaining.ToString())
                : _localizationSystem.Translate(LocalizationKeys.SHOP_AD_CAPTION_EXHAUSTED);
        }

        private void StepAmount(int delta)
        {
            _pendingAmount = Mathf.Clamp(_pendingAmount + delta, 0, _currencySystem.AvailableToConvert);
            RefreshCoinsTab();
        }

        /// <summary>Tapping the figure itself takes the lot: "all of it" is the common case, and stepping
        /// there fifty at a time would be a chore.</summary>
        private void TakeAllAmount()
        {
            _pendingAmount = _currencySystem.AvailableToConvert;
            RefreshCoinsTab();
        }

        /// <summary>
        /// Sells the pending amount of score for coins. Refused by the System when the amount is zero;
        /// the message says so here because the System's refusal is silent and the button is drawn
        /// live-or-not rather than disabled.
        /// </summary>
        private void Convert()
        {
            if (_pendingAmount <= 0)
            {
                _message = _localizationSystem.Translate(LocalizationKeys.SHOP_TOAST_NOTHING_TO_CONVERT);
                RefreshToast();
                return;
            }

            _currencySystem.ConvertScoreToCoins(_pendingAmount);
            _message = _localizationSystem.Translate(LocalizationKeys.SHOP_TOAST_CONVERTED);

            // The balance and the converted figure repaint through their subscriptions; the pending
            // amount is re-clamped by the same repaint, so the panel reads "0 left" without a second
            // pass here.
            Refresh();
        }

        /// <summary>
        /// The ad row button's tap handler. Refuses outright, before <see cref="RequestAdCoins"/> is ever
        /// called, once <see cref="CurrencySystem.RemainingAdGrantsToday"/> reads zero for today (issue
        /// #257, AC8) — the exhausted row is drawn dead rather than merely coloured that way, so a tap on
        /// it cannot even start an ad request.
        /// </summary>
        private void OnAdButtonTapped()
        {
            if (_currencySystem.RemainingAdGrantsToday.Value <= 0)
            {
                return;
            }

            RequestAdCoins().Forget();
        }

        /// <summary>
        /// Asks the System for the ad reward. Whether an ad is shown, watched to the end and worth
        /// anything is the System's business through <see cref="ICoinRewardSource"/>; this only reports
        /// the yes or no it came back with.
        /// </summary>
        private async UniTaskVoid RequestAdCoins()
        {
            if (_isRequestingAd)
            {
                return;
            }

            _isRequestingAd = true;
            try
            {
                bool granted = await _currencySystem.GrantCoinsFromAdAsync(
                    _currencySystem.AdRewardCoins, this.GetCancellationTokenOnDestroy());
                _message = _localizationSystem.Translate(granted
                    ? LocalizationKeys.SHOP_TOAST_COINS_ADDED
                    : LocalizationKeys.SHOP_TOAST_AD_REFUSED);
                RefreshToast();
            }
            finally
            {
                _isRequestingAd = false;
            }
        }

        /// <summary>
        /// Buys a coin bundle with real money. Every decision about whether and how much to credit
        /// belongs to the System — this only names the SKU the player tapped. A dismissal and a store
        /// failure read the same here, because neither has a currency string to tell them apart yet.
        /// </summary>
        private async UniTaskVoid PurchaseBundle(string sku)
        {
            if (_isPurchasingBundle)
            {
                return;
            }

            _isPurchasingBundle = true;
            try
            {
                bool bought = await _currencySystem.PurchaseCoinBundleAsync(
                    sku, this.GetCancellationTokenOnDestroy());
                _message = _localizationSystem.Translate(bought
                    ? LocalizationKeys.SHOP_TOAST_COINS_ADDED
                    : LocalizationKeys.SHOP_TOAST_BUNDLE_REFUSED);
                RefreshToast();
            }
            finally
            {
                _isPurchasingBundle = false;
            }
        }

        /// <summary>
        /// Repaints one card. Three mutually exclusive states in priority order, mirroring the strip's:
        /// locked wins outright, then affordable, then "not enough coins". A discount is orthogonal —
        /// it decorates whichever of the three the card is in, except locked, which shows no price at
        /// all and so nothing to discount.
        /// </summary>
        private void RefreshItem(int itemIndex)
        {
            ItemWidgets item = _items[itemIndex];
            PowerUpKind kind = ItemKinds[itemIndex];
            bool isLocked = !PowerUpUnlockLevels.IsUnlockedAt(kind, _currentLevelNumber);
            long price = _currencySystem.QuotePriceFor(kind, PURCHASE_QUANTITY);
            bool isAffordable = !isLocked && price <= _profileModel.CoinBalance.Value;
            int standardPrice = _priceConfig.GetPrice(kind);
            bool isDiscounted = !isLocked && price < standardPrice;
            float alpha = isLocked ? LOCKED_ITEM_ALPHA : 1f;

            item.Shadow.color = WithAlpha(_palette.ItemShadow, alpha);
            item.Plate.color = WithAlpha(_palette.ItemPlate, alpha);
            item.Band.color = WithAlpha(_palette.ItemBand, alpha);
            item.BandSquare.color = WithAlpha(_palette.ItemBand, alpha);
            item.Tile.color = WithAlpha(_palette.TileColourFor(kind), alpha);
            item.Glyph.color = WithAlpha(Color.white, alpha);
            item.Name.color = WithAlpha(_palette.ItemName, alpha);
            item.Description.color = WithAlpha(_palette.ItemDescription, alpha);

            item.Name.text = _localizationSystem.Translate(NameKeyOf(kind));
            item.Description.text = _localizationSystem.Translate(DescriptionKeyOf(kind));

            bool showHeld = !isLocked && _counts[itemIndex] > 0;
            item.HeldBadge.SetActive(showHeld);
            if (showHeld)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(HELD_COUNT_PREFIX);
                _stringBuilder.Append(_counts[itemIndex]);
                item.HeldCount.text = _stringBuilder.ToString();
            }

            // The button: a requirement on a locked card, a price on every other.
            item.LockGlyph.enabled = isLocked;
            item.Coin.enabled = !isLocked;
            if (isLocked)
            {
                item.ButtonPlate.color = _palette.LockedButton;
                item.Price.color = _palette.LockedText;
                _stringBuilder.Clear();
                _stringBuilder.Append(_localizationSystem.Translate(LocalizationKeys.HUD_LEVEL_SHORT));
                _stringBuilder.Append(' ');
                _stringBuilder.Append(PowerUpUnlockLevels.LevelFor(kind));
                item.Price.text = _stringBuilder.ToString();
                item.Price.alignment = TextAnchor.MiddleLeft;
                ((RectTransform)item.Price.transform).anchoredPosition = new Vector2(PRICE_X_PLAIN, 0f);
            }
            else
            {
                item.ButtonPlate.color = isAffordable ? _palette.BuyButton : _palette.UnaffordableButton;
                item.Price.color = _palette.BuyButtonText;
                item.Price.text = price.ToString();
                item.Price.alignment = TextAnchor.MiddleLeft;
                ((RectTransform)item.Price.transform).anchoredPosition = new Vector2(
                    isDiscounted ? PRICE_X_DISCOUNTED : PRICE_X_PLAIN, 0f);
                ((RectTransform)item.Coin.transform).anchoredPosition = new Vector2(
                    isDiscounted ? COIN_X_DISCOUNTED : COIN_X_PLAIN, 0f);
            }

            RefreshWasPrice(item, price, standardPrice, isDiscounted);
        }

        /// <summary>
        /// Paints — or hides — the struck-through standard price beside a discounted sale price, and
        /// the percentage badge on the band. Hidden by emptying the text and disabling the images
        /// rather than by deactivating GameObjects: all of it is built once and toggled on every
        /// repaint, and a SetActive pair would dirty the canvas layout for no gain over a component that
        /// draws nothing. The bar is sized from <see cref="Text.preferredWidth"/> after the text is
        /// assigned, which is the only order in which it reports the new string's width.
        /// </summary>
        private void RefreshWasPrice(ItemWidgets item, long price, int standardPrice, bool isDiscounted)
        {
            if (!isDiscounted)
            {
                item.WasPrice.text = string.Empty;
                item.WasPriceBar.enabled = false;
                item.SaleBadge.enabled = false;
                item.SaleText.text = string.Empty;
                return;
            }

            item.WasPrice.text = standardPrice.ToString();
            item.WasPrice.color = WithAlpha(_palette.WasPrice, WAS_PRICE_ALPHA);
            item.WasPriceBar.enabled = true;
            item.WasPriceBar.color = WithAlpha(_palette.WasPrice, WAS_PRICE_ALPHA);
            ((RectTransform)item.WasPriceBar.transform).sizeDelta = new Vector2(
                item.WasPrice.preferredWidth, STRIKETHROUGH_THICKNESS);

            int percentOff = standardPrice > 0
                ? Mathf.RoundToInt((1f - ((float)price / standardPrice)) * 100f)
                : 0;
            item.SaleBadge.enabled = true;
            item.SaleBadge.color = _palette.SaleBadge;
            _stringBuilder.Clear();
            _stringBuilder.Append('-');
            _stringBuilder.Append(percentOff);
            _stringBuilder.Append('%');
            item.SaleText.text = _stringBuilder.ToString();
        }

        /// <summary>The toast pill at the foot of the card, sized to its message and hidden when there is
        /// none. Deactivated rather than emptied: it is one object, and an invisible pill would still
        /// sit over the last row of the grid.</summary>
        private void RefreshToast()
        {
            bool hasMessage = !string.IsNullOrEmpty(_message);
            _toastRect.gameObject.SetActive(hasMessage);
            if (!hasMessage)
            {
                return;
            }

            _toastText.text = _message;
            _toastRect.sizeDelta = new Vector2(_toastText.preferredWidth + TOAST_PADDING, TOAST_HEIGHT);
        }

        private static string NameKeyOf(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.RowClear:
                    return LocalizationKeys.POWERUP_NAME_ROW_CLEAR;
                case PowerUpKind.ColumnClear:
                    return LocalizationKeys.POWERUP_NAME_COLUMN_CLEAR;
                case PowerUpKind.Joker:
                    return LocalizationKeys.POWERUP_NAME_JOKER;
                case PowerUpKind.ColorCleanser:
                    return LocalizationKeys.POWERUP_NAME_COLOR_CLEANSER;
                case PowerUpKind.Rotate:
                    return LocalizationKeys.POWERUP_NAME_ROTATE;
                case PowerUpKind.Reroll:
                    return LocalizationKeys.POWERUP_NAME_REROLL;
                case PowerUpKind.DoubleMultiplier:
                    return LocalizationKeys.POWERUP_NAME_DOUBLE_MULTIPLIER;
                case PowerUpKind.GhostFit:
                    return LocalizationKeys.POWERUP_NAME_GHOST_FIT;
                default:
                    return LocalizationKeys.POWERUP_NAME_BOMB;
            }
        }

        /// <summary>String-table key of a kind's one-line description, mapped as <see cref="NameKeyOf"/> is.</summary>
        private static string DescriptionKeyOf(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.RowClear:
                    return LocalizationKeys.POWERUP_DESC_ROW_CLEAR;
                case PowerUpKind.ColumnClear:
                    return LocalizationKeys.POWERUP_DESC_COLUMN_CLEAR;
                case PowerUpKind.Joker:
                    return LocalizationKeys.POWERUP_DESC_JOKER;
                case PowerUpKind.ColorCleanser:
                    return LocalizationKeys.POWERUP_DESC_COLOR_CLEANSER;
                case PowerUpKind.Rotate:
                    return LocalizationKeys.POWERUP_DESC_ROTATE;
                case PowerUpKind.Reroll:
                    return LocalizationKeys.POWERUP_DESC_REROLL;
                case PowerUpKind.DoubleMultiplier:
                    return LocalizationKeys.POWERUP_DESC_DOUBLE_MULTIPLIER;
                case PowerUpKind.GhostFit:
                    return LocalizationKeys.POWERUP_DESC_GHOST_FIT;
                default:
                    return LocalizationKeys.POWERUP_DESC_BOMB;
            }
        }

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        // ---------------------------------------------------------------- painting the static chrome

        /// <summary>Paints everything that is neither a model value nor a card state: the card, the
        /// balance strip, the tabs, the placeholder and the toast. Once, from the palette.</summary>
        private void PaintChrome()
        {
            _cardImage.color = _palette.CardFace;
            _cardShadowImage.color = _palette.CardShadow;
            _headerText.color = _palette.DarkPlate;
            _closeBarA.color = _palette.DarkPlate;
            _closeBarB.color = _palette.DarkPlate;

            _balancePlate.color = _palette.DarkPlate;
            _balanceCoin.color = Color.white;
            _balanceText.color = _palette.CoinYellow;
            _earnButtonPlate.color = _palette.EarnButton;
            _earnButtonText.color = _palette.BuyButtonText;

            _dealsPlate.color = _palette.ItemPlate;
            _dealsText.color = _palette.ItemDescription;

            PaintCoinsTab();

            _toastPlate.color = _palette.DarkPlate;
            _toastText.color = _palette.CoinYellow;

            for (int itemIndex = 0; itemIndex < ItemCount; itemIndex++)
            {
                ItemWidgets item = _items[itemIndex];
                item.HeldRing.color = _palette.CardFace;
                item.HeldFill.color = _palette.HeldBadge;
                item.HeldCount.color = _palette.BuyButtonText;
                item.Coin.color = Color.white;
                item.LockGlyph.color = _palette.LockedText;
                item.SaleText.color = _palette.BuyButtonText;
            }

            PaintTabs();
        }

        private void PaintCoinsTab()
        {
            _freeSectionText.color = _palette.InkSoft;
            _bundlesSectionText.color = _palette.InkSoft;

            _convertPanelPlate.color = _palette.ItemPlate;
            _convertTitleText.color = _palette.ItemName;
            _rateText.color = _palette.ItemDescription;
            _totalStatPlate.color = WithAlpha(_palette.ItemName, 0.1f);
            _totalStatLabel.color = _palette.SoftInk;
            _totalStatValue.color = _palette.ItemName;
            _convertibleStatBorder.color = _palette.StatHighlight;
            _convertibleStatPlate.color = Color.Lerp(_palette.ItemPlate, _palette.StatHighlight, 0.16f);
            _convertibleStatLabel.color = _palette.StatHighlight;
            _convertibleStatValue.color = _palette.StatHighlight;
            _minusPlate.color = _palette.ItemBand;
            _plusPlate.color = _palette.ItemBand;
            _minusText.color = _palette.ItemName;
            _plusText.color = _palette.ItemName;
            _amountText.color = _palette.CoinYellow;
            _convertButtonText.color = _palette.BuyButtonText;

            _adRowShadow.color = _palette.CardShadow;
            _adRowPlate.color = _palette.LightPlate;
            _adTile.color = _palette.AdButton;
            _adGlyph.color = Color.white;
            _adTitleText.color = _palette.Ink;
            _adCaptionText.color = _palette.InkSoft;
            _adButtonPlate.color = _palette.AdButton;
            _adButtonText.color = _palette.BuyButtonText;
            _adButtonCoin.color = Color.white;

            _stringBuilder.Clear();
            _stringBuilder.Append('+');
            _stringBuilder.Append(_currencySystem.AdRewardCoins);
            _adButtonText.text = _stringBuilder.ToString();

            // The caption text and the button's colour are repainted by RefreshAdRow instead, since both
            // depend on the daily cap's remaining count (issue #257) and this method, unlike Refresh, is
            // never called again once the card has opened.

            for (int bundleIndex = 0; bundleIndex < _bundles.Length; bundleIndex++)
            {
                BundleWidgets bundle = _bundles[bundleIndex];
                bundle.Shadow.color = _palette.CardShadow;
                bundle.Plate.color = _palette.LightPlate;
                bundle.Pile.color = Color.white;
                bundle.Amount.color = _palette.BundleAmount;
                bundle.Name.color = _palette.InkSoft;
                bundle.ButtonPlate.color = _palette.EarnButton;
                bundle.ButtonText.color = _palette.BuyButtonText;
            }

            if (_noBundlesText != null)
            {
                _noBundlesText.color = _palette.InkSoft;
            }
        }

        /// <summary>The active tab in its full colour, the other two multiplied down.</summary>
        private void PaintTabs()
        {
            _tabPowerUpsPlate.color = TabColour(_palette.TabPowerUp, ShopTab.PowerUps);
            _tabPowerUpsText.color = _palette.TabPowerUpText;

            _tabCoinsPlate.color = TabColour(_palette.TabCoin, ShopTab.Coins);
            _tabCoinsText.color = _palette.TabText;

            _tabDealsPlate.color = TabColour(_palette.TabPromotion, ShopTab.Deals);
            _tabDealsText.color = _palette.TabText;
        }

        private Color TabColour(Color lit, ShopTab tab)
            => _activeTab == tab ? lit : lit * _palette.InactiveTabTint;

        // ------------------------------------------------------------------------------ building

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("PowerUpShopPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            _cardRect = CellFactory.CreateCard(
                panelRect, "PowerUpShopCard", _cardSize, out _cardImage, out _cardShadowImage);

            // Everything below is built with a transparent colour: the card is built in Awake, before
            // injection is guaranteed, and PaintChrome in Start paints all of it.
            _headerText = UiTextFactory.Create(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, Color.clear, _displayFont);
            ((RectTransform)_headerText.transform).anchoredPosition =
                new Vector2(0f, (_cardSize.y * 0.5f) - (HEADER_INSET * 0.5f));
            // Text is set by RepaintLocalizedChrome, run from Start once injection has happened.

            BuildBalanceStrip(_cardRect);
            BuildTabs(_cardRect);
            BuildViewport(_cardRect);
            BuildDealsPlaceholder(_cardRect);
            BuildToast();
            BuildCloseButton();

            _panel = panelObject;
        }

        /// <summary>The dark plate with the coin and the balance on the left and the earn button on the
        /// right, so the coins and the way to get more sit together.</summary>
        private void BuildBalanceStrip(RectTransform parent)
        {
            float width = _cardSize.x - (SIDE_INSET * 2f);

            var stripObject = new GameObject("BalanceStrip", typeof(RectTransform), typeof(Image));
            var stripRect = (RectTransform)stripObject.transform;
            stripRect.SetParent(parent, false);
            Centre(stripRect, new Vector2(width, BALANCE_HEIGHT));
            stripRect.anchoredPosition = new Vector2(0f, TopY(BALANCE_TOP, BALANCE_HEIGHT));
            _balancePlate = ConfigureRounded(stripObject.GetComponent<Image>(), BALANCE_CORNER_RADIUS);

            float left = (-width * 0.5f) + BALANCE_PADDING;

            var coinObject = new GameObject("Coin", typeof(RectTransform), typeof(Image));
            var coinRect = (RectTransform)coinObject.transform;
            coinRect.SetParent(stripRect, false);
            Centre(coinRect, new Vector2(BALANCE_COIN_SIZE, BALANCE_COIN_SIZE));
            coinRect.anchoredPosition = new Vector2(left + (BALANCE_COIN_SIZE * 0.5f), 0f);
            _balanceCoin = ConfigureGlyph(coinObject.GetComponent<Image>(), _coinSprite);

            _balanceText = UiTextFactory.Create(
                stripRect, "Balance", _balanceFontSize, FontStyle.Bold, Color.clear, _displayFont);
            _balanceText.alignment = TextAnchor.MiddleLeft;
            var balanceRect = (RectTransform)_balanceText.transform;
            float textLeft = left + BALANCE_COIN_SIZE + ITEM_INNER_GAP;
            float textWidth = width - EARN_BUTTON_WIDTH - (BALANCE_PADDING * 2f) - (textLeft + (width * 0.5f));
            balanceRect.sizeDelta = new Vector2(textWidth, BALANCE_HEIGHT);
            balanceRect.anchoredPosition = new Vector2(textLeft + (textWidth * 0.5f), 0f);

            RectTransform earnRect = BuildChunkyButton(
                stripRect, "EarnButton", new Vector2(EARN_BUTTON_WIDTH, EARN_BUTTON_HEIGHT),
                out _earnButtonPlate, OpenCoins);
            earnRect.anchoredPosition = new Vector2(
                (width * 0.5f) - BALANCE_PADDING - (EARN_BUTTON_WIDTH * 0.5f), 0f);
            _earnButtonText = UiTextFactory.Create(
                earnRect, "Label", _earnFontSize, FontStyle.Bold, Color.clear, _displayFont);
        }

        private void BuildTabs(RectTransform parent)
        {
            float width = _cardSize.x - (SIDE_INSET * 2f);
            float tabWidth = (width - (TAB_GAP * (TAB_COUNT - 1))) / TAB_COUNT;
            float pitch = tabWidth + TAB_GAP;
            float y = TopY(TABS_TOP, TAB_HEIGHT);
            var size = new Vector2(tabWidth, TAB_HEIGHT);

            RectTransform powerUpsRect = BuildChunkyButton(
                parent, "TabPowerUps", size, out _tabPowerUpsPlate, () => SelectTab(ShopTab.PowerUps));
            powerUpsRect.anchoredPosition = new Vector2(-pitch, y);
            _tabPowerUpsText = UiTextFactory.Create(
                powerUpsRect, "Label", _tabFontSize, FontStyle.Bold, Color.clear, _displayFont);

            RectTransform coinsRect = BuildChunkyButton(
                parent, "TabCoins", size, out _tabCoinsPlate, () => SelectTab(ShopTab.Coins));
            coinsRect.anchoredPosition = new Vector2(0f, y);
            _tabCoinsText = UiTextFactory.Create(
                coinsRect, "Label", _tabFontSize, FontStyle.Bold, Color.clear, _displayFont);

            RectTransform dealsRect = BuildChunkyButton(
                parent, "TabDeals", size, out _tabDealsPlate, () => SelectTab(ShopTab.Deals));
            dealsRect.anchoredPosition = new Vector2(pitch, y);
            _tabDealsText = UiTextFactory.Create(
                dealsRect, "Label", _tabFontSize, FontStyle.Bold, Color.clear, _displayFont);
        }

        /// <summary>
        /// The scrolling grid: a clipped viewport whose invisible backdrop is the ScrollRect's raycast
        /// target — a drag has to land on a graphic to reach the ScrollRect at all — and a content rect
        /// sized exactly to the rows the nine cards need, pinned to the viewport's top.
        /// </summary>
        private void BuildViewport(RectTransform parent)
        {
            float width = _cardSize.x - (SIDE_INSET * 2f);
            int rowCount = (ItemCount + GRID_COLUMNS - 1) / GRID_COLUMNS;
            float contentHeight = GRID_TOP_PADDING + (rowCount * ITEM_HEIGHT)
                + ((rowCount - 1) * GRID_GAP) + GRID_BOTTOM_PADDING;

            _viewportObject = BuildScrollViewport(parent, "ItemsViewport", contentHeight, out _scrollRect);
            RectTransform contentRect = _scrollRect.content;

            float itemWidth = (width - (GRID_GAP * (GRID_COLUMNS - 1))) / GRID_COLUMNS;
            for (int itemIndex = 0; itemIndex < ItemCount; itemIndex++)
            {
                int column = itemIndex % GRID_COLUMNS;
                int row = itemIndex / GRID_COLUMNS;
                float x = (column - ((GRID_COLUMNS - 1) * 0.5f)) * (itemWidth + GRID_GAP);
                float y = -(GRID_TOP_PADDING + (row * (ITEM_HEIGHT + GRID_GAP)) + (ITEM_HEIGHT * 0.5f));
                _items[itemIndex] = BuildItem(contentRect, itemIndex, itemWidth, new Vector2(x, y));
            }
        }

        /// <summary>
        /// A clipped, vertically scrolling viewport in the grid's place, with an invisible backdrop as
        /// the ScrollRect's raycast target — a drag has to land on a graphic to reach the ScrollRect at
        /// all — and a content rect of exactly <paramref name="contentHeight"/> pinned to its top.
        /// </summary>
        private GameObject BuildScrollViewport(
            RectTransform parent, string objectName, float contentHeight, out ScrollRect scrollRect)
        {
            float width = _cardSize.x - (SIDE_INSET * 2f);
            float height = _cardSize.y - VIEWPORT_TOP - VIEWPORT_BOTTOM_INSET;

            var viewportObject = new GameObject(
                objectName, typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            var viewportRect = (RectTransform)viewportObject.transform;
            viewportRect.SetParent(parent, false);
            Centre(viewportRect, new Vector2(width, height));
            viewportRect.anchoredPosition = new Vector2(0f, TopY(VIEWPORT_TOP, height));

            var backdrop = viewportObject.GetComponent<Image>();
            backdrop.color = Color.clear;
            backdrop.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            var contentRect = (RectTransform)contentObject.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0.5f, 1f);
            contentRect.anchorMax = new Vector2(0.5f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(width, contentHeight);
            contentRect.anchoredPosition = Vector2.zero;

            scrollRect = viewportObject.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            return viewportObject;
        }

        /// <summary>
        /// One item card, top to bottom as the design draws it: a lighter band with the name, the
        /// glossy tile with the glyph and the held badge on its corner, the description, and the price
        /// button. The button is the only tap target; the card itself is not, so a drag begun anywhere
        /// on it still scrolls.
        /// </summary>
        private ItemWidgets BuildItem(RectTransform parent, int itemIndex, float itemWidth, Vector2 anchoredPosition)
        {
            var item = new ItemWidgets();
            PowerUpKind kind = ItemKinds[itemIndex];

            var rootObject = new GameObject($"ShopItem_{kind}", typeof(RectTransform));
            var rootRect = (RectTransform)rootObject.transform;
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(itemWidth, ITEM_HEIGHT);
            rootRect.anchoredPosition = anchoredPosition;
            item.Rect = rootRect;

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(rootRect, false);
            Centre(shadowRect, new Vector2(itemWidth, ITEM_HEIGHT));
            shadowRect.anchoredPosition = new Vector2(0f, -ITEM_SHADOW_DROP);
            item.Shadow = ConfigureRounded(shadowObject.GetComponent<Image>(), ITEM_CORNER_RADIUS);

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(rootRect, false);
            Centre(plateRect, new Vector2(itemWidth, ITEM_HEIGHT));
            item.Plate = ConfigureRounded(plateObject.GetComponent<Image>(), ITEM_CORNER_RADIUS);

            // The band: a rounded plate for the top corners, and a flat one over its lower half so the
            // bottom corners are square where the band meets the plate.
            float bandY = (ITEM_HEIGHT * 0.5f) - (BAND_HEIGHT * 0.5f);
            var bandObject = new GameObject("Band", typeof(RectTransform), typeof(Image));
            var bandRect = (RectTransform)bandObject.transform;
            bandRect.SetParent(rootRect, false);
            Centre(bandRect, new Vector2(itemWidth, BAND_HEIGHT));
            bandRect.anchoredPosition = new Vector2(0f, bandY);
            item.Band = ConfigureRounded(bandObject.GetComponent<Image>(), ITEM_CORNER_RADIUS);

            var bandSquareObject = new GameObject("BandSquare", typeof(RectTransform), typeof(Image));
            var bandSquareRect = (RectTransform)bandSquareObject.transform;
            bandSquareRect.SetParent(rootRect, false);
            Centre(bandSquareRect, new Vector2(itemWidth, BAND_HEIGHT * 0.5f));
            bandSquareRect.anchoredPosition = new Vector2(0f, bandY - (BAND_HEIGHT * 0.25f));
            item.BandSquare = bandSquareObject.GetComponent<Image>();
            item.BandSquare.color = Color.clear;
            item.BandSquare.raycastTarget = false;

            item.Name = UiTextFactory.Create(
                rootRect, "Name", _itemNameFontSize, FontStyle.Bold, Color.clear, _displayFont);
            var nameRect = (RectTransform)item.Name.transform;
            nameRect.sizeDelta = new Vector2(itemWidth - (ITEM_PADDING * 2f), BAND_HEIGHT);
            nameRect.anchoredPosition = new Vector2(0f, bandY);
            item.Name.horizontalOverflow = HorizontalWrapMode.Wrap;
            item.Name.resizeTextForBestFit = true;
            item.Name.resizeTextMinSize = 14;
            item.Name.resizeTextMaxSize = _itemNameFontSize;

            var saleObject = new GameObject("SaleBadge", typeof(RectTransform), typeof(Image));
            var saleRect = (RectTransform)saleObject.transform;
            saleRect.SetParent(rootRect, false);
            Centre(saleRect, new Vector2(SALE_BADGE_WIDTH, SALE_BADGE_HEIGHT));
            saleRect.anchoredPosition = new Vector2(
                (itemWidth * 0.5f) - (SALE_BADGE_WIDTH * 0.5f) - 6f, bandY);
            item.SaleBadge = ConfigureRounded(saleObject.GetComponent<Image>(), SALE_BADGE_CORNER_RADIUS);
            item.SaleBadge.enabled = false;
            item.SaleText = UiTextFactory.Create(
                saleRect, "Label", _badgeFontSize, FontStyle.Bold, Color.clear, _displayFont);

            float tileY = bandY - (BAND_HEIGHT * 0.5f) - ITEM_PADDING - (TILE_SIZE * 0.5f);
            var tileObject = new GameObject("Tile", typeof(RectTransform), typeof(Image));
            var tileRect = (RectTransform)tileObject.transform;
            tileRect.SetParent(rootRect, false);
            Centre(tileRect, new Vector2(TILE_SIZE, TILE_SIZE));
            tileRect.anchoredPosition = new Vector2(0f, tileY);
            item.Tile = ConfigureGlyph(tileObject.GetComponent<Image>(), _tileSprite);

            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(tileRect, false);
            Centre(glyphRect, new Vector2(GLYPH_SIZE, GLYPH_SIZE));
            glyphRect.anchoredPosition = new Vector2(0f, GLYPH_RISE);
            item.Glyph = ConfigureGlyph(glyphObject.GetComponent<Image>(), IconFor(itemIndex));

            // The glyph is also a tap target — reopens this kind's info popup, the same card the HUD
            // strip's long-press opens. An invisible plate over the tile, like the Coins tab's "take it
            // all" figure: it stays visually unchanged and only adds a hit area, sized a little past the
            // glyph itself for a comfortable touch target without covering the price button below.
            PowerUpKind capturedGlyphKind = ItemKinds[itemIndex];
            var glyphTapObject = new GameObject(
                "GlyphTapTarget", typeof(RectTransform), typeof(Image), typeof(LevelPathNodeButton));
            var glyphTapRect = (RectTransform)glyphTapObject.transform;
            glyphTapRect.SetParent(tileRect, false);
            Centre(glyphTapRect, new Vector2(TILE_SIZE, TILE_SIZE));
            var glyphTapPlate = glyphTapObject.GetComponent<Image>();
            glyphTapPlate.color = Color.clear;
            glyphTapPlate.raycastTarget = true;
            glyphTapObject.GetComponent<LevelPathNodeButton>().SetClicked(
                () => _infoPopupSystem.Open(InfoPopupSubjectKind.PowerUp, (int)capturedGlyphKind));

            BuildHeldBadge(item, tileRect);

            float descriptionY = tileY - (TILE_SIZE * 0.5f) - ITEM_INNER_GAP - (DESCRIPTION_HEIGHT * 0.5f);
            item.Description = UiTextFactory.Create(
                rootRect, "Description", _itemDescriptionFontSize, FontStyle.Bold, Color.clear);
            item.Description.alignment = TextAnchor.UpperCenter;
            item.Description.horizontalOverflow = HorizontalWrapMode.Wrap;
            var descriptionRect = (RectTransform)item.Description.transform;
            descriptionRect.sizeDelta = new Vector2(itemWidth - (ITEM_PADDING * 2f), DESCRIPTION_HEIGHT);
            descriptionRect.anchoredPosition = new Vector2(0f, descriptionY);

            float buttonY = (-ITEM_HEIGHT * 0.5f) + ITEM_PADDING + (BUY_BUTTON_HEIGHT * 0.5f);
            int capturedIndex = itemIndex;
            RectTransform buttonRect = BuildChunkyButton(
                rootRect, "BuyButton", new Vector2(itemWidth - (ITEM_PADDING * 2f), BUY_BUTTON_HEIGHT),
                out item.ButtonPlate, () => Buy(capturedIndex));
            buttonRect.anchoredPosition = new Vector2(0f, buttonY);

            var coinObject = new GameObject("Coin", typeof(RectTransform), typeof(Image));
            var coinRect = (RectTransform)coinObject.transform;
            coinRect.SetParent(buttonRect, false);
            Centre(coinRect, new Vector2(COIN_GLYPH_SIZE, COIN_GLYPH_SIZE));
            coinRect.anchoredPosition = new Vector2(COIN_X_PLAIN, 0f);
            item.Coin = ConfigureGlyph(coinObject.GetComponent<Image>(), _coinSprite);

            var lockObject = new GameObject("Lock", typeof(RectTransform), typeof(Image));
            var lockRect = (RectTransform)lockObject.transform;
            lockRect.SetParent(buttonRect, false);
            Centre(lockRect, new Vector2(LOCK_GLYPH_SIZE, LOCK_GLYPH_SIZE));
            lockRect.anchoredPosition = new Vector2(COIN_X_PLAIN, 0f);
            item.LockGlyph = ConfigureGlyph(lockObject.GetComponent<Image>(), _lockSprite);
            item.LockGlyph.enabled = false;

            item.Price = UiTextFactory.Create(
                buttonRect, "Price", _itemPriceFontSize, FontStyle.Bold, Color.clear, _displayFont);
            item.Price.alignment = TextAnchor.MiddleLeft;
            var priceRect = (RectTransform)item.Price.transform;
            priceRect.sizeDelta = new Vector2(PRICE_TEXT_WIDTH, BUY_BUTTON_HEIGHT);
            priceRect.anchoredPosition = new Vector2(PRICE_X_PLAIN, 0f);

            item.WasPrice = UiTextFactory.Create(
                buttonRect, "WasPrice", _wasPriceFontSize, FontStyle.Bold, Color.clear, _displayFont);
            item.WasPrice.alignment = TextAnchor.MiddleRight;
            var wasPriceRect = (RectTransform)item.WasPrice.transform;
            wasPriceRect.sizeDelta = new Vector2(WAS_PRICE_WIDTH, BUY_BUTTON_HEIGHT);
            wasPriceRect.anchoredPosition = new Vector2(WAS_PRICE_X, 0f);

            var barObject = new GameObject("Strikethrough", typeof(RectTransform), typeof(Image));
            var barRect = (RectTransform)barObject.transform;
            barRect.SetParent(wasPriceRect, false);
            barRect.anchorMin = new Vector2(1f, 0.5f);
            barRect.anchorMax = new Vector2(1f, 0.5f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.sizeDelta = new Vector2(0f, STRIKETHROUGH_THICKNESS);
            barRect.anchoredPosition = Vector2.zero;
            item.WasPriceBar = barObject.GetComponent<Image>();
            item.WasPriceBar.color = Color.clear;
            item.WasPriceBar.raycastTarget = false;
            item.WasPriceBar.enabled = false;

            return item;
        }

        /// <summary>The green "x2" badge on the tile's top-right corner: a cream ring under a green
        /// disc, so it reads against both the tile and the plate behind it.</summary>
        private void BuildHeldBadge(ItemWidgets item, RectTransform tileRect)
        {
            var badgeObject = new GameObject("HeldBadge", typeof(RectTransform));
            var badgeRect = (RectTransform)badgeObject.transform;
            badgeRect.SetParent(tileRect, false);
            Centre(badgeRect, new Vector2(HELD_BADGE_SIZE, HELD_BADGE_SIZE));
            badgeRect.anchoredPosition = new Vector2(TILE_SIZE * 0.5f, TILE_SIZE * 0.5f);
            item.HeldBadge = badgeObject;

            var ringObject = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            var ringRect = (RectTransform)ringObject.transform;
            ringRect.SetParent(badgeRect, false);
            Centre(ringRect, new Vector2(HELD_BADGE_SIZE, HELD_BADGE_SIZE));
            item.HeldRing = ConfigureGlyph(ringObject.GetComponent<Image>(), UiSpriteFactory.Circle);

            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRect = (RectTransform)fillObject.transform;
            fillRect.SetParent(badgeRect, false);
            Centre(fillRect, new Vector2(
                HELD_BADGE_SIZE - (HELD_BADGE_BORDER * 2f), HELD_BADGE_SIZE - (HELD_BADGE_BORDER * 2f)));
            item.HeldFill = ConfigureGlyph(fillObject.GetComponent<Image>(), UiSpriteFactory.Circle);

            item.HeldCount = UiTextFactory.Create(
                badgeRect, "Count", _badgeFontSize, FontStyle.Bold, Color.clear, _displayFont);

            badgeObject.SetActive(false);
        }

        /// <summary>
        /// The Coins tab, top to bottom as the design draws it: an "earn for free" label, the convert
        /// panel, the ad row, a "coin packs" label and one row per configured bundle. Built in Start
        /// because the bundle count is config; hidden until its tab is picked.
        /// </summary>
        private void BuildCoinsTab()
        {
            float width = _cardSize.x - (SIDE_INSET * 2f);
            int bundleCount = _bundleConfig.BundleCount;
            float bundlesHeight = bundleCount > 0
                ? (bundleCount * ROW_HEIGHT) + ((bundleCount - 1) * ROW_GAP)
                : ROW_HEIGHT;
            float contentHeight = GRID_TOP_PADDING + SECTION_LABEL_HEIGHT + SECTION_GAP + CONVERT_PANEL_HEIGHT
                + ROW_GAP + ROW_HEIGHT + (SECTION_GAP * 2f) + SECTION_LABEL_HEIGHT + SECTION_GAP
                + bundlesHeight + GRID_BOTTOM_PADDING;

            _coinsViewportObject = BuildScrollViewport(_cardRect, "CoinsViewport", contentHeight, out _coinsScrollRect);
            RectTransform content = _coinsScrollRect.content;

            float cursor = -GRID_TOP_PADDING;
            _freeSectionText = BuildSectionLabel(content, "FreeSection", width, ref cursor);
            cursor -= SECTION_GAP;
            BuildConvertPanel(content, width, ref cursor);
            cursor -= ROW_GAP;
            BuildAdRow(content, width, ref cursor);
            cursor -= SECTION_GAP * 2f;
            _bundlesSectionText = BuildSectionLabel(content, "BundlesSection", width, ref cursor);
            cursor -= SECTION_GAP;

            _bundles = new BundleWidgets[bundleCount];
            for (int bundleIndex = 0; bundleIndex < bundleCount; bundleIndex++)
            {
                _bundles[bundleIndex] = BuildBundleRow(content, bundleIndex, width, ref cursor);
                cursor -= ROW_GAP;
            }

            if (bundleCount == 0)
            {
                _noBundlesText = UiTextFactory.Create(
                    content, "NoBundles", _rowCaptionFontSize, FontStyle.Bold, Color.clear);
                var noBundlesRect = (RectTransform)_noBundlesText.transform;
                TopAnchor(noBundlesRect, new Vector2(width, ROW_HEIGHT), cursor);
            }

            _coinsViewportObject.SetActive(false);
        }

        /// <summary>A small spaced-out caption naming the rows under it, flush left. Text is filled in
        /// by <see cref="RepaintLocalizedChrome"/>, not here — this only lays the label out.</summary>
        private Text BuildSectionLabel(RectTransform parent, string objectName, float width, ref float cursor)
        {
            Text label = UiTextFactory.Create(parent, objectName, _sectionLabelFontSize, FontStyle.Bold, Color.clear);
            label.alignment = TextAnchor.MiddleLeft;
            var rect = (RectTransform)label.transform;
            TopAnchor(rect, new Vector2(width - (ROW_PADDING * 2f), SECTION_LABEL_HEIGHT), cursor);
            rect.anchoredPosition = new Vector2(ROW_PADDING, rect.anchoredPosition.y);
            cursor -= SECTION_LABEL_HEIGHT;
            return label;
        }

        /// <summary>
        /// The dark convert panel: title and rate on one line, the two stat plates under it, the
        /// stepper, and the Convert button. The "convertible" plate is the one with the highlight
        /// border, because that figure is the one the player is here to spend.
        /// </summary>
        private void BuildConvertPanel(RectTransform parent, float width, ref float cursor)
        {
            var panelObject = new GameObject("ConvertPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(parent, false);
            TopAnchor(panelRect, new Vector2(width, CONVERT_PANEL_HEIGHT), cursor);
            _convertPanelPlate = ConfigureRounded(panelObject.GetComponent<Image>(), PANEL_CORNER_RADIUS);
            cursor -= CONVERT_PANEL_HEIGHT;

            float inner = width - (PANEL_PADDING * 2f);
            float y = (CONVERT_PANEL_HEIGHT * 0.5f) - PANEL_PADDING;

            _convertTitleText = UiTextFactory.Create(
                panelRect, "Title", _panelTitleFontSize, FontStyle.Bold, Color.clear, _displayFont);
            _convertTitleText.alignment = TextAnchor.MiddleLeft;
            var titleRect = (RectTransform)_convertTitleText.transform;
            titleRect.sizeDelta = new Vector2(inner * 0.62f, PANEL_TITLE_HEIGHT);
            titleRect.anchoredPosition = new Vector2((-inner * 0.5f) + (inner * 0.31f), y - (PANEL_TITLE_HEIGHT * 0.5f));

            _rateText = UiTextFactory.Create(panelRect, "Rate", _rowCaptionFontSize, FontStyle.Bold, Color.clear);
            _rateText.alignment = TextAnchor.MiddleRight;
            var rateRect = (RectTransform)_rateText.transform;
            rateRect.sizeDelta = new Vector2(inner * 0.38f, PANEL_TITLE_HEIGHT);
            rateRect.anchoredPosition = new Vector2((inner * 0.5f) - (inner * 0.19f), y - (PANEL_TITLE_HEIGHT * 0.5f));
            y -= PANEL_TITLE_HEIGHT + ITEM_INNER_GAP;

            float statWidth = (inner - STAT_PLATE_GAP) * 0.5f;
            float statY = y - (STAT_PLATE_HEIGHT * 0.5f);
            BuildStatPlate(panelRect, "TotalStat", new Vector2((-inner * 0.5f) + (statWidth * 0.5f), statY), statWidth,
                false, out _totalStatPlate, out _, out _totalStatLabel, out _totalStatValue);
            BuildStatPlate(panelRect, "ConvertibleStat", new Vector2((inner * 0.5f) - (statWidth * 0.5f), statY), statWidth,
                true, out _convertibleStatPlate, out _convertibleStatBorder,
                out _convertibleStatLabel, out _convertibleStatValue);
            y -= STAT_PLATE_HEIGHT + ITEM_INNER_GAP;

            float stepperY = y - (STEPPER_HEIGHT * 0.5f);
            RectTransform minusRect = BuildChunkyButton(
                panelRect, "Minus", new Vector2(STEPPER_BUTTON_WIDTH, STEPPER_HEIGHT), out _minusPlate, () => StepAmount(-AMOUNT_STEP));
            minusRect.anchoredPosition = new Vector2(-(STEPPER_AMOUNT_WIDTH * 0.5f) - (STEPPER_BUTTON_WIDTH * 0.5f), stepperY);
            _minusText = UiTextFactory.Create(minusRect, "Glyph", _stepperFontSize, FontStyle.Bold, Color.clear, _displayFont);
            _minusText.text = "-";

            RectTransform plusRect = BuildChunkyButton(
                panelRect, "Plus", new Vector2(STEPPER_BUTTON_WIDTH, STEPPER_HEIGHT), out _plusPlate, () => StepAmount(AMOUNT_STEP));
            plusRect.anchoredPosition = new Vector2((STEPPER_AMOUNT_WIDTH * 0.5f) + (STEPPER_BUTTON_WIDTH * 0.5f), stepperY);
            _plusText = UiTextFactory.Create(plusRect, "Glyph", _stepperFontSize, FontStyle.Bold, Color.clear, _displayFont);
            _plusText.text = "+";

            // The figure is a tap target too — the "take it all" shortcut — on an invisible plate.
            var amountObject = new GameObject("Amount", typeof(RectTransform), typeof(Image), typeof(LevelPathNodeButton));
            var amountRect = (RectTransform)amountObject.transform;
            amountRect.SetParent(panelRect, false);
            Centre(amountRect, new Vector2(STEPPER_AMOUNT_WIDTH, STEPPER_HEIGHT));
            amountRect.anchoredPosition = new Vector2(0f, stepperY);
            var amountPlate = amountObject.GetComponent<Image>();
            amountPlate.color = Color.clear;
            amountPlate.raycastTarget = true;
            amountObject.GetComponent<LevelPathNodeButton>().SetClicked(TakeAllAmount);
            _amountText = UiTextFactory.Create(amountRect, "Figure", _stepperFontSize, FontStyle.Bold, Color.clear, _displayFont);
            y -= STEPPER_HEIGHT + ITEM_INNER_GAP;

            RectTransform convertRect = BuildChunkyButton(
                panelRect, "ConvertButton", new Vector2(inner, ACTION_BUTTON_HEIGHT), out _convertButtonPlate, Convert);
            convertRect.anchoredPosition = new Vector2(0f, y - (ACTION_BUTTON_HEIGHT * 0.5f));
            _convertButtonText = UiTextFactory.Create(
                convertRect, "Label", _earnFontSize, FontStyle.Bold, Color.clear, _displayFont);
        }

        /// <summary>One stat on the convert panel: a caption over a figure on a translucent plate, with
        /// an optional highlight border drawn as a slightly larger plate underneath. The caption text is
        /// filled in by <see cref="RepaintLocalizedChrome"/>, not here.</summary>
        private void BuildStatPlate(
            RectTransform parent, string objectName, Vector2 anchoredPosition, float width, bool bordered,
            out Image plate, out Image border, out Text label, out Text value)
        {
            border = null;
            if (bordered)
            {
                var borderObject = new GameObject(objectName + "Border", typeof(RectTransform), typeof(Image));
                var borderRect = (RectTransform)borderObject.transform;
                borderRect.SetParent(parent, false);
                Centre(borderRect, new Vector2(width + (STAT_PLATE_BORDER * 2f), STAT_PLATE_HEIGHT + (STAT_PLATE_BORDER * 2f)));
                borderRect.anchoredPosition = anchoredPosition;
                border = ConfigureRounded(borderObject.GetComponent<Image>(), STAT_PLATE_CORNER_RADIUS + STAT_PLATE_BORDER);
            }

            var plateObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(parent, false);
            Centre(plateRect, new Vector2(width, STAT_PLATE_HEIGHT));
            plateRect.anchoredPosition = anchoredPosition;
            plate = ConfigureRounded(plateObject.GetComponent<Image>(), STAT_PLATE_CORNER_RADIUS);

            label = UiTextFactory.Create(plateRect, "Label", _statLabelFontSize, FontStyle.Bold, Color.clear);
            label.alignment = TextAnchor.MiddleLeft;
            var labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(width - (ROW_PADDING * 2f), STAT_PLATE_HEIGHT * 0.4f);
            labelRect.anchoredPosition = new Vector2(0f, STAT_PLATE_HEIGHT * 0.22f);

            value = UiTextFactory.Create(plateRect, "Value", _statValueFontSize, FontStyle.Bold, Color.clear, _displayFont);
            value.alignment = TextAnchor.MiddleLeft;
            var valueRect = (RectTransform)value.transform;
            valueRect.sizeDelta = new Vector2(width - (ROW_PADDING * 2f), STAT_PLATE_HEIGHT * 0.6f);
            valueRect.anchoredPosition = new Vector2(0f, -STAT_PLATE_HEIGHT * 0.15f);
        }

        /// <summary>The watch-an-ad row: a blue tile with the play glyph, the title and the reward
        /// caption, and a blue button quoting the reward.</summary>
        private void BuildAdRow(RectTransform parent, float width, ref float cursor)
        {
            RectTransform rowRect = BuildLightRow(parent, "AdRow", width, ref cursor, out _adRowShadow, out _adRowPlate);
            float left = (-width * 0.5f) + ROW_PADDING;

            var tileObject = new GameObject("Tile", typeof(RectTransform), typeof(Image));
            var tileRect = (RectTransform)tileObject.transform;
            tileRect.SetParent(rowRect, false);
            Centre(tileRect, new Vector2(ROW_TILE_SIZE, ROW_TILE_SIZE));
            tileRect.anchoredPosition = new Vector2(left + (ROW_TILE_SIZE * 0.5f), 0f);
            _adTile = ConfigureRounded(tileObject.GetComponent<Image>(), ROW_TILE_CORNER_RADIUS);

            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(tileRect, false);
            Centre(glyphRect, new Vector2(ROW_TILE_GLYPH_SIZE, ROW_TILE_GLYPH_SIZE));
            _adGlyph = ConfigureGlyph(glyphObject.GetComponent<Image>(), _adSprite);

            float textLeft = left + ROW_TILE_SIZE + ITEM_INNER_GAP;
            float textWidth = width - ROW_PADDING - ROW_BUTTON_WIDTH - ITEM_INNER_GAP - (textLeft + (width * 0.5f));
            float textCentreX = textLeft + (textWidth * 0.5f);

            _adTitleText = UiTextFactory.Create(rowRect, "Title", _rowTitleFontSize, FontStyle.Bold, Color.clear, _displayFont);
            _adTitleText.alignment = TextAnchor.MiddleLeft;
            var titleRect = (RectTransform)_adTitleText.transform;
            titleRect.sizeDelta = new Vector2(textWidth, ROW_HEIGHT * 0.5f);
            titleRect.anchoredPosition = new Vector2(textCentreX, ROW_HEIGHT * 0.16f);

            _adCaptionText = UiTextFactory.Create(rowRect, "Caption", _rowCaptionFontSize, FontStyle.Bold, Color.clear);
            _adCaptionText.alignment = TextAnchor.MiddleLeft;
            var captionRect = (RectTransform)_adCaptionText.transform;
            captionRect.sizeDelta = new Vector2(textWidth, ROW_HEIGHT * 0.4f);
            captionRect.anchoredPosition = new Vector2(textCentreX, -ROW_HEIGHT * 0.17f);

            RectTransform buttonRect = BuildChunkyButton(
                rowRect, "AdButton", new Vector2(ROW_BUTTON_WIDTH, ROW_BUTTON_HEIGHT), out _adButtonPlate,
                OnAdButtonTapped);
            buttonRect.anchoredPosition = new Vector2((width * 0.5f) - ROW_PADDING - (ROW_BUTTON_WIDTH * 0.5f), 0f);

            _adButtonText = UiTextFactory.Create(buttonRect, "Label", _itemPriceFontSize, FontStyle.Bold, Color.clear, _displayFont);
            _adButtonText.alignment = TextAnchor.MiddleRight;
            var adLabelRect = (RectTransform)_adButtonText.transform;
            adLabelRect.sizeDelta = new Vector2(ROW_BUTTON_WIDTH * 0.46f, ROW_BUTTON_HEIGHT);
            adLabelRect.anchoredPosition = new Vector2(-ROW_BUTTON_WIDTH * 0.16f, 0f);

            var coinObject = new GameObject("Coin", typeof(RectTransform), typeof(Image));
            var coinRect = (RectTransform)coinObject.transform;
            coinRect.SetParent(buttonRect, false);
            Centre(coinRect, new Vector2(COIN_GLYPH_SIZE, COIN_GLYPH_SIZE));
            coinRect.anchoredPosition = new Vector2(ROW_BUTTON_WIDTH * 0.24f, 0f);
            _adButtonCoin = ConfigureGlyph(coinObject.GetComponent<Image>(), _coinSprite);
        }

        /// <summary>One coin bundle: its pile, the coin figure large in orange with the bundle's name
        /// under it, and a green button. Built once here with the plain "BUY" label; the store's real
        /// localized price, once <see cref="CoinBundlePriceModel"/> has one, is painted in by
        /// <see cref="RefreshBundleRows"/> on every repaint (issue #256), not here — this only lays the
        /// row out.</summary>
        private BundleWidgets BuildBundleRow(RectTransform parent, int bundleIndex, float width, ref float cursor)
        {
            var bundle = new BundleWidgets();
            CoinBundle config = _bundleConfig.BundleAt(bundleIndex);
            bundle.Sku = config.Sku;

            RectTransform rowRect = BuildLightRow(parent, $"Bundle_{bundleIndex}", width, ref cursor, out bundle.Shadow, out bundle.Plate);
            float left = (-width * 0.5f) + ROW_PADDING;

            var pileObject = new GameObject("Pile", typeof(RectTransform), typeof(Image));
            var pileRect = (RectTransform)pileObject.transform;
            pileRect.SetParent(rowRect, false);
            Centre(pileRect, new Vector2(ROW_HEIGHT - ROW_PADDING, ROW_HEIGHT - ROW_PADDING));
            pileRect.anchoredPosition = new Vector2(left + ((ROW_HEIGHT - ROW_PADDING) * 0.5f), 0f);
            bundle.Pile = ConfigureGlyph(pileObject.GetComponent<Image>(), CoinPileFor(bundleIndex));

            float textLeft = left + (ROW_HEIGHT - ROW_PADDING) + ITEM_INNER_GAP;
            float textWidth = width - ROW_PADDING - ROW_BUTTON_WIDTH - ITEM_INNER_GAP - (textLeft + (width * 0.5f));
            float textCentreX = textLeft + (textWidth * 0.5f);

            bundle.Amount = UiTextFactory.Create(rowRect, "Amount", _bundleAmountFontSize, FontStyle.Bold, Color.clear, _displayFont);
            bundle.Amount.alignment = TextAnchor.MiddleLeft;
            var amountRect = (RectTransform)bundle.Amount.transform;
            amountRect.sizeDelta = new Vector2(textWidth, ROW_HEIGHT * 0.55f);
            amountRect.anchoredPosition = new Vector2(textCentreX, ROW_HEIGHT * 0.14f);
            bundle.Amount.text = config.CoinAmount.ToString();

            bundle.Name = UiTextFactory.Create(rowRect, "Name", _rowCaptionFontSize, FontStyle.Bold, Color.clear);
            bundle.Name.alignment = TextAnchor.MiddleLeft;
            var nameRect = (RectTransform)bundle.Name.transform;
            nameRect.sizeDelta = new Vector2(textWidth, ROW_HEIGHT * 0.35f);
            nameRect.anchoredPosition = new Vector2(textCentreX, -ROW_HEIGHT * 0.22f);
            bundle.Name.text = config.DisplayName;

            string sku = config.Sku;
            RectTransform buttonRect = BuildChunkyButton(
                rowRect, "BuyButton", new Vector2(ROW_BUTTON_WIDTH, ROW_BUTTON_HEIGHT), out bundle.ButtonPlate,
                () => PurchaseBundle(sku).Forget());
            buttonRect.anchoredPosition = new Vector2((width * 0.5f) - ROW_PADDING - (ROW_BUTTON_WIDTH * 0.5f), 0f);
            bundle.ButtonText = UiTextFactory.Create(buttonRect, "Label", _earnFontSize, FontStyle.Bold, Color.clear, _displayFont);

            return bundle;
        }

        /// <summary>A white row plate with a warm drop shadow, top-anchored at <paramref name="cursor"/>,
        /// which it advances past itself.</summary>
        private RectTransform BuildLightRow(
            RectTransform parent, string objectName, float width, ref float cursor, out Image shadow, out Image plate)
        {
            var rowObject = new GameObject(objectName, typeof(RectTransform));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(parent, false);
            TopAnchor(rowRect, new Vector2(width, ROW_HEIGHT), cursor);
            cursor -= ROW_HEIGHT;

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(rowRect, false);
            Centre(shadowRect, new Vector2(width, ROW_HEIGHT));
            shadowRect.anchoredPosition = new Vector2(0f, -ROW_SHADOW_DROP);
            shadow = ConfigureRounded(shadowObject.GetComponent<Image>(), ROW_CORNER_RADIUS);

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(rowRect, false);
            Centre(plateRect, new Vector2(width, ROW_HEIGHT));
            plate = ConfigureRounded(plateObject.GetComponent<Image>(), ROW_CORNER_RADIUS);

            return rowRect;
        }

        /// <summary>The pile for the n-th bundle row: the n-th sprite, or the last one past the end.</summary>
        private Sprite CoinPileFor(int bundleIndex)
        {
            if (_coinPileSprites == null || _coinPileSprites.Length == 0)
            {
                return _coinSprite;
            }

            Sprite pile = _coinPileSprites[Mathf.Min(bundleIndex, _coinPileSprites.Length - 1)];
            return pile != null ? pile : _coinSprite;
        }

        /// <summary>Pins <paramref name="rect"/> to the top-centre of its parent with its own top edge
        /// <paramref name="topY"/> below the parent's top, its centre being its pivot.</summary>
        private static void TopAnchor(RectTransform rect, Vector2 size, float topY)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, topY - (size.y * 0.5f));
        }

        /// <summary>What the Deals tab shows until a campaign screen exists (issue #165): one plate with
        /// one sentence, in the grid's place.</summary>
        private void BuildDealsPlaceholder(RectTransform parent)
        {
            float width = _cardSize.x - (SIDE_INSET * 2f);
            float height = _cardSize.y - VIEWPORT_TOP - VIEWPORT_BOTTOM_INSET;

            _dealsPlaceholder = new GameObject("DealsPlaceholder", typeof(RectTransform), typeof(Image));
            var placeholderRect = (RectTransform)_dealsPlaceholder.transform;
            placeholderRect.SetParent(parent, false);
            Centre(placeholderRect, new Vector2(width, height));
            placeholderRect.anchoredPosition = new Vector2(0f, TopY(VIEWPORT_TOP, height));
            _dealsPlate = ConfigureRounded(_dealsPlaceholder.GetComponent<Image>(), ITEM_CORNER_RADIUS);

            _dealsText = UiTextFactory.Create(
                placeholderRect, "Label", _placeholderFontSize, FontStyle.Bold, Color.clear, _displayFont);

            _dealsPlaceholder.SetActive(false);
        }

        /// <summary>The message pill at the foot of the card, over the grid's bottom padding so it never
        /// covers a button.</summary>
        private void BuildToast()
        {
            var toastObject = new GameObject("Toast", typeof(RectTransform), typeof(Image));
            _toastRect = (RectTransform)toastObject.transform;
            _toastRect.SetParent(_cardRect, false);
            Centre(_toastRect, new Vector2(_cardSize.x * 0.5f, TOAST_HEIGHT));
            _toastRect.anchoredPosition = new Vector2(
                0f, (-_cardSize.y * 0.5f) + TOAST_BOTTOM + (TOAST_HEIGHT * 0.5f));
            _toastPlate = ConfigureRounded(toastObject.GetComponent<Image>(), TOAST_CORNER_RADIUS);

            _toastText = UiTextFactory.Create(
                _toastRect, "Label", _toastFontSize, FontStyle.Bold, Color.clear, _displayFont);

            toastObject.SetActive(false);
        }

        /// <summary>
        /// A glossy 3D plate that is also an EventSystem tap target: the sliced button sprite, tinted
        /// by the caller, with a <see cref="LevelPathNodeButton"/> forwarding its click. Its own rect
        /// is the hit area. The caller positions it and adds its label.
        /// </summary>
        private RectTransform BuildChunkyButton(
            RectTransform parent, string objectName, Vector2 size, out Image plate, System.Action onClick)
        {
            var buttonObject = new GameObject(
                objectName, typeof(RectTransform), typeof(Image), typeof(LevelPathNodeButton));
            var buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(parent, false);
            Centre(buttonRect, size);

            plate = buttonObject.GetComponent<Image>();
            plate.sprite = _buttonSprite;
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = BUTTON_SLICE_SCALE;
            plate.color = Color.clear;
            plate.raycastTarget = true;

            buttonObject.GetComponent<LevelPathNodeButton>().SetClicked(onClick);
            return buttonRect;
        }

        /// <summary>The item's glyph, authored in <see cref="ItemKinds"/> order.</summary>
        private Sprite IconFor(int itemIndex)
        {
            Sprite icon = _rowIcons != null && itemIndex < _rowIcons.Length ? _rowIcons[itemIndex] : null;
            if (icon == null)
            {
                Debug.LogError($"{nameof(PowerUpShopView)} has no icon sprite assigned for {ItemKinds[itemIndex]}.", this);
            }

            return icon;
        }

        /// <summary>The shared rounded sprite sliced to <paramref name="radius"/> reference pixels — the
        /// same derivation the hub's tabs use, so every plate on this screen batches together.</summary>
        private static Image ConfigureRounded(Image image, float radius)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / radius;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A non-interactive picture: aspect kept, no raycast, painted later.</summary>
        private static Image ConfigureGlyph(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>The close cross, drawn as two rotated bars so it needs no glyph asset — the same
        /// treatment the other cards give theirs. The hub hides it, but keeps the rect as a hit test.</summary>
        private void BuildCloseButton()
        {
            var closeObject = new GameObject("CloseButton", typeof(RectTransform));
            _closeButtonRect = (RectTransform)closeObject.transform;
            _closeButtonRect.SetParent(_cardRect, false);
            Centre(_closeButtonRect, new Vector2(ICON_BUTTON_SIZE, ICON_BUTTON_SIZE));
            _closeButtonRect.anchoredPosition = new Vector2(
                (_cardSize.x * 0.5f) - (ICON_BUTTON_SIZE * 0.5f) - (SIDE_INSET * 0.5f),
                (_cardSize.y * 0.5f) - (HEADER_INSET * 0.5f));

            _closeBarA = BuildCloseBar(45f);
            _closeBarB = BuildCloseBar(-45f);
        }

        private Image BuildCloseBar(float rotationDegrees)
        {
            var barObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            var barRect = (RectTransform)barObject.transform;
            barRect.SetParent(_closeButtonRect, false);
            Centre(barRect, new Vector2(ICON_BUTTON_SIZE * 0.62f, 8f));
            barRect.localRotation = Quaternion.Euler(0f, 0f, rotationDegrees);

            var barImage = barObject.GetComponent<Image>();
            barImage.color = Color.clear;
            barImage.raycastTarget = false;
            return barImage;
        }

        /// <summary>Anchored Y of an element <paramref name="height"/> tall whose top edge sits
        /// <paramref name="offsetFromTop"/> below the card's top edge.</summary>
        private float TopY(float offsetFromTop, float height)
            => (_cardSize.y * 0.5f) - offsetFromTop - (height * 0.5f);

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Every widget of one item card, so a repaint addresses the card rather than nine
        /// parallel arrays. A class rather than a struct: it is filled in piecemeal by the builder and
        /// read by reference on every repaint.</summary>
        private sealed class ItemWidgets
        {
            public RectTransform Rect;
            public Image Shadow;
            public Image Plate;
            public Image Band;
            public Image BandSquare;
            public Image Tile;
            public Image Glyph;
            public GameObject HeldBadge;
            public Image HeldRing;
            public Image HeldFill;
            public Text HeldCount;
            public Text Name;
            public Text Description;
            public Image ButtonPlate;
            public Image Coin;
            public Image LockGlyph;
            public Text Price;
            public Text WasPrice;
            public Image WasPriceBar;
            public Image SaleBadge;
            public Text SaleText;
        }

        /// <summary>Every widget of one coin bundle row, plus the SKU its button names.</summary>
        private sealed class BundleWidgets
        {
            public string Sku;
            public Image Shadow;
            public Image Plate;
            public Image Pile;
            public Text Amount;
            public Text Name;
            public Image ButtonPlate;
            public Text ButtonText;
        }
    }
}
