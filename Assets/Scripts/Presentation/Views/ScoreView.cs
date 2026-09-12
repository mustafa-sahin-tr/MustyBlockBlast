using System.Text;
using MustyBlockBlast.Gameplay;
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
    /// Large score at the top-centre; "Best" is pinned to the top-left corner instead of sitting
    /// under the score, so it stays legible and out of the score's way. Binds to <see cref="ScoreModel"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScoreView : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 780f);
        [SerializeField] private int _scoreFontSize = 130;
        [SerializeField] private int _bestLabelFontSize = 36;
        [SerializeField] private int _bestValueFontSize = 96;
        [SerializeField] private Vector2 _bestCornerOffset = new Vector2(32f, -32f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        private ScoreModel _scoreModel;
        private TimedHighScoreModel _timedHighScoreModel;
        private SettingsModel _settingsModel;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private Text _scoreText;
        private Text _bestLabelText;
        private Text _bestValueText;

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            TimedHighScoreModel timedHighScoreModel,
            SettingsModel settingsModel,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem)
        {
            _scoreModel = scoreModel;
            _timedHighScoreModel = timedHighScoreModel;
            _settingsModel = settingsModel;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(600f, 220f);
            rect.anchoredPosition = _anchoredPosition;

            // Labels are built in Awake, before the theme is known; the theme subscription in Start
            // paints them (and repaints them on every later theme switch).
            _scoreText = UiTextFactory.Create(rect, "ScoreLabel", _scoreFontSize, FontStyle.Bold, Color.clear);

            // "Best" is pinned to the Canvas's top-left corner rather than nested under the centred
            // score box, so its position doesn't depend on where the score sits. Label and value are
            // two stacked labels rather than one line with a space, so a timed duration suffix (e.g.
            // "BEST (15s)") never pushes the number sideways.
            var canvasRect = (RectTransform)transform.parent;

            _bestLabelText = UiTextFactory.Create(canvasRect, "BestLabel", _bestLabelFontSize, FontStyle.Bold, Color.clear);
            var bestLabelRect = (RectTransform)_bestLabelText.transform;
            bestLabelRect.anchorMin = new Vector2(0f, 1f);
            bestLabelRect.anchorMax = new Vector2(0f, 1f);
            bestLabelRect.pivot = new Vector2(0f, 1f);
            bestLabelRect.anchoredPosition = _bestCornerOffset;
            _bestLabelText.alignment = TextAnchor.UpperLeft;

            _bestValueText = UiTextFactory.Create(canvasRect, "BestValue", _bestValueFontSize, FontStyle.Bold, Color.clear);
            var bestValueRect = (RectTransform)_bestValueText.transform;
            bestValueRect.anchorMin = new Vector2(0f, 1f);
            bestValueRect.anchorMax = new Vector2(0f, 1f);
            bestValueRect.pivot = new Vector2(0f, 1f);
            bestValueRect.anchoredPosition = _bestCornerOffset + new Vector2(0f, -(_bestLabelFontSize + 8f));
            _bestValueText.alignment = TextAnchor.UpperLeft;
        }

        private void Start()
        {
            if (_scoreModel == null || _timedHighScoreModel == null || _settingsModel == null
                || _gameModeSystem == null || _timedModeSystem == null)
            {
                Debug.LogError($"{nameof(ScoreView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _scoreModel.Score.Subscribe(OnScoreChanged).AddTo(_disposables);

            // "Best" has several inputs that can change which value or wording is authoritative:
            // the active mode, the endless high score, the timed per-duration best, and (for the
            // duration suffix) the selected timed duration itself.
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);
            _scoreModel.HighScore.Subscribe(OnHighScoreChanged).AddTo(_disposables);
            _timedHighScoreModel.Best.Subscribe(OnTimedBestChanged).AddTo(_disposables);
            _timedModeSystem.SelectedDuration.Subscribe(OnSelectedDurationChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _scoreText.color = theme.Ink;
            _bestLabelText.color = theme.SoftInk;
            _bestValueText.color = theme.Ink;
        }

        private void OnScoreChanged(int score)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(score);
            _scoreText.text = _stringBuilder.ToString();
        }

        private void OnModeChanged(GameMode mode) => RefreshBestLabel();

        private void OnHighScoreChanged(int highScore) => RefreshBestLabel();

        private void OnTimedBestChanged(int timedBest) => RefreshBestLabel();

        private void OnSelectedDurationChanged(float durationSeconds) => RefreshBestLabel();

        /// <summary>
        /// Repaints the "BEST" label and value from whichever best is authoritative for the active
        /// mode. Timed mode names its duration in the label (e.g. "BEST (15s)") since a best is only
        /// comparable within its own duration; endless keeps the plain "BEST" label.
        /// </summary>
        private void RefreshBestLabel()
        {
            bool isTimed = _gameModeSystem.CurrentMode.Value == GameMode.Timed;
            int best = isTimed ? _timedHighScoreModel.Best.Value : _scoreModel.HighScore.Value;

            _stringBuilder.Clear();
            _stringBuilder.Append("BEST");
            if (isTimed)
            {
                _stringBuilder.Append(" (");
                _stringBuilder.Append((int)_timedModeSystem.SelectedDuration.Value);
                _stringBuilder.Append("s)");
            }
            _bestLabelText.text = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append(best);
            _bestValueText.text = _stringBuilder.ToString();
        }
    }
}
