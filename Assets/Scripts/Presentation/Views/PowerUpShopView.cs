using System.Text;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The power-up shop, drawn as a carnival stall (issue #234): a striped awning over a cream card,
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

        /// <summary>The two sub-tabs that draw something in the card. The Coins tab is not one of them:
        /// it opens <see cref="CoinConversionView"/> over this card instead, since that card already
        /// holds every way to earn coins, and the shop returns to the tab it was on underneath.</summary>
        private enum ShopTab
        {
            PowerUps,
            Deals,
        }

        // Layout, in canvas reference pixels, on the 880-wide card the other hub cards share. Offsets
        // are measured down from the card's top edge; TopY turns them into anchored positions.
        private const float SIDE_INSET = 24f;
        private const float AWNING_TOP = 4f;
        private const float AWNING_HEIGHT = 112f;
        private const float BALANCE_TOP = 128f;
        private const float BALANCE_HEIGHT = 80f;
        private const float BALANCE_CORNER_RADIUS = 18f;
        private const float BALANCE_COIN_SIZE = 56f;
        private const float BALANCE_PADDING = 14f;
        private const float EARN_BUTTON_WIDTH = 300f;
        private const float EARN_BUTTON_HEIGHT = 60f;
        private const float TABS_TOP = 224f;
        private const float TAB_HEIGHT = 72f;
        private const float TAB_GAP = 12f;
        private const int TAB_COUNT = 3;
        private const float VIEWPORT_TOP = 312f;
        private const float VIEWPORT_BOTTOM_INSET = 24f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float HEADER_INSET = 84f;

        // The grid: two columns of item cards, scrolled vertically.
        private const int GRID_COLUMNS = 2;
        private const float GRID_GAP = 16f;
        private const float GRID_TOP_PADDING = 8f;
        private const float GRID_BOTTOM_PADDING = 96f;
        private const float ITEM_HEIGHT = 384f;
        private const float ITEM_CORNER_RADIUS = 22f;
        private const float ITEM_SHADOW_DROP = 8f;
        private const float ITEM_PADDING = 16f;
        private const float ITEM_INNER_GAP = 12f;
        private const float BAND_HEIGHT = 46f;
        private const float TILE_SIZE = 150f;
        private const float GLYPH_SIZE = 88f;

        /// <summary>The tile sprite's glossy face sits above a darker lip, so the glyph is lifted off
        /// the tile's geometric centre to sit on the face.</summary>
        private const float GLYPH_RISE = 8f;
        private const float DESCRIPTION_HEIGHT = 60f;
        private const float BUY_BUTTON_HEIGHT = 72f;
        private const float BUY_BUTTON_CORNER_RADIUS = 16f;
        private const float HELD_BADGE_SIZE = 44f;
        private const float HELD_BADGE_BORDER = 4f;
        private const float SALE_BADGE_WIDTH = 72f;
        private const float SALE_BADGE_HEIGHT = 30f;
        private const float SALE_BADGE_CORNER_RADIUS = 12f;
        private const float COIN_GLYPH_SIZE = 36f;
        private const float LOCK_GLYPH_SIZE = 32f;
        private const float STRIKETHROUGH_THICKNESS = 3f;

        /// <summary>
        /// Where the coin and the figure sit inside the price button, in the button's local space. Two
        /// layouts: centred when the price stands alone, shifted right when a struck-through standard
        /// price sits to its left.
        /// </summary>
        private const float PRICE_TEXT_WIDTH = 150f;
        private const float COIN_X_PLAIN = -44f;
        private const float PRICE_X_PLAIN = 60f;
        private const float WAS_PRICE_WIDTH = 110f;
        private const float WAS_PRICE_X = -106f;
        private const float COIN_X_DISCOUNTED = -20f;
        private const float PRICE_X_DISCOUNTED = 84f;

        private const float TOAST_HEIGHT = 56f;
        private const float TOAST_BOTTOM = 44f;
        private const float TOAST_PADDING = 48f;
        private const float TOAST_CORNER_RADIUS = 28f;

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

        // Plain strings, not String Table keys, for the reason CoinConversionView states: LocalizationKeys
        // has no currency section yet, and adding keys with no translations behind them would render
        // the keys themselves. Tracked for a follow-up.
        private const string HEADER_TEXT = "POWER-UP SHOP";
        private const string TAB_POWER_UPS_TEXT = "POWER-UPS";
        private const string TAB_COINS_TEXT = "COINS";
        private const string TAB_DEALS_TEXT = "DEALS";
        private const string EARN_BUTTON_TEXT = "+ GET COINS";
        private const string DEALS_PLACEHOLDER_TEXT = "Deals are coming soon.";
        private const string LOCKED_LEVEL_PREFIX = "Lv ";
        private const string HELD_COUNT_PREFIX = "x";
        private const string PURCHASED_MESSAGE = "Bought!";
        private const string INSUFFICIENT_COINS_MESSAGE = "Not enough coins.";
        private const string LOCKED_MESSAGE = "Level up to unlock this.";

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1040f);
        [SerializeField] private int _headerFontSize = 52;
        [SerializeField] private int _balanceFontSize = 40;
        [SerializeField] private int _tabFontSize = 26;
        [SerializeField] private int _earnFontSize = 24;
        [SerializeField] private int _itemNameFontSize = 26;
        [SerializeField] private int _itemDescriptionFontSize = 22;
        [SerializeField] private int _itemPriceFontSize = 32;
        [SerializeField] private int _wasPriceFontSize = 20;
        [SerializeField] private int _badgeFontSize = 20;
        [SerializeField] private int _toastFontSize = 26;
        [SerializeField] private int _placeholderFontSize = 30;

        [Header("Art")]
        [Tooltip("The chunky display face for names, prices and tab labels. Falls back to the built-in "
            + "runtime font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("White 9-sliced glossy button with a darker bottom lip. Tinted at runtime.")]
        [SerializeField] private Sprite _buttonSprite;

        [Tooltip("White glossy rounded tile behind each glyph. Tinted at runtime per kind.")]
        [SerializeField] private Sprite _tileSprite;

        [Tooltip("The striped awning drawn across the top of the card.")]
        [SerializeField] private Sprite _awningSprite;

        [Tooltip("White-on-transparent padlock glyph for a locked card's button.")]
        [SerializeField] private Sprite _lockSprite;

        [Tooltip("The coin drawn beside every price and the balance.")]
        [SerializeField] private Sprite _coinSprite;

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
        private LevelProgressionModel _levelProgressionModel;
        private CurrencySystem _currencySystem;
        private TimerRunSystem _timerRunSystem;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private CoinConversionView _coinConversionView;
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

        private Image _awningImage;
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

        private CanvasGroup _contentGroup;
        private GameObject _viewportObject;
        private ScrollRect _scrollRect;
        private GameObject _dealsPlaceholder;
        private Image _dealsPlate;
        private Text _dealsText;

        private RectTransform _toastRect;
        private Image _toastPlate;
        private Text _toastText;

        private ShopTab _activeTab = ShopTab.PowerUps;
        private int _currentLevelNumber = 1;
        private string _message = string.Empty;

        /// <summary>Whether the conversion card was open at the last poll — see <see cref="Update"/>.</summary>
        private bool _wasConversionOpen;

        [Inject]
        public void Construct(
            ProfileModel profileModel,
            PowerUpModel powerUpModel,
            LevelProgressionModel levelProgressionModel,
            CurrencySystem currencySystem,
            TimerRunSystem timerRunSystem,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            PowerUpPriceConfig priceConfig,
            ShopPaletteConfig palette,
            CoinConversionView coinConversionView)
        {
            _profileModel = profileModel;
            _powerUpModel = powerUpModel;
            _levelProgressionModel = levelProgressionModel;
            _currencySystem = currencySystem;
            _timerRunSystem = timerRunSystem;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _priceConfig = priceConfig;
            _palette = palette;
            _coinConversionView = coinConversionView;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_profileModel == null || _powerUpModel == null || _levelProgressionModel == null
                || _currencySystem == null || _timerRunSystem == null || _localizationModel == null
                || _localizationSystem == null || _priceConfig == null || _palette == null
                || _coinConversionView == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpShopView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

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

        /// <summary>
        /// Keeps the card's EventSystem targets out of reach while <see cref="CoinConversionView"/> is
        /// showing over it. That card is modal only in <see cref="BoardInputView"/>'s manual routing;
        /// its scrim has no raycast target, so without this a tap on its plate would fall through to a
        /// price button underneath and buy something. A flag flip on change, never per frame.
        /// </summary>
        private void Update()
        {
            if (!IsOpen || _coinConversionView == null)
            {
                return;
            }

            bool isConversionOpen = _coinConversionView.IsOpen;
            if (isConversionOpen == _wasConversionOpen)
            {
                return;
            }

            _wasConversionOpen = isConversionOpen;
            _contentGroup.blocksRaycasts = !isConversionOpen;
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

        internal void Open()
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            _message = string.Empty;
            _wasConversionOpen = false;
            _contentGroup.blocksRaycasts = true;
            SelectTab(ShopTab.PowerUps);
            _scrollRect.verticalNormalizedPosition = 1f;
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
                    _message = PURCHASED_MESSAGE;
                    break;
                case PowerUpPurchaseResult.Locked:
                    _message = LOCKED_MESSAGE;
                    break;
                default:
                    // InsufficientCoins, and InvalidQuantity — which this card cannot produce, since
                    // PURCHASE_QUANTITY is a positive constant.
                    _message = INSUFFICIENT_COINS_MESSAGE;
                    break;
            }

            // The count and balance subscriptions repaint on a success; a refusal moves neither, so the
            // toast has to be painted here either way.
            Refresh();
        }

        /// <summary>The Coins tab and the balance strip's earn button both land here: the conversion
        /// card is where score, ads and bundles all turn into coins (issue #219), and it opens over
        /// this card rather than replacing it.</summary>
        private void OpenCoins() => _coinConversionView.Open();

        private void SelectTab(ShopTab tab)
        {
            _activeTab = tab;
            bool showItems = tab == ShopTab.PowerUps;
            _viewportObject.SetActive(showItems);
            _dealsPlaceholder.SetActive(!showItems);

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

            RefreshToast();
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
                _stringBuilder.Append(LOCKED_LEVEL_PREFIX);
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
        /// awning, the balance strip, the tabs, the placeholder and the toast. Once, from the palette.</summary>
        private void PaintChrome()
        {
            _cardImage.color = _palette.CardFace;
            _cardShadowImage.color = _palette.CardShadow;
            _headerText.color = _palette.DarkPlate;
            _closeBarA.color = _palette.DarkPlate;
            _closeBarB.color = _palette.DarkPlate;

            _awningImage.color = Color.white;
            _balancePlate.color = _palette.DarkPlate;
            _balanceCoin.color = Color.white;
            _balanceText.color = _palette.CoinYellow;
            _earnButtonPlate.color = _palette.EarnButton;
            _earnButtonText.color = _palette.BuyButtonText;

            _dealsPlate.color = _palette.ItemPlate;
            _dealsText.color = _palette.ItemDescription;

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

        /// <summary>The active tab in its full colour, the others multiplied down. The Coins tab is
        /// never active — it is a launcher — so it is always drawn lit, as a button is.</summary>
        private void PaintTabs()
        {
            bool powerUpsActive = _activeTab == ShopTab.PowerUps;
            _tabPowerUpsPlate.color = powerUpsActive
                ? _palette.TabPowerUp
                : _palette.TabPowerUp * _palette.InactiveTabTint;
            _tabPowerUpsText.color = _palette.TabPowerUpText;

            _tabCoinsPlate.color = _palette.TabCoin;
            _tabCoinsText.color = _palette.TabText;

            _tabDealsPlate.color = powerUpsActive
                ? _palette.TabPromotion * _palette.InactiveTabTint
                : _palette.TabPromotion;
            _tabDealsText.color = _palette.TabText;
        }

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
            _headerText.text = HEADER_TEXT;

            // The tabs and the grid share one CanvasGroup so a single flag can take every EventSystem
            // target on the card out of reach while the conversion card sits over it.
            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(CanvasGroup));
            var contentRect = (RectTransform)contentObject.transform;
            contentRect.SetParent(_cardRect, false);
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            _contentGroup = contentObject.GetComponent<CanvasGroup>();

            BuildAwning();
            BuildBalanceStrip(contentRect);
            BuildTabs(contentRect);
            BuildViewport(contentRect);
            BuildDealsPlaceholder(contentRect);
            BuildToast();
            BuildCloseButton();

            _panel = panelObject;
        }

        private void BuildAwning()
        {
            var awningObject = new GameObject("Awning", typeof(RectTransform), typeof(Image));
            var awningRect = (RectTransform)awningObject.transform;
            awningRect.SetParent(_cardRect, false);
            Centre(awningRect, new Vector2(_cardSize.x - (SIDE_INSET * 2f) + 8f, AWNING_HEIGHT));
            awningRect.anchoredPosition = new Vector2(0f, TopY(AWNING_TOP, AWNING_HEIGHT));

            _awningImage = awningObject.GetComponent<Image>();
            _awningImage.sprite = _awningSprite;
            _awningImage.type = Image.Type.Simple;
            _awningImage.preserveAspect = false;
            _awningImage.color = Color.clear;
            _awningImage.raycastTarget = false;
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
            _earnButtonText.text = EARN_BUTTON_TEXT;
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
            _tabPowerUpsText.text = TAB_POWER_UPS_TEXT;

            RectTransform coinsRect = BuildChunkyButton(
                parent, "TabCoins", size, out _tabCoinsPlate, OpenCoins);
            coinsRect.anchoredPosition = new Vector2(0f, y);
            _tabCoinsText = UiTextFactory.Create(
                coinsRect, "Label", _tabFontSize, FontStyle.Bold, Color.clear, _displayFont);
            _tabCoinsText.text = TAB_COINS_TEXT;

            RectTransform dealsRect = BuildChunkyButton(
                parent, "TabDeals", size, out _tabDealsPlate, () => SelectTab(ShopTab.Deals));
            dealsRect.anchoredPosition = new Vector2(pitch, y);
            _tabDealsText = UiTextFactory.Create(
                dealsRect, "Label", _tabFontSize, FontStyle.Bold, Color.clear, _displayFont);
            _tabDealsText.text = TAB_DEALS_TEXT;
        }

        /// <summary>
        /// The scrolling grid: a clipped viewport whose invisible backdrop is the ScrollRect's raycast
        /// target — a drag has to land on a graphic to reach the ScrollRect at all — and a content rect
        /// sized exactly to the rows the nine cards need, pinned to the viewport's top.
        /// </summary>
        private void BuildViewport(RectTransform parent)
        {
            float width = _cardSize.x - (SIDE_INSET * 2f);
            float height = _cardSize.y - VIEWPORT_TOP - VIEWPORT_BOTTOM_INSET;

            _viewportObject = new GameObject(
                "ItemsViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            var viewportRect = (RectTransform)_viewportObject.transform;
            viewportRect.SetParent(parent, false);
            Centre(viewportRect, new Vector2(width, height));
            viewportRect.anchoredPosition = new Vector2(0f, TopY(VIEWPORT_TOP, height));

            var backdrop = _viewportObject.GetComponent<Image>();
            backdrop.color = Color.clear;
            backdrop.raycastTarget = true;

            int rowCount = (ItemCount + GRID_COLUMNS - 1) / GRID_COLUMNS;
            float contentHeight = GRID_TOP_PADDING + (rowCount * ITEM_HEIGHT)
                + ((rowCount - 1) * GRID_GAP) + GRID_BOTTOM_PADDING;

            var contentObject = new GameObject("ItemsContent", typeof(RectTransform));
            var contentRect = (RectTransform)contentObject.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0.5f, 1f);
            contentRect.anchorMax = new Vector2(0.5f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(width, contentHeight);
            contentRect.anchoredPosition = Vector2.zero;

            _scrollRect = _viewportObject.GetComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.viewport = viewportRect;
            _scrollRect.content = contentRect;

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
            item.Name.resizeTextMinSize = 16;
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
            _dealsText.text = DEALS_PLACEHOLDER_TEXT;

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
    }
}
