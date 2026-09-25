using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay;
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
    /// The one end-of-run card (issue #266), drawn in the storefront vocabulary of the hub cards:
    /// an awning, a title worded for why the run ended, the run's score in a sunken well over a
    /// record plate and a total plate, every badge that unlocked during the run, and the actions —
    /// play again, change mode (Timed), next level (Path). It replaces the result summary of #220 and
    /// the separate game-over card it used to sit over: the result and the choice of what to do next
    /// are one card now, and there is no "tap to continue" step between them.
    /// <para>
    /// Holds no logic and writes no model. The run score, the reason, the mode and the level played
    /// are captured on <see cref="GameOverMessage"/>, because the next run resets all of them and a
    /// language switch must still be able to re-word the card. The lifetime total is observed on
    /// <see cref="ProfileModel.TotalScoreEarned"/> rather than read once, because
    /// <see cref="CurrencySystem"/> banks the run into it on the very same message and this View must
    /// not depend on which subscriber the broker calls first. A record set this run is remembered off
    /// <see cref="NewRecordMessage"/> and marked on the record plate.
    /// </para>
    /// <para>
    /// The badge list (issue #221) is read from <see cref="BadgeModel.UnlockedThisRun"/>, which
    /// <see cref="BadgeSystem"/> clears at run start. A row is tappable while its reward is
    /// unclaimed; the tap goes to <see cref="BadgeSystem.ClaimReward"/> and the row repaints as done
    /// through <see cref="BadgeModel.Revision"/>. With no unlocks the section is not drawn and the
    /// card is shorter by exactly that much.
    /// </para>
    /// <para>
    /// Modal while open: <see cref="BoardInputView"/> routes every tap into <see cref="HandleTap"/>,
    /// which claims a badge itself and hands anything else back as a <see cref="RunEndAction"/> for
    /// the input View to carry out. There is no close control and a tap outside the card does
    /// nothing; the card closes on <see cref="RunStartedMessage"/>, which every restart ends in, or on
    /// <see cref="RunRescuedMessage"/> (issue #371), when a no-moves ending is taken back by the
    /// rewarded-ad rescue and the same run carries on with a fresh dock.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunResultView : MonoBehaviour
    {
        // Layout, in canvas reference pixels. The mockup is 390px wide against this canvas's 1080, so
        // widths are its figures at 2.77x; heights are tightened so the tallest card (three stars,
        // four badge rows, two buttons) still clears the safe area.
        private const float CARD_CORNER_RADIUS = 72f;
        private const float CARD_SHADOW_DROP = 28f;

        /// <summary>Where the card's vertical centre sits whatever its height: a little below the
        /// canvas centre, so the tallest card runs from about +700 to about -800.</summary>
        private const float CARD_CENTRE_Y = -50f;

        private const float SIDE_INSET = 33f;
        private const float HEADER_TOP_PAD = 40f;
        private const float ADORNMENT_HEIGHT = 80f;
        private const float ADORNMENT_GAP = 14f;
        private const float TITLE_HEIGHT = 80f;
        private const float REASON_HEIGHT = 40f;
        private const float WELL_MARGIN = 30f;
        private const float WELL_CORNER_RADIUS = 50f;
        private const float WELL_PAD = 24f;
        private const float SCORE_LABEL_HEIGHT = 30f;
        private const float SCORE_VALUE_HEIGHT = 120f;
        private const float STAT_GAP = 16f;
        private const float STAT_PLATE_HEIGHT = 120f;
        private const float STAT_PLATE_GAP = 22f;
        private const float STAT_PLATE_RADIUS = 39f;
        private const float STAT_LABEL_TOP = 22f;
        private const float STAT_VALUE_BOTTOM = 26f;
        private const float TROPHY_SIZE = 28f;
        private const float TROPHY_GAP = 8f;
        private const float NEW_TAG_WIDTH = 110f;
        private const float NEW_TAG_HEIGHT = 40f;
        private const float COIN_BOX_TOP_GAP = 24f;
        private const float COIN_BOX_HEIGHT = 110f;
        private const float COIN_BOX_RADIUS = 39f;
        private const float COIN_BOX_PAD = 22f;
        private const float COIN_DISC_SIZE = 84f;
        private const float COIN_GLYPH_SIZE = 50f;
        private const float COIN_ICON_TEXT_GAP = 22f;
        private const float COIN_CAPTION_TOP = 20f;
        private const float COIN_VALUE_BOTTOM = 22f;
        private const float COIN_PILL_WIDTH = 220f;
        private const float COIN_PILL_HEIGHT = 84f;

        /// <summary>The level-reward box (issue #462): the coin box's plate and caption, with one
        /// accent disc per power-up the level paid, right-aligned where the coin box has its pill.
        /// Discs are the coin box's icon disc size, so both rows read as the same family.</summary>
        private const float REWARD_BOX_ICON_GAP = 14f;
        private const float REWARD_GLYPH_SIZE = 56f;

        /// <summary>Most discs the reward row can show: a milestone's bundle plus one first-try streak
        /// bonus (issue #464). A rule authored to pay more shows its first this-many.</summary>
        private const int REWARD_ROW_CAPACITY = LevelCompletionRewards.MILESTONE_REWARD_COUNT + 1;

        /// <summary>Seconds the coin balance takes to count up to its post-level figure. The score
        /// card's own count-up length (<see cref="ScoreView"/>), so the two read as the same gesture.</summary>
        private const float COIN_COUNT_UP_DURATION = 0.4f;

        /// <summary>The fail card's life row (issue #478): the coin box's footprint, in the lives pink.</summary>
        private const float LIVES_HEART_SIZE = 84f;
        private const float LIVES_BADGE_SIZE = 46f;
        private const float LIVES_ROW_OUTLINE = 4f;

        /// <summary>Between the count change and the refill countdown on the life row's detail line. A
        /// symbol, not a word — nothing here for a translator.</summary>
        private const string LIVES_DETAIL_SEPARATOR = " · ";

        /// <summary>The badge on the life row's heart. A minus sign and a digit: the same everywhere.</summary>
        private const string LIFE_LOST_BADGE = "\u22121";

        private static readonly Color LivesFill = new Color32(0xFF, 0xEE, 0xF0, 0xFF);
        private static readonly Color LivesOutline = new Color32(0xF8, 0xC9, 0xD0, 0xFF);
        private static readonly Color LivesInk = new Color32(0x8E, 0x2A, 0x3A, 0xFF);
        private static readonly Color LivesSubInk = new Color32(0xB0, 0x48, 0x5A, 0xFF);
        private static readonly Color LifeLostRed = new Color32(0xE0, 0x3E, 0x4E, 0xFF);
        private static readonly Color LivesCountOutline = new Color(0.45f, 0.05f, 0.08f, 0.9f);

        private const float BADGES_TOP_GAP = 24f;
        private const float BADGES_HEADING_HEIGHT = 30f;
        private const float BADGES_HEADING_GAP = 14f;
        private const float BADGES_HEADING_INSET = 11f;
        private const float BADGE_ROW_HEIGHT = 110f;
        private const float BADGE_ROW_GAP = 12f;
        private const float BADGE_ROW_RADIUS = 39f;
        private const float BADGE_ROW_PAD = 22f;
        private const float BADGE_ICON_DISC_SIZE = 84f;
        private const float BADGE_ICON_GLYPH_SIZE = 46f;
        private const float BADGE_ICON_TEXT_GAP = 22f;
        private const float BADGE_CLAIM_WIDTH = 220f;
        private const float BADGE_CLAIM_HEIGHT = 84f;
        private const float BADGE_CLAIM_COIN_SIZE = 50f;
        private const float BADGE_CLAIM_COIN_GAP = 10f;
        private const float BADGE_DONE_DISC_SIZE = 80f;
        private const float BADGE_DONE_CHECK_SIZE = 40f;
        private const float BUTTONS_TOP_PAD = 24f;
        private const float BUTTON_HEIGHT = 116f;
        private const float BUTTON_GAP = 20f;

        // The Path success row (issue #464): a square restart on the left, the next-level button filling
        // the rest — the next level's icon inset on its left, "NEXT · N" over its name, a chevron right.
        private const float NEXT_ROW_HEIGHT = 150f;
        private const float NEXT_ICON_SIZE = 116f;
        private const float NEXT_ICON_FRAME = 6f;
        private const float NEXT_ICON_RADIUS = 30f;
        private const float NEXT_ICON_INSET = 16f;
        private const float NEXT_TEXT_GAP = 22f;
        private const float NEXT_CHEVRON_SIZE = 44f;
        private const float NEXT_CHEVRON_INSET = 28f;
        private const float RESTART_GLYPH_SIZE = 64f;
        private const float RESTART_INLINE_GLYPH_SIZE = 48f;
        private const float RESTART_INLINE_GAP = 16f;
        private static readonly Color NextSubLabelInk = new Color(0.92f, 0.98f, 0.9f, 1f);
        private const float BOTTOM_PAD = 36f;
        private const float STAR_DISC_SIZE = 76f;
        private const float STAR_GLYPH_SIZE = 44f;
        private const float STAR_GAP = 16f;
        private const int STAR_COUNT = 3;
        private const float CLOCK_PILL_WIDTH = 230f;
        private const float CLOCK_PILL_HEIGHT = 70f;
        private const float CLOCK_GLYPH_SIZE = 36f;
        private const float CLOCK_GLYPH_GAP = 10f;

        /// <summary>Derived: the well's full height, which never changes.</summary>
        private const float WELL_HEIGHT =
            (WELL_PAD * 2f) + SCORE_LABEL_HEIGHT + SCORE_VALUE_HEIGHT + STAT_GAP + STAT_PLATE_HEIGHT;

        /// <summary>Rows the card can draw. More unlocks in one run than this are simply not listed;
        /// they stay claimable from the badge wall, and a run that fells five badges is not a real
        /// case worth a scrolling list here.</summary>
        private const int BADGE_ROW_COUNT = 4;

        /// <summary>Which theme kinds tint the buttons: the primary restart, the secondary "change
        /// mode", and the green "next level" advance — the same kinds the profile card's buttons take.</summary>
        private const int PRIMARY_KIND = 1;
        private const int SECONDARY_KIND = 2;

        /// <summary>Button slots the card can draw, stacked top to bottom. Three is the most any card
        /// needs: a Classic no-moves ending with a rescue on offer (issue #371) — "watch ad", "play
        /// again", "change mode". Every other card uses one or two and hides the rest.</summary>
        private const int BUTTON_SLOT_COUNT = 3;

        /// <summary>The record value's ink: the accent pulled toward black, as <see cref="ScoreView"/>
        /// paints its best figure, so the record reads as gold without washing out on the pale plate.</summary>
        private const float RECORD_VALUE_SHADE = 0.35f;

        /// <summary>Drop and alpha of the title's and the buttons' lettering shadow: the mockup's
        /// "0 3px 0 rgba(43,38,51,0.12)" and "0 2px 0 rgba(0,0,0,0.25)".</summary>
        private const float TITLE_SHADOW_DROP = 6f;
        private const float TITLE_SHADOW_ALPHA = 0.12f;
        private const float BUTTON_SHADOW_DROP = 4f;
        private const float BUTTON_SHADOW_ALPHA = 0.25f;

        /// <summary>Prefix on the claim amount, so "+50" reads as something to collect. A symbol, not a
        /// word — nothing here for a translator.</summary>
        private const string REWARD_PREFIX = "+";

        /// <summary>What the timed clock reads when the round has ended. Digits and a colon: the same
        /// in every language, and always this, since the card only shows it for a time-up.</summary>
        private const string CLOCK_AT_TIME_UP = "00:00";

        [Header("Layout")]
        [Tooltip("Width of the card, in reference pixels. Its height follows its content.")]
        [SerializeField] private float _cardWidth = 960f;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        [Header("Type")]
        [SerializeField] private int _titleFontSize = 60;
        [SerializeField] private int _reasonFontSize = 30;
        [SerializeField] private int _captionFontSize = 26;
        [SerializeField] private int _scoreFontSize = 120;
        [SerializeField] private int _statFontSize = 52;
        [SerializeField] private int _badgeNameFontSize = 34;
        [SerializeField] private int _claimFontSize = 36;
        [SerializeField] private int _buttonFontSize = 48;

        [Header("Art")]
        [Tooltip("The chunky display face for the title, the numbers and the buttons. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("The heavy label face for the small uppercase captions and the badge names. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _labelFont;

        [Tooltip("White 9-sliced glossy button, shared with the shop. Tinted at runtime from the theme.")]
        [SerializeField] private Sprite _buttonSprite;

        [Tooltip("White restart (circular arrow) glyph on the play-again button (issue #464).")]
        [SerializeField] private Sprite _restartSprite;

        [Tooltip("White chevron on the next-level button (issue #464).")]
        [SerializeField] private Sprite _chevronSprite;

        [Tooltip("White trophy silhouette beside the record caption. Hidden when unassigned.")]
        [SerializeField] private Sprite _trophySprite;

        [Tooltip("White clock silhouette on the timed card's 00:00 pill. Hidden when unassigned.")]
        [SerializeField] private Sprite _clockSprite;

        [Tooltip("The coin on a badge row's claim button. Hidden when unassigned.")]
        [SerializeField] private Sprite _coinSprite;

        [Tooltip("Full-colour heart (HudIcon_Heart) for the fail card's life row.")]
        [SerializeField] private Sprite _heartSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);
        private readonly BadgeRow[] _badgeRows = new BadgeRow[BADGE_ROW_COUNT];
        private readonly Image[] _starDiscs = new Image[STAR_COUNT];
        private readonly Image[] _starDiscShadows = new Image[STAR_COUNT];
        private readonly Image[] _starGlyphs = new Image[STAR_COUNT];
        private readonly ActionButton[] _buttons = new ActionButton[BUTTON_SLOT_COUNT];

        private ScoreModel _scoreModel;
        private ProfileModel _profileModel;

        /// <summary>Source of a power-up's glyph for the level-reward box — the strip's own art, as
        /// the Level Path's reward badges and the info popup use.</summary>
        private PowerUpInventoryView _powerUpInventoryView;
        private ISubscriber<LevelAdvancedMessage> _levelAdvancedSubscriber;
        private RewardRuleModel _rewardRuleModel;
        private PathRunModel _pathRunModel;
        private TimedHighScoreModel _timedHighScoreModel;
        private BadgeModel _badgeModel;
        private BadgeSystem _badgeSystem;
        private BadgeCatalog _badgeCatalog;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private LevelCatalog _levelCatalog;
        private LevelIdentityCatalog _levelIdentityCatalog;
        private LivesModel _livesModel;
        private LivesConfig _livesConfig;

        /// <summary>True while the buttons share one row: a Path success with a next level (issue #464).</summary>
        private bool _buttonsInRow;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private ISubscriber<NewRecordMessage> _newRecordSubscriber;
        private ISubscriber<RunRescuedMessage> _runRescuedSubscriber;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardShadow;
        private Image _cardPlate;
        private HudChrome.Awning _awning;

        private RectTransform _starsRoot;
        private RectTransform _clockRoot;
        private Image _clockPlate;
        private Image _clockGlyph;
        private Text _clockText;
        private Text _titleText;
        private Shadow _titleShadow;
        private Text _reasonText;

        private RectTransform _wellRoot;
        private Image _wellLip;
        private Image _wellFace;
        private Text _scoreCaptionText;
        private Text _scoreValueText;
        private StatPlate _recordPlate;
        private StatPlate _totalPlate;
        private Image _trophyImage;
        private RectTransform _trophyRect;
        private RectTransform _newTagRoot;
        private Image _newTagPlate;
        private Text _newTagText;

        private RectTransform _coinBoxRoot;
        private Image _coinBoxShadow;
        private Image _coinBoxPlate;
        private Image _coinBoxDisc;
        private Image _coinBoxGlyph;
        private Text _coinBoxCaptionText;
        private Text _coinBoxValueText;
        private Image _coinBoxPillPlate;
        private Text _coinBoxPillText;

        /// <summary>The fail card's "You lost a life" row (issue #478): the new count in a heart with a
        /// "−1" badge, and "17 → 16 lives · +5 in 12:34" beside it. Shown only when the run on the card
        /// actually cost a life — see <see cref="RefreshLivesRow"/>.</summary>
        private RectTransform _livesRowRoot;
        private Text _livesRowCountText;
        private Text _livesRowTitleText;
        private Text _livesRowDetailText;

        /// <summary>Everything this clear paid: the level's own reward (issue #462) followed by any
        /// first-try streak bonus (issue #464), in one row.</summary>
        private RewardRow _levelRewardRow;

        /// <summary>Scratch list for the reward row's kinds, reused per open.</summary>
        private readonly List<PowerUpKind> _shownRewards = new List<PowerUpKind>(REWARD_ROW_CAPACITY);

        /// <summary>
        /// The level whose first clear this run paid its power-up reward, or 0 when none did. Set off
        /// <see cref="LevelAdvancedMessage"/> — published only when a clear moves the frontier, which is
        /// exactly when <see cref="LevelProgressionSystem"/> pays the reward, and before the game over
        /// that opens this card — and cleared on run start. A replay never advances the frontier, so a
        /// replayed level's card shows no reward box: it paid none.
        /// </summary>
        private int _rewardedLevelNumber;

        private Text _badgesHeadingText;
        private Text _claimHintText;

        private ThemeDefinition _currentTheme;

        /// <summary>The score of the run that just ended, captured on game over: the model's own value
        /// is reset by the next run, and a locale change must still be able to repaint this.</summary>
        private int _runScore;

        /// <summary><see cref="ProfileModel.CoinBalance"/> as the run began, so the coins the level
        /// itself paid out can be told from the balance the player walked in with. Captured on
        /// <see cref="RunStartedMessage"/>, which every Path level start ends in.</summary>
        private int _coinsAtRunStart;

        /// <summary>What the run just ended added to the coin balance. Captured on game over for the
        /// same reason as <see cref="_runScore"/>: the next run moves the balance on.</summary>
        private int _coinsEarnedThisRun;

        /// <summary>The figure the coin box is currently showing, mid count-up.</summary>
        private int _displayedCoins;

        private CancellationToken _destroyToken;
        private CancellationTokenSource _coinCountUpCts;

        /// <summary>Why the last run ended. Kept so a language switch can re-word the card without
        /// waiting for the next game over — the wording is reason-specific.</summary>
        private GameOverReason _lastReason = GameOverReason.NoMovesLeft;

        /// <summary>The mode the last run was played in, captured with the reason for the same
        /// re-word-on-language-switch reason: which stats the plates show and which buttons the card
        /// offers follow from it, and a mode switch from the hub must not re-dress a card that is
        /// already up.</summary>
        private GameMode _lastMode = GameMode.Endless;

        /// <summary>The timed round length of the last run, in seconds, for the record caption and
        /// the reason line. Captured, since the picker can change it while the card is up.</summary>
        private float _lastDurationSeconds;

        /// <summary><see cref="PathRunModel.ActiveLevelNumber"/> at the moment the last game over was
        /// shown. Captured rather than read live: Path mode can replay an already-unlocked level below
        /// the progression frontier, so the level actually played has to outlive the message.</summary>
        private int _playedLevelNumber;

        /// <summary>Whether the catalog authors a level past <see cref="_playedLevelNumber"/>. Only
        /// meaningful while <see cref="_lastReason"/> is <see cref="GameOverReason.LevelCompleted"/>.</summary>
        private bool _hasNextLevel;

        /// <summary>Whether <see cref="NewRecordMessage"/> fired during the run that just ended.
        /// Cleared on run start, so a record from an earlier run can never tag a later card.</summary>
        private bool _isNewRecordThisRun;

        /// <summary>
        /// <see cref="GameOverMessage.IsRescueAvailable"/> of the last game over (issue #371): whether
        /// the card offers "watch ad" beside the restart. Captured with the reason so a language switch
        /// re-words the same buttons; withdrawn by <see cref="WithdrawRescueOffer"/> once the rescue
        /// has been refused, since the System consumes the offer and a second tap would do nothing.
        /// </summary>
        private bool _isRescueAvailable;

        /// <summary>One built badge row. Rebuilt never, repainted on every open and every claim.</summary>
        private sealed class BadgeRow
        {
            internal BadgeRow(
                RectTransform root, Image shadow, Image plate, Image iconDisc, Image iconGlyph, Text nameText,
                Image claimPlate, Image claimCoin, Text claimText, Image doneDisc, Image doneCheck)
            {
                Root = root;
                Shadow = shadow;
                Plate = plate;
                IconDisc = iconDisc;
                IconGlyph = iconGlyph;
                NameText = nameText;
                ClaimPlate = claimPlate;
                ClaimCoin = claimCoin;
                ClaimText = claimText;
                DoneDisc = doneDisc;
                DoneCheck = doneCheck;
            }

            internal RectTransform Root { get; }

            internal Image Shadow { get; }

            internal Image Plate { get; }

            internal Image IconDisc { get; }

            internal Image IconGlyph { get; }

            internal Text NameText { get; }

            internal Image ClaimPlate { get; }

            internal Image ClaimCoin { get; }

            internal Text ClaimText { get; }

            internal Image DoneDisc { get; }

            internal Image DoneCheck { get; }

            /// <summary>Id of the badge this row currently draws, or null while it draws none.</summary>
            internal string BadgeId { get; set; }

            /// <summary>Whether the row currently offers a claim. Cached from the last repaint so the
            /// tap path does not re-derive it.</summary>
            internal bool IsClaimable { get; set; }
        }

        /// <summary>One of the two stat plates under the score: a caption over a display figure.</summary>
        private sealed class StatPlate
        {
            internal StatPlate(RectTransform root, Image shadow, Image plate, Text captionText, Text valueText)
            {
                Root = root;
                Shadow = shadow;
                Plate = plate;
                CaptionText = captionText;
                ValueText = valueText;
            }

            internal RectTransform Root { get; }

            internal Image Shadow { get; }

            internal Image Plate { get; }

            internal Text CaptionText { get; }

            internal Text ValueText { get; }
        }

        /// <summary>One reward row on a Path clear's card (issues #462, #464): a plate, a caption, and
        /// a fixed set of accent discs with power-up glyphs, of which only as many as were paid show.</summary>
        private sealed class RewardRow
        {
            internal RewardRow(RectTransform root, Image shadow, Image plate, Text captionText, Image[] discs, Image[] glyphs)
            {
                Root = root;
                Shadow = shadow;
                Plate = plate;
                CaptionText = captionText;
                Discs = discs;
                Glyphs = glyphs;
            }

            internal RectTransform Root { get; }

            internal Image Shadow { get; }

            internal Image Plate { get; }

            internal Text CaptionText { get; }

            internal Image[] Discs { get; }

            internal Image[] Glyphs { get; }
        }

        /// <summary>One glossy action button: the pill is the hit area, the label sits on it. What
        /// the button does and which kind tints it are set per open in <see cref="RefreshButtons"/>.</summary>
        private sealed class ActionButton
        {
            internal ActionButton(
                Image plate, Text labelText, Image restartGlyph, RectTransform levelIconRoot, Image levelIcon,
                Text subLabelText, Image chevron)
            {
                Plate = plate;
                Rect = plate.rectTransform;
                LabelText = labelText;
                RestartGlyph = restartGlyph;
                LevelIconRoot = levelIconRoot;
                LevelIcon = levelIcon;
                SubLabelText = subLabelText;
                Chevron = chevron;
            }

            /// <summary>The restart arrow: the whole face of the square play-again button, or inline
            /// before the label on a wide restart (issue #464).</summary>
            internal Image RestartGlyph { get; }

            /// <summary>The next level's icon on its white frame, inset on the next-level button's left.</summary>
            internal RectTransform LevelIconRoot { get; }

            internal Image LevelIcon { get; }

            /// <summary>The next level's name under "NEXT · N".</summary>
            internal Text SubLabelText { get; }

            internal Image Chevron { get; }

            internal Image Plate { get; }

            internal RectTransform Rect { get; }

            internal Text LabelText { get; }

            internal RunEndAction Action { get; set; }

            internal int Kind { get; set; }
        }

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            ProfileModel profileModel,
            PathRunModel pathRunModel,
            TimedHighScoreModel timedHighScoreModel,
            BadgeModel badgeModel,
            BadgeSystem badgeSystem,
            BadgeCatalog badgeCatalog,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            LevelCatalog levelCatalog,
            LevelIdentityCatalog levelIdentityCatalog,
            PowerUpInventoryView powerUpInventoryView,
            ISubscriber<LevelAdvancedMessage> levelAdvancedSubscriber,
            RewardRuleModel rewardRuleModel,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<NewRecordMessage> newRecordSubscriber,
            ISubscriber<RunRescuedMessage> runRescuedSubscriber,
            LivesModel livesModel,
            LivesConfig livesConfig)
        {
            _livesModel = livesModel;
            _livesConfig = livesConfig;
            _scoreModel = scoreModel;
            _profileModel = profileModel;
            _pathRunModel = pathRunModel;
            _timedHighScoreModel = timedHighScoreModel;
            _badgeModel = badgeModel;
            _badgeSystem = badgeSystem;
            _badgeCatalog = badgeCatalog;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
            _levelCatalog = levelCatalog;
            _levelIdentityCatalog = levelIdentityCatalog;
            _powerUpInventoryView = powerUpInventoryView;
            _levelAdvancedSubscriber = levelAdvancedSubscriber;
            _rewardRuleModel = rewardRuleModel;
            _gameOverSubscriber = gameOverSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
            _newRecordSubscriber = newRecordSubscriber;
            _runRescuedSubscriber = runRescuedSubscriber;
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
            if (_scoreModel == null || _profileModel == null || _pathRunModel == null || _timedHighScoreModel == null
                || _badgeModel == null || _badgeSystem == null || _badgeCatalog == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null || _gameModeSystem == null
                || _timedModeSystem == null || _gameOverSubscriber == null || _runStartedSubscriber == null
                || _newRecordSubscriber == null || _runRescuedSubscriber == null
                || _powerUpInventoryView == null || _levelAdvancedSubscriber == null || _rewardRuleModel == null
                || _livesModel == null || _livesConfig == null)
            {
                Debug.LogError($"{nameof(RunResultView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // All of these repaint whether the panel is showing or hidden, so it is already correct the
            // next time a run ends.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _profileModel.TotalScoreEarned.Subscribe(OnLifetimeTotalChanged).AddTo(_disposables);

            // Every unlock and every claim bumps this, so the row just tapped repaints in place.
            _badgeModel.Revision.Subscribe(OnBadgesChanged).AddTo(_disposables);

            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
            _newRecordSubscriber.Subscribe(OnNewRecord).AddTo(_disposables);
            _runRescuedSubscriber.Subscribe(OnRunRescued).AddTo(_disposables);
            _levelAdvancedSubscriber.Subscribe(OnLevelAdvanced).AddTo(_disposables);

            // Observed rather than read on game over (issue #478): LivesSystem charges the life on the
            // same GameOverMessage this card opens on, and which subscriber the broker calls first must
            // not decide whether the row shows. A rescue clears it, and the card closes then anyway.
            _livesModel.LivesBeforeLastCharge.Subscribe(OnLivesChargeChanged).AddTo(_disposables);
            _livesModel.SecondsUntilRefill.Subscribe(OnSecondsUntilRefillChanged).AddTo(_disposables);

            // A defensive baseline: every real run start replaces this, but a card that somehow opens
            // without one then reads "nothing earned" rather than crediting the whole balance.
            _coinsAtRunStart = _profileModel.CoinBalance.Value;
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            CancelCoinCountUp();
        }

        /// <summary>True while the card is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// The level number "Next level" would start, or -1 when there is none — i.e. whenever that
        /// button is not on the card. <see cref="BoardInputView"/> reads this once <see cref="HandleTap"/>
        /// has come back with <see cref="RunEndAction.NextLevel"/>.
        /// </summary>
        internal int NextLevelNumber => _hasNextLevel ? _playedLevelNumber + 1 : -1;

        /// <summary>
        /// Routes a tap while open. A tap on a badge row that still has a reward claims it and keeps
        /// the card up, so the player sees the row turn done and the HUD coins tick; a tap on one of
        /// the buttons comes back as the action for the caller to carry out; anything else is
        /// <see cref="RunEndAction.None"/> — there is nothing on this card that dismisses it.
        /// </summary>
        internal RunEndAction HandleTap(Vector2 screenPosition)
        {
            if (!IsOpen)
            {
                return RunEndAction.None;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            for (int rowIndex = 0; rowIndex < _badgeRows.Length; rowIndex++)
            {
                BadgeRow row = _badgeRows[rowIndex];
                if (!row.IsClaimable || !row.Root.gameObject.activeSelf)
                {
                    continue;
                }

                if (RectTransformUtility.RectangleContainsScreenPoint(row.Root, screenPosition, eventCamera))
                {
                    // The repaint comes back through BadgeModel.Revision, not from here; and the System
                    // refuses a stale claim, so a double tap cannot pay twice.
                    _badgeSystem.ClaimReward(row.BadgeId);
                    return RunEndAction.None;
                }
            }

            for (int buttonIndex = 0; buttonIndex < _buttons.Length; buttonIndex++)
            {
                ActionButton button = _buttons[buttonIndex];
                if (button.Rect.gameObject.activeSelf
                    && RectTransformUtility.RectangleContainsScreenPoint(button.Rect, screenPosition, eventCamera))
                {
                    return button.Action;
                }
            }

            return RunEndAction.None;
        }

        /// <summary>
        /// Takes "watch ad" off the card (issue #371). <see cref="BoardInputView"/> calls this once
        /// <c>BoardSystem.TryApplyNoMovesRescueAsync</c> has come back false — the ad was refused,
        /// failed or cancelled — because the System consumes the offer on any outcome and the button
        /// would otherwise sit there doing nothing. The rest of the card is untouched: the run stays
        /// ended and the restart is still there to tap.
        /// </summary>
        internal void WithdrawRescueOffer()
        {
            if (!_isRescueAvailable)
            {
                return;
            }

            _isRescueAvailable = false;
            RefreshButtons();
            Layout();
        }

        private void OnGameOver(GameOverMessage message)
        {
            _runScore = _scoreModel.Score.Value;

            // Coin cells pay out during play, so the balance is already final here; nothing spends
            // coins inside a level, but the clamp keeps a negative out of the "+N" all the same.
            _coinsEarnedThisRun = Mathf.Max(0, _profileModel.CoinBalance.Value - _coinsAtRunStart);

            _lastReason = message.Reason;
            _isRescueAvailable = message.IsRescueAvailable;
            _lastMode = _gameModeSystem.CurrentMode.Value;
            _lastDurationSeconds = _timedModeSystem.SelectedDuration.Value;

            // The level actually played, not the progression frontier — Path mode can replay an
            // already-unlocked level below it. Captured now because ActiveLevelNumber can move on to
            // whatever the player taps next before this card is dismissed.
            _playedLevelNumber = _pathRunModel.ActiveLevelNumber.Value;
            _hasNextLevel = message.Reason == GameOverReason.LevelCompleted
                && _levelCatalog != null
                && _levelCatalog.Find(_playedLevelNumber + 1) != null;

            RefreshAll();

            _panel.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void OnRunStarted(RunStartedMessage message)
        {
            _isNewRecordThisRun = false;
            _rewardedLevelNumber = 0;
            _coinsAtRunStart = _profileModel.CoinBalance.Value;
            CancelCoinCountUp();
            _panel.SetActive(false);
        }

        /// <summary>The ending was taken back (issue #371): the same run carries on with a fresh dock,
        /// so the card simply goes away. Nothing else is reset — the record flag and the badge list
        /// belong to the run, and the run is still the same one.</summary>
        private void OnRunRescued(RunRescuedMessage message)
        {
            _isRescueAvailable = false;
            _panel.SetActive(false);
        }

        private void OnNewRecord(NewRecordMessage message) => _isNewRecordThisRun = true;

        private void OnLivesChargeChanged(int livesBeforeCharge)
        {
            RefreshLivesRow();
            if (IsOpen)
            {
                Layout();
            }
        }

        /// <summary>Keeps the row's "+5 in mm:ss" ticking while the card is up; nothing to do otherwise.</summary>
        private void OnSecondsUntilRefillChanged(int secondsUntilRefill)
        {
            if (IsOpen && _livesRowRoot != null && _livesRowRoot.gameObject.activeSelf)
            {
                PaintLivesDetail();
            }
        }

        /// <summary>A first clear just moved the frontier past the level it cleared — and paid that
        /// level's reward. See <see cref="_rewardedLevelNumber"/>.</summary>
        private void OnLevelAdvanced(LevelAdvancedMessage message)
            => _rewardedLevelNumber = message.CurrentLevelNumber - 1;

        private void OnLifetimeTotalChanged(int total) => RefreshStats();

        private void OnBadgesChanged(int revision)
        {
            RefreshBadges();
            Layout();
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            _cardShadow.color = theme.CardShadow;
            _cardPlate.color = theme.CardBackground;
            _awning.Paint(theme.Accent);

            _titleText.color = theme.Accent;
            _titleShadow.effectColor = HudChrome.WithAlpha(theme.Ink, TITLE_SHADOW_ALPHA);
            _reasonText.color = theme.SoftInk;

            for (int starIndex = 0; starIndex < STAR_COUNT; starIndex++)
            {
                _starDiscShadows[starIndex].color = HudChrome.Darken(theme.Accent, HudChrome.LIP_SHADE);
                _starDiscs[starIndex].color = theme.Accent;
                _starGlyphs[starIndex].color = Color.white;
            }

            _clockPlate.color = theme.Accent;
            _clockGlyph.color = _clockSprite != null ? Color.white : Color.clear;
            _clockText.color = Color.white;

            _wellLip.color = HudChrome.WellLipTint(theme.CardBackground, theme.Ink);
            _wellFace.color = HudChrome.WellTint(theme.CardBackground, theme.Ink);
            _scoreCaptionText.color = theme.SoftInk;
            _scoreValueText.color = theme.Ink;

            PaintStatPlate(_recordPlate, HudChrome.Darken(theme.Accent, RECORD_VALUE_SHADE));
            PaintStatPlate(_totalPlate, theme.Ink);
            _trophyImage.color = _trophySprite != null ? theme.Accent : Color.clear;
            _newTagPlate.color = theme.Accent;
            _newTagText.color = Color.white;

            PaintCoinBox();
            PaintRewardRow(_levelRewardRow);

            _badgesHeadingText.color = theme.SoftInk;
            _claimHintText.color = theme.Accent;

            RefreshBadges();
            for (int buttonIndex = 0; buttonIndex < _buttons.Length; buttonIndex++)
            {
                PaintButton(_buttons[buttonIndex]);
            }
        }

        /// <summary>The coin box in the card's own vocabulary: a badge row's plate and gold icon disc,
        /// with the "+N" pill in the green the done-checks use — these coins are already credited, so
        /// the pill must not read as something still to claim.</summary>
        private void PaintCoinBox()
        {
            if (_coinBoxRoot == null || _currentTheme == null)
            {
                return;
            }

            _coinBoxShadow.color = _currentTheme.CardShadow;
            _coinBoxPlate.color = _currentTheme.CardBackground;
            _coinBoxDisc.color = _currentTheme.Accent;
            _coinBoxGlyph.color = _coinSprite != null ? Color.white : Color.clear;
            _coinBoxCaptionText.color = _currentTheme.SoftInk;
            _coinBoxValueText.color = _currentTheme.Ink;
            _coinBoxPillPlate.color = _currentTheme.GetFill(HudChrome.GREEN_KIND);
            _coinBoxPillText.color = Color.white;
        }

        /// <summary>A reward row in the coin box's vocabulary: same plate, same caption ink, and
        /// accent discs carrying the power-up glyphs as the Level Path's reward badges do.</summary>
        private void PaintRewardRow(RewardRow row)
        {
            if (row == null || _currentTheme == null)
            {
                return;
            }

            row.Shadow.color = _currentTheme.CardShadow;
            row.Plate.color = _currentTheme.CardBackground;
            row.CaptionText.color = _currentTheme.SoftInk;
            for (int rewardIndex = 0; rewardIndex < row.Discs.Length; rewardIndex++)
            {
                row.Discs[rewardIndex].color = _currentTheme.Accent;
                row.Glyphs[rewardIndex].color = Color.white;
            }
        }

        private void PaintStatPlate(StatPlate statPlate, Color valueColour)
        {
            statPlate.Shadow.color = _currentTheme.CardShadow;
            statPlate.Plate.color = _currentTheme.CardBackground;
            statPlate.CaptionText.color = _currentTheme.SoftInk;
            statPlate.ValueText.color = valueColour;
        }

        private void PaintButton(ActionButton button)
        {
            if (_currentTheme == null)
            {
                return;
            }

            button.Plate.color = _currentTheme.GetFill(button.Kind);
            button.LabelText.color = Color.white;
        }

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            _scoreCaptionText.text = _localizationSystem.Translate(LocalizationKeys.HUD_SCORE_LABEL);
            _newTagText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_NEW_RECORD);
            _badgesHeadingText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_BADGES_HEADING);
            _claimHintText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_CLAIM_HINT);
            RefreshAll();
        }

        /// <summary>Everything that depends on the captured run: wording, figures, badges, buttons,
        /// and then the layout that fits the card around them.</summary>
        private void RefreshAll()
        {
            RefreshHeader();
            RefreshStats();
            RefreshCoinBox();
            RefreshRewardRows();
            RefreshLivesRow();
            RefreshBadges();
            RefreshButtons();
            Layout();
        }

        /// <summary>
        /// Words the title and the reason line from why the run ended and the mode it was played in.
        /// A Path success names the level cleared; a Path failure is "no moves left" like Endless, with
        /// a reason line that names the level so the "try again" below reads as "this level again".
        /// </summary>
        private void RefreshHeader()
        {
            bool isPath = _lastMode == GameMode.Path;

            if (_lastReason == GameOverReason.LevelCompleted)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(_playedLevelNumber);
                _titleText.text = _localizationSystem.Format(
                    LocalizationKeys.GAME_OVER_TITLE_LEVEL_COMPLETE, _stringBuilder.ToString());
                _reasonText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_REASON_LEVEL_COMPLETE);
            }
            else if (_lastReason == GameOverReason.TimeUp)
            {
                _titleText.text = _localizationSystem.Translate(LocalizationKeys.GAME_OVER_TITLE_TIME_UP);
                _reasonText.text = _localizationSystem.Format(
                    LocalizationKeys.RUN_RESULT_REASON_TIME_UP, FormatDuration());
            }
            else if (_lastReason == GameOverReason.ObjectiveMissed)
            {
                // Path-only (issue #307 AC4/AC6b): a timer cell expired, distinct copy from "no moves
                // left" so the player can tell the two failures apart — this one names the level, exactly
                // as the no-moves Path branch below does, since "try again" reads as "this level again".
                _titleText.text = _localizationSystem.Translate(LocalizationKeys.GAME_OVER_TITLE_OBJECTIVE_MISSED);
                _stringBuilder.Clear();
                _stringBuilder.Append(_playedLevelNumber);
                _reasonText.text = _localizationSystem.Format(
                    LocalizationKeys.RUN_RESULT_REASON_OBJECTIVE_MISSED, _stringBuilder.ToString());
            }
            else
            {
                _titleText.text = _localizationSystem.Translate(LocalizationKeys.GAME_OVER_TITLE_NO_MOVES);
                if (isPath)
                {
                    _stringBuilder.Clear();
                    _stringBuilder.Append(_playedLevelNumber);
                    _reasonText.text = _localizationSystem.Format(
                        LocalizationKeys.RUN_RESULT_REASON_LEVEL_FAILED, _stringBuilder.ToString());
                }
                else
                {
                    _reasonText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_REASON_NO_MOVES);
                }
            }

            _clockText.text = CLOCK_AT_TIME_UP;
        }

        /// <summary>The timed round length through the shared minutes format, so it reads exactly as
        /// the duration picker spells it.</summary>
        private string FormatDuration()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(Mathf.RoundToInt(_lastDurationSeconds / 60f));
            return _localizationSystem.Format(LocalizationKeys.FORMAT_MINUTES, _stringBuilder.ToString());
        }

        /// <summary>
        /// The score well: this run's score, then the two plates. Endless shows the endless record and
        /// the lifetime total; Timed the record for this round length and the lifetime total; Path the
        /// level's own score beside the running total of the walk — both, because a level resets the
        /// first and only ever adds to the second.
        /// </summary>
        private void RefreshStats()
        {
            if (_localizationSystem == null)
            {
                return;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(_runScore);
            _scoreValueText.text = _stringBuilder.ToString();

            switch (_lastMode)
            {
                case GameMode.Path:
                    _recordPlate.CaptionText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_LEVEL_SCORE_LABEL);
                    SetFigure(_recordPlate, _runScore);
                    _totalPlate.CaptionText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_PATH_TOTAL_LABEL);
                    SetFigure(_totalPlate, _pathRunModel.PathTotalScore.Value);
                    break;
                case GameMode.Timed:
                    // Classic mode's "Sınırsız" duration (issue #355) never ends by time, so its record
                    // caption reads like Endless's plain one rather than naming a length that would not
                    // have applied. The figure itself still comes from _timedHighScoreModel, which
                    // already keeps this duration's own bucket separate from every other one.
                    _recordPlate.CaptionText.text = TimedModeConfig.IsEndlessDuration(_lastDurationSeconds)
                        ? _localizationSystem.Translate(LocalizationKeys.SCORE_BEST)
                        : _localizationSystem.Format(LocalizationKeys.SCORE_BEST_TIMED, FormatDuration());
                    SetFigure(_recordPlate, _timedHighScoreModel.Best.Value);
                    _totalPlate.CaptionText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_TOTAL_LABEL);
                    SetFigure(_totalPlate, _profileModel.TotalScoreEarned.Value);
                    break;
                default:
                    _recordPlate.CaptionText.text = _localizationSystem.Translate(LocalizationKeys.SCORE_BEST);
                    SetFigure(_recordPlate, _scoreModel.HighScore.Value);
                    _totalPlate.CaptionText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_TOTAL_LABEL);
                    SetFigure(_totalPlate, _profileModel.TotalScoreEarned.Value);
                    break;
            }

            PlaceTrophy();
            _newTagRoot.gameObject.SetActive(_isNewRecordThisRun);
        }

        private void SetFigure(StatPlate statPlate, int value)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(value);
            statPlate.ValueText.text = _stringBuilder.ToString();
        }

        /// <summary>
        /// Sits the trophy just left of the record caption's first letter, and nudges the caption right
        /// by half the trophy so the pair stays centred on the plate. The caption overflows its rect,
        /// so its width is read off the text generator, which is current as soon as the text is set.
        /// </summary>
        private void PlaceTrophy()
        {
            var captionRect = (RectTransform)_recordPlate.CaptionText.transform;
            float captionY = captionRect.anchoredPosition.y;

            // Path's left plate is the level's own score, not a record, so it carries no trophy.
            bool showTrophy = _trophySprite != null && _lastMode != GameMode.Path;
            _trophyImage.gameObject.SetActive(showTrophy);

            if (!showTrophy)
            {
                captionRect.anchoredPosition = new Vector2(0f, captionY);
                return;
            }

            float captionWidth = _recordPlate.CaptionText.preferredWidth;
            float shift = (TROPHY_SIZE + TROPHY_GAP) * 0.5f;
            captionRect.anchoredPosition = new Vector2(shift, captionY);
            _trophyRect.anchoredPosition = new Vector2(shift - (captionWidth * 0.5f) - TROPHY_GAP - (TROPHY_SIZE * 0.5f), captionY);
        }

        /// <summary>
        /// The coin box, shown only on a cleared Path level that actually paid coins out: the balance
        /// counting up to where the level left it, and the "+N" the level itself added. Every other
        /// card — Endless, Timed, a failed level, a level that paid nothing — leaves the row hidden,
        /// and <see cref="Layout"/> then gives it no height at all.
        /// </summary>
        private void RefreshCoinBox()
        {
            if (_coinBoxRoot == null || _localizationSystem == null || _profileModel == null)
            {
                return;
            }

            bool show = _lastMode == GameMode.Path
                && _lastReason == GameOverReason.LevelCompleted
                && _coinsEarnedThisRun > 0;

            bool wasShowing = _coinBoxRoot.gameObject.activeSelf;
            _coinBoxRoot.gameObject.SetActive(show);

            if (!show)
            {
                CancelCoinCountUp();
                return;
            }

            _coinBoxCaptionText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_COIN_LABEL);
            _coinBoxPillText.text = FormatReward(_coinsEarnedThisRun);

            // Only a freshly opened box counts up. A repaint while it is already on screen — a language
            // switch, a badge claim — must not replay the climb from the start.
            if (!wasShowing)
            {
                SetCoinFigure(_coinsAtRunStart);
                AnimateCoinsToAsync(_profileModel.CoinBalance.Value).Forget();
            }
        }

        /// <summary>
        /// The reward row, shown only on a cleared Path level whose clear paid something: the level's own
        /// reward (issue #462) — a first clear — followed by any first-try streak bonus the same clear
        /// landed (issue #464), one disc each, in the order they flew in. One row rather than two: what
        /// the player won is the point, not which rule paid which part. A hidden row gets no height from
        /// <see cref="Layout"/>.
        /// </summary>
        private void RefreshRewardRows()
        {
            if (_levelRewardRow == null || _localizationSystem == null || _powerUpInventoryView == null)
            {
                return;
            }

            _shownRewards.Clear();
            bool isPathClear = _lastMode == GameMode.Path && _lastReason == GameOverReason.LevelCompleted;
            if (isPathClear && _rewardedLevelNumber > 0 && _rewardedLevelNumber == _playedLevelNumber)
            {
                _shownRewards.AddRange(LevelCompletionRewards.For(_playedLevelNumber));
            }

            if (isPathClear
                && _rewardRuleModel.LastPayoutLevelNumber > 0
                && _rewardRuleModel.LastPayoutLevelNumber == _playedLevelNumber)
            {
                _shownRewards.AddRange(_rewardRuleModel.LastPayoutKinds);
            }

            ShowRewardRow(
                _levelRewardRow, _shownRewards.Count > 0, LocalizationKeys.RUN_RESULT_LEVEL_REWARD_LABEL, _shownRewards);
        }

        /// <summary>
        /// The life row, shown only on a Path failure that actually charged a life: the run on the card
        /// is Path, it did not end in <see cref="GameOverReason.LevelCompleted"/>, and
        /// <see cref="LivesModel.LivesBeforeLastCharge"/> says a charge landed. A failure at zero lives
        /// charged nothing and leaves that at 0, so its card has no row — the player lost nothing.
        /// Hidden, it takes no height from <see cref="Layout"/>.
        /// </summary>
        private void RefreshLivesRow()
        {
            if (_livesRowRoot == null || _livesModel == null || _localizationSystem == null)
            {
                return;
            }

            int livesBefore = _livesModel.LivesBeforeLastCharge.Value;
            bool show = _lastMode == GameMode.Path
                && _lastReason != GameOverReason.LevelCompleted
                && livesBefore > 0;
            _livesRowRoot.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(livesBefore - 1);
            _livesRowCountText.text = _stringBuilder.ToString();
            _livesRowTitleText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_LIFE_LOST);
            PaintLivesDetail();
        }

        /// <summary>"17 → 16 lives", plus " · +5 in 12:34" while below the cap — omitted at or above it,
        /// where <see cref="LivesModel.SecondsUntilRefill"/> is 0 and no refill is coming.</summary>
        private void PaintLivesDetail()
        {
            int livesBefore = _livesModel.LivesBeforeLastCharge.Value;
            _stringBuilder.Clear();
            _stringBuilder.Append(livesBefore);
            string before = _stringBuilder.ToString();
            _stringBuilder.Clear();
            _stringBuilder.Append(livesBefore - 1);
            string change = _localizationSystem.Format(LocalizationKeys.RUN_RESULT_LIVES_CHANGE, before, _stringBuilder.ToString());

            int seconds = _livesModel.SecondsUntilRefill.Value;
            if (seconds <= 0)
            {
                _livesRowDetailText.text = change;
                return;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(_livesConfig.RefillAmount);
            string amount = _stringBuilder.ToString();

            // mm:ss, as the HUD's own countdown: a universal numeric format, not a table entry.
            _stringBuilder.Clear();
            AppendPadded(seconds / 60);
            _stringBuilder.Append(':');
            AppendPadded(seconds % 60);
            string refill = _localizationSystem.Format(LocalizationKeys.RUN_RESULT_LIVES_REFILL_IN, amount, _stringBuilder.ToString());

            _stringBuilder.Clear();
            _stringBuilder.Append(change);
            _stringBuilder.Append(LIVES_DETAIL_SEPARATOR);
            _stringBuilder.Append(refill);
            _livesRowDetailText.text = _stringBuilder.ToString();
        }

        private void AppendPadded(int value)
        {
            if (value < 10)
            {
                _stringBuilder.Append('0');
            }

            _stringBuilder.Append(value);
        }

        private void ShowRewardRow(RewardRow row, bool show, string captionKey, IReadOnlyList<PowerUpKind> rewards)
        {
            row.Root.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            row.CaptionText.text = _localizationSystem.Translate(captionKey);

            int shownCount = Mathf.Min(rewards.Count, row.Discs.Length);
            float rowWidth = _cardWidth - (SIDE_INSET * 2f);
            float rightEdge = (rowWidth * 0.5f) - COIN_BOX_PAD;
            for (int rewardIndex = 0; rewardIndex < row.Discs.Length; rewardIndex++)
            {
                bool hasReward = rewardIndex < shownCount;
                row.Discs[rewardIndex].gameObject.SetActive(hasReward);
                row.Glyphs[rewardIndex].gameObject.SetActive(hasReward);
                if (!hasReward)
                {
                    continue;
                }

                // Right-aligned, first reward rightmost-last: laid out left to right so the list reads
                // in grant order, the same order the fly-ins arrive in.
                int slotFromRight = shownCount - 1 - rewardIndex;
                float discX = rightEdge - (COIN_DISC_SIZE * 0.5f)
                    - (slotFromRight * (COIN_DISC_SIZE + REWARD_BOX_ICON_GAP));
                Vector2 position = new Vector2(discX, 0f);
                row.Discs[rewardIndex].rectTransform.anchoredPosition = position;
                row.Glyphs[rewardIndex].rectTransform.anchoredPosition = position;
                row.Glyphs[rewardIndex].sprite = _powerUpInventoryView.IconFor(rewards[rewardIndex]);
            }
        }

        /// <summary>
        /// Counts the shown balance up to <paramref name="targetCoins"/> over
        /// <see cref="COIN_COUNT_UP_DURATION"/>, as <see cref="ScoreView"/> counts the score.
        /// </summary>
        private async UniTaskVoid AnimateCoinsToAsync(int targetCoins)
        {
            CancelCoinCountUp();

            if (_displayedCoins == targetCoins)
            {
                SetCoinFigure(targetCoins);
                return;
            }

            _coinCountUpCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
            CancellationToken token = _coinCountUpCts.Token;

            int startCoins = _displayedCoins;

            try
            {
                float elapsed = 0f;
                while (elapsed < COIN_COUNT_UP_DURATION)
                {
                    float t = Mathf.Clamp01(elapsed / COIN_COUNT_UP_DURATION);
                    SetCoinFigure(startCoins + Mathf.RoundToInt((targetCoins - startCoins) * t));
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a run restart, a hidden box, or object destruction.
                return;
            }

            SetCoinFigure(targetCoins);
        }

        private void CancelCoinCountUp()
        {
            if (_coinCountUpCts == null)
            {
                return;
            }

            _coinCountUpCts.Cancel();
            _coinCountUpCts.Dispose();
            _coinCountUpCts = null;
        }

        private void SetCoinFigure(int coins)
        {
            _displayedCoins = coins;
            _stringBuilder.Clear();
            _stringBuilder.Append(coins);
            _coinBoxValueText.text = _stringBuilder.ToString();
        }

        /// <summary>
        /// Repaints the badge section from this run's unlock buffer: the heading and the claim hint
        /// show only when there is at least one row (and the hint only while one is still claimable),
        /// then each row is painted as claimable or done. <see cref="Layout"/> fits the card after.
        /// </summary>
        private void RefreshBadges()
        {
            if (_panel == null || _currentTheme == null || _localizationSystem == null)
            {
                return;
            }

            IReadOnlyList<string> unlocked = _badgeModel.UnlockedThisRun;
            int rowCount = Mathf.Min(unlocked.Count, BADGE_ROW_COUNT);

            bool anyClaimable = false;
            for (int rowIndex = 0; rowIndex < _badgeRows.Length; rowIndex++)
            {
                RefreshBadgeRow(rowIndex, rowIndex < rowCount ? unlocked[rowIndex] : null);
                anyClaimable |= _badgeRows[rowIndex].IsClaimable;
            }

            _badgesHeadingText.gameObject.SetActive(rowCount > 0);
            _claimHintText.gameObject.SetActive(anyClaimable);
        }

        private void RefreshBadgeRow(int rowIndex, string badgeId)
        {
            BadgeRow row = _badgeRows[rowIndex];

            bool exists = badgeId != null;
            row.Root.gameObject.SetActive(exists);

            if (!exists)
            {
                row.BadgeId = null;
                row.IsClaimable = false;
                return;
            }

            bool isClaimable = _badgeSystem.IsClaimable(badgeId);
            row.BadgeId = badgeId;
            row.IsClaimable = isClaimable;

            BadgeConfig config = _badgeCatalog.Find(badgeId);
            Sprite icon = config != null ? config.Icon : null;

            row.Shadow.color = _currentTheme.CardShadow;
            row.Plate.color = _currentTheme.CardBackground;
            row.IconDisc.color = _currentTheme.Accent;
            row.IconGlyph.sprite = icon;
            row.IconGlyph.color = icon == null ? Color.clear : Color.white;
            row.NameText.color = _currentTheme.Ink;
            row.NameText.text = DisplayNameOf(config, badgeId);

            // A claimable row carries the glossy gold "+N" button, the loudest thing on the card; a
            // claimed row settles to the green check the goal chips use for "done".
            row.ClaimPlate.gameObject.SetActive(isClaimable);
            row.DoneDisc.gameObject.SetActive(!isClaimable);
            row.DoneCheck.gameObject.SetActive(!isClaimable);

            if (isClaimable)
            {
                row.ClaimPlate.color = _currentTheme.Accent;
                row.ClaimCoin.color = _coinSprite != null ? Color.white : Color.clear;
                row.ClaimText.color = Color.white;
                row.ClaimText.text = FormatReward(_badgeSystem.CoinRewardOf(badgeId));
            }
            else
            {
                row.DoneDisc.color = _currentTheme.GetFill(HudChrome.GREEN_KIND);
                row.DoneCheck.color = Color.white;
            }
        }

        /// <summary>
        /// The badge's name in the player's language: the string-table entry when a key is authored,
        /// else the authored fallback name, else the id — the same resolution the badge wall uses.
        /// </summary>
        private string DisplayNameOf(BadgeConfig config, string badgeId)
        {
            if (config == null)
            {
                return badgeId;
            }

            if (!string.IsNullOrEmpty(config.DisplayNameKey))
            {
                string translated = _localizationSystem.Translate(config.DisplayNameKey);
                if (!string.IsNullOrEmpty(translated) && translated != config.DisplayNameKey)
                {
                    return translated;
                }
            }

            return !string.IsNullOrEmpty(config.DisplayName) ? config.DisplayName : badgeId;
        }

        private string FormatReward(int coins)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(REWARD_PREFIX);
            _stringBuilder.Append(coins);
            return _stringBuilder.ToString();
        }

        /// <summary>
        /// Which buttons the card offers, from the captured reason and mode, filled top to bottom. A
        /// Path success with a next level leads with the green advance and keeps "play again" under
        /// it; a no-moves ending with a rescue on offer (issue #371) leads with the green "watch ad"
        /// and keeps the restart under it — a fresh dock for the same run is the better offer, and
        /// the restart is what the player gets by ignoring it; a Path failure is otherwise a single
        /// "try again"; Timed keeps its "change mode" escape hatch under the restart; everything
        /// else — Endless, or a Path success on the last authored level — is the one restart button.
        /// Only a no-moves ending ever adds a button, so every other reason's card is exactly as it was.
        /// </summary>
        private void RefreshButtons()
        {
            if (_localizationSystem == null)
            {
                return;
            }

            bool isPath = _lastMode == GameMode.Path;
            int buttonIndex = 0;

            _buttonsInRow = _lastReason == GameOverReason.LevelCompleted && _hasNextLevel;
            if (_buttonsInRow)
            {
                // One row (issue #464): the square restart on the left, then the next-level button
                // showing where the player is going — its icon, number and name.
                ConfigureRestartSquare(buttonIndex++);
                ConfigureNextLevel(buttonIndex++, NextLevelNumber);
            }
            else
            {
                // The flag is only ever set on a no-moves ending, but the reason is checked too so the
                // card can never grow a rescue button for a reason the System has no rescue for.
                if (_lastReason == GameOverReason.NoMovesLeft && _isRescueAvailable)
                {
                    ConfigureButton(
                        buttonIndex++, RunEndAction.WatchAd, HudChrome.GREEN_KIND,
                        _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_WATCH_AD));
                }

                string restartKey = isPath && _lastReason != GameOverReason.LevelCompleted
                    ? LocalizationKeys.RUN_RESULT_TRY_AGAIN
                    : LocalizationKeys.RUN_RESULT_PLAY_AGAIN;
                ConfigureButton(
                    buttonIndex++, RunEndAction.PlayAgain, PRIMARY_KIND, _localizationSystem.Translate(restartKey),
                    withRestartGlyph: true);

                if (_lastMode == GameMode.Timed)
                {
                    ConfigureButton(
                        buttonIndex++, RunEndAction.ChangeMode, SECONDARY_KIND,
                        _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_CHANGE_MODE));
                }
            }

            for (int hiddenIndex = buttonIndex; hiddenIndex < _buttons.Length; hiddenIndex++)
            {
                _buttons[hiddenIndex].Action = RunEndAction.None;
                _buttons[hiddenIndex].Rect.gameObject.SetActive(false);
            }
        }

        /// <summary>A full-width stacked button with a centred label — and, for a restart, the restart
        /// arrow before it, the two centred as one group.</summary>
        private void ConfigureButton(int buttonIndex, RunEndAction action, int kind, string label, bool withRestartGlyph = false)
        {
            ActionButton button = PrepareButton(buttonIndex, action, kind, new Vector2(_cardWidth - (SIDE_INSET * 2f), BUTTON_HEIGHT));
            button.LabelText.text = label;
            button.LabelText.alignment = TextAnchor.MiddleCenter;
            button.LabelText.gameObject.SetActive(true);

            bool glyph = withRestartGlyph && _restartSprite != null;
            button.RestartGlyph.gameObject.SetActive(glyph);
            float labelWidth = button.LabelText.preferredWidth;
            if (glyph)
            {
                float groupWidth = RESTART_INLINE_GLYPH_SIZE + RESTART_INLINE_GAP + labelWidth;
                button.RestartGlyph.rectTransform.sizeDelta = new Vector2(RESTART_INLINE_GLYPH_SIZE, RESTART_INLINE_GLYPH_SIZE);
                button.RestartGlyph.rectTransform.anchoredPosition = new Vector2((-groupWidth * 0.5f) + (RESTART_INLINE_GLYPH_SIZE * 0.5f), 0f);
                SetLabelCentre(button.LabelText, (groupWidth * 0.5f) - (labelWidth * 0.5f), -2f);
            }
            else
            {
                SetLabelCentre(button.LabelText, 0f, -2f);
            }
        }

        /// <summary>The square play-again button of the success row: the restart arrow alone.</summary>
        private void ConfigureRestartSquare(int buttonIndex)
        {
            ActionButton button = PrepareButton(
                buttonIndex, RunEndAction.PlayAgain, PRIMARY_KIND, new Vector2(NEXT_ROW_HEIGHT, NEXT_ROW_HEIGHT));
            button.LabelText.gameObject.SetActive(_restartSprite == null);
            button.LabelText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_PLAY_AGAIN);
            button.RestartGlyph.gameObject.SetActive(_restartSprite != null);
            button.RestartGlyph.rectTransform.sizeDelta = new Vector2(RESTART_GLYPH_SIZE, RESTART_GLYPH_SIZE);
            button.RestartGlyph.rectTransform.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// The next-level button of the success row: the next level's icon inset on its left, "NEXT · N"
        /// over the level's name, and a chevron on the right. Falls back to the plain label when the
        /// level has no identity row.
        /// </summary>
        private void ConfigureNextLevel(int buttonIndex, int nextLevelNumber)
        {
            float rowWidth = _cardWidth - (SIDE_INSET * 2f);
            float width = rowWidth - NEXT_ROW_HEIGHT - BUTTON_GAP;
            ActionButton button = PrepareButton(
                buttonIndex, RunEndAction.NextLevel, HudChrome.GREEN_KIND, new Vector2(width, NEXT_ROW_HEIGHT));

            _stringBuilder.Clear();
            _stringBuilder.Append(nextLevelNumber);
            button.LabelText.text = _localizationSystem.Format(LocalizationKeys.RUN_RESULT_NEXT_SHORT, _stringBuilder.ToString());
            button.LabelText.gameObject.SetActive(true);

            // Beside the icon the label has a fixed width, so it shrinks to fit rather than overrun.
            button.LabelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            button.LabelText.resizeTextForBestFit = true;
            button.LabelText.resizeTextMinSize = _buttonFontSize / 2;
            button.LabelText.resizeTextMaxSize = _buttonFontSize;

            LevelIdentityConfig identity = _levelIdentityCatalog != null ? _levelIdentityCatalog.Find(nextLevelNumber) : null;
            bool hasIcon = identity != null && identity.Icon != null;
            string levelName = identity != null && !string.IsNullOrEmpty(identity.NameKey)
                ? _localizationSystem.Translate(identity.NameKey)
                : string.Empty;

            float left = -width * 0.5f;
            button.LevelIconRoot.gameObject.SetActive(hasIcon);
            float textLeft = left + NEXT_CHEVRON_INSET;
            if (hasIcon)
            {
                button.LevelIcon.sprite = identity.Icon;
                float iconCentre = left + NEXT_ICON_INSET + (NEXT_ICON_SIZE * 0.5f);
                button.LevelIconRoot.anchoredPosition = new Vector2(iconCentre, 0f);
                textLeft = left + NEXT_ICON_INSET + NEXT_ICON_SIZE + NEXT_TEXT_GAP;
            }

            bool hasChevron = _chevronSprite != null;
            button.Chevron.gameObject.SetActive(hasChevron);
            button.Chevron.rectTransform.anchoredPosition = new Vector2(
                (width * 0.5f) - NEXT_CHEVRON_INSET - (NEXT_CHEVRON_SIZE * 0.5f), 0f);

            bool hasName = levelName.Length > 0;
            button.SubLabelText.gameObject.SetActive(hasName);
            button.SubLabelText.text = levelName;

            button.LabelText.alignment = TextAnchor.MiddleLeft;
            button.SubLabelText.alignment = TextAnchor.MiddleLeft;
            float textWidth = (width * 0.5f) - NEXT_CHEVRON_INSET - (hasChevron ? NEXT_CHEVRON_SIZE + 12f : 0f) - textLeft;
            button.LabelText.rectTransform.pivot = new Vector2(0f, 0.5f);
            button.LabelText.rectTransform.sizeDelta = new Vector2(textWidth, NEXT_ROW_HEIGHT * 0.45f);
            button.LabelText.rectTransform.anchoredPosition = new Vector2(textLeft, hasName ? 18f : -2f);
            button.SubLabelText.rectTransform.sizeDelta = new Vector2(textWidth, NEXT_ROW_HEIGHT * 0.3f);
            button.SubLabelText.rectTransform.anchoredPosition = new Vector2(textLeft, -30f);
        }

        /// <summary>Shows slot <paramref name="buttonIndex"/> at <paramref name="size"/> for
        /// <paramref name="action"/>, painted as <paramref name="kind"/>, with every optional part hidden —
        /// the configure call turns on only what its shape uses.</summary>
        private ActionButton PrepareButton(int buttonIndex, RunEndAction action, int kind, Vector2 size)
        {
            ActionButton button = _buttons[buttonIndex];
            button.Action = action;
            button.Kind = kind;
            button.Rect.sizeDelta = size;
            button.Rect.gameObject.SetActive(true);
            button.LabelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            button.LabelText.resizeTextForBestFit = false;
            button.RestartGlyph.gameObject.SetActive(false);
            button.LevelIconRoot.gameObject.SetActive(false);
            button.SubLabelText.gameObject.SetActive(false);
            button.Chevron.gameObject.SetActive(false);
            PaintButton(button);
            return button;
        }

        /// <summary>Centres a label at (<paramref name="x"/>, <paramref name="y"/>) inside its button,
        /// undoing any left-aligned shape an earlier open gave it.</summary>
        private static void SetLabelCentre(Text label, float x, float y)
        {
            RectTransform rect = label.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(label.preferredWidth + 8f, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>
        /// Stacks the card's sections from its top edge and sizes the card to the result. Run on
        /// every open, locale switch and badge repaint, since the adornment, the badge rows and the
        /// second button all come and go. The card's centre stays at <see cref="CARD_CENTRE_Y"/>
        /// whatever its height.
        /// </summary>
        private void Layout()
        {
            if (_panel == null)
            {
                return;
            }

            float y = HudChrome.AWNING_HEIGHT + HudChrome.AWNING_SCALLOP_HEIGHT + HEADER_TOP_PAD;

            bool showStars = _lastReason == GameOverReason.LevelCompleted;
            bool showClock = _lastReason == GameOverReason.TimeUp;
            _starsRoot.gameObject.SetActive(showStars);
            _clockRoot.gameObject.SetActive(showClock);
            if (showStars || showClock)
            {
                HangCentre(showStars ? _starsRoot : _clockRoot, 0f, y + (ADORNMENT_HEIGHT * 0.5f));
                y += ADORNMENT_HEIGHT + ADORNMENT_GAP;
            }

            HangCentre((RectTransform)_titleText.transform, 0f, y + (TITLE_HEIGHT * 0.5f));
            y += TITLE_HEIGHT;
            HangCentre((RectTransform)_reasonText.transform, 0f, y + (REASON_HEIGHT * 0.5f));
            y += REASON_HEIGHT;

            y += WELL_MARGIN;
            HangCentre(_wellRoot, 0f, y + (WELL_HEIGHT * 0.5f));
            y += WELL_HEIGHT;

            // Between the well and the badges, and only on a Path level that paid coins out: hidden,
            // it takes no height, so every other card is exactly as tall as it was.
            if (_coinBoxRoot.gameObject.activeSelf)
            {
                y += COIN_BOX_TOP_GAP;
                HangCentre(_coinBoxRoot, 0f, y + (COIN_BOX_HEIGHT * 0.5f));
                y += COIN_BOX_HEIGHT;
            }

            // Straight under the coin box, on the same terms: only a Path clear that paid something.
            y = LayoutRewardRow(_levelRewardRow, y);

            // The life row takes the same slot on the other outcome: only a Path failure that cost a life
            // (issue #478). The two rows never show together — one needs a clear, the other a failure.
            if (_livesRowRoot.gameObject.activeSelf)
            {
                y += COIN_BOX_TOP_GAP;
                HangCentre(_livesRowRoot, 0f, y + (COIN_BOX_HEIGHT * 0.5f));
                y += COIN_BOX_HEIGHT;
            }

            if (_badgesHeadingText.gameObject.activeSelf)
            {
                y += BADGES_TOP_GAP;
                float headingCentre = y + (BADGES_HEADING_HEIGHT * 0.5f);
                float headingHalfWidth = (_cardWidth * 0.5f) - SIDE_INSET - BADGES_HEADING_INSET;
                HangCentre((RectTransform)_badgesHeadingText.transform, -headingHalfWidth, headingCentre);
                HangCentre((RectTransform)_claimHintText.transform, headingHalfWidth, headingCentre);
                y += BADGES_HEADING_HEIGHT + BADGES_HEADING_GAP;

                for (int rowIndex = 0; rowIndex < _badgeRows.Length; rowIndex++)
                {
                    BadgeRow row = _badgeRows[rowIndex];
                    if (!row.Root.gameObject.activeSelf)
                    {
                        break;
                    }

                    if (rowIndex > 0)
                    {
                        y += BADGE_ROW_GAP;
                    }

                    HangCentre(row.Root, 0f, y + (BADGE_ROW_HEIGHT * 0.5f));
                    y += BADGE_ROW_HEIGHT;
                }
            }

            y += BUTTONS_TOP_PAD;
            if (_buttonsInRow)
            {
                // Square restart on the left, the next-level button filling the rest of the row.
                float rowLeft = -(_cardWidth * 0.5f) + SIDE_INSET;
                float rowCentre = y + (NEXT_ROW_HEIGHT * 0.5f);
                HangCentre(_buttons[0].Rect, rowLeft + (NEXT_ROW_HEIGHT * 0.5f), rowCentre);
                HangCentre(_buttons[1].Rect, rowLeft + NEXT_ROW_HEIGHT + BUTTON_GAP + (_buttons[1].Rect.sizeDelta.x * 0.5f), rowCentre);
                y += NEXT_ROW_HEIGHT;
            }

            for (int buttonIndex = 0; buttonIndex < _buttons.Length && !_buttonsInRow; buttonIndex++)
            {
                ActionButton button = _buttons[buttonIndex];
                if (!button.Rect.gameObject.activeSelf)
                {
                    continue;
                }

                if (buttonIndex > 0)
                {
                    y += BUTTON_GAP;
                }

                HangCentre(button.Rect, 0f, y + (BUTTON_HEIGHT * 0.5f));
                y += BUTTON_HEIGHT;
            }

            y += BOTTOM_PAD;

            _cardRect.sizeDelta = new Vector2(_cardWidth, y);
            _cardRect.anchoredPosition = new Vector2(0f, CARD_CENTRE_Y + (y * 0.5f));
        }

        /// <summary>Hangs a centre-pivoted rect (or an edge-pivoted label) from the card's top edge,
        /// its vertical centre <paramref name="centreFromTop"/> below it. The pivot is left alone, so
        /// a left- or right-aligned label keeps <paramref name="x"/> as its aligned edge.</summary>
        /// <summary>Hangs a reward row under <paramref name="y"/> when it is showing and returns the
        /// new running height; a hidden row takes no room.</summary>
        private static float LayoutRewardRow(RewardRow row, float y)
        {
            if (!row.Root.gameObject.activeSelf)
            {
                return y;
            }

            y += COIN_BOX_TOP_GAP;
            HangCentre(row.Root, 0f, y + (COIN_BOX_HEIGHT * 0.5f));
            return y + COIN_BOX_HEIGHT;
        }

        private static void HangCentre(RectTransform rect, float x, float centreFromTop)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, -centreFromTop);
        }

        /// <summary>Stretches a rect over its parent, dropped <paramref name="drop"/> pixels, so it
        /// follows the card's height without being resized itself.</summary>
        private static void Stretch(RectTransform rect, float drop)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = new Vector2(0f, -drop);
        }

        /// <summary>
        /// Builds the whole card once, unpainted and unworded: the scrim, the card plate over its
        /// shadow with the awning across its top, then every section in stacking order. Nothing is
        /// positioned vertically here — <see cref="Layout"/> does that per open — and nothing is
        /// coloured: the theme and locale subscriptions in Start paint and word it.
        /// </summary>
        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            var panelObject = new GameObject("RunResultPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            // The card is top-pivoted so Layout can grow it downward from a fixed top edge and then
            // slide that edge to keep the centre where it is. Its shadow and plate stretch over it.
            var cardSize = new Vector2(_cardWidth, WELL_HEIGHT);
            _cardRect = HudChrome.CreateRect(panelRect, "Card", cardSize, Vector2.zero);
            _cardRect.pivot = new Vector2(0.5f, 1f);
            _cardShadow = HudChrome.BuildRounded(_cardRect, "Shadow", cardSize, Vector2.zero, CARD_CORNER_RADIUS);
            Stretch(_cardShadow.rectTransform, CARD_SHADOW_DROP);
            _cardPlate = HudChrome.BuildRounded(_cardRect, "Plate", cardSize, Vector2.zero, CARD_CORNER_RADIUS);
            Stretch(_cardPlate.rectTransform, 0f);
            _awning = HudChrome.BuildAwning(_cardRect, _cardWidth, CARD_CORNER_RADIUS);

            BuildStars();
            BuildClock();

            _titleText = HudChrome.CreateLabel(
                _cardRect, "Title", _titleFontSize, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, _displayFont);
            _titleShadow = AddLetteringShadow(_titleText, TITLE_SHADOW_DROP);
            _reasonText = HudChrome.CreateLabel(
                _cardRect, "Reason", _reasonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, _labelFont);

            BuildWell();
            BuildCoinBox();
            _levelRewardRow = BuildRewardRow("RewardBox");
            BuildLivesRow();

            _badgesHeadingText = HudChrome.CreateLabel(
                _cardRect, "BadgesHeading", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, _labelFont);
            _badgesHeadingText.gameObject.SetActive(false);
            _claimHintText = HudChrome.CreateLabel(
                _cardRect, "ClaimHint", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleRight, Vector2.zero, _labelFont);
            _claimHintText.gameObject.SetActive(false);

            for (int rowIndex = 0; rowIndex < BADGE_ROW_COUNT; rowIndex++)
            {
                _badgeRows[rowIndex] = BuildBadgeRow(rowIndex);
            }

            for (int buttonIndex = 0; buttonIndex < BUTTON_SLOT_COUNT; buttonIndex++)
            {
                _buttons[buttonIndex] = BuildButton($"Button_{buttonIndex}");
                _buttons[buttonIndex].Rect.gameObject.SetActive(buttonIndex == 0);
            }

            _panel = panelObject;
        }

        /// <summary>The three gold star discs over a Path success's title.</summary>
        private void BuildStars()
        {
            float pitch = STAR_DISC_SIZE + STAR_GAP;
            _starsRoot = HudChrome.CreateRect(
                _cardRect, "Stars", new Vector2((pitch * STAR_COUNT) - STAR_GAP, STAR_DISC_SIZE), Vector2.zero);

            for (int starIndex = 0; starIndex < STAR_COUNT; starIndex++)
            {
                float x = (starIndex - ((STAR_COUNT - 1) * 0.5f)) * pitch;
                RectTransform discRoot = HudChrome.BuildPill(
                    _starsRoot, $"Star_{starIndex}", new Vector2(STAR_DISC_SIZE, STAR_DISC_SIZE), new Vector2(x, 0f),
                    out _starDiscShadows[starIndex], out _starDiscs[starIndex]);
                _starGlyphs[starIndex] = HudChrome.BuildGlyph(
                    discRoot, "Glyph", UiSpriteFactory.FivePointStar,
                    new Vector2(STAR_GLYPH_SIZE, STAR_GLYPH_SIZE), Vector2.zero);
            }

            _starsRoot.gameObject.SetActive(false);
        }

        /// <summary>The "00:00" pill over a time-up's title: the timer HUD's pill, stopped.</summary>
        private void BuildClock()
        {
            var pillSize = new Vector2(CLOCK_PILL_WIDTH, CLOCK_PILL_HEIGHT);
            _clockRoot = HudChrome.CreateRect(_cardRect, "Clock", pillSize, Vector2.zero);
            _clockPlate = HudChrome.BuildGlossyPill(_clockRoot, "Pill", pillSize, Vector2.zero, _buttonSprite);

            // Glyph then digits, the pair centred: the digits at this face are about three glyph
            // widths, so the group starts a little left of centre.
            float groupStart = -(CLOCK_GLYPH_SIZE * 2f) - CLOCK_GLYPH_GAP;
            _clockGlyph = HudChrome.BuildGlyph(
                _clockRoot, "Glyph", _clockSprite, new Vector2(CLOCK_GLYPH_SIZE, CLOCK_GLYPH_SIZE),
                new Vector2(groupStart + (CLOCK_GLYPH_SIZE * 0.5f), 0f));
            _clockText = HudChrome.CreateLabel(
                _clockRoot, "Digits", _claimFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(groupStart + CLOCK_GLYPH_SIZE + CLOCK_GLYPH_GAP, -2f), _displayFont);

            _clockRoot.gameObject.SetActive(false);
        }

        /// <summary>The sunken well: the score caption and figure over the two stat plates.</summary>
        private void BuildWell()
        {
            float wellWidth = _cardWidth - (SIDE_INSET * 2f);
            _wellRoot = HudChrome.BuildWell(
                _cardRect, "Well", new Vector2(wellWidth, WELL_HEIGHT), Vector2.zero, WELL_CORNER_RADIUS,
                out _wellLip, out _wellFace);

            float top = WELL_HEIGHT * 0.5f;
            _scoreCaptionText = HudChrome.CreateLabel(
                _wellRoot, "ScoreCaption", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, top - WELL_PAD - (SCORE_LABEL_HEIGHT * 0.5f)), _labelFont);

            // The display face sits high in its line, so the figure is nudged down a little to read
            // as centred in its band — the same correction the score card makes.
            _scoreValueText = HudChrome.CreateLabel(
                _wellRoot, "ScoreValue", _scoreFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, top - WELL_PAD - SCORE_LABEL_HEIGHT - (SCORE_VALUE_HEIGHT * 0.5f) - (_scoreFontSize * 0.06f)),
                _displayFont);

            float plateWidth = (wellWidth - (WELL_PAD * 2f) - STAT_PLATE_GAP) * 0.5f;
            float plateX = (plateWidth + STAT_PLATE_GAP) * 0.5f;
            float plateY = -top + WELL_PAD + (STAT_PLATE_HEIGHT * 0.5f);
            _recordPlate = BuildStatPlate("RecordPlate", plateWidth, new Vector2(-plateX, plateY));
            _totalPlate = BuildStatPlate("TotalPlate", plateWidth, new Vector2(plateX, plateY));

            // Positioned once the caption's width is known (see PlaceTrophy); centre-pivoted so its
            // anchored position is simply where it sits.
            _trophyImage = HudChrome.BuildGlyph(
                _recordPlate.Root, "Trophy", _trophySprite, new Vector2(TROPHY_SIZE, TROPHY_SIZE), Vector2.zero);
            _trophyRect = _trophyImage.rectTransform;

            // The "new record" tag: a small accent pill tucked over the plate's top-right corner, tilted
            // a touch so it reads as stuck on rather than drawn in.
            var tagSize = new Vector2(NEW_TAG_WIDTH, NEW_TAG_HEIGHT);
            _newTagRoot = HudChrome.CreateRect(
                _recordPlate.Root, "NewTag", tagSize,
                new Vector2((plateWidth * 0.5f) - (NEW_TAG_WIDTH * 0.45f), (STAT_PLATE_HEIGHT * 0.5f) - (NEW_TAG_HEIGHT * 0.15f)));
            _newTagRoot.localRotation = Quaternion.Euler(0f, 0f, 8f);
            _newTagPlate = HudChrome.BuildRounded(_newTagRoot, "Plate", tagSize, Vector2.zero, NEW_TAG_HEIGHT * 0.5f);
            _newTagText = HudChrome.CreateLabel(
                _newTagRoot, "Label", _captionFontSize, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(0f, -1f), _displayFont);
            _newTagRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// The coin box: a badge row's plate the width of the well, the gold coin disc on the left,
        /// the "Coin" caption over the counting balance beside it, and the green "+N" pill on the
        /// right. Built hidden — only a Path level that paid coins out ever shows it.
        /// </summary>
        private void BuildCoinBox()
        {
            float rowWidth = _cardWidth - (SIDE_INSET * 2f);
            var rowSize = new Vector2(rowWidth, COIN_BOX_HEIGHT);
            _coinBoxRoot = HudChrome.CreateRect(_cardRect, "CoinBox", rowSize, Vector2.zero);
            HudChrome.BuildPlate(
                _coinBoxRoot, "Body", rowSize, Vector2.zero, COIN_BOX_RADIUS, HudChrome.PLATE_SHADOW_DROP,
                out _coinBoxShadow, out _coinBoxPlate);

            float discX = (-rowWidth * 0.5f) + COIN_BOX_PAD + (COIN_DISC_SIZE * 0.5f);
            _coinBoxDisc = HudChrome.BuildCircle(_coinBoxRoot, "IconDisc", COIN_DISC_SIZE, new Vector2(discX, 0f));
            _coinBoxGlyph = HudChrome.BuildGlyph(
                _coinBoxRoot, "IconGlyph", _coinSprite, new Vector2(COIN_GLYPH_SIZE, COIN_GLYPH_SIZE),
                new Vector2(discX, 0f));

            // Caption over figure, stacked as a stat plate stacks them but hung off the disc.
            float textX = discX + (COIN_DISC_SIZE * 0.5f) + COIN_ICON_TEXT_GAP;
            float half = COIN_BOX_HEIGHT * 0.5f;
            _coinBoxCaptionText = HudChrome.CreateLabel(
                _coinBoxRoot, "Caption", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(textX, half - COIN_CAPTION_TOP - (_captionFontSize * 0.5f)), _labelFont);
            _coinBoxValueText = HudChrome.CreateLabel(
                _coinBoxRoot, "Value", _statFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(textX, -half + COIN_VALUE_BOTTOM + (_statFontSize * 0.42f)), _displayFont);

            float rightEdge = (rowWidth * 0.5f) - COIN_BOX_PAD;
            var pillSize = new Vector2(COIN_PILL_WIDTH, COIN_PILL_HEIGHT);
            _coinBoxPillPlate = HudChrome.BuildGlossyPill(
                _coinBoxRoot, "Earned", pillSize, new Vector2(rightEdge - (COIN_PILL_WIDTH * 0.5f), 0f), _buttonSprite);
            _coinBoxPillText = HudChrome.CreateLabel(
                _coinBoxPillPlate.rectTransform, "Amount", _claimFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, -2f), _displayFont);

            _coinBoxRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// The fail card's life row (issue #478): the coin box's footprint on the lives pink — a heart
        /// holding the new count with a red "−1" badge on its shoulder, the bold "You lost a life" and the
        /// detail line under it. Fixed colours rather than theme ones: the pink is what marks lives across
        /// the level-start card, the sheet and this row. Built hidden.
        /// </summary>
        private void BuildLivesRow()
        {
            float rowWidth = _cardWidth - (SIDE_INSET * 2f);
            var rowSize = new Vector2(rowWidth, COIN_BOX_HEIGHT);
            _livesRowRoot = HudChrome.CreateRect(_cardRect, "LivesRow", rowSize, Vector2.zero);
            HudChrome.BuildRounded(_livesRowRoot, "Fill", rowSize, Vector2.zero, COIN_BOX_RADIUS).color = LivesFill;
            HudChrome.BuildOutline(_livesRowRoot, "Outline", rowSize, Vector2.zero, COIN_BOX_RADIUS, LIVES_ROW_OUTLINE)
                .color = LivesOutline;

            float heartX = (-rowWidth * 0.5f) + COIN_BOX_PAD + (LIVES_HEART_SIZE * 0.5f);
            var heartSize = new Vector2(LIVES_HEART_SIZE, LIVES_HEART_SIZE);
            Image heart = HudChrome.BuildGlyph(_livesRowRoot, "Heart", _heartSprite, heartSize, new Vector2(heartX, 0f));
            heart.preserveAspect = true;
            heart.color = _heartSprite != null ? Color.white : Color.clear;

            _livesRowCountText = HudChrome.CreateLabel(
                _livesRowRoot, "Count", _claimFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(heartX, LIVES_HEART_SIZE * 0.06f), _displayFont);
            _livesRowCountText.color = Color.white;
            Outline countOutline = _livesRowCountText.gameObject.AddComponent<Outline>();
            countOutline.effectColor = LivesCountOutline;
            countOutline.effectDistance = new Vector2(2f, -2f);

            var badgePosition = new Vector2(heartX + (LIVES_HEART_SIZE * 0.42f), LIVES_HEART_SIZE * 0.36f);
            Image badge = HudChrome.BuildCircle(_livesRowRoot, "LostBadge", LIVES_BADGE_SIZE, badgePosition);
            badge.color = LifeLostRed;
            Text badgeText = HudChrome.CreateLabel(
                badge.rectTransform, "Amount", _captionFontSize - 4, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, 1f), _labelFont);
            badgeText.color = Color.white;
            badgeText.text = LIFE_LOST_BADGE;

            float textX = heartX + (LIVES_HEART_SIZE * 0.5f) + COIN_ICON_TEXT_GAP;
            float half = COIN_BOX_HEIGHT * 0.5f;
            _livesRowTitleText = HudChrome.CreateLabel(
                _livesRowRoot, "Title", _captionFontSize + 4, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(textX, half - COIN_CAPTION_TOP - (_captionFontSize * 0.5f)), _labelFont);
            _livesRowTitleText.color = LivesInk;
            _livesRowDetailText = HudChrome.CreateLabel(
                _livesRowRoot, "Detail", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(textX, -half + COIN_VALUE_BOTTOM + (_captionFontSize * 0.5f)), _labelFont);
            _livesRowDetailText.color = LivesSubInk;

            _livesRowRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// One reward row (issues #462, #464): the coin box's plate, its caption on the left, and up to
        /// <see cref="REWARD_ROW_CAPACITY"/> accent discs carrying power-up glyphs, positioned per open
        /// by <see cref="ShowRewardRow"/>. Built hidden.
        /// </summary>
        private RewardRow BuildRewardRow(string objectName)
        {
            float rowWidth = _cardWidth - (SIDE_INSET * 2f);
            var rowSize = new Vector2(rowWidth, COIN_BOX_HEIGHT);
            RectTransform root = HudChrome.CreateRect(_cardRect, objectName, rowSize, Vector2.zero);
            HudChrome.BuildPlate(
                root, "Body", rowSize, Vector2.zero, COIN_BOX_RADIUS, HudChrome.PLATE_SHADOW_DROP,
                out Image shadow, out Image plate);

            float captionX = (-rowWidth * 0.5f) + COIN_BOX_PAD + COIN_ICON_TEXT_GAP;
            Text caption = HudChrome.CreateLabel(
                root, "Caption", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(captionX, 0f), _labelFont);

            var discs = new Image[REWARD_ROW_CAPACITY];
            var glyphs = new Image[REWARD_ROW_CAPACITY];
            for (int rewardIndex = 0; rewardIndex < REWARD_ROW_CAPACITY; rewardIndex++)
            {
                discs[rewardIndex] = HudChrome.BuildCircle(
                    root, $"RewardDisc_{rewardIndex}", COIN_DISC_SIZE, Vector2.zero);
                glyphs[rewardIndex] = HudChrome.BuildGlyph(
                    root, $"RewardGlyph_{rewardIndex}", null,
                    new Vector2(REWARD_GLYPH_SIZE, REWARD_GLYPH_SIZE), Vector2.zero);
            }

            root.gameObject.SetActive(false);
            return new RewardRow(root, shadow, plate, caption, discs, glyphs);
        }

        private StatPlate BuildStatPlate(string objectName, float width, Vector2 anchoredPosition)
        {
            RectTransform root = HudChrome.BuildPlate(
                _wellRoot, objectName, new Vector2(width, STAT_PLATE_HEIGHT), anchoredPosition,
                STAT_PLATE_RADIUS, HudChrome.PLATE_SHADOW_DROP, out Image shadow, out Image plate);

            float half = STAT_PLATE_HEIGHT * 0.5f;
            Text caption = HudChrome.CreateLabel(
                root, "Caption", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, half - STAT_LABEL_TOP - (_captionFontSize * 0.5f)), _labelFont);
            Text value = HudChrome.CreateLabel(
                root, "Value", _statFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, -half + STAT_VALUE_BOTTOM + (_statFontSize * 0.42f)), _displayFont);

            return new StatPlate(root, shadow, plate, caption, value);
        }

        /// <summary>
        /// One badge row: a plate the width of the well, the badge's gold icon disc on the left, its
        /// name beside that, and on the right either the glossy "+N" claim button or the green done
        /// check. The row's own rect is the hit area.
        /// </summary>
        private BadgeRow BuildBadgeRow(int rowIndex)
        {
            float rowWidth = _cardWidth - (SIDE_INSET * 2f);
            var rowSize = new Vector2(rowWidth, BADGE_ROW_HEIGHT);
            RectTransform root = HudChrome.CreateRect(_cardRect, $"BadgeRow_{rowIndex}", rowSize, Vector2.zero);
            HudChrome.BuildPlate(
                root, "Body", rowSize, Vector2.zero, BADGE_ROW_RADIUS, HudChrome.PLATE_SHADOW_DROP,
                out Image shadow, out Image plate);

            float discX = (-rowWidth * 0.5f) + BADGE_ROW_PAD + (BADGE_ICON_DISC_SIZE * 0.5f);
            Image iconDisc = HudChrome.BuildCircle(root, "IconDisc", BADGE_ICON_DISC_SIZE, new Vector2(discX, 0f));
            Image iconGlyph = HudChrome.BuildGlyph(
                root, "IconGlyph", null, new Vector2(BADGE_ICON_GLYPH_SIZE, BADGE_ICON_GLYPH_SIZE), new Vector2(discX, 0f));

            Text nameText = HudChrome.CreateLabel(
                root, "Name", _badgeNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(discX + (BADGE_ICON_DISC_SIZE * 0.5f) + BADGE_ICON_TEXT_GAP, 0f), _labelFont);

            float rightEdge = (rowWidth * 0.5f) - BADGE_ROW_PAD;
            var claimSize = new Vector2(BADGE_CLAIM_WIDTH, BADGE_CLAIM_HEIGHT);
            Image claimPlate = HudChrome.BuildGlossyPill(
                root, "Claim", claimSize, new Vector2(rightEdge - (BADGE_CLAIM_WIDTH * 0.5f), 0f), _buttonSprite);
            RectTransform claimRect = claimPlate.rectTransform;
            float coinX = -(BADGE_CLAIM_COIN_SIZE * 0.5f) - (BADGE_CLAIM_COIN_GAP * 0.5f) - (BADGE_CLAIM_COIN_SIZE * 0.5f);
            Image claimCoin = HudChrome.BuildGlyph(
                claimRect, "Coin", _coinSprite, new Vector2(BADGE_CLAIM_COIN_SIZE, BADGE_CLAIM_COIN_SIZE), new Vector2(coinX, 0f));
            Text claimText = HudChrome.CreateLabel(
                claimRect, "Amount", _claimFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(coinX + (BADGE_CLAIM_COIN_SIZE * 0.5f) + BADGE_CLAIM_COIN_GAP, -2f), _displayFont);

            float doneX = rightEdge - (BADGE_DONE_DISC_SIZE * 0.5f);
            Image doneDisc = HudChrome.BuildCircle(root, "DoneDisc", BADGE_DONE_DISC_SIZE, new Vector2(doneX, 0f));
            Image doneCheck = HudChrome.BuildGlyph(
                root, "DoneCheck", UiSpriteFactory.CheckMark,
                new Vector2(BADGE_DONE_CHECK_SIZE, BADGE_DONE_CHECK_SIZE), new Vector2(doneX, 0f));

            root.gameObject.SetActive(false);

            return new BadgeRow(
                root, shadow, plate, iconDisc, iconGlyph, nameText, claimPlate, claimCoin, claimText, doneDisc, doneCheck);
        }

        /// <summary>A full-width glossy action button with its label. Tinted and worded per open.</summary>
        private ActionButton BuildButton(string objectName)
        {
            var size = new Vector2(_cardWidth - (SIDE_INSET * 2f), BUTTON_HEIGHT);
            Image plate = HudChrome.BuildGlossyPill(_cardRect, objectName, size, Vector2.zero, _buttonSprite);
            Text label = HudChrome.CreateLabel(
                plate.rectTransform, "Label", _buttonFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, -2f), _displayFont);
            AddLetteringShadow(label, BUTTON_SHADOW_DROP).effectColor = new Color(0f, 0f, 0f, BUTTON_SHADOW_ALPHA);

            Image restartGlyph = HudChrome.BuildGlyph(
                plate.rectTransform, "Restart", _restartSprite, new Vector2(RESTART_GLYPH_SIZE, RESTART_GLYPH_SIZE), Vector2.zero);
            restartGlyph.color = Color.white;
            restartGlyph.gameObject.SetActive(false);

            float framed = NEXT_ICON_SIZE + (NEXT_ICON_FRAME * 2f);
            RectTransform iconRoot = HudChrome.CreateRect(plate.rectTransform, "LevelIcon", new Vector2(framed, framed), Vector2.zero);
            HudChrome.BuildRounded(iconRoot, "Frame", new Vector2(framed, framed), Vector2.zero, NEXT_ICON_RADIUS + NEXT_ICON_FRAME)
                .color = Color.white;
            Image levelIcon = HudChrome.BuildGlyph(iconRoot, "Icon", null, new Vector2(NEXT_ICON_SIZE, NEXT_ICON_SIZE), Vector2.zero);
            levelIcon.color = Color.white;
            iconRoot.gameObject.SetActive(false);

            Text subLabel = HudChrome.CreateLabel(
                plate.rectTransform, "SubLabel", _captionFontSize + 4, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, _labelFont);
            subLabel.color = NextSubLabelInk;
            subLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            subLabel.gameObject.SetActive(false);

            Image chevron = HudChrome.BuildGlyph(
                plate.rectTransform, "Chevron", _chevronSprite, new Vector2(NEXT_CHEVRON_SIZE, NEXT_CHEVRON_SIZE), Vector2.zero);
            chevron.color = Color.white;
            chevron.gameObject.SetActive(false);

            return new ActionButton(plate, label, restartGlyph, iconRoot, levelIcon, subLabel, chevron);
        }

        /// <summary>The mockup's hard "0 Npx 0" text shadow: a plain drop, no blur, riding the
        /// label's own alpha so an unpainted label casts nothing.</summary>
        private static Shadow AddLetteringShadow(Text text, float drop)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(0f, -drop);
            shadow.useGraphicAlpha = true;
            return shadow;
        }
    }
}
