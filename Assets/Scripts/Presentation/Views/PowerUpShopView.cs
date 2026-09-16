using System.Text;
using MustyBlockBlast.Gameplay;
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
    /// costs, and a tap that buys it. Opened from <see cref="PowerUpShopButtonView"/> in the right-edge
    /// icon column.
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
        private const float BALANCE_Y = 390f;
        private const float ROWS_TOP_Y = 290f;
        private const float ROW_HEIGHT = 76f;
        private const float MESSAGE_Y = -470f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float SIDE_INSET = 48f;
        private const float HEADER_INSET = 84f;

        /// <summary>Alpha applied to a row whose kind is still behind its level gate, matching
        /// <see cref="PowerUpInventoryView"/>'s locked slots so "locked" reads the same in both places.</summary>
        private const float LOCKED_ROW_ALPHA = 0.35f;

        /// <summary>Alpha applied to a row the player cannot currently afford. Above the locked alpha on
        /// purpose: "come back with more coins" is a live offer, where a locked row is not one at all.</summary>
        private const float UNAFFORDABLE_ROW_ALPHA = 0.6f;

        // Plain strings, not String Table keys, for the reason CoinConversionView states:
        // LocalizationKeys has no currency section yet, and adding keys with no translations behind
        // them would render the keys themselves. Tracked for a follow-up.
        private const string HEADER_TEXT = "POWER-UP SHOP";
        private const string COIN_BALANCE_PREFIX_TEXT = "Coins: ";
        private const string BUY_BUTTON_TEXT = "BUY";
        private const string LOCKED_LEVEL_PREFIX = "Reach Lv";
        private const string PURCHASED_MESSAGE = "Bought!";
        private const string INSUFFICIENT_COINS_MESSAGE = "Not enough coins.";
        private const string LOCKED_MESSAGE = "Locked — level up to unlock this.";
        private const string OPENING_MESSAGE = "Tap a power-up to buy one.";

        private static readonly Vector2 BuyButtonSize = new Vector2(150f, 60f);

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1040f);
        [SerializeField] private int _headerFontSize = 52;
        [SerializeField] private int _balanceFontSize = 44;
        [SerializeField] private int _bodyFontSize = 32;
        [SerializeField] private int _buttonFontSize = 28;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(48);

        private readonly RectTransform[] _rowRects = new RectTransform[RowCount];
        private readonly Text[] _nameTexts = new Text[RowCount];
        private readonly Text[] _priceTexts = new Text[RowCount];
        private readonly Text[] _buyTexts = new Text[RowCount];
        private readonly Image[] _buyPlates = new Image[RowCount];

        /// <summary>Held counts, mirrored from <see cref="PowerUpModel"/> so a repaint never has to
        /// reach back through the model. Written only by the count subscriptions.</summary>
        private readonly int[] _counts = new int[RowCount];

        private ProfileModel _profileModel;
        private PowerUpModel _powerUpModel;
        private LevelProgressionModel _levelProgressionModel;
        private CurrencySystem _currencySystem;
        private TimerRunSystem _timerRunSystem;
        private SettingsModel _settingsModel;

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
            SettingsModel settingsModel)
        {
            _profileModel = profileModel;
            _powerUpModel = powerUpModel;
            _levelProgressionModel = levelProgressionModel;
            _currencySystem = currencySystem;
            _timerRunSystem = timerRunSystem;
            _settingsModel = settingsModel;
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
                || _currencySystem == null || _timerRunSystem == null || _settingsModel == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpShopView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so the theme is known before any row is painted below.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

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

        private void Close()
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
        /// </summary>
        private void RefreshRow(int rowIndex)
        {
            PowerUpKind kind = RowKinds[rowIndex];
            bool isLocked = !PowerUpUnlockLevels.IsUnlockedAt(kind, _currentLevelNumber);
            long price = _currencySystem.QuotePriceFor(kind, PURCHASE_QUANTITY);
            bool isAffordable = !isLocked && price <= _profileModel.CoinBalance.Value;

            float alpha = isLocked
                ? LOCKED_ROW_ALPHA
                : (isAffordable ? 1f : UNAFFORDABLE_ROW_ALPHA);

            _nameTexts[rowIndex].color = WithAlpha(_currentTheme.Ink, alpha);
            _priceTexts[rowIndex].color = WithAlpha(_currentTheme.SoftInk, alpha);
            _buyPlates[rowIndex].color = WithAlpha(
                isAffordable ? _currentTheme.Accent : _currentTheme.Ink, alpha * 0.9f);
            _buyTexts[rowIndex].color = WithAlpha(_currentTheme.CardBackground, alpha);

            _stringBuilder.Clear();
            _stringBuilder.Append(DisplayNameOf(kind));
            _stringBuilder.Append("  x");
            _stringBuilder.Append(_counts[rowIndex]);
            _nameTexts[rowIndex].text = _stringBuilder.ToString();

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
                _stringBuilder.Append(" coins");
            }

            _priceTexts[rowIndex].text = _stringBuilder.ToString();
        }

        /// <summary>
        /// Human-readable name of a kind. Spelled out here rather than taken from
        /// <see cref="System.Enum.ToString"/>, which allocates and would render "ColorCleanser" as one
        /// word. Not localized yet, for the reason the rest of this card's strings are not.
        /// </summary>
        private static string DisplayNameOf(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.RowClear:
                    return "Row Clear";
                case PowerUpKind.ColumnClear:
                    return "Column Clear";
                case PowerUpKind.Joker:
                    return "Joker";
                case PowerUpKind.ColorCleanser:
                    return "Colour Cleanser";
                case PowerUpKind.Rotate:
                    return "Rotate";
                case PowerUpKind.Reroll:
                    return "Reroll";
                case PowerUpKind.DoubleMultiplier:
                    return "Double Score";
                case PowerUpKind.GhostFit:
                    return "Ghost Fit";
                default:
                    return "Bomb";
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
            ((RectTransform)_balanceText.transform).anchoredPosition = new Vector2(0f, BALANCE_Y);

            for (int rowIndex = 0; rowIndex < RowCount; rowIndex++)
            {
                BuildRow(rowIndex, ROWS_TOP_Y - (rowIndex * ROW_HEIGHT));
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
        /// One shop row: the kind and how many are held on the left, the price in the middle, a BUY
        /// plate on the right. The row's own rect is the hit area, which is what makes the whole offer
        /// tappable rather than only the plate.
        /// </summary>
        private void BuildRow(int rowIndex, float y)
        {
            float rowWidth = _cardSize.x - (SIDE_INSET * 2f);

            var rowObject = new GameObject($"ShopRow_{RowKinds[rowIndex]}", typeof(RectTransform));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(_cardRect, false);
            Centre(rowRect, new Vector2(rowWidth, ROW_HEIGHT));
            rowRect.anchoredPosition = new Vector2(0f, y);
            _rowRects[rowIndex] = rowRect;

            Text nameText = UiTextFactory.Create(
                rowRect, "Name", _bodyFontSize, FontStyle.Bold, Color.clear);
            nameText.alignment = TextAnchor.MiddleLeft;
            var nameRect = (RectTransform)nameText.transform;
            nameRect.sizeDelta = new Vector2(rowWidth * 0.46f, ROW_HEIGHT);
            nameRect.anchoredPosition = new Vector2(-rowWidth * 0.27f, 0f);
            _nameTexts[rowIndex] = nameText;

            Text priceText = UiTextFactory.Create(
                rowRect, "Price", _bodyFontSize, FontStyle.Normal, Color.clear);
            priceText.alignment = TextAnchor.MiddleRight;
            var priceRect = (RectTransform)priceText.transform;
            priceRect.sizeDelta = new Vector2(rowWidth * 0.34f, ROW_HEIGHT);
            priceRect.anchoredPosition = new Vector2(rowWidth * 0.11f, 0f);
            _priceTexts[rowIndex] = priceText;

            var plateObject = new GameObject("BuyPlate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(rowRect, false);
            Centre(plateRect, BuyButtonSize);
            plateRect.anchoredPosition = new Vector2((rowWidth - BuyButtonSize.x) * 0.5f, 0f);

            var plateImage = plateObject.GetComponent<Image>();
            plateImage.sprite = UiSpriteFactory.RoundedSquare;
            plateImage.type = Image.Type.Sliced;
            plateImage.pixelsPerUnitMultiplier = 2f;
            plateImage.color = Color.clear;
            plateImage.raycastTarget = false;
            _buyPlates[rowIndex] = plateImage;

            Text buyText = UiTextFactory.Create(
                plateRect, "Caption", _buttonFontSize, FontStyle.Bold, Color.clear);
            buyText.text = BUY_BUTTON_TEXT;
            _buyTexts[rowIndex] = buyText;
        }

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
