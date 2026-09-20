using System.Text;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The level-start screen for the <see cref="PowerUpKind.CoinSower"/> power-up: how many extra coin
    /// cells to sow into the level about to be played, what they cost, and the tap that commits and
    /// starts it.
    /// <para>
    /// A new shape of screen for this project, and the reason it is a screen at all. Every other overlay
    /// here is opened over a run in progress and closed again; this one sits <em>between</em> tapping a
    /// level node and that level starting — the tap on a node no longer starts a run, it opens this, and
    /// this is what goes on to ask <see cref="LevelProgressionSystem.TryStartPathLevel"/>. That is a
    /// View-layer insertion and nothing more: no Model gained a "level about to start" field, because the
    /// only thing that needs to remember which level was tapped is this card, for as long as it is open
    /// (see <see cref="_pendingLevelNumber"/>).
    /// </para>
    /// <para>
    /// Deliberately only on the node-tap path. Entering Path mode from Endless or Timed also ends in a
    /// fresh Path run (see <c>LevelProgressionSystem.OnModeChanged</c>), and that path is left exactly as
    /// it was: it is a mode switch rather than a discrete "play this level" tap, and putting a purchase
    /// prompt in front of it would make changing mode cost money to get out of.
    /// </para>
    /// <para>
    /// The quantity picker is a stepper like the shop's Coins tab uses, and for the same reason: the
    /// pending figure is a picker position and nothing else, so no System and no Model has any use for
    /// it until it is committed. It is clamped live to both bounds that matter — the configured ceiling
    /// and what the balance can actually cover — so the card can never offer a quantity the purchase
    /// would refuse.
    /// </para>
    /// <para>
    /// Committing is two System calls and a start, in that order: buy the units with coins, spend them
    /// out of the inventory, hand the quantity to <see cref="LevelCoinCellSeedSystem"/>, then start the
    /// level. Buying and spending separately is not a detour — it is this codebase's one-writer-per-slice
    /// split (<see cref="CurrencySystem"/> owns the balance, <see cref="PowerUpSystem"/> owns the
    /// inventory), and a Coin Sower unit passes through the inventory exactly as every other bought
    /// power-up does.
    /// </para>
    /// <para>
    /// Zero is a first-class answer: it skips the purchase call entirely and simply starts the level, so
    /// declining costs nothing. Dismissing the card instead — a tap on the scrim, or the close cross —
    /// starts nothing at all, which is the way back out of a node tapped by accident.
    /// </para>
    /// <para>
    /// While it is open it is modal and swallows every tap, and it holds the run's clock through
    /// <see cref="TimerRunSystem.SetMenuPaused"/> — see the gate chain in <see cref="BoardInputView"/>,
    /// which is what keeps it mutually exclusive with the other overlays so that flag can never have two
    /// owners.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoinSowerPickerView : MonoBehaviour
    {
        /// <summary>
        /// How much one stepper tap moves the pending quantity. One, unlike the conversion card's coarse
        /// step: the whole range here is a handful of cells, so every value in it is worth landing on.
        /// </summary>
        private const int QUANTITY_STEP = 1;

        // Layout, in canvas reference pixels, matching the other cards so they all read as one family.
        private const float HEADER_Y = 300f;
        private const float LEVEL_Y = 224f;
        private const float BALANCE_Y = 158f;
        private const float UNIT_PRICE_Y = 96f;
        private const float QUANTITY_ROW_Y = 0f;
        private const float QUANTITY_STEPPER_X = 250f;
        private const float TOTAL_Y = -84f;
        private const float START_BUTTON_Y = -190f;
        private const float MESSAGE_Y = -290f;
        private const float STEPPER_SIZE = 96f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float SIDE_INSET = 60f;
        private const float HEADER_INSET = 84f;

        private static readonly Vector2 WideButtonSize = new Vector2(620f, 100f);

        // Plain strings, not String Table keys, for the reason PowerUpShopView states: LocalizationKeys
        // has no currency section yet, and adding keys with no translations behind them would render
        // the keys themselves. Tracked for a follow-up.
        private const string HEADER_TEXT = "COIN SOWER";
        private const string LEVEL_PREFIX_TEXT = "Level ";
        private const string COIN_BALANCE_PREFIX_TEXT = "Coins: ";
        private const string START_BUTTON_TEXT = "START LEVEL";
        private const string OPENING_MESSAGE = "Sow coin cells into this level, or start with none.";
        private const string LOCKED_MESSAGE = "Coin Sower locked — reach Lv";
        private const string NO_COINS_MESSAGE = "Not enough coins for a single coin cell.";
        private const string INSUFFICIENT_COINS_MESSAGE = "Not enough coins — lower the amount.";
        private const string PURCHASE_REFUSED_MESSAGE = "That purchase was refused. Try a lower amount.";
        private const string SOW_FAILED_MESSAGE =
            "Those coin cells could not be sown. They are still in your inventory.";
        private const string CANNOT_START_MESSAGE = "This level cannot be started right now.";

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(64);

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 760f);
        [SerializeField] private int _headerFontSize = 52;
        [SerializeField] private int _balanceFontSize = 44;
        [SerializeField] private int _quantityFontSize = 64;
        [SerializeField] private int _bodyFontSize = 32;
        [SerializeField] private int _buttonFontSize = 36;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private ProfileModel _profileModel;
        private LevelProgressionModel _levelProgressionModel;
        private SettingsModel _settingsModel;
        private CurrencySystem _currencySystem;
        private PowerUpSystem _powerUpSystem;
        private LevelProgressionSystem _levelProgressionSystem;
        private LevelCoinCellSeedSystem _coinCellSeedSystem;
        private TimerRunSystem _timerRunSystem;
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
        private Text _levelText;
        private Text _balanceText;
        private Text _unitPriceText;
        private Text _quantityText;
        private Text _totalText;
        private Text _messageText;
        private Text _minusText;
        private Text _plusText;
        private Text _startButtonText;

        private RectTransform _minusRect;
        private RectTransform _plusRect;
        private RectTransform _quantityRect;
        private RectTransform _startButtonRect;
        private Image _minusPlate;
        private Image _plusPlate;
        private Image _startButtonPlate;

        private ThemeDefinition _currentTheme;

        /// <summary>
        /// The level this card was opened for. The whole of the "a level is about to start" state, and it
        /// lives here rather than in a Model on purpose: nothing outside this card has any use for a
        /// level the player has not committed to starting, and the moment they do commit the number
        /// leaves as an argument to <see cref="LevelProgressionSystem.TryStartPathLevel"/>.
        /// </summary>
        private int _pendingLevelNumber;

        /// <summary>
        /// Coin cells the Start button would buy. A picker position and nothing else, clamped to
        /// <see cref="MaxAffordableQuantity"/> on every repaint so a balance shrinking under it can never
        /// leave it offering more than the player can pay for.
        /// </summary>
        private int _pendingQuantity;

        [Inject]
        public void Construct(
            ProfileModel profileModel,
            LevelProgressionModel levelProgressionModel,
            SettingsModel settingsModel,
            CurrencySystem currencySystem,
            PowerUpSystem powerUpSystem,
            LevelProgressionSystem levelProgressionSystem,
            LevelCoinCellSeedSystem coinCellSeedSystem,
            TimerRunSystem timerRunSystem,
            PowerUpPriceConfig priceConfig)
        {
            _profileModel = profileModel;
            _levelProgressionModel = levelProgressionModel;
            _settingsModel = settingsModel;
            _currencySystem = currencySystem;
            _powerUpSystem = powerUpSystem;
            _levelProgressionSystem = levelProgressionSystem;
            _coinCellSeedSystem = coinCellSeedSystem;
            _timerRunSystem = timerRunSystem;
            _priceConfig = priceConfig;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_profileModel == null || _levelProgressionModel == null || _settingsModel == null
                || _currencySystem == null || _powerUpSystem == null || _levelProgressionSystem == null
                || _coinCellSeedSystem == null || _timerRunSystem == null || _priceConfig == null)
            {
                Debug.LogError(
                    $"{nameof(CoinSowerPickerView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so the theme is known before anything below is painted.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Observed rather than read once on open, for the reason the shop observes them: a coin cell
            // cleared in the previous run, or a purchase made on another screen, moves the balance, and
            // reaching the unlock level must open this card in the run that got the player there.
            _profileModel.CoinBalance.Subscribe(OnBalanceChanged).AddTo(_disposables);
            _levelProgressionModel.CurrentLevelNumber.Subscribe(OnLevelChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the card for <paramref name="levelNumber"/> and holds the run's clock. Called by
        /// <see cref="LevelPathPanelView"/> when a node is tapped, in place of that tap starting the run
        /// itself.
        /// <para>
        /// Opens at a quantity of zero rather than at the most the player can afford — the opposite of
        /// the conversion card's "take the lot" default, and deliberately so: this one spends money. The
        /// default answer to an offer must be the free one.
        /// </para>
        /// </summary>
        internal void Open(int levelNumber)
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            _pendingLevelNumber = levelNumber;
            _pendingQuantity = 0;
            SetMessageForOpening();
            Refresh();

            _panel.SetActive(true);
            transform.SetAsLastSibling();
            _timerRunSystem.SetMenuPaused(true);
        }

        /// <summary>
        /// Routes a tap while the panel is open. The close cross wins, then the picker and the Start
        /// button; the card then swallows anything else, so a tap on a figure is a deliberate no-op
        /// rather than a dismissal. Only a tap on the scrim outside the card closes.
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

            if (RectTransformUtility.RectangleContainsScreenPoint(_minusRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(_pendingQuantity - QUANTITY_STEP);
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_plusRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(_pendingQuantity + QUANTITY_STEP);
                return;
            }

            // Tapping the figure itself takes as many as the player can afford, the same shortcut the
            // conversion card's figure is — and the same reason: the top of the range is a common answer
            // and stepping to it one at a time is a chore.
            if (RectTransformUtility.RectangleContainsScreenPoint(_quantityRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(MaxAffordableQuantity());
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_startButtonRect, screenPosition, eventCamera))
            {
                ConfirmAndStart();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        /// <summary>Hides the card and releases the clock. Starts nothing: a dismissal is the way back
        /// out of a node tapped by accident.</summary>
        private void Close()
        {
            _panel.SetActive(false);
            _timerRunSystem.SetMenuPaused(false);
        }

        /// <summary>
        /// Commits the picker and starts the level. The order is the substance of this method.
        /// <para>
        /// A quantity of zero skips the purchase entirely rather than asking for none of something:
        /// <see cref="CurrencySystem.TryPurchasePowerUp"/> answers an empty ask with
        /// <see cref="PowerUpPurchaseResult.InvalidQuantity"/>, which is the right answer to a bug and
        /// the wrong one to a player who simply wants to play the level for free.
        /// </para>
        /// <para>
        /// Above zero: buy, then spend, then queue, then start. Every step but the last can refuse, and
        /// each refusal leaves the card open with a message rather than starting the level as though
        /// nothing had been asked for — a silent fall-through to a free start would spend the player's
        /// tap on something they did not choose.
        /// </para>
        /// <para>
        /// The spend cannot fail in practice: <see cref="PowerUpSystem.TrySpendCoinSowerBulk"/> refuses
        /// atomically and the units it is asked for were granted by the purchase one line above. If it
        /// somehow does, the coins are already gone but the units are <em>not</em>: they sit in the
        /// persisted inventory and the next commit can spend them. So nothing is queued, nothing is
        /// started, and the card says as much — the one outcome that neither loses the purchase nor sows
        /// cells that were not paid out of the inventory.
        /// </para>
        /// <para>
        /// The queue happens before the start because it must: the run consumes it on
        /// <c>RunStartedMessage</c>, which <see cref="LevelProgressionSystem.TryStartPathLevel"/>
        /// publishes on its way through. Should that start then be refused — a level that stopped being
        /// unlocked, or a mode that changed under the card — the queued quantity simply stays queued and
        /// is sown by whichever run opens next. The player paid for coin cells and gets coin cells; they
        /// are not lost to a refusal this card cannot cause.
        /// </para>
        /// </summary>
        private void ConfirmAndStart()
        {
            if (_pendingQuantity > 0)
            {
                PowerUpPurchaseResult purchaseResult = _currencySystem.TryPurchasePowerUp(
                    PowerUpKind.CoinSower, _pendingQuantity);
                if (purchaseResult != PowerUpPurchaseResult.Success)
                {
                    SetMessage(MessageFor(purchaseResult));
                    return;
                }

                if (!_powerUpSystem.TrySpendCoinSowerBulk(_pendingQuantity))
                {
                    SetMessage(SOW_FAILED_MESSAGE);
                    return;
                }

                _coinCellSeedSystem.QueueExtraCoinCells(_pendingQuantity);
            }

            if (!_levelProgressionSystem.TryStartPathLevel(_pendingLevelNumber))
            {
                SetMessage(CANNOT_START_MESSAGE);
                return;
            }

            Close();
        }

        /// <summary>What a refused purchase is worth telling the player. Locked is folded into the
        /// opening message's own locked wording, which already names the level to reach.</summary>
        private string MessageFor(PowerUpPurchaseResult result)
        {
            switch (result)
            {
                case PowerUpPurchaseResult.Locked:
                    return LockedMessage();
                case PowerUpPurchaseResult.InsufficientCoins:
                    return INSUFFICIENT_COINS_MESSAGE;
                default:
                    // InvalidQuantity, which the zero branch above makes unreachable. There is no wording
                    // that would help a player with a bug in this View, so it reads as a plain refusal.
                    return PURCHASE_REFUSED_MESSAGE;
            }
        }

        /// <summary>
        /// The most coin cells the player could pay for right now: the configured ceiling, or what the
        /// balance covers at the unit price, whichever is smaller.
        /// <para>
        /// Integer division, so a balance that covers two and a half cells offers two. That is the same
        /// arithmetic <see cref="CurrencySystem.TryPurchasePowerUp"/> charges — quantity times unit price,
        /// as a whole — rather than a second, friendlier rounding that would offer a quantity the purchase
        /// then refuses.
        /// </para>
        /// <para>
        /// Zero while the kind is behind its level gate, so the picker cannot be moved off zero at all
        /// there: coins never open that gate (the System refuses the purchase too, so this is the message
        /// rather than the enforcement). Zero unit price — only reachable from a mis-authored config —
        /// falls back to the ceiling rather than dividing by it.
        /// </para>
        /// </summary>
        private int MaxAffordableQuantity()
        {
            if (IsLocked())
            {
                return 0;
            }

            int ceiling = _priceConfig.CoinSowerMaxQuantity;
            int unitPrice = UnitPrice();
            if (unitPrice <= 0)
            {
                return ceiling;
            }

            int balance = _profileModel.CoinBalance.Value;
            int quantity = Mathf.Min(ceiling, balance / unitPrice);

            // The division above is only exact while the unit price is. Under a live promotion it is
            // not: CurrencySystem discounts the line total and rounds once, so a floored unit price can
            // divide into the balance one time more than the real total covers. Walked back down against
            // the actual quote — at most a handful of steps, since the ceiling is a handful — so the
            // picker can never offer a quantity the purchase would then refuse as unaffordable.
            while (quantity > 0 && _currencySystem.QuotePriceFor(PowerUpKind.CoinSower, quantity) > balance)
            {
                quantity--;
            }

            return quantity;
        }

        /// <summary>Coins one coin cell costs. Quoted through <see cref="CurrencySystem.QuotePriceFor"/>
        /// for a single unit, so the figure shown and the figure charged are the same arithmetic — the
        /// contract the shop and the conversion screen both have with their System.</summary>
        private int UnitPrice() => (int)_currencySystem.QuotePriceFor(PowerUpKind.CoinSower, 1);

        /// <summary>Whether the kind is still behind its level gate, read at the same public seam the
        /// shop reads it at so the card and the System can never disagree.</summary>
        private bool IsLocked()
            => !PowerUpUnlockLevels.IsUnlockedAt(
                PowerUpKind.CoinSower, _levelProgressionModel.CurrentLevelNumber.Value);

        private void OnBalanceChanged(int value) => Refresh();

        private void OnLevelChanged(int levelNumber) => Refresh();

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
            _levelText.color = theme.SoftInk;
            _balanceText.color = theme.Accent;
            _unitPriceText.color = theme.SoftInk;
            _quantityText.color = theme.Ink;
            _totalText.color = theme.Ink;
            _messageText.color = theme.SoftInk;

            Color neutralPlate = Color.Lerp(theme.CardBackground, theme.Ink, 0.16f);
            _minusPlate.color = neutralPlate;
            _plusPlate.color = neutralPlate;
            _minusText.color = theme.Ink;
            _plusText.color = theme.Ink;

            // Start is the call to action and gets the accent fill, the same primary treatment the
            // conversion card gives Convert.
            _startButtonPlate.color = theme.Accent;
            _startButtonText.color = theme.CardBackground;

            _closeBarA.color = theme.Ink;
            _closeBarB.color = theme.Ink;

            Refresh();
        }

        /// <summary>Clamps and stores the picker position, then repaints. The one place the pending
        /// quantity is written, so it can never be left outside what can be bought.</summary>
        private void SetPendingQuantity(int quantity)
        {
            _pendingQuantity = Mathf.Clamp(quantity, 0, MaxAffordableQuantity());
            Refresh();
        }

        private void SetMessage(string message)
        {
            _messageText.text = message;
            Refresh();
        }

        /// <summary>The line the card opens on: the offer, or why there is no offer. Written on open
        /// rather than every repaint, so a refusal message survives the repaint that follows it.</summary>
        private void SetMessageForOpening()
        {
            if (IsLocked())
            {
                _messageText.text = LockedMessage();
                return;
            }

            _messageText.text = MaxAffordableQuantity() > 0 ? OPENING_MESSAGE : NO_COINS_MESSAGE;
        }

        private string LockedMessage()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(LOCKED_MESSAGE);
            _stringBuilder.Append(PowerUpUnlockLevels.LevelFor(PowerUpKind.CoinSower));
            return _stringBuilder.ToString();
        }

        /// <summary>Repaints every figure from the models. Cheap enough to be the only repaint path: it
        /// runs on an open, a stepper tap, a balance change and a theme switch — never per frame.</summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null || _currencySystem == null)
            {
                return;
            }

            int maxQuantity = MaxAffordableQuantity();

            // Re-clamped here as well as in SetPendingQuantity: a purchase made from this card, or a
            // coin spent elsewhere, shrinks the ceiling underneath the picker, and this is the repaint
            // that follows it.
            if (_pendingQuantity > maxQuantity)
            {
                _pendingQuantity = maxQuantity;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(LEVEL_PREFIX_TEXT);
            _stringBuilder.Append(_pendingLevelNumber);
            _levelText.text = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append(COIN_BALANCE_PREFIX_TEXT);
            _stringBuilder.Append(_profileModel.CoinBalance.Value);
            _balanceText.text = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append(UnitPrice());
            _stringBuilder.Append(" coins per coin cell");
            _unitPriceText.text = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append(_pendingQuantity);
            _quantityText.text = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append("Total: ");
            _stringBuilder.Append(_currencySystem.QuotePriceFor(PowerUpKind.CoinSower, _pendingQuantity));
            _stringBuilder.Append(" coins");
            _totalText.text = _stringBuilder.ToString();
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("CoinSowerPickerPanel", typeof(RectTransform), typeof(Image));
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
                panelRect, "CoinSowerPickerCard", _cardSize, out _cardImage, out _cardShadowImage);

            // Everything below is built with a transparent colour: the card is built in Awake, before
            // the theme is known, and the theme subscription in Start paints all of it.
            _headerText = UiTextFactory.Create(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_headerText.transform).anchoredPosition = new Vector2(0f, HEADER_Y);
            _headerText.text = HEADER_TEXT;

            _levelText = UiTextFactory.Create(
                _cardRect, "Level", _bodyFontSize, FontStyle.Normal, Color.clear);
            ((RectTransform)_levelText.transform).anchoredPosition = new Vector2(0f, LEVEL_Y);

            _balanceText = UiTextFactory.Create(
                _cardRect, "Balance", _balanceFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_balanceText.transform).anchoredPosition = new Vector2(0f, BALANCE_Y);

            _unitPriceText = UiTextFactory.Create(
                _cardRect, "UnitPrice", _bodyFontSize, FontStyle.Normal, Color.clear);
            ((RectTransform)_unitPriceText.transform).anchoredPosition = new Vector2(0f, UNIT_PRICE_Y);

            _minusRect = BuildStepper("MinusButton", -QUANTITY_STEPPER_X, "-", out _minusPlate, out _minusText);
            _plusRect = BuildStepper("PlusButton", QUANTITY_STEPPER_X, "+", out _plusPlate, out _plusText);

            _quantityText = UiTextFactory.Create(
                _cardRect, "Quantity", _quantityFontSize, FontStyle.Bold, Color.clear);
            _quantityRect = (RectTransform)_quantityText.transform;
            _quantityRect.sizeDelta = new Vector2(320f, STEPPER_SIZE);
            _quantityRect.anchoredPosition = new Vector2(0f, QUANTITY_ROW_Y);

            _totalText = UiTextFactory.Create(
                _cardRect, "Total", _bodyFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_totalText.transform).anchoredPosition = new Vector2(0f, TOTAL_Y);

            _startButtonRect = BuildWideButton(
                "StartButton", START_BUTTON_Y, START_BUTTON_TEXT,
                out _startButtonPlate, out _startButtonText);

            _messageText = UiTextFactory.Create(
                _cardRect, "Message", _bodyFontSize, FontStyle.Normal, Color.clear);
            var messageRect = (RectTransform)_messageText.transform;
            messageRect.sizeDelta = new Vector2(_cardSize.x - (SIDE_INSET * 2f), 64f);
            messageRect.anchoredPosition = new Vector2(0f, MESSAGE_Y);
            _messageText.text = OPENING_MESSAGE;

            BuildCloseButton();

            _panel = panelObject;
        }

        /// <summary>One square stepper plate with a glyph on it. Returns the root the tap is hit-tested
        /// against.</summary>
        private RectTransform BuildStepper(
            string name, float x, string glyph, out Image plateImage, out Text glyphText)
        {
            var plateObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(_cardRect, false);
            Centre(plateRect, new Vector2(STEPPER_SIZE, STEPPER_SIZE));
            plateRect.anchoredPosition = new Vector2(x, QUANTITY_ROW_Y);

            plateImage = plateObject.GetComponent<Image>();
            plateImage.sprite = UiSpriteFactory.RoundedSquare;
            plateImage.type = Image.Type.Sliced;
            plateImage.pixelsPerUnitMultiplier = 1.4f;
            plateImage.color = Color.clear;
            plateImage.raycastTarget = false;

            glyphText = UiTextFactory.Create(plateRect, "Glyph", _quantityFontSize, FontStyle.Bold, Color.clear);
            glyphText.text = glyph;

            return plateRect;
        }

        /// <summary>One full-width action plate with a caption. Returns the root the tap is hit-tested
        /// against.</summary>
        private RectTransform BuildWideButton(
            string name, float y, string caption, out Image plateImage, out Text captionText)
        {
            var plateObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(_cardRect, false);
            Centre(plateRect, WideButtonSize);
            plateRect.anchoredPosition = new Vector2(0f, y);

            plateImage = plateObject.GetComponent<Image>();
            plateImage.sprite = UiSpriteFactory.RoundedSquare;
            plateImage.type = Image.Type.Sliced;
            plateImage.pixelsPerUnitMultiplier = 1.4f;
            plateImage.color = Color.clear;
            plateImage.raycastTarget = false;

            captionText = UiTextFactory.Create(plateRect, "Caption", _buttonFontSize, FontStyle.Bold, Color.clear);
            captionText.text = caption;

            return plateRect;
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
