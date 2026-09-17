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
    /// The power-up shop: one row per <see cref="PowerUpKind"/> with what the player holds, what one
    /// costs, and a tap that buys it. Shown as the shop tab of <see cref="HubPanelView"/>, which is its
    /// only opener.
    /// <para>
    /// Holds no logic, like every other card here. It never writes a model: the balance and the nine
    /// counters are observed, so the rows repaint whether this screen or something else moved them —
    /// including <see cref="PowerUpInventoryView"/>'s earn-by-ad taps on the strip underneath. A tap on
    /// a row is one call to <see cref="CurrencySystem.TryPurchasePowerUp"/> and nothing more; whether
    /// it is refused, and why, is the System's answer and not this card's guess.
    /// </para>
    /// <para>
    /// Prices are quoted through <see cref="CurrencySystem.QuotePriceFor"/> rather than read out of the
    /// config directly, for the reason the conversion screen quotes through
    /// <see cref="CurrencySystem.QuoteCoinsFor"/>: the figure shown and the figure charged must come
    /// from the same arithmetic rather than two copies of it.
    /// </para>
    /// <para>
    /// That is also how a live promotion reaches this card: the quote it already asks for arrives
    /// discounted, so nothing here knows a campaign exists. Whether to <em>say</em> so — the dimmed
    /// standard price struck through above the sale one — is decided by comparing that quote against
    /// <see cref="PowerUpPriceConfig.GetPrice"/>, rather than by reading
    /// <see cref="PromotionConfig"/> directly: which campaign applies, whether two of them stack and
    /// when a window closes are all the System's answers, and a View holding its own copy of that logic
    /// could only ever disagree with the price it is drawing.
    /// </para>
    /// <para>
    /// A row has the same three states a strip slot does — locked, held, empty — and locked wins
    /// outright for the same reason: a kind behind its level gate is not an offer. It is dimmed and its
    /// tap does nothing but say so, because coins never open that gate (the System refuses it too, so
    /// this is the message rather than the enforcement).
    /// </para>
    /// <para>
    /// While it is open it is modal and swallows every tap, and it holds the run's clock through
    /// <see cref="TimerRunSystem.SetMenuPaused"/> — see the gate chain in
    /// <see cref="BoardInputView"/>, which is what keeps it mutually exclusive with the other overlays
    /// so that flag can never have two owners.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerUpShopView : MonoBehaviour
    {
        /// <summary>
        /// One row per <see cref="PowerUpKind"/>, in the same display order
        /// <see cref="PowerUpInventoryView"/> draws its strip in. Deliberately a second copy of that
        /// order rather than a shared one: the strip's is private to it, and a shop row and a HUD slot
        /// are free to diverge later without either having to ask the other's permission.
        /// </summary>
        private static readonly PowerUpKind[] RowKinds =
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

        /// <summary>Derived from <see cref="RowKinds"/> rather than written out, so the two can never
        /// disagree about how many rows there are.</summary>
        private static readonly int RowCount = RowKinds.Length;

        /// <summary>How many of a kind one tap buys. Fixed at one for now: the shop is a functional
        /// slice, and a quantity picker is a design pass this screen has not had yet.</summary>
        private const int PURCHASE_QUANTITY = 1;

        // Layout, in canvas reference pixels, matching the other cards so they all read as one family.
        private const float HEADER_Y = 470f;
        private const float BALANCE_Y = 445f;

        // The balance line is shared with the convert button (issue #219): balance on the left, the
        // way into the conversion screen on the right, so the coins and the way to get more sit
        // together and the nine rows below keep the height they have.
        private const float BALANCE_X = -190f;
        private const float CONVERT_BUTTON_X = 210f;
        private const float CONVERT_BUTTON_CORNER_RADIUS = 14f;
        private static readonly Vector2 ConvertButtonSize = new Vector2(360f, 64f);
        private const float ROWS_TOP_Y = 350f;
        private const float MESSAGE_Y = -462f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float SIDE_INSET = 48f;
        private const float HEADER_INSET = 84f;

        // The row, after the design (issue #207) and one round of on-device feedback: the design's
        // 40px tile and 14px gap read too small on the 880-wide card, so the tile, the glyph and the
        // type are scaled up and the gap tightened to keep nine rows on the card. The rows start where
        // the card's own header used to be — HubPanelView hides that header, so the space is free.
        private const float ROW_HEIGHT = 80f;
        private const float ROW_GAP = 10f;
        private const float ROW_PITCH = ROW_HEIGHT + ROW_GAP;
        private const float ROW_PADDING = 10f;
        private const float ROW_CORNER_RADIUS = 14f;
        private const float ICON_TILE_SIZE = 64f;
        private const float ICON_TILE_CORNER_RADIUS = 12f;
        private const float ICON_GLYPH_SIZE = 50f;
        private const float ICON_TEXT_GAP = 12f;

        /// <summary>How far a row's plate is tinted from the card towards the ink. The design's beige on
        /// off-white is a small step, not a second colour: a tint keeps it a theme-derived shade.</summary>
        private const float ROW_PLATE_TINT = 0.05f;

        /// <summary>Vertical offsets of the name and the description from the row's centre line, so the
        /// two stack as one label rather than two rows of text.</summary>
        private const float NAME_RISE = 15f;
        private const float DESCRIPTION_DROP = 16f;

        /// <summary>Alpha applied to the struck-through standard price on a discounted row. Well under
        /// the sale price's, so the eye lands on what the player would pay rather than on what they
        /// would have paid.</summary>
        private const float WAS_PRICE_ALPHA = 0.45f;

        /// <summary>Thickness of the line drawn through the standard price, in canvas reference pixels.
        /// A thin <see cref="Image"/> bar, the same primitive the close cross is built from, rather than
        /// a rich-text tag: this card draws with <see cref="Text"/>, which has no strikethrough.</summary>
        private const float STRIKETHROUGH_THICKNESS = 3f;

        /// <summary>How far the sale price drops from a row's centre line to make room for the standard
        /// price above it. Applied only on a discounted row — an undiscounted one keeps its single price
        /// centred, exactly as before.</summary>
        private const float DISCOUNTED_PRICE_DROP = 0.17f;

        /// <summary>Where the struck-through standard price sits, as a fraction of the row height above
        /// the centre line.</summary>
        private const float WAS_PRICE_RISE = 0.26f;

        /// <summary>Alpha applied to a row whose kind is still behind its level gate. Unlike
        /// <see cref="PowerUpInventoryView"/>, which hides a locked kind's slot entirely, this is a full
        /// catalog: every kind gets a row regardless of level, so "locked" has to be a visual state here
        /// rather than an absence.</summary>
        private const float LOCKED_ROW_ALPHA = 0.35f;

        /// <summary>Alpha applied to a row the player cannot currently afford. Above the locked alpha on
        /// purpose: "come back with more coins" is a live offer, where a locked row is not one at all.</summary>
        private const float UNAFFORDABLE_ROW_ALPHA = 0.6f;

        // Plain strings, not String Table keys, for the reason CoinConversionView states:
        // LocalizationKeys has no currency section yet, and adding keys with no translations behind
        // them would render the keys themselves. Tracked for a follow-up.
        private const string HEADER_TEXT = "POWER-UP SHOP";
        private const string COIN_BALANCE_PREFIX_TEXT = "Coins: ";
        private const string LOCKED_LEVEL_PREFIX = "Reach Lv";
        private const string PURCHASED_MESSAGE = "Bought!";
        private const string INSUFFICIENT_COINS_MESSAGE = "Not enough coins.";
        private const string LOCKED_MESSAGE = "Locked — level up to unlock this.";
        private const string OPENING_MESSAGE = "Tap a power-up to buy one.";

        /// <summary>Label of the button that opens <see cref="CoinConversionView"/>. Authored text like
        /// the header and the messages around it; the card has no string-table pass yet.</summary>
        private const string CONVERT_BUTTON_TEXT = "CONVERT SCORE";
        private const string COINS_SUFFIX_TEXT = " coins";

        /// <summary>The price column's width as a fraction of a row's width. Named because three things
        /// are placed in that column — the price, the standard price above it and the bar through that —
        /// and three copies of the same number would be three chances to drift. The column sits flush
        /// against the row's right padding, as the design's price does.</summary>
        private const float PRICE_COLUMN_WIDTH_FRACTION = 0.30f;

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1040f);
        [SerializeField] private int _headerFontSize = 52;
        [SerializeField] private int _balanceFontSize = 44;
        [SerializeField] private int _bodyFontSize = 32;
        [SerializeField] private int _wasPriceFontSize = 24;

        [Header("Rows")]
        [SerializeField] private int _rowNameFontSize = 32;
        [SerializeField] private int _rowDescriptionFontSize = 24;
        [SerializeField] private int _rowPriceFontSize = 30;

        [Tooltip("White-on-transparent glyphs, one per shop row in display order (Bomb, Row Clear, Column "
            + "Clear, Joker, Colour Cleanser, Rotate, Reroll, Double Score, Ghost Fit). Tinted at runtime.")]
        [SerializeField] private Sprite[] _rowIcons = new Sprite[RowCount];

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(48);

        private readonly RectTransform[] _rowRects = new RectTransform[RowCount];
        private readonly Image[] _rowPlates = new Image[RowCount];
        private readonly Image[] _iconTiles = new Image[RowCount];
        private readonly Image[] _iconGlyphs = new Image[RowCount];
        private readonly Text[] _nameTexts = new Text[RowCount];
        private readonly Text[] _descriptionTexts = new Text[RowCount];
        private readonly Text[] _priceTexts = new Text[RowCount];

        /// <summary>The standard price of a discounted row, and the bar drawn through it. Built for
        /// every row and shown on none of them by default: a campaign can start or end between two
        /// openings of this card, so the "was" figure has to be one repaint away rather than one
        /// instantiation away.</summary>
        private readonly Text[] _wasPriceTexts = new Text[RowCount];
        private readonly Image[] _strikethroughBars = new Image[RowCount];

        /// <summary>Held counts, mirrored from <see cref="PowerUpModel"/> so a repaint never has to
        /// reach back through the model. Written only by the count subscriptions.</summary>
        private readonly int[] _counts = new int[RowCount];

        private ProfileModel _profileModel;
        private PowerUpModel _powerUpModel;
        private LevelProgressionModel _levelProgressionModel;
        private CurrencySystem _currencySystem;
        private TimerRunSystem _timerRunSystem;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private CoinConversionView _coinConversionView;

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
        private Text _balanceText;
        private Text _messageText;
        private RectTransform _convertButtonRect;
        private Image _convertButtonPlate;
        private Text _convertButtonText;

        private ThemeDefinition _currentTheme;

        /// <summary>The player's progression frontier, mirrored from
        /// <see cref="LevelProgressionModel.CurrentLevelNumber"/> for the reason
        /// <see cref="PowerUpInventoryView"/> mirrors it: every row's locked state is derived from this
        /// one number, so it has a single source and cannot go stale.</summary>
        private int _currentLevelNumber;

        /// <summary>What the last tap did, drawn at the foot of the card. The whole of the card's
        /// feedback: a purchase and both refusals are three different sentences, and the System already
        /// says which one applies.</summary>
        private string _message = OPENING_MESSAGE;

        [Inject]
        public void Construct(
            ProfileModel profileModel,
            PowerUpModel powerUpModel,
            LevelProgressionModel levelProgressionModel,
            CurrencySystem currencySystem,
            TimerRunSystem timerRunSystem,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            PowerUpPriceConfig priceConfig,
            CoinConversionView coinConversionView)
        {
            _priceConfig = priceConfig;
            _coinConversionView = coinConversionView;
            _profileModel = profileModel;
            _powerUpModel = powerUpModel;
            _levelProgressionModel = levelProgressionModel;
            _currencySystem = currencySystem;
            _timerRunSystem = timerRunSystem;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
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
                || _currencySystem == null || _timerRunSystem == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null || _priceConfig == null
                || _coinConversionView == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpShopView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so the theme is known before any row is painted below.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

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

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>The card's own rect, current size included. Read by <see cref="HubPanelView"/> to
        /// sit its tab bar flush against whichever card is open, rather than at a fixed offset that
        /// would gap open against a shorter card.</summary>
        internal RectTransform CardRect => _cardRect;

        /// <summary>This card's own close cross. Hidden by <see cref="HubPanelView"/> once opened
        /// there, since the hub's own header now carries the one close button for whichever tab is
        /// open.</summary>
        internal RectTransform CloseButtonRect => _closeButtonRect;

        /// <summary>This card's own title, which just repeats the tab it belongs to. Hidden by
        /// <see cref="HubPanelView"/> once opened there, since the hub's own header now says the same
        /// thing.</summary>
        internal Text HeaderTitleText => _headerText;

        /// <summary>Shows the card and holds the run's clock. Called by <see cref="BoardInputView"/>
        /// when the HUD icon is tapped.</summary>
        internal void Open()
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            _message = OPENING_MESSAGE;
            Refresh();
            _panel.SetActive(true);
            transform.SetAsLastSibling();
            _timerRunSystem.SetMenuPaused(true);
        }

        /// <summary>
        /// Routes a tap while the panel is open. The close cross wins, then the nine rows; the card then
        /// swallows anything else, so a tap between rows is a deliberate no-op rather than a dismissal.
        /// Only a tap on the scrim outside the card closes.
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

            // Opens over this card rather than replacing it: the hub stays open underneath, and the
            // conversion card's own scrim tap brings the player back here.
            if (RectTransformUtility.RectangleContainsScreenPoint(_convertButtonRect, screenPosition, eventCamera))
            {
                _coinConversionView.Open();
                return;
            }

            for (int rowIndex = 0; rowIndex < RowCount; rowIndex++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(
                    _rowRects[rowIndex], screenPosition, eventCamera))
                {
                    Buy(rowIndex);
                    return;
                }
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
        /// Asks the System to buy one of this row's kind and reports what it said. The whole row is the
        /// button: the price and the BUY plate are two parts of one offer, and a tap that lands between
        /// them is still a tap on the offer.
        /// <para>
        /// Every outcome — including both refusals — is routed through the System rather than pre-empted
        /// here. A locked row and an unaffordable one are already drawn as such, so this cannot normally
        /// happen; when it does, the refusal the player is shown is the one the System actually gave.
        /// </para>
        /// </summary>
        private void Buy(int rowIndex)
        {
            PowerUpPurchaseResult result = _currencySystem.TryPurchasePowerUp(
                RowKinds[rowIndex], PURCHASE_QUANTITY);

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
                    // PURCHASE_QUANTITY is a positive constant. Folded in rather than given its own
                    // sentence: there is no wording that would help a player with a bug in this View.
                    _message = INSUFFICIENT_COINS_MESSAGE;
                    break;
            }

            // The count and balance subscriptions repaint on a success; a refusal moves neither, so the
            // message has to be painted here either way.
            Refresh();
        }

        /// <summary>
        /// Binds one inventory counter to the row that draws <paramref name="kind"/>. The row index is
        /// resolved from <see cref="RowKinds"/> at subscribe time rather than written at the call site,
        /// so a row can be added or reordered without a counter repainting its neighbour.
        /// </summary>
        private void WatchCount(ReactiveProperty<int> counter, PowerUpKind kind)
        {
            int rowIndex = RowIndexOf(kind);
            if (rowIndex < 0)
            {
                return;
            }

            counter.Subscribe(count =>
            {
                _counts[rowIndex] = count;
                Refresh();
            }).AddTo(_disposables);
        }

        /// <summary>The row drawing <paramref name="kind"/>, or -1 when no row does.</summary>
        private static int RowIndexOf(PowerUpKind kind)
        {
            for (int rowIndex = 0; rowIndex < RowCount; rowIndex++)
            {
                if (RowKinds[rowIndex] == kind)
                {
                    return rowIndex;
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

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;
            _headerText.color = theme.Ink;
            _balanceText.color = theme.Accent;
            _messageText.color = theme.SoftInk;
            _convertButtonPlate.color = theme.Accent;
            _convertButtonText.color = theme.CardBackground;
            _closeBarA.color = theme.Ink;
            _closeBarB.color = theme.Ink;

            Refresh();
        }

        /// <summary>
        /// Repaints every row and both figures from the models. Cheap enough to be the only repaint
        /// path: it runs on an open, a purchase, a grant, a level change and a theme switch — never per
        /// frame.
        /// </summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null || _currencySystem == null)
            {
                return;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(COIN_BALANCE_PREFIX_TEXT);
            _stringBuilder.Append(_profileModel.CoinBalance.Value);
            _balanceText.text = _stringBuilder.ToString();

            _messageText.text = _message;

            for (int rowIndex = 0; rowIndex < RowCount; rowIndex++)
            {
                RefreshRow(rowIndex);
            }
        }

        /// <summary>
        /// Repaints one row. Three mutually exclusive states in priority order, mirroring the strip's:
        /// locked wins outright, then affordable, then "not enough coins" — the last two differing only
        /// in alpha, because an unaffordable row is still a real offer and a locked one is not.
        /// <para>
        /// A discount is a fourth, orthogonal thing rather than a fifth state: it decorates whichever of
        /// the three the row is already in, because a power-up on sale can still be unaffordable and one
        /// behind its gate is not on offer at any price. A locked row therefore shows no "was" figure at
        /// all, for the reason it shows no price — there is nothing there to discount.
        /// </para>
        /// </summary>
        private void RefreshRow(int rowIndex)
        {
            PowerUpKind kind = RowKinds[rowIndex];
            bool isLocked = !PowerUpUnlockLevels.IsUnlockedAt(kind, _currentLevelNumber);
            long price = _currencySystem.QuotePriceFor(kind, PURCHASE_QUANTITY);
            bool isAffordable = !isLocked && price <= _profileModel.CoinBalance.Value;

            // The System's quote coming in under the standard price is the only evidence this card
            // needs, and wants, that a campaign is live. PURCHASE_QUANTITY is one, so the two figures
            // are directly comparable without re-deriving a line total.
            int standardPrice = _priceConfig.GetPrice(kind);
            bool isDiscounted = !isLocked && price < standardPrice;

            float alpha = isLocked
                ? LOCKED_ROW_ALPHA
                : (isAffordable ? 1f : UNAFFORDABLE_ROW_ALPHA);

            // Every colour is a theme field or a step between two of them, so a theme switch restyles
            // the whole row with nothing hard-coded (issue #207, AC6).
            _rowPlates[rowIndex].color = WithAlpha(
                Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, ROW_PLATE_TINT), alpha);
            _iconTiles[rowIndex].color = WithAlpha(_currentTheme.CardBackground, alpha);
            _iconGlyphs[rowIndex].color = WithAlpha(_currentTheme.Ink, alpha);
            _nameTexts[rowIndex].color = WithAlpha(_currentTheme.Ink, alpha);
            _descriptionTexts[rowIndex].color = WithAlpha(_currentTheme.SoftInk, alpha);

            // The price is the row's one accent, as in the design; a locked row shows a requirement
            // there instead, in the soft ink a non-offer deserves.
            _priceTexts[rowIndex].color = WithAlpha(isLocked ? _currentTheme.SoftInk : _currentTheme.Accent, alpha);

            _stringBuilder.Clear();
            _stringBuilder.Append(_localizationSystem.Translate(NameKeyOf(kind)));
            _stringBuilder.Append("  x");
            _stringBuilder.Append(_counts[rowIndex]);
            _nameTexts[rowIndex].text = _stringBuilder.ToString();

            _descriptionTexts[rowIndex].text = _localizationSystem.Translate(DescriptionKeyOf(kind));

            _stringBuilder.Clear();
            if (isLocked)
            {
                // The requirement, not a price: a locked row has nothing to sell, so quoting a figure
                // next to a dead BUY plate would only invite the tap it is going to refuse.
                _stringBuilder.Append(LOCKED_LEVEL_PREFIX);
                _stringBuilder.Append(PowerUpUnlockLevels.LevelFor(kind));
            }
            else
            {
                _stringBuilder.Append(price);
                _stringBuilder.Append(COINS_SUFFIX_TEXT);
            }

            _priceTexts[rowIndex].text = _stringBuilder.ToString();

            // Dropped only while a "was" figure sits above it, so an undiscounted row keeps the single
            // centred price this card has always drawn.
            ((RectTransform)_priceTexts[rowIndex].transform).anchoredPosition = new Vector2(
                PriceColumnX(), isDiscounted ? -ROW_HEIGHT * DISCOUNTED_PRICE_DROP : 0f);

            RefreshWasPrice(rowIndex, standardPrice, isDiscounted, alpha);
        }

        /// <summary>
        /// Paints — or hides — the struck-through standard price above a discounted row's sale price.
        /// <para>
        /// Hidden by emptying the text and disabling the bar rather than by deactivating either
        /// GameObject: both are built once in <see cref="BuildPanel"/> and toggled on every repaint, and
        /// a <see cref="GameObject.SetActive(bool)"/> pair would dirty the canvas layout for no gain
        /// over a component that draws nothing.
        /// </para>
        /// <para>
        /// The bar is sized from <see cref="Text.preferredWidth"/> rather than from the rect it lives in,
        /// because the text is right-aligned inside a fixed-width column: a bar the width of the column
        /// would run out past "50 coins" into empty space. Read after the text is assigned, which is the
        /// only order in which it reports the new string's width.
        /// </para>
        /// </summary>
        private void RefreshWasPrice(int rowIndex, int standardPrice, bool isDiscounted, float alpha)
        {
            Text wasPriceText = _wasPriceTexts[rowIndex];
            Image strikethroughBar = _strikethroughBars[rowIndex];

            if (!isDiscounted)
            {
                wasPriceText.text = string.Empty;
                strikethroughBar.enabled = false;
                return;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(standardPrice);
            _stringBuilder.Append(COINS_SUFFIX_TEXT);
            wasPriceText.text = _stringBuilder.ToString();
            wasPriceText.color = WithAlpha(_currentTheme.SoftInk, alpha * WAS_PRICE_ALPHA);

            strikethroughBar.enabled = true;
            strikethroughBar.color = WithAlpha(_currentTheme.SoftInk, alpha * WAS_PRICE_ALPHA);
            ((RectTransform)strikethroughBar.transform).sizeDelta = new Vector2(
                wasPriceText.preferredWidth, STRIKETHROUGH_THICKNESS);
        }

        /// <summary>String-table key of a kind's display name. The text itself lives in the tables, one
        /// row per language, so this is only the mapping from enum to key.</summary>
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
            // the theme is known, and the theme subscription in Start paints all of it.
            _headerText = UiTextFactory.Create(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_headerText.transform).anchoredPosition = new Vector2(0f, HEADER_Y);
            _headerText.text = HEADER_TEXT;

            _balanceText = UiTextFactory.Create(
                _cardRect, "Balance", _balanceFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_balanceText.transform).anchoredPosition = new Vector2(BALANCE_X, BALANCE_Y);

            BuildConvertButton(new Vector2(CONVERT_BUTTON_X, BALANCE_Y));

            for (int rowIndex = 0; rowIndex < RowCount; rowIndex++)
            {
                BuildRow(rowIndex, ROWS_TOP_Y - (rowIndex * ROW_PITCH));
            }

            _messageText = UiTextFactory.Create(
                _cardRect, "Message", _bodyFontSize, FontStyle.Normal, Color.clear);
            var messageRect = (RectTransform)_messageText.transform;
            messageRect.sizeDelta = new Vector2(_cardSize.x - (SIDE_INSET * 2f), ROW_HEIGHT);
            messageRect.anchoredPosition = new Vector2(0f, MESSAGE_Y);

            BuildCloseButton();

            _panel = panelObject;
        }

        /// <summary>
        /// The accent pill that opens the conversion screen. Its own rect is the hit area, as a row's
        /// is. Built transparent like everything else here and painted by the theme subscription.
        /// </summary>
        private void BuildConvertButton(Vector2 anchoredPosition)
        {
            var buttonObject = new GameObject("ConvertButton", typeof(RectTransform), typeof(Image));
            _convertButtonRect = (RectTransform)buttonObject.transform;
            _convertButtonRect.SetParent(_cardRect, false);
            _convertButtonRect.anchorMin = new Vector2(0.5f, 0.5f);
            _convertButtonRect.anchorMax = new Vector2(0.5f, 0.5f);
            _convertButtonRect.pivot = new Vector2(0.5f, 0.5f);
            _convertButtonRect.sizeDelta = ConvertButtonSize;
            _convertButtonRect.anchoredPosition = anchoredPosition;

            _convertButtonPlate = ConfigureRounded(
                buttonObject.GetComponent<Image>(), CONVERT_BUTTON_CORNER_RADIUS);

            _convertButtonText = UiTextFactory.Create(
                _convertButtonRect, "Label", _bodyFontSize, FontStyle.Bold, Color.clear);
            _convertButtonText.text = CONVERT_BUTTON_TEXT;
        }

        /// <summary>
        /// One shop row, left to right as the design draws it: a rounded icon tile, the name with the
        /// description under it, and the price on the right — all on the row's own tinted plate. The
        /// row's own rect is the hit area, which is what makes the whole offer tappable; the BUY plate
        /// the old layout ended in is gone, since the design has none and the tap never needed it.
        /// </summary>
        private void BuildRow(int rowIndex, float y)
        {
            float rowWidth = RowWidth;

            var rowObject = new GameObject($"ShopRow_{RowKinds[rowIndex]}", typeof(RectTransform));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(_cardRect, false);
            Centre(rowRect, new Vector2(rowWidth, ROW_HEIGHT));
            rowRect.anchoredPosition = new Vector2(0f, y);
            _rowRects[rowIndex] = rowRect;

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(rowRect, false);
            Centre(plateRect, new Vector2(rowWidth, ROW_HEIGHT));
            _rowPlates[rowIndex] = ConfigureRounded(plateObject.GetComponent<Image>(), ROW_CORNER_RADIUS);

            float leftEdge = -rowWidth * 0.5f + ROW_PADDING;

            var tileObject = new GameObject("IconTile", typeof(RectTransform), typeof(Image));
            var tileRect = (RectTransform)tileObject.transform;
            tileRect.SetParent(rowRect, false);
            Centre(tileRect, new Vector2(ICON_TILE_SIZE, ICON_TILE_SIZE));
            tileRect.anchoredPosition = new Vector2(leftEdge + (ICON_TILE_SIZE * 0.5f), 0f);
            _iconTiles[rowIndex] = ConfigureRounded(tileObject.GetComponent<Image>(), ICON_TILE_CORNER_RADIUS);

            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(tileRect, false);
            Centre(glyphRect, new Vector2(ICON_GLYPH_SIZE, ICON_GLYPH_SIZE));
            var glyph = glyphObject.GetComponent<Image>();
            glyph.sprite = IconFor(rowIndex);
            glyph.type = Image.Type.Simple;
            glyph.preserveAspect = true;
            glyph.color = Color.clear;
            glyph.raycastTarget = false;
            _iconGlyphs[rowIndex] = glyph;

            float priceWidth = rowWidth * PRICE_COLUMN_WIDTH_FRACTION;
            float textLeft = leftEdge + ICON_TILE_SIZE + ICON_TEXT_GAP;
            float textWidth = rowWidth - ROW_PADDING - priceWidth - (textLeft + (rowWidth * 0.5f));
            float textCentreX = textLeft + (textWidth * 0.5f);

            Text nameText = UiTextFactory.Create(
                rowRect, "Name", _rowNameFontSize, FontStyle.Bold, Color.clear);
            nameText.alignment = TextAnchor.MiddleLeft;
            var nameRect = (RectTransform)nameText.transform;
            nameRect.sizeDelta = new Vector2(textWidth, ROW_HEIGHT * 0.5f);
            nameRect.anchoredPosition = new Vector2(textCentreX, NAME_RISE);
            _nameTexts[rowIndex] = nameText;

            Text descriptionText = UiTextFactory.Create(
                rowRect, "Description", _rowDescriptionFontSize, FontStyle.Normal, Color.clear);
            descriptionText.alignment = TextAnchor.MiddleLeft;
            var descriptionRect = (RectTransform)descriptionText.transform;
            descriptionRect.sizeDelta = new Vector2(textWidth, ROW_HEIGHT * 0.5f);
            descriptionRect.anchoredPosition = new Vector2(textCentreX, -DESCRIPTION_DROP);
            _descriptionTexts[rowIndex] = descriptionText;

            Text priceText = UiTextFactory.Create(
                rowRect, "Price", _rowPriceFontSize, FontStyle.Bold, Color.clear);
            priceText.alignment = TextAnchor.MiddleRight;
            var priceRect = (RectTransform)priceText.transform;
            priceRect.sizeDelta = new Vector2(priceWidth, ROW_HEIGHT);
            priceRect.anchoredPosition = new Vector2(PriceColumnX(), 0f);
            _priceTexts[rowIndex] = priceText;

            BuildWasPrice(rowIndex, rowRect, rowWidth);
        }

        /// <summary>The row's icon, authored in <see cref="RowKinds"/> order.</summary>
        private Sprite IconFor(int rowIndex)
        {
            Sprite icon = _rowIcons != null && rowIndex < _rowIcons.Length ? _rowIcons[rowIndex] : null;
            if (icon == null)
            {
                Debug.LogError($"{nameof(PowerUpShopView)} has no icon sprite assigned for {RowKinds[rowIndex]}.", this);
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

        /// <summary>
        /// The standard price of a discounted row and the line drawn through it, stacked above the sale
        /// price in the same column and at a smaller size. Built for every row and shown on none until a
        /// repaint says otherwise.
        /// <para>
        /// Stacked rather than set beside the sale price because the row is already three columns wide —
        /// name, price, BUY plate — and squeezing a fourth in would have narrowed the name column past
        /// "Colour Cleanser". The vertical pair is also the shape a shopper reads without a legend.
        /// </para>
        /// <para>
        /// The bar is anchored to the column's right edge, matching the right-aligned text it crosses
        /// out, so only its width has to be recomputed when the figure changes.
        /// </para>
        /// </summary>
        private void BuildWasPrice(int rowIndex, RectTransform rowRect, float rowWidth)
        {
            Text wasPriceText = UiTextFactory.Create(
                rowRect, "WasPrice", _wasPriceFontSize, FontStyle.Normal, Color.clear);
            wasPriceText.alignment = TextAnchor.MiddleRight;
            var wasPriceRect = (RectTransform)wasPriceText.transform;
            wasPriceRect.sizeDelta = new Vector2(
                rowWidth * PRICE_COLUMN_WIDTH_FRACTION, ROW_HEIGHT * 0.5f);
            wasPriceRect.anchoredPosition = new Vector2(
                PriceColumnX(), ROW_HEIGHT * WAS_PRICE_RISE);
            _wasPriceTexts[rowIndex] = wasPriceText;

            var barObject = new GameObject("Strikethrough", typeof(RectTransform), typeof(Image));
            var barRect = (RectTransform)barObject.transform;
            barRect.SetParent(wasPriceRect, false);
            barRect.anchorMin = new Vector2(1f, 0.5f);
            barRect.anchorMax = new Vector2(1f, 0.5f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.sizeDelta = new Vector2(0f, STRIKETHROUGH_THICKNESS);
            barRect.anchoredPosition = Vector2.zero;

            var barImage = barObject.GetComponent<Image>();
            barImage.color = Color.clear;
            barImage.raycastTarget = false;
            barImage.enabled = false;
            _strikethroughBars[rowIndex] = barImage;
        }

        /// <summary>A row's width inside the card's side insets. Derived rather than stored so it cannot
        /// go stale against <see cref="_cardSize"/>.</summary>
        private float RowWidth => _cardSize.x - (SIDE_INSET * 2f);

        /// <summary>The price column's centre, in a row's local space: flush against the right padding.</summary>
        private float PriceColumnX()
            => (RowWidth * 0.5f) - ROW_PADDING - (RowWidth * PRICE_COLUMN_WIDTH_FRACTION * 0.5f);

        /// <summary>The close cross, drawn as two rotated bars so it needs no glyph asset — the same
        /// treatment the other cards give theirs.</summary>
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

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }
    }
}
