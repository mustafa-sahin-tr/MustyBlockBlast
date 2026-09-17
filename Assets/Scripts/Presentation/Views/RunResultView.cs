using System.Collections.Generic;
using System.Text;
using MessagePipe;
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
    /// The result summary shown the moment a run ends, before <see cref="GameOverView"/>: this run's
    /// score, the player's lifetime total, and every badge that unlocked during the run. The end of a
    /// run reads as a result first and a choice of what to do next second (issue #220) — the
    /// game-over card with its Play Again / Next Level actions is exactly as it was, one tap further on.
    /// <para>
    /// Holds no logic and writes no model. The run score is captured off <see cref="ScoreModel"/> on
    /// <see cref="GameOverMessage"/>, because the next run resets it; the lifetime total is observed
    /// on <see cref="ProfileModel.TotalScoreEarned"/> rather than read once, because
    /// <see cref="CurrencySystem"/> banks the run into it on the very same message and this View must
    /// not depend on which subscriber the broker calls first.
    /// </para>
    /// <para>
    /// The badge list (issue #221) is read from <see cref="BadgeModel.UnlockedThisRun"/>, which
    /// <see cref="BadgeSystem"/> clears at run start, so a badge from an earlier run can never appear
    /// here. A row is tappable while its reward is unclaimed; the tap goes to
    /// <see cref="BadgeSystem.ClaimReward"/>, the coins land on the profile, and the row repaints as
    /// done through <see cref="BadgeModel.Revision"/>. With no unlocks the section is not drawn at
    /// all and the card keeps its short height.
    /// </para>
    /// <para>
    /// Modal while open: <see cref="BoardInputView"/> routes every tap here ahead of the game-over
    /// restart tap. A tap on a claimable badge claims; any other tap dismisses. Closes on
    /// <see cref="RunStartedMessage"/> as well, for the run that starts by some other route. Built once
    /// in <see cref="Awake"/> like the game-over card, and styled as that card is — a redesign is a
    /// later pass.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunResultView : MonoBehaviour
    {
        // Layout, in canvas reference pixels, matching the game-over card so the two read as a pair.
        // Everything above the hint hangs from the card's top edge and the hint from its bottom, so the
        // card can grow by the badge section without any of it moving relative to its own edge.
        private const float TITLE_INSET = 80f;
        private const float RUN_SCORE_INSET = 178f;
        private const float LIFETIME_TOTAL_INSET = 246f;
        private const float BADGES_TITLE_INSET = 326f;
        private const float BADGE_ROWS_INSET = 392f;
        private const float HINT_BOTTOM_INSET = 60f;

        /// <summary>Height added to the base card for the badge heading, plus one pitch per row.</summary>
        private const float BADGES_TITLE_HEIGHT = 66f;
        private const float BADGE_ROW_HEIGHT = 92f;
        private const float BADGE_ROW_PITCH = 104f;
        private const float BADGE_ROW_SIDE_INSET = 50f;
        private const float BADGE_ROW_TEXT_INSET = 20f;
        private const float BADGE_ICON_DISC_SIZE = 68f;
        private const float BADGE_ICON_GLYPH_SIZE = 42f;
        private const float BADGE_ICON_TEXT_GAP = 16f;
        private const float BADGE_ROW_CORNER_RADIUS = 14f;
        private const float BADGE_ROW_PLATE_TINT = 0.06f;
        private const float BADGE_DONE_DOT_SIZE = 24f;

        /// <summary>Rows the card can draw. More unlocks in one run than this are simply not listed;
        /// they stay claimable from the badge wall, and a run that fells five badges is not a real
        /// case worth a scrolling list here.</summary>
        private const int BADGE_ROW_COUNT = 4;

        /// <summary>Prefix on the claim amount, so "+50" reads as something to collect. A symbol, not a
        /// word — nothing here for a translator.</summary>
        private const string REWARD_PREFIX = "+";

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(760f, 460f);

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);
        private readonly BadgeRow[] _badgeRows = new BadgeRow[BADGE_ROW_COUNT];

        private ScoreModel _scoreModel;
        private ProfileModel _profileModel;
        private BadgeModel _badgeModel;
        private BadgeSystem _badgeSystem;
        private BadgeCatalog _badgeCatalog;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private RectTransform _cardShadowRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private Text _titleText;
        private Text _runScoreText;
        private Text _lifetimeTotalText;
        private Text _badgesTitleText;
        private Text _hintText;

        private ThemeDefinition _currentTheme;

        /// <summary>The score of the run that just ended, captured on game over: the model's own value
        /// is reset by the next run, and a locale change must still be able to repaint this.</summary>
        private int _runScore;

        /// <summary>One built badge row. Rebuilt never, repainted on every open and every claim.</summary>
        private sealed class BadgeRow
        {
            internal BadgeRow(
                RectTransform root, Image plate, Image iconDisc, Image iconGlyph, Image doneDot,
                Text nameText, Text rewardText)
            {
                Root = root;
                Plate = plate;
                IconDisc = iconDisc;
                IconGlyph = iconGlyph;
                DoneDot = doneDot;
                NameText = nameText;
                RewardText = rewardText;
            }

            internal RectTransform Root { get; }

            internal Image Plate { get; }

            internal Image IconDisc { get; }

            internal Image IconGlyph { get; }

            internal Image DoneDot { get; }

            internal Text NameText { get; }

            internal Text RewardText { get; }

            /// <summary>Id of the badge this row currently draws, or null while it draws none.</summary>
            internal string BadgeId { get; set; }

            /// <summary>Whether the row currently offers a claim. Cached from the last repaint so the
            /// tap path does not re-derive it.</summary>
            internal bool IsClaimable { get; set; }
        }

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            ProfileModel profileModel,
            BadgeModel badgeModel,
            BadgeSystem badgeSystem,
            BadgeCatalog badgeCatalog,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _scoreModel = scoreModel;
            _profileModel = profileModel;
            _badgeModel = badgeModel;
            _badgeSystem = badgeSystem;
            _badgeCatalog = badgeCatalog;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _gameOverSubscriber = gameOverSubscriber;
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
            if (_scoreModel == null || _profileModel == null || _badgeModel == null || _badgeSystem == null
                || _badgeCatalog == null || _settingsModel == null || _localizationModel == null
                || _localizationSystem == null || _gameOverSubscriber == null || _runStartedSubscriber == null)
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
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the card is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Routes a tap while open. A tap on a badge row that still has a reward claims it and keeps
        /// the card up, so the player sees the row turn done and the HUD coins tick; any other tap
        /// dismisses the card and reveals the game-over card underneath.
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
                    return;
                }
            }

            Close();
        }

        private void Close() => _panel.SetActive(false);

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;
            _titleText.color = theme.Ink;
            _runScoreText.color = theme.Ink;
            _lifetimeTotalText.color = theme.Accent;
            _badgesTitleText.color = theme.SoftInk;
            _hintText.color = theme.SoftInk;

            RefreshBadges();
        }

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            _titleText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_TITLE);
            _badgesTitleText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_BADGES_TITLE);
            _hintText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_TAP_HINT);
            RefreshRunScore();
            RefreshLifetimeTotal();
            RefreshBadges();
        }

        private void OnLifetimeTotalChanged(int total) => RefreshLifetimeTotal();

        private void OnBadgesChanged(int revision) => RefreshBadges();

        private void OnGameOver(GameOverMessage message)
        {
            _runScore = _scoreModel.Score.Value;
            RefreshRunScore();
            RefreshLifetimeTotal();
            RefreshBadges();

            _panel.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void OnRunStarted(RunStartedMessage message) => _panel.SetActive(false);

        private void RefreshRunScore()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(_runScore);
            _runScoreText.text = _localizationSystem.Format(
                LocalizationKeys.RUN_RESULT_RUN_SCORE, _stringBuilder.ToString());
        }

        private void RefreshLifetimeTotal()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(_profileModel.TotalScoreEarned.Value);
            _lifetimeTotalText.text = _localizationSystem.Format(
                LocalizationKeys.RUN_RESULT_LIFETIME_TOTAL, _stringBuilder.ToString());
        }

        /// <summary>
        /// Repaints the badge section from this run's unlock buffer: sizes the card for however many
        /// rows there are (none at all is the common case, and then the section is hidden and the card
        /// is its base height), then paints each row as claimable or done.
        /// </summary>
        private void RefreshBadges()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            IReadOnlyList<string> unlocked = _badgeModel.UnlockedThisRun;
            int rowCount = Mathf.Min(unlocked.Count, BADGE_ROW_COUNT);

            float height = _cardSize.y;
            if (rowCount > 0)
            {
                height += BADGES_TITLE_HEIGHT + (rowCount * BADGE_ROW_PITCH);
            }

            var size = new Vector2(_cardSize.x, height);
            _cardRect.sizeDelta = size;
            _cardShadowRect.sizeDelta = size + new Vector2(10f, 10f);

            bool showSection = rowCount > 0;
            if (_badgesTitleText.gameObject.activeSelf != showSection)
            {
                _badgesTitleText.gameObject.SetActive(showSection);
            }

            for (int rowIndex = 0; rowIndex < _badgeRows.Length; rowIndex++)
            {
                RefreshBadgeRow(rowIndex, rowIndex < rowCount ? unlocked[rowIndex] : null);
            }
        }

        private void RefreshBadgeRow(int rowIndex, string badgeId)
        {
            BadgeRow row = _badgeRows[rowIndex];

            bool exists = badgeId != null;
            if (row.Root.gameObject.activeSelf != exists)
            {
                row.Root.gameObject.SetActive(exists);
            }

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

            // A claimable row inverts onto the accent plate, as the badge wall's unlocked tile does,
            // so the thing to tap is the loudest thing on the card; a claimed row settles onto a quiet
            // tinted plate with the done dot.
            Color plateColour = isClaimable
                ? _currentTheme.Accent
                : Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, BADGE_ROW_PLATE_TINT);
            Color inkOnPlate = isClaimable ? _currentTheme.CardBackground : _currentTheme.Ink;

            row.Plate.color = plateColour;
            row.IconDisc.color = _currentTheme.CardBackground;
            row.IconGlyph.sprite = icon;
            row.IconGlyph.color = icon == null ? Color.clear : (isClaimable ? _currentTheme.Accent : _currentTheme.Ink);
            row.NameText.color = inkOnPlate;
            row.NameText.text = DisplayNameOf(config, badgeId);

            row.DoneDot.color = isClaimable ? Color.clear : _currentTheme.Accent;
            row.RewardText.color = isClaimable ? inkOnPlate : Color.clear;
            row.RewardText.text = isClaimable ? FormatReward(_badgeSystem.CoinRewardOf(badgeId)) : string.Empty;
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

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

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

            _cardRect = CellFactory.CreateCard(
                panelRect, "RunResultCard", _cardSize, out _cardImage, out _cardShadowImage);
            _cardShadowRect = _cardShadowImage.rectTransform;

            // Built transparent: the card is built in Awake, before the theme or the language is known,
            // and the subscriptions in Start paint and word it.
            _titleText = CreateTopAnchoredLabel("Title", 72, FontStyle.Bold, TITLE_INSET);
            _runScoreText = CreateTopAnchoredLabel("RunScore", 56, FontStyle.Normal, RUN_SCORE_INSET);
            _lifetimeTotalText = CreateTopAnchoredLabel("LifetimeTotal", 44, FontStyle.Bold, LIFETIME_TOTAL_INSET);
            _badgesTitleText = CreateTopAnchoredLabel("BadgesTitle", 30, FontStyle.Normal, BADGES_TITLE_INSET);
            _badgesTitleText.gameObject.SetActive(false);

            for (int rowIndex = 0; rowIndex < BADGE_ROW_COUNT; rowIndex++)
            {
                _badgeRows[rowIndex] = BuildBadgeRow(
                    rowIndex, BADGE_ROWS_INSET + (rowIndex * BADGE_ROW_PITCH) + (BADGE_ROW_HEIGHT * 0.5f));
            }

            _hintText = UiTextFactory.Create(_cardRect, "Hint", 40, FontStyle.Normal, Color.clear);
            var hintRect = (RectTransform)_hintText.transform;
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, HINT_BOTTOM_INSET);

            _panel = panelObject;
        }

        /// <summary>A centred label hanging <paramref name="inset"/> below the card's top edge, so it
        /// stays put when the card grows for the badge section.</summary>
        private Text CreateTopAnchoredLabel(string objectName, int fontSize, FontStyle fontStyle, float inset)
        {
            Text text = UiTextFactory.Create(_cardRect, objectName, fontSize, fontStyle, Color.clear);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = new Vector2(0.5f, 1f);
            textRect.anchorMax = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -inset);
            return text;
        }

        /// <summary>
        /// One badge row hanging from the card's top edge: a rounded plate the whole width of the card
        /// less its side insets (the plate's rect is the hit area), the badge's icon disc on the left,
        /// its name beside that, and the claim amount or the done dot on the right.
        /// </summary>
        private BadgeRow BuildBadgeRow(int rowIndex, float centreInset)
        {
            float rowWidth = _cardSize.x - (BADGE_ROW_SIDE_INSET * 2f);
            var rowSize = new Vector2(rowWidth, BADGE_ROW_HEIGHT);

            var rowObject = new GameObject($"BadgeRow_{rowIndex}", typeof(RectTransform), typeof(Image));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(_cardRect, false);
            rowRect.anchorMin = new Vector2(0.5f, 1f);
            rowRect.anchorMax = new Vector2(0.5f, 1f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.sizeDelta = rowSize;
            rowRect.anchoredPosition = new Vector2(0f, -centreInset);

            var plateImage = rowObject.GetComponent<Image>();
            plateImage.sprite = UiSpriteFactory.RoundedSquare;
            plateImage.type = Image.Type.Sliced;
            plateImage.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / BADGE_ROW_CORNER_RADIUS;
            plateImage.color = Color.clear;
            plateImage.raycastTarget = false;

            float discX = (-rowWidth * 0.5f) + BADGE_ROW_TEXT_INSET + (BADGE_ICON_DISC_SIZE * 0.5f);

            var discObject = new GameObject("IconDisc", typeof(RectTransform), typeof(Image));
            var discRect = (RectTransform)discObject.transform;
            discRect.SetParent(rowRect, false);
            Centre(discRect, new Vector2(BADGE_ICON_DISC_SIZE, BADGE_ICON_DISC_SIZE));
            discRect.anchoredPosition = new Vector2(discX, 0f);
            var discImage = discObject.GetComponent<Image>();
            ConfigureCircle(discImage);

            var glyphObject = new GameObject("IconGlyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(discRect, false);
            Centre(glyphRect, new Vector2(BADGE_ICON_GLYPH_SIZE, BADGE_ICON_GLYPH_SIZE));
            var glyphImage = glyphObject.GetComponent<Image>();
            glyphImage.type = Image.Type.Simple;
            glyphImage.preserveAspect = true;
            glyphImage.color = Color.clear;
            glyphImage.raycastTarget = false;

            float textX = discX + (BADGE_ICON_DISC_SIZE * 0.5f) + BADGE_ICON_TEXT_GAP;
            Text nameText = UiTextFactory.Create(rowRect, "Name", 34, FontStyle.Bold, Color.clear);
            nameText.alignment = TextAnchor.MiddleLeft;
            var nameRect = (RectTransform)nameText.transform;
            nameRect.pivot = new Vector2(0f, 0.5f);
            nameRect.anchoredPosition = new Vector2(textX, 0f);

            float rightX = (rowWidth * 0.5f) - BADGE_ROW_TEXT_INSET;
            Text rewardText = UiTextFactory.Create(rowRect, "Reward", 34, FontStyle.Bold, Color.clear);
            rewardText.alignment = TextAnchor.MiddleRight;
            var rewardRect = (RectTransform)rewardText.transform;
            rewardRect.pivot = new Vector2(1f, 0.5f);
            rewardRect.anchoredPosition = new Vector2(rightX, 0f);

            var dotObject = new GameObject("DoneDot", typeof(RectTransform), typeof(Image));
            var dotRect = (RectTransform)dotObject.transform;
            dotRect.SetParent(rowRect, false);
            Centre(dotRect, new Vector2(BADGE_DONE_DOT_SIZE, BADGE_DONE_DOT_SIZE));
            dotRect.anchoredPosition = new Vector2(rightX - (BADGE_DONE_DOT_SIZE * 0.5f), 0f);
            var dotImage = dotObject.GetComponent<Image>();
            ConfigureCircle(dotImage);

            rowObject.SetActive(false);

            return new BadgeRow(rowRect, plateImage, discImage, glyphImage, dotImage, nameText, rewardText);
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not
        // through an EventSystem, and this scene has none. The circle sprite has no border, so it is
        // never sliced.
        private static void ConfigureCircle(Image image)
        {
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            image.raycastTarget = false;
        }
    }
}
