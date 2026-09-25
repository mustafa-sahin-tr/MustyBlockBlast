using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
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
    /// The level-start card (issue #464, mockup https://claude.ai/artifact/DUpE9YKMpXjXp71BEX6EUb): what
    /// a level node opens. It names the level (its <see cref="LevelIdentityCatalog"/> name and icon),
    /// says what clearing it pays, shows the player's power-ups now and after, and — for a level that can
    /// be played — offers the Coin Sower sow and the Start button.
    /// <para>
    /// A View-layer insertion between a node tap and the run: nothing in a Model says "a level is about
    /// to start". The tapped level lives here (<see cref="_pendingLevelNumber"/>) for as long as the card
    /// is open and leaves as the argument to <see cref="LevelProgressionSystem.TryStartPathLevel"/>.
    /// </para>
    /// <para>
    /// Three modes, from where the level sits against the frontier:
    /// <list type="bullet">
    /// <item><b>Play</b> (the frontier level): "clear it to win" lists the level's own reward
    /// (<see cref="LevelCompletionRewards"/>) and the first-try streak goal with its x/n progress; when a
    /// first-try clear would complete a rule, the actual bonus is shown. The inventory grid shows now ›
    /// after.</item>
    /// <item><b>Replay</b> (below the frontier): the rewards read as already collected and the streak row
    /// says replays don't count — both true of what a replay pays (nothing).</item>
    /// <item><b>Locked</b> (above the frontier): a read-only preview of the rewards, with neither
    /// buttons nor inventory — the level cannot be started from here.</item>
    /// </list>
    /// Every preview is computed by the same pure rules the payout uses
    /// (<see cref="LevelCompletionRewards.For(int)"/>, <see cref="RewardRules.AppendFirstTryPayout"/>),
    /// so the card can never promise something the systems will not pay.
    /// </para>
    /// <para>
    /// The Coin Sower sow is a compact stepper row, shown only once the kind is unlocked — the old
    /// "Sowing unlocks at Level 35" box is gone. The Watch Ad button still banks two Coin Sower charges
    /// (issue #404) in every mode that can start a run, and shares one row with Start.
    /// </para>
    /// <para>
    /// While open it is modal, swallows every tap and holds the run's clock through
    /// <see cref="TimerRunSystem.SetMenuPaused"/> — see the gate chain in <see cref="BoardInputView"/>.
    /// </para>
    /// <para>
    /// Lives (issue #478): a pink row above the buttons shows "n / cap lives" and what failing costs,
    /// and Start asks <see cref="LivesSystem.TryPassStartGate"/> before anything is spent. At zero lives
    /// the out-of-lives sheet opens over this card, which stays open underneath for the retry.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelStartCardView : MonoBehaviour
    {
        /// <summary>How much one stepper tap moves the pending sow quantity.</summary>
        private const int QUANTITY_STEP = 1;

        /// <summary>Charges one rewarded ad banks (issue #404); the ad button's "+2" chip promises it.</summary>
        private const int CHARGES_PER_AD = 2;

        // Layout, in canvas reference pixels (1080x1920), scaled from the 390pt-wide mockup.
        private const float CARD_WIDTH = 900f;
        private const float CARD_PADDING_TOP = 52f;
        private const float CARD_PADDING_BOTTOM = 48f;
        private const float CARD_RADIUS = 64f;
        private const float CARD_SHADOW_DROP = 26f;
        private const float SIDE_INSET = 50f;
        private const float CONTENT_WIDTH = CARD_WIDTH - (SIDE_INSET * 2f);
        private const float STACK_GAP = 22f;
        private const float CAPTION_GAP = 10f;

        private const float ICON_SIZE = 190f;
        private const float ICON_FRAME = 10f;
        private const float ICON_RADIUS = 52f;
        private const float ICON_BADGE_SIZE = 70f;
        private const float TITLE_HEIGHT = 70f;
        private const int TITLE_MIN_FONT_SIZE = 36;
        private const float PILL_HEIGHT = 54f;
        private const float PILL_PADDING = 44f;
        private const float PILL_GAP = 14f;

        private const float CAPTION_HEIGHT = 36f;

        private const float REWARD_TILE_WIDTH = 176f;
        private const float REWARD_TILE_HEIGHT = 196f;
        private const float REWARD_TILE_GAP = 22f;
        private const float REWARD_DISC_SIZE = 96f;
        private const float REWARD_GLYPH_SIZE = 58f;
        private const float TILE_RADIUS = 36f;
        private const float TILE_LIP = 7f;
        private const float REWARD_CHECK_SIZE = 44f;

        private const float STREAK_HEIGHT = 150f;
        private const float STREAK_RADIUS = 36f;
        private const float STREAK_OUTLINE = 5f;
        private const float STREAK_PAD = 24f;
        private const float STREAK_FLAME_TILE = 78f;
        private const float STREAK_FLAME_GLYPH = 48f;
        private const float STREAK_TEXT_X = STREAK_PAD + STREAK_FLAME_TILE + 24f;
        private const float STREAK_PIP_WIDTH = 50f;
        private const float STREAK_PIP_HEIGHT = 18f;
        private const float STREAK_PIP_GAP = 9f;
        private const int STREAK_MAX_PIPS = 10;
        private const float STREAK_BONUS_DISC = 88f;
        private const float STREAK_BONUS_GLYPH = 52f;
        private const float STREAK_RIGHT_WIDTH = 150f;

        private const int GRID_COLUMNS = 5;
        private const float GRID_GAP = 16f;
        private const float GRID_CELL_WIDTH = (CONTENT_WIDTH - (GRID_GAP * (GRID_COLUMNS - 1))) / GRID_COLUMNS;
        private const float GRID_CELL_HEIGHT = 130f;
        private const float GRID_DISC_SIZE = 64f;
        private const float GRID_GLYPH_SIZE = 40f;

        private const float SOWER_HEIGHT = 112f;
        private const float SOWER_PAD = 26f;
        private const float SOWER_COIN_SIZE = 52f;
        private const float STEPPER_SIZE = 84f;
        private const float QUANTITY_WIDTH = 74f;

        private const float BUTTON_HEIGHT = 128f;
        private const float BUTTON_GAP = 22f;
        private const float AD_BUTTON_SHARE = 0.43f;
        private const float BUTTON_SLICE_SCALE = 2.5f;
        private const float TILE_SLICE_SCALE = 2f;
        private const float BUTTON_LABEL_RISE = 6f;
        private const float AD_CHIP_WIDTH = 108f;
        private const float AD_CHIP_HEIGHT = 52f;
        private const float AD_CHIP_GAP = 14f;
        private const float AD_CHIP_COIN_SIZE = 30f;

        private const float LIVES_ROW_HEIGHT = 112f;
        private const float LIVES_ROW_RADIUS = 36f;
        private const float LIVES_ROW_OUTLINE = 4f;
        private const float LIVES_ROW_PAD = 22f;
        private const float LIVES_HEART_SIZE = 76f;
        private const float LIVES_TEXT_GAP = 20f;
        private const float LOCK_FOOTER_HEIGHT = 112f;
        private const float LOCK_FOOTER_GLYPH = 46f;
        private const float MESSAGE_HEIGHT = 76f;
        private const float CLOSE_DISC_SIZE = 92f;
        private const float CLOSE_INSET = 36f;
        private const float CLOSE_BAR_THICKNESS = 9f;
        private const float CHECK_BAR_THICKNESS = 8f;

        private const string PROGRESS_SEPARATOR = "/";
        private const string TIMES = "×";
        private const string PLUS = "+";
        private const string QUESTION_MARK = "?";

        private static readonly Vector2 TextShadowOffset = new Vector2(0f, -4f);

        /// <summary>The power-up strip's kinds in strip order — the rewardable set, and the inventory
        /// grid's order. Hold and Coin Sower are earned by ad and have their own surfaces.</summary>
        private static readonly PowerUpKind[] GridKinds =
        {
            PowerUpKind.Bomb, PowerUpKind.RowClear, PowerUpKind.ColumnClear, PowerUpKind.Joker,
            PowerUpKind.ColorCleanser, PowerUpKind.Rotate, PowerUpKind.Reroll, PowerUpKind.DoubleMultiplier,
            PowerUpKind.GhostFit, PowerUpKind.PaintCross,
        };

        // The mockup's palette. Fixed rather than theme-tinted, like the shop's: the candy buttons and
        // coloured discs are the card's identity, and they read on every theme's scrim.
        private static readonly Color CardFace = new Color32(0xFD, 0xFC, 0xFB, 0xFF);
        private static readonly Color CardShadow = new Color32(0x2B, 0x26, 0x33, 0x3A);
        private static readonly Color TitleInk = new Color32(0x01, 0x57, 0x9B, 0xFF);
        private static readonly Color CaptionInk = new Color32(0x7A, 0x72, 0x60, 0xFF);
        private static readonly Color MutedInk = new Color32(0x8C, 0x84, 0x70, 0xFF);
        private static readonly Color LabelInk = new Color32(0x4A, 0x44, 0x58, 0xFF);
        private static readonly Color TileFace = Color.white;
        private static readonly Color TileLip = new Color32(0xE6, 0xE0, 0xD2, 0xFF);
        private static readonly Color MilestoneFill = new Color32(0xFF, 0xF1, 0xCC, 0xFF);
        private static readonly Color MilestoneInk = new Color32(0x8A, 0x61, 0x00, 0xFF);
        private static readonly Color StreakFill = new Color32(0xFF, 0xF4, 0xE3, 0xFF);
        private static readonly Color StreakOutline = new Color32(0xF7, 0xDD, 0xB5, 0xFF);
        private static readonly Color StreakLandsFill = new Color32(0xFF, 0xE9, 0xC2, 0xFF);
        private static readonly Color StreakLandsOutline = new Color32(0xF2, 0xB4, 0x5A, 0xFF);
        private static readonly Color StreakReplayFill = new Color32(0xF4, 0xF1, 0xEA, 0xFF);
        private static readonly Color StreakInk = new Color32(0x5A, 0x4A, 0x2E, 0xFF);
        private static readonly Color StreakAccentInk = new Color32(0xB4, 0x5A, 0x00, 0xFF);
        private static readonly Color FlameInk = new Color32(0xE0, 0x70, 0x1A, 0xFF);
        private static readonly Color PipOn = new Color32(0xF0, 0x8A, 0x24, 0xFF);
        private static readonly Color PipNext = new Color32(0xFF, 0xC4, 0x5C, 0xFF);
        private static readonly Color PipOff = new Color32(0xEA, 0xDF, 0xCB, 0xFF);
        private static readonly Color GainGreen = new Color32(0x3F, 0xAE, 0x3F, 0xFF);
        private static readonly Color CountInk = new Color32(0x6E, 0x67, 0x80, 0xFF);
        private static readonly Color SowerFill = new Color32(0xFF, 0xF6, 0xE2, 0xFF);
        private static readonly Color SowerInk = new Color32(0x91, 0x70, 0x0F, 0xFF);
        private static readonly Color SowerSubInk = new Color32(0xA8, 0x8B, 0x3A, 0xFF);
        private static readonly Color StepperFill = new Color32(0xF1, 0xEE, 0xE8, 0xFF);
        private static readonly Color AdBlue = new Color32(0x3F, 0x86, 0xEE, 0xFF);
        private static readonly Color StartGreen = new Color32(0x4C, 0xBA, 0x44, 0xFF);
        private static readonly Color ChipFill = new Color(1f, 1f, 1f, 0.24f);
        private static readonly Color TextShadowColour = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color CloseDisc = new Color32(0xF1, 0xEE, 0xE8, 0xFF);
        private static readonly Color CloseCross = new Color32(0x8A, 0x84, 0x96, 0xFF);
        private static readonly Color LockFooterFill = new Color32(0xEF, 0xEC, 0xF3, 0xFF);
        private static readonly Color LockInk = new Color32(0x5E, 0x5A, 0x66, 0xFF);
        private static readonly Color LockedIconTint = new Color(0.72f, 0.72f, 0.74f, 1f);
        private static readonly Color DimmedAlpha = new Color(1f, 1f, 1f, 0.5f);

        /// <summary>The lives row's pink (issue #478 mockup: #FFEEF0 on #F8C9D0), shared with the
        /// out-of-lives sheet and the fail card.</summary>
        private static readonly Color LivesFill = new Color32(0xFF, 0xEE, 0xF0, 0xFF);
        private static readonly Color LivesOutline = new Color32(0xF8, 0xC9, 0xD0, 0xFF);
        private static readonly Color LivesInk = new Color32(0x8E, 0x2A, 0x3A, 0xFF);
        private static readonly Color LivesSubInk = new Color32(0xB0, 0x48, 0x5A, 0xFF);
        private static readonly Color LivesCountOutline = new Color(0.45f, 0.05f, 0.08f, 0.9f);

        private static readonly Color MeadowInk = new Color32(0x3E, 0x9E, 0x5C, 0xFF);
        private static readonly Color WinterInk = new Color32(0x2E, 0x8F, 0xC2, 0xFF);
        private static readonly Color CityInk = new Color32(0xD2, 0x69, 0x1E, 0xFF);
        private static readonly Color NeighborhoodInk = new Color32(0xB8, 0x4A, 0x8C, 0xFF);
        private const float ZONE_PILL_FILL_ALPHA = 0.14f;

        private enum CardMode
        {
            Play,
            Replay,
            Locked,
        }

        private enum StreakState
        {
            Hidden,
            Progress,
            Lands,
            Replay,
        }

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(64);

        /// <summary>Scratch lists for one repaint, reused so an open allocates nothing per call.</summary>
        private readonly List<PowerUpKind> _levelRewards = new List<PowerUpKind>(4);
        private readonly List<PowerUpKind> _bonusRewards = new List<PowerUpKind>(4);

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
        [Tooltip("Coin glyph used for the sow row and the ad chip.")]
        [SerializeField] private Sprite _coinSprite;
        [Tooltip("Fallback icon for a level with no LevelIdentityCatalog icon.")]
        [SerializeField] private Sprite _coinCellSprite;
        [Tooltip("White flame glyph for the first-try streak row. Tinted at runtime.")]
        [SerializeField] private Sprite _flameSprite;
        [Tooltip("Full-colour heart (HudIcon_Heart) the lives row's count sits on.")]
        [SerializeField] private Sprite _heartSprite;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.08f, 0.09f, 0.16f, 0.58f);

        private PowerUpModel _powerUpModel;
        private LevelProgressionModel _levelProgressionModel;
        private RewardRuleModel _rewardRuleModel;
        private PowerUpSystem _powerUpSystem;
        private LevelProgressionSystem _levelProgressionSystem;
        private RewardRuleSystem _rewardRuleSystem;
        private LevelCoinCellSeedSystem _coinCellSeedSystem;
        private TimerRunSystem _timerRunSystem;
        private LocalizationSystem _localizationSystem;
        private PowerUpPriceConfig _priceConfig;
        private LevelCatalog _levelCatalog;
        private LevelIdentityCatalog _levelIdentityCatalog;
        private RewardRuleCatalog _rewardRuleCatalog;
        private PowerUpInventoryView _powerUpInventoryView;
        private LivesModel _livesModel;
        private LivesConfig _livesConfig;
        private LivesSystem _livesSystem;

        private Canvas _canvas;
        private CancellationToken _destroyToken;
        private GameObject _panel;
        private RectTransform _cardRect;
        private RectTransform _cardShadowRect;
        private RectTransform _closeButtonRect;

        private RectTransform _iconRect;
        private Image _iconImage;
        private RectTransform _lockBadgeRect;
        private RectTransform _checkBadgeRect;
        private Text _titleText;
        private RectTransform _pillRowRect;
        private RectTransform _zonePillRect;
        private Image _zonePillFill;
        private Text _zonePillText;
        private RectTransform _milestonePillRect;
        private Text _milestoneText;

        private Text _rewardsCaptionText;
        private RectTransform _rewardsRowRect;
        private RewardTile[] _rewardTiles;

        private RectTransform _streakRect;
        private Image _streakFill;
        private Image _streakOutline;
        private Text _streakLineText;
        private Image[] _streakPips;
        private RectTransform _streakBonusRect;
        private Image _streakBonusDisc;
        private Image _streakBonusGlyph;
        private RectTransform _streakPendingRect;
        private Text _streakRightCaption;
        private Text _streakRightFigure;

        private RectTransform _inventoryCaptionRect;
        private Text _inventoryCaptionText;
        private RectTransform _gridRect;
        private GridCell[] _gridCells;

        private RectTransform _sowerRect;
        private Text _sowerTitleText;
        private Text _sowerChargesText;
        private RectTransform _minusRect;
        private RectTransform _plusRect;
        private RectTransform _quantityRect;
        private Text _quantityText;

        private RectTransform _buttonsRect;
        private RectTransform _watchAdButtonRect;
        private Text _watchAdText;
        private RectTransform _adChipRect;
        private RectTransform _startButtonRect;
        private Text _startButtonText;

        private RectTransform _livesRowRect;
        private Text _livesCountText;
        private Text _livesLineText;
        private Text _livesHintText;

        private RectTransform _lockFooterRect;
        private Text _lockFooterText;
        private RectTransform _lockFooterGlyphRect;

        private RectTransform _messageRect;
        private Text _messageText;

        /// <summary>The level this card was opened for — the whole "a level is about to start" state.</summary>
        private int _pendingLevelNumber;

        /// <summary>Coin cells Start would sow; clamped to <see cref="MaxSowableQuantity"/> on every repaint.</summary>
        private int _pendingQuantity;

        /// <summary>A refusal to show in the footer, or null. Cleared on open.</summary>
        private string _messageOverride;

        /// <summary>True from a "watch ad" tap until the source answers, so a second tap cannot queue a
        /// second ad.</summary>
        private bool _isRequestingReward;

        private CardMode _mode;

        /// <summary>One reward tile: a white plate, a coloured disc with the kind's glyph, its name and
        /// "×1", plus a check for a replay's already-collected rewards.</summary>
        private sealed class RewardTile
        {
            internal RectTransform Root;
            internal CanvasGroup Group;
            internal Image Disc;
            internal Image Glyph;
            internal Text Name;
            internal Text Count;
            internal RectTransform Check;
        }

        /// <summary>One inventory grid cell: a coloured disc with the kind's glyph and its count.</summary>
        private sealed class GridCell
        {
            internal RectTransform Root;
            internal Image Disc;
            internal Image Glyph;
            internal Text Count;
        }

        [Inject]
        public void Construct(
            PowerUpModel powerUpModel,
            LevelProgressionModel levelProgressionModel,
            RewardRuleModel rewardRuleModel,
            PowerUpSystem powerUpSystem,
            LevelProgressionSystem levelProgressionSystem,
            RewardRuleSystem rewardRuleSystem,
            LevelCoinCellSeedSystem coinCellSeedSystem,
            TimerRunSystem timerRunSystem,
            LocalizationSystem localizationSystem,
            PowerUpPriceConfig priceConfig,
            LevelCatalog levelCatalog,
            LevelIdentityCatalog levelIdentityCatalog,
            RewardRuleCatalog rewardRuleCatalog,
            PowerUpInventoryView powerUpInventoryView,
            LivesModel livesModel,
            LivesConfig livesConfig,
            LivesSystem livesSystem)
        {
            _powerUpModel = powerUpModel;
            _levelProgressionModel = levelProgressionModel;
            _rewardRuleModel = rewardRuleModel;
            _powerUpSystem = powerUpSystem;
            _levelProgressionSystem = levelProgressionSystem;
            _rewardRuleSystem = rewardRuleSystem;
            _coinCellSeedSystem = coinCellSeedSystem;
            _timerRunSystem = timerRunSystem;
            _localizationSystem = localizationSystem;
            _priceConfig = priceConfig;
            _levelCatalog = levelCatalog;
            _levelIdentityCatalog = levelIdentityCatalog;
            _rewardRuleCatalog = rewardRuleCatalog;
            _powerUpInventoryView = powerUpInventoryView;
            _livesModel = livesModel;
            _livesConfig = livesConfig;
            _livesSystem = livesSystem;
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
            if (_powerUpModel == null || _levelProgressionModel == null || _rewardRuleModel == null
                || _powerUpSystem == null || _levelProgressionSystem == null || _rewardRuleSystem == null
                || _coinCellSeedSystem == null || _timerRunSystem == null || _localizationSystem == null
                || _priceConfig == null || _levelCatalog == null || _levelIdentityCatalog == null
                || _rewardRuleCatalog == null || _powerUpInventoryView == null
                || _livesModel == null || _livesConfig == null || _livesSystem == null)
            {
                Debug.LogError(
                    $"{nameof(LevelStartCardView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Observed rather than read once on open: the ad grant this card asks for lands here, and the
            // frontier can move while the card is closed.
            _powerUpModel.CoinSowerCount.Subscribe(OnChargesChanged).AddTo(_disposables);
            _levelProgressionModel.CurrentLevelNumber.Subscribe(OnLevelChanged).AddTo(_disposables);

            // Observed for the same reason: the out-of-lives sheet can open over this card, and the lives
            // an ad pays there must show here the moment the sheet closes (issue #478).
            _livesModel.CurrentLives.Subscribe(OnLivesChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the card for <paramref name="levelNumber"/> — any level, locked ones included (a
        /// read-only preview) — and holds the run's clock. Called by <see cref="LevelPathPanelView"/> when
        /// a node is tapped. Opens at a sow quantity of zero: the default answer to spending charges the
        /// player watched ads for must be the one that keeps them.
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
        /// Routes a tap while the panel is open: the close cross, the sow stepper, the ad and Start
        /// buttons; anything else on the card is swallowed, and only a tap on the scrim closes.
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

            bool sowerShown = _sowerRect.gameObject.activeSelf;
            if (sowerShown && Contains(_minusRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(_pendingQuantity - QUANTITY_STEP);
                return;
            }

            if (sowerShown && Contains(_plusRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(_pendingQuantity + QUANTITY_STEP);
                return;
            }

            // Tapping the figure takes as many as the player holds — the top of the range is a common
            // answer and stepping to it one at a time is a chore.
            if (sowerShown && Contains(_quantityRect, screenPosition, eventCamera))
            {
                SetPendingQuantity(MaxSowableQuantity());
                return;
            }

            bool buttonsShown = _buttonsRect.gameObject.activeSelf;
            if (buttonsShown && Contains(_watchAdButtonRect, screenPosition, eventCamera))
            {
                RequestReward();
                return;
            }

            if (buttonsShown && Contains(_startButtonRect, screenPosition, eventCamera))
            {
                ConfirmAndStart();
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

        /// <summary>Hides the card and releases the clock. Starts nothing.</summary>
        private void Close()
        {
            _panel.SetActive(false);
            _timerRunSystem.SetMenuPaused(false);
        }

        /// <summary>
        /// Commits the sow (if any) and starts the level: spend, then queue, then start — the run consumes
        /// the queue on <c>RunStartedMessage</c>, which the start publishes. A refused spend leaves the card
        /// open with a message rather than silently starting without what was asked for.
        /// <para>
        /// The lives gate goes first (issue #478): at zero lives the start would be refused anyway, and
        /// asking only after the spend would take the player's Coin Sower charges for a run that never
        /// deals. A refusal opens the out-of-lives sheet over this card and leaves the card as it is, so
        /// Start is still there once an ad (or the clock) has paid a life back.
        /// </para>
        /// </summary>
        private void ConfirmAndStart()
        {
            if (!_livesSystem.TryPassStartGate())
            {
                return;
            }

            if (_pendingQuantity > 0 && !_powerUpSystem.TrySpendCoinSowerBulk(_pendingQuantity))
            {
                SetMessage(_localizationSystem.Translate(LocalizationKeys.LEVEL_START_SOW_FAILED));
                return;
            }

            if (_pendingQuantity > 0)
            {
                _coinCellSeedSystem.QueueExtraCoinCells(_pendingQuantity);
            }

            if (!_levelProgressionSystem.TryStartPathLevel(_pendingLevelNumber))
            {
                SetMessage(_localizationSystem.Translate(LocalizationKeys.LEVEL_START_CANNOT_START));
                return;
            }

            Close();
        }

        /// <summary>Fire-and-forget earn request, mirroring <see cref="HoldSlotView"/>'s. The System banks
        /// and persists; the charge subscription repaints.</summary>
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
                // The View went away mid-request. The System only banks a reward it was handed.
            }
            finally
            {
                _isRequestingReward = false;
            }
        }

        /// <summary>The most charges the player could sow right now; zero while the kind is behind its
        /// level gate, so the stepper cannot move off zero there.</summary>
        private int MaxSowableQuantity()
        {
            if (!IsSowerUnlocked())
            {
                return 0;
            }

            return Mathf.Min(_priceConfig.CoinSowerMaxQuantity, _powerUpModel.CoinSowerCount.Value);
        }

        private bool IsSowerUnlocked()
            => PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.CoinSower, _levelProgressionModel.CurrentLevelNumber.Value);

        private void OnChargesChanged(int value) => Refresh();

        private void OnLevelChanged(int levelNumber) => Refresh();

        private void OnLivesChanged(int lives) => Refresh();

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

        private CardMode ModeFor(int levelNumber)
        {
            int frontier = _levelProgressionModel.CurrentLevelNumber.Value;
            if (levelNumber > frontier)
            {
                return CardMode.Locked;
            }

            return levelNumber < frontier ? CardMode.Replay : CardMode.Play;
        }

        /// <summary>
        /// Whether a first clear of the pending level would pay its reward at all. The frontier's clear
        /// pays only when it can advance — the last authored level advances nowhere and pays nothing
        /// (see <c>LevelProgressionSystem.TryAdvanceFrontierAfterPathLevel</c>).
        /// </summary>
        private bool ClearWouldPay() => _levelCatalog.Find(_pendingLevelNumber + 1) != null;

        /// <summary>Repaints every row from the models and re-stacks the card. Runs on an open, a stepper
        /// tap and a charge change — never per frame.</summary>
        private void Refresh()
        {
            if (_panel == null || _powerUpModel == null)
            {
                return;
            }

            _mode = ModeFor(_pendingLevelNumber);

            int maxQuantity = MaxSowableQuantity();
            if (_pendingQuantity > maxQuantity)
            {
                _pendingQuantity = maxQuantity;
            }

            CollectRewards();

            PaintHeader();
            PaintRewards();
            PaintStreak();
            PaintInventory();
            PaintSower();
            PaintLives();
            PaintButtons();

            string footer = _messageOverride;
            _messageText.text = footer;
            _messageRect.gameObject.SetActive(!string.IsNullOrEmpty(footer));

            LayoutCard();
        }

        /// <summary>
        /// Fills <see cref="_levelRewards"/> with what this level pays and <see cref="_bonusRewards"/> with
        /// the streak bonus a first-try clear would add — both through the payout's own pure functions.
        /// A replay lists the rewards it already paid; a locked preview lists what it will pay.
        /// </summary>
        private void CollectRewards()
        {
            _levelRewards.Clear();
            _bonusRewards.Clear();

            bool pays = _mode == CardMode.Replay || ClearWouldPay();
            if (pays)
            {
                _levelRewards.AddRange(LevelCompletionRewards.For(_pendingLevelNumber));
            }

            if (_mode == CardMode.Play && pays && _rewardRuleSystem.IsFirstTryPending(_pendingLevelNumber))
            {
                RewardRules.AppendFirstTryPayout(
                    _rewardRuleCatalog.Rules, _rewardRuleModel.FirstTryStreak.Value + 1, _pendingLevelNumber, _bonusRewards);
            }
        }

        private void PaintHeader()
        {
            LevelIdentityConfig identity = _levelIdentityCatalog.Find(_pendingLevelNumber);
            Sprite icon = identity != null && identity.Icon != null ? identity.Icon : _coinCellSprite;
            _iconImage.sprite = icon;
            _iconImage.color = _mode == CardMode.Locked ? LockedIconTint : Color.white;
            _lockBadgeRect.gameObject.SetActive(_mode == CardMode.Locked);
            _checkBadgeRect.gameObject.SetActive(_mode == CardMode.Replay);

            string levelName = identity != null && !string.IsNullOrEmpty(identity.NameKey)
                ? _localizationSystem.Translate(identity.NameKey)
                : string.Empty;
            _titleText.text = levelName.ToUpperInvariant();
            _titleText.gameObject.SetActive(!string.IsNullOrEmpty(levelName));

            LevelPathZoneKind zone = LevelPathZones.ZoneFor(_pendingLevelNumber);
            Color zoneInk = ZoneInk(zone);
            _stringBuilder.Clear();
            _stringBuilder.Append(_pendingLevelNumber);
            _zonePillText.text = _localizationSystem.Format(
                LocalizationKeys.LEVEL_START_PILL, _localizationSystem.Translate(ZoneKey(zone)), _stringBuilder.ToString());
            _zonePillText.color = zoneInk;
            _zonePillFill.color = HudChrome.WithAlpha(zoneInk, ZONE_PILL_FILL_ALPHA);

            bool milestone = LevelCompletionRewards.IsMilestone(_pendingLevelNumber);
            _milestonePillRect.gameObject.SetActive(milestone);
            _milestoneText.text = _localizationSystem.Translate(LocalizationKeys.LEVEL_START_MILESTONE);

            // The zone pill and the milestone chip are centred as one group.
            float zoneWidth = _zonePillText.preferredWidth + PILL_PADDING;
            SizePill(_zonePillRect, zoneWidth);
            float milestoneWidth = milestone ? _milestoneText.preferredWidth + PILL_PADDING : 0f;
            SizePill(_milestonePillRect, milestoneWidth);
            float groupWidth = zoneWidth + (milestone ? PILL_GAP + milestoneWidth : 0f);
            _zonePillRect.anchoredPosition = new Vector2((-groupWidth * 0.5f) + (zoneWidth * 0.5f), 0f);
            _milestonePillRect.anchoredPosition = new Vector2((groupWidth * 0.5f) - (milestoneWidth * 0.5f), 0f);
        }

        private static void SizePill(RectTransform pill, float width)
        {
            var size = new Vector2(width, PILL_HEIGHT);
            pill.sizeDelta = size;
            for (int childIndex = 0; childIndex < pill.childCount; childIndex++)
            {
                ((RectTransform)pill.GetChild(childIndex)).sizeDelta = size;
            }
        }

        private void PaintRewards()
        {
            string captionKey = _mode == CardMode.Replay
                ? LocalizationKeys.LEVEL_START_COLLECTED_CAPTION
                : (_mode == CardMode.Locked ? LocalizationKeys.LEVEL_START_LOCKED_CAPTION : LocalizationKeys.LEVEL_START_CLEAR_TO_WIN);
            _rewardsCaptionText.text = _localizationSystem.Translate(captionKey);

            bool show = _levelRewards.Count > 0;
            _rewardsCaptionText.gameObject.SetActive(show);
            _rewardsRowRect.gameObject.SetActive(show);

            int shown = Mathf.Min(_levelRewards.Count, _rewardTiles.Length);
            float rowWidth = (shown * REWARD_TILE_WIDTH) + (Mathf.Max(0, shown - 1) * REWARD_TILE_GAP);
            for (int tileIndex = 0; tileIndex < _rewardTiles.Length; tileIndex++)
            {
                RewardTile tile = _rewardTiles[tileIndex];
                bool active = tileIndex < shown;
                tile.Root.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                PowerUpKind kind = _levelRewards[tileIndex];
                tile.Root.anchoredPosition = new Vector2(
                    (-rowWidth * 0.5f) + (REWARD_TILE_WIDTH * 0.5f) + (tileIndex * (REWARD_TILE_WIDTH + REWARD_TILE_GAP)), 0f);
                tile.Disc.color = KindColour(kind);
                tile.Glyph.sprite = _powerUpInventoryView.IconFor(kind);
                tile.Name.text = _localizationSystem.Translate(PowerUpShopView.NameKeyOf(kind));
                tile.Count.color = KindColour(kind);
                tile.Count.text = TIMES + "1";
                tile.Group.alpha = _mode == CardMode.Replay ? DimmedAlpha.a : 1f;
                tile.Check.gameObject.SetActive(_mode == CardMode.Replay);
            }
        }

        /// <summary>
        /// The first-try streak as a reward item: x/n progress towards the rule closest to paying, the
        /// actual bonus when a first-try clear of this level completes it, or a "replays don't count"
        /// note. Hidden on a locked preview, when no rule is authored, and when the clear pays nothing.
        /// </summary>
        private void PaintStreak()
        {
            int streak = _rewardRuleModel.FirstTryStreak.Value;
            bool hasRule = RewardRules.TryGetNextPayout(
                _rewardRuleCatalog.Rules, RewardRuleCondition.ConsecutiveFirstTryClears, streak,
                out RewardRuleConfig nextRule, out int progress);

            StreakState state;
            if (!hasRule || _mode == CardMode.Locked || _levelRewards.Count == 0)
            {
                state = StreakState.Hidden;
            }
            else if (_mode == CardMode.Replay)
            {
                state = StreakState.Replay;
            }
            else
            {
                state = _bonusRewards.Count > 0 ? StreakState.Lands : StreakState.Progress;
            }

            _streakRect.gameObject.SetActive(state != StreakState.Hidden);
            if (state == StreakState.Hidden)
            {
                return;
            }

            int threshold = nextRule.Threshold;
            bool lands = state == StreakState.Lands;
            _streakFill.color = lands ? StreakLandsFill : (state == StreakState.Replay ? StreakReplayFill : StreakFill);
            _streakOutline.color = lands ? StreakLandsOutline : (state == StreakState.Replay ? TileLip : StreakOutline);

            string progressText = ProgressText(lands ? threshold : progress, threshold);
            switch (state)
            {
                case StreakState.Lands:
                    _streakLineText.text = _localizationSystem.Format(LocalizationKeys.LEVEL_START_STREAK_LANDS, progressText);
                    break;
                case StreakState.Replay:
                    _streakLineText.text = _localizationSystem.Translate(LocalizationKeys.LEVEL_START_STREAK_REPLAY);
                    break;
                default:
                    _streakLineText.text = _localizationSystem.Format(LocalizationKeys.LEVEL_START_STREAK_PROGRESS, progressText);
                    break;
            }

            int pipCount = Mathf.Min(threshold, STREAK_MAX_PIPS);
            int litCount = lands ? progress : Mathf.Min(progress, pipCount);
            for (int pipIndex = 0; pipIndex < _streakPips.Length; pipIndex++)
            {
                Image pip = _streakPips[pipIndex];
                bool active = pipIndex < pipCount;
                pip.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                bool isThisClear = lands && pipIndex >= litCount;
                pip.color = pipIndex < litCount ? PipOn : (isThisClear ? PipNext : PipOff);
            }

            _streakBonusRect.gameObject.SetActive(lands);
            _streakPendingRect.gameObject.SetActive(state == StreakState.Progress);
            _streakRightFigure.gameObject.SetActive(state == StreakState.Replay);

            _stringBuilder.Clear();
            _stringBuilder.Append(lands ? _bonusRewards.Count : nextRule.RewardCount);
            string countText = _stringBuilder.ToString();

            if (lands)
            {
                PowerUpKind bonus = _bonusRewards[0];
                _streakBonusDisc.color = KindColour(bonus);
                _streakBonusGlyph.sprite = _powerUpInventoryView.IconFor(bonus);
                _streakRightCaption.text = _localizationSystem.Format(LocalizationKeys.LEVEL_START_STREAK_BONUS, countText);
                _streakRightCaption.gameObject.SetActive(true);
            }
            else if (state == StreakState.Progress)
            {
                _streakRightCaption.text = _localizationSystem.Format(
                    LocalizationKeys.LEVEL_START_STREAK_AT, countText, ProgressText(threshold, threshold));
                _streakRightCaption.gameObject.SetActive(true);
            }
            else
            {
                _streakRightFigure.text = ProgressText(progress, threshold);
                _streakRightCaption.gameObject.SetActive(false);
            }
        }

        private string ProgressText(int value, int total)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(value);
            _stringBuilder.Append(PROGRESS_SEPARATOR);
            _stringBuilder.Append(total);
            return _stringBuilder.ToString();
        }

        /// <summary>
        /// The inventory grid: the strip kinds the player holds right now, with their counts. What this
        /// clear adds is already listed under "clear it to win", so it is not repeated here. Hidden on a
        /// locked preview and when the player holds nothing.
        /// </summary>
        private void PaintInventory()
        {
            int visible = 0;
            bool show = _mode != CardMode.Locked;
            for (int kindIndex = 0; kindIndex < GridKinds.Length; kindIndex++)
            {
                PowerUpKind kind = GridKinds[kindIndex];
                GridCell cell = _gridCells[kindIndex];
                int count = CountFor(kind);
                bool active = show && count > 0;
                cell.Root.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                int column = visible % GRID_COLUMNS;
                int row = visible / GRID_COLUMNS;
                cell.Root.anchoredPosition = new Vector2(
                    (-CONTENT_WIDTH * 0.5f) + (GRID_CELL_WIDTH * 0.5f) + (column * (GRID_CELL_WIDTH + GRID_GAP)),
                    -(GRID_CELL_HEIGHT * 0.5f) - (row * (GRID_CELL_HEIGHT + GRID_GAP)));
                visible++;

                cell.Disc.color = KindColour(kind);
                cell.Glyph.sprite = _powerUpInventoryView.IconFor(kind);

                _stringBuilder.Clear();
                _stringBuilder.Append(count);
                cell.Count.text = _stringBuilder.ToString();
            }

            _inventoryCaptionRect.gameObject.SetActive(visible > 0);
            _gridRect.gameObject.SetActive(visible > 0);
            if (visible == 0)
            {
                return;
            }

            _inventoryCaptionText.text = _localizationSystem.Translate(LocalizationKeys.LEVEL_START_YOUR_POWER_UPS);
            int rows = (visible + GRID_COLUMNS - 1) / GRID_COLUMNS;
            float height = (rows * GRID_CELL_HEIGHT) + ((rows - 1) * GRID_GAP);
            _gridRect.sizeDelta = new Vector2(CONTENT_WIDTH, height);
        }

        /// <summary>The compact sow row, shown only once Coin Sower is unlocked and the level can be
        /// started.</summary>
        private void PaintSower()
        {
            bool show = _mode != CardMode.Locked && IsSowerUnlocked();
            _sowerRect.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            _sowerTitleText.text = _localizationSystem.Translate(LocalizationKeys.LEVEL_START_SOW_ROW);
            _stringBuilder.Clear();
            _stringBuilder.Append(_powerUpModel.CoinSowerCount.Value);
            _sowerChargesText.text = _localizationSystem.Format(LocalizationKeys.LEVEL_START_CHARGES, _stringBuilder.ToString());

            _stringBuilder.Clear();
            _stringBuilder.Append(_pendingQuantity);
            _quantityText.text = _stringBuilder.ToString();
        }

        /// <summary>
        /// The lives row (issue #478): "17 / 20 lives" in bold over what the level costs, on the pink
        /// plate with the count in a small heart. Shown whenever the card can start the level — a replay
        /// costs a life on failure just as the frontier does — and hidden on a locked preview.
        /// </summary>
        private void PaintLives()
        {
            bool show = _mode != CardMode.Locked && _livesModel != null;
            _livesRowRect.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(_livesModel.CurrentLives.Value);
            string count = _stringBuilder.ToString();
            _livesCountText.text = count;

            _stringBuilder.Clear();
            _stringBuilder.Append(_livesConfig.RegenCap);
            _livesLineText.text = _localizationSystem.Format(LocalizationKeys.LEVEL_START_LIVES, count, _stringBuilder.ToString());
            _livesHintText.text = _localizationSystem.Translate(LocalizationKeys.LEVEL_START_LIVES_HINT);
        }

        private void PaintButtons()
        {
            bool locked = _mode == CardMode.Locked;
            _buttonsRect.gameObject.SetActive(!locked);
            _lockFooterRect.gameObject.SetActive(locked);

            if (locked)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(_levelProgressionModel.CurrentLevelNumber.Value);
                _lockFooterText.text = _localizationSystem.Format(LocalizationKeys.LEVEL_START_UNLOCK_HINT, _stringBuilder.ToString());
                float groupWidth = LOCK_FOOTER_GLYPH + 16f + _lockFooterText.preferredWidth;
                _lockFooterGlyphRect.anchoredPosition = new Vector2((-groupWidth * 0.5f) + (LOCK_FOOTER_GLYPH * 0.5f), 0f);
                _lockFooterText.rectTransform.anchoredPosition = new Vector2(
                    (groupWidth * 0.5f) - (_lockFooterText.preferredWidth * 0.5f), 0f);
                return;
            }

            _watchAdText.text = _localizationSystem.Translate(LocalizationKeys.LEVEL_START_WATCH_AD);
            LayoutAdButtonContent();

            if (_pendingQuantity > 0)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(_pendingQuantity);
                _startButtonText.text = _localizationSystem.Format(LocalizationKeys.LEVEL_START_START_SOW, _stringBuilder.ToString());
            }
            else
            {
                _startButtonText.text = _localizationSystem.Translate(
                    _mode == CardMode.Replay ? LocalizationKeys.LEVEL_START_PLAY_AGAIN : LocalizationKeys.LEVEL_START_START);
            }
        }

        /// <summary>Centres the ad caption and its "+2 coin" chip as one group.</summary>
        private void LayoutAdButtonContent()
        {
            float labelWidth = _watchAdText.preferredWidth;
            float groupWidth = labelWidth + AD_CHIP_GAP + AD_CHIP_WIDTH;
            RectTransform labelRect = _watchAdText.rectTransform;
            labelRect.sizeDelta = new Vector2(labelWidth, BUTTON_HEIGHT);
            labelRect.anchoredPosition = new Vector2((-groupWidth * 0.5f) + (labelWidth * 0.5f), BUTTON_LABEL_RISE);
            _adChipRect.anchoredPosition = new Vector2((groupWidth * 0.5f) - (AD_CHIP_WIDTH * 0.5f), BUTTON_LABEL_RISE);
        }

        private int CountFor(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.Bomb:
                    return _powerUpModel.BombCount.Value;
                case PowerUpKind.RowClear:
                    return _powerUpModel.RowClearCount.Value;
                case PowerUpKind.ColumnClear:
                    return _powerUpModel.ColumnClearCount.Value;
                case PowerUpKind.Joker:
                    return _powerUpModel.JokerCount.Value;
                case PowerUpKind.ColorCleanser:
                    return _powerUpModel.ColorCleanserCount.Value;
                case PowerUpKind.Rotate:
                    return _powerUpModel.RotateCount.Value;
                case PowerUpKind.Reroll:
                    return _powerUpModel.RerollCount.Value;
                case PowerUpKind.DoubleMultiplier:
                    return _powerUpModel.DoubleMultiplierCount.Value;
                case PowerUpKind.GhostFit:
                    return _powerUpModel.GhostFitCount.Value;
                case PowerUpKind.PaintCross:
                    return _powerUpModel.PaintCrossCount.Value;
                default:
                    return 0;
            }
        }

        /// <summary>Each kind's disc colour on this card — the mockup's per-kind candy palette.</summary>
        private static Color KindColour(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.Bomb:
                    return new Color32(0xE0, 0x52, 0x5F, 0xFF);
                case PowerUpKind.RowClear:
                    return new Color32(0x3E, 0x9E, 0x5C, 0xFF);
                case PowerUpKind.ColumnClear:
                    return new Color32(0x8A, 0x5C, 0xCB, 0xFF);
                case PowerUpKind.Joker:
                    return new Color32(0xE0, 0x8A, 0x1E, 0xFF);
                case PowerUpKind.ColorCleanser:
                    return new Color32(0x2E, 0x8F, 0xC2, 0xFF);
                case PowerUpKind.Rotate:
                    return new Color32(0xD9, 0x58, 0x8F, 0xFF);
                case PowerUpKind.Reroll:
                    return new Color32(0x4A, 0x76, 0xD6, 0xFF);
                case PowerUpKind.DoubleMultiplier:
                    return new Color32(0xD2, 0x69, 0x1E, 0xFF);
                case PowerUpKind.GhostFit:
                    return new Color32(0x66, 0x75, 0x8F, 0xFF);
                case PowerUpKind.PaintCross:
                    return new Color32(0xB8, 0x4A, 0x8C, 0xFF);
                default:
                    return MutedInk;
            }
        }

        private static Color ZoneInk(LevelPathZoneKind zone)
        {
            switch (zone)
            {
                case LevelPathZoneKind.Winter:
                    return WinterInk;
                case LevelPathZoneKind.City:
                    return CityInk;
                case LevelPathZoneKind.Neighborhood:
                    return NeighborhoodInk;
                default:
                    return MeadowInk;
            }
        }

        private static string ZoneKey(LevelPathZoneKind zone)
        {
            switch (zone)
            {
                case LevelPathZoneKind.Winter:
                    return LocalizationKeys.LEVEL_START_ZONE_WINTER;
                case LevelPathZoneKind.City:
                    return LocalizationKeys.LEVEL_START_ZONE_CITY;
                case LevelPathZoneKind.Neighborhood:
                    return LocalizationKeys.LEVEL_START_ZONE_NEIGHBORHOOD;
                default:
                    return LocalizationKeys.LEVEL_START_ZONE_MEADOW;
            }
        }

        /// <summary>
        /// Stacks the active rows top to bottom and sizes the card around them, so each mode's card is
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
            cursor = PlaceRow(_iconRect, ICON_SIZE + (ICON_FRAME * 2f), cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_titleText.rectTransform, TITLE_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_pillRowRect, PILL_HEIGHT, cursor, top, apply, CAPTION_GAP);
            cursor = PlaceRow(_rewardsCaptionText.rectTransform, CAPTION_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_rewardsRowRect, REWARD_TILE_HEIGHT, cursor, top, apply, CAPTION_GAP + 12f);
            cursor = PlaceRow(_streakRect, STREAK_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_inventoryCaptionRect, CAPTION_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_gridRect, _gridRect.sizeDelta.y, cursor, top, apply, CAPTION_GAP + 6f);
            cursor = PlaceRow(_sowerRect, SOWER_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_livesRowRect, LIVES_ROW_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_buttonsRect, BUTTON_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_lockFooterRect, LOCK_FOOTER_HEIGHT, cursor, top, apply, STACK_GAP);
            cursor = PlaceRow(_messageRect, MESSAGE_HEIGHT, cursor, top, apply, STACK_GAP);
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

            var panelObject = new GameObject("LevelStartPanel", typeof(RectTransform), typeof(Image));
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
                panelRect, "LevelStartCardShadow", new Vector2(CARD_WIDTH, 1f), Vector2.zero, CARD_RADIUS);
            cardShadow.color = CardShadow;
            _cardShadowRect = cardShadow.rectTransform;

            Image card = HudChrome.BuildRounded(
                panelRect, "LevelStartCard", new Vector2(CARD_WIDTH, 1f), Vector2.zero, CARD_RADIUS);
            card.color = CardFace;
            _cardRect = card.rectTransform;

            BuildIcon();

            _titleText = CreateText(_cardRect, "Title", _headerFontSize, _displayFont, TitleInk);
            _titleText.rectTransform.sizeDelta = new Vector2(CONTENT_WIDTH - (CLOSE_DISC_SIZE * 0.5f), TITLE_HEIGHT);
            _titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _titleText.resizeTextForBestFit = true;
            _titleText.resizeTextMinSize = TITLE_MIN_FONT_SIZE;
            _titleText.resizeTextMaxSize = _headerFontSize;

            BuildPills();

            _rewardsCaptionText = BuildCaption("RewardsCaption");
            BuildRewardsRow();
            BuildStreakRow();
            BuildInventory();
            BuildSowerRow();
            BuildLivesRow();
            BuildButtons();
            BuildLockFooter();

            _messageText = CreateText(_cardRect, "Message", _bodyFontSize - 4, _bodyFont, MutedInk);
            _messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _messageRect = _messageText.rectTransform;
            _messageRect.sizeDelta = new Vector2(CONTENT_WIDTH, MESSAGE_HEIGHT);

            BuildCloseButton();

            _panel = panelObject;
        }

        /// <summary>The level icon on a white frame with a soft drop, plus the lock (locked preview) and
        /// check (replay) badges on its corner.</summary>
        private void BuildIcon()
        {
            float framed = ICON_SIZE + (ICON_FRAME * 2f);
            _iconRect = HudChrome.CreateRect(_cardRect, "LevelIcon", new Vector2(framed, framed), Vector2.zero);

            Image drop = HudChrome.BuildRounded(
                _iconRect, "Drop", new Vector2(framed, framed), new Vector2(0f, -TILE_LIP * 1.5f), ICON_RADIUS + ICON_FRAME);
            drop.color = CardShadow;
            HudChrome.BuildRounded(
                _iconRect, "Frame", new Vector2(framed, framed), Vector2.zero, ICON_RADIUS + ICON_FRAME).color = Color.white;

            _iconImage = HudChrome.BuildGlyph(_iconRect, "Icon", _coinCellSprite, new Vector2(ICON_SIZE, ICON_SIZE), Vector2.zero);
            _iconImage.preserveAspect = true;

            var badgePosition = new Vector2(framed * 0.5f - 6f, -framed * 0.5f + 6f);
            _lockBadgeRect = HudChrome.CreateRect(_iconRect, "LockBadge", new Vector2(ICON_BADGE_SIZE, ICON_BADGE_SIZE), badgePosition);
            HudChrome.BuildCircle(_lockBadgeRect, "Disc", ICON_BADGE_SIZE, Vector2.zero).color = LockInk;
            HudChrome.BuildGlyph(
                _lockBadgeRect, "Lock", _lockSprite, new Vector2(ICON_BADGE_SIZE * 0.55f, ICON_BADGE_SIZE * 0.55f), Vector2.zero)
                .color = Color.white;

            _checkBadgeRect = HudChrome.CreateRect(_iconRect, "CheckBadge", new Vector2(ICON_BADGE_SIZE, ICON_BADGE_SIZE), badgePosition);
            HudChrome.BuildCircle(_checkBadgeRect, "Disc", ICON_BADGE_SIZE, Vector2.zero).color = GainGreen;
            BuildCheck(_checkBadgeRect, ICON_BADGE_SIZE * 0.5f, CHECK_BAR_THICKNESS);
        }

        private void BuildPills()
        {
            _pillRowRect = HudChrome.CreateRect(_cardRect, "PillRow", new Vector2(CONTENT_WIDTH, PILL_HEIGHT), Vector2.zero);

            _zonePillRect = HudChrome.CreateRect(_pillRowRect, "ZonePill", new Vector2(1f, PILL_HEIGHT), Vector2.zero);
            _zonePillFill = HudChrome.BuildRounded(_zonePillRect, "Plate", new Vector2(1f, PILL_HEIGHT), Vector2.zero, PILL_HEIGHT * 0.5f);
            _zonePillText = CreateText(_zonePillRect, "Label", _bodyFontSize - 4, _bodyFont, MeadowInk);

            _milestonePillRect = HudChrome.CreateRect(_pillRowRect, "MilestonePill", new Vector2(1f, PILL_HEIGHT), Vector2.zero);
            HudChrome.BuildRounded(_milestonePillRect, "Plate", new Vector2(1f, PILL_HEIGHT), Vector2.zero, PILL_HEIGHT * 0.5f)
                .color = MilestoneFill;
            _milestoneText = CreateText(_milestonePillRect, "Label", _bodyFontSize - 4, _bodyFont, MilestoneInk);
        }

        /// <summary>A small left-aligned uppercase section caption.</summary>
        private Text BuildCaption(string objectName)
        {
            Text caption = HudChrome.CreateLabel(
                _cardRect, objectName, _bodyFontSize - 6, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(-CONTENT_WIDTH * 0.5f, 0f), _bodyFont);
            caption.color = CaptionInk;
            return caption;
        }

        private void BuildRewardsRow()
        {
            _rewardsRowRect = HudChrome.CreateRect(
                _cardRect, "RewardsRow", new Vector2(CONTENT_WIDTH, REWARD_TILE_HEIGHT), Vector2.zero);
            _rewardTiles = new RewardTile[LevelCompletionRewards.MILESTONE_REWARD_COUNT];
            var tileSize = new Vector2(REWARD_TILE_WIDTH, REWARD_TILE_HEIGHT);
            for (int tileIndex = 0; tileIndex < _rewardTiles.Length; tileIndex++)
            {
                var tile = new RewardTile();
                tile.Root = HudChrome.CreateRect(_rewardsRowRect, $"RewardTile_{tileIndex}", tileSize, Vector2.zero);
                tile.Group = tile.Root.gameObject.AddComponent<CanvasGroup>();
                tile.Group.blocksRaycasts = false;
                HudChrome.BuildRounded(tile.Root, "Lip", tileSize, new Vector2(0f, -TILE_LIP), TILE_RADIUS).color = TileLip;
                HudChrome.BuildRounded(tile.Root, "Face", tileSize, Vector2.zero, TILE_RADIUS).color = TileFace;

                float discY = (REWARD_TILE_HEIGHT * 0.5f) - 22f - (REWARD_DISC_SIZE * 0.5f);
                tile.Disc = HudChrome.BuildCircle(tile.Root, "Disc", REWARD_DISC_SIZE, new Vector2(0f, discY));
                tile.Glyph = HudChrome.BuildGlyph(
                    tile.Root, "Glyph", null, new Vector2(REWARD_GLYPH_SIZE, REWARD_GLYPH_SIZE), new Vector2(0f, discY));
                tile.Glyph.color = Color.white;

                tile.Name = CreateText(tile.Root, "Name", _bodyFontSize - 8, _bodyFont, LabelInk);
                tile.Name.rectTransform.sizeDelta = new Vector2(REWARD_TILE_WIDTH - 16f, 52f);
                tile.Name.rectTransform.anchoredPosition = new Vector2(0f, -36f);
                tile.Name.lineSpacing = 0.85f;
                tile.Name.horizontalOverflow = HorizontalWrapMode.Wrap;
                tile.Name.resizeTextForBestFit = true;
                tile.Name.resizeTextMinSize = 16;
                tile.Name.resizeTextMaxSize = _bodyFontSize - 8;

                tile.Count = CreateText(tile.Root, "Count", _bodyFontSize - 2, _displayFont, MutedInk);
                tile.Count.rectTransform.anchoredPosition = new Vector2(0f, -78f);

                var checkPosition = new Vector2((REWARD_TILE_WIDTH * 0.5f) - 30f, (REWARD_TILE_HEIGHT * 0.5f) - 30f);
                tile.Check = HudChrome.CreateRect(tile.Root, "Check", new Vector2(REWARD_CHECK_SIZE, REWARD_CHECK_SIZE), checkPosition);
                HudChrome.BuildCircle(tile.Check, "Disc", REWARD_CHECK_SIZE, Vector2.zero).color = MutedInk;
                BuildCheck(tile.Check, REWARD_CHECK_SIZE * 0.5f, CHECK_BAR_THICKNESS * 0.6f);

                _rewardTiles[tileIndex] = tile;
            }
        }

        /// <summary>The first-try streak row: flame tile, one line of copy, n pips and the bonus on the
        /// right (a disc when this clear lands it, a dashed "?" while pending, a figure on a replay).</summary>
        private void BuildStreakRow()
        {
            var size = new Vector2(CONTENT_WIDTH, STREAK_HEIGHT);
            _streakRect = HudChrome.CreateRect(_cardRect, "StreakRow", size, Vector2.zero);
            _streakFill = HudChrome.BuildRounded(_streakRect, "Fill", size, Vector2.zero, STREAK_RADIUS);
            _streakOutline = HudChrome.BuildOutline(_streakRect, "Outline", size, Vector2.zero, STREAK_RADIUS, STREAK_OUTLINE);

            float left = -CONTENT_WIDTH * 0.5f;
            var flameTileSize = new Vector2(STREAK_FLAME_TILE, STREAK_FLAME_TILE);
            var flamePosition = new Vector2(left + STREAK_PAD + (STREAK_FLAME_TILE * 0.5f), 0f);
            HudChrome.BuildRounded(_streakRect, "FlameTile", flameTileSize, flamePosition, 24f).color = Color.white;
            HudChrome.BuildGlyph(
                _streakRect, "Flame", _flameSprite, new Vector2(STREAK_FLAME_GLYPH, STREAK_FLAME_GLYPH), flamePosition).color = FlameInk;

            float textWidth = CONTENT_WIDTH - STREAK_TEXT_X - STREAK_RIGHT_WIDTH;
            _streakLineText = HudChrome.CreateLabel(
                _streakRect, "Line", _bodyFontSize - 6, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(left + STREAK_TEXT_X, 22f), _bodyFont);
            _streakLineText.color = StreakInk;
            _streakLineText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _streakLineText.rectTransform.sizeDelta = new Vector2(textWidth, 60f);
            _streakLineText.resizeTextForBestFit = true;
            _streakLineText.resizeTextMinSize = 18;
            _streakLineText.resizeTextMaxSize = _bodyFontSize - 6;

            _streakPips = new Image[STREAK_MAX_PIPS];
            for (int pipIndex = 0; pipIndex < STREAK_MAX_PIPS; pipIndex++)
            {
                float x = left + STREAK_TEXT_X + (STREAK_PIP_WIDTH * 0.5f) + (pipIndex * (STREAK_PIP_WIDTH + STREAK_PIP_GAP));
                _streakPips[pipIndex] = HudChrome.BuildRounded(
                    _streakRect, $"Pip_{pipIndex}", new Vector2(STREAK_PIP_WIDTH, STREAK_PIP_HEIGHT), new Vector2(x, -34f),
                    STREAK_PIP_HEIGHT * 0.5f);
            }

            float rightX = (CONTENT_WIDTH * 0.5f) - (STREAK_RIGHT_WIDTH * 0.5f) - 8f;
            var discPosition = new Vector2(rightX, 14f);
            _streakBonusDisc = HudChrome.BuildCircle(_streakRect, "BonusDisc", STREAK_BONUS_DISC, discPosition);
            _streakBonusRect = _streakBonusDisc.rectTransform;
            _streakBonusGlyph = HudChrome.BuildGlyph(
                _streakBonusRect, "Glyph", null, new Vector2(STREAK_BONUS_GLYPH, STREAK_BONUS_GLYPH), Vector2.zero);
            _streakBonusGlyph.color = Color.white;

            _streakPendingRect = HudChrome.CreateRect(
                _streakRect, "PendingBonus", new Vector2(STREAK_BONUS_DISC, STREAK_BONUS_DISC), discPosition);
            HudChrome.BuildCircle(_streakPendingRect, "Face", STREAK_BONUS_DISC, Vector2.zero).color = Color.white;
            HudChrome.BuildOutline(
                _streakPendingRect, "Ring", new Vector2(STREAK_BONUS_DISC, STREAK_BONUS_DISC), Vector2.zero,
                STREAK_BONUS_DISC * 0.5f, 4f).color = StreakLandsOutline;
            CreateText(_streakPendingRect, "Mark", _bodyFontSize + 4, _displayFont, FlameInk).text = QUESTION_MARK;

            _streakRightCaption = CreateText(_streakRect, "RightCaption", _bodyFontSize - 12, _bodyFont, StreakAccentInk);
            _streakRightCaption.rectTransform.anchoredPosition = new Vector2(rightX, -48f);

            _streakRightFigure = CreateText(_streakRect, "RightFigure", _bodyFontSize + 4, _displayFont, MutedInk);
            _streakRightFigure.rectTransform.anchoredPosition = new Vector2(rightX, 0f);
        }

        private void BuildInventory()
        {
            _inventoryCaptionRect = HudChrome.CreateRect(
                _cardRect, "InventoryCaption", new Vector2(CONTENT_WIDTH, CAPTION_HEIGHT), Vector2.zero);
            _inventoryCaptionText = HudChrome.CreateLabel(
                _inventoryCaptionRect, "Caption", _bodyFontSize - 6, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(-CONTENT_WIDTH * 0.5f, 0f), _bodyFont);
            _inventoryCaptionText.color = CaptionInk;

            _gridRect = HudChrome.CreateRect(_cardRect, "InventoryGrid", new Vector2(CONTENT_WIDTH, GRID_CELL_HEIGHT), Vector2.zero);
            _gridCells = new GridCell[GridKinds.Length];
            var cellSize = new Vector2(GRID_CELL_WIDTH, GRID_CELL_HEIGHT);
            for (int kindIndex = 0; kindIndex < GridKinds.Length; kindIndex++)
            {
                var cell = new GridCell();
                cell.Root = HudChrome.CreateRect(_gridRect, $"Cell_{GridKinds[kindIndex]}", cellSize, Vector2.zero);

                // Anchored to the grid's top edge, so cells are placed downwards from it whatever
                // height the grid is given for the rows showing (see PaintInventory).
                cell.Root.anchorMin = new Vector2(0.5f, 1f);
                cell.Root.anchorMax = new Vector2(0.5f, 1f);
                HudChrome.BuildRounded(cell.Root, "Lip", cellSize, new Vector2(0f, -TILE_LIP * 0.8f), 30f).color = TileLip;
                HudChrome.BuildRounded(cell.Root, "Face", cellSize, Vector2.zero, 30f).color = TileFace;

                var discPosition = new Vector2(0f, 18f);
                cell.Disc = HudChrome.BuildCircle(cell.Root, "Disc", GRID_DISC_SIZE, discPosition);
                cell.Glyph = HudChrome.BuildGlyph(
                    cell.Root, "Glyph", null, new Vector2(GRID_GLYPH_SIZE, GRID_GLYPH_SIZE), discPosition);
                cell.Glyph.color = Color.white;

                cell.Count = CreateText(cell.Root, "Count", _bodyFontSize - 6, _bodyFont, CountInk);
                cell.Count.rectTransform.anchoredPosition = new Vector2(0f, -40f);

                _gridCells[kindIndex] = cell;
            }
        }

        private void BuildSowerRow()
        {
            var size = new Vector2(CONTENT_WIDTH, SOWER_HEIGHT);
            _sowerRect = HudChrome.CreateRect(_cardRect, "SowerRow", size, Vector2.zero);
            HudChrome.BuildRounded(_sowerRect, "Plate", size, Vector2.zero, 36f).color = SowerFill;

            float left = -CONTENT_WIDTH * 0.5f;
            HudChrome.BuildGlyph(
                _sowerRect, "Coin", _coinSprite, new Vector2(SOWER_COIN_SIZE, SOWER_COIN_SIZE),
                new Vector2(left + SOWER_PAD + (SOWER_COIN_SIZE * 0.5f), 0f)).color = Color.white;

            float textX = left + SOWER_PAD + SOWER_COIN_SIZE + 20f;
            _sowerTitleText = HudChrome.CreateLabel(
                _sowerRect, "Title", _bodyFontSize - 4, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(textX, 16f), _bodyFont);
            _sowerTitleText.color = SowerInk;
            _sowerChargesText = HudChrome.CreateLabel(
                _sowerRect, "Charges", _bodyFontSize - 10, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(textX, -20f), _bodyFont);
            _sowerChargesText.color = SowerSubInk;

            float right = CONTENT_WIDTH * 0.5f - 18f;
            float plusX = right - (STEPPER_SIZE * 0.5f);
            float quantityX = plusX - (STEPPER_SIZE * 0.5f) - (QUANTITY_WIDTH * 0.5f);
            float minusX = quantityX - (QUANTITY_WIDTH * 0.5f) - (STEPPER_SIZE * 0.5f);
            _minusRect = BuildStepper("MinusButton", minusX, "–");
            _plusRect = BuildStepper("PlusButton", plusX, "+");

            _quantityRect = HudChrome.CreateRect(
                _sowerRect, "Quantity", new Vector2(QUANTITY_WIDTH, SOWER_HEIGHT), new Vector2(quantityX, 0f));
            _quantityText = CreateText(_quantityRect, "Figure", _bodyFontSize + 18, _displayFont, TitleInk);
        }

        private RectTransform BuildStepper(string objectName, float x, string glyph)
        {
            var size = new Vector2(STEPPER_SIZE, STEPPER_SIZE);
            RectTransform stepperRect = HudChrome.CreateRect(_sowerRect, objectName, size, new Vector2(x, 0f));
            BuildSlicedPlate(stepperRect, "Plate", _tileSprite, size, TILE_SLICE_SCALE).color = StepperFill;

            Text glyphText = CreateText(stepperRect, "Glyph", _bodyFontSize + 12, _displayFont, TitleInk);
            glyphText.text = glyph;
            glyphText.rectTransform.anchoredPosition = new Vector2(0f, 4f);
            return stepperRect;
        }

        /// <summary>The pink lives row: a small heart holding the count, the bold "n / cap lives" line and
        /// the cost hint under it. Laid out as the sow row is — glyph on the left, two left-aligned lines.</summary>
        private void BuildLivesRow()
        {
            var size = new Vector2(CONTENT_WIDTH, LIVES_ROW_HEIGHT);
            _livesRowRect = HudChrome.CreateRect(_cardRect, "LivesRow", size, Vector2.zero);
            HudChrome.BuildRounded(_livesRowRect, "Plate", size, Vector2.zero, LIVES_ROW_RADIUS).color = LivesFill;
            HudChrome.BuildOutline(_livesRowRect, "Outline", size, Vector2.zero, LIVES_ROW_RADIUS, LIVES_ROW_OUTLINE)
                .color = LivesOutline;

            float left = -CONTENT_WIDTH * 0.5f;
            var heartSize = new Vector2(LIVES_HEART_SIZE, LIVES_HEART_SIZE);
            var heartPosition = new Vector2(left + LIVES_ROW_PAD + (LIVES_HEART_SIZE * 0.5f), 0f);
            Image heart = HudChrome.BuildGlyph(_livesRowRect, "Heart", _heartSprite, heartSize, heartPosition);
            heart.preserveAspect = true;
            heart.color = _heartSprite != null ? Color.white : Color.clear;

            _livesCountText = CreateText(_livesRowRect, "Count", _bodyFontSize - 4, _displayFont, Color.white);
            _livesCountText.rectTransform.sizeDelta = heartSize;
            _livesCountText.rectTransform.anchoredPosition = heartPosition + new Vector2(0f, LIVES_HEART_SIZE * 0.06f);
            Outline countOutline = _livesCountText.gameObject.AddComponent<Outline>();
            countOutline.effectColor = LivesCountOutline;
            countOutline.effectDistance = new Vector2(2f, -2f);

            float textX = left + LIVES_ROW_PAD + LIVES_HEART_SIZE + LIVES_TEXT_GAP;
            _livesLineText = HudChrome.CreateLabel(
                _livesRowRect, "Lives", _bodyFontSize - 2, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(textX, 18f), _bodyFont);
            _livesLineText.color = LivesInk;
            _livesHintText = HudChrome.CreateLabel(
                _livesRowRect, "Hint", _bodyFontSize - 10, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(textX, -20f), _bodyFont);
            _livesHintText.color = LivesSubInk;
        }

        /// <summary>Watch Ad (blue, "+2 coin" chip) and Start (green) side by side on one row.</summary>
        private void BuildButtons()
        {
            _buttonsRect = HudChrome.CreateRect(_cardRect, "Buttons", new Vector2(CONTENT_WIDTH, BUTTON_HEIGHT), Vector2.zero);

            float adWidth = (CONTENT_WIDTH - BUTTON_GAP) * AD_BUTTON_SHARE;
            float startWidth = CONTENT_WIDTH - BUTTON_GAP - adWidth;
            float left = -CONTENT_WIDTH * 0.5f;

            var adSize = new Vector2(adWidth, BUTTON_HEIGHT);
            _watchAdButtonRect = HudChrome.CreateRect(
                _buttonsRect, "WatchAdButton", adSize, new Vector2(left + (adWidth * 0.5f), 0f));
            BuildSlicedPlate(_watchAdButtonRect, "Plate", _buttonSprite, adSize, BUTTON_SLICE_SCALE).color = AdBlue;
            _watchAdText = CreateButtonText(_watchAdButtonRect, "Caption", _buttonFontSize - 6);

            var chipSize = new Vector2(AD_CHIP_WIDTH, AD_CHIP_HEIGHT);
            _adChipRect = HudChrome.CreateRect(_watchAdButtonRect, "Chip", chipSize, Vector2.zero);
            HudChrome.BuildRounded(_adChipRect, "Plate", chipSize, Vector2.zero, AD_CHIP_HEIGHT * 0.5f).color = ChipFill;
            Text chipText = CreateText(_adChipRect, "Amount", _bodyFontSize - 4, _bodyFont, Color.white);
            _stringBuilder.Clear();
            _stringBuilder.Append(PLUS);
            _stringBuilder.Append(CHARGES_PER_AD);
            chipText.text = _stringBuilder.ToString();
            chipText.rectTransform.anchoredPosition = new Vector2(-16f, 2f);
            AddTextShadow(chipText);
            HudChrome.BuildGlyph(
                _adChipRect, "Coin", _coinSprite, new Vector2(AD_CHIP_COIN_SIZE, AD_CHIP_COIN_SIZE), new Vector2(26f, 0f))
                .color = Color.white;

            var startSize = new Vector2(startWidth, BUTTON_HEIGHT);
            _startButtonRect = HudChrome.CreateRect(
                _buttonsRect, "StartButton", startSize, new Vector2((CONTENT_WIDTH * 0.5f) - (startWidth * 0.5f), 0f));
            BuildSlicedPlate(_startButtonRect, "Plate", _buttonSprite, startSize, BUTTON_SLICE_SCALE).color = StartGreen;
            _startButtonText = CreateButtonText(_startButtonRect, "Caption", _buttonFontSize);
            _startButtonText.rectTransform.sizeDelta = new Vector2(startWidth - 24f, BUTTON_HEIGHT);
            _startButtonText.rectTransform.anchoredPosition = new Vector2(0f, BUTTON_LABEL_RISE);
            _startButtonText.resizeTextForBestFit = true;
            _startButtonText.resizeTextMinSize = 24;
            _startButtonText.resizeTextMaxSize = _buttonFontSize;
        }

        /// <summary>The locked preview's footer in place of the buttons: "Clear Level N to unlock".</summary>
        private void BuildLockFooter()
        {
            var size = new Vector2(CONTENT_WIDTH, LOCK_FOOTER_HEIGHT);
            _lockFooterRect = HudChrome.CreateRect(_cardRect, "LockFooter", size, Vector2.zero);
            HudChrome.BuildRounded(_lockFooterRect, "Plate", size, Vector2.zero, 40f).color = LockFooterFill;
            Image glyph = HudChrome.BuildGlyph(
                _lockFooterRect, "Lock", _lockSprite, new Vector2(LOCK_FOOTER_GLYPH, LOCK_FOOTER_GLYPH), Vector2.zero);
            glyph.color = LockInk;
            _lockFooterGlyphRect = glyph.rectTransform;
            _lockFooterText = CreateText(_lockFooterRect, "Label", _bodyFontSize - 2, _bodyFont, LockInk);
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

        /// <summary>A white tick drawn as two rotated bars — the check badge's glyph, with no asset.</summary>
        private static void BuildCheck(RectTransform parent, float length, float thickness)
        {
            Image shortBar = HudChrome.BuildRounded(
                parent, "CheckShort", new Vector2(length * 0.5f, thickness), new Vector2(-length * 0.22f, -length * 0.08f), thickness * 0.5f);
            shortBar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            shortBar.color = Color.white;

            Image longBar = HudChrome.BuildRounded(
                parent, "CheckLong", new Vector2(length, thickness), new Vector2(length * 0.14f, length * 0.06f), thickness * 0.5f);
            longBar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            longBar.color = Color.white;
        }

        /// <summary>A white glossy sprite, 9-sliced to <paramref name="size"/>; the caller tints it. Falls
        /// back to a plain rounded plate if the sprite is not assigned.</summary>
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

        private Text CreateButtonText(RectTransform parent, string objectName, int fontSize)
        {
            Text text = CreateText(parent, objectName, fontSize, _bodyFont, Color.white);
            AddTextShadow(text);
            return text;
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
