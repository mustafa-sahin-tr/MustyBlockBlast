using System.Text;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The score conversion screen: what the player has scored in total, what they have already
    /// sold, what is still theirs to sell, and the two ways to earn a coin.
    /// <para>
    /// Three figures rather than one, and deliberately so. "Total" alone would look like a balance the
    /// player keeps losing; "available" alone would hide that the rest was already banked. Shown
    /// together they say the one thing the player needs to trust: nothing has been taken, the score is
    /// all still accounted for.
    /// </para>
    /// <para>
    /// Holds no logic, like every other card here. The amount picker is the only state it owns — a
    /// pending figure, clamped to what is available — and even that is only a number handed to
    /// <see cref="CurrencySystem"/> on a tap. It never writes a model: the balance and the counters are
    /// observed, so they repaint whether this screen or something else moved them.
    /// </para>
    /// <para>
    /// Opened by the player from the power-up shop (see <see cref="PowerUpShopView"/>) and by nothing
    /// else — it no longer opens itself at game over (issue #219): the end of a run is a result, not a
    /// sales pitch. Closes on <see cref="RunStartedMessage"/> or on a tap outside its card. While it
    /// is open it is modal and swallows every tap, above even the hub it was opened from — see the
    /// gate chain in <see cref="BoardInputView"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoinConversionView : MonoBehaviour
    {
        // Layout, in canvas reference pixels, matching the other cards so they all read as one family.
        private const float HEADER_Y = 400f;
        private const float BALANCE_Y = 312f;
        private const float STATS_TOP_Y = 180f;
        private const float STATS_ROW_SPACING = 64f;
        private const float AMOUNT_ROW_Y = -40f;
        private const float AMOUNT_STEPPER_X = 250f;
        private const float QUOTE_Y = -116f;
        private const float CONVERT_BUTTON_Y = -206f;
        private const float AD_BUTTON_Y = -326f;
        private const float BUNDLE_LABEL_Y = -364f;
        private const float BUNDLE_ROW_Y = -408f;
        private const float BUNDLE_ROW_HEIGHT = 50f;
        private const float BUNDLE_ROW_GAP = 14f;
        private const float STEPPER_SIZE = 96f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float SIDE_INSET = 60f;
        private const float HEADER_INSET = 84f;

        private static readonly Vector2 WideButtonSize = new Vector2(620f, 100f);

        /// <summary>
        /// How much one stepper tap moves the pending amount. A coarse step on purpose: the point of
        /// the picker is "some of it, not all of it", and a player who wants a round number is better
        /// served by the two ends — the minimum and the whole pool — than by nudging one point at a time.
        /// </summary>
        private const int AMOUNT_STEP = 50;

        // Plain strings, not String Table keys, for the reason ProfilePanelView states: LocalizationKeys
        // has no currency section yet, and adding keys with no translations behind them would render the
        // keys themselves. Tracked for a follow-up.
        private const string HEADER_TEXT = "CONVERT SCORE";
        private const string COIN_BALANCE_PREFIX_TEXT = "Coins: ";
        private const string TOTAL_LABEL_TEXT = "Total score earned";
        private const string CONVERTED_LABEL_TEXT = "Already converted";
        private const string AVAILABLE_LABEL_TEXT = "Available to convert";
        private const string NOTHING_TO_CONVERT_TEXT = "Nothing left to convert — play on to earn more.";
        private const string CONVERT_BUTTON_TEXT = "CONVERT";
        private const string WATCH_AD_BUTTON_TEXT = "WATCH AD FOR COINS";
        private const string BUNDLE_ROW_LABEL_TEXT = "or buy coins";

        /// <summary>
        /// Most bundle plates the strip will draw, whatever the config holds. A ceiling on the row
        /// rather than on the line-up: the strip is one line across the bottom of an existing card, and
        /// a fifth plate would be too narrow to read. A config with more bundles than this needs the
        /// real storefront screen this strip is standing in for, not a thinner plate.
        /// </summary>
        private const int BUNDLE_ROW_MAX = 4;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(48);

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 900f);
        [SerializeField] private int _headerFontSize = 56;
        [SerializeField] private int _balanceFontSize = 64;
        [SerializeField] private int _bodyFontSize = 32;
        [SerializeField] private int _buttonFontSize = 36;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private ProfileModel _profileModel;
        private CurrencySystem _currencySystem;
        private CoinBundleConfig _bundleConfig;
        private SettingsModel _settingsModel;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;

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
        private Text _totalLabelText;
        private Text _totalValueText;
        private Text _convertedLabelText;
        private Text _convertedValueText;
        private Text _availableLabelText;
        private Text _availableValueText;
        private Text _amountText;
        private Text _quoteText;
        private Text _minusText;
        private Text _plusText;
        private Text _convertButtonText;
        private Text _adButtonText;
        private Text _bundleRowLabelText;

        /// <summary>
        /// The real-money bundle strip: one plate per offered SKU, built once from
        /// <see cref="CoinBundleConfig"/> in Awake because the line-up is static config and cannot
        /// change while the card is open. Parallel arrays rather than a small holder type, matching how
        /// the rest of this card keeps its plates and captions — and the SKUs are kept alongside so a
        /// tap resolves to a product id without re-reading the config.
        /// </summary>
        private RectTransform[] _bundleRects;
        private Image[] _bundlePlates;
        private Text[] _bundleTexts;
        private string[] _bundleSkus;

        private RectTransform _minusRect;
        private RectTransform _plusRect;
        private RectTransform _amountRect;
        private RectTransform _convertButtonRect;
        private RectTransform _adButtonRect;
        private Image _minusPlate;
        private Image _plusPlate;
        private Image _convertButtonPlate;
        private Image _adButtonPlate;

        /// <summary>
        /// The amount the Convert button would sell. Owned here because it is a picker position and
        /// nothing else — no System and no Model has any use for a figure the player has not committed
        /// to. Clamped to <see cref="CurrencySystem.AvailableToConvert"/> on every repaint, so the pool
        /// shrinking under it can never leave it offering more than there is.
        /// </summary>
        private int _pendingAmount;

        /// <summary>Guards the ad flow against a second tap while a request is already in flight.</summary>
        private bool _isRequestingAd;

        /// <summary>
        /// Guards the purchase flow the same way, and separately: a store prompt is modal and slow, and
        /// a second tap behind it would ask the store for a second concurrent order that it would only
        /// refuse.
        /// </summary>
        private bool _isPurchasingBundle;

        [Inject]
        public void Construct(
            ProfileModel profileModel,
            CurrencySystem currencySystem,
            CoinBundleConfig bundleConfig,
            SettingsModel settingsModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _profileModel = profileModel;
            _currencySystem = currencySystem;
            _bundleConfig = bundleConfig;
            _settingsModel = settingsModel;
            _runStartedSubscriber = runStartedSubscriber;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_profileModel == null || _currencySystem == null || _bundleConfig == null
                || _settingsModel == null || _runStartedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(CoinConversionView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Built here rather than in Awake with the rest of the card: it is the one part of the card
            // whose shape comes from an injected config, and injection has only certainly happened by
            // now. It must precede the theme subscription below, which paints what this creates.
            BuildBundleRow();

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Observed rather than read once on open: an ad grant and a conversion both move these
            // while the card is showing, and subscribing is what keeps the drawn figures and the model
            // from ever disagreeing.
            _profileModel.CoinBalance.Subscribe(OnCurrencyChanged).AddTo(_disposables);
            _profileModel.TotalScoreEarned.Subscribe(OnCurrencyChanged).AddTo(_disposables);
            _profileModel.ScoreConverted.Subscribe(OnCurrencyChanged).AddTo(_disposables);

            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the card, opened on the whole pool selected: the player who wants all of it — most
        /// of them — taps Convert once, and the picker is there for the one who wants less. Reached
        /// from the shop's convert button; an already-open card returns immediately.
        /// </summary>
        internal void Open()
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            SetPendingAmount(_currencySystem.AvailableToConvert);

            _panel.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// <summary>
        /// Routes a tap while the panel is open. The close cross wins, then the picker and the two
        /// actions; the card then swallows anything else, so a tap on a figure is a deliberate no-op
        /// rather than a dismissal. Only a tap on the scrim outside the card closes — which is also how
        /// the player gets back to the shop underneath.
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
                SetPendingAmount(_pendingAmount - AMOUNT_STEP);
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_plusRect, screenPosition, eventCamera))
            {
                SetPendingAmount(_pendingAmount + AMOUNT_STEP);
                return;
            }

            // Tapping the figure itself takes the lot. The one shortcut on the card, because "all of it"
            // is the common case and stepping there from 50 at a time would be a chore.
            if (RectTransformUtility.RectangleContainsScreenPoint(_amountRect, screenPosition, eventCamera))
            {
                SetPendingAmount(_currencySystem.AvailableToConvert);
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_convertButtonRect, screenPosition, eventCamera))
            {
                // Refused by the System when there is nothing to convert — the button is deliberately
                // still tappable so the card needs no disabled state, and the refusal is silent.
                _currencySystem.ConvertScoreToCoins(_pendingAmount);
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_adButtonRect, screenPosition, eventCamera))
            {
                RequestAdCoins().Forget();
                return;
            }

            if (TryHandleBundleTap(screenPosition, eventCamera))
            {
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        private void Close() => _panel.SetActive(false);

        /// <summary>
        /// Watches an ad for coins. Nothing about the convertible pool is touched here — this is the
        /// second, independent faucet, and the System keeps it that way.
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
                await _currencySystem.GrantCoinsFromAdAsync(
                    _currencySystem.AdRewardCoins, this.GetCancellationTokenOnDestroy());
            }
            finally
            {
                _isRequestingAd = false;
            }
        }

        /// <summary>
        /// Routes a tap to a bundle plate, if it landed on one. Returns whether it did, so the caller
        /// can stop rather than fall through to the card's swallow-everything case.
        /// <para>
        /// An index loop rather than a per-plate handler, for the reason every other hit test on this
        /// card is one: the card owns no EventSystem buttons at all, and a strip of four plates is not
        /// worth a second input mechanism.
        /// </para>
        /// </summary>
        private bool TryHandleBundleTap(Vector2 screenPosition, Camera eventCamera)
        {
            if (_bundleRects == null)
            {
                return false;
            }

            for (int bundleIndex = 0; bundleIndex < _bundleRects.Length; bundleIndex++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(
                        _bundleRects[bundleIndex], screenPosition, eventCamera))
                {
                    PurchaseBundle(_bundleSkus[bundleIndex]).Forget();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Buys a coin bundle with real money. The fourth faucet, and the only one that is not free: like
        /// the ad grant it takes nothing out of the convertible pool, and like the ad grant every
        /// decision about whether and how much to credit belongs to the System — this only names the SKU
        /// the player tapped.
        /// <para>
        /// A refusal is silent, as the Convert button's is: a dismissal needs no message (the player did
        /// it), and the failure cases have no currency strings to say it with yet. The balance label
        /// repaints itself on a success through the model subscription, which is the confirmation.
        /// </para>
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
                await _currencySystem.PurchaseCoinBundleAsync(
                    sku, this.GetCancellationTokenOnDestroy());
            }
            finally
            {
                _isPurchasingBundle = false;
            }
        }

        private void OnRunStarted(RunStartedMessage message) => _panel.SetActive(false);

        private void OnCurrencyChanged(int value) => Refresh();

        /// <summary>Clamps and stores the picker position, then repaints. The one place the pending
        /// amount is written, so it can never be left outside what is available.</summary>
        private void SetPendingAmount(int amount)
        {
            _pendingAmount = Mathf.Clamp(amount, 0, _currencySystem.AvailableToConvert);
            Refresh();
        }

        /// <summary>Repaints every figure from the models. Cheap enough to be the only repaint path:
        /// it runs on an open, a conversion, an ad grant and a stepper tap — never per frame.</summary>
        private void Refresh()
        {
            if (_panel == null || _currencySystem == null)
            {
                return;
            }

            int available = _currencySystem.AvailableToConvert;

            // Re-clamped here as well as in SetPendingAmount: a conversion made from this card shrinks
            // the pool underneath the picker, and this is the repaint that follows it.
            if (_pendingAmount > available)
            {
                _pendingAmount = available;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(COIN_BALANCE_PREFIX_TEXT);
            _stringBuilder.Append(_profileModel.CoinBalance.Value);
            _balanceText.text = _stringBuilder.ToString();

            _totalValueText.text = FormatInt(_profileModel.TotalScoreEarned.Value);
            _convertedValueText.text = FormatInt(_profileModel.ScoreConverted.Value);
            _availableValueText.text = FormatInt(available);

            if (available <= 0)
            {
                _amountText.text = FormatInt(0);
                _quoteText.text = NOTHING_TO_CONVERT_TEXT;
                return;
            }

            _amountText.text = FormatInt(_pendingAmount);

            _stringBuilder.Clear();
            _stringBuilder.Append(_pendingAmount);
            _stringBuilder.Append(" pts  ->  ");
            _stringBuilder.Append(_currencySystem.QuoteCoinsFor(_pendingAmount));
            _stringBuilder.Append(" coins");
            _quoteText.text = _stringBuilder.ToString();
        }

        /// <summary>One integer through the shared builder, so a repaint allocates the one string it
        /// hands to the label rather than the several a concatenation would.</summary>
        private string FormatInt(int value)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(value);
            return _stringBuilder.ToString();
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;

            _headerText.color = theme.Ink;
            _balanceText.color = theme.Accent;
            _amountText.color = theme.Ink;
            _quoteText.color = theme.SoftInk;

            _totalLabelText.color = theme.SoftInk;
            _convertedLabelText.color = theme.SoftInk;
            _availableLabelText.color = theme.SoftInk;
            _totalValueText.color = theme.Ink;
            _convertedValueText.color = theme.Ink;
            _availableValueText.color = theme.Ink;

            Color neutralPlate = Color.Lerp(theme.CardBackground, theme.Ink, 0.16f);
            _minusPlate.color = neutralPlate;
            _plusPlate.color = neutralPlate;
            _minusText.color = theme.Ink;
            _plusText.color = theme.Ink;

            // Convert is the call to action and gets the accent fill; the ad is the alternative and
            // gets the neutral plate — the same primary/secondary pairing the game-over card uses.
            _convertButtonPlate.color = theme.Accent;
            _convertButtonText.color = theme.CardBackground;
            _adButtonPlate.color = neutralPlate;
            _adButtonText.color = theme.Ink;

            _closeBarA.color = theme.Ink;
            _closeBarB.color = theme.Ink;

            if (_bundleRowLabelText != null)
            {
                _bundleRowLabelText.color = theme.SoftInk;
            }

            // The bundle plates take the neutral plate, not the accent: Convert is the card's one call
            // to action, and a row of real-money buttons shouting louder than it would be a nudge this
            // card has no business making.
            if (_bundlePlates != null)
            {
                for (int bundleIndex = 0; bundleIndex < _bundlePlates.Length; bundleIndex++)
                {
                    _bundlePlates[bundleIndex].color = neutralPlate;
                    _bundleTexts[bundleIndex].color = theme.Ink;
                }
            }
        }

        /// <summary>
        /// Builds the real-money bundle strip along the bottom of the card: one plate per offered SKU,
        /// captioned with the coins it pays, sized to share the row evenly.
        /// <para>
        /// A strip on an existing card rather than a storefront of its own, deliberately and for now.
        /// This is the screen a player already comes to when they want more coins, so it is where a
        /// bundle belongs until there is a designed store to move it to — the same "functional slice,
        /// not a final design pass" footing the power-up shop was built on.
        /// </para>
        /// <para>
        /// Prices are not shown, because this card cannot know them: what a bundle costs in money lives
        /// in the platform store and is read back from it, so a figure printed here would be a guess.
        /// The store's own prompt is what quotes the price, which is also the only quote the player can
        /// act on.
        /// </para>
        /// </summary>
        private void BuildBundleRow()
        {
            int bundleCount = Mathf.Min(_bundleConfig.BundleCount, BUNDLE_ROW_MAX);
            if (bundleCount <= 0)
            {
                return;
            }

            _bundleRowLabelText = UiTextFactory.Create(
                _cardRect, "BundleRowLabel", _bodyFontSize, FontStyle.Normal, Color.clear);
            ((RectTransform)_bundleRowLabelText.transform).anchoredPosition =
                new Vector2(0f, BUNDLE_LABEL_Y);
            _bundleRowLabelText.text = BUNDLE_ROW_LABEL_TEXT;

            _bundleRects = new RectTransform[bundleCount];
            _bundlePlates = new Image[bundleCount];
            _bundleTexts = new Text[bundleCount];
            _bundleSkus = new string[bundleCount];

            float rowWidth = _cardSize.x - (SIDE_INSET * 2f);
            float slotWidth = rowWidth / bundleCount;
            float plateWidth = slotWidth - BUNDLE_ROW_GAP;
            float firstSlotCentre = -(rowWidth * 0.5f) + (slotWidth * 0.5f);

            for (int bundleIndex = 0; bundleIndex < bundleCount; bundleIndex++)
            {
                CoinBundle bundle = _bundleConfig.BundleAt(bundleIndex);
                _bundleSkus[bundleIndex] = bundle.Sku;

                var plateObject = new GameObject(
                    "BundleButton" + bundleIndex, typeof(RectTransform), typeof(Image));
                var plateRect = (RectTransform)plateObject.transform;
                plateRect.SetParent(_cardRect, false);
                Centre(plateRect, new Vector2(plateWidth, BUNDLE_ROW_HEIGHT));
                plateRect.anchoredPosition = new Vector2(
                    firstSlotCentre + (slotWidth * bundleIndex), BUNDLE_ROW_Y);

                var plateImage = plateObject.GetComponent<Image>();
                plateImage.sprite = UiSpriteFactory.RoundedSquare;
                plateImage.type = Image.Type.Sliced;
                plateImage.pixelsPerUnitMultiplier = 1.4f;
                plateImage.color = Color.clear;
                plateImage.raycastTarget = false;

                Text captionText = UiTextFactory.Create(
                    plateRect, "Caption", _bodyFontSize, FontStyle.Bold, Color.clear);
                captionText.text = FormatInt(bundle.CoinAmount);

                _bundleRects[bundleIndex] = plateRect;
                _bundlePlates[bundleIndex] = plateImage;
                _bundleTexts[bundleIndex] = captionText;
            }
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("CoinConversionPanel", typeof(RectTransform), typeof(Image));
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
                panelRect, "CoinConversionCard", _cardSize, out _cardImage, out _cardShadowImage);

            // Everything below is built with a transparent colour: the card is built in Awake, before
            // the theme is known, and the theme subscription in Start paints all of it.
            _headerText = UiTextFactory.Create(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_headerText.transform).anchoredPosition = new Vector2(0f, HEADER_Y);
            _headerText.text = HEADER_TEXT;

            _balanceText = UiTextFactory.Create(
                _cardRect, "Balance", _balanceFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_balanceText.transform).anchoredPosition = new Vector2(0f, BALANCE_Y);

            BuildStatRow(
                "Total", STATS_TOP_Y, TOTAL_LABEL_TEXT, out _totalLabelText, out _totalValueText);
            BuildStatRow(
                "Converted", STATS_TOP_Y - STATS_ROW_SPACING, CONVERTED_LABEL_TEXT,
                out _convertedLabelText, out _convertedValueText);
            BuildStatRow(
                "Available", STATS_TOP_Y - (STATS_ROW_SPACING * 2f), AVAILABLE_LABEL_TEXT,
                out _availableLabelText, out _availableValueText);

            _minusRect = BuildStepper("MinusButton", -AMOUNT_STEPPER_X, "-", out _minusPlate, out _minusText);
            _plusRect = BuildStepper("PlusButton", AMOUNT_STEPPER_X, "+", out _plusPlate, out _plusText);

            _amountText = UiTextFactory.Create(
                _cardRect, "Amount", _balanceFontSize, FontStyle.Bold, Color.clear);
            _amountRect = (RectTransform)_amountText.transform;
            _amountRect.sizeDelta = new Vector2(320f, STEPPER_SIZE);
            _amountRect.anchoredPosition = new Vector2(0f, AMOUNT_ROW_Y);

            _quoteText = UiTextFactory.Create(_cardRect, "Quote", _bodyFontSize, FontStyle.Normal, Color.clear);
            ((RectTransform)_quoteText.transform).anchoredPosition = new Vector2(0f, QUOTE_Y);

            _convertButtonRect = BuildWideButton(
                "ConvertButton", CONVERT_BUTTON_Y, CONVERT_BUTTON_TEXT,
                out _convertButtonPlate, out _convertButtonText);
            _adButtonRect = BuildWideButton(
                "WatchAdButton", AD_BUTTON_Y, WATCH_AD_BUTTON_TEXT, out _adButtonPlate, out _adButtonText);

            BuildCloseButton();

            _panel = panelObject;
        }

        /// <summary>One label-and-value row: the label left-aligned inside the card, the figure right.
        /// Three of these are what make the card say "nothing was taken".</summary>
        private void BuildStatRow(
            string name, float y, string labelValue, out Text labelText, out Text valueText)
        {
            float halfWidth = _cardSize.x * 0.5f;

            labelText = UiTextFactory.Create(
                _cardRect, name + "Label", _bodyFontSize, FontStyle.Normal, Color.clear);
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.text = labelValue;
            var labelRect = (RectTransform)labelText.transform;
            labelRect.sizeDelta = new Vector2(halfWidth, STATS_ROW_SPACING);
            labelRect.anchoredPosition = new Vector2(-halfWidth + SIDE_INSET + (halfWidth * 0.5f), y);

            valueText = UiTextFactory.Create(
                _cardRect, name + "Value", _bodyFontSize, FontStyle.Bold, Color.clear);
            valueText.alignment = TextAnchor.MiddleRight;
            var valueRect = (RectTransform)valueText.transform;
            valueRect.sizeDelta = new Vector2(halfWidth * 0.6f, STATS_ROW_SPACING);
            valueRect.anchoredPosition = new Vector2(halfWidth - SIDE_INSET - (halfWidth * 0.3f), y);
        }

        /// <summary>One square stepper plate with a glyph on it. Returns the root the tap is
        /// hit-tested against.</summary>
        private RectTransform BuildStepper(
            string name, float x, string glyph, out Image plateImage, out Text glyphText)
        {
            var plateObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(_cardRect, false);
            Centre(plateRect, new Vector2(STEPPER_SIZE, STEPPER_SIZE));
            plateRect.anchoredPosition = new Vector2(x, AMOUNT_ROW_Y);

            plateImage = plateObject.GetComponent<Image>();
            plateImage.sprite = UiSpriteFactory.RoundedSquare;
            plateImage.type = Image.Type.Sliced;
            plateImage.pixelsPerUnitMultiplier = 1.4f;
            plateImage.color = Color.clear;
            plateImage.raycastTarget = false;

            glyphText = UiTextFactory.Create(plateRect, "Glyph", _balanceFontSize, FontStyle.Bold, Color.clear);
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
                (_cardSize.x * 0.5f) - (ICON_BUTTON_SIZE * 0.5f) - SIDE_INSET * 0.5f,
                (_cardSize.y * 0.5f) - HEADER_INSET * 0.5f);

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
        }
    }
}
