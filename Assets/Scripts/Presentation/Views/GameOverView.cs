using System.Text;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
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
        [SerializeField] private BlockPalette _palette;
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);

        private ScoreModel _scoreModel;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private GameObject _panel;
        private Text _scoreText;

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _scoreModel = scoreModel;
            _gameOverSubscriber = gameOverSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
        }

        private void Awake()
        {
            if (_palette == null)
            {
                _palette = BlockPalette.CreateDefault();
            }

            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_gameOverSubscriber == null)
            {
                Debug.LogError($"{nameof(GameOverView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

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
                panelRect, "GameOverCard", _cardSize, _palette.CardBackground, _palette.CardShadow);

            Text title = UiTextFactory.Create(card, "Title", 72, FontStyle.Bold, _palette.Ink);
            title.text = "NO MOVES LEFT";
            ((RectTransform)title.transform).anchoredPosition = new Vector2(0f, 110f);

            _scoreText = UiTextFactory.Create(card, "FinalScore", 56, FontStyle.Normal, _palette.Ink);
            ((RectTransform)_scoreText.transform).anchoredPosition = new Vector2(0f, 0f);

            Text hint = UiTextFactory.Create(card, "Hint", 40, FontStyle.Normal, _palette.SoftInk);
            hint.text = "Tap anywhere to play again";
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -120f);

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
