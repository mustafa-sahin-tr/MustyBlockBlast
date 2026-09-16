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
    /// Shows the end-of-run card, worded for why the run ended. Restarting is a tap handled by the
    /// input View; timed runs also get a "change mode" link back to the mode/duration picker.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameOverView : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(760f, 420f);

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);

        private ScoreModel _scoreModel;
        private PathRunModel _pathRunModel;
        private TimedHighScoreModel _timedHighScoreModel;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private LevelCatalog _levelCatalog;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private GameObject _panel;
        private Image _cardImage;
        private Image _cardShadowImage;
        private Text _titleText;
        private Text _scoreText;
        private Text _hintText;
        private Text _changeModeText;
        private RectTransform _playAgainRoot;
        private Image _playAgainShadowImage;
        private Image _playAgainPlateImage;
        private Image _playAgainIconImage;
        private RectTransform _nextLevelRoot;
        private Image _nextLevelShadowImage;
        private Image _nextLevelPlateImage;
        private Text _nextLevelNumberText;
        private Canvas _canvas;

        /// <summary>
        /// Why the last run ended. Kept so a language switch can re-word the card without waiting for
        /// the next game over — the wording is reason-specific, so the reason has to outlive the message.
        /// </summary>
        private GameOverReason _lastGameOverReason = GameOverReason.NoMovesLeft;

        /// <summary>
        /// <see cref="PathRunModel.ActiveLevelNumber"/> at the moment the last <see cref="GameOverMessage"/>
        /// was shown. Captured rather than read live: Path mode can replay an already-unlocked level
        /// below the progression frontier, so the level actually played has to outlive the message the
        /// same way <see cref="_lastGameOverReason"/> does, for the same re-word-on-language-switch reason.
        /// </summary>
        private int _completedLevelNumber;

        /// <summary>Whether the catalog authors a level past <see cref="_completedLevelNumber"/>. Only
        /// meaningful while <see cref="_lastGameOverReason"/> is <see cref="GameOverReason.LevelCompleted"/>.</summary>
        private bool _hasNextLevel;

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            PathRunModel pathRunModel,
            TimedHighScoreModel timedHighScoreModel,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            LevelCatalog levelCatalog,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _scoreModel = scoreModel;
            _pathRunModel = pathRunModel;
            _timedHighScoreModel = timedHighScoreModel;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
            _levelCatalog = levelCatalog;
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
            if (_gameOverSubscriber == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null)
            {
                Debug.LogError($"{nameof(GameOverView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Both repaint the card and the labels whether the panel is showing or hidden, so it is
            // already correct the next time a run ends.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);

            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;
            _titleText.color = theme.Ink;
            _scoreText.color = theme.Ink;
            _hintText.color = theme.SoftInk;
            _changeModeText.color = theme.SoftInk;

            // A neutral plate — the same treatment LevelPathPanelView gives an already-cleared node —
            // so the restart action reads as the secondary one next to Next Level's accent-filled call
            // to action.
            Color neutralPlate = Color.Lerp(theme.CardBackground, theme.Ink, 0.16f);
            _playAgainPlateImage.color = neutralPlate;
            _playAgainShadowImage.color = theme.CardShadow;
            _playAgainIconImage.color = theme.Ink;

            // Accent-filled badge with the number punched out of it — the same treatment the level
            // path overlay gives the node the player is currently on, so "jump to this level" reads
            // the same way in both places.
            _nextLevelPlateImage.color = theme.Accent;
            _nextLevelShadowImage.color = theme.CardShadow;
            _nextLevelNumberText.color = theme.CardBackground;
        }

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            _hintText.text = _localizationSystem.Translate(LocalizationKeys.GAME_OVER_HINT);
            _changeModeText.text = _localizationSystem.Translate(LocalizationKeys.GAME_OVER_CHANGE_MODE);
            RefreshTitle();
            RefreshScoreLine();
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("GameOverPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            RectTransform card = CellFactory.CreateCard(
                panelRect, "GameOverCard", _cardSize, out _cardImage, out _cardShadowImage);

            // The panel is built in Awake, before the theme or the language is known; the theme and
            // locale subscriptions in Start paint and word it (and redo both on every later switch).
            _titleText = UiTextFactory.Create(card, "Title", 72, FontStyle.Bold, Color.clear);
            ((RectTransform)_titleText.transform).anchoredPosition = new Vector2(0f, 110f);

            _scoreText = UiTextFactory.Create(card, "FinalScore", 56, FontStyle.Normal, Color.clear);
            ((RectTransform)_scoreText.transform).anchoredPosition = new Vector2(0f, 0f);

            _hintText = UiTextFactory.Create(card, "Hint", 40, FontStyle.Normal, Color.clear);
            ((RectTransform)_hintText.transform).anchoredPosition = new Vector2(0f, -120f);

            // Sized explicitly rather than left at the default text rect: this is the one label on
            // the card that is hit-tested, so its rect has to be a sane tap target.
            _changeModeText = UiTextFactory.Create(card, "ChangeMode", 32, FontStyle.Normal, Color.clear);
            var changeModeRect = (RectTransform)_changeModeText.transform;
            changeModeRect.sizeDelta = new Vector2(320f, 60f);
            changeModeRect.anchoredPosition = new Vector2(0f, -175f);
            _changeModeText.gameObject.SetActive(false);

            // The two-button Path-mode layout, shown only for a LevelCompleted game over with a next
            // level to advance to — see OnGameOver. Both are round badges sitting where the hint text
            // sits for every other reason, and both are hidden by default for the same reason
            // _changeModeText is: nothing has shown a game over yet.
            _playAgainRoot = BuildRoundBadge(
                card, "PlayAgainButton", new Vector2(-160f, -140f),
                out _playAgainShadowImage, out _playAgainPlateImage);

            var playAgainIconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var playAgainIconRect = (RectTransform)playAgainIconObject.transform;
            playAgainIconRect.SetParent(_playAgainRoot, false);
            playAgainIconRect.anchorMin = new Vector2(0.5f, 0.5f);
            playAgainIconRect.anchorMax = new Vector2(0.5f, 0.5f);
            playAgainIconRect.pivot = new Vector2(0.5f, 0.5f);
            playAgainIconRect.sizeDelta = new Vector2(ROUND_BADGE_SIZE * 0.56f, ROUND_BADGE_SIZE * 0.56f);
            _playAgainIconImage = playAgainIconObject.GetComponent<Image>();
            _playAgainIconImage.sprite = UiSpriteFactory.RefreshIcon;
            _playAgainIconImage.type = Image.Type.Simple;
            _playAgainIconImage.color = Color.clear;
            _playAgainIconImage.raycastTarget = false;

            _playAgainRoot.gameObject.SetActive(false);

            _nextLevelRoot = BuildRoundBadge(
                card, "NextLevelButton", new Vector2(160f, -140f),
                out _nextLevelShadowImage, out _nextLevelPlateImage);
            _nextLevelNumberText = UiTextFactory.Create(_nextLevelRoot, "Number", 48, FontStyle.Bold, Color.clear);
            _nextLevelRoot.gameObject.SetActive(false);

            _panel = panelObject;
        }

        /// <summary>Side of the two-button Path-mode layout's round badges — shared so "Play Again" and
        /// "Next Level" always match in size whatever glyph or number sits inside them.</summary>
        private const float ROUND_BADGE_SIZE = 120f;

        /// <summary>
        /// Builds one round badge — shadow plus a tintable plate, both the shared <see cref="UiSpriteFactory.Circle"/>
        /// sprite — at <paramref name="anchoredPosition"/> on the card. The caller adds whatever sits on
        /// top (a number, an icon) and is responsible for activating the returned root once it has.
        /// Mirrors <c>LevelPathPanelView</c>'s node look, so every round tap target on the game-over
        /// card and the level path overlay reads as the same kind of thing.
        /// </summary>
        private static RectTransform BuildRoundBadge(
            RectTransform card, string name, Vector2 anchoredPosition, out Image shadowImage, out Image plateImage)
        {
            var rootObject = new GameObject(name, typeof(RectTransform));
            var root = (RectTransform)rootObject.transform;
            root.SetParent(card, false);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(ROUND_BADGE_SIZE, ROUND_BADGE_SIZE);
            root.anchoredPosition = anchoredPosition;

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(root, false);
            shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
            shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
            shadowRect.pivot = new Vector2(0.5f, 0.5f);
            shadowRect.sizeDelta = new Vector2(ROUND_BADGE_SIZE + 10f, ROUND_BADGE_SIZE + 10f);
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            shadowImage = shadowObject.GetComponent<Image>();
            shadowImage.sprite = UiSpriteFactory.Circle;
            shadowImage.type = Image.Type.Simple;
            shadowImage.color = Color.clear;
            shadowImage.raycastTarget = false;

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(root, false);
            plateRect.anchorMin = new Vector2(0.5f, 0.5f);
            plateRect.anchorMax = new Vector2(0.5f, 0.5f);
            plateRect.pivot = new Vector2(0.5f, 0.5f);
            plateRect.sizeDelta = new Vector2(ROUND_BADGE_SIZE, ROUND_BADGE_SIZE);
            plateImage = plateObject.GetComponent<Image>();
            plateImage.sprite = UiSpriteFactory.Circle;
            plateImage.type = Image.Type.Simple;
            plateImage.color = Color.clear;
            plateImage.raycastTarget = false;

            return root;
        }

        /// <summary>
        /// True when the given screen point is on the "change mode" link. Only meaningful while the
        /// link is visible (timed game over). Called by <see cref="BoardInputView"/>.
        /// </summary>
        internal bool ContainsChangeModeScreenPoint(Vector2 screenPosition)
            => ContainsScreenPoint((RectTransform)_changeModeText.transform, screenPosition);

        /// <summary>
        /// True when the given screen point is on the round "Play Again" badge. Only meaningful while
        /// the two-button Path-mode layout is showing — see <see cref="OnGameOver"/>. Called by
        /// <see cref="BoardInputView"/>.
        /// </summary>
        internal bool ContainsPlayAgainScreenPoint(Vector2 screenPosition)
            => ContainsScreenPoint(_playAgainRoot, screenPosition);

        /// <summary>
        /// True when the given screen point is on the round "Next Level" badge. Only meaningful while
        /// the two-button Path-mode layout is showing — see <see cref="OnGameOver"/>. Called by
        /// <see cref="BoardInputView"/>.
        /// </summary>
        internal bool ContainsNextLevelScreenPoint(Vector2 screenPosition)
            => ContainsScreenPoint(_nextLevelRoot, screenPosition);

        /// <summary>
        /// The level number "Next Level" would start, or -1 when there is none — i.e. whenever the
        /// button itself is hidden. <see cref="BoardInputView"/> reads this once
        /// <see cref="ContainsNextLevelScreenPoint"/> has confirmed the tap landed on the button.
        /// </summary>
        internal int NextLevelNumber => _hasNextLevel ? _completedLevelNumber + 1 : -1;

        private bool ContainsScreenPoint(RectTransform rect, Vector2 screenPosition)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return false;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, eventCamera);
        }

        private void OnGameOver(GameOverMessage message)
        {
            _lastGameOverReason = message.Reason;

            if (message.Reason == GameOverReason.LevelCompleted)
            {
                // The level actually played, not the progression frontier — Path mode can replay an
                // already-unlocked level below it. Captured now because ActiveLevelNumber can move on
                // to whatever the player taps next before this card is dismissed.
                _completedLevelNumber = _pathRunModel.ActiveLevelNumber.Value;
                _hasNextLevel = _levelCatalog != null
                    && _levelCatalog.Find(_completedLevelNumber + 1) != null;
            }
            else
            {
                _hasNextLevel = false;
            }

            RefreshTitle();

            // Two distinct outcomes share this card: a Path-mode success with a next level to advance
            // to gets its own two-button layout: everything else — including a Path-mode success on the
            // last authored level — keeps the single full-card restart tap it always had, with the timed
            // flow's "change mode" escape hatch layered on top of that same tap.
            bool showTwoButtons = _hasNextLevel;
            _hintText.gameObject.SetActive(!showTwoButtons);
            _playAgainRoot.gameObject.SetActive(showTwoButtons);
            _nextLevelRoot.gameObject.SetActive(showTwoButtons);
            _changeModeText.gameObject.SetActive(message.Reason == GameOverReason.TimeUp);

            if (showTwoButtons)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(NextLevelNumber);
                _nextLevelNumberText.text = _stringBuilder.ToString();
            }

            RefreshScoreLine();

            _panel.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// <summary>
        /// Words the card from the reason the run ended, which is the only thing that separates a
        /// Path-mode success from a Path-mode failure — both show this same card, so the reason has to
        /// carry the difference rather than a second screen doing it. A Path-mode success also names the
        /// level just cleared, using the number captured in <see cref="OnGameOver"/>.
        /// </summary>
        private void RefreshTitle()
        {
            if (_lastGameOverReason == GameOverReason.LevelCompleted)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(_completedLevelNumber);
                _titleText.text = _localizationSystem.Format(
                    LocalizationKeys.GAME_OVER_TITLE_LEVEL_COMPLETE, _stringBuilder.ToString());
                return;
            }

            string key = _lastGameOverReason == GameOverReason.TimeUp
                ? LocalizationKeys.GAME_OVER_TITLE_TIME_UP
                : LocalizationKeys.GAME_OVER_TITLE_NO_MOVES;

            _titleText.text = _localizationSystem.Translate(key);
        }

        private void RefreshScoreLine()
        {
            if (_gameModeSystem.CurrentMode.Value == GameMode.Path)
            {
                // Both figures, because a Path run resets the first and only ever adds to the second:
                // showing the level's score alone would hide the walk, and the walk alone would hide
                // what this level was worth.
                _stringBuilder.Clear();
                _stringBuilder.Append(_scoreModel.Score.Value);
                string levelScore = _stringBuilder.ToString();

                _stringBuilder.Clear();
                _stringBuilder.Append(_pathRunModel.PathTotalScore.Value);

                _scoreText.text = _localizationSystem.Format(
                    LocalizationKeys.GAME_OVER_SCORE_AND_PATH_TOTAL, levelScore, _stringBuilder.ToString());
                return;
            }

            if (_gameModeSystem.CurrentMode.Value == GameMode.Timed)
            {
                // Timed bests are per round length, so the length has to be named or the number is
                // meaningless. The length goes through the shared seconds format, so it reads exactly
                // as the countdown HUD spells it.
                _stringBuilder.Clear();
                _stringBuilder.Append((int)_timedModeSystem.SelectedDuration.Value);
                string duration = _localizationSystem.Format(
                    LocalizationKeys.FORMAT_SECONDS, _stringBuilder.ToString());

                _stringBuilder.Clear();
                _stringBuilder.Append(_timedHighScoreModel.Best.Value);

                _scoreText.text = _localizationSystem.Format(
                    LocalizationKeys.GAME_OVER_BEST_FOR_DURATION, duration, _stringBuilder.ToString());
                return;
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(_scoreModel.Score.Value);
            string score = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append(_scoreModel.HighScore.Value);

            _scoreText.text = _localizationSystem.Format(
                LocalizationKeys.GAME_OVER_SCORE_AND_BEST, score, _stringBuilder.ToString());
        }

        private void OnRunStarted(RunStartedMessage message) => _panel.SetActive(false);
    }
}
