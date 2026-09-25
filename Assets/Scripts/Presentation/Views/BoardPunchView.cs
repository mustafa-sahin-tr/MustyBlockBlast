using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The board's line-clear punch (issue #367 AC5): a short scale kick on the board card itself —
    /// never the camera — small for one line and growing with the number of lines and the combo
    /// streak, always capped so the tray and the next drag stay precise.
    /// <para>
    /// Sits on the <see cref="BoardView"/>'s own GameObject and scales that transform, so the card,
    /// the well and every cell move together as one. BoardView never touches its own scale, so the
    /// two never fight.
    /// </para>
    /// <para>
    /// A newer clear supersedes an older punch (AC8): each punch takes a generation number and a
    /// running one stops as soon as it is no longer the newest, so rapid clears never stack. The
    /// player can switch the punch off from the settings card
    /// (<see cref="SettingsModel.BoardPunchEnabled"/>); switching it off mid-punch snaps the board
    /// back to rest.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardPunchView : MonoBehaviour
    {
        /// <summary>The maximum of sin(3πt)·(1−t)² on [0, 1], so <see cref="Curve"/> peaks at exactly 1.</summary>
        private static readonly float PeakOfUnnormalisedCurve = FindCurvePeak();

        [Tooltip("Extra scale a one-line clear on a fresh streak adds at the punch's peak (0.015 = 1.5%).")]
        [SerializeField] private float _baseAmplitude = 0.015f;

        [Tooltip("Extra peak scale for every line beyond the first cleared by the same placement.")]
        [SerializeField] private float _amplitudePerExtraLine = 0.008f;

        [Tooltip("Extra peak scale for every consecutive clearing placement beyond the first.")]
        [SerializeField] private float _amplitudePerStreakStep = 0.004f;

        [Tooltip("Hard cap on the peak's extra scale, however big the combo.")]
        [SerializeField] private float _maxAmplitude = 0.035f;

        [Tooltip("Length of the whole punch, in seconds of unscaled time.")]
        [SerializeField] private float _duration = 0.24f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private SettingsModel _settingsModel;
        private ScoreModel _scoreModel;
        private ISubscriber<LinesClearedMessage> _linesClearedSubscriber;

        private Transform _target;
        private CancellationToken _destroyToken;
        private int _generation;

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            ScoreModel scoreModel,
            ISubscriber<LinesClearedMessage> linesClearedSubscriber)
        {
            _settingsModel = settingsModel;
            _scoreModel = scoreModel;
            _linesClearedSubscriber = linesClearedSubscriber;
        }

        private void Awake()
        {
            _target = transform;
            _destroyToken = this.GetCancellationTokenOnDestroy();
        }

        private void Start()
        {
            if (_settingsModel == null || _scoreModel == null || _linesClearedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(BoardPunchView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _linesClearedSubscriber.Subscribe(OnLinesCleared).AddTo(_disposables);
            _settingsModel.BoardPunchEnabled.Subscribe(OnBoardPunchEnabledChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>
        /// The punch's peak extra scale for a clear of <paramref name="lineCount"/> lines on the
        /// <paramref name="streak"/>-th clearing placement in a row, capped at <paramref name="maxAmplitude"/>.
        /// </summary>
        internal static float Amplitude(
            int lineCount, int streak, float baseAmplitude, float perExtraLine, float perStreakStep, float maxAmplitude)
        {
            if (lineCount <= 0)
            {
                return 0f;
            }

            float amplitude = baseAmplitude
                + (perExtraLine * (lineCount - 1))
                + (perStreakStep * Mathf.Max(0, streak - 1));
            return Mathf.Clamp(amplitude, 0f, maxAmplitude);
        }

        /// <summary>
        /// The punch's shape over normalised time <paramref name="t"/>: a quick swell, a small
        /// undershoot and a smaller rebound, dying out to exactly 0 at t = 1. Its largest value is
        /// 1 — reached in the first swell — so the peak scale never exceeds the amplitude.
        /// </summary>
        internal static float Curve(float t)
        {
            if (t <= 0f || t >= 1f)
            {
                return 0f;
            }

            float decay = (1f - t) * (1f - t);
            return Mathf.Sin(t * Mathf.PI * 3f) * decay / PeakOfUnnormalisedCurve;
        }

        private static float FindCurvePeak()
        {
            const int SAMPLE_COUNT = 1000;
            float peak = 0f;
            for (int sampleIndex = 1; sampleIndex < SAMPLE_COUNT; sampleIndex++)
            {
                float t = sampleIndex / (float)SAMPLE_COUNT;
                float value = Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * (1f - t);
                peak = Mathf.Max(peak, value);
            }

            return peak;
        }

        private void OnLinesCleared(LinesClearedMessage message)
        {
            if (!_settingsModel.BoardPunchEnabled.Value)
            {
                return;
            }

            // The streak is already this placement's: ScoreSystem counts it off PiecePlacedMessage,
            // which BoardSystem publishes before this message.
            float amplitude = Amplitude(
                message.LineCount, _scoreModel.Streak.Value,
                _baseAmplitude, _amplitudePerExtraLine, _amplitudePerStreakStep, _maxAmplitude);
            if (amplitude <= 0f)
            {
                return;
            }

            _generation++;
            PlayPunchAsync(amplitude, _generation).Forget();
        }

        private void OnBoardPunchEnabledChanged(bool enabled)
        {
            if (enabled)
            {
                return;
            }

            // Stops any running punch at its next frame and puts the board back at rest now.
            _generation++;
            _target.localScale = Vector3.one;
        }

        private async UniTaskVoid PlayPunchAsync(float amplitude, int generation)
        {
            float duration = Mathf.Max(0.01f, _duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float scale = 1f + (amplitude * Curve(elapsed / duration));
                _target.localScale = new Vector3(scale, scale, 1f);

                bool cancelled = await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken).SuppressCancellationThrow();
                if (cancelled || generation != _generation)
                {
                    return;
                }

                elapsed += Time.unscaledDeltaTime;
            }

            _target.localScale = Vector3.one;
        }
    }
}
