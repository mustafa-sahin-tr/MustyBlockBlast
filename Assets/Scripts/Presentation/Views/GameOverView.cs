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
        private TimedHighScoreModel _timedHighScoreModel;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private GameObject _panel;
        private Image _cardImage;
        private Image _cardShadowImage;
        private Text _titleText;
        private Text _scoreText;
        private Text _hintText;
        private Text _changeModeText;
        private Canvas _canvas;

        /// <summary>
        /// Why the last run ended. Kept so a language switch can re-word the card without waiting for
        /// the next game over — the wording is reason-specific, so the reason has to outlive the message.
        /// </summary>
        private GameOverReason _lastGameOverReason = GameOverReason.NoMovesLeft;

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            TimedHighScoreModel timedHighScoreModel,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _scoreModel = scoreModel;
            _timedHighScoreModel = timedHighScoreModel;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
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

            _panel = panelObject;
        }

        /// <summary>
        /// True when the given screen point is on the "change mode" link. Only meaningful while the
        /// link is visible (timed game over). Called by <see cref="BoardInputView"/>.
        /// </summary>
        internal bool ContainsChangeModeScreenPoint(Vector2 screenPosition)
        {
            if (_changeModeText == null || !_changeModeText.gameObject.activeSelf)
            {
                return false;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)_changeModeText.transform, screenPosition, eventCamera);
        }

        private void OnGameOver(GameOverMessage message)
        {
            _lastGameOverReason = message.Reason;
            RefreshTitle();

            // Only the timed flow offers the escape hatch: endless keeps the whole card as one big
            // restart target, exactly as before.
            _changeModeText.gameObject.SetActive(message.Reason == GameOverReason.TimeUp);

            RefreshScoreLine();

            _panel.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void RefreshTitle()
        {
            string key = _lastGameOverReason == GameOverReason.TimeUp
                ? LocalizationKeys.GAME_OVER_TITLE_TIME_UP
                : LocalizationKeys.GAME_OVER_TITLE_NO_MOVES;

            _titleText.text = _localizationSystem.Translate(key);
        }

        private void RefreshScoreLine()
        {
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
