using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay;
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
    /// The power-up inventory strip between the board and the tray: one icon per kind with the count
    /// the player holds. Binds to <see cref="PowerUpModel"/> and shows which kind is currently armed.
    /// <para>
    /// Like <see cref="SettingsButtonView"/> it only knows how to draw itself and whether a screen
    /// point is on one of its icons — the tap that arms or cancels is routed by
    /// <see cref="BoardInputView"/>, which is the single owner of pointer input in this scene. That is
    /// also what stops a tap on an icon from reaching the board underneath: the input View resolves
    /// the icon first and returns, so the two can never both claim the same press.
    /// </para>
    /// <para>
    /// An empty slot doubles as the earn entry point for its own kind — one fixed kind per slot, never
    /// a random or chosen one — so the slots are also the rewarded placements.
    /// </para>
    /// <para>
    /// A slot has two states, not three: held (a count) and empty (the earn offer). A kind still
    /// behind its level gate (see <see cref="PowerUpUnlockLevels"/>) is not drawn at all — no padlock,
    /// no reserved space — so the strip is only ever as wide as the kinds the player can actually use,
    /// and it reflows as kinds unlock. The level gate is a live subscription, so reaching a kind's
    /// level reveals it in the same run; the reveal is animated rather than snapped, because the whole
    /// strip shifts when it happens.
    /// </para>
    /// <para>
    /// Slot index and kind are bound in exactly one place, <see cref="SlotKinds"/>: every parallel
    /// array is sized from its length and every lookup goes through <see cref="SlotIndexOf"/>, so
    /// reordering or adding a kind cannot leave one array reading a different slot than its neighbour.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerUpInventoryView : MonoBehaviour
    {
        /// <summary>One slot per <see cref="PowerUpKind"/>, in this display order.</summary>
        private static readonly PowerUpKind[] SlotKinds =
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

        /// <summary>Derived from <see cref="SlotKinds"/> rather than written out, so the two can never
        /// disagree about how many slots there are.</summary>
        private static readonly int SlotCount = SlotKinds.Length;

        /// <summary>Alpha applied to a slot the player holds none of, so "empty" reads at a glance.</summary>
        private const float EMPTY_SLOT_ALPHA = 0.35f;

        /// <summary>Side of the count badge, as a fraction of the slot.</summary>
        private const float BADGE_SIZE = 0.36f;

        /// <summary>How far the badge is pushed past the plate's bottom-right corner. Sitting slightly
        /// outside the plate is what makes it read as a chip stuck onto the slot rather than as part of
        /// the icon.</summary>
        private const float BADGE_CORNER_OVERLAP = 4f;

        /// <summary>Fraction of the badge the count glyph may fill. The serialized font size is a
        /// preference, not a promise: the badge is small enough that an unclamped size would spill off
        /// the chip, and a number that overhangs its own badge reads as a glitch.</summary>
        private const float BADGE_FONT_FILL = 0.66f;

        /// <summary>
        /// Divides the rounded square's baked 16px corner radius when the sprite is sliced: the lower
        /// the multiplier, the larger the rendered radius. Tuned so the plate reads as a rounded pill
        /// rather than as a softened square at <see cref="_slotSize"/>.
        /// </summary>
        private const float PLATE_CORNER_MULTIPLIER = 1.7f;

        /// <summary>How long the strip takes to slide to its new width when a kind unlocks. Long enough
        /// to be read as a reveal, short enough not to delay the tap that follows it.</summary>
        private const float REFLOW_DURATION_SECONDS = 0.22f;

        /// <summary>Alpha of the whole strip once the run is over and nothing can be armed.</summary>
        private const float RUN_OVER_ALPHA = 0.4f;

        /// <summary>Drawn in place of the count on a slot the player holds none of: that slot's tap is
        /// the "earn one" gesture, so it reads as an offer rather than as a dead icon showing 0.</summary>
        private const string EARN_AFFORDANCE_LABEL = "+";

        /// <summary>Side of a slot's icon glyph, as a fraction of the slot.</summary>
        private const float GLYPH_SIZE_FRACTION = 0.6f;

        /// <summary>Alpha of the Ghost Fit glyph, drawn semi-transparent so it reads as the ghost it is
        /// named after — kept from the placeholder era because it is still the right look for it.</summary>
        private const float GHOST_FIT_GLYPH_ALPHA = 0.55f;

        [Header("Layout")]
        [Tooltip("Strip centre in canvas space. Sits in the gap between the board card and the tray.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -460f);

        [SerializeField] private float _slotSize = 104f;

        [Tooltip("Preferred horizontal distance between neighbouring slot centres.")]
        [SerializeField] private float _slotSpacing = 180f;

        [Tooltip("Widest the strip may grow, in canvas units. Spacing is squeezed below the preferred "
            + "value rather than letting the strip run off the canvas as kinds are added.")]
        [SerializeField] private float _maxStripWidth = 1000f;

        [SerializeField] private int _countFontSize = 34;

        [Header("Icons")]
        [Tooltip("White-on-transparent glyphs, one per slot in display order (Bomb, Row Clear, Column Clear, "
            + "Joker, Colour Cleanser, Rotate, Reroll, Double Score, Ghost Fit) — the same sprites the shop "
            + "rows draw, so a power-up looks the same wherever it is met. Tinted at runtime.")]
        [SerializeField] private Sprite[] _slotIcons = new Sprite[SlotCount];

        [Tooltip("Extra scale applied to the armed slot, so the selection reads without any new art.")]
        [SerializeField] private float _armedScale = 1.12f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _countBuilder = new StringBuilder(8);

        private readonly RectTransform[] _slotRects = new RectTransform[SlotCount];
        private readonly Image[] _plateImages = new Image[SlotCount];
        private readonly Image[] _shadowImages = new Image[SlotCount];
        private readonly Image[] _glyphImages = new Image[SlotCount];
        private readonly Text[] _countTexts = new Text[SlotCount];
        private readonly int[] _counts = new int[SlotCount];

        /// <summary>The chip the count (or the earn offer's "+") is drawn on, overlapping the plate's
        /// bottom-right corner.</summary>
        private readonly Image[] _badgeImages = new Image[SlotCount];

        /// <summary>Where each slot sat when the current reflow started, so the animation can slide from
        /// there. Pre-allocated because a reflow runs per frame and must not allocate.</summary>
        private readonly float[] _reflowStartX = new float[SlotCount];

        /// <summary>Per slot: a reward request is in flight. Guards against a rapid double tap firing
        /// two concurrent requests and banking two power-ups for one watch.</summary>
        private readonly bool[] _isRequestingReward = new bool[SlotCount];

        private PowerUpModel _powerUpModel;
        private PowerUpSystem _powerUpSystem;
        private LevelProgressionModel _levelProgressionModel;
        private SettingsModel _settingsModel;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;

        private CanvasGroup _canvasGroup;
        private Canvas _canvas;
        private ThemeDefinition _currentTheme;
        private PowerUpKind? _armed;
        private bool _isRunOver;

        /// <summary>The player's progression frontier, mirrored from
        /// <see cref="LevelProgressionModel.CurrentLevelNumber"/> so a repaint never has to reach back
        /// through the model. Which slots exist at all is derived from this one number.</summary>
        private int _currentLevelNumber;

        /// <summary>How many leading slots are on screen. Everything the strip lays out — spacing, its
        /// own width, which slots a tap may hit — is measured from this rather than from
        /// <see cref="SlotCount"/>, so a locked kind costs no space.</summary>
        private int _visibleCount;

        /// <summary>Guards the very first level push (which seeds the opening paint) from animating: on
        /// the first paint there is no previous layout to slide away from.</summary>
        private bool _hasSeededLevel;

        /// <summary>The running reflow, cancelled when a newer one starts or the View goes away.</summary>
        private CancellationTokenSource _reflowCts;

        [Inject]
        public void Construct(
            PowerUpModel powerUpModel,
            PowerUpSystem powerUpSystem,
            LevelProgressionModel levelProgressionModel,
            SettingsModel settingsModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _powerUpModel = powerUpModel;
            _powerUpSystem = powerUpSystem;
            _levelProgressionModel = levelProgressionModel;
            _settingsModel = settingsModel;
            _runStartedSubscriber = runStartedSubscriber;
            _gameOverSubscriber = gameOverSubscriber;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            // Unity allows several CanvasGroups on one object and then picks between them opaquely, so
            // an existing one is adopted rather than shadowed by a second.
            if (!TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            BuildSlots();
        }

        private void Start()
        {
            if (_powerUpModel == null || _powerUpSystem == null || _levelProgressionModel == null
                || _settingsModel == null
                || _runStartedSubscriber == null || _gameOverSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpInventoryView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so _currentTheme is set before any slot is painted below.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Subscribed rather than read once: reaching a kind's unlock level must reveal its slot in
            // the run that got the player there, with no restart and no relaunch. ReactiveProperty
            // pushes the current value on subscribe, so this also seeds the opening paint.
            _levelProgressionModel.CurrentLevelNumber.Subscribe(OnLevelChanged).AddTo(_disposables);

            WatchCount(_powerUpModel.BombCount, PowerUpKind.Bomb);
            WatchCount(_powerUpModel.RowClearCount, PowerUpKind.RowClear);
            WatchCount(_powerUpModel.ColumnClearCount, PowerUpKind.ColumnClear);
            WatchCount(_powerUpModel.JokerCount, PowerUpKind.Joker);
            WatchCount(_powerUpModel.ColorCleanserCount, PowerUpKind.ColorCleanser);
            WatchCount(_powerUpModel.RotateCount, PowerUpKind.Rotate);
            WatchCount(_powerUpModel.RerollCount, PowerUpKind.Reroll);
            WatchCount(_powerUpModel.DoubleMultiplierCount, PowerUpKind.DoubleMultiplier);
            WatchCount(_powerUpModel.GhostFitCount, PowerUpKind.GhostFit);
            _powerUpModel.Armed.Subscribe(OnArmedChanged).AddTo(_disposables);

            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(_disposables);
        }

        private void OnDestroy()
        {
            CancelReflow();
            _disposables.Dispose();
        }

        /// <summary>
        /// Routes a tap that landed on one of the icons: arms that kind, cancels it when it is already
        /// the armed one, or — on a slot the player holds none of — asks for one to be earned. Returns
        /// false when the point is on no icon, so <see cref="BoardInputView"/> can carry on down its
        /// gate chain. Only the slots currently on screen are considered: a kind behind its level gate
        /// is not drawn, so there is no point on the canvas that could resolve to it.
        /// <para>
        /// <see cref="PowerUpKind.Reroll"/> and <see cref="PowerUpKind.DoubleMultiplier"/> are the
        /// exceptions to the arm-then-aim flow: with no target to aim at there is no second tap to wait
        /// for, so a tap on one the player holds applies it there and then.
        /// </para>
        /// </summary>
        internal bool TryHandleTap(Vector2 screenPosition)
        {
            if (_isRunOver || _powerUpSystem == null)
            {
                return false;
            }

            for (int slotIndex = 0; slotIndex < _visibleCount; slotIndex++)
            {
                if (!ContainsScreenPoint(slotIndex, screenPosition))
                {
                    continue;
                }

                PowerUpKind kind = SlotKinds[slotIndex];
                if (kind == PowerUpKind.Reroll && _counts[slotIndex] > 0)
                {
                    // The kinds with no target: there is nothing to aim at, so the tap that would arm
                    // any other kind applies this one outright. The System refuses to arm either of
                    // them at all, so these branches are their only way in.
                    _powerUpSystem.TryApplyReroll();
                }
                else if (kind == PowerUpKind.DoubleMultiplier && _counts[slotIndex] > 0)
                {
                    _powerUpSystem.TryApplyDoubleMultiplier();
                }
                else if (kind == PowerUpKind.GhostFit && _counts[slotIndex] > 0)
                {
                    // Targetless like the two above. The System also reads a second tap here as the
                    // gesture that takes an existing suggestion back down, which is why this branch
                    // hands every tap on the icon straight to it rather than gating on anything.
                    _powerUpSystem.TryApplyGhostFit();
                }
                else if (_armed == kind)
                {
                    // Tapping the armed icon again is the cancel gesture; nothing is spent.
                    _powerUpSystem.CancelArm();
                }
                else if (_counts[slotIndex] <= 0)
                {
                    // Arming a kind the player holds none of is refused by the System, so this tap
                    // would otherwise be a dead press. It is instead the earn gesture, offered exactly
                    // where a player reaches for a power-up they do not have.
                    RequestReward(slotIndex, kind);
                }
                else
                {
                    _powerUpSystem.Arm(kind);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Fire-and-forget earn request for one fixed kind. The View banks nothing itself: the System
        /// increments and persists the inventory, and the count subscription already bound in
        /// <see cref="Start"/> repaints this slot from that. A grant never arms anything.
        /// </summary>
        private void RequestReward(int slotIndex, PowerUpKind kind)
        {
            if (_isRequestingReward[slotIndex])
            {
                return;
            }

            RequestRewardAsync(slotIndex, kind, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid RequestRewardAsync(
            int slotIndex, PowerUpKind kind, CancellationToken cancellationToken)
        {
            _isRequestingReward[slotIndex] = true;
            try
            {
                await _powerUpSystem.GrantRewardAsync(kind, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The View went away mid-request. Nothing to undo: the System only banks a reward it
                // was actually handed.
            }
            finally
            {
                _isRequestingReward[slotIndex] = false;
            }
        }

        private bool ContainsScreenPoint(int slotIndex, Vector2 screenPosition)
        {
            RectTransform slotRect = _slotRects[slotIndex];
            if (slotRect == null)
            {
                return false;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(slotRect, screenPosition, eventCamera);
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            RefreshAllSlots();
        }

        /// <summary>
        /// Binds one inventory counter to the slot that draws <paramref name="kind"/>. The slot index
        /// is resolved from <see cref="SlotKinds"/> at subscribe time rather than written at the call
        /// site, so a slot can be added or reordered without any chance of a counter repainting its
        /// neighbour.
        /// </summary>
        private void WatchCount(ReactiveProperty<int> counter, PowerUpKind kind)
        {
            int slotIndex = SlotIndexOf(kind);
            if (slotIndex < 0)
            {
                return;
            }

            counter.Subscribe(count => SetCount(slotIndex, count)).AddTo(_disposables);
        }

        /// <summary>The slot drawing <paramref name="kind"/>, or -1 when no slot does.</summary>
        private static int SlotIndexOf(PowerUpKind kind)
        {
            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                if (SlotKinds[slotIndex] == kind)
                {
                    return slotIndex;
                }
            }

            return -1;
        }

        private void SetCount(int slotIndex, int count)
        {
            _counts[slotIndex] = count;
            RefreshSlot(slotIndex);
        }

        /// <summary>
        /// How many leading slots the mirrored level number unlocks. A single count is enough rather
        /// than a flag per slot because <see cref="SlotKinds"/> is ordered by ascending unlock level,
        /// so the unlocked kinds are always a prefix of it — the loop still asks the gate table for
        /// every kind and stops at the first locked one rather than assuming that ordering silently.
        /// </summary>
        private int ResolveVisibleCount()
        {
            int visibleCount = 0;
            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                if (!PowerUpUnlockLevels.IsUnlockedAt(SlotKinds[slotIndex], _currentLevelNumber))
                {
                    break;
                }

                visibleCount++;
            }

            return visibleCount;
        }

        /// <summary>Every slot is repainted rather than only the one that just unlocked: which slots a
        /// level change affects is exactly what the gate table decides, and asking it nine times is
        /// cheaper than tracking it.</summary>
        private void OnLevelChanged(int currentLevelNumber)
        {
            _currentLevelNumber = currentLevelNumber;

            int visibleCount = ResolveVisibleCount();
            if (visibleCount != _visibleCount)
            {
                bool animate = _hasSeededLevel;
                _visibleCount = visibleCount;
                ApplyLayout(animate);
            }

            _hasSeededLevel = true;
            RefreshAllSlots();
        }

        private void OnArmedChanged(PowerUpKind? armed)
        {
            _armed = armed;
            RefreshAllSlots();
        }

        private void OnRunStarted(RunStartedMessage message) => SetRunOver(false);

        private void OnGameOver(GameOverMessage message) => SetRunOver(true);

        /// <summary>
        /// Power-ups are not offered once the run is over. The strip is dimmed and made non-interactive
        /// rather than deactivated: toggling the GameObject would rebuild the shared canvas mesh every
        /// time a run ends or starts.
        /// </summary>
        private void SetRunOver(bool isRunOver)
        {
            _isRunOver = isRunOver;
            _canvasGroup.alpha = isRunOver ? RUN_OVER_ALPHA : 1f;
            _canvasGroup.interactable = !isRunOver;
            _canvasGroup.blocksRaycasts = !isRunOver;
        }

        private void RefreshAllSlots()
        {
            for (int slotIndex = 0; slotIndex < _visibleCount; slotIndex++)
            {
                RefreshSlot(slotIndex);
            }
        }

        /// <summary>
        /// Repaints one slot from the three inputs that can change how it looks: the theme, the count
        /// held, and whether this kind is the armed one. A kind behind its level gate never reaches
        /// here — it has no slot on screen at all — so there are two states, not three: "holds some"
        /// (the count) and "holds none" (the earn affordance).
        /// </summary>
        private void RefreshSlot(int slotIndex)
        {
            if (_currentTheme == null || _plateImages[slotIndex] == null)
            {
                return;
            }

            bool isArmed = _armed == SlotKinds[slotIndex];
            bool isAvailable = _counts[slotIndex] > 0;
            float alpha = isAvailable ? 1f : EMPTY_SLOT_ALPHA;

            // The armed slot inverts: the accent fills the plate and the glyph is punched out of it in
            // the plate's usual colour, which reads as "selected" without needing a second sprite.
            Color plateColour = isArmed ? _currentTheme.Accent : _currentTheme.CardBackground;
            Color glyphColour = isArmed ? _currentTheme.CardBackground : _currentTheme.Ink;

            // Ghost Fit's glyph is the one drawn see-through; every other kind's takes the slot's own
            // alpha unchanged.
            float glyphAlpha = SlotKinds[slotIndex] == PowerUpKind.GhostFit
                ? alpha * GHOST_FIT_GLYPH_ALPHA
                : alpha;

            _plateImages[slotIndex].color = WithAlpha(plateColour, alpha);
            _shadowImages[slotIndex].color = WithAlpha(_currentTheme.CardShadow, alpha);
            _glyphImages[slotIndex].color = WithAlpha(glyphColour, glyphAlpha);

            // The badge is drawn at full strength over an otherwise dimmed empty slot, and is left out
            // of the armed state's colour swap entirely: it is the one part of the slot that states a
            // number, and a number has to be legible in every state the plate can take.
            // Two tones, so the offer is never mistaken for a stock of one: the accent means "you hold
            // this many", the softer ink means "tap to earn one".
            _badgeImages[slotIndex].color = isAvailable ? _currentTheme.Accent : _currentTheme.SoftInk;
            _countTexts[slotIndex].color = _currentTheme.CardBackground;

            float scale = isArmed ? _armedScale : 1f;
            _slotRects[slotIndex].localScale = new Vector3(scale, scale, 1f);

            _countBuilder.Clear();
            if (isAvailable)
            {
                _countBuilder.Append(_counts[slotIndex]);
            }
            else
            {
                _countBuilder.Append(EARN_AFFORDANCE_LABEL);
            }

            _countTexts[slotIndex].text = _countBuilder.ToString();
        }

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        /// <summary>
        /// Builds every slot up front, including the ones still behind their level gate, and then hands
        /// the arrangement to <see cref="ApplyLayout"/>. Hidden slots are deactivated rather than never
        /// created: unlocking mid-run then costs one SetActive instead of building a GameObject, and a
        /// deactivated slot reserves no space and can catch no tap.
        /// </summary>
        private void BuildSlots()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = _anchoredPosition;

            // Every size here is in canvas reference units, so the strip owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            rect.localScale = Vector3.one;

            // The widest the strip can ever be, so the shadow padding a slot is built with does not
            // have to be revised every time the strip reflows.
            float spacing = SpacingFor(SlotCount);

            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                BuildSlot(rect, slotIndex, spacing);
            }

            _visibleCount = ResolveVisibleCount();
            ApplyLayout(animate: false);
        }

        /// <summary>
        /// The spacing the strip is actually laid out with: the preferred value, squeezed just enough
        /// to keep the whole strip inside <see cref="_maxStripWidth"/>. Measured from the slots on
        /// screen rather than from <see cref="SlotCount"/>, so the strip only pays the squeeze once the
        /// kinds that need it have actually unlocked.
        /// </summary>
        private float ResolveSlotSpacing() => SpacingFor(_visibleCount);

        private float SpacingFor(int slotCount)
        {
            if (slotCount <= 1)
            {
                return _slotSpacing;
            }

            float maxSpacing = (_maxStripWidth - _slotSize) / (slotCount - 1);
            return _slotSpacing <= maxSpacing ? _slotSpacing : maxSpacing;
        }

        /// <summary>
        /// Places the visible slots and sizes the strip around them. A growth is slid rather than
        /// snapped: every slot already on screen shifts when one is revealed, and a whole strip jumping
        /// sideways under the player's thumb reads as a glitch rather than as a reward.
        /// </summary>
        private void ApplyLayout(bool animate)
        {
            CancelReflow();

            float spacing = ResolveSlotSpacing();
            float originX = -spacing * ((_visibleCount - 1) * 0.5f);
            float width = _visibleCount > 0 ? (spacing * (_visibleCount - 1)) + _slotSize : 0f;
            var rect = (RectTransform)transform;

            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                RectTransform slotRect = _slotRects[slotIndex];
                if (slotRect == null)
                {
                    continue;
                }

                bool isVisible = slotIndex < _visibleCount;
                _reflowStartX[slotIndex] = slotRect.gameObject.activeSelf
                    ? slotRect.anchoredPosition.x
                    : originX + (slotIndex * spacing);
                slotRect.gameObject.SetActive(isVisible);
            }

            if (!animate)
            {
                rect.sizeDelta = new Vector2(width, _slotSize);
                for (int slotIndex = 0; slotIndex < _visibleCount; slotIndex++)
                {
                    _slotRects[slotIndex].anchoredPosition = new Vector2(originX + (slotIndex * spacing), 0f);
                }

                return;
            }

            _reflowCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            ReflowAsync(rect, originX, spacing, width, _reflowCts.Token).Forget();
        }

        private async UniTaskVoid ReflowAsync(
            RectTransform rect, float originX, float spacing, float width, CancellationToken cancellationToken)
        {
            float startWidth = rect.sizeDelta.x;
            int animatedCount = _visibleCount;

            try
            {
                float elapsed = 0f;
                while (elapsed < REFLOW_DURATION_SECONDS)
                {
                    // Unscaled: the strip must still reveal itself while the run is paused behind a
                    // level-up panel, which is exactly when a kind unlocks.
                    float progress = EaseOutCubic(elapsed / REFLOW_DURATION_SECONDS);
                    ApplyReflowFrame(rect, originX, spacing, width, startWidth, animatedCount, progress);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    elapsed += Time.unscaledDeltaTime;
                }

                ApplyReflowFrame(rect, originX, spacing, width, startWidth, animatedCount, 1f);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer reflow, which has already taken over the final positions, or
                // the View went away.
            }
        }

        private void ApplyReflowFrame(
            RectTransform rect,
            float originX,
            float spacing,
            float width,
            float startWidth,
            int animatedCount,
            float progress)
        {
            rect.sizeDelta = new Vector2(Mathf.Lerp(startWidth, width, progress), _slotSize);

            for (int slotIndex = 0; slotIndex < animatedCount; slotIndex++)
            {
                RectTransform slotRect = _slotRects[slotIndex];
                if (slotRect == null)
                {
                    continue;
                }

                float targetX = originX + (slotIndex * spacing);
                slotRect.anchoredPosition = new Vector2(
                    Mathf.Lerp(_reflowStartX[slotIndex], targetX, progress), 0f);
            }
        }

        private void CancelReflow()
        {
            if (_reflowCts == null)
            {
                return;
            }

            _reflowCts.Cancel();
            _reflowCts.Dispose();
            _reflowCts = null;
        }

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        private void BuildSlot(RectTransform parent, int slotIndex, float spacing)
        {
            var slotObject = new GameObject($"PowerUpSlot_{SlotKinds[slotIndex]}", typeof(RectTransform));
            var slotRect = (RectTransform)slotObject.transform;
            slotRect.SetParent(parent, false);
            Centre(slotRect, new Vector2(_slotSize, _slotSize));
            _slotRects[slotIndex] = slotRect;

            // The shadow reads as a drop shadow by being larger than the plate it sits behind — but at
            // a narrow squeeze (ResolveSlotSpacing), the preferred +10f padding can make two neighbours'
            // shadows overlap. Clamped to leave at least 4px of daylight between adjacent shadows, so
            // the strip never has to choose between "no shadow" and "shadows collide" as slots are added.
            float shadowPadding = Mathf.Max(0f, Mathf.Min(10f, spacing - _slotSize - 4f));
            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(slotRect, false);
            Centre(shadowRect, new Vector2(_slotSize + shadowPadding, _slotSize + shadowPadding));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImages[slotIndex] = ConfigurePlate(shadowObject.GetComponent<Image>());

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(slotRect, false);
            Centre(plateRect, new Vector2(_slotSize, _slotSize));
            _plateImages[slotIndex] = ConfigurePlate(plateObject.GetComponent<Image>());

            _glyphImages[slotIndex] = BuildGlyph(plateRect, SlotKinds[slotIndex]);

            // The badge is a sibling of the plate rather than a child of it, and built after it, so it
            // draws over both the plate and the glyph without inheriting the plate's colour.
            float badgeSide = _slotSize * BADGE_SIZE;
            var badgeObject = new GameObject("CountBadge", typeof(RectTransform), typeof(Image));
            var badgeRect = (RectTransform)badgeObject.transform;
            badgeRect.SetParent(slotRect, false);
            badgeRect.anchorMin = new Vector2(1f, 0f);
            badgeRect.anchorMax = new Vector2(1f, 0f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(badgeSide, badgeSide);
            badgeRect.anchoredPosition = new Vector2(BADGE_CORNER_OVERLAP, -BADGE_CORNER_OVERLAP);
            _badgeImages[slotIndex] = ConfigureCircle(badgeObject.GetComponent<Image>());

            // Counts are built here, before the theme or the inventory is known; the subscriptions in
            // Start fill in both.
            int fontSize = Mathf.Min(_countFontSize, Mathf.RoundToInt(badgeSide * BADGE_FONT_FILL));
            Text countText = UiTextFactory.Create(
                badgeRect, "Count", fontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)countText.transform).sizeDelta = new Vector2(badgeSide, badgeSide);
            _countTexts[slotIndex] = countText;
        }

        /// <summary>
        /// One tinted sprite per kind from <see cref="_slotIcons"/> — the same icons the shop rows draw,
        /// so the thing the player bought is the thing they see in the strip. The sprites live in the
        /// hub icon atlas, so the strip still batches with the rest of the UI. The placeholder
        /// silhouettes assembled from the rounded square and the circle are gone with this.
        /// </summary>
        private Image BuildGlyph(RectTransform parent, PowerUpKind kind)
        {
            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(parent, false);
            float side = _slotSize * GLYPH_SIZE_FRACTION;
            Centre(glyphRect, new Vector2(side, side));

            var glyphImage = glyphObject.GetComponent<Image>();
            glyphImage.sprite = IconFor(kind);
            glyphImage.type = Image.Type.Simple;
            glyphImage.preserveAspect = true;
            glyphImage.color = Color.clear;
            glyphImage.raycastTarget = false;
            return glyphImage;
        }

        /// <summary>The icon for <paramref name="kind"/>, authored in <see cref="SlotKinds"/> order.</summary>
        private Sprite IconFor(PowerUpKind kind)
        {
            int slotIndex = SlotIndexOf(kind);
            Sprite icon = _slotIcons != null && slotIndex >= 0 && slotIndex < _slotIcons.Length
                ? _slotIcons[slotIndex]
                : null;
            if (icon == null)
            {
                Debug.LogError($"{nameof(PowerUpInventoryView)} has no icon sprite assigned for {kind}.", this);
            }

            return icon;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not
        // through an EventSystem, and this scene has none.
        private static Image ConfigurePlate(Image image)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = PLATE_CORNER_MULTIPLIER;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        // The circle sprite has no border, so it must never be sliced.
        private static Image ConfigureCircle(Image image)
        {
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }
    }
}
