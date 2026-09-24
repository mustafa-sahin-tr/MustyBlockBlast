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
    /// Look (issue #427, mockup https://claude.ai/artifact/Dnqfm9mUJH7Q5AKrbP9vEu): the shop's candy
    /// language rather than theme-tinted plates — a gold icon tile, a Bowlby One SC title, glossy chunky
    /// buttons (blue to earn, green to start) cut from the shop's button sprite. The card has three
    /// states and lays itself out as a vertical stack of whichever rows the state shows, so no state
    /// leaves a hole or an overlap:
    /// <list type="bullet">
    /// <item><b>Locked</b> (before the unlock level): a lock panel replaces the picker, so nothing implies
    /// the sow is available; the bank is framed as "Banked charges" and Watch Ad as saving for later.
    /// Watch Ad and Start Level stay exactly as capable as they were — only the sow itself is gated (see
    /// <see cref="IsLocked"/>).</item>
    /// <item><b>Charges</b>: the stepper and big figure, and Start states what it commits to
    /// ("Start Level · Sow 3").</item>
    /// <item><b>Empty</b> (unlocked, nothing banked): a "no charges yet" panel, Watch Ad as the prominent
    /// action and a softer "Start Level with none".</item>
    /// </list>
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
        /// the only caller that asks for it, and the ad button's "+2" chip promises it.
        /// </summary>
        private const int CHARGES_PER_AD = 2;

        // Layout, in canvas reference pixels (1080x1920), scaled from the 390pt-wide mockup.
        private const float CARD_WIDTH = 880f;
        private const float CARD_PADDING_TOP = 64f;
        private const float CARD_PADDING_BOTTOM = 60f;
        private const float CARD_RADIUS = 64f;
        private const float CARD_SHADOW_DROP = 26f;
        private const float SIDE_INSET = 60f;
        private const float CONTENT_WIDTH = CARD_WIDTH - (SIDE_INSET * 2f);
        private const float STACK_GAP = 28f;

        private const float ICON_TILE_SIZE = 184f;
        private const float ICON_GLYPH_SIZE = 124f;
        private const float HEADER_HEIGHT = 72f;
        private const float LEVEL_PILL_HEIGHT = 62f;
        private const float LEVEL_PILL_WIDTH = 210f;

        private const float PANEL_RADIUS = 44f;
        private const float PANEL_OUTLINE = 5f;
        private const float LOCKED_PANEL_HEIGHT = 330f;
        private const float EMPTY_PANEL_HEIGHT = 220f;
        private const float LOCK_TILE_SIZE = 116f;
        private const float LOCK_GLYPH_SIZE = 84f;
        private const float PANEL_TEXT_WIDTH = CONTENT_WIDTH - 60f;

        private const float CHARGES_PILL_HEIGHT = 100f;
        private const float CHARGES_COIN_SIZE = 54f;
        private const float CHARGES_PADDING = 32f;

        private const float PICKER_HEIGHT = 176f;
        private const float STEPPER_SIZE = 124f;
        private const float STEPPER_X = 190f;
        private const float QUANTITY_WIDTH = 230f;

        private const float WATCH_AD_HEIGHT = 128f;
        private const float START_HEIGHT = 136f;
        private const float BUTTON_SLICE_SCALE = 2.5f;

        // The shop tile's 60px slice border would round a small plate into a disc; scaled so the
        // steppers and icon tile stay rounded squares.
        private const float TILE_SLICE_SCALE = 2f;
        private const float BUTTON_LABEL_RISE = 6f;
        private const float AD_CHIP_WIDTH = 124f;
        private const float AD_CHIP_HEIGHT = 58f;
        private const float AD_CHIP_GAP = 18f;
        private const float AD_CHIP_COIN_SIZE = 34f;

        private const float MESSAGE_HEIGHT = 84f;
        private const float CLOSE_DISC_SIZE = 92f;
        private const float CLOSE_INSET = 40f;
        private const float CLOSE_BAR_THICKNESS = 9f;

        private static readonly Vector2 TextShadowOffset = new Vector2(0f, -4f);

        // The mockup's palette. Fixed rather than theme-tinted, like the shop's: the candy buttons and
        // gold tile are the card's identity, and they read on every theme's scrim.
        private static readonly Color CardFace = new Color32(0xFD, 0xFC, 0xFB, 0xFF);
        private static readonly Color CardShadow = new Color32(0x2B, 0x26, 0x33, 0x3A);
        private static readonly Color TitleInk = new Color32(0x01, 0x57, 0x9B, 0xFF);
        private static readonly Color PillFill = new Color32(0xF1, 0xEE, 0xF8, 0xFF);
        private static readonly Color PillInk = new Color32(0x60, 0x97, 0xC2, 0xFF);
        private static readonly Color IconTileGold = new Color32(0xF0, 0xB8, 0x3A, 0xFF);
        private static readonly Color IconTileEmpty = new Color32(0xE6, 0xE1, 0xD6, 0xFF);
        private static readonly Color IconGlyphEmpty = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color PanelFill = new Color32(0xF6, 0xF4, 0xEF, 0xFF);
        private static readonly Color PanelOutline = new Color32(0xE3, 0xDF, 0xD2, 0xFF);
        private static readonly Color LockTileFill = new Color32(0xE3, 0xDF, 0xD2, 0xFF);
        private static readonly Color PanelTitleInk = new Color32(0x4A, 0x44, 0x38, 0xFF);
        private static readonly Color MutedInk = new Color32(0x8C, 0x84, 0x70, 0xFF);
        private static readonly Color ChargesFill = new Color32(0xFF, 0xF6, 0xE2, 0xFF);
        private static readonly Color ChargesInk = new Color32(0x91, 0x70, 0x0F, 0xFF);
        private static readonly Color StepperFill = new Color32(0xF1, 0xEE, 0xE8, 0xFF);
        private static readonly Color AdBlue = new Color32(0x3F, 0x86, 0xEE, 0xFF);
        private static readonly Color StartGreen = new Color32(0x4C, 0xBA, 0x44, 0xFF);
        private static readonly Color StartGreenSoft = new Color32(0x8F, 0xD6, 0x80, 0xFF);
        private static readonly Color ChipFill = new Color(1f, 1f, 1f, 0.24f);
        private static readonly Color TextShadowColour = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color CloseDisc = new Color32(0xF1, 0xEE, 0xE8, 0xFF);
        private static readonly Color CloseCross = new Color32(0x8A, 0x84, 0x96, 0xFF);

        // Plain strings, not String Table keys, for the reason PowerUpShopView states: LocalizationKeys
        // has no currency section yet, and adding keys with no translations behind them would render
        // the keys themselves. Tracked for a follow-up.
        private const string HEADER_TEXT = "COIN SOWER";
        private const string LEVEL_PREFIX_TEXT = "Level ";
        private const string CHARGES_LABEL_TEXT = "Charges";
        private const string BANKED_LABEL_TEXT = "Banked charges";
        private const string QUANTITY_CAPTION_TEXT = "COIN CELLS";
        private const string WATCH_AD_LOCKED_TEXT = "Watch ad";
        private const string WATCH_AD_MORE_TEXT = "Watch ad for more";
        private const string WATCH_AD_EMPTY_TEXT = "Watch ad to earn charges";
        private const string AD_CHIP_TEXT = "+2";
        private const string START_BUTTON_TEXT = "Start Level";
        private const string START_SOW_PREFIX_TEXT = "Start Level · Sow ";
        private const string START_WITH_NONE_TEXT = "Start Level with none";
        private const string OPENING_MESSAGE = "Sow coin cells into this level, or start with none.";
        private const string LOCKED_TITLE_PREFIX = "Sowing unlocks at Level ";
        private const string LOCKED_SUBTEXT = "Charges you bank now stay saved for when the gate opens.";
        private const string LOCKED_FOOTER_PREFIX = "Starting now plays Level ";
        private const string LOCKED_FOOTER_SUFFIX = " with no coin cells sown.";
        private const string EMPTY_TITLE_TEXT = "No charges banked yet";
        private const string EMPTY_SUBTEXT = "Watch an ad below to earn some, or start the level with none.";
        private const string SOW_FAILED_MESSAGE =
            "Those coin cells could not be sown. Your charges are still banked.";
        private const string CANNOT_START_MESSAGE = "This level cannot be started right now.";

        private enum CardState
        {
            Locked,
            Charges,
            Empty,
        }

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(64);

        [Header("Type")]
        [Tooltip("Display face (Bowlby One SC): title and figures.")]
        [SerializeField] private Font _displayFont;
        [Tooltip("Body face (Baloo 2 ExtraBold): labels, buttons and copy.")]
        [SerializeField] private Font _bodyFont;
        [SerializeField] private int _headerFontSize = 64;
        [SerializeField] private int _quantityFontSize = 104;
        [SerializeField] private int _bodyFontSize = 32;
        [SerializeField] private int _buttonFontSize = 40;

        [Header("Sprites")]
        [Tooltip("White 9-sliced glossy button with a darker bottom lip (the shop's). Tinted at runtime.")]
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("White glossy rounded tile (the shop's). Tinted at runtime.")]
        [SerializeField] private Sprite _tileSprite;
        [Tooltip("White padlock glyph. Tinted at runtime.")]
        [SerializeField] private Sprite _lockSprite;
        [Tooltip("Coin glyph used for the charges row and the ad chip.")]
        [SerializeField] private Sprite _coinSprite;
        [Tooltip("The coin cell's own icon, shown on the header tile — it is what the card sows.")]
        [SerializeField] private Sprite _coinCellSprite;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.08f, 0.09f, 0.16f, 0.58f);

        private PowerUpModel _powerUpModel;
        private LevelProgressionModel _levelProgressionModel;
        private PowerUpSystem _powerUpSystem;
        private LevelProgressionSystem _levelProgressionSystem;
        private LevelCoinCellSeedSystem _coinCellSeedSystem;
        private TimerRunSystem _timerRunSystem;
        private PowerUpPriceConfig _priceConfig;

        private Canvas _canvas;
        private CancellationToken _destroyToken;
        private GameObject _panel;
        private RectTransform _cardRect;
        private RectTransform _cardShadowRect;
        private RectTransform _closeButtonRect;

        private RectTransform _iconTileRect;
        private Image _iconTile;
        private Image _iconGlyph;
        private RectTransform _headerRect;
        private RectTransform _levelPillRect;
        private Text _levelText;

        // The info panel is the locked explanation or the empty-bank explanation — never both, and
        // never beside the picker (issue #427).
        private RectTransform _infoPanelRect;
        private Image _infoPanelFill;
        private Image _infoPanelOutline;
        private RectTransform _lockTileRect;
        private Text _infoTitleText;
        private Text _infoSubText;

        private RectTransform _chargesPillRect;
        private Text _chargesLabelText;
        private Text _chargesValueText;

        private RectTransform _pickerRect;
        private RectTransform _minusRect;
        private RectTransform _plusRect;
        private RectTransform _quantityRect;
        private Text _quantityText;

        private RectTransform _watchAdButtonRect;
        private Text _watchAdText;
        private RectTransform _adChipRect;

        private RectTransform _startButtonRect;
        private Image _startButtonPlate;
        private Text _startButtonText;

        private RectTransform _messageRect;
        private Text _messageText;

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
        /// A refusal to show in the footer instead of the state's own line, or null. Cleared on open, so
        /// it survives the repaints that follow the refusal but not a reopen.
        /// </summary>
        private string _messageOverride;

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
            PowerUpSystem powerUpSystem,
            LevelProgressionSystem levelProgressionSystem,
            LevelCoinCellSeedSystem coinCellSeedSystem,
            TimerRunSystem timerRunSystem,
            PowerUpPriceConfig priceConfig)
        {
            _powerUpModel = powerUpModel;
            _levelProgressionModel = levelProgressionModel;
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
            if (_powerUpModel == null || _levelProgressionModel == null
                || _powerUpSystem == null || _levelProgressionSystem == null
                || _coinCellSeedSystem == null || _timerRunSystem == null || _priceConfig == null)
            {
                Debug.LogError(
                    $"{nameof(CoinSowerPickerView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

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
            _messageOverride = null;
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

            // The picker is only on screen in the Charges state (see Refresh) — its rects still exist
            // while hidden, so this guard keeps a tap from moving a quantity nobody can see.
            bool pickerShown = _pickerRect.gameObject.activeSelf;

            if (pickerShown
                && RectTransformUtility.RectangleContainsScreenPoint(_minusRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(_pendingQuantity - QUANTITY_STEP);
                return;
            }

            if (pickerShown
                && RectTransformUtility.RectangleContainsScreenPoint(_plusRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(_pendingQuantity + QUANTITY_STEP);
                return;
            }

            // Tapping the figure itself takes as many as the player holds, the same shortcut the
            // conversion card's figure is — and the same reason: the top of the range is a common answer
            // and stepping to it one at a time is a chore.
            if (pickerShown
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

        private CardState CurrentState()
        {
            if (IsLocked())
            {
                return CardState.Locked;
            }

            return _powerUpModel.CoinSowerCount.Value > 0 ? CardState.Charges : CardState.Empty;
        }

        private void OnChargesChanged(int value) => Refresh();

        private void OnLevelChanged(int levelNumber) => Refresh();

        /// <summary>Clamps and stores the picker position, then repaints. The one place the pending
        /// quantity is written, so it can never be left outside what is banked.</summary>
        private void SetPendingQuantity(int quantity)
        {
            _pendingQuantity = Mathf.Clamp(quantity, 0, MaxSowableQuantity());
            Refresh();
        }

        private void SetMessage(string message)
        {
            _messageOverride = message;
            Refresh();
        }

        /// <summary>Repaints every figure from the models and re-stacks the card for its state. Cheap
        /// enough to be the only repaint path: it runs on an open, a stepper tap and a charge change —
        /// never per frame.</summary>
        private void Refresh()
        {
            if (_panel == null || _powerUpModel == null)
            {
                return;
            }

            CardState state = CurrentState();
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

            // A dimmed, empty-looking tile when there is nothing to sow; the gold coin tile otherwise.
            bool empty = state == CardState.Empty;
            _iconTile.color = empty ? IconTileEmpty : IconTileGold;
            _iconGlyph.color = empty ? IconGlyphEmpty : Color.white;

            _infoPanelRect.gameObject.SetActive(state != CardState.Charges);
            _chargesPillRect.gameObject.SetActive(state != CardState.Empty);
            _pickerRect.gameObject.SetActive(state == CardState.Charges);

            switch (state)
            {
                case CardState.Locked:
                    PaintLocked();
                    break;
                case CardState.Charges:
                    PaintCharges();
                    break;
                default:
                    PaintEmpty();
                    break;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(_powerUpModel.CoinSowerCount.Value);
            _chargesValueText.text = _stringBuilder.ToString();

            LayoutAdButtonContent();

            string footer = _messageOverride ?? DefaultFooter(state);
            _messageText.text = footer;
            _messageRect.gameObject.SetActive(!string.IsNullOrEmpty(footer));

            LayoutCard();
        }

        private void PaintLocked()
        {
            SizeInfoPanel(LOCKED_PANEL_HEIGHT);
            _lockTileRect.gameObject.SetActive(true);
            _lockTileRect.anchoredPosition = new Vector2(0f, 88f);
            _infoTitleText.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            _infoSubText.rectTransform.anchoredPosition = new Vector2(0f, -86f);

            _stringBuilder.Clear();
            _stringBuilder.Append(LOCKED_TITLE_PREFIX);
            _stringBuilder.Append(PowerUpUnlockLevels.LevelFor(PowerUpKind.CoinSower));
            _infoTitleText.text = _stringBuilder.ToString();
            _infoSubText.text = LOCKED_SUBTEXT;

            // "Banked" rather than "Charges" while locked: the same number, framed as saved-for-later
            // rather than spendable-now, so it stops reading as contradicting the lock.
            _chargesLabelText.text = BANKED_LABEL_TEXT;
            _watchAdText.text = WATCH_AD_LOCKED_TEXT;
            SetStartButton(START_BUTTON_TEXT, StartGreen);
        }

        private void PaintCharges()
        {
            _chargesLabelText.text = CHARGES_LABEL_TEXT;
            _watchAdText.text = WATCH_AD_MORE_TEXT;

            _stringBuilder.Clear();
            _stringBuilder.Append(_pendingQuantity);
            _quantityText.text = _stringBuilder.ToString();

            // Start names what it commits to, so the tap that spends charges says so on its face.
            if (_pendingQuantity > 0)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(START_SOW_PREFIX_TEXT);
                _stringBuilder.Append(_pendingQuantity);
                SetStartButton(_stringBuilder.ToString(), StartGreen);
            }
            else
            {
                SetStartButton(START_BUTTON_TEXT, StartGreen);
            }
        }

        private void PaintEmpty()
        {
            SizeInfoPanel(EMPTY_PANEL_HEIGHT);
            _lockTileRect.gameObject.SetActive(false);
            _infoTitleText.rectTransform.anchoredPosition = new Vector2(0f, 40f);
            _infoSubText.rectTransform.anchoredPosition = new Vector2(0f, -36f);
            _infoTitleText.text = EMPTY_TITLE_TEXT;
            _infoSubText.text = EMPTY_SUBTEXT;

            // Earning is the thing to do here, so Watch Ad carries the weight and Start steps back to a
            // softer green.
            _watchAdText.text = WATCH_AD_EMPTY_TEXT;
            SetStartButton(START_WITH_NONE_TEXT, StartGreenSoft);
        }

        private string DefaultFooter(CardState state)
        {
            switch (state)
            {
                case CardState.Locked:
                    _stringBuilder.Clear();
                    _stringBuilder.Append(LOCKED_FOOTER_PREFIX);
                    _stringBuilder.Append(_pendingLevelNumber);
                    _stringBuilder.Append(LOCKED_FOOTER_SUFFIX);
                    return _stringBuilder.ToString();
                case CardState.Charges:
                    return OPENING_MESSAGE;
                default:
                    // The empty panel already says the level can be started with none.
                    return null;
            }
        }

        private void SetStartButton(string caption, Color fill)
        {
            _startButtonText.text = caption;
            _startButtonPlate.color = fill;
        }

        private void SizeInfoPanel(float height)
        {
            var size = new Vector2(CONTENT_WIDTH, height);
            _infoPanelRect.sizeDelta = size;
            _infoPanelFill.rectTransform.sizeDelta = size;
            _infoPanelOutline.rectTransform.sizeDelta = size;
        }

        /// <summary>Centres the ad caption and its "+2" chip as one group, whatever the caption's
        /// state-dependent length.</summary>
        private void LayoutAdButtonContent()
        {
            float labelWidth = _watchAdText.preferredWidth;
            float groupWidth = labelWidth + AD_CHIP_GAP + AD_CHIP_WIDTH;
            var labelRect = _watchAdText.rectTransform;
            labelRect.sizeDelta = new Vector2(labelWidth, WATCH_AD_HEIGHT);
            labelRect.anchoredPosition = new Vector2(
                (-groupWidth * 0.5f) + (labelWidth * 0.5f), BUTTON_LABEL_RISE);
            _adChipRect.anchoredPosition = new Vector2(
                (groupWidth * 0.5f) - (AD_CHIP_WIDTH * 0.5f), BUTTON_LABEL_RISE);
        }

        /// <summary>
        /// Stacks the active rows top to bottom and sizes the card around them, so each state's card is
        /// exactly as tall as what it shows. Two passes over the same order: measure, then place.
        /// </summary>
        private void LayoutCard()
        {
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

        private float StackRows(float top, bool apply)
        {
            float cursor = 0f;
            cursor = PlaceRow(_iconTileRect, ICON_TILE_SIZE, cursor, top, apply);
            cursor = PlaceRow(_headerRect, HEADER_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_levelPillRect, LEVEL_PILL_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_infoPanelRect, _infoPanelRect.sizeDelta.y, cursor, top, apply);
            cursor = PlaceRow(_chargesPillRect, CHARGES_PILL_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_pickerRect, PICKER_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_watchAdButtonRect, WATCH_AD_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_startButtonRect, START_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_messageRect, MESSAGE_HEIGHT, cursor, top, apply);
            return cursor;
        }

        /// <summary>Places one row below the cursor (with the stack gap before every row but the
        /// first) and returns the new cursor. Inactive rows take no space.</summary>
        private static float PlaceRow(RectTransform row, float height, float cursor, float top, bool apply)
        {
            if (!row.gameObject.activeSelf)
            {
                return cursor;
            }

            if (cursor > 0f)
            {
                cursor += STACK_GAP;
            }

            if (apply)
            {
                row.anchoredPosition = new Vector2(0f, top - cursor - (height * 0.5f));
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

            Image cardShadow = HudChrome.BuildRounded(
                panelRect, "CoinSowerPickerCardShadow", new Vector2(CARD_WIDTH, 1f), Vector2.zero, CARD_RADIUS);
            cardShadow.color = CardShadow;
            _cardShadowRect = cardShadow.rectTransform;

            Image card = HudChrome.BuildRounded(
                panelRect, "CoinSowerPickerCard", new Vector2(CARD_WIDTH, 1f), Vector2.zero, CARD_RADIUS);
            card.color = CardFace;
            _cardRect = card.rectTransform;

            BuildIconTile();

            Text header = CreateText(_cardRect, "Header", _headerFontSize, _displayFont, TitleInk);
            header.text = HEADER_TEXT;
            _headerRect = header.rectTransform;
            _headerRect.sizeDelta = new Vector2(CONTENT_WIDTH, HEADER_HEIGHT);

            BuildLevelPill();
            BuildInfoPanel();
            BuildChargesPill();
            BuildPicker();
            BuildWatchAdButton();
            BuildStartButton();

            _messageText = CreateText(_cardRect, "Message", _bodyFontSize - 4, _bodyFont, MutedInk);
            _messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _messageRect = _messageText.rectTransform;
            _messageRect.sizeDelta = new Vector2(CONTENT_WIDTH, MESSAGE_HEIGHT);

            BuildCloseButton();

            _panel = panelObject;
        }

        /// <summary>The gold tile at the top of the card, carrying the coin cell's own icon — the thing
        /// this card sows.</summary>
        private void BuildIconTile()
        {
            _iconTileRect = HudChrome.CreateRect(
                _cardRect, "IconTile", new Vector2(ICON_TILE_SIZE, ICON_TILE_SIZE), Vector2.zero);
            _iconTile = BuildSlicedPlate(
                _iconTileRect, "Tile", _tileSprite, new Vector2(ICON_TILE_SIZE, ICON_TILE_SIZE), TILE_SLICE_SCALE * 0.7f);
            _iconGlyph = HudChrome.BuildGlyph(
                _iconTileRect, "Glyph", _coinCellSprite, new Vector2(ICON_GLYPH_SIZE, ICON_GLYPH_SIZE), new Vector2(0f, 6f));
        }

        private void BuildLevelPill()
        {
            var size = new Vector2(LEVEL_PILL_WIDTH, LEVEL_PILL_HEIGHT);
            _levelPillRect = HudChrome.CreateRect(_cardRect, "LevelPill", size, Vector2.zero);
            HudChrome.BuildRounded(_levelPillRect, "Plate", size, Vector2.zero, LEVEL_PILL_HEIGHT * 0.5f).color = PillFill;
            _levelText = CreateText(_levelPillRect, "Level", _bodyFontSize - 2, _bodyFont, PillInk);
        }

        /// <summary>
        /// The panel that takes the picker's place when there is nothing to pick: the lock explanation
        /// before the unlock level, the "no charges yet" line after it. One panel, repainted and resized
        /// per state in Refresh.
        /// </summary>
        private void BuildInfoPanel()
        {
            var size = new Vector2(CONTENT_WIDTH, LOCKED_PANEL_HEIGHT);
            _infoPanelRect = HudChrome.CreateRect(_cardRect, "InfoPanel", size, Vector2.zero);

            _infoPanelFill = HudChrome.BuildRounded(_infoPanelRect, "Fill", size, Vector2.zero, PANEL_RADIUS);
            _infoPanelFill.color = PanelFill;
            _infoPanelOutline = HudChrome.BuildOutline(
                _infoPanelRect, "Outline", size, Vector2.zero, PANEL_RADIUS, PANEL_OUTLINE);
            _infoPanelOutline.color = PanelOutline;

            var lockTileSize = new Vector2(LOCK_TILE_SIZE, LOCK_TILE_SIZE);
            _lockTileRect = HudChrome.CreateRect(_infoPanelRect, "LockTile", lockTileSize, Vector2.zero);
            HudChrome.BuildRounded(_lockTileRect, "Plate", lockTileSize, Vector2.zero, 34f).color = LockTileFill;
            HudChrome.BuildGlyph(
                _lockTileRect, "Lock", _lockSprite, new Vector2(LOCK_GLYPH_SIZE, LOCK_GLYPH_SIZE), Vector2.zero)
                .color = MutedInk;

            _infoTitleText = CreateText(_infoPanelRect, "Title", _bodyFontSize + 4, _bodyFont, PanelTitleInk);
            _infoTitleText.rectTransform.sizeDelta = new Vector2(PANEL_TEXT_WIDTH, 52f);

            _infoSubText = CreateText(_infoPanelRect, "Subtext", _bodyFontSize - 4, _bodyFont, MutedInk);
            _infoSubText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _infoSubText.rectTransform.sizeDelta = new Vector2(PANEL_TEXT_WIDTH, 80f);
        }

        /// <summary>The bank row: a coin, the label ("Charges", or "Banked charges" while locked) and the
        /// figure, on a warm strip so the bank reads as currency.</summary>
        private void BuildChargesPill()
        {
            var size = new Vector2(CONTENT_WIDTH, CHARGES_PILL_HEIGHT);
            _chargesPillRect = HudChrome.CreateRect(_cardRect, "ChargesPill", size, Vector2.zero);
            HudChrome.BuildRounded(_chargesPillRect, "Plate", size, Vector2.zero, 36f).color = ChargesFill;

            float left = (-CONTENT_WIDTH * 0.5f) + CHARGES_PADDING;
            HudChrome.BuildGlyph(
                _chargesPillRect, "Coin", _coinSprite, new Vector2(CHARGES_COIN_SIZE, CHARGES_COIN_SIZE),
                new Vector2(left + (CHARGES_COIN_SIZE * 0.5f), 0f)).color = Color.white;

            _chargesLabelText = HudChrome.CreateLabel(
                _chargesPillRect, "Label", _bodyFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(left + CHARGES_COIN_SIZE + 18f, 0f), _bodyFont);
            _chargesLabelText.color = ChargesInk;

            _chargesValueText = HudChrome.CreateLabel(
                _chargesPillRect, "Value", _bodyFontSize + 14, FontStyle.Normal, TextAnchor.MiddleRight,
                new Vector2((CONTENT_WIDTH * 0.5f) - CHARGES_PADDING, 0f), _displayFont);
            _chargesValueText.color = ChargesInk;
        }

        /// <summary>The stepper flanking a big figure with its "coin cells" caption; tapping the figure
        /// takes the maximum (see HandleTap).</summary>
        private void BuildPicker()
        {
            _pickerRect = HudChrome.CreateRect(
                _cardRect, "Picker", new Vector2(CONTENT_WIDTH, PICKER_HEIGHT), Vector2.zero);

            _minusRect = BuildStepper("MinusButton", -STEPPER_X, "–");
            _plusRect = BuildStepper("PlusButton", STEPPER_X, "+");

            _quantityRect = HudChrome.CreateRect(
                _pickerRect, "Quantity", new Vector2(QUANTITY_WIDTH, PICKER_HEIGHT), Vector2.zero);
            _quantityText = CreateText(_quantityRect, "Figure", _quantityFontSize, _displayFont, TitleInk);
            _quantityText.rectTransform.anchoredPosition = new Vector2(0f, 20f);

            Text caption = CreateText(_quantityRect, "Caption", _bodyFontSize - 8, _bodyFont, PillInk);
            caption.text = QUANTITY_CAPTION_TEXT;
            caption.rectTransform.anchoredPosition = new Vector2(0f, -62f);
        }

        private RectTransform BuildStepper(string objectName, float x, string glyph)
        {
            var size = new Vector2(STEPPER_SIZE, STEPPER_SIZE);
            RectTransform stepperRect = HudChrome.CreateRect(_pickerRect, objectName, size, new Vector2(x, 0f));
            BuildSlicedPlate(stepperRect, "Plate", _tileSprite, size, TILE_SLICE_SCALE).color = StepperFill;

            Text glyphText = CreateText(stepperRect, "Glyph", _headerFontSize, _displayFont, TitleInk);
            glyphText.text = glyph;
            glyphText.rectTransform.anchoredPosition = new Vector2(0f, BUTTON_LABEL_RISE);
            return stepperRect;
        }

        /// <summary>The blue earn button: a caption plus a "+2 coin" chip, centred together by
        /// <see cref="LayoutAdButtonContent"/>.</summary>
        private void BuildWatchAdButton()
        {
            var size = new Vector2(CONTENT_WIDTH, WATCH_AD_HEIGHT);
            _watchAdButtonRect = HudChrome.CreateRect(_cardRect, "WatchAdButton", size, Vector2.zero);
            BuildSlicedPlate(_watchAdButtonRect, "Plate", _buttonSprite, size, BUTTON_SLICE_SCALE).color = AdBlue;

            _watchAdText = CreateButtonText(_watchAdButtonRect, "Caption");

            var chipSize = new Vector2(AD_CHIP_WIDTH, AD_CHIP_HEIGHT);
            _adChipRect = HudChrome.CreateRect(_watchAdButtonRect, "Chip", chipSize, Vector2.zero);
            HudChrome.BuildRounded(_adChipRect, "Plate", chipSize, Vector2.zero, AD_CHIP_HEIGHT * 0.5f).color = ChipFill;

            Text chipText = CreateText(_adChipRect, "Amount", _bodyFontSize, _bodyFont, Color.white);
            chipText.text = AD_CHIP_TEXT;
            chipText.rectTransform.anchoredPosition = new Vector2(-18f, 2f);
            AddTextShadow(chipText);

            HudChrome.BuildGlyph(
                _adChipRect, "Coin", _coinSprite, new Vector2(AD_CHIP_COIN_SIZE, AD_CHIP_COIN_SIZE),
                new Vector2(30f, 0f)).color = Color.white;
        }

        private void BuildStartButton()
        {
            var size = new Vector2(CONTENT_WIDTH, START_HEIGHT);
            _startButtonRect = HudChrome.CreateRect(_cardRect, "StartButton", size, Vector2.zero);
            _startButtonPlate = BuildSlicedPlate(_startButtonRect, "Plate", _buttonSprite, size, BUTTON_SLICE_SCALE);
            _startButtonPlate.color = StartGreen;

            _startButtonText = CreateButtonText(_startButtonRect, "Caption");
            _startButtonText.rectTransform.sizeDelta = new Vector2(CONTENT_WIDTH, START_HEIGHT);
            _startButtonText.rectTransform.anchoredPosition = new Vector2(0f, BUTTON_LABEL_RISE);
        }

        /// <summary>The close cross on a soft disc, drawn as two rotated bars so it needs no glyph
        /// asset.</summary>
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
        /// back to a plain rounded plate if the sprite is not assigned, so a missing reference degrades
        /// the look rather than the layout.</summary>
        private static Image BuildSlicedPlate(
            RectTransform parent, string objectName, Sprite sprite, Vector2 size, float sliceScale = 1f)
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

        private Text CreateButtonText(RectTransform parent, string objectName)
        {
            Text text = CreateText(parent, objectName, _buttonFontSize, _bodyFont, Color.white);
            AddTextShadow(text);
            return text;
        }

        private static Text CreateText(RectTransform parent, string objectName, int fontSize, Font font, Color colour)
            => UiTextFactory.Create(parent, objectName, fontSize, FontStyle.Normal, colour, font);

        /// <summary>The mockup's dark text-shadow under white button captions.</summary>
        private static void AddTextShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = TextShadowColour;
            shadow.effectDistance = TextShadowOffset;
        }
    }
}
