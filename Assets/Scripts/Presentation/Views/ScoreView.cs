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
using UnityEngine.Serialization;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The storefront score card (issue #265): a plain card-coloured plate over its shadow under the
    /// top bar — no awning, by decision: on the play screen it only ate height the score could use —
    /// with the run score on the left under a "score" caption, the best score on the right under a
    /// trophy caption, and a centre slot between them that <see cref="TimerHudView"/> and
    /// <see cref="StreakPillView"/> take turns occupying. Binds to <see cref="ScoreModel"/>.
    /// <para>
    /// Only the card's own labels are drawn here. The slot is exposed as a bare rect on purpose: the
    /// card neither knows nor decides what sits in it, so adding a third occupant is a matter of
    /// parenting onto <see cref="CentreSlot"/> rather than of teaching the card another state.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScoreView : MonoBehaviour
    {
        /// <summary>Corner radius of the card: the mockup's 20px at the canvas's scale.</summary>
        private const float CARD_CORNER_RADIUS = 44f;

        /// <summary>The card's drop: deeper than a pill's, since it is the one plate on the HUD that
        /// reads as a piece of furniture rather than a chip.</summary>
        private const float CARD_SHADOW_DROP = 12f;

        /// <summary>Width and height of the slot in the middle of the card: wide enough for a
        /// "x2,5 STREAK" pill or a "mm:ss" clock, never wider than the gap between the two columns.</summary>
        private static readonly Vector2 CentreSlotSize = new Vector2(400f, 100f);

        /// <summary>Side of the trophy glyph beside the best caption.</summary>
        private const float TROPHY_SIZE = 30f;

        /// <summary>Gap between the trophy and the best caption's first letter.</summary>
        private const float TROPHY_GAP = 8f;

        /// <summary>The best value's ink: the accent pulled toward black — the mockup's #91700F on
        /// the gold accent — so the record reads as gold without washing out on the pale card.</summary>
        private const float BEST_VALUE_SHADE = 0.35f;

        [Header("Layout")]
        [Tooltip("Centre of the card, in reference pixels from the canvas centre. Sits under the top bar.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 728f);

        [Tooltip("Size of the card plate, in reference pixels. A full-width bar, as wide as the board card.")]
        [SerializeField] private Vector2 _cardSize = new Vector2(960f, 150f);

        [Tooltip("Horizontal inset of the two columns from the card's edges, in reference pixels.")]
        [SerializeField] private float _contentInset = 40f;

        [SerializeField] private int _scoreFontSize = 92;

        [Tooltip("Size of the two small uppercase captions (score, best).")]
        [FormerlySerializedAs("_bestLabelFontSize")]
        [SerializeField] private int _labelFontSize = 28;
        [SerializeField] private int _bestValueFontSize = 55;
        [SerializeField] private float _countUpDuration = 0.4f;

        [Header("New Record Celebration")]
        [SerializeField] private float _celebrationDuration = 0.7f;
        [SerializeField] private float _celebrationScale = 1.8f;

        [Header("Art")]
        [Tooltip("The chunky display face for the two numbers. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _scoreFont;

        [Tooltip("The heavy label face for the small uppercase captions. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _labelFont;

        [Tooltip("White trophy silhouette beside the best caption. Tinted with the accent; hidden when unassigned.")]
        [SerializeField] private Sprite _trophySprite;

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

        private Canvas _canvas;
        private Image _cardShadow;
        private Image _cardPlate;
        private Text _scoreLabelText;
        private Text _scoreText;
        private Text _bestLabelText;
        private Text _bestValueText;
        private Image _trophyImage;
        private RectTransform _trophyRect;
        private RectTransform _bestLabelRect;
        private RectTransform _bestValueRect;
        private RectTransform _centreSlot;

        private CancellationToken _destroyToken;
        private CancellationTokenSource _countUpCts;
        private CancellationTokenSource _celebrationCts;
        private int _displayedScore;
        private ThemeDefinition _currentTheme;

        /// <summary>
        /// The empty rect in the middle of the card, between the score and the best columns. Built in
        /// Awake, so it is safe to parent onto from any sibling's Start. Its occupants centre
        /// themselves in it; the card never resizes it.
        /// </summary>
        internal RectTransform CentreSlot => _centreSlot;

        /// <summary>
        /// The score number's on-screen position, in the same space <see cref="RectTransformUtility"/>
        /// screen-point conversions expect. Lets a sibling view (<see cref="BonusFeedbackView"/>, #329)
        /// compute a flight target toward this card without either view knowing the other's canvas
        /// nesting or scale.
        /// </summary>
        internal Vector2 ScoreCounterScreenPosition
        {
            get
            {
                Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? _canvas.worldCamera
                    : null;
                return RectTransformUtility.WorldToScreenPoint(eventCamera, _scoreText.transform.position);
            }
        }

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
            _canvas = GetComponentInParent<Canvas>();
            BuildCard();
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

        /// <summary>
        /// The card, then the three regions left to right: the score column, the centre slot and the
        /// best column. Built in Awake, before the theme is known; the theme subscription in Start
        /// paints it (and repaints it on every later switch).
        /// </summary>
        private void BuildCard()
        {
            var rect = (RectTransform)transform;
            HudChrome.Centre(rect, _cardSize);
            rect.anchoredPosition = _anchoredPosition;

            // Every size here is in canvas reference units, so the card owns its own scale rather than
            // inheriting whatever the scene object happened to be created with.
            rect.localScale = Vector3.one;

            RectTransform cardRect = HudChrome.BuildPlate(
                rect, "Card", _cardSize, Vector2.zero, CARD_CORNER_RADIUS, CARD_SHADOW_DROP,
                out _cardShadow, out _cardPlate);

            // With nothing along the card's top edge, the content band is the whole card: both columns
            // and the slot are laid out around its vertical centre. The captions sit above it and the
            // numbers hang a little below it, so a caption-and-number pair reads as centred.
            float bandCentreY = 0f;
            float labelY = bandCentreY + (_labelFontSize * 1.2f);
            float leftX = (-_cardSize.x * 0.5f) + _contentInset;
            float rightX = (_cardSize.x * 0.5f) - _contentInset;

            _scoreLabelText = HudChrome.CreateLabel(
                cardRect, "ScoreCaption", _labelFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX, labelY), _labelFont);
            _scoreText = HudChrome.CreateLabel(
                cardRect, "ScoreLabel", _scoreFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(leftX, bandCentreY - (_scoreFontSize * 0.2f)), _scoreFont);

            _centreSlot = HudChrome.CreateRect(cardRect, "CentreSlot", CentreSlotSize, new Vector2(0f, bandCentreY));

            _bestLabelText = HudChrome.CreateLabel(
                cardRect, "BestLabel", _labelFontSize, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(rightX, labelY), _labelFont);
            _bestLabelRect = (RectTransform)_bestLabelText.transform;

            // Positioned once the caption's width is known (see PlaceTrophy); centre-pivoted so its
            // anchored position is simply where it sits.
            _trophyImage = HudChrome.BuildGlyph(
                cardRect, "Trophy", _trophySprite, new Vector2(TROPHY_SIZE, TROPHY_SIZE), new Vector2(rightX, labelY));
            _trophyRect = (RectTransform)_trophyImage.transform;

            _bestValueText = HudChrome.CreateLabel(
                cardRect, "BestValue", _bestValueFontSize, FontStyle.Normal, TextAnchor.MiddleRight,
                new Vector2(rightX, bandCentreY - (_bestValueFontSize * 0.35f)), _scoreFont);
            _bestValueRect = (RectTransform)_bestValueText.transform;
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
            _scoreLabelText.color = theme.SoftInk;
            _scoreText.color = theme.Ink;
            _trophyImage.color = _trophySprite != null ? theme.Accent : Color.clear;

            // Skip repainting the best label/value while a celebration is mid-flight — it owns their
            // colour for its duration and will restore the themed colour itself when it ends.
            if (_celebrationCts == null)
            {
                _bestLabelText.color = theme.SoftInk;
                _bestValueText.color = BestValueColour(theme);
            }
        }

        private static Color BestValueColour(ThemeDefinition theme) => HudChrome.Darken(theme.Accent, BEST_VALUE_SHADE);

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

        /// <summary>
        /// Cancels any count-up in flight, mid-frame, before it has visibly progressed (its own first
        /// tick is a no-op — see <see cref="AnimateScoreToAsync"/>). Called by
        /// <see cref="BonusFeedbackView"/> (#329, AC2) the instant a bonus popup starts its flight, so
        /// the displayed number holds at its pre-bonus value until <see cref="ResumeScoreCountUp"/>
        /// releases it, rather than counting up in step with the raw <see cref="ScoreModel.Score"/>
        /// change that always lands first in the same synchronous publish.
        /// </summary>
        internal void PauseScoreCountUp() => CancelCountUp();

        /// <summary>
        /// Restarts the count-up toward the model's current score. Called once a bonus popup's flight
        /// has actually reached this card, so the number only starts climbing when the popup does
        /// (#329, AC2). Reads <see cref="ScoreModel.Score"/> live rather than a value passed in at
        /// pause time, since a later, superseding bonus may have moved the target since then.
        /// </summary>
        internal void ResumeScoreCountUp() => AnimateScoreToAsync(_scoreModel.Score.Value).Forget();

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
                _bestValueText.color = Color.Lerp(BestValueColour(_currentTheme), flashColor, pulse);
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
                _bestValueText.color = BestValueColour(_currentTheme);
            }
        }

        private void OnLocaleChanged(LocaleDefinition locale) => RefreshCaptions();

        private void OnModeChanged(GameMode mode) => RefreshCaptions();

        private void OnHighScoreChanged(int highScore) => RefreshCaptions();

        private void OnTimedBestChanged(int timedBest) => RefreshCaptions();

        private void OnSelectedDurationChanged(float durationSeconds) => RefreshCaptions();

        /// <summary>
        /// Repaints the score caption and the best caption and value from whichever best is
        /// authoritative for the active mode. Timed mode names its duration in the caption since a
        /// best is only comparable within its own duration; endless keeps the plain caption.
        /// </summary>
        private void RefreshCaptions()
        {
            _scoreLabelText.text = _localizationSystem.Translate(LocalizationKeys.HUD_SCORE_LABEL);

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

            PlaceTrophy();

            _stringBuilder.Clear();
            _stringBuilder.Append(best);
            _bestValueText.text = _stringBuilder.ToString();
        }

        /// <summary>
        /// Sits the trophy just left of the caption's first letter. The caption is right-aligned and
        /// overflows its rect, so its left edge is its right edge less its preferred width — measured
        /// off the text generator, which is current as soon as the text is set.
        /// </summary>
        private void PlaceTrophy()
        {
            float captionLeft = _bestLabelRect.anchoredPosition.x - _bestLabelText.preferredWidth;
            _trophyRect.anchoredPosition = new Vector2(
                captionLeft - TROPHY_GAP - (TROPHY_SIZE * 0.5f), _bestLabelRect.anchoredPosition.y);
        }
    }
}
