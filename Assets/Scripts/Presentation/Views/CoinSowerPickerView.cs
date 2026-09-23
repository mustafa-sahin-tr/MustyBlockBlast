using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    /// The level-start screen for the <see cref="PowerUpKind.CoinSower"/> power-up: how many of the
    /// player's banked charges to sow into the level about to be played as extra coin cells, a way to
    /// bank more without leaving the card, and the tap that commits and starts it.
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
    /// it was: it is a mode switch rather than a discrete "play this level" tap, and putting a prompt in
    /// front of it would make changing mode a decision about power-ups.
    /// </para>
    /// <para>
    /// Coin Sower charges are earned the way <see cref="PowerUpKind.Hold"/>'s are — a rewarded ad,
    /// through <see cref="PowerUpSystem.GrantRewardAsync"/> — except that one ad banks two (issue #404).
    /// The "watch ad" button on this card is that same earn seam placed where the charges are spent, so
    /// a player who opens the card with none, or with fewer than they want, can top up and commit
    /// without going anywhere. The button banks; it never sows. The count subscription bound in
    /// <see cref="Start"/> repaints the offer from the bank, exactly as <see cref="HoldSlotView"/>
    /// repaints its badge.
    /// </para>
    /// <para>
    /// The quantity picker is a stepper like the shop's Coins tab uses, and for the same reason: the
    /// pending figure is a picker position and nothing else, so no System and no Model has any use for
    /// it until it is committed. It is clamped live to both bounds that matter — the configured ceiling
    /// and how many charges are actually banked — so the card can never offer a quantity the spend would
    /// refuse.
    /// </para>
    /// <para>
    /// Committing is one System call and a start: spend the charges out of the inventory through
    /// <see cref="PowerUpSystem.TrySpendCoinSowerBulk"/>, hand the quantity to
    /// <see cref="LevelCoinCellSeedSystem"/>, then start the level. No coins move at any point — a sow
    /// costs banked charges and nothing else.
    /// </para>
    /// <para>
    /// Zero is a first-class answer: it skips the spend entirely and simply starts the level, so
    /// declining costs nothing. Dismissing the card instead — a tap on the scrim, or the close cross —
    /// starts nothing at all, which is the way back out of a node tapped by accident.
    /// </para>
    /// <para>
    /// While it is open it is modal and swallows every tap, and it holds the run's clock through
    /// <see cref="TimerRunSystem.SetMenuPaused"/> — see the gate chain in <see cref="BoardInputView"/>,
    /// which is what keeps it mutually exclusive with the other overlays so that flag can never have two
    /// owners.
    /// </para>
    /// <para>
    /// Before Level unlock (issue #427): the picker is swapped for a dedicated locked panel rather than
    /// left on screen showing a quantity of zero next to a still-live Watch Ad button — the two read as
    /// contradictory ("locked" beside a working spend control). The panel explains that charges earned
    /// now stay banked for when the gate opens; Watch Ad and Start Level stay exactly as capable as they
    /// were, since only the sow itself is gated (see <see cref="IsLocked"/>).
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

        /// <summary>
        /// Charges one rewarded ad banks. Two rather than Hold's one because a single coin cell is a
        /// small nudge and the picker exists to sow several; the figure lives here because this card is
        /// the only caller that asks for it, and the caption below promises it.
        /// </summary>
        private const int CHARGES_PER_AD = 2;

        // Layout, in canvas reference pixels, matching the other cards so they all read as one family.
        private const float HEADER_Y = 300f;
        private const float LEVEL_Y = 224f;
        private const float CHARGES_Y = 158f;
        private const float WATCH_AD_BUTTON_Y = 96f;
        private const float QUANTITY_ROW_Y = 0f;
        private const float QUANTITY_STEPPER_X = 250f;
        private const float START_BUTTON_Y = -190f;
        private const float MESSAGE_Y = -290f;
        private const float STEPPER_SIZE = 96f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float SIDE_INSET = 60f;
        private const float HEADER_INSET = 84f;

        // Locked-panel layout: shares the vertical band between the Watch Ad and Start buttons that the
        // picker occupies when unlocked (see the class summary's issue #427 paragraph).
        private const float LOCK_BADGE_Y = 26f;
        private const float LOCK_TITLE_Y = -30f;
        private const float LOCK_SUB_Y = -82f;
        private static readonly Vector2 LockBadgeSize = new Vector2(150f, 40f);
        private static readonly Vector2 LockPanelWidth = new Vector2(720f, 1f);

        private static readonly Vector2 WideButtonSize = new Vector2(620f, 100f);

        // Plain strings, not String Table keys, for the reason PowerUpShopView states: LocalizationKeys
        // has no currency section yet, and adding keys with no translations behind them would render
        // the keys themselves. Tracked for a follow-up.
        private const string HEADER_TEXT = "COIN SOWER";
        private const string LEVEL_PREFIX_TEXT = "Level ";
        private const string CHARGES_PREFIX_TEXT = "Charges: ";
        private const string BANKED_PREFIX_TEXT = "Banked: ";
        private const string WATCH_AD_BUTTON_TEXT = "WATCH AD (+2)";
        private const string START_BUTTON_TEXT = "START LEVEL";
        private const string OPENING_MESSAGE = "Sow coin cells into this level, or start with none.";
        private const string LOCKED_BADGE_TEXT = "LOCKED";
        private const string LOCKED_TITLE_PREFIX = "Unlocks at Level ";
        private const string LOCKED_SUBTEXT = "Charges you earn now stay banked until then.";
        private const string LOCKED_FOOTER_MESSAGE = "Starting now plays this level with no coin cells sown.";
        private const string NO_CHARGES_MESSAGE = "No Coin Sower charges yet — watch an ad to earn some.";
        private const string SOW_FAILED_MESSAGE =
            "Those coin cells could not be sown. Your charges are still banked.";
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

        private PowerUpModel _powerUpModel;
        private LevelProgressionModel _levelProgressionModel;
        private SettingsModel _settingsModel;
        private PowerUpSystem _powerUpSystem;
        private LevelProgressionSystem _levelProgressionSystem;
        private LevelCoinCellSeedSystem _coinCellSeedSystem;
        private TimerRunSystem _timerRunSystem;
        private PowerUpPriceConfig _priceConfig;

        private Canvas _canvas;
        private CancellationToken _destroyToken;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private RectTransform _closeButtonRect;
        private Image _closeBarA;
        private Image _closeBarB;

        private Text _headerText;
        private Text _levelText;
        private Image _levelPlate;
        private Text _chargesText;
        private Image _chargesPlate;
        private Image _chargesIcon;
        private Text _quantityText;
        private Text _messageText;
        private Text _minusText;
        private Text _plusText;
        private Text _watchAdText;
        private Text _startButtonText;

        private RectTransform _minusRect;
        private RectTransform _plusRect;
        private RectTransform _quantityRect;
        private RectTransform _watchAdButtonRect;
        private RectTransform _startButtonRect;
        private Image _minusPlate;
        private Image _minusShadow;
        private Image _plusPlate;
        private Image _plusShadow;
        private Image _watchAdPlate;
        private Image _watchAdShadow;
        private Image _startButtonPlate;
        private Image _startButtonShadow;

        // The picker and the locked panel occupy the same slot on the card and are never shown together
        // — see the class summary's issue #427 paragraph. Exactly one is active at a time, toggled from
        // Refresh().
        private GameObject _pickerGroup;
        private GameObject _lockedGroup;
        private Image _lockedBadgePlate;
        private Text _lockedBadgeText;
        private Text _lockedTitleText;
        private Text _lockedSubText;

        private ThemeDefinition _currentTheme;

        /// <summary>
        /// The level this card was opened for. The whole of the "a level is about to start" state, and it
        /// lives here rather than in a Model on purpose: nothing outside this card has any use for a
        /// level the player has not committed to starting, and the moment they do commit the number
        /// leaves as an argument to <see cref="LevelProgressionSystem.TryStartPathLevel"/>.
        /// </summary>
        private int _pendingLevelNumber;

        /// <summary>
        /// Coin cells the Start button would sow. A picker position and nothing else, clamped to
        /// <see cref="MaxSowableQuantity"/> on every repaint so a bank shrinking under it can never
        /// leave it offering more than the player holds.
        /// </summary>
        private int _pendingQuantity;

        /// <summary>
        /// True from a "watch ad" tap until the source answers. The re-entrancy guard
        /// <see cref="HoldSlotView"/> keeps for the same reason: a second tap while an ad is up must
        /// not queue a second ad behind it.
        /// </summary>
        private bool _isRequestingReward;

        [Inject]
        public void Construct(
            PowerUpModel powerUpModel,
            LevelProgressionModel levelProgressionModel,
            SettingsModel settingsModel,
            PowerUpSystem powerUpSystem,
            LevelProgressionSystem levelProgressionSystem,
            LevelCoinCellSeedSystem coinCellSeedSystem,
            TimerRunSystem timerRunSystem,
            PowerUpPriceConfig priceConfig)
        {
            _powerUpModel = powerUpModel;
            _levelProgressionModel = levelProgressionModel;
            _settingsModel = settingsModel;
            _powerUpSystem = powerUpSystem;
            _levelProgressionSystem = levelProgressionSystem;
            _coinCellSeedSystem = coinCellSeedSystem;
            _timerRunSystem = timerRunSystem;
            _priceConfig = priceConfig;
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
            if (_powerUpModel == null || _levelProgressionModel == null || _settingsModel == null
                || _powerUpSystem == null || _levelProgressionSystem == null
                || _coinCellSeedSystem == null || _timerRunSystem == null || _priceConfig == null)
            {
                Debug.LogError(
                    $"{nameof(CoinSowerPickerView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so the theme is known before anything below is painted.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Observed rather than read once on open, for the reason the Hold slot observes its count:
            // the ad grant this card itself asks for lands here, and so does a charge earned anywhere
            // else; and reaching the unlock level must open this card in the run that got the player
            // there.
            _powerUpModel.CoinSowerCount.Subscribe(OnChargesChanged).AddTo(_disposables);
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
        /// Opens at a quantity of zero rather than at the most the player holds — the opposite of the
        /// conversion card's "take the lot" default, and deliberately so: this one spends something the
        /// player watched ads to earn. The default answer to an offer must be the one that keeps them.
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
        /// Routes a tap while the panel is open. The close cross wins, then the picker, the ad button and
        /// the Start button; the card then swallows anything else, so a tap on a figure is a deliberate
        /// no-op rather than a dismissal. Only a tap on the scrim outside the card closes.
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

            // The picker is not on screen while locked (see Refresh) — its rects still exist offstage,
            // so this guard keeps a tap from moving a quantity nobody can see.
            bool locked = IsLocked();

            if (!locked
                && RectTransformUtility.RectangleContainsScreenPoint(_minusRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(_pendingQuantity - QUANTITY_STEP);
                return;
            }

            if (!locked
                && RectTransformUtility.RectangleContainsScreenPoint(_plusRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(_pendingQuantity + QUANTITY_STEP);
                return;
            }

            // Tapping the figure itself takes as many as the player holds, the same shortcut the
            // conversion card's figure is — and the same reason: the top of the range is a common answer
            // and stepping to it one at a time is a chore.
            if (!locked
                && RectTransformUtility.RectangleContainsScreenPoint(_quantityRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(MaxSowableQuantity());
                return;
            }

            // Not gated on the level lock: a banked charge is harmless before the gate opens, and Hold —
            // the kind this earn path is borrowed from — has no gate at all. Only the sow is gated.
            if (RectTransformUtility.RectangleContainsScreenPoint(_watchAdButtonRect, screenPosition, eventCamera))
            {
                RequestReward();
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
        /// A quantity of zero skips the spend entirely rather than asking for none of something:
        /// <see cref="PowerUpSystem.TrySpendCoinSowerBulk"/> refuses an empty ask, which is the right
        /// answer to a bug and the wrong one to a player who simply wants to play the level with none.
        /// </para>
        /// <para>
        /// Above zero: spend, then queue, then start. A refused spend leaves the card open with a message
        /// rather than starting the level as though nothing had been asked for — a silent fall-through to
        /// a plain start would spend the player's tap on something they did not choose. The spend cannot
        /// fail in practice, since the picker is clamped to the bank it reads; if it somehow does, the
        /// charges are exactly where they were, and the card says as much.
        /// </para>
        /// <para>
        /// The queue happens before the start because it must: the run consumes it on
        /// <c>RunStartedMessage</c>, which <see cref="LevelProgressionSystem.TryStartPathLevel"/>
        /// publishes on its way through. Should that start then be refused — a level that stopped being
        /// unlocked, or a mode that changed under the card — the queued quantity simply stays queued and
        /// is sown by whichever run opens next. The player spent charges on coin cells and gets coin
        /// cells; they are not lost to a refusal this card cannot cause.
        /// </para>
        /// </summary>
        private void ConfirmAndStart()
        {
            if (_pendingQuantity > 0 && !_powerUpSystem.TrySpendCoinSowerBulk(_pendingQuantity))
            {
                SetMessage(SOW_FAILED_MESSAGE);
                return;
            }

            if (_pendingQuantity > 0)
            {
                _coinCellSeedSystem.QueueExtraCoinCells(_pendingQuantity);
            }

            if (!_levelProgressionSystem.TryStartPathLevel(_pendingLevelNumber))
            {
                SetMessage(CANNOT_START_MESSAGE);
                return;
            }

            Close();
        }

        /// <summary>
        /// Fire-and-forget earn request, mirroring <see cref="HoldSlotView"/>'s. The View banks nothing
        /// itself: the System increments and persists the inventory, and the count subscription bound in
        /// <see cref="Start"/> repaints the offer from that. A grant never sows anything.
        /// </summary>
        private void RequestReward()
        {
            if (_isRequestingReward)
            {
                return;
            }

            RequestRewardAsync(_destroyToken).Forget();
        }

        private async UniTaskVoid RequestRewardAsync(CancellationToken cancellationToken)
        {
            _isRequestingReward = true;
            try
            {
                await _powerUpSystem.GrantRewardAsync(
                    PowerUpKind.CoinSower, cancellationToken, quantity: CHARGES_PER_AD);
            }
            catch (OperationCanceledException)
            {
                // The View went away mid-request. Nothing to undo: the System only banks a reward it
                // was actually handed.
            }
            finally
            {
                _isRequestingReward = false;
            }
        }

        /// <summary>
        /// The most banked charges the player could sow right now: the configured ceiling, or what is
        /// banked, whichever is smaller.
        /// <para>
        /// Zero while the kind is behind its level gate, so the picker cannot be moved off zero at all
        /// there: a banked charge never opens that gate. The gate is read here rather than in the System,
        /// which is why <see cref="PowerUpSystem.TrySpendCoinSowerBulk"/> does not read it again.
        /// </para>
        /// </summary>
        private int MaxSowableQuantity()
        {
            if (IsLocked())
            {
                return 0;
            }

            return Mathf.Min(_priceConfig.CoinSowerMaxQuantity, _powerUpModel.CoinSowerCount.Value);
        }

        /// <summary>Whether the kind is still behind its level gate, read at the same public seam the
        /// shop reads it at so the card and the System can never disagree.</summary>
        private bool IsLocked()
            => !PowerUpUnlockLevels.IsUnlockedAt(
                PowerUpKind.CoinSower, _levelProgressionModel.CurrentLevelNumber.Value);

        private void OnChargesChanged(int value) => Refresh();

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
            _chargesText.color = theme.Accent;
            _quantityText.color = theme.Ink;
            _messageText.color = theme.SoftInk;

            Color neutralPlate = Color.Lerp(theme.CardBackground, theme.Ink, 0.16f);
            Color softBand = Color.Lerp(theme.CardBackground, theme.SoftInk, 0.14f);
            Color accentBand = Color.Lerp(theme.CardBackground, theme.Accent, 0.16f);

            _levelPlate.color = softBand;
            _chargesPlate.color = accentBand;
            _chargesIcon.color = theme.Accent;

            _minusPlate.color = neutralPlate;
            _plusPlate.color = neutralPlate;
            _minusShadow.color = theme.CardShadow;
            _plusShadow.color = theme.CardShadow;
            _minusText.color = theme.Ink;
            _plusText.color = theme.Ink;

            // The ad button is a secondary action and gets the stepper's neutral plate: Start is the
            // one thing on this card the player came to do.
            _watchAdPlate.color = neutralPlate;
            _watchAdShadow.color = theme.CardShadow;
            _watchAdText.color = theme.Ink;

            // Start is the call to action and gets the accent fill, the same primary treatment the
            // conversion card gives Convert.
            _startButtonPlate.color = theme.Accent;
            _startButtonShadow.color = theme.CardShadow;
            _startButtonText.color = theme.CardBackground;

            _lockedBadgePlate.color = neutralPlate;
            _lockedBadgeText.color = theme.SoftInk;
            _lockedTitleText.color = theme.Ink;
            _lockedSubText.color = theme.SoftInk;

            _closeBarA.color = theme.Ink;
            _closeBarB.color = theme.Ink;

            Refresh();
        }

        /// <summary>Clamps and stores the picker position, then repaints. The one place the pending
        /// quantity is written, so it can never be left outside what is banked.</summary>
        private void SetPendingQuantity(int quantity)
        {
            _pendingQuantity = Mathf.Clamp(quantity, 0, MaxSowableQuantity());
            Refresh();
        }

        private void SetMessage(string message)
        {
            _messageText.text = message;
            Refresh();
        }

        /// <summary>The line the card opens on: the offer, or why there is no offer. Written on open
        /// rather than every repaint, so a refusal message survives the repaint that follows it.
        /// <para>
        /// The lock itself is explained by the dedicated locked panel (see Refresh), so this footer only
        /// adds what the panel does not say: that Start Level is still there and still free.
        /// </para>
        /// </summary>
        private void SetMessageForOpening()
        {
            if (IsLocked())
            {
                _messageText.text = LOCKED_FOOTER_MESSAGE;
                return;
            }

            _messageText.text = MaxSowableQuantity() > 0 ? OPENING_MESSAGE : NO_CHARGES_MESSAGE;
        }

        /// <summary>Repaints every figure from the models. Cheap enough to be the only repaint path: it
        /// runs on an open, a stepper tap, a charge change and a theme switch — never per frame.</summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null || _powerUpModel == null)
            {
                return;
            }

            bool locked = IsLocked();
            int maxQuantity = MaxSowableQuantity();

            // Re-clamped here as well as in SetPendingQuantity: a spend made from this card shrinks the
            // bank underneath the picker, and this is the repaint that follows it.
            if (_pendingQuantity > maxQuantity)
            {
                _pendingQuantity = maxQuantity;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(LEVEL_PREFIX_TEXT);
            _stringBuilder.Append(_pendingLevelNumber);
            _levelText.text = _stringBuilder.ToString();

            // "Banked" rather than "Charges" while locked: the same number, framed as saved-for-later
            // rather than spendable-now, so it stops reading as contradicting the lock (issue #427).
            _stringBuilder.Clear();
            _stringBuilder.Append(locked ? BANKED_PREFIX_TEXT : CHARGES_PREFIX_TEXT);
            _stringBuilder.Append(_powerUpModel.CoinSowerCount.Value);
            _chargesText.text = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append(_pendingQuantity);
            _quantityText.text = _stringBuilder.ToString();

            // The picker and the locked panel share one slot on the card — see the class summary's
            // issue #427 paragraph — so exactly one of them is ever active.
            _pickerGroup.SetActive(!locked);
            _lockedGroup.SetActive(locked);

            if (locked)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(LOCKED_TITLE_PREFIX);
                _stringBuilder.Append(PowerUpUnlockLevels.LevelFor(PowerUpKind.CoinSower));
                _lockedTitleText.text = _stringBuilder.ToString();
            }
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

            BuildLevelPill();
            BuildChargesPill();

            _watchAdButtonRect = BuildWideButton(
                "WatchAdButton", WATCH_AD_BUTTON_Y, WATCH_AD_BUTTON_TEXT,
                out _watchAdPlate, out _watchAdShadow, out _watchAdText);

            BuildPickerGroup();
            BuildLockedGroup();

            _startButtonRect = BuildWideButton(
                "StartButton", START_BUTTON_Y, START_BUTTON_TEXT,
                out _startButtonPlate, out _startButtonShadow, out _startButtonText);

            _messageText = UiTextFactory.Create(
                _cardRect, "Message", _bodyFontSize, FontStyle.Normal, Color.clear);
            var messageRect = (RectTransform)_messageText.transform;
            messageRect.sizeDelta = new Vector2(_cardSize.x - (SIDE_INSET * 2f), 64f);
            messageRect.anchoredPosition = new Vector2(0f, MESSAGE_Y);
            _messageText.text = OPENING_MESSAGE;

            BuildCloseButton();

            _panel = panelObject;
        }

        /// <summary>Level chip: a soft rounded pill behind the level number, instead of bare text
        /// floating on the card — the same chip treatment the level path card gives a node number.</summary>
        private void BuildLevelPill()
        {
            var plateObject = new GameObject("LevelPlate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(_cardRect, false);
            Centre(plateRect, new Vector2(260f, 56f));
            plateRect.anchoredPosition = new Vector2(0f, LEVEL_Y);

            _levelPlate = plateObject.GetComponent<Image>();
            _levelPlate.sprite = UiSpriteFactory.RoundedSquare;
            _levelPlate.type = Image.Type.Sliced;
            _levelPlate.pixelsPerUnitMultiplier = 0.9f;
            _levelPlate.color = Color.clear;
            _levelPlate.raycastTarget = false;

            _levelText = UiTextFactory.Create(
                plateRect, "Level", _bodyFontSize, FontStyle.Bold, Color.clear);
        }

        /// <summary>Charges chip: a coin dot plus the figure on a warm plate, so a bank of charges reads
        /// as currency rather than as a bare number — matching the coin badge every other earn seam in
        /// the app wears (see <see cref="HoldSlotView"/>, the HUD coin total).</summary>
        private void BuildChargesPill()
        {
            var plateObject = new GameObject("ChargesPlate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(_cardRect, false);
            Centre(plateRect, new Vector2(320f, 60f));
            plateRect.anchoredPosition = new Vector2(0f, CHARGES_Y);

            _chargesPlate = plateObject.GetComponent<Image>();
            _chargesPlate.sprite = UiSpriteFactory.RoundedSquare;
            _chargesPlate.type = Image.Type.Sliced;
            _chargesPlate.pixelsPerUnitMultiplier = 0.9f;
            _chargesPlate.color = Color.clear;
            _chargesPlate.raycastTarget = false;

            var iconObject = new GameObject("ChargesIcon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(plateRect, false);
            Centre(iconRect, new Vector2(32f, 32f));
            iconRect.anchoredPosition = new Vector2(-110f, 0f);

            _chargesIcon = iconObject.GetComponent<Image>();
            _chargesIcon.sprite = UiSpriteFactory.Circle;
            _chargesIcon.type = Image.Type.Simple;
            _chargesIcon.color = Color.clear;
            _chargesIcon.raycastTarget = false;

            _chargesText = UiTextFactory.Create(
                plateRect, "Charges", _balanceFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_chargesText.transform).anchoredPosition = new Vector2(30f, 0f);
        }

        /// <summary>The stepper and quantity figure, parented under one group so Refresh can show or
        /// hide the whole picker in one call rather than toggling three rects individually.</summary>
        private void BuildPickerGroup()
        {
            _pickerGroup = new GameObject("PickerGroup", typeof(RectTransform));
            var groupRect = (RectTransform)_pickerGroup.transform;
            groupRect.SetParent(_cardRect, false);
            groupRect.anchorMin = new Vector2(0.5f, 0.5f);
            groupRect.anchorMax = new Vector2(0.5f, 0.5f);
            groupRect.pivot = new Vector2(0.5f, 0.5f);
            groupRect.sizeDelta = Vector2.zero;
            groupRect.anchoredPosition = Vector2.zero;

            _minusRect = BuildStepper(
                groupRect, "MinusButton", -QUANTITY_STEPPER_X, "-",
                out _minusPlate, out _minusShadow, out _minusText);
            _plusRect = BuildStepper(
                groupRect, "PlusButton", QUANTITY_STEPPER_X, "+",
                out _plusPlate, out _plusShadow, out _plusText);

            _quantityText = UiTextFactory.Create(
                groupRect, "Quantity", _quantityFontSize, FontStyle.Bold, Color.clear);
            _quantityRect = (RectTransform)_quantityText.transform;
            _quantityRect.sizeDelta = new Vector2(320f, STEPPER_SIZE);
            _quantityRect.anchoredPosition = new Vector2(0f, QUANTITY_ROW_Y);
        }

        /// <summary>
        /// The card's locked-state slot: a small "LOCKED" chip, the unlock level, and a one-line
        /// reassurance that banked charges are not lost — replacing the picker entirely rather than
        /// leaving it on screen offering a quantity of zero next to a still-active Watch Ad button (the
        /// contradiction issue #427 reported). Built inactive; Refresh shows it opposite the picker.
        /// </summary>
        private void BuildLockedGroup()
        {
            _lockedGroup = new GameObject("LockedGroup", typeof(RectTransform));
            var groupRect = (RectTransform)_lockedGroup.transform;
            groupRect.SetParent(_cardRect, false);
            groupRect.anchorMin = new Vector2(0.5f, 0.5f);
            groupRect.anchorMax = new Vector2(0.5f, 0.5f);
            groupRect.pivot = new Vector2(0.5f, 0.5f);
            groupRect.sizeDelta = Vector2.zero;
            groupRect.anchoredPosition = Vector2.zero;

            var badgeObject = new GameObject("LockedBadge", typeof(RectTransform), typeof(Image));
            var badgeRect = (RectTransform)badgeObject.transform;
            badgeRect.SetParent(groupRect, false);
            Centre(badgeRect, LockBadgeSize);
            badgeRect.anchoredPosition = new Vector2(0f, LOCK_BADGE_Y);

            _lockedBadgePlate = badgeObject.GetComponent<Image>();
            _lockedBadgePlate.sprite = UiSpriteFactory.RoundedSquare;
            _lockedBadgePlate.type = Image.Type.Sliced;
            _lockedBadgePlate.pixelsPerUnitMultiplier = 0.9f;
            _lockedBadgePlate.color = Color.clear;
            _lockedBadgePlate.raycastTarget = false;

            int badgeFontSize = Mathf.RoundToInt(_bodyFontSize * 0.6f);
            _lockedBadgeText = UiTextFactory.Create(
                badgeRect, "LockedBadgeText", badgeFontSize, FontStyle.Bold, Color.clear);
            _lockedBadgeText.text = LOCKED_BADGE_TEXT;

            _lockedTitleText = UiTextFactory.Create(
                groupRect, "LockedTitle", _bodyFontSize, FontStyle.Bold, Color.clear);
            var titleRect = (RectTransform)_lockedTitleText.transform;
            titleRect.sizeDelta = LockPanelWidth;
            titleRect.anchoredPosition = new Vector2(0f, LOCK_TITLE_Y);

            int subFontSize = Mathf.RoundToInt(_bodyFontSize * 0.78f);
            _lockedSubText = UiTextFactory.Create(
                groupRect, "LockedSubtext", subFontSize, FontStyle.Normal, Color.clear);
            var subRect = (RectTransform)_lockedSubText.transform;
            subRect.sizeDelta = LockPanelWidth;
            subRect.anchoredPosition = new Vector2(0f, LOCK_SUB_Y);
            _lockedSubText.text = LOCKED_SUBTEXT;

            _lockedGroup.SetActive(false);
        }

        /// <summary>One square stepper plate with a glyph on it, and the offset shadow behind it that
        /// gives every card and button on this screen its tactile lift — see
        /// <see cref="CellFactory.CreateCard"/>. Returns the root the tap is hit-tested against.</summary>
        private RectTransform BuildStepper(
            RectTransform parent, string name, float x, string glyph,
            out Image plateImage, out Image shadowImage, out Text glyphText)
        {
            var plateRect = CellFactory.CreateCard(
                parent, name, new Vector2(STEPPER_SIZE, STEPPER_SIZE), out plateImage, out shadowImage, 0.7f);
            plateRect.anchoredPosition = new Vector2(x, QUANTITY_ROW_Y);
            plateImage.raycastTarget = false;

            glyphText = UiTextFactory.Create(plateRect, "Glyph", _quantityFontSize, FontStyle.Bold, Color.clear);
            glyphText.text = glyph;

            return plateRect;
        }

        /// <summary>One full-width action plate with a caption, lifted off the card with the same offset
        /// shadow every card here wears. Returns the root the tap is hit-tested against.</summary>
        private RectTransform BuildWideButton(
            string name, float y, string caption,
            out Image plateImage, out Image shadowImage, out Text captionText)
        {
            RectTransform plateRect = CellFactory.CreateCard(
                _cardRect, name, WideButtonSize, out plateImage, out shadowImage, 0.7f);
            plateRect.anchoredPosition = new Vector2(0f, y);
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
