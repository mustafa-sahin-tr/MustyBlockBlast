using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The power-up inventory strip between the board and the tray: one slot per unlocked kind with
    /// the count the player holds. Binds to <see cref="PowerUpModel"/> and shows which kind is
    /// currently armed.
    /// <para>
    /// Drawn in the storefront vocabulary (issue #265). A slot is a card-coloured plate over a dropped
    /// shadow with a kind-tinted icon plate on it and a count badge on its top-right corner, and it
    /// has three looks: held (a green "xN" badge), empty (the plate goes translucent inside an outline
    /// ring, the icon fades, and a pink "+" badge offers the shop), and armed (an accent ring around
    /// the plate). When more kinds are unlocked than fit at the preferred size, every slot shrinks so
    /// the strip never runs past the board's width — the mockup's 36px slots at nine kinds.
    /// </para>
    /// <para>
    /// Like <see cref="SettingsButtonView"/> it only knows how to draw itself and whether a screen
    /// point is on one of its icons — the tap that arms or cancels is routed by
    /// <see cref="BoardInputView"/>, which is the single owner of pointer input in this scene. That is
    /// also what stops a tap on an icon from reaching the board underneath: the input View resolves
    /// the icon first and returns, so the two can never both claim the same press.
    /// </para>
    /// <para>
    /// An empty slot doubles as the shop entry point: a tap on it asks <see cref="BoardInputView"/> to
    /// open the power-up shop, because a power-up is only ever bought with coins (issue #216).
    /// </para>
    /// <para>
    /// A kind still behind its level gate (see <see cref="PowerUpUnlockLevels"/>) is not drawn at all —
    /// no padlock, no reserved space — so the strip is only ever as wide as the kinds the player can
    /// actually use, and it reflows as kinds unlock. The level gate is a live subscription, so reaching
    /// a kind's level reveals it in the same run; the reveal is animated rather than snapped, because
    /// the whole strip shifts when it happens.
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
            PowerUpKind.PaintCross,
        };

        /// <summary>Derived from <see cref="SlotKinds"/> rather than written out, so the two can never
        /// disagree about how many slots there are.</summary>
        private static readonly int SlotCount = SlotKinds.Length;

        /// <summary>How many theme kinds the icon plates cycle through: one per block colour.</summary>
        private const int KIND_COUNT = 5;

        /// <summary>Alpha of the plate on a slot the player holds none of: the mockup's 0.55.</summary>
        private const float EMPTY_PLATE_ALPHA = 0.55f;

        /// <summary>Alpha of the icon plate on a slot the player holds none of: the mockup's 0.5.</summary>
        private const float EMPTY_ICON_ALPHA = 0.5f;

        /// <summary>Corner radius of the slot plate as a fraction of the slot: the mockup's 15px on 52.</summary>
        private const float PLATE_RADIUS_FRACTION = 0.29f;

        /// <summary>Side of the kind-tinted icon plate as a fraction of the slot: the mockup's 38px on 52.</summary>
        private const float ICON_PLATE_FRACTION = 0.73f;

        /// <summary>Corner radius of the icon plate as a fraction of its own side: the mockup's 11px on 38.</summary>
        private const float ICON_PLATE_RADIUS_FRACTION = 0.29f;

        /// <summary>The icon plate's bottom bevel as a fraction of the slot: the mockup's 3px on 52.</summary>
        private const float ICON_BEVEL_FRACTION = 0.06f;

        /// <summary>Side of a slot's glyph as a fraction of the slot: the mockup's 22px on 52.</summary>
        private const float GLYPH_SIZE_FRACTION = 0.42f;

        /// <summary>Diameter of the count badge as a fraction of the slot: the mockup's 22px on 52.</summary>
        private const float BADGE_SIZE_FRACTION = 0.42f;

        /// <summary>How far the badge's centre sits inside the plate's top-right corner, as a fraction
        /// of the slot: the mockup's 4px on 52. Most of the chip hangs outside the plate.</summary>
        private const float BADGE_INSET_FRACTION = 0.077f;

        /// <summary>Thickness of the empty slot's outline ring: the mockup's 2px.</summary>
        private const float EMPTY_RING_THICKNESS = 6f;

        /// <summary>Thickness of the armed slot's accent ring, and how far it stands off the plate:
        /// the mockup's 3px halo.</summary>
        private const float ARMED_RING_THICKNESS = 8f;

        /// <summary>Alpha of the Ghost Fit glyph, drawn semi-transparent so it reads as the ghost it is
        /// named after.</summary>
        private const float GHOST_FIT_GLYPH_ALPHA = 0.55f;

        /// <summary>How long the strip takes to slide to its new width when a kind unlocks. Long enough
        /// to be read as a reveal, short enough not to delay the tap that follows it.</summary>
        private const float REFLOW_DURATION_SECONDS = 0.22f;

        /// <summary>Alpha of the whole strip once the run is over and nothing can be armed.</summary>
        private const float RUN_OVER_ALPHA = 0.4f;

        /// <summary>Drawn in place of the count on a slot the player holds none of: that slot's tap opens
        /// the shop, so it reads as an offer rather than as a dead icon showing 0.</summary>
        private const string SHOP_AFFORDANCE_LABEL = "+";

        /// <summary>Prefix of a held count: the mockup's "x2".</summary>
        private const string COUNT_PREFIX = "x";

        [Header("Layout")]
        [Tooltip("Strip centre in canvas space. Sits in the gap between the board card and the tray.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -520f);

        [Tooltip("Side of a slot at the preferred size. Slots shrink below this only when the strip would otherwise run past its width limit.")]
        [SerializeField] private float _slotSize = 144f;

        [Tooltip("Preferred gap between neighbouring slots.")]
        [SerializeField] private float _slotGap = 28f;

        [Tooltip("The gap slots close up to before they start shrinking.")]
        [SerializeField] private float _minSlotGap = 16f;

        [Tooltip("Widest the strip may grow, in canvas units. Slots are shrunk rather than letting the "
            + "strip run past the board as kinds are added.")]
        [SerializeField] private float _maxStripWidth = 960f;

        [Header("Icons")]
        [Tooltip("White-on-transparent glyphs, one per slot in display order (Bomb, Row Clear, Column Clear, "
            + "Joker, Colour Cleanser, Rotate, Reroll, Double Score, Ghost Fit, Paint Cross) — the same "
            + "sprites the shop rows draw, so a power-up looks the same wherever it is met. Tinted at runtime.")]
        [SerializeField] private Sprite[] _slotIcons = new Sprite[SlotCount];

        [Header("Art")]
        [Tooltip("The heavy label face for the count badges. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _labelFont;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _countBuilder = new StringBuilder(8);

        private readonly RectTransform[] _slotRects = new RectTransform[SlotCount];
        private readonly Image[] _plateImages = new Image[SlotCount];
        private readonly Image[] _shadowImages = new Image[SlotCount];
        private readonly Image[] _emptyRingImages = new Image[SlotCount];
        private readonly Image[] _armedRingImages = new Image[SlotCount];
        private readonly Image[] _iconShadeImages = new Image[SlotCount];
        private readonly Image[] _iconFillImages = new Image[SlotCount];
        private readonly Image[] _glyphImages = new Image[SlotCount];
        private readonly Image[] _badgeRimImages = new Image[SlotCount];
        private readonly Image[] _badgeDiscImages = new Image[SlotCount];
        private readonly Text[] _countTexts = new Text[SlotCount];
        private readonly int[] _counts = new int[SlotCount];

        /// <summary>Where each slot sat when the current reflow started, so the animation can slide from
        /// there. Pre-allocated because a reflow runs per frame and must not allocate.</summary>
        private readonly float[] _reflowStartX = new float[SlotCount];

        private PowerUpModel _powerUpModel;
        private PowerUpSystem _powerUpSystem;
        private LevelProgressionModel _levelProgressionModel;
        private SettingsModel _settingsModel;
        private GameModeSystem _gameModeSystem;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;

        private CanvasGroup _canvasGroup;
        private Canvas _canvas;
        private ThemeDefinition _currentTheme;
        private PowerUpKind? _armed;
        private bool _isRunOver;

        /// <summary>
        /// Whether the active mode's ruleset includes power-ups (issue #355). The strip has nothing to
        /// offer in Classic mode (<see cref="GameMode.Timed"/>) — it is hidden outright, mirroring how
        /// <c>LevelPathButtonView.RefreshVisibility</c> hides its own pill outside the one mode it
        /// applies to.
        /// </summary>
        private bool _extrasEnabled = true;

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
            GameModeSystem gameModeSystem,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _powerUpModel = powerUpModel;
            _powerUpSystem = powerUpSystem;
            _levelProgressionModel = levelProgressionModel;
            _settingsModel = settingsModel;
            _gameModeSystem = gameModeSystem;
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
                || _settingsModel == null || _gameModeSystem == null
                || _runStartedSubscriber == null || _gameOverSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpInventoryView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so _currentTheme is set before any slot is painted below.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Subscribed early, same reason: whether the strip may show at all is decided before any
            // run-over/theme repaint below runs.
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);

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
            WatchCount(_powerUpModel.PaintCrossCount, PowerUpKind.PaintCross);
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
        /// the armed one, or — on a slot the player holds none of — reports through
        /// <paramref name="wantsShop"/> that the shop should open, since this View owns no card of its
        /// own. Returns false when the point is on no icon, so <see cref="BoardInputView"/> can carry
        /// on down its gate chain. Only the slots currently on screen are considered: a kind behind its
        /// level gate is not drawn, so there is no point on the canvas that could resolve to it.
        /// <para>
        /// <see cref="PowerUpKind.Reroll"/> and <see cref="PowerUpKind.DoubleMultiplier"/> are the
        /// exceptions to the arm-then-aim flow: with no target to aim at there is no second tap to wait
        /// for, so a tap on one the player holds applies it there and then.
        /// </para>
        /// </summary>
        internal bool TryHandleTap(Vector2 screenPosition, out bool wantsShop)
        {
            wantsShop = false;

            if (_isRunOver || !_extrasEnabled || _powerUpSystem == null)
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
                    // would otherwise be a dead press. It is instead the way into the shop, offered
                    // exactly where a player reaches for a power-up they do not have. Nothing is
                    // granted here: a power-up is only ever bought.
                    wantsShop = true;
                }
                else
                {
                    _powerUpSystem.Arm(kind);
                }

                return true;
            }

            return false;
        }

        /// <summary>Slot index under a screen point, or -1. Hit-tests only the slots currently on
        /// screen, exactly like <see cref="TryHandleTap"/> does — unlike that call, this does not act on
        /// what it finds. Used by <see cref="BoardInputView"/> to resolve a power-up strip press to a
        /// slot without firing the press's tap action, so the action can be deferred to release and
        /// resolved as either an ordinary tap or the long-press "show info" gesture.</summary>
        internal int GetSlotIndexAt(Vector2 screenPosition)
        {
            if (_isRunOver || !_extrasEnabled || _powerUpSystem == null)
            {
                return -1;
            }

            for (int slotIndex = 0; slotIndex < _visibleCount; slotIndex++)
            {
                if (ContainsScreenPoint(slotIndex, screenPosition))
                {
                    return slotIndex;
                }
            }

            return -1;
        }

        /// <summary>The <see cref="PowerUpKind"/> drawn in slot <paramref name="slotIndex"/>. Static
        /// because the mapping is fixed at compile time (see <see cref="SlotKinds"/>) and does not
        /// depend on any instance state.</summary>
        internal static PowerUpKind KindAt(int slotIndex) => SlotKinds[slotIndex];

        /// <summary>The on-screen rect of <paramref name="kind"/>'s strip slot. Null for a kind with no
        /// slot in the strip (Reroll, CoinSower, Hold — see the class summary).</summary>
        internal RectTransform GetSlotRectTransform(PowerUpKind kind)
        {
            for (int slotIndex = 0; slotIndex < SlotKinds.Length; slotIndex++)
            {
                if (SlotKinds[slotIndex] == kind)
                {
                    return _slotRects[slotIndex];
                }
            }

            return null;
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
        /// Hides the whole strip outright in Classic mode (<see cref="GameMode.Timed"/> — issue #355),
        /// which has no power-ups to offer: no dimming, no reserved space to tap through, mirroring how
        /// <c>LevelPathButtonView.RefreshVisibility</c> hides its own Path-only pill.
        /// </summary>
        private void OnModeChanged(GameMode mode)
        {
            _extrasEnabled = mode != GameMode.Timed;
            RefreshInteractivity();
        }

        /// <summary>
        /// Power-ups are not offered once the run is over, or once the active mode has none to offer.
        /// The strip is dimmed/hidden and made non-interactive rather than deactivated: toggling the
        /// GameObject would rebuild the shared canvas mesh every time a run ends, starts or the mode
        /// changes.
        /// </summary>
        private void SetRunOver(bool isRunOver)
        {
            _isRunOver = isRunOver;
            RefreshInteractivity();
        }

        private void RefreshInteractivity()
        {
            if (!_extrasEnabled)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
                return;
            }

            _canvasGroup.alpha = _isRunOver ? RUN_OVER_ALPHA : 1f;
            _canvasGroup.interactable = !_isRunOver;
            _canvasGroup.blocksRaycasts = !_isRunOver;
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
        /// here — it has no slot on screen at all. Held and empty are exclusive; armed is a ring laid
        /// over either, though in practice only a held kind can be armed.
        /// </summary>
        private void RefreshSlot(int slotIndex)
        {
            if (_currentTheme == null || _plateImages[slotIndex] == null)
            {
                return;
            }

            bool isArmed = _armed == SlotKinds[slotIndex];
            bool isAvailable = _counts[slotIndex] > 0;
            float plateAlpha = isAvailable ? 1f : EMPTY_PLATE_ALPHA;
            float iconAlpha = isAvailable ? 1f : EMPTY_ICON_ALPHA;
            int kind = KindFor(slotIndex);

            _plateImages[slotIndex].color = HudChrome.WithAlpha(_currentTheme.CardBackground, plateAlpha);

            // An empty slot has no drop shadow: it reads as a socket in the background rather than as
            // a chip sat on it, which is the mockup's inset ring look.
            _shadowImages[slotIndex].color = isAvailable ? _currentTheme.CardShadow : Color.clear;
            _emptyRingImages[slotIndex].color = isAvailable ? Color.clear : _currentTheme.EmptyCellOutline;
            _armedRingImages[slotIndex].color = isArmed ? _currentTheme.Accent : Color.clear;

            _iconShadeImages[slotIndex].color = HudChrome.WithAlpha(_currentTheme.GetShade(kind), iconAlpha);
            _iconFillImages[slotIndex].color = HudChrome.WithAlpha(_currentTheme.GetFill(kind), iconAlpha);

            // Ghost Fit's glyph is the one drawn see-through; every other kind's takes the icon plate's
            // alpha unchanged.
            float glyphAlpha = SlotKinds[slotIndex] == PowerUpKind.GhostFit
                ? iconAlpha * GHOST_FIT_GLYPH_ALPHA
                : iconAlpha;
            _glyphImages[slotIndex].color = HudChrome.WithAlpha(Color.white, glyphAlpha);

            // The badge is drawn at full strength over an otherwise dimmed empty slot: it is the one
            // part of the slot that states a number, and a number has to be legible in every state.
            // Two tones, so the offer is never mistaken for a stock of one: green means "you hold this
            // many", pink means "tap to buy one".
            _badgeRimImages[slotIndex].color = _currentTheme.CardBackground;
            _badgeDiscImages[slotIndex].color = isAvailable
                ? _currentTheme.GetFill(HudChrome.GREEN_KIND)
                : HudChrome.OfferPink;
            _countTexts[slotIndex].color = Color.white;

            _countBuilder.Clear();
            if (isAvailable)
            {
                _countBuilder.Append(COUNT_PREFIX);
                _countBuilder.Append(_counts[slotIndex]);
            }
            else
            {
                _countBuilder.Append(SHOP_AFFORDANCE_LABEL);
            }

            _countTexts[slotIndex].text = _countBuilder.ToString();
        }

        /// <summary>The theme kind a slot's icon plate is tinted with: the block colours in order,
        /// wrapping after the fifth, so the strip reads as the same palette as the board.</summary>
        private static int KindFor(int slotIndex) => (slotIndex % KIND_COUNT) + 1;

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

            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                BuildSlot(rect, slotIndex);
            }

            _visibleCount = ResolveVisibleCount();
            ApplyLayout(animate: false);
        }

        /// <summary>
        /// The side and gap the visible slots are actually laid out with. At the preferred size and gap
        /// while they fit inside <see cref="_maxStripWidth"/>; past that the gap closes to
        /// <see cref="_minSlotGap"/> and every slot shrinks by the same factor, so nine kinds sit in the
        /// width five did. Measured from the slots on screen rather than from <see cref="SlotCount"/>,
        /// so the strip only pays the squeeze once the kinds that need it have actually unlocked.
        /// </summary>
        private void ResolveSlotLayout(int slotCount, out float slotSide, out float gap)
        {
            slotSide = _slotSize;
            gap = _slotGap;

            if (slotCount <= 1)
            {
                return;
            }

            float preferredWidth = (slotCount * _slotSize) + ((slotCount - 1) * _slotGap);
            if (preferredWidth <= _maxStripWidth)
            {
                return;
            }

            gap = Mathf.Min(_slotGap, _minSlotGap);
            slotSide = Mathf.Min(_slotSize, (_maxStripWidth - ((slotCount - 1) * gap)) / slotCount);
        }

        /// <summary>
        /// Places the visible slots and sizes the strip around them. A growth is slid rather than
        /// snapped: every slot already on screen shifts when one is revealed, and a whole strip jumping
        /// sideways under the player's thumb reads as a glitch rather than as a reward. A shrink in
        /// slot size is applied at once — it is a scale on the slot, and the slide covers the shift.
        /// </summary>
        private void ApplyLayout(bool animate)
        {
            CancelReflow();

            ResolveSlotLayout(_visibleCount, out float slotSide, out float gap);
            float spacing = slotSide + gap;
            float originX = -spacing * ((_visibleCount - 1) * 0.5f);
            float width = _visibleCount > 0 ? (spacing * (_visibleCount - 1)) + slotSide : 0f;
            float scale = _slotSize > 0f ? slotSide / _slotSize : 1f;
            var scaleVector = new Vector3(scale, scale, 1f);
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
                slotRect.localScale = scaleVector;
                slotRect.gameObject.SetActive(isVisible);
            }

            if (!animate)
            {
                rect.sizeDelta = new Vector2(width, slotSide);
                for (int slotIndex = 0; slotIndex < _visibleCount; slotIndex++)
                {
                    _slotRects[slotIndex].anchoredPosition = new Vector2(originX + (slotIndex * spacing), 0f);
                }

                return;
            }

            _reflowCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            ReflowAsync(rect, originX, spacing, width, slotSide, _reflowCts.Token).Forget();
        }

        private async UniTaskVoid ReflowAsync(
            RectTransform rect,
            float originX,
            float spacing,
            float width,
            float slotSide,
            CancellationToken cancellationToken)
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
                    ApplyReflowFrame(rect, originX, spacing, width, slotSide, startWidth, animatedCount, progress);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    elapsed += Time.unscaledDeltaTime;
                }

                ApplyReflowFrame(rect, originX, spacing, width, slotSide, startWidth, animatedCount, 1f);
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
            float slotSide,
            float startWidth,
            int animatedCount,
            float progress)
        {
            rect.sizeDelta = new Vector2(Mathf.Lerp(startWidth, width, progress), slotSide);

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

        /// <summary>
        /// One slot at the preferred size, bottom to top: the armed ring standing off the plate, the
        /// plate over its shadow, the empty ring inset on the plate, the kind-tinted icon plate (a shade
        /// base with the fill lifted off its bottom edge, the same bevel a block wears) with the glyph
        /// on it, and the count badge hung on the top-right corner. Everything is painted clear here
        /// and coloured by <see cref="RefreshSlot"/>.
        /// </summary>
        private void BuildSlot(RectTransform parent, int slotIndex)
        {
            var slotObject = new GameObject($"PowerUpSlot_{SlotKinds[slotIndex]}", typeof(RectTransform));
            var slotRect = (RectTransform)slotObject.transform;
            slotRect.SetParent(parent, false);
            HudChrome.Centre(slotRect, new Vector2(_slotSize, _slotSize));
            _slotRects[slotIndex] = slotRect;

            float plateRadius = _slotSize * PLATE_RADIUS_FRACTION;
            var slotSize = new Vector2(_slotSize, _slotSize);

            float armedSide = _slotSize + (ARMED_RING_THICKNESS * 2f);
            _armedRingImages[slotIndex] = HudChrome.BuildOutline(
                slotRect, "ArmedRing", new Vector2(armedSide, armedSide), Vector2.zero,
                plateRadius + ARMED_RING_THICKNESS, ARMED_RING_THICKNESS);

            RectTransform plateRect = HudChrome.BuildPlate(
                slotRect, "Plate", slotSize, Vector2.zero, plateRadius, HudChrome.PLATE_SHADOW_DROP,
                out _shadowImages[slotIndex], out _plateImages[slotIndex]);

            _emptyRingImages[slotIndex] = HudChrome.BuildOutline(
                plateRect, "EmptyRing", slotSize, Vector2.zero, plateRadius, EMPTY_RING_THICKNESS);

            float iconSide = _slotSize * ICON_PLATE_FRACTION;
            float iconRadius = iconSide * ICON_PLATE_RADIUS_FRACTION;
            float iconBevel = _slotSize * ICON_BEVEL_FRACTION;
            RectTransform iconRect = HudChrome.CreateRect(plateRect, "Icon", new Vector2(iconSide, iconSide), Vector2.zero);
            _iconShadeImages[slotIndex] = HudChrome.BuildRounded(
                iconRect, "Shade", new Vector2(iconSide, iconSide), Vector2.zero, iconRadius);
            _iconFillImages[slotIndex] = HudChrome.BuildRounded(
                iconRect, "Fill", new Vector2(iconSide, iconSide - iconBevel), new Vector2(0f, iconBevel * 0.5f), iconRadius);

            float glyphSide = _slotSize * GLYPH_SIZE_FRACTION;
            _glyphImages[slotIndex] = HudChrome.BuildGlyph(
                iconRect, "Glyph", IconFor(SlotKinds[slotIndex]), new Vector2(glyphSide, glyphSide),
                new Vector2(0f, iconBevel * 0.5f));

            // The badge is a sibling of the plate rather than a child of it, and built after it, so it
            // draws over both the plate and the icon without inheriting the plate's colour. Counts are
            // built before the theme or the inventory is known; the subscriptions in Start fill in both.
            float badgeInset = _slotSize * BADGE_INSET_FRACTION;
            var badgePosition = new Vector2((_slotSize * 0.5f) - badgeInset, (_slotSize * 0.5f) - badgeInset);
            HudChrome.BuildBadge(
                slotRect, "CountBadge", _slotSize * BADGE_SIZE_FRACTION, badgePosition, _labelFont,
                out _badgeRimImages[slotIndex], out _badgeDiscImages[slotIndex], out _countTexts[slotIndex]);
        }

        /// <summary>The icon for <paramref name="kind"/>, authored in <see cref="SlotKinds"/> order —
        /// the same sprites the shop rows draw, so the thing the player bought is the thing they see
        /// in the strip. Internal so <see cref="InfoPopupView"/> can reuse it for a power-up popup's
        /// hero icon.</summary>
        internal Sprite IconFor(PowerUpKind kind)
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
    }
}
