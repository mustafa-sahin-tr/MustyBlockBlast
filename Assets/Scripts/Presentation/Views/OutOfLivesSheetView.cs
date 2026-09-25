using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
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
    /// The out-of-lives sheet (issue #478): a modal card over a dimmed scrim with the player's lives in a
    /// big heart, the countdown to the next xx:00 refill, the refill rule, and a rewarded-ad offer of
    /// <see cref="LivesConfig.AdRewardAmount"/> lives. Opens on <see cref="OutOfLivesMessage"/> — a Path
    /// start refused at zero lives — and on a tap on the HUD's lives section (<see cref="LivesHudView"/>).
    /// <para>
    /// Opened from the HUD it may well find lives left, so it words itself from the count rather than
    /// from how it was opened: "OUT OF LIVES" and a greyed heart only at zero, "LIVES" otherwise. The
    /// countdown box is hidden at or above the cap (<see cref="LivesModel.SecondsUntilRefill"/> reads 0
    /// there — no refill is coming), and the ad button greys out and reads "Lives full", because
    /// <see cref="LivesSystem.RequestAdLivesAsync"/> would refuse it anyway.
    /// </para>
    /// <para>
    /// Binds to <see cref="LivesModel"/> reactively — the countdown ticks because
    /// <see cref="LivesSystem"/> rewrites the seconds once a second, and a granted ad repaints the heart
    /// because it rewrites the count — and repaints only while open. It holds no rule: whether the ad is
    /// on offer is <see cref="LivesSystem.CanRequestAdLives"/>'s answer.
    /// </para>
    /// <para>
    /// The offers sit in their own stack (<see cref="_offersRect"/>) that <see cref="LayoutCard"/> sizes
    /// from the buttons it holds, so the second offer — the gold coin lives pack of issue #479, under the
    /// ad — is one more button in that stack, not a re-layout of the card. A successful ad or pack leaves
    /// the sheet open on the new count: the player sees the lives land, and whatever it opened over — the
    /// level-start card, the end-of-run card — is still underneath for the start they were after.
    /// </para>
    /// <para>
    /// The pack (issue #479) is never capped, so it stays gold at 22 lives while the ad beside it reads
    /// "Lives full". It greys out while the coin balance is short of <see cref="CurrencySystem.LivesPackPrice"/>
    /// — the shop's unaffordable-row treatment — and repaints on every balance change, so earning the
    /// coins elsewhere lights it up. A tap on the grey button, or a purchase the System refuses, shows the
    /// sheet's message line rather than doing nothing silently. Whether the pack is bought is
    /// <see cref="CurrencySystem.TryPurchaseLivesPack"/>'s answer; this View only reads the price.
    /// </para>
    /// <para>
    /// Modal while open: <see cref="BoardInputView"/> routes every tap into <see cref="HandleTap"/>, above
    /// the level-start and end-of-run cards it opens over. It does not touch
    /// <see cref="TimerRunSystem.SetMenuPaused"/>: lives are Path-only, Path runs have no clock, and the
    /// single menu-pause flag may already be held by the level-start card beneath it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutOfLivesSheetView : MonoBehaviour
    {
        // Layout, in canvas reference pixels, in the level-start card's vocabulary (same width, padding,
        // corner and close cross), so the two read as one family when this one opens over the other.
        private const float CARD_WIDTH = 900f;
        private const float CARD_PADDING_TOP = 56f;
        private const float CARD_PADDING_BOTTOM = 48f;
        private const float CARD_RADIUS = 64f;
        private const float CARD_SHADOW_DROP = 26f;
        private const float SIDE_INSET = 50f;
        private const float CONTENT_WIDTH = CARD_WIDTH - (SIDE_INSET * 2f);
        private const float STACK_GAP = 24f;

        private const float HEART_SIZE = 190f;
        private const float TITLE_HEIGHT = 76f;
        private const float COUNTDOWN_HEIGHT = 176f;
        private const float COUNTDOWN_RADIUS = 36f;
        private const float COUNTDOWN_OUTLINE = 4f;
        private const float BODY_HEIGHT = 96f;
        private const float BUTTON_HEIGHT = 128f;
        private const float BUTTON_GAP = 18f;
        private const float BUTTON_SLICE_SCALE = 2.5f;
        private const float BUTTON_LABEL_RISE = 6f;
        private const float AD_CHIP_WIDTH = 118f;
        private const float AD_CHIP_HEIGHT = 56f;
        private const float AD_CHIP_GAP = 16f;
        private const float AD_CHIP_HEART_SIZE = 36f;
        private const float PACK_SIDE_INSET = 22f;
        private const float PACK_PILL_WIDTH = 150f;
        private const float PACK_PILL_HEIGHT = 64f;
        private const float PACK_PILL_HEART_SIZE = 40f;
        private const float PACK_PRICE_CHIP_WIDTH = 176f;
        private const float PACK_PRICE_CHIP_HEIGHT = 64f;
        private const float PACK_PRICE_COIN_SIZE = 42f;
        private const float MESSAGE_HEIGHT = 56f;
        private const float FOOTER_HEIGHT = 44f;
        private const float CLOSE_DISC_SIZE = 92f;
        private const float CLOSE_INSET = 36f;
        private const float CLOSE_BAR_THICKNESS = 9f;

        private const string PLUS = "+";

        /// <summary>The mockup's "▶" before "Watch ad". A symbol, not a word — kept out of the String Table
        /// so no translator can drop it.</summary>
        private const string PLAY_GLYPH_PREFIX = "\u25B6 ";

        private static readonly Color CardFace = new Color32(0xFD, 0xFC, 0xFB, 0xFF);
        private static readonly Color CardShadow = new Color32(0x2B, 0x26, 0x33, 0x3A);
        private static readonly Color TitleInk = new Color32(0xC8, 0x3A, 0x4B, 0xFF);
        private static readonly Color BodyInk = new Color32(0x5E, 0x57, 0x6B, 0xFF);
        private static readonly Color MutedInk = new Color32(0x8C, 0x84, 0x70, 0xFF);

        /// <summary>The mockup's lives pink (#FFEEF0 on #F8C9D0), shared with the level-start card's and
        /// the fail card's lives rows.</summary>
        private static readonly Color LivesFill = new Color32(0xFF, 0xEE, 0xF0, 0xFF);
        private static readonly Color LivesOutline = new Color32(0xF8, 0xC9, 0xD0, 0xFF);
        private static readonly Color LivesCaptionInk = new Color32(0xB0, 0x48, 0x5A, 0xFF);
        private static readonly Color LivesFigureInk = new Color32(0xC8, 0x3A, 0x4B, 0xFF);

        private static readonly Color AdBlue = new Color32(0x3F, 0x86, 0xEE, 0xFF);

        /// <summary>The mockup's chunky gold lives-pack button, and the dark gold ink of its price chip
        /// (the level-start card's milestone ink).</summary>
        private static readonly Color PackGold = new Color32(0xF5, 0xB3, 0x1B, 0xFF);
        private static readonly Color PackPillFill = new Color(0.55f, 0.3f, 0f, 0.28f);
        private static readonly Color PackPriceChipFill = Color.white;
        private static readonly Color PackPriceInk = new Color32(0x8A, 0x61, 0x00, 0xFF);
        private static readonly Color DisabledGrey = new Color32(0xC9, 0xC4, 0xD2, 0xFF);
        private static readonly Color ChipFill = new Color(1f, 1f, 1f, 0.24f);
        private static readonly Color TextShadowColour = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Vector2 TextShadowOffset = new Vector2(0f, -4f);
        private static readonly Color CloseDisc = new Color32(0xF1, 0xEE, 0xE8, 0xFF);
        private static readonly Color CloseCross = new Color32(0x8A, 0x84, 0x96, 0xFF);

        /// <summary>The heart at zero lives: the full-colour art washed out toward the card, which is the
        /// mockup's "grey heart" without a second sprite.</summary>
        private static readonly Color EmptyHeartTint = new Color(0.78f, 0.76f, 0.8f, 0.55f);

        /// <summary>A deep red outline round the white count, as <see cref="LivesHudView"/> draws it.</summary>
        private static readonly Color CountOutlineColour = new Color(0.45f, 0.05f, 0.08f, 0.9f);
        private static readonly Color EmptyCountOutlineColour = new Color(0.35f, 0.33f, 0.4f, 0.9f);
        private static readonly Vector2 CountOutlineDistance = new Vector2(3f, -3f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        [Header("Type")]
        [Tooltip("Display face (Bowlby One SC): title, heart count and countdown.")]
        [SerializeField] private Font _displayFont;
        [Tooltip("Body face (Baloo 2 ExtraBold): captions, copy and the button.")]
        [SerializeField] private Font _bodyFont;
        [SerializeField] private int _titleFontSize = 64;
        [SerializeField] private int _heartCountFontSize = 72;
        [SerializeField] private int _countdownFontSize = 84;
        [SerializeField] private int _bodyFontSize = 32;
        [SerializeField] private int _buttonFontSize = 40;

        [Header("Sprites")]
        [Tooltip("White 9-sliced glossy button with a darker bottom lip (the shop's). Tinted at runtime.")]
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("Full-colour heart (HudIcon_Heart), the HUD lives section's own.")]
        [SerializeField] private Sprite _heartSprite;
        [Tooltip("The coin icon on the lives pack's price chip — the shop's and the level-start card's own.")]
        [SerializeField] private Sprite _coinSprite;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.08f, 0.09f, 0.16f, 0.58f);

        private LivesModel _livesModel;
        private LivesConfig _livesConfig;
        private LivesSystem _livesSystem;
        private ProfileModel _profileModel;
        private CurrencySystem _currencySystem;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ISubscriber<OutOfLivesMessage> _outOfLivesSubscriber;

        private Canvas _canvas;
        private CancellationToken _destroyToken;
        private GameObject _panel;
        private RectTransform _cardRect;
        private RectTransform _cardShadowRect;
        private RectTransform _closeButtonRect;

        private RectTransform _heartRect;
        private Image _heartImage;
        private Text _heartCountText;
        private Outline _heartCountOutline;
        private Text _titleText;

        private RectTransform _countdownRect;
        private Text _countdownCaptionText;
        private Text _countdownFigureText;

        private Text _bodyText;

        private RectTransform _offersRect;
        private RectTransform _adButtonRect;
        private Image _adButtonPlate;
        private Text _adButtonText;
        private RectTransform _adChipRect;
        private Text _adChipText;

        private RectTransform _packButtonRect;
        private Image _packButtonPlate;
        private Text _packButtonText;
        private Text _packAmountText;
        private Text _packPriceText;

        private RectTransform _messageRect;
        private Text _messageText;
        private Text _footerText;

        /// <summary>A refusal to show under the offers, or null. Cleared on open.</summary>
        private string _messageOverride;

        /// <summary>True from a "watch ad" tap until the System answers, so a second tap cannot queue a
        /// second ad. The System refuses one too; this keeps that refusal from being shown as a decline.</summary>
        private bool _isRequestingAd;

        [Inject]
        public void Construct(
            LivesModel livesModel,
            LivesConfig livesConfig,
            LivesSystem livesSystem,
            ProfileModel profileModel,
            CurrencySystem currencySystem,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            ISubscriber<OutOfLivesMessage> outOfLivesSubscriber)
        {
            _livesModel = livesModel;
            _livesConfig = livesConfig;
            _livesSystem = livesSystem;
            _profileModel = profileModel;
            _currencySystem = currencySystem;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _outOfLivesSubscriber = outOfLivesSubscriber;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            _destroyToken = this.GetCancellationTokenOnDestroy();
            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_livesModel == null || _livesConfig == null || _livesSystem == null
                || _profileModel == null || _currencySystem == null || _localizationModel == null
                || _localizationSystem == null || _outOfLivesSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(OutOfLivesSheetView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _outOfLivesSubscriber.Subscribe(OnOutOfLives).AddTo(_disposables);
            _livesModel.CurrentLives.Subscribe(_ => RefreshIfOpen()).AddTo(_disposables);
            _livesModel.SecondsUntilRefill.Subscribe(OnSecondsUntilRefillChanged).AddTo(_disposables);
            _profileModel.CoinBalance.Subscribe(_ => RefreshIfOpen()).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(_ => RefreshIfOpen()).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the sheet is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the sheet on top of whatever is up. Called on <see cref="OutOfLivesMessage"/> and by
        /// <see cref="BoardInputView"/> for a tap on the HUD's lives section. A second open while showing
        /// is a no-op.
        /// </summary>
        internal void Open()
        {
            if (_panel == null || IsOpen || _livesModel == null)
            {
                return;
            }

            _messageOverride = null;
            _panel.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
        }

        /// <summary>
        /// Routes a tap while open: the close cross, the ad button, the lives pack; anything else on the card is
        /// swallowed, and only a tap on the scrim closes.
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

            if (Contains(_closeButtonRect, screenPosition, eventCamera))
            {
                Close();
                return;
            }

            if (Contains(_adButtonRect, screenPosition, eventCamera))
            {
                RequestAd();
                return;
            }

            if (Contains(_packButtonRect, screenPosition, eventCamera))
            {
                BuyLivesPack();
                return;
            }

            if (Contains(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        private static bool Contains(RectTransform rect, Vector2 screenPosition, Camera eventCamera)
            => RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, eventCamera);

        private void Close() => _panel.SetActive(false);

        private void OnOutOfLives(OutOfLivesMessage message) => Open();

        private void OnSecondsUntilRefillChanged(int secondsUntilRefill) => RefreshIfOpen();

        private void RefreshIfOpen()
        {
            if (IsOpen)
            {
                Refresh();
            }
        }

        /// <summary>Fire-and-forget ad request. The System banks and persists; the lives subscription
        /// repaints. A disabled button (at the cap, or mid-request) does nothing.</summary>
        private void RequestAd()
        {
            if (_isRequestingAd || !_livesSystem.CanRequestAdLives)
            {
                return;
            }

            RequestAdAsync(_destroyToken).Forget();
        }

        private async UniTaskVoid RequestAdAsync(CancellationToken cancellationToken)
        {
            _isRequestingAd = true;
            _messageOverride = null;
            Refresh();

            bool granted = false;
            try
            {
                granted = await _livesSystem.RequestAdLivesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The View went away mid-request. The System only banks a reward it was handed.
                return;
            }
            catch (Exception exception)
            {
                // The ad SDK's failure is not the player's problem: logged, and read as "no ad".
                Debug.LogException(exception, this);
            }
            finally
            {
                _isRequestingAd = false;
            }

            if (!granted)
            {
                _messageOverride = _localizationSystem.Translate(LocalizationKeys.LIVES_SHEET_AD_REFUSED);
            }

            RefreshIfOpen();
        }

        /// <summary>
        /// The gold lives pack (issue #479). Short of coins — the grey button — it only says so; otherwise
        /// the System debits, grants and flushes, and the lives and balance subscriptions repaint the
        /// sheet on the new count. Refused mid-way (the balance moved under the tap), it says so too.
        /// </summary>
        private void BuyLivesPack()
        {
            if (_isRequestingAd)
            {
                return;
            }

            bool bought = IsPackAffordable() && _currencySystem.TryPurchaseLivesPack();
            _messageOverride = bought
                ? null
                : _localizationSystem.Translate(LocalizationKeys.LIVES_SHEET_PACK_REFUSED);
            Refresh();
        }

        private bool IsPackAffordable() => _currencySystem.LivesPackPrice <= _profileModel.CoinBalance.Value;

        /// <summary>Repaints every row from the model and re-stacks the card. Runs on open and on every
        /// lives, countdown or locale change while open — never per frame.</summary>
        private void Refresh()
        {
            if (_panel == null || _livesModel == null)
            {
                return;
            }

            int lives = _livesModel.CurrentLives.Value;
            bool isOut = lives <= 0;

            _stringBuilder.Clear();
            _stringBuilder.Append(lives);
            _heartCountText.text = _stringBuilder.ToString();
            _heartImage.color = _heartSprite == null ? Color.clear : (isOut ? EmptyHeartTint : Color.white);
            _heartCountOutline.effectColor = isOut ? EmptyCountOutlineColour : CountOutlineColour;

            _titleText.text = _localizationSystem.Translate(
                isOut ? LocalizationKeys.LIVES_SHEET_TITLE_OUT : LocalizationKeys.LIVES_SHEET_TITLE);

            string refillAmount = FormatCount(_livesConfig.RefillAmount);
            PaintCountdown(refillAmount);

            _bodyText.text = _localizationSystem.Format(
                LocalizationKeys.LIVES_SHEET_BODY, refillAmount, FormatCount(_livesConfig.RegenCap));

            PaintAdButton();
            PaintPackButton();

            _messageText.text = _messageOverride;
            _messageRect.gameObject.SetActive(!string.IsNullOrEmpty(_messageOverride));
            _footerText.text = _localizationSystem.Translate(LocalizationKeys.LIVES_SHEET_FOOTER);

            LayoutCard();
        }

        /// <summary>The "NEXT +5 LIVES IN / 23:14" box, or nothing at or above the cap — the model's 0.
        /// A zero-padded clock, not a String Table entry, for the reason <see cref="LivesHudView"/>'s is not.</summary>
        private void PaintCountdown(string refillAmount)
        {
            int seconds = _livesModel.SecondsUntilRefill.Value;
            bool show = seconds > 0;
            _countdownRect.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            _countdownCaptionText.text = _localizationSystem.Format(LocalizationKeys.LIVES_SHEET_NEXT_REFILL, refillAmount);
            _countdownFigureText.text = FormatClock(seconds);
        }

        /// <summary>Blue with the "+3 ♥" chip while an ad would pay; grey, chipless and "Lives full" at or
        /// above the cap. Greyed without a caption change while a request is in flight.</summary>
        private void PaintAdButton()
        {
            bool isFull = _livesModel.CurrentLives.Value >= _livesConfig.RegenCap;
            bool isEnabled = !isFull && !_isRequestingAd && _livesSystem.CanRequestAdLives;

            _adButtonPlate.color = isEnabled ? AdBlue : DisabledGrey;
            _adChipRect.gameObject.SetActive(!isFull);
            _stringBuilder.Clear();
            if (isFull)
            {
                _stringBuilder.Append(_localizationSystem.Translate(LocalizationKeys.LIVES_SHEET_FULL));
            }
            else
            {
                _stringBuilder.Append(PLAY_GLYPH_PREFIX);
                _stringBuilder.Append(_localizationSystem.Translate(LocalizationKeys.LIVES_SHEET_WATCH_AD));
            }

            _adButtonText.text = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append(PLUS);
            _stringBuilder.Append(_livesConfig.AdRewardAmount);
            _adChipText.text = _stringBuilder.ToString();

            LayoutAdButtonContent(!isFull);
        }

        /// <summary>Gold while the balance covers the price, grey (the shop's unaffordable treatment) while
        /// it does not, and while an ad is in flight. Never greyed by the cap: the pack is uncapped.</summary>
        private void PaintPackButton()
        {
            bool isEnabled = !_isRequestingAd && IsPackAffordable();
            _packButtonPlate.color = isEnabled ? PackGold : DisabledGrey;
            _packButtonText.text = _localizationSystem.Translate(LocalizationKeys.LIVES_SHEET_PACK);

            _stringBuilder.Clear();
            _stringBuilder.Append(PLUS);
            _stringBuilder.Append(_livesConfig.LivesPackAmount);
            _packAmountText.text = _stringBuilder.ToString();

            _packPriceText.text = FormatCount(_currencySystem.LivesPackPrice);
        }

        /// <summary>Centres the caption and, when shown, its "+N ♥" chip as one group.</summary>
        private void LayoutAdButtonContent(bool withChip)
        {
            float labelWidth = _adButtonText.preferredWidth;
            float groupWidth = withChip ? labelWidth + AD_CHIP_GAP + AD_CHIP_WIDTH : labelWidth;
            RectTransform labelRect = _adButtonText.rectTransform;
            labelRect.sizeDelta = new Vector2(labelWidth, BUTTON_HEIGHT);
            labelRect.anchoredPosition = new Vector2((-groupWidth * 0.5f) + (labelWidth * 0.5f), BUTTON_LABEL_RISE);
            _adChipRect.anchoredPosition = new Vector2((groupWidth * 0.5f) - (AD_CHIP_WIDTH * 0.5f), BUTTON_LABEL_RISE);
        }

        private string FormatCount(int value)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(value);
            return _stringBuilder.ToString();
        }

        private string FormatClock(int totalSeconds)
        {
            _stringBuilder.Clear();
            AppendPadded(totalSeconds / 60);
            _stringBuilder.Append(':');
            AppendPadded(totalSeconds % 60);
            return _stringBuilder.ToString();
        }

        private void AppendPadded(int value)
        {
            if (value < 10)
            {
                _stringBuilder.Append('0');
            }

            _stringBuilder.Append(value);
        }

        /// <summary>
        /// Stacks the showing rows top to bottom and sizes the card around them, measure then place, as
        /// the level-start card does. The offers stack is sized from its active buttons first.
        /// </summary>
        private void LayoutCard()
        {
            LayoutOffers();

            float contentHeight = StackRows(0f, false);
            float cardHeight = CARD_PADDING_TOP + contentHeight + CARD_PADDING_BOTTOM;
            float top = cardHeight * 0.5f;
            StackRows(top - CARD_PADDING_TOP, true);

            var cardSize = new Vector2(CARD_WIDTH, cardHeight);
            _cardRect.sizeDelta = cardSize;
            _cardShadowRect.sizeDelta = cardSize;
            _cardShadowRect.anchoredPosition = new Vector2(0f, -CARD_SHADOW_DROP);

            _closeButtonRect.anchoredPosition = new Vector2(
                (CARD_WIDTH * 0.5f) - CLOSE_INSET - (CLOSE_DISC_SIZE * 0.5f),
                top - CLOSE_INSET - (CLOSE_DISC_SIZE * 0.5f));
        }

        /// <summary>Stacks the offer buttons inside <see cref="_offersRect"/> and gives it their height.
        /// Written over every child so the coin-pack button (issue #479) needs no change here.</summary>
        private void LayoutOffers()
        {
            int shown = 0;
            for (int childIndex = 0; childIndex < _offersRect.childCount; childIndex++)
            {
                if (_offersRect.GetChild(childIndex).gameObject.activeSelf)
                {
                    shown++;
                }
            }

            float height = (shown * BUTTON_HEIGHT) + (Mathf.Max(0, shown - 1) * BUTTON_GAP);
            _offersRect.sizeDelta = new Vector2(CONTENT_WIDTH, height);

            float cursor = height * 0.5f;
            for (int childIndex = 0; childIndex < _offersRect.childCount; childIndex++)
            {
                var child = (RectTransform)_offersRect.GetChild(childIndex);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                child.anchoredPosition = new Vector2(0f, cursor - (BUTTON_HEIGHT * 0.5f));
                cursor -= BUTTON_HEIGHT + BUTTON_GAP;
            }
        }

        private float StackRows(float top, bool apply)
        {
            float cursor = 0f;
            cursor = PlaceRow(_heartRect, HEART_SIZE, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_titleText.rectTransform, TITLE_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_countdownRect, COUNTDOWN_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_bodyText.rectTransform, BODY_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_offersRect, _offersRect.sizeDelta.y, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_messageRect, MESSAGE_HEIGHT, cursor, top, apply, STACK_GAP * 0.5f);
            cursor = PlaceRow(_footerText.rectTransform, FOOTER_HEIGHT, cursor, top, apply, STACK_GAP);
            return cursor;
        }

        /// <summary>Places one row below the cursor, <paramref name="gap"/> after the previous one, and
        /// returns the new cursor. Inactive rows take no space.</summary>
        private static float PlaceRow(RectTransform row, float height, float cursor, float top, bool apply, float gap)
        {
            if (!row.gameObject.activeSelf)
            {
                return cursor;
            }

            if (cursor > 0f)
            {
                cursor += gap;
            }

            if (apply)
            {
                row.anchoredPosition = new Vector2(row.anchoredPosition.x, top - cursor - (height * 0.5f));
            }

            return cursor + height;
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            var panelObject = new GameObject("OutOfLivesPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            Image cardShadow = HudChrome.BuildRounded(
                panelRect, "OutOfLivesCardShadow", new Vector2(CARD_WIDTH, 1f), Vector2.zero, CARD_RADIUS);
            cardShadow.color = CardShadow;
            _cardShadowRect = cardShadow.rectTransform;

            Image card = HudChrome.BuildRounded(
                panelRect, "OutOfLivesCard", new Vector2(CARD_WIDTH, 1f), Vector2.zero, CARD_RADIUS);
            card.color = CardFace;
            _cardRect = card.rectTransform;

            BuildHeart();

            _titleText = CreateText(_cardRect, "Title", _titleFontSize, _displayFont, TitleInk);
            _titleText.rectTransform.sizeDelta = new Vector2(CONTENT_WIDTH - CLOSE_DISC_SIZE, TITLE_HEIGHT);
            _titleText.resizeTextForBestFit = true;
            _titleText.resizeTextMinSize = 36;
            _titleText.resizeTextMaxSize = _titleFontSize;
            _titleText.horizontalOverflow = HorizontalWrapMode.Wrap;

            BuildCountdown();

            _bodyText = CreateText(_cardRect, "Body", _bodyFontSize - 2, _bodyFont, BodyInk);
            _bodyText.rectTransform.sizeDelta = new Vector2(CONTENT_WIDTH, BODY_HEIGHT);
            _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bodyText.lineSpacing = 0.9f;

            _offersRect = HudChrome.CreateRect(_cardRect, "Offers", new Vector2(CONTENT_WIDTH, BUTTON_HEIGHT), Vector2.zero);
            BuildAdButton();
            BuildPackButton();

            _messageText = CreateText(_cardRect, "Message", _bodyFontSize - 6, _bodyFont, MutedInk);
            _messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _messageRect = _messageText.rectTransform;
            _messageRect.sizeDelta = new Vector2(CONTENT_WIDTH, MESSAGE_HEIGHT);

            _footerText = CreateText(_cardRect, "Footer", _bodyFontSize - 8, _bodyFont, MutedInk);
            _footerText.rectTransform.sizeDelta = new Vector2(CONTENT_WIDTH, FOOTER_HEIGHT);
            _footerText.horizontalOverflow = HorizontalWrapMode.Wrap;

            BuildCloseButton();

            _panel = panelObject;
        }

        /// <summary>The big heart with the count inside it, drawn as <see cref="LivesHudView"/> draws its
        /// small one: the full-colour art, a white figure with a deep red outline, nudged up a touch.</summary>
        private void BuildHeart()
        {
            var heartSize = new Vector2(HEART_SIZE, HEART_SIZE);
            _heartRect = HudChrome.CreateRect(_cardRect, "Heart", heartSize, Vector2.zero);
            _heartImage = HudChrome.BuildGlyph(_heartRect, "Icon", _heartSprite, heartSize, Vector2.zero);
            _heartImage.preserveAspect = true;

            _heartCountText = CreateText(_heartRect, "Count", _heartCountFontSize, _displayFont, Color.white);
            _heartCountText.rectTransform.sizeDelta = heartSize;
            _heartCountText.rectTransform.anchoredPosition = new Vector2(0f, HEART_SIZE * 0.06f);
            _heartCountOutline = _heartCountText.gameObject.AddComponent<Outline>();
            _heartCountOutline.effectColor = CountOutlineColour;
            _heartCountOutline.effectDistance = CountOutlineDistance;
        }

        /// <summary>The pink "NEXT +5 LIVES IN" box with the big mm:ss under its caption.</summary>
        private void BuildCountdown()
        {
            var size = new Vector2(CONTENT_WIDTH, COUNTDOWN_HEIGHT);
            _countdownRect = HudChrome.CreateRect(_cardRect, "Countdown", size, Vector2.zero);
            HudChrome.BuildRounded(_countdownRect, "Fill", size, Vector2.zero, COUNTDOWN_RADIUS).color = LivesFill;
            HudChrome.BuildOutline(_countdownRect, "Outline", size, Vector2.zero, COUNTDOWN_RADIUS, COUNTDOWN_OUTLINE)
                .color = LivesOutline;

            _countdownCaptionText = CreateText(_countdownRect, "Caption", _bodyFontSize - 6, _bodyFont, LivesCaptionInk);
            _countdownCaptionText.rectTransform.anchoredPosition = new Vector2(0f, 46f);

            _countdownFigureText = CreateText(_countdownRect, "Figure", _countdownFontSize, _displayFont, LivesFigureInk);
            _countdownFigureText.rectTransform.anchoredPosition = new Vector2(0f, -20f);
        }

        /// <summary>The blue "Watch ad [+3 ♥]" button, first child of the offers stack.</summary>
        private void BuildAdButton()
        {
            var size = new Vector2(CONTENT_WIDTH, BUTTON_HEIGHT);
            _adButtonRect = HudChrome.CreateRect(_offersRect, "WatchAdButton", size, Vector2.zero);
            _adButtonPlate = BuildSlicedPlate(_adButtonRect, "Plate", _buttonSprite, size, BUTTON_SLICE_SCALE);
            _adButtonPlate.color = AdBlue;

            _adButtonText = CreateText(_adButtonRect, "Caption", _buttonFontSize, _bodyFont, Color.white);
            AddTextShadow(_adButtonText);

            var chipSize = new Vector2(AD_CHIP_WIDTH, AD_CHIP_HEIGHT);
            _adChipRect = HudChrome.CreateRect(_adButtonRect, "Chip", chipSize, Vector2.zero);
            HudChrome.BuildRounded(_adChipRect, "Plate", chipSize, Vector2.zero, AD_CHIP_HEIGHT * 0.5f).color = ChipFill;
            _adChipText = CreateText(_adChipRect, "Amount", _bodyFontSize, _bodyFont, Color.white);
            _adChipText.rectTransform.anchoredPosition = new Vector2(-18f, 2f);
            AddTextShadow(_adChipText);
            Image chipHeart = HudChrome.BuildGlyph(
                _adChipRect, "Heart", _heartSprite, new Vector2(AD_CHIP_HEART_SIZE, AD_CHIP_HEART_SIZE), new Vector2(30f, 0f));
            chipHeart.preserveAspect = true;
            chipHeart.color = _heartSprite != null ? Color.white : Color.clear;
        }

        /// <summary>
        /// The gold "[♥ +10] Lives pack [coin 150]" button (issue #479), second child of the offers stack:
        /// the lives pill hugs the left edge, the white price chip the right, and the caption sits centred
        /// between them.
        /// </summary>
        private void BuildPackButton()
        {
            var size = new Vector2(CONTENT_WIDTH, BUTTON_HEIGHT);
            _packButtonRect = HudChrome.CreateRect(_offersRect, "LivesPackButton", size, Vector2.zero);
            _packButtonPlate = BuildSlicedPlate(_packButtonRect, "Plate", _buttonSprite, size, BUTTON_SLICE_SCALE);
            _packButtonPlate.color = PackGold;

            float halfWidth = CONTENT_WIDTH * 0.5f;

            var pillSize = new Vector2(PACK_PILL_WIDTH, PACK_PILL_HEIGHT);
            var pillCentre = new Vector2(-halfWidth + PACK_SIDE_INSET + (PACK_PILL_WIDTH * 0.5f), BUTTON_LABEL_RISE);
            RectTransform pillRect = HudChrome.CreateRect(_packButtonRect, "LivesPill", pillSize, pillCentre);
            HudChrome.BuildRounded(pillRect, "Plate", pillSize, Vector2.zero, PACK_PILL_HEIGHT * 0.5f).color =
                PackPillFill;
            Image pillHeart = HudChrome.BuildGlyph(
                pillRect, "Heart", _heartSprite, new Vector2(PACK_PILL_HEART_SIZE, PACK_PILL_HEART_SIZE),
                new Vector2((-PACK_PILL_WIDTH * 0.5f) + 12f + (PACK_PILL_HEART_SIZE * 0.5f), 0f));
            pillHeart.preserveAspect = true;
            pillHeart.color = _heartSprite != null ? Color.white : Color.clear;
            _packAmountText = CreateText(pillRect, "Amount", _bodyFontSize, _bodyFont, Color.white);
            _packAmountText.rectTransform.sizeDelta =
                new Vector2(PACK_PILL_WIDTH - PACK_PILL_HEART_SIZE - 16f, PACK_PILL_HEIGHT);
            _packAmountText.rectTransform.anchoredPosition = new Vector2(PACK_PILL_HEART_SIZE * 0.5f, 2f);
            AddTextShadow(_packAmountText);

            var chipSize = new Vector2(PACK_PRICE_CHIP_WIDTH, PACK_PRICE_CHIP_HEIGHT);
            var chipCentre = new Vector2(
                halfWidth - PACK_SIDE_INSET - (PACK_PRICE_CHIP_WIDTH * 0.5f), BUTTON_LABEL_RISE);
            RectTransform chipRect = HudChrome.CreateRect(_packButtonRect, "PriceChip", chipSize, chipCentre);
            HudChrome.BuildRounded(chipRect, "Plate", chipSize, Vector2.zero, PACK_PRICE_CHIP_HEIGHT * 0.5f).color =
                PackPriceChipFill;
            Image coin = HudChrome.BuildGlyph(
                chipRect, "Coin", _coinSprite, new Vector2(PACK_PRICE_COIN_SIZE, PACK_PRICE_COIN_SIZE),
                new Vector2((-PACK_PRICE_CHIP_WIDTH * 0.5f) + 12f + (PACK_PRICE_COIN_SIZE * 0.5f), 0f));
            coin.preserveAspect = true;
            coin.color = _coinSprite != null ? Color.white : Color.clear;
            _packPriceText = CreateText(chipRect, "Price", _bodyFontSize, _bodyFont, PackPriceInk);
            _packPriceText.rectTransform.sizeDelta =
                new Vector2(PACK_PRICE_CHIP_WIDTH - PACK_PRICE_COIN_SIZE - 16f, PACK_PRICE_CHIP_HEIGHT);
            _packPriceText.rectTransform.anchoredPosition = new Vector2(PACK_PRICE_COIN_SIZE * 0.5f, 2f);

            float captionWidth =
                CONTENT_WIDTH - (2f * (PACK_SIDE_INSET + Mathf.Max(PACK_PILL_WIDTH, PACK_PRICE_CHIP_WIDTH)));
            _packButtonText = CreateText(_packButtonRect, "Caption", _buttonFontSize, _bodyFont, Color.white);
            _packButtonText.rectTransform.sizeDelta = new Vector2(captionWidth, BUTTON_HEIGHT);
            _packButtonText.rectTransform.anchoredPosition = new Vector2(0f, BUTTON_LABEL_RISE);
            _packButtonText.resizeTextForBestFit = true;
            _packButtonText.resizeTextMinSize = 24;
            _packButtonText.resizeTextMaxSize = _buttonFontSize;
            AddTextShadow(_packButtonText);
        }

        /// <summary>The close cross on a soft disc, drawn as two rotated bars so it needs no glyph asset.</summary>
        private void BuildCloseButton()
        {
            _closeButtonRect = HudChrome.CreateRect(
                _cardRect, "CloseButton", new Vector2(CLOSE_DISC_SIZE, CLOSE_DISC_SIZE), Vector2.zero);
            HudChrome.BuildCircle(_closeButtonRect, "Disc", CLOSE_DISC_SIZE, Vector2.zero).color = CloseDisc;

            for (int barIndex = 0; barIndex < 2; barIndex++)
            {
                Image bar = HudChrome.BuildRounded(
                    _closeButtonRect, "Bar", new Vector2(CLOSE_DISC_SIZE * 0.4f, CLOSE_BAR_THICKNESS),
                    Vector2.zero, CLOSE_BAR_THICKNESS * 0.5f);
                bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, barIndex == 0 ? 45f : -45f);
                bar.color = CloseCross;
            }
        }

        /// <summary>A white glossy sprite, 9-sliced to <paramref name="size"/>; the caller tints it. Falls
        /// back to a plain rounded plate if the sprite is not assigned.</summary>
        private static Image BuildSlicedPlate(
            RectTransform parent, string objectName, Sprite sprite, Vector2 size, float sliceScale)
        {
            if (sprite == null)
            {
                return HudChrome.BuildRounded(parent, objectName, size, Vector2.zero, size.y * 0.25f);
            }

            var plateObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(parent, false);
            HudChrome.Centre(plateRect, size);

            var plate = plateObject.GetComponent<Image>();
            plate.sprite = sprite;
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = sliceScale;
            plate.raycastTarget = false;
            return plate;
        }

        private static Text CreateText(RectTransform parent, string objectName, int fontSize, Font font, Color colour)
            => UiTextFactory.Create(parent, objectName, fontSize, FontStyle.Normal, colour, font);

        /// <summary>The mockup's dark text-shadow under white captions.</summary>
        private static void AddTextShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = TextShadowColour;
            shadow.effectDistance = TextShadowOffset;
        }
    }
}
