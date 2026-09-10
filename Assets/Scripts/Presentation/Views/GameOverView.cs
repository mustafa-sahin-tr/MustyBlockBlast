using System.Text;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Shows the end-of-run card. Restarting is a tap handled by the input View.</summary>
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
        private SettingsModel _settingsModel;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private GameObject _panel;
        private Image _cardImage;
        private Image _cardShadowImage;
        private Text _titleText;
        private Text _scoreText;
        private Text _hintText;

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            SettingsModel settingsModel,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _scoreModel = scoreModel;
            _settingsModel = settingsModel;
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
            if (_gameOverSubscriber == null || _settingsModel == null)
            {
                Debug.LogError($"{nameof(GameOverView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Repaints the card and the labels whether the panel is showing or hidden, so it is
            // already correct the next time a run ends.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

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

            // The panel is built in Awake, before the theme is known; the theme subscription in Start
            // paints it (and repaints it on every later theme switch).
            _titleText = UiTextFactory.Create(card, "Title", 72, FontStyle.Bold, Color.clear);
            _titleText.text = "NO MOVES LEFT";
            ((RectTransform)_titleText.transform).anchoredPosition = new Vector2(0f, 110f);

            _scoreText = UiTextFactory.Create(card, "FinalScore", 56, FontStyle.Normal, Color.clear);
            ((RectTransform)_scoreText.transform).anchoredPosition = new Vector2(0f, 0f);

            _hintText = UiTextFactory.Create(card, "Hint", 40, FontStyle.Normal, Color.clear);
            _hintText.text = "Tap anywhere to play again";
            ((RectTransform)_hintText.transform).anchoredPosition = new Vector2(0f, -120f);

            _panel = panelObject;
        }

        private void OnGameOver(GameOverMessage message)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append("Score ");
            _stringBuilder.Append(_scoreModel.Score.Value);
            _stringBuilder.Append("   Best ");
            _stringBuilder.Append(_scoreModel.HighScore.Value);
            _scoreText.text = _stringBuilder.ToString();

            _panel.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void OnRunStarted(RunStartedMessage message) => _panel.SetActive(false);
    }
}
