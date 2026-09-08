using System.Text;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Large score at the top, smaller "Best" line below it. Binds to <see cref="ScoreModel"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class ScoreView : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 780f);
        [SerializeField] private int _scoreFontSize = 130;
        [SerializeField] private int _bestFontSize = 40;
        [SerializeField] private float _bestOffsetY = -95f;

        [Header("Palette")]
        [SerializeField] private BlockPalette _palette;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        private ScoreModel _scoreModel;
        private Text _scoreText;
        private Text _bestText;

        [Inject]
        public void Construct(ScoreModel scoreModel)
        {
            _scoreModel = scoreModel;
        }

        private void Awake()
        {
            if (_palette == null)
            {
                _palette = BlockPalette.CreateDefault();
            }

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(600f, 220f);
            rect.anchoredPosition = _anchoredPosition;

            _scoreText = UiTextFactory.Create(rect, "ScoreLabel", _scoreFontSize, FontStyle.Bold, _palette.Ink);
            _bestText = UiTextFactory.Create(rect, "BestLabel", _bestFontSize, FontStyle.Normal, _palette.SoftInk);
            ((RectTransform)_bestText.transform).anchoredPosition = new Vector2(0f, _bestOffsetY);
        }

        private void Start()
        {
            if (_scoreModel == null)
            {
                Debug.LogError($"{nameof(ScoreView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _scoreModel.Score.Subscribe(OnScoreChanged).AddTo(_disposables);
            _scoreModel.HighScore.Subscribe(OnHighScoreChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnScoreChanged(int score)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(score);
            _scoreText.text = _stringBuilder.ToString();
        }

        private void OnHighScoreChanged(int highScore)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append("BEST ");
            _stringBuilder.Append(highScore);
            _bestText.text = _stringBuilder.ToString();
        }
    }
}
