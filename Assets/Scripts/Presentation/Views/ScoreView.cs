using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        [SerializeField] private float _countUpDuration = 0.4f;

        [Header("New Record Celebration")]
        [SerializeField] private float _celebrationDuration = 0.7f;
        [SerializeField] private float _celebrationScale = 1.8f;

        [Header("Score Style")]
        [Tooltip("Decorative font for the score label only. Leave empty to fall back to the builtin font.")]
        [SerializeField] private Font _scoreFont;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        private ScoreModel _scoreModel;
        private TimedHighScoreModel _timedHighScoreModel;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private ISubscriber<NewRecordMessage> _newRecordSubscriber;
        private Text _scoreText;
        private Text _bestLabelText;
        private Text _bestValueText;
        private RectTransform _bestLabelRect;
        private RectTransform _bestValueRect;

        private CancellationToken _destroyToken;
        private CancellationTokenSource _countUpCts;
        private CancellationTokenSource _celebrationCts;
        private int _displayedScore;
        private ThemeDefinition _currentTheme;

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            TimedHighScoreModel timedHighScoreModel,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<NewRecordMessage> newRecordSubscriber)
        {
            _scoreModel = scoreModel;
            _timedHighScoreModel = timedHighScoreModel;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
            _runStartedSubscriber = runStartedSubscriber;
            _newRecordSubscriber = newRecordSubscriber;
        }

        private void Awake()
        {
            _destroyToken = this.GetCancellationTokenOnDestroy();

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(600f, 220f);
            rect.anchoredPosition = _anchoredPosition;

            // Labels are built in Awake, before the theme is known; the theme subscription in Start
            // paints them (and repaints them on every later theme switch).
            _scoreText = UiTextFactory.Create(rect, "ScoreLabel", _scoreFontSize, FontStyle.Bold, Color.clear, _scoreFont);

            // "Best" is pinned to the Canvas's top-left corner rather than nested under the centred
            // score box, so its position doesn't depend on where the score sits. Label and value are
            // two stacked labels rather than one line with a space, so a timed duration suffix (e.g.
            // "BEST (15s)") never pushes the number sideways.
            var canvasRect = (RectTransform)transform.parent;

            _bestLabelText = UiTextFactory.Create(canvasRect, "BestLabel", _bestLabelFontSize, FontStyle.Bold, Color.clear);
            _bestLabelRect = (RectTransform)_bestLabelText.transform;
            _bestLabelRect.anchorMin = new Vector2(0f, 1f);
            _bestLabelRect.anchorMax = new Vector2(0f, 1f);
            _bestLabelRect.pivot = new Vector2(0f, 1f);
            _bestLabelRect.anchoredPosition = _bestCornerOffset;
            _bestLabelText.alignment = TextAnchor.UpperLeft;

            _bestValueText = UiTextFactory.Create(canvasRect, "BestValue", _bestValueFontSize, FontStyle.Bold, Color.clear);
            _bestValueRect = (RectTransform)_bestValueText.transform;
            _bestValueRect.anchorMin = new Vector2(0f, 1f);
            _bestValueRect.anchorMax = new Vector2(0f, 1f);
            _bestValueRect.pivot = new Vector2(0f, 1f);
            _bestValueRect.anchoredPosition = _bestCornerOffset + new Vector2(0f, -(_bestLabelFontSize + 8f));
            _bestValueText.alignment = TextAnchor.UpperLeft;
        }

        private void Start()
        {
            if (_scoreModel == null || _timedHighScoreModel == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null
                || _gameModeSystem == null || _timedModeSystem == null || _runStartedSubscriber == null
                || _newRecordSubscriber == null)
            {
                Debug.LogError($"{nameof(ScoreView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _scoreModel.Score.Subscribe(OnScoreChanged).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
            _newRecordSubscriber.Subscribe(OnNewRecordReached).AddTo(_disposables);

            // "Best" has several inputs that can change which value or wording is authoritative:
            // the active mode, the endless high score, the timed per-duration best, (for the
            // duration suffix) the selected timed duration itself, and the language the label and its
            // suffix are written in.
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);
            _scoreModel.HighScore.Subscribe(OnHighScoreChanged).AddTo(_disposables);
            _timedHighScoreModel.Best.Subscribe(OnTimedBestChanged).AddTo(_disposables);
            _timedModeSystem.SelectedDuration.Subscribe(OnSelectedDurationChanged).AddTo(_disposables);
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            CancelCountUp();
            CancelCelebrationToken();
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            _scoreText.color = theme.Accent;

            // Skip repainting the best label/value while a celebration is mid-flight — it owns their
            // colour for its duration and will restore the themed colour itself when it ends.
            if (_celebrationCts == null)
            {
                _bestLabelText.color = theme.SoftInk;
                _bestValueText.color = theme.Ink;
            }
        }

        private void OnScoreChanged(int score) => AnimateScoreToAsync(score).Forget();

        /// <summary>
        /// Resets the label to 0 immediately with no animation, so a fresh run never visibly
        /// counts down from (or up from) the previous run's final score.
        /// </summary>
        private void OnRunStarted(RunStartedMessage message)
        {
            CancelCountUp();
            SetDisplayedScore(0);
            CancelCelebrationToken();
            ResetCelebrationVisuals();
        }

        /// <summary>
        /// Counts the displayed score up to <paramref name="targetScore"/> over a fixed duration.
        /// A new call always supersedes any animation already in flight, retargeting from the
        /// currently displayed value rather than restarting or stacking (acceptance criterion 3).
        /// </summary>
        private async UniTaskVoid AnimateScoreToAsync(int targetScore)
        {
            CancelCountUp();

            if (_displayedScore == targetScore)
            {
                // Still writes the text even when the value is unchanged, e.g. the initial
                // Subscribe callback with the starting score before any label text exists.
                SetDisplayedScore(targetScore);
                return;
            }

            _countUpCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
            CancellationToken token = _countUpCts.Token;

            int startScore = _displayedScore;

            try
            {
                float elapsed = 0f;
                while (elapsed < _countUpDuration)
                {
                    float t = Mathf.Clamp01(elapsed / _countUpDuration);
                    SetDisplayedScore(startScore + Mathf.RoundToInt((targetScore - startScore) * t));
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer score change, a run restart, or object destruction.
                return;
            }

            SetDisplayedScore(targetScore);
        }

        private void CancelCountUp()
        {
            if (_countUpCts == null)
            {
                return;
            }

            _countUpCts.Cancel();
            _countUpCts.Dispose();
            _countUpCts = null;
        }

        private void SetDisplayedScore(int score)
        {
            _displayedScore = score;
            _stringBuilder.Clear();
            _stringBuilder.Append(score);
            _scoreText.text = _stringBuilder.ToString();
        }

        /// <summary>
        /// <see cref="ScoreSystem"/> only ever publishes this while in Endless mode, so no mode check
        /// is needed here — the best label is already showing <see cref="ScoreModel.HighScore"/>.
        /// </summary>
        private void OnNewRecordReached(NewRecordMessage message) => PlayNewRecordCelebrationAsync().Forget();

        /// <summary>
        /// Pulse-scales and colour-flashes the best label/value once. A new call always supersedes any
        /// celebration already in flight, mirroring <see cref="AnimateScoreToAsync"/>.
        /// </summary>
        private async UniTaskVoid PlayNewRecordCelebrationAsync()
        {
            CancelCelebrationToken();
            _celebrationCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
            CancellationToken token = _celebrationCts.Token;

            Color flashColor = _currentTheme != null ? _currentTheme.Accent : Color.white;

            try
            {
                float elapsed = 0f;
                while (elapsed < _celebrationDuration)
                {
                    float t = Mathf.Clamp01(elapsed / _celebrationDuration);
                    float pulse = Mathf.Sin(t * Mathf.PI);
                    ApplyCelebrationFrame(pulse, flashColor);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a run restart or object destruction.
                return;
            }

            _celebrationCts.Dispose();
            _celebrationCts = null;
            ResetCelebrationVisuals();
        }

        private void ApplyCelebrationFrame(float pulse, Color flashColor)
        {
            float scale = 1f + pulse * (_celebrationScale - 1f);
            var scaleVector = new Vector3(scale, scale, 1f);
            _bestLabelRect.localScale = scaleVector;
            _bestValueRect.localScale = scaleVector;

            if (_currentTheme != null)
            {
                _bestLabelText.color = Color.Lerp(_currentTheme.SoftInk, flashColor, pulse);
                _bestValueText.color = Color.Lerp(_currentTheme.Ink, flashColor, pulse);
            }
        }

        private void CancelCelebrationToken()
        {
            if (_celebrationCts == null)
            {
                return;
            }

            _celebrationCts.Cancel();
            _celebrationCts.Dispose();
            _celebrationCts = null;
        }

        private void ResetCelebrationVisuals()
        {
            _bestLabelRect.localScale = Vector3.one;
            _bestValueRect.localScale = Vector3.one;

            if (_currentTheme != null)
            {
                _bestLabelText.color = _currentTheme.SoftInk;
                _bestValueText.color = _currentTheme.Ink;
            }
        }

        private void OnLocaleChanged(LocaleDefinition locale) => RefreshBestLabel();

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

            if (isTimed)
            {
                // The round length goes through the shared minutes format rather than being spelled
                // here, so the duration picker and this suffix always read the same way.
                _stringBuilder.Clear();
                _stringBuilder.Append(Mathf.RoundToInt(_timedModeSystem.SelectedDuration.Value / 60f));
                string duration = _localizationSystem.Format(
                    LocalizationKeys.FORMAT_MINUTES, _stringBuilder.ToString());

                _bestLabelText.text = _localizationSystem.Format(LocalizationKeys.SCORE_BEST_TIMED, duration);
            }
            else
            {
                _bestLabelText.text = _localizationSystem.Translate(LocalizationKeys.SCORE_BEST);
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(best);
            _bestValueText.text = _stringBuilder.ToString();
        }
    }
}
