using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Board-anchored "+N BONUS" popup shown whenever a placement earns scoring bonus points (#61).
    /// Deliberately reports the flat combined total and never says which rule fired. Sits in the same
    /// visual family as <see cref="LineClearBurstView"/> but is a separate, simpler element: a single
    /// animated label, placed above the burst word so the two never overlap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BonusFeedbackView : MonoBehaviour
    {
        /// <summary>At or above this many points the popup uses the louder "big" treatment. The
        /// board-wipe bonus is a flat 200 and so is always big; the multiplier-shaped bonuses are
        /// usually smaller. Magnitude only — no rule identity is involved.</summary>
        private const int BIG_BONUS_THRESHOLD = 100;

        private const float STANDARD_DURATION = 0.8f;
        private const float BIG_DURATION = 1.15f;

        private const int STANDARD_FONT_SIZE = 40;
        private const int BIG_FONT_SIZE = 52;

        private const float STANDARD_PEAK_SCALE = 1.06f;
        private const float BIG_PEAK_SCALE = 1.3f;

        private const float GROW_FRACTION = 0.18f;
        private const float SETTLE_FRACTION = 0.12f;
        private const float FADE_FRACTION = 0.35f;

        /// <summary>Floor the flight's punch-adjusted scale shrinks to as the popup nears the score
        /// counter (#329) — never all the way to zero, so the label is still legible for its last
        /// visible frame rather than popping out of existence.</summary>
        private const float FLIGHT_MIN_SCALE = 0.3f;

        /// <summary>Colour flashes the big tier cycles through per second.</summary>
        private const float BIG_FLASH_CYCLES = 3f;

        /// <summary>Palette entry the big tier's colour flash tints towards.</summary>
        private const int FLASH_COLOUR_ID = 3;

        // Fixed dark outline, matching LineClearBurstView: the fill is always near-white, so a
        // theme-derived (possibly light) ink would make the label unreadable.
        private static readonly Color TextOutlineColor = new Color(0.1686f, 0.1529f, 0.20f, 1f);

        [Header("Layout")]
        [Tooltip("Popup centre in canvas space. Kept clear of the line-clear burst word below it.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 340f);

        [Header("Timing")]
        [Tooltip("Multiplies both tier durations. 1 = the approved timings.")]
        [SerializeField] private float _durationScale = 1f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        // Reused so the per-popup label costs no string concatenation garbage.
        private readonly StringBuilder _labelBuilder = new StringBuilder(16);

        private ISubscriber<BonusScoredMessage> _bonusScoredSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private SettingsModel _settingsModel;
        private ScoreView _scoreView;
        private ThemeDefinition _currentTheme;

        private Canvas _canvas;
        private GameObject _layerObject;
        private RectTransform _centreRect;
        private RectTransform _textRect;
        private Text _text;
        private Outline _textOutline;

        private Vector2 _flightStart;
        private Vector2 _flightTarget;

        private CancellationToken _destroyToken;
        private CancellationTokenSource _popupCts;
        private int _generation;
        private bool _isDestroyed;

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            ISubscriber<BonusScoredMessage> bonusScoredSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ScoreView scoreView)
        {
            _settingsModel = settingsModel;
            _bonusScoredSubscriber = bonusScoredSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
            _scoreView = scoreView;
        }

        private void Awake()
        {
            _destroyToken = this.GetCancellationTokenOnDestroy();
            _canvas = GetComponentInParent<Canvas>();

            BuildHierarchy();
            _layerObject.SetActive(false);
        }

        private void Start()
        {
            if (_bonusScoredSubscriber == null || _settingsModel == null || _scoreView == null)
            {
                Debug.LogError(
                    $"{nameof(BonusFeedbackView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // A popup lives for about a second, so the theme is only read when one starts — nothing is
            // repainted retroactively.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _bonusScoredSubscriber.Subscribe(OnBonusScored).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _disposables.Dispose();
            CancelPopup();
        }

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        private void OnBonusScored(BonusScoredMessage message)
        {
            // Defensive: ScoreSystem never publishes a zero bonus.
            if (message.BonusAmount <= 0 || _currentTheme == null)
            {
                return;
            }

            PlayAsync(message.BonusAmount).Forget();
        }

        /// <summary>Wipes any lingering or in-flight popup the moment a fresh run begins.</summary>
        private void OnRunStarted(RunStartedMessage message) => StopPopup();

        private void StopPopup()
        {
            CancelPopup();

            // Bumping the generation stops the cancelled run's cleanup from touching shared state later.
            _generation++;
            Hide();
        }

        private void CancelPopup()
        {
            if (_popupCts == null)
            {
                return;
            }

            _popupCts.Cancel();
            _popupCts.Dispose();
            _popupCts = null;
        }

        private async UniTaskVoid PlayAsync(int bonusAmount)
        {
            // A newer bonus always supersedes the one still on screen.
            CancelPopup();

            // Holds the score card's own count-up right where it stands (#329, AC2): the raw
            // ScoreModel.Score change that triggers it always lands, synchronously, before this bonus
            // message does, so by the time control reaches here that count-up has been kicked off but
            // has not yet visibly progressed — cancelling it now is a silent no-op on screen. Only a
            // popup that actually finishes its flight releases it again, below.
            _scoreView.PauseScoreCountUp();

            _popupCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
            CancellationToken token = _popupCts.Token;
            int generation = ++_generation;

            bool isBig = bonusAmount >= BIG_BONUS_THRESHOLD;
            float duration = Mathf.Max(0.05f, (isBig ? BIG_DURATION : STANDARD_DURATION) * _durationScale);

            Setup(bonusAmount, isBig);

            bool reachedTarget = false;

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    Animate(elapsed / duration, isBig);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.unscaledDeltaTime;
                }

                reachedTarget = true;
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer bonus, a restart, or object destruction. Whichever popup
                // supersedes this one paused the counter again on its own way in, and will be the one
                // to release it — or, on a restart/destruction, ScoreView resets independently.
            }
            finally
            {
                // Only clean up when nothing newer has taken over, otherwise this would wipe the
                // freshly-set start state of the popup that replaced this one.
                if (!_isDestroyed && generation == _generation)
                {
                    Hide();
                }
            }

            // The number only starts climbing once the popup that earned it has actually arrived
            // (#329, AC2) — never on a superseded or cancelled flight.
            if (reachedTarget && !_isDestroyed)
            {
                _scoreView.ResumeScoreCountUp();
            }
        }

        private void Setup(int bonusAmount, bool isBig)
        {
            _layerObject.SetActive(true);
            transform.SetAsLastSibling();

            _labelBuilder.Clear();
            _labelBuilder.Append('+');
            _labelBuilder.Append(bonusAmount);
            _labelBuilder.Append(" BONUS");
            _text.text = _labelBuilder.ToString();

            _text.fontSize = isBig ? BIG_FONT_SIZE : STANDARD_FONT_SIZE;
            _textOutline.effectDistance = isBig ? new Vector2(2f, -2f) : new Vector2(1.5f, -1.5f);

            _flightStart = _anchoredPosition;
            _flightTarget = ResolveFlightTarget();
            _centreRect.anchoredPosition = _flightStart;
            _textRect.localScale = Vector3.one;
        }

        /// <summary>
        /// The score card's on-screen position (<see cref="ScoreView.ScoreCounterScreenPosition"/>),
        /// converted into this popup's own canvas-local space (#329). Falls back to the popup's spawn
        /// position — a zero-distance "flight" — if either canvas is unavailable or the conversion
        /// fails, so a missing reference degrades to the old in-place behaviour rather than throwing.
        /// </summary>
        private Vector2 ResolveFlightTarget()
        {
            if (_canvas == null)
            {
                return _anchoredPosition;
            }

            Vector2 screenPoint = _scoreView.ScoreCounterScreenPosition;
            Camera eventCamera = _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_centreRect.parent, screenPoint, eventCamera, out Vector2 localPoint))
            {
                return _anchoredPosition;
            }

            return localPoint;
        }

        private void Animate(float normalisedTime, bool isBig)
        {
            float fadeStart = 1f - FADE_FRACTION;
            float fade = normalisedTime < fadeStart
                ? 1f
                : 1f - Mathf.Clamp01((normalisedTime - fadeStart) / FADE_FRACTION);

            float peak = isBig ? BIG_PEAK_SCALE : STANDARD_PEAK_SCALE;
            float punch;
            if (normalisedTime < GROW_FRACTION)
            {
                punch = Mathf.Lerp(0.55f, peak, EaseOutCubic(normalisedTime / GROW_FRACTION));
            }
            else if (normalisedTime < GROW_FRACTION + SETTLE_FRACTION)
            {
                punch = Mathf.Lerp(peak, 1f, (normalisedTime - GROW_FRACTION) / SETTLE_FRACTION);
            }
            else
            {
                punch = 1f;
            }

            // The initial grow/settle punch plays out as before; layered on top, the popup shrinks
            // across its whole flight as it nears the score card, reusing BoardView's
            // PlayFlyToCornerFadeAsync idiom (#331) of shrinking a UI element as it approaches a target.
            float flightShrink = Mathf.Lerp(1f, FLIGHT_MIN_SCALE, EaseOutCubic(normalisedTime));
            float scale = punch * flightShrink;
            _textRect.localScale = new Vector3(scale, scale, 1f);

            float travel = EaseOutCubic(normalisedTime);
            _centreRect.anchoredPosition = Vector2.LerpUnclamped(_flightStart, _flightTarget, travel);

            Color fill = Color.white;
            if (isBig)
            {
                // Extra flourish for the loud tier: the label pulses towards a palette colour.
                float flash = Mathf.Abs(Mathf.Sin(normalisedTime * BIG_FLASH_CYCLES * Mathf.PI));
                fill = Color.Lerp(Color.white, _currentTheme.GetFill(FLASH_COLOUR_ID), flash * 0.8f);
            }

            fill.a = fade;
            _text.color = fill;

            Color outline = TextOutlineColor;
            outline.a = fade;
            _textOutline.effectColor = outline;
        }

        private void Hide()
        {
            _textRect.localScale = Vector3.one;
            _centreRect.anchoredPosition = _anchoredPosition;
            _layerObject.SetActive(false);
        }

        private void BuildHierarchy()
        {
            var rootRect = (RectTransform)transform;
            Stretch(rootRect);

            // Own nested Canvas so the per-frame label changes rebuild only this canvas, not the shared
            // UICanvas that holds the board and the score.
            var layerObject = new GameObject("BonusLayer", typeof(RectTransform), typeof(Canvas));
            _layerObject = layerObject;
            var layerRect = (RectTransform)layerObject.transform;
            layerRect.SetParent(rootRect, false);
            Stretch(layerRect);

            var centreObject = new GameObject("BonusCentre", typeof(RectTransform));
            _centreRect = (RectTransform)centreObject.transform;
            _centreRect.SetParent(layerRect, false);
            _centreRect.anchorMin = new Vector2(0.5f, 0.5f);
            _centreRect.anchorMax = new Vector2(0.5f, 0.5f);
            _centreRect.pivot = new Vector2(0.5f, 0.5f);
            _centreRect.sizeDelta = Vector2.zero;
            _centreRect.anchoredPosition = _anchoredPosition;

            _text = UiTextFactory.Create(
                _centreRect, "BonusLabel", STANDARD_FONT_SIZE, FontStyle.Bold, Color.white);
            _textRect = (RectTransform)_text.transform;
            _textOutline = _text.gameObject.AddComponent<Outline>();
            _textOutline.useGraphicAlpha = false;
            _textOutline.effectColor = TextOutlineColor;
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
