using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Reactive;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Flies a large copy of a granted power-up's icon from the centre of the screen down into its
    /// inventory slot whenever a <see cref="PowerUpGrantedMessage"/> is published while
    /// <see cref="PowerUpInventoryView"/> is on screen (issue #353) — the rewarded-ad grant and the
    /// coin-shop purchase grant, both of which occur in this scene. Mirrors
    /// <see cref="BonusFeedbackView"/>'s flight idiom: ease-out cubic, shrinking as it approaches, its
    /// own nested Canvas so the per-frame move never rebuilds the shared UICanvas.
    /// <para>
    /// A grant with no resolvable inventory slot (a kind the strip does not draw, e.g.
    /// <see cref="PowerUpKind.Hold"/>, or a scope with no <see cref="PowerUpInventoryView"/> at all —
    /// the level-up reward path, out of scope for this issue) takes no action beyond publishing
    /// <see cref="PowerUpGrantAnimationCompletedMessage"/> immediately, so nothing waiting on that
    /// signal (see <c>InfoPopupSystem</c>) can ever hang. The same is true of a kind with no icon
    /// configured below.
    /// </para>
    /// <para>
    /// A quantity-purchase fires one <see cref="PowerUpGrantedMessage"/> per unit, synchronously, in
    /// the same frame. Each is queued and played as its own full flight, one at a time, rather than
    /// collapsed into a single "+N" icon — see <see cref="ProcessQueueAsync"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerUpGrantAnimationView : MonoBehaviour
    {
        /// <summary>Slot kinds this View knows an icon for, and the only kinds it can ever fly to,
        /// since these are the only ones <see cref="PowerUpInventoryView.GetSlotRectTransform"/>
        /// resolves. Declared here (rather than reusing <see cref="PowerUpInventoryView"/>'s private
        /// array) so this View owns its own hero-sized icon set, independent of the strip's small ones.</summary>
        private static readonly PowerUpKind[] IconKinds =
        {
            PowerUpKind.Bomb,
            PowerUpKind.RowClear,
            PowerUpKind.ColumnClear,
            PowerUpKind.Joker,
            PowerUpKind.ColorCleanser,
            PowerUpKind.Rotate,
            PowerUpKind.Reroll,
            PowerUpKind.DoubleMultiplier,
            PowerUpKind.GhostFit,
        };

        private const float FLIGHT_DURATION_SECONDS = 0.6f;

        /// <summary>Lower bound on the flight's opening scale, as a multiple of the slot's resting
        /// size (issue #353's decision). Configurable per <see cref="_startScaleMultiplier"/> below,
        /// clamped up to this floor so a hastily-tuned value can never fall under the approved minimum.</summary>
        private const float MIN_START_SCALE_MULTIPLIER = 2f;

        [Header("Icons")]
        [Tooltip("Hero-sized icon per kind, in the same order as the power-up strip (Bomb, Row Clear, "
            + "Column Clear, Joker, Colour Cleanser, Rotate, Reroll, Double Score, Ghost Fit). A kind "
            + "granted with no entry here skips its flight and logs an editor warning.")]
        [SerializeField] private Sprite[] _icons = new Sprite[IconKinds.Length];

        [Header("Timing")]
        [Tooltip("Starting scale as a multiple of the slot's resting size. Floored at 2x.")]
        [SerializeField] private float _startScaleMultiplier = MIN_START_SCALE_MULTIPLIER;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly Queue<PowerUpKind> _pendingGrants = new Queue<PowerUpKind>();

        private ISubscriber<PowerUpGrantedMessage> _powerUpGrantedSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private IPublisher<PowerUpGrantAnimationCompletedMessage> _grantAnimationCompletedPublisher;
        private PowerUpInventoryView _powerUpInventoryView;

        private Canvas _canvas;
        private GameObject _layerObject;
        private RectTransform _iconRect;
        private Image _iconImage;

        private Vector2 _flightStart;
        private Vector2 _flightTarget;
        private Vector2 _restingSize;

        private CancellationToken _destroyToken;
        private CancellationTokenSource _runCts;
        private bool _isProcessingQueue;
        private bool _isDestroyed;

        [Inject]
        public void Construct(
            ISubscriber<PowerUpGrantedMessage> powerUpGrantedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            IPublisher<PowerUpGrantAnimationCompletedMessage> grantAnimationCompletedPublisher,
            PowerUpInventoryView powerUpInventoryView)
        {
            _powerUpGrantedSubscriber = powerUpGrantedSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
            _grantAnimationCompletedPublisher = grantAnimationCompletedPublisher;
            _powerUpInventoryView = powerUpInventoryView;
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
            if (_powerUpGrantedSubscriber == null || _grantAnimationCompletedPublisher == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpGrantAnimationView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            _powerUpGrantedSubscriber.Subscribe(OnPowerUpGranted).AddTo(_disposables);

            if (_runStartedSubscriber != null)
            {
                _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
            }
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _disposables.Dispose();
            CancelCurrentFlight();
        }

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        private void OnPowerUpGranted(PowerUpGrantedMessage message)
        {
            _pendingGrants.Enqueue(message.Kind);

            if (!_isProcessingQueue)
            {
                ProcessQueueAsync().Forget();
            }
        }

        /// <summary>A fresh run wipes any grants still queued or in flight rather than letting a stale
        /// flight land after a restart. Every wiped grant still publishes its completion immediately,
        /// so nothing left waiting on it (see <c>InfoPopupSystem</c>) is stranded.</summary>
        private void OnRunStarted(RunStartedMessage message)
        {
            CancelCurrentFlight();

            while (_pendingGrants.Count > 0)
            {
                PublishCompleted(_pendingGrants.Dequeue());
            }

            Hide();
        }

        private void CancelCurrentFlight()
        {
            if (_runCts == null)
            {
                return;
            }

            _runCts.Cancel();
            _runCts.Dispose();
            _runCts = null;
        }

        private async UniTaskVoid ProcessQueueAsync()
        {
            _isProcessingQueue = true;

            try
            {
                while (_pendingGrants.Count > 0 && !_isDestroyed)
                {
                    PowerUpKind kind = _pendingGrants.Dequeue();
                    await PlayGrantAsync(kind);
                }
            }
            finally
            {
                _isProcessingQueue = false;
            }
        }

        private async UniTask PlayGrantAsync(PowerUpKind kind)
        {
            RectTransform slotRect = _powerUpInventoryView != null
                ? _powerUpInventoryView.GetSlotRectTransform(kind)
                : null;

            // No slot to fly into — a scope with no inventory view (out of scope for #353), or a kind
            // the strip does not draw at all (e.g. Hold, granted only via the ad-reward path). No
            // action is taken, per the issue's negative acceptance criterion.
            if (slotRect == null)
            {
                PublishCompleted(kind);
                return;
            }

            Sprite icon = IconFor(kind);
            if (icon == null)
            {
                LogMissingIconWarning(kind);
                PublishCompleted(kind);
                return;
            }

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
            CancellationToken token = _runCts.Token;

            Setup(icon, slotRect);

            try
            {
                float elapsed = 0f;
                while (elapsed < FLIGHT_DURATION_SECONDS)
                {
                    Animate(elapsed / FLIGHT_DURATION_SECONDS);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.unscaledDeltaTime;
                }

                Animate(1f);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a run restart or object destruction — OnRunStarted/OnDestroy already
                // published this grant's completion (or the System waiting on it was itself disposed),
                // so nothing more is published here.
                return;
            }
            finally
            {
                if (!_isDestroyed)
                {
                    Hide();
                }
            }

            PublishCompleted(kind);
        }

        private void PublishCompleted(PowerUpKind kind)
        {
            if (_isDestroyed)
            {
                return;
            }

            _grantAnimationCompletedPublisher.Publish(new PowerUpGrantAnimationCompletedMessage(kind));
        }

        private void Setup(Sprite icon, RectTransform slotRect)
        {
            _layerObject.SetActive(true);
            transform.SetAsLastSibling();

            _iconImage.sprite = icon;

            _flightStart = Vector2.zero;
            _flightTarget = ResolveFlightTarget(slotRect);
            _restingSize = Vector2.Scale(slotRect.rect.size, slotRect.localScale);

            float startScale = Mathf.Max(_startScaleMultiplier, MIN_START_SCALE_MULTIPLIER);
            _iconRect.sizeDelta = _restingSize * startScale;
            _iconRect.anchoredPosition = _flightStart;
        }

        /// <summary>
        /// <paramref name="slotRect"/>'s on-screen position converted into this View's own canvas-local
        /// space, mirroring <see cref="BonusFeedbackView.ResolveFlightTarget"/>. Falls back to centre
        /// screen — a zero-distance "flight" — if the conversion fails, so a broken reference degrades
        /// rather than throws.
        /// </summary>
        private Vector2 ResolveFlightTarget(RectTransform slotRect)
        {
            if (_canvas == null)
            {
                return _flightStart;
            }

            Camera eventCamera = _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, slotRect.position);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_iconRect.parent, screenPoint, eventCamera, out Vector2 localPoint))
            {
                return _flightStart;
            }

            return localPoint;
        }

        private void Animate(float normalisedTime)
        {
            float progress = EaseOutCubic(normalisedTime);

            _iconRect.anchoredPosition = Vector2.LerpUnclamped(_flightStart, _flightTarget, progress);

            float startScale = Mathf.Max(_startScaleMultiplier, MIN_START_SCALE_MULTIPLIER);
            Vector2 size = Vector2.LerpUnclamped(_restingSize * startScale, _restingSize, progress);
            _iconRect.sizeDelta = size;
        }

        private void Hide()
        {
            _layerObject.SetActive(false);
        }

        private void BuildHierarchy()
        {
            var rootRect = (RectTransform)transform;
            Stretch(rootRect);

            // Own nested Canvas so this flight's per-frame move/resize rebuilds only this canvas, not
            // the shared UICanvas holding the board, the score card and the power-up strip.
            var layerObject = new GameObject("PowerUpGrantLayer", typeof(RectTransform), typeof(Canvas));
            _layerObject = layerObject;
            var layerRect = (RectTransform)layerObject.transform;
            layerRect.SetParent(rootRect, false);
            Stretch(layerRect);

            var iconObject = new GameObject("GrantIcon", typeof(RectTransform), typeof(Image));
            _iconRect = (RectTransform)iconObject.transform;
            _iconRect.SetParent(layerRect, false);
            _iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            _iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            _iconRect.pivot = new Vector2(0.5f, 0.5f);
            _iconRect.anchoredPosition = Vector2.zero;

            _iconImage = iconObject.GetComponent<Image>();
            _iconImage.raycastTarget = false;
            _iconImage.preserveAspect = true;
        }

        /// <summary>The icon configured for <paramref name="kind"/>, or null when this kind has no
        /// entry (either it is not one of <see cref="IconKinds"/> at all, or its slot is unassigned).</summary>
        private Sprite IconFor(PowerUpKind kind)
        {
            for (int kindIndex = 0; kindIndex < IconKinds.Length; kindIndex++)
            {
                if (IconKinds[kindIndex] != kind)
                {
                    continue;
                }

                return _icons != null && kindIndex < _icons.Length ? _icons[kindIndex] : null;
            }

            return null;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogMissingIconWarning(PowerUpKind kind)
        {
            Debug.LogWarning(
                $"{nameof(PowerUpGrantAnimationView)} has no icon sprite assigned for {kind}; skipping its grant animation.",
                this);
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
