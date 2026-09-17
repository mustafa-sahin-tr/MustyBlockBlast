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
    /// score and the player's lifetime total. The end of a run reads as a result first and a choice of
    /// what to do next second (issue #220) — the game-over card with its Play Again / Next Level
    /// actions is exactly as it was, one tap further on.
    /// <para>
    /// Holds no logic and writes no model. The run score is captured off <see cref="ScoreModel"/> on
    /// <see cref="GameOverMessage"/>, because the next run resets it; the lifetime total is observed
    /// on <see cref="ProfileModel.TotalScoreEarned"/> rather than read once, because
    /// <see cref="CurrencySystem"/> banks the run into it on the very same message and this View must
    /// not depend on which subscriber the broker calls first.
    /// </para>
    /// <para>
    /// Modal while open: <see cref="BoardInputView"/> routes every tap here ahead of the game-over
    /// restart tap, and any tap dismisses it. Closes on <see cref="RunStartedMessage"/> as well, for
    /// the run that starts by some other route. Built once in <see cref="Awake"/> like the game-over
    /// card, and styled as that card is — a redesign is a later pass.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunResultView : MonoBehaviour
    {
        // Layout, in canvas reference pixels, matching the game-over card so the two read as a pair.
        private const float TITLE_Y = 150f;
        private const float RUN_SCORE_Y = 50f;
        private const float LIFETIME_TOTAL_Y = -20f;
        private const float HINT_Y = -150f;

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(760f, 460f);

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);

        private ScoreModel _scoreModel;
        private ProfileModel _profileModel;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;

        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private Text _titleText;
        private Text _runScoreText;
        private Text _lifetimeTotalText;
        private Text _hintText;

        /// <summary>The score of the run that just ended, captured on game over: the model's own value
        /// is reset by the next run, and a locale change must still be able to repaint this.</summary>
        private int _runScore;

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            ProfileModel profileModel,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _scoreModel = scoreModel;
            _profileModel = profileModel;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _gameOverSubscriber = gameOverSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
        }

        private void Awake()
        {
            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_scoreModel == null || _profileModel == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null
                || _gameOverSubscriber == null || _runStartedSubscriber == null)
            {
                Debug.LogError($"{nameof(RunResultView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // All three repaint whether the panel is showing or hidden, so it is already correct the
            // next time a run ends.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _profileModel.TotalScoreEarned.Subscribe(OnLifetimeTotalChanged).AddTo(_disposables);

            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the card is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Any tap while open dismisses the card and reveals the game-over card underneath. There is
        /// nothing on this card to aim at, so the position is not consulted; the parameter keeps the
        /// call shaped like every other overlay's for the gate chain.
        /// </summary>
        internal void HandleTap(Vector2 screenPosition)
        {
            if (!IsOpen)
            {
                return;
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

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;
            _titleText.color = theme.Ink;
            _runScoreText.color = theme.Ink;
            _lifetimeTotalText.color = theme.Accent;
            _hintText.color = theme.SoftInk;
        }

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            _titleText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_TITLE);
            _hintText.text = _localizationSystem.Translate(LocalizationKeys.RUN_RESULT_TAP_HINT);
            RefreshRunScore();
            RefreshLifetimeTotal();
        }

        private void OnLifetimeTotalChanged(int total) => RefreshLifetimeTotal();

        private void OnGameOver(GameOverMessage message)
        {
            _runScore = _scoreModel.Score.Value;
            RefreshRunScore();
            RefreshLifetimeTotal();

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

            // Built transparent: the card is built in Awake, before the theme or the language is known,
            // and the subscriptions in Start paint and word it.
            _titleText = UiTextFactory.Create(_cardRect, "Title", 72, FontStyle.Bold, Color.clear);
            ((RectTransform)_titleText.transform).anchoredPosition = new Vector2(0f, TITLE_Y);

            _runScoreText = UiTextFactory.Create(_cardRect, "RunScore", 56, FontStyle.Normal, Color.clear);
            ((RectTransform)_runScoreText.transform).anchoredPosition = new Vector2(0f, RUN_SCORE_Y);

            _lifetimeTotalText = UiTextFactory.Create(_cardRect, "LifetimeTotal", 44, FontStyle.Bold, Color.clear);
            ((RectTransform)_lifetimeTotalText.transform).anchoredPosition = new Vector2(0f, LIFETIME_TOTAL_Y);

            _hintText = UiTextFactory.Create(_cardRect, "Hint", 40, FontStyle.Normal, Color.clear);
            ((RectTransform)_hintText.transform).anchoredPosition = new Vector2(0f, HINT_Y);

            _panel = panelObject;
        }
    }
}
