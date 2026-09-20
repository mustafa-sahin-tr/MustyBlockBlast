using System.Collections.Generic;
using System.Text;
using MessagePipe;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
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
    /// nothing; the card closes on <see cref="RunStartedMessage"/>, which every action ends in.
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

        [Tooltip("White trophy silhouette beside the record caption. Hidden when unassigned.")]
        [SerializeField] private Sprite _trophySprite;

        [Tooltip("White clock silhouette on the timed card's 00:00 pill. Hidden when unassigned.")]
        [SerializeField] private Sprite _clockSprite;

        [Tooltip("The coin on a badge row's claim button. Hidden when unassigned.")]
        [SerializeField] private Sprite _coinSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);
        private readonly BadgeRow[] _badgeRows = new BadgeRow[BADGE_ROW_COUNT];
        private readonly Image[] _starDiscs = new Image[STAR_COUNT];
        private readonly Image[] _starDiscShadows = new Image[STAR_COUNT];
        private readonly Image[] _starGlyphs = new Image[STAR_COUNT];

        private ScoreModel _scoreModel;
        private ProfileModel _profileModel;
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
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private ISubscriber<NewRecordMessage> _newRecordSubscriber;

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

        private Text _badgesHeadingText;
        private Text _claimHintText;

        private ActionButton _primaryButton;
        private ActionButton _secondaryButton;

        private ThemeDefinition _currentTheme;

        /// <summary>The score of the run that just ended, captured on game over: the model's own value
        /// is reset by the next run, and a locale change must still be able to repaint this.</summary>
        private int _runScore;

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

        /// <summary>One glossy action button: the pill is the hit area, the label sits on it. What
        /// the button does and which kind tints it are set per open in <see cref="RefreshButtons"/>.</summary>
        private sealed class ActionButton
        {
            internal ActionButton(Image plate, Text labelText)
            {
                Plate = plate;
                Rect = plate.rectTransform;
                LabelText = labelText;
            }

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
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<NewRecordMessage> newRecordSubscriber)
        {
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
            _gameOverSubscriber = gameOverSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
            _newRecordSubscriber = newRecordSubscriber;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_scoreModel == null || _profileModel == null || _pathRunModel == null || _timedHighScoreModel == null
                || _badgeModel == null || _badgeSystem == null || _badgeCatalog == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null || _gameModeSystem == null
                || _timedModeSystem == null || _gameOverSubscriber == null || _runStartedSubscriber == null
                || _newRecordSubscriber == null)
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
        }

        private void OnDestroy() => _disposables.Dispose();

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

            if (RectTransformUtility.RectangleContainsScreenPoint(_primaryButton.Rect, screenPosition, eventCamera))
            {
                return _primaryButton.Action;
            }

            if (_secondaryButton.Rect.gameObject.activeSelf
                && RectTransformUtility.RectangleContainsScreenPoint(_secondaryButton.Rect, screenPosition, eventCamera))
            {
                return _secondaryButton.Action;
            }

            return RunEndAction.None;
        }

        private void OnGameOver(GameOverMessage message)
        {
            _runScore = _scoreModel.Score.Value;
            _lastReason = message.Reason;
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
            _panel.SetActive(false);
        }

        private void OnNewRecord(NewRecordMessage message) => _isNewRecordThisRun = true;

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

            _badgesHeadingText.color = theme.SoftInk;
            _claimHintText.color = theme.Accent;

            RefreshBadges();
            PaintButton(_primaryButton);
            PaintButton(_secondaryButton);
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
        /// Which buttons the card offers, from the captured reason and mode. A Path success with a
        /// next level leads with the green advance and keeps "play again" under it; a Path failure
        /// is a single "try again"; Timed keeps its "change mode" escape hatch under "play again";
        /// everything else — Endless, or a Path success on the last authored level — is the one
        /// restart button.
        /// </summary>
        private void RefreshButtons()
        {
            if (_localizationSystem == null)
            {
                return;
            }

            bool isPath = _lastMode == GameMode.Path;
            bool showSecondary;

            if (_lastReason == GameOverReason.LevelCompleted && _hasNextLevel)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(NextLevelNumber);
                ConfigureButton(
                    _primaryButton, RunEndAction.NextLevel, HudChrome.GREEN_KIND,
                    _localizationSystem.Format(LocalizationKeys.RUN_RESULT_NEXT_LEVEL, _stringBuilder.ToString()));
                ConfigureButton(
                    _secondaryButton, RunEndAction.PlayAgain, PRIMARY_KIND,
                    _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_PLAY_AGAIN));
                showSecondary = true;
            }
            else
            {
                string restartKey = isPath && _lastReason != GameOverReason.LevelCompleted
                    ? LocalizationKeys.RUN_RESULT_TRY_AGAIN
                    : LocalizationKeys.RUN_RESULT_PLAY_AGAIN;
                ConfigureButton(
                    _primaryButton, RunEndAction.PlayAgain, PRIMARY_KIND, _localizationSystem.Translate(restartKey));

                showSecondary = _lastMode == GameMode.Timed;
                if (showSecondary)
                {
                    ConfigureButton(
                        _secondaryButton, RunEndAction.ChangeMode, SECONDARY_KIND,
                        _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_CHANGE_MODE));
                }
            }

            _secondaryButton.Rect.gameObject.SetActive(showSecondary);
        }

        private void ConfigureButton(ActionButton button, RunEndAction action, int kind, string label)
        {
            button.Action = action;
            button.Kind = kind;
            button.LabelText.text = label;
            PaintButton(button);
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
            HangCentre(_primaryButton.Rect, 0f, y + (BUTTON_HEIGHT * 0.5f));
            y += BUTTON_HEIGHT;
            if (_secondaryButton.Rect.gameObject.activeSelf)
            {
                y += BUTTON_GAP;
                HangCentre(_secondaryButton.Rect, 0f, y + (BUTTON_HEIGHT * 0.5f));
                y += BUTTON_HEIGHT;
            }

            y += BOTTOM_PAD;

            _cardRect.sizeDelta = new Vector2(_cardWidth, y);
            _cardRect.anchoredPosition = new Vector2(0f, CARD_CENTRE_Y + (y * 0.5f));
        }

        /// <summary>Hangs a centre-pivoted rect (or an edge-pivoted label) from the card's top edge,
        /// its vertical centre <paramref name="centreFromTop"/> below it. The pivot is left alone, so
        /// a left- or right-aligned label keeps <paramref name="x"/> as its aligned edge.</summary>
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

            _primaryButton = BuildButton("PrimaryButton");
            _secondaryButton = BuildButton("SecondaryButton");
            _secondaryButton.Rect.gameObject.SetActive(false);

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
            return new ActionButton(plate, label);
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
