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
        };

        /// <summary>Derived from <see cref="SlotKinds"/> rather than written out, so the two can never
        /// disagree about how many slots there are.</summary>
        private static readonly int SlotCount = SlotKinds.Length;

        /// <summary>Alpha applied to a slot the player holds none of, so "empty" reads at a glance.</summary>
        private const float EMPTY_SLOT_ALPHA = 0.35f;

        /// <summary>Alpha of the whole strip once the run is over and nothing can be armed.</summary>
        private const float RUN_OVER_ALPHA = 0.4f;

        /// <summary>Drawn in place of the count on a slot the player holds none of: that slot's tap is
        /// the "earn one" gesture, so it reads as an offer rather than as a dead icon showing 0.</summary>
        private const string EARN_AFFORDANCE_LABEL = "+";

        /// <summary>Turns the joker's square glyph onto its point, so it is distinct from the bomb's
        /// disc and the two clear bars without needing a fourth sprite.</summary>
        private const float JOKER_GLYPH_ROTATION_DEGREES = 45f;

        /// <summary>Tilts the rotate power-up's bar off both axes, so it is distinct from the row and
        /// column bars and reads as "turned" — again without needing another sprite.</summary>
        private const float ROTATE_GLYPH_ROTATION_DEGREES = 45f;

        /// <summary>Height-to-width ratio that squashes the reroll's circle into a lozenge, so it reads
        /// apart from the bomb's disc without needing an eighth sprite.</summary>
        private const float REROLL_GLYPH_FLATTEN = 0.5f;

        /// <summary>Width-to-height ratio that stands the same circle on end for the double multiplier,
        /// so it reads apart from the reroll's flat lozenge without needing a ninth sprite.</summary>
        private const float DOUBLE_MULTIPLIER_GLYPH_FLATTEN = 0.5f;

        [Header("Layout")]
        [Tooltip("Strip centre in canvas space. Sits in the gap between the board card and the tray.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -430f);

        [SerializeField] private float _slotSize = 104f;

        [Tooltip("Preferred horizontal distance between neighbouring slot centres.")]
        [SerializeField] private float _slotSpacing = 180f;

        [Tooltip("Widest the strip may grow, in canvas units. Spacing is squeezed below the preferred "
            + "value rather than letting the strip run off the canvas as kinds are added.")]
        [SerializeField] private float _maxStripWidth = 1000f;

        [SerializeField] private int _countFontSize = 34;

        [Tooltip("Extra scale applied to the armed slot, so the selection reads without any new art.")]
        [SerializeField] private float _armedScale = 1.12f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _countBuilder = new StringBuilder(4);

        private readonly RectTransform[] _slotRects = new RectTransform[SlotCount];
        private readonly Image[] _plateImages = new Image[SlotCount];
        private readonly Image[] _shadowImages = new Image[SlotCount];
        private readonly Image[] _glyphImages = new Image[SlotCount];
        private readonly Text[] _countTexts = new Text[SlotCount];
        private readonly int[] _counts = new int[SlotCount];

        /// <summary>Per slot: a reward request is in flight. Guards against a rapid double tap firing
        /// two concurrent requests and banking two power-ups for one watch.</summary>
        private readonly bool[] _isRequestingReward = new bool[SlotCount];

        private PowerUpModel _powerUpModel;
        private PowerUpSystem _powerUpSystem;
        private SettingsModel _settingsModel;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;

        private CanvasGroup _canvasGroup;
        private Canvas _canvas;
        private ThemeDefinition _currentTheme;
        private PowerUpKind? _armed;
        private bool _isRunOver;

        [Inject]
        public void Construct(
            PowerUpModel powerUpModel,
            PowerUpSystem powerUpSystem,
            SettingsModel settingsModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _powerUpModel = powerUpModel;
            _powerUpSystem = powerUpSystem;
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
            if (_powerUpModel == null || _powerUpSystem == null || _settingsModel == null
                || _runStartedSubscriber == null || _gameOverSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpInventoryView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so _currentTheme is set before any slot is painted below.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            WatchCount(_powerUpModel.BombCount, PowerUpKind.Bomb);
            WatchCount(_powerUpModel.RowClearCount, PowerUpKind.RowClear);
            WatchCount(_powerUpModel.ColumnClearCount, PowerUpKind.ColumnClear);
            WatchCount(_powerUpModel.JokerCount, PowerUpKind.Joker);
            WatchCount(_powerUpModel.ColorCleanserCount, PowerUpKind.ColorCleanser);
            WatchCount(_powerUpModel.RotateCount, PowerUpKind.Rotate);
            WatchCount(_powerUpModel.RerollCount, PowerUpKind.Reroll);
            WatchCount(_powerUpModel.DoubleMultiplierCount, PowerUpKind.DoubleMultiplier);
            _powerUpModel.Armed.Subscribe(OnArmedChanged).AddTo(_disposables);

            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>
        /// Routes a tap that landed on one of the icons: arms that kind, cancels it when it is already
        /// the armed one, or — on a slot the player holds none of — asks for one to be earned. Returns
        /// false when the point is on no icon, so <see cref="BoardInputView"/> can carry on down its
        /// gate chain.
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

            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
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
            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                RefreshSlot(slotIndex);
            }
        }

        /// <summary>Repaints one slot from the three inputs that can change how it looks: the theme,
        /// the count held, and whether this kind is the armed one.</summary>
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

            _plateImages[slotIndex].color = WithAlpha(plateColour, alpha);
            _shadowImages[slotIndex].color = WithAlpha(_currentTheme.CardShadow, alpha);
            _glyphImages[slotIndex].color = WithAlpha(glyphColour, alpha);
            _countTexts[slotIndex].color = WithAlpha(_currentTheme.Ink, alpha);

            float scale = isArmed ? _armedScale : 1f;
            _slotRects[slotIndex].localScale = new Vector3(scale, scale, 1f);

            if (isAvailable)
            {
                _countBuilder.Clear();
                _countBuilder.Append(_counts[slotIndex]);
                _countTexts[slotIndex].text = _countBuilder.ToString();
            }
            else
            {
                _countTexts[slotIndex].text = EARN_AFFORDANCE_LABEL;
            }
        }

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        private void BuildSlots()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            float spacing = ResolveSlotSpacing();
            rect.sizeDelta = new Vector2((spacing * (SlotCount - 1)) + _slotSize, _slotSize);
            rect.anchoredPosition = _anchoredPosition;

            // Every size here is in canvas reference units, so the strip owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            rect.localScale = Vector3.one;

            float originX = -spacing * ((SlotCount - 1) * 0.5f);

            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                BuildSlot(rect, slotIndex, originX + (slotIndex * spacing));
            }
        }

        /// <summary>
        /// The spacing the strip is actually laid out with: the preferred value, squeezed just enough
        /// to keep the whole strip inside <see cref="_maxStripWidth"/>. Derived rather than authored so
        /// adding a kind widens the gaps' arithmetic instead of pushing the outermost slot off screen —
        /// the serialized spacing is already close to the canvas width at six slots.
        /// </summary>
        private float ResolveSlotSpacing()
        {
            if (SlotCount <= 1)
            {
                return _slotSpacing;
            }

            float maxSpacing = (_maxStripWidth - _slotSize) / (SlotCount - 1);
            return _slotSpacing <= maxSpacing ? _slotSpacing : maxSpacing;
        }

        private void BuildSlot(RectTransform parent, int slotIndex, float centreX)
        {
            var slotObject = new GameObject($"PowerUpSlot_{SlotKinds[slotIndex]}", typeof(RectTransform));
            var slotRect = (RectTransform)slotObject.transform;
            slotRect.SetParent(parent, false);
            Centre(slotRect, new Vector2(_slotSize, _slotSize));
            slotRect.anchoredPosition = new Vector2(centreX, 0f);
            _slotRects[slotIndex] = slotRect;

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(slotRect, false);
            Centre(shadowRect, new Vector2(_slotSize + 10f, _slotSize + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImages[slotIndex] = ConfigurePlate(shadowObject.GetComponent<Image>());

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(slotRect, false);
            Centre(plateRect, new Vector2(_slotSize, _slotSize));
            _plateImages[slotIndex] = ConfigurePlate(plateObject.GetComponent<Image>());

            _glyphImages[slotIndex] = BuildGlyph(plateRect, SlotKinds[slotIndex]);

            // Counts are built here, before the theme or the inventory is known; the subscriptions in
            // Start fill in both.
            Text countText = UiTextFactory.Create(
                slotRect, "Count", _countFontSize, FontStyle.Bold, Color.clear);
            var countRect = (RectTransform)countText.transform;
            countRect.anchorMin = new Vector2(1f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(1f, 0f);
            countRect.anchoredPosition = new Vector2(4f, -10f);
            countText.alignment = TextAnchor.LowerRight;
            _countTexts[slotIndex] = countText;
        }

        /// <summary>
        /// One Image per kind, drawn from the shared placeholder sprites so the strip adds no art
        /// dependency and keeps batching with the rest of the UI: a disc for the bomb, a wide bar for
        /// the row clear, a tall bar for the column clear, a diamond — the same rounded square, turned
        /// 45 degrees — for the joker, which reads as "one cell, placed askew", an upright square for
        /// the colour cleanser, that same bar turned 45 degrees for the rotate, and a flattened circle
        /// for the reroll, and that same circle stood on end for the double multiplier.
        /// </summary>
        private Image BuildGlyph(RectTransform parent, PowerUpKind kind)
        {
            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(parent, false);

            var glyphImage = glyphObject.GetComponent<Image>();

            float barLength = _slotSize * 0.62f;
            float barThickness = _slotSize * 0.20f;

            switch (kind)
            {
                case PowerUpKind.RowClear:
                    Centre(glyphRect, new Vector2(barLength, barThickness));
                    ConfigurePlate(glyphImage);
                    break;
                case PowerUpKind.ColumnClear:
                    Centre(glyphRect, new Vector2(barThickness, barLength));
                    ConfigurePlate(glyphImage);
                    break;
                case PowerUpKind.Joker:
                    float side = _slotSize * 0.40f;
                    Centre(glyphRect, new Vector2(side, side));
                    // Rotating the rect, not the sprite: the same rounded square every other plate
                    // uses stays in the same atlas draw call, it is simply drawn on its point.
                    glyphRect.localRotation = Quaternion.Euler(0f, 0f, JOKER_GLYPH_ROTATION_DEGREES);
                    ConfigurePlate(glyphImage);
                    break;
                case PowerUpKind.ColorCleanser:
                    // Upright square (not rotated to Joker's diamond, not round like Bomb) — the fifth
                    // distinct silhouette in the strip, no new sprite needed.
                    float swatchSide = _slotSize * 0.44f;
                    Centre(glyphRect, new Vector2(swatchSide, swatchSide));
                    ConfigurePlate(glyphImage);
                    break;
                case PowerUpKind.Rotate:
                    // The sixth silhouette: the row clear's bar, tilted. Same rect-rotation trick as
                    // Joker's diamond, so it stays in the shared atlas draw call.
                    Centre(glyphRect, new Vector2(barLength, barThickness));
                    glyphRect.localRotation = Quaternion.Euler(0f, 0f, ROTATE_GLYPH_ROTATION_DEGREES);
                    ConfigurePlate(glyphImage);
                    break;
                case PowerUpKind.Reroll:
                    // The seventh: the bomb's circle sprite, squashed into a lozenge. Distinct from the
                    // bomb's true disc and from every straight-edged plate in the strip, and still the
                    // same two sprites — nothing new to atlas.
                    float lozengeWidth = _slotSize * 0.56f;
                    Centre(glyphRect, new Vector2(lozengeWidth, lozengeWidth * REROLL_GLYPH_FLATTEN));
                    ConfigureCircle(glyphImage);
                    break;
                case PowerUpKind.DoubleMultiplier:
                    // The eighth: the same circle sprite as the reroll's lozenge, stood on end instead
                    // of laid flat. Distinct from both the flat lozenge and the bomb's true disc, and
                    // still no new sprite to atlas.
                    float uprightWidth = _slotSize * 0.56f * DOUBLE_MULTIPLIER_GLYPH_FLATTEN;
                    Centre(glyphRect, new Vector2(uprightWidth, _slotSize * 0.56f));
                    ConfigureCircle(glyphImage);
                    break;
                default:
                    float diameter = _slotSize * 0.5f;
                    Centre(glyphRect, new Vector2(diameter, diameter));
                    ConfigureCircle(glyphImage);
                    break;
            }

            return glyphImage;
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
            image.pixelsPerUnitMultiplier = 3f;
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
