using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Systems;
using Mtafasahin.Reactive;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Flies a large copy of a freshly won icon from the centre of the screen down to its destination —
    /// a granted power-up's inventory slot on <see cref="PowerUpGrantedMessage"/>, or a newly spawned
    /// special board cell's own icon spot on <see cref="SpecialCellSpawnedMessage"/> — while
    /// <see cref="PowerUpInventoryView"/>/<see cref="BoardView"/> is on screen (issue #353). Mirrors
    /// <see cref="BonusFeedbackView"/>'s flight idiom: ease-out cubic, shrinking as it approaches, its
    /// own nested Canvas so the per-frame move never rebuilds the shared UICanvas.
    /// <para>
    /// A grant with no resolvable inventory slot (a kind the strip does not draw, e.g.
    /// <see cref="PowerUpKind.Hold"/>, or a scope with no <see cref="PowerUpInventoryView"/> at all —
    /// the level-up reward path, out of scope for this issue) takes no action beyond publishing
    /// <see cref="PowerUpGrantAnimationCompletedMessage"/> immediately, so nothing waiting on that
    /// signal (see <c>InfoPopupSystem</c>) can ever hang. The same is true of a kind with no icon
    /// configured below. A special-cell spawn with no resolvable board position (board not laid out,
    /// or a scope with no <see cref="BoardView"/>) simply takes no action — nothing awaits its
    /// completion, unlike a power-up grant's.
    /// </para>
    /// <para>
    /// A quantity-purchase fires one <see cref="PowerUpGrantedMessage"/> per unit, synchronously, in
    /// the same frame; likewise a cascade can spawn more than one special cell in one resolution. Each
    /// is queued and played as its own full flight, one at a time, rather than collapsed or played
    /// concurrently — see <see cref="ProcessQueueAsync"/>.
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
            PowerUpKind.PaintCross,
        };

        /// <summary>Lower bound on the flight's opening scale, as a multiple of the slot's resting
        /// size (issue #353's decision). Configurable per <see cref="_startScaleMultiplier"/> below,
        /// clamped up to this floor so a hastily-tuned value can never fall under the approved minimum.</summary>
        private const float MIN_START_SCALE_MULTIPLIER = 2f;

        /// <summary>
        /// How far above its parent canvas the fly-in layer sorts. A level's reward is granted in the
        /// same frame the level-complete card opens and moves itself to the top of the sibling order,
        /// so sibling order alone would put the "YOU WON!" caption behind that card (issue #464).
        /// </summary>
        private const int LAYER_SORT_OFFSET = 50;

        // The "YOU WON! · +1 Row Clear" caption an earned grant holds at the centre before flying
        // (issue #464): its plate, text sizes and how the icon pops in and the caption fades out.
        private const float CAPTION_PLATE_WIDTH = 560f;
        private const float CAPTION_PLATE_PADDING = 150f;
        private const float CAPTION_PLATE_RADIUS = 56f;
        private const float CAPTION_TITLE_GAP = 56f;
        private const float CAPTION_NAME_GAP = 48f;
        private const int CAPTION_TITLE_FONT_SIZE = 64;
        private const int CAPTION_NAME_FONT_SIZE = 40;
        private const float POP_IN_SECONDS = 0.25f;
        private const float POP_IN_START_SCALE = 0.6f;
        private const float CAPTION_FADE_OUT_FRACTION = 0.35f;
        private const string PLUS_ONE = "+1 ";
        private static readonly Color CaptionPlate = new Color(0.12f, 0.1f, 0.2f, 0.88f);
        private static readonly Color CaptionTitleInk = new Color32(0xFF, 0xD2, 0x4A, 0xFF);
        private static readonly Color CaptionShadow = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Vector2 CaptionShadowOffset = new Vector2(0f, -4f);

        [Header("Icons")]
        [Tooltip("Hero-sized icon per kind, in the same order as the power-up strip (Bomb, Row Clear, "
            + "Column Clear, Joker, Colour Cleanser, Rotate, Reroll, Double Score, Ghost Fit, Paint Cross). A kind "
            + "granted with no entry here skips its flight and logs an editor warning.")]
        [SerializeField] private Sprite[] _icons = new Sprite[IconKinds.Length];

        [Header("Timing")]
        [Tooltip("Starting scale as a multiple of the slot's resting size. Floored at 2x.")]
        [SerializeField] private float _startScaleMultiplier = MIN_START_SCALE_MULTIPLIER;

        [Tooltip("Seconds the flight from centre screen to its destination takes.")]
        [SerializeField] private float _flightDuration = 0.5f;

        [Tooltip("Seconds an earned power-up holds at the centre with its \"YOU WON!\" caption before "
            + "flying. Purchases skip the hold.")]
        [SerializeField] private float _holdDuration = 0.8f;

        [Header("Caption")]
        [Tooltip("Display face (Bowlby One SC) for the \"YOU WON!\" title.")]
        [SerializeField] private Font _titleFont;
        [Tooltip("Body face for the \"+1 Row Clear\" line.")]
        [SerializeField] private Font _bodyFont;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly Queue<PendingFlight> _pendingGrants = new Queue<PendingFlight>();

        private ISubscriber<PowerUpGrantedMessage> _powerUpGrantedSubscriber;
        private ISubscriber<SpecialCellSpawnedMessage> _specialCellSpawnedSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private IPublisher<PowerUpGrantAnimationCompletedMessage> _grantAnimationCompletedPublisher;
        private ISubscriber<PendingFlightsDrainMessage> _pendingFlightsDrainSubscriber;
        private IPublisher<SpecialCellFlightCompletedMessage> _specialCellFlightCompletedPublisher;
        private IPublisher<PendingFlightsDrainedMessage> _pendingFlightsDrainedPublisher;
        private PowerUpInventoryView _powerUpInventoryView;
        private BoardView _boardView;
        private BoardSystem _boardSystem;
        private LocalizationSystem _localizationSystem;

        /// <summary>One queued flight — either a granted power-up (flies to its inventory slot, and
        /// publishes <see cref="PowerUpGrantAnimationCompletedMessage"/> on arrival) or a newly spawned
        /// special board cell (flies to its own board icon spot, publishes nothing). A single queue so
        /// the two sources never play concurrently — see the type's own doc comment.</summary>
        private readonly struct PendingFlight
        {
            private readonly bool _isPowerUp;
            private readonly PowerUpGrantSource _source;
            private readonly PowerUpKind _powerUpKind;
            private readonly SpecialCellKind _specialCellKind;
            private readonly GridPosition _position;

            private PendingFlight(
                bool isPowerUp, PowerUpGrantSource source, PowerUpKind powerUpKind, SpecialCellKind specialCellKind, GridPosition position)
            {
                _isPowerUp = isPowerUp;
                _source = source;
                _powerUpKind = powerUpKind;
                _specialCellKind = specialCellKind;
                _position = position;
            }

            public static PendingFlight ForPowerUp(PowerUpKind kind, PowerUpGrantSource source)
                => new PendingFlight(true, source, kind, default, default);

            public static PendingFlight ForSpecialCell(SpecialCellKind kind, GridPosition position)
                => new PendingFlight(false, PowerUpGrantSource.Reward, default, kind, position);

            public bool IsPowerUp => _isPowerUp;

            /// <summary>An earned power-up (not bought, not a special cell): the one flight that holds
            /// at the centre with the "YOU WON!" caption.</summary>
            public bool ShowsWonCaption => _isPowerUp && _source != PowerUpGrantSource.Purchase;

            public PowerUpGrantSource Source => _source;
            public PowerUpKind PowerUpKindValue => _powerUpKind;
            public SpecialCellKind SpecialCellKindValue => _specialCellKind;
            public GridPosition Position => _position;
        }

        private Canvas _canvas;
        private GameObject _layerObject;
        private RectTransform _iconRect;
        private Image _iconImage;
        private CanvasGroup _captionGroup;
        private RectTransform _captionPlateRect;
        private Text _captionTitleText;
        private Text _captionNameText;

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
            ISubscriber<SpecialCellSpawnedMessage> specialCellSpawnedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            IPublisher<PowerUpGrantAnimationCompletedMessage> grantAnimationCompletedPublisher,
            PowerUpInventoryView powerUpInventoryView,
            BoardView boardView,
            BoardSystem boardSystem,
            LocalizationSystem localizationSystem,
            ISubscriber<PendingFlightsDrainMessage> pendingFlightsDrainSubscriber,
            IPublisher<PendingFlightsDrainedMessage> pendingFlightsDrainedPublisher,
            IPublisher<SpecialCellFlightCompletedMessage> specialCellFlightCompletedPublisher)
        {
            _specialCellFlightCompletedPublisher = specialCellFlightCompletedPublisher;
            _pendingFlightsDrainSubscriber = pendingFlightsDrainSubscriber;
            _pendingFlightsDrainedPublisher = pendingFlightsDrainedPublisher;
            _localizationSystem = localizationSystem;
            _powerUpGrantedSubscriber = powerUpGrantedSubscriber;
            _specialCellSpawnedSubscriber = specialCellSpawnedSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
            _grantAnimationCompletedPublisher = grantAnimationCompletedPublisher;
            _powerUpInventoryView = powerUpInventoryView;
            _boardView = boardView;
            _boardSystem = boardSystem;
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

            if (_specialCellSpawnedSubscriber != null)
            {
                _specialCellSpawnedSubscriber.Subscribe(OnSpecialCellSpawned).AddTo(_disposables);
            }

            if (_runStartedSubscriber != null)
            {
                _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
            }

            if (_pendingFlightsDrainSubscriber != null && _pendingFlightsDrainedPublisher != null)
            {
                _pendingFlightsDrainSubscriber.Subscribe(OnPendingFlightsDrain).AddTo(_disposables);
            }
        }

        /// <summary>A cleared level asks to be told once every flight has landed (see
        /// <see cref="PendingFlightsDrainMessage"/>).</summary>
        private void OnPendingFlightsDrain(PendingFlightsDrainMessage message) => ReplyWhenDrainedAsync().Forget();

        /// <summary>
        /// Waits one frame first — the level completes in the middle of the final placement, and that
        /// same placement's special-cell spawns are published after it — then until the queue has
        /// emptied, and replies. Replies on destruction too, though the system's own timeout would
        /// cover that.
        /// </summary>
        private async UniTaskVoid ReplyWhenDrainedAsync()
        {
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                while (_isProcessingQueue || _pendingGrants.Count > 0)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _pendingFlightsDrainedPublisher.Publish(new PendingFlightsDrainedMessage());
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
            Enqueue(PendingFlight.ForPowerUp(message.Kind, message.Source));
        }

        /// <summary>Issue #353 (expanded): a newly spawned special board cell — an Explosive Core,
        /// Vortex, Chain Lightning or Score Gem landing anywhere on the board — gets the same
        /// centre-screen-to-destination flight as a granted power-up, flying to its own board icon
        /// spot instead of an inventory slot. Unlike a power-up grant, nothing awaits this flight's
        /// completion, so a missing destination/icon simply takes no action (see
        /// <see cref="PlayGrantAsync"/>).
        /// <para>
        /// A spawn on the move that also ended the run gets no flight at all. <c>BoardSystem</c>
        /// publishes <c>PiecePlacedMessage</c> synchronously, so the level-complete/game-over path has
        /// already run — and <see cref="BoardSystem.IsGameOver"/> is already true — by the time the
        /// spawn messages for that same placement are raised. The result card is coming up this very
        /// frame, and a hero icon flying across it would simply collide with it. The cell itself is
        /// still spawned and still granted exactly as before; only its animation is skipped.
        /// </para></summary>
        private void OnSpecialCellSpawned(SpecialCellSpawnedMessage message)
        {
            if (_boardSystem != null && _boardSystem.IsGameOver)
            {
                PublishSpecialCellCompleted(message.Kind);
                return;
            }

            Enqueue(PendingFlight.ForSpecialCell(message.Kind, message.Position));
        }

        private void Enqueue(PendingFlight flight)
        {
            _pendingGrants.Enqueue(flight);

            if (!_isProcessingQueue)
            {
                ProcessQueueAsync().Forget();
            }
        }

        /// <summary>A fresh run wipes any grants still queued or in flight rather than letting a stale
        /// flight land after a restart. Every wiped power-up grant still publishes its completion
        /// immediately, so nothing left waiting on it (see <c>InfoPopupSystem</c>) is stranded; a
        /// wiped special-cell spawn publishes nothing, since nothing awaits it.</summary>
        private void OnRunStarted(RunStartedMessage message)
        {
            CancelCurrentFlight();

            while (_pendingGrants.Count > 0)
            {
                PendingFlight flight = _pendingGrants.Dequeue();
                if (flight.IsPowerUp)
                {
                    PublishCompleted(flight.PowerUpKindValue);
                }
                else
                {
                    PublishSpecialCellCompleted(flight.SpecialCellKindValue);
                }
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
                    PendingFlight flight = _pendingGrants.Dequeue();
                    await PlayGrantAsync(flight);
                }
            }
            finally
            {
                _isProcessingQueue = false;
            }
        }

        private async UniTask PlayGrantAsync(PendingFlight flight)
        {
            RectTransform destination = flight.IsPowerUp
                ? (_powerUpInventoryView != null ? _powerUpInventoryView.GetSlotRectTransform(flight.PowerUpKindValue) : null)
                : (_boardView != null ? _boardView.GetCellIconRectTransform(flight.Position) : null);

            // No destination to fly into — a scope with no inventory view/board view (out of scope for
            // #353), or a power-up kind the strip does not draw at all (e.g. Hold, granted only via the
            // ad-reward path). No action is taken, per the issue's negative acceptance criterion. A
            // power-up grant still publishes its completion so nothing awaiting it can hang; a
            // special-cell spawn has nothing awaiting it, so it simply does nothing.
            if (destination == null)
            {
                PublishFlightCompleted(flight);
                return;
            }

            Sprite icon = flight.IsPowerUp ? IconFor(flight.PowerUpKindValue) : _boardView.IconSprite(flight.SpecialCellKindValue);
            if (icon == null)
            {
                if (flight.IsPowerUp)
                {
                    LogMissingIconWarning(flight.PowerUpKindValue);
                }

                PublishFlightCompleted(flight);
                return;
            }

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
            CancellationToken token = _runCts.Token;

            Setup(icon, destination);
            bool showsCaption = flight.ShowsWonCaption && _localizationSystem != null;
            SetupCaption(showsCaption, flight.PowerUpKindValue, flight.Source);

            try
            {
                // An earned power-up first holds at the centre, popping in under its caption, so the
                // player reads what they won before it leaves for the strip (issue #464).
                if (showsCaption)
                {
                    float holdDuration = Mathf.Max(0.01f, _holdDuration);
                    float held = 0f;
                    while (held < holdDuration)
                    {
                        AnimateHold(held);
                        await UniTask.Yield(PlayerLoopTiming.Update, token);
                        held += Time.unscaledDeltaTime;
                    }

                    AnimateHold(holdDuration);
                }

                float flightDuration = Mathf.Max(0.01f, _flightDuration);
                float elapsed = 0f;
                while (elapsed < flightDuration)
                {
                    Animate(elapsed / flightDuration);
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

            PublishFlightCompleted(flight);
        }

        /// <summary>Announces that <paramref name="flight"/> has landed (or was skipped): the power-up
        /// completion for a grant, the special-cell completion for a spawned cell.</summary>
        private void PublishFlightCompleted(PendingFlight flight)
        {
            if (flight.IsPowerUp)
            {
                PublishCompleted(flight.PowerUpKindValue);
            }
            else
            {
                PublishSpecialCellCompleted(flight.SpecialCellKindValue);
            }
        }

        private void PublishSpecialCellCompleted(SpecialCellKind kind)
        {
            if (_isDestroyed || _specialCellFlightCompletedPublisher == null)
            {
                return;
            }

            _specialCellFlightCompletedPublisher.Publish(new SpecialCellFlightCompletedMessage(kind));
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

        /// <summary>
        /// Shows or hides the "YOU WON! · +1 Row Clear" caption for the flight about to play, sized
        /// around the icon's opening size. Hidden (and so skipped) for purchases and special cells.
        /// </summary>
        private void SetupCaption(bool show, PowerUpKind kind, PowerUpGrantSource source)
        {
            _captionGroup.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            _captionGroup.alpha = 0f;
            // A streak bonus says so, so it never reads as a second copy of the level's own reward.
            _captionTitleText.text = _localizationSystem.Translate(
                source == PowerUpGrantSource.StreakBonus ? LocalizationKeys.GRANT_STREAK_BONUS : LocalizationKeys.GRANT_WON);
            _captionNameText.text = PLUS_ONE + _localizationSystem.Translate(PowerUpShopView.NameKeyOf(kind));

            float iconHalf = _iconRect.sizeDelta.y * 0.5f;
            _captionTitleText.rectTransform.anchoredPosition = new Vector2(0f, iconHalf + CAPTION_TITLE_GAP);
            _captionNameText.rectTransform.anchoredPosition = new Vector2(0f, -iconHalf - CAPTION_NAME_GAP);
            _captionPlateRect.sizeDelta = new Vector2(
                Mathf.Max(CAPTION_PLATE_WIDTH, _iconRect.sizeDelta.x + CAPTION_PLATE_PADDING),
                (iconHalf * 2f) + CAPTION_PLATE_PADDING + CAPTION_TITLE_GAP + CAPTION_NAME_GAP);
        }

        /// <summary>The hold before an earned flight: over its first <see cref="POP_IN_SECONDS"/> the
        /// icon pops in at the centre and the caption fades up, then both rest until the flight.</summary>
        private void AnimateHold(float heldSeconds)
        {
            float pop = Mathf.Clamp01(heldSeconds / POP_IN_SECONDS);
            float startScale = Mathf.Max(_startScaleMultiplier, MIN_START_SCALE_MULTIPLIER);
            float scale = Mathf.LerpUnclamped(POP_IN_START_SCALE, 1f, EaseOutCubic(pop));
            _iconRect.sizeDelta = _restingSize * (startScale * scale);
            _iconRect.anchoredPosition = _flightStart;
            _captionGroup.alpha = pop;
        }

        private void Animate(float normalisedTime)
        {
            float progress = EaseOutCubic(normalisedTime);

            // The caption clears out of the way as the icon leaves, so only the icon arrives.
            if (_captionGroup.gameObject.activeSelf)
            {
                _captionGroup.alpha = 1f - Mathf.Clamp01(normalisedTime / CAPTION_FADE_OUT_FRACTION);
            }

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

            // Sorted above every panel rather than by sibling order — see LAYER_SORT_OFFSET.
            var layerCanvas = layerObject.GetComponent<Canvas>();
            layerCanvas.overrideSorting = true;
            layerCanvas.sortingOrder = (_canvas != null ? _canvas.sortingOrder : 0) + LAYER_SORT_OFFSET;

            BuildCaption(layerRect);

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

        /// <summary>The caption behind the held icon: a dark rounded plate, the gold title above the icon
        /// and the "+1 name" line below it. Built behind the icon so the icon draws on top.</summary>
        private void BuildCaption(RectTransform layerRect)
        {
            var captionObject = new GameObject("WonCaption", typeof(RectTransform), typeof(CanvasGroup));
            var captionRect = (RectTransform)captionObject.transform;
            captionRect.SetParent(layerRect, false);
            captionRect.anchorMin = new Vector2(0.5f, 0.5f);
            captionRect.anchorMax = new Vector2(0.5f, 0.5f);
            captionRect.sizeDelta = Vector2.zero;
            _captionGroup = captionObject.GetComponent<CanvasGroup>();
            _captionGroup.blocksRaycasts = false;
            _captionGroup.interactable = false;

            Image plate = HudChrome.BuildRounded(
                captionRect, "Plate", new Vector2(CAPTION_PLATE_WIDTH, CAPTION_PLATE_WIDTH), Vector2.zero, CAPTION_PLATE_RADIUS);
            plate.color = CaptionPlate;
            _captionPlateRect = plate.rectTransform;

            _captionTitleText = UiTextFactory.Create(
                captionRect, "Title", CAPTION_TITLE_FONT_SIZE, FontStyle.Normal, CaptionTitleInk, _titleFont);
            AddCaptionShadow(_captionTitleText);
            _captionNameText = UiTextFactory.Create(
                captionRect, "Name", CAPTION_NAME_FONT_SIZE, FontStyle.Bold, Color.white, _bodyFont);
            AddCaptionShadow(_captionNameText);

            captionObject.SetActive(false);
        }

        private static void AddCaptionShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = CaptionShadow;
            shadow.effectDistance = CaptionShadowOffset;
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
