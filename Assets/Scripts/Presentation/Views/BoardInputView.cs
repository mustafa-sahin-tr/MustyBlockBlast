using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The only class that touches input. Reads a pointer (mouse or touch) via the Input System,
    /// drags a ghost piece and asks <see cref="BoardSystem"/> to place it. Zero game logic: legality
    /// and consequences are decided by the System.
    /// <para>
    /// A press is resolved by a single ordered gate chain, so exactly one claimant handles it: the
    /// four modal overlays (the hub — which is itself the one owner of the settings, power-up shop,
    /// leaderboard, profile and badges cards — level path, objective info, Coin Sower picker), game
    /// over, the settings icon, the level path icon, an objective icon, the coin pill's "+" disc, a
    /// power-up inventory icon, an armed power-up being aimed at the board, and finally a tray piece
    /// being picked up. HUD icons are resolved
    /// here rather than by an EventSystem: the scene has one, but every UI Image outside
    /// <see cref="LevelPathPanelView"/> has its raycast target off, so nothing else is reachable
    /// through it.
    /// </para>
    /// <para>
    /// That panel is therefore the one gate below that swallows its press instead of routing it: its
    /// trail scrolls, which needs the EventSystem's drag handling, and the EventSystem reads the same
    /// pointer this View does. Every other overlay still resolves its own taps here.
    /// </para>
    /// <para>
    /// An armed power-up is aimed at the board, with one exception: <see cref="PowerUpKind.Rotate"/> is
    /// aimed at the tray, so its release resolves to a dock slot index instead of a board cell. Both
    /// branches share the one aim gate, so only the target resolution differs.
    /// </para>
    /// <para>
    /// A drag is also cancelled if its dock slot is rewritten underneath it — see
    /// <see cref="OnTraySlotChanged"/>, which covers the Reroll power-up replacing the whole dock.
    /// </para>
    /// <para>
    /// A Ghost Fit suggestion is dismissed by any press that reaches past the power-up strip — aiming an
    /// armed kind at the board, touching a cell, or picking up a dock piece other than the suggested one.
    /// Picking up the suggested piece is the exception: that is the player acting on the hint, so the
    /// silhouette stays up to aim at and the placement itself takes it down.
    /// </para>
    /// <para>
    /// A dock slot holding a <see cref="SpecialPieceKind.DemolitionHammer"/> is the one slot a press does
    /// not start a drag on: it arms instead, and the next press is aimed at the board exactly as an armed
    /// power-up is. That arm is this View's own state and never enters
    /// <see cref="PowerUpModel.Armed"/> — the hammer is not an inventory item.
    /// </para>
    /// <para>
    /// A piece drag has a second destination besides the board: released over <see cref="HoldSlotView"/>
    /// it is parked in the pocket instead of placed. That branch is decided while dragging, not on
    /// release, so the highlight the player sees and the drop they get are always the same thing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardInputView : MonoBehaviour
    {
        [Header("Drag feel")]
        [Tooltip("Screen-space offset applied to the dragged piece so a finger does not cover it.")]
        [SerializeField] private Vector2 _dragScreenOffset = new Vector2(0f, 150f);
        [SerializeField] private float _ghostAlpha = 0.9f;

        private readonly List<CellView> _ghostCells = new List<CellView>(9);
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Reused every aim frame so previewing a power-up target allocates nothing. Sized to
        /// the widest footprint any kind can target, so no aim frame can ever grow it.</summary>
        private readonly List<GridPosition> _powerUpTargetBuffer =
            new List<GridPosition>(PowerUpTargetCells.MAX_TARGET_CELLS);

        private InputAction _pointerPositionAction;
        private InputAction _pointerPressAction;
        private RectTransform _dragLayer;
        private RectTransform _ghostRoot;
        private Canvas _canvas;

        private BoardSystem _boardSystem;
        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private SettingsModel _settingsModel;
        private PowerUpModel _powerUpModel;
        private PowerUpSystem _powerUpSystem;
        private GhostFitSystem _ghostFitSystem;
        private LevelProgressionSystem _levelProgressionSystem;
        private ThemeDefinition _currentTheme;
        private BoardView _boardView;
        private PieceTrayView _trayView;
        private HoldSlotView _holdSlotView;
        private SettingsButtonView _settingsButtonView;
        private HubPanelView _hubPanelView;
        private LevelPathButtonView _levelPathButtonView;
        private LevelPathPanelView _levelPathPanelView;
        private CoinSowerPickerView _coinSowerPickerView;
        private RunResultView _runResultView;
        private CoinTotalHudView _coinTotalHudView;
        private PowerUpInventoryView _powerUpInventoryView;
        private ObjectiveIconContainerView _objectiveIconContainerView;
        private ObjectiveInfoPopupView _objectiveInfoPopupView;
        private InfoPopupView _infoPopupView;
        private InfoPopupSystem _infoPopupSystem;
        private CellSkinIconCatalog _cellSkinIconCatalog;

        private int _draggedSlot = -1;

        /// <summary>Screen position the current drag's press started at, for the special-piece manual
        /// info popup reopen gesture (see <see cref="OnPressReleased"/>'s snap-back branch): a tap that
        /// moved too far reads as an aborted drag rather than a look-again tap.</summary>
        private Vector2 _dragStartScreenPosition;

        private GridPosition _currentAnchor;
        private bool _hasAnchor;

        /// <summary>Screen position a press landed on a power-up strip slot at, recorded so the tap's
        /// action (arm/cancel/shop) can be deferred to release and resolved as either a normal tap or
        /// the long-press "show info" gesture. -1 slot means no tap is pending.</summary>
        private Vector2 _pendingPowerUpTapScreenPosition;
        private float _pendingPowerUpTapStartTime;
        private bool _hasPendingPowerUpTap;

        /// <summary>Held duration, in unscaled seconds, that turns a power-up strip press into the
        /// "show info" gesture instead of its normal tap action.</summary>
        private const float POWERUP_LONG_PRESS_SECONDS = 0.48f;

        /// <summary>Screen-space movement, in pixels, a power-up strip press may drift by and still
        /// count as a long-press rather than an aborted drag.</summary>
        private const float POWERUP_LONG_PRESS_MOVE_TOLERANCE = 14f;

        /// <summary>Screen-space movement, in pixels, a dock press may drift by and still count as a tap
        /// (rather than a drag) for the special-piece info popup reopen gesture.</summary>
        private const float TRAY_TAP_MOVE_TOLERANCE = 14f;

        /// <summary>Whether this drag frame's ghost sits over the Hold slot. Resolved during the drag
        /// rather than on release, so the drop and the highlight can never disagree about the target.</summary>
        private bool _isOverHoldSlot;

        private bool _isAimingPowerUp;
        private GridPosition _powerUpTargetCell;
        private bool _hasPowerUpTarget;

        /// <summary>Dock slot the armed Rotate is over this aim frame, or -1. Rotate is the one kind
        /// aimed at the tray rather than the board, so it resolves to a slot index instead of a cell.</summary>
        private int _powerUpTargetSlot = -1;

        /// <summary>
        /// Dock slot holding the armed <see cref="SpecialPieceKind.DemolitionHammer"/>, or -1 for none.
        /// <para>
        /// Deliberately a field on this View rather than an entry in <see cref="PowerUpModel.Armed"/>:
        /// the hammer is never an inventory item (it is injected by a board condition and consumed by
        /// its one use), so riding the power-up arm model would make it one. There is no System-side
        /// arm state to cancel either, which is why the cancel gesture is implemented here.
        /// </para>
        /// </summary>
        private int _armedHammerSlot = -1;

        /// <summary>Whether the armed hammer is currently being aimed — the hammer's own mirror of
        /// <see cref="_isAimingPowerUp"/>, and mutually exclusive with it and with a drag.</summary>
        private bool _isAimingHammer;

        private GridPosition _hammerTargetCell;
        private bool _hasHammerTarget;

        [Inject]
        public void Construct(
            BoardSystem boardSystem,
            BoardModel boardModel,
            TrayModel trayModel,
            SettingsModel settingsModel,
            PowerUpModel powerUpModel,
            PowerUpSystem powerUpSystem,
            GhostFitSystem ghostFitSystem,
            LevelProgressionSystem levelProgressionSystem,
            BoardView boardView,
            PieceTrayView trayView,
            HoldSlotView holdSlotView,
            SettingsButtonView settingsButtonView,
            HubPanelView hubPanelView,
            LevelPathButtonView levelPathButtonView,
            LevelPathPanelView levelPathPanelView,
            CoinSowerPickerView coinSowerPickerView,
            RunResultView runResultView,
            CoinTotalHudView coinTotalHudView,
            PowerUpInventoryView powerUpInventoryView,
            ObjectiveIconContainerView objectiveIconContainerView,
            ObjectiveInfoPopupView objectiveInfoPopupView,
            InfoPopupView infoPopupView,
            InfoPopupSystem infoPopupSystem,
            CellSkinIconCatalog cellSkinIconCatalog)
        {
            _boardSystem = boardSystem;
            _boardModel = boardModel;
            _trayModel = trayModel;
            _settingsModel = settingsModel;
            _powerUpModel = powerUpModel;
            _powerUpSystem = powerUpSystem;
            _ghostFitSystem = ghostFitSystem;
            _levelProgressionSystem = levelProgressionSystem;
            _boardView = boardView;
            _trayView = trayView;
            _holdSlotView = holdSlotView;
            _settingsButtonView = settingsButtonView;
            _hubPanelView = hubPanelView;
            _levelPathButtonView = levelPathButtonView;
            _levelPathPanelView = levelPathPanelView;
            _coinSowerPickerView = coinSowerPickerView;
            _runResultView = runResultView;
            _coinTotalHudView = coinTotalHudView;
            _powerUpInventoryView = powerUpInventoryView;
            _objectiveIconContainerView = objectiveIconContainerView;
            _objectiveInfoPopupView = objectiveInfoPopupView;
            _infoPopupView = infoPopupView;
            _infoPopupSystem = infoPopupSystem;
            _cellSkinIconCatalog = cellSkinIconCatalog;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            _pointerPositionAction = new InputAction("PointerPosition", InputActionType.Value, "<Pointer>/position");
            _pointerPressAction = new InputAction("PointerPress", InputActionType.Button, "<Pointer>/press");

            CreateDragLayer();
        }

        private void OnEnable()
        {
            _pointerPressAction.started += OnPressStarted;
            _pointerPressAction.canceled += OnPressReleased;
            _pointerPositionAction.Enable();
            _pointerPressAction.Enable();
        }

        private void OnDisable()
        {
            _pointerPressAction.started -= OnPressStarted;
            _pointerPressAction.canceled -= OnPressReleased;
            _pointerPressAction.Disable();
            _pointerPositionAction.Disable();

            // The arm lives on this View and is driven entirely by presses, so a disabled View can
            // never receive the release that would end it: drop it here, with its reticle and its
            // lifted dock plate, rather than leave a highlight on screen with nothing able to clear it.
            CancelHammerArm();

            // Same reasoning as the arm above: a power-up strip press deferred to release (see
            // OnPressStarted/ResolvePendingPowerUpTap) can never reach its release while disabled either.
            // Left true, the next OnPressReleased after re-enabling — for whatever unrelated gesture is
            // in flight by then — would wrongly resolve as this stale tap instead.
            _hasPendingPowerUpTap = false;
        }

        private void Start()
        {
            if (_settingsModel == null)
            {
                Debug.LogError(
                    $"{nameof(BoardInputView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // The ghost is built on pick-up, so keeping the theme current is enough — there is
            // nothing already on screen to repaint when the theme changes.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _trayModel.SlotChanged += OnTraySlotChanged;
        }

        private void Update()
        {
            // Aiming a power-up and dragging a piece are mutually exclusive: the gate chain in
            // OnPressStarted never starts one while the other is live.
            if (_isAimingPowerUp)
            {
                UpdatePowerUpAim(_pointerPositionAction.ReadValue<Vector2>());
                return;
            }

            if (_isAimingHammer)
            {
                UpdateHammerAim(_pointerPositionAction.ReadValue<Vector2>());
                return;
            }

            if (_draggedSlot < 0)
            {
                return;
            }

            UpdateDrag(_pointerPositionAction.ReadValue<Vector2>());
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_trayModel != null)
            {
                _trayModel.SlotChanged -= OnTraySlotChanged;
            }

            _pointerPositionAction?.Dispose();
            _pointerPressAction?.Dispose();
        }

        /// <summary>
        /// Drops an in-flight drag whose piece was swapped out from under it. The Reroll power-up is the
        /// only thing that can do this today: it rewrites all three dock slots without a placement, so
        /// a drag still running would go on showing a ghost of a piece the tray no longer offers and
        /// would drop *that* piece's shape onto the board.
        /// <para>
        /// Every other write to a dock slot is a consequence of the drag ending — a placement or a park
        /// both clear <see cref="_draggedSlot"/> before touching the model — so those never reach the
        /// cancel below, and this hooks the model rather than the reroll specifically: the invariant is
        /// "the slot being dragged changed underneath us", whatever changed it.
        /// </para>
        /// <para>
        /// On the current single-pointer input path this is unreachable in practice, and deliberately
        /// kept anyway. <c>_pointerPressAction</c> is one button action over <c>&lt;Pointer&gt;/press</c>:
        /// while it is actuated by the press that began the drag it does not fire <c>started</c> again,
        /// so the separate tap a reroll needs cannot happen until the drag's own press is released. This
        /// guards the case a second pointer, a second input path, or any other future caller of
        /// <c>TryRerollTray</c> would open up — none of which should have to know a drag exists.
        /// </para>
        /// </summary>
        private void OnTraySlotChanged(int slotIndex)
        {
            // Same invariant as the drag below, one gesture up: the armed slot changed underneath the
            // arm. Covers the hammer being spent, a new run rewriting the dock, and anything else that
            // could leave the player aiming a slot that no longer holds a hammer.
            if (_armedHammerSlot == slotIndex
                && _trayModel.GetSpecialKind(slotIndex) != SpecialPieceKind.DemolitionHammer)
            {
                CancelHammerArm();
            }

            if (_draggedSlot != slotIndex)
            {
                return;
            }

            _draggedSlot = -1;
            _hasAnchor = false;
            _isOverHoldSlot = false;

            _boardView.ClearPreview();
            _boardView.ClearWouldClearHighlight();
            _holdSlotView.SetHovered(false);
            DestroyGhost();

            // The slot holds a piece again — the new one — so the plate the drag hid must come back.
            _trayView.SetSlotVisible(slotIndex, true);
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
        }

        private void OnPressStarted(InputAction.CallbackContext context)
        {
            Vector2 screenPosition = _pointerPositionAction.ReadValue<Vector2>();

            // This can open from a tap on a power-up's icon inside the Power-up Shop tab, so the hub is
            // still open underneath it. Checked before the hub's own gate so a tap on this card's close
            // cross reaches it instead of being swallowed by the hub's router first (that was a real bug
            // — the close cross did nothing while the shop was open behind it, because the hub gate below
            // used to run first and never returned control here).
            if (_infoPopupView.IsOpen)
            {
                _infoPopupView.HandleTap(screenPosition);
                return;
            }

            // While any overlay is open it is modal and swallows every tap. These four gates are also
            // what keeps the overlays mutually exclusive, and the argument scales with their number
            // rather than pairing them off: *every* "is a panel open, route the tap into it" gate sits
            // above *every* "tapped an icon, open that panel" gate, so an icon tap is only ever reached
            // with all four panels closed. No panel can therefore stack on another, and the single
            // TimerRunSystem menu-pause flag they all share can never be held by two owners at once.
            // The hub counts once here for the five cards it owns: it is their only opener and only
            // router, so the five cannot stack on each other either.
            if (_hubPanelView.IsOpen)
            {
                _hubPanelView.HandleTap(screenPosition);
                return;
            }

            // The level path panel is the one overlay this View does not route into: its trail scrolls,
            // so it is driven by the scene's EventSystem, which reads the same physical pointer
            // independently. Forwarding the press here as well would have both pipelines resolve it.
            // Swallowing it is still this gate's job — that is what keeps the press off the board.
            if (_levelPathPanelView.IsOpen)
            {
                return;
            }

            if (_objectiveInfoPopupView.IsOpen)
            {
                _objectiveInfoPopupView.HandleTap(screenPosition);
                return;
            }

            // The level-start Coin Sower picker. Opened by a node tap in the level path panel rather than
            // by a HUD icon, so it has no "tapped an icon, open that panel" gate below — but it is modal
            // exactly like the others and holds the same single menu-pause flag, so it belongs in this
            // tier. It opens only as the path panel closes, which is what keeps the two from stacking.
            if (_coinSowerPickerView.IsOpen)
            {
                _coinSowerPickerView.HandleTap(screenPosition);
                return;
            }

            // Game over is checked before the HUD icon: the end-of-run card covers the whole screen,
            // so honouring a tap on the icon hidden underneath it would be a hidden hotspot. The card
            // resolves the tap itself — a claimable badge is claimed in place, a button comes back as
            // the action to carry out, and anything else (the scrim, a label) is nothing at all.
            if (_runResultView.IsOpen)
            {
                switch (_runResultView.HandleTap(screenPosition))
                {
                    case RunEndAction.ChangeMode:
                        // Straight to the settings tab, which is where the mode row lives — the button
                        // names a setting, so it opens on the section that holds it rather than on the
                        // hub's front door.
                        _hubPanelView.Open(HubTab.Settings);
                        break;
                    case RunEndAction.NextLevel:
                        _levelProgressionSystem.TryStartPathLevel(_runResultView.NextLevelNumber);
                        break;
                    case RunEndAction.PlayAgain:
                        // "Play again" and Path's "Try again" are the same restart: the mode and, in
                        // Path, the active level are untouched, so the run that starts is the same one.
                        _boardSystem.StartNewRun();
                        break;
                }

                return;
            }

            if (_boardSystem.IsGameOver)
            {
                // The card was not there to take the tap — it only ever happens if the game over
                // arrived before the card had subscribed. Restarting is the one sane thing left.
                _boardSystem.StartNewRun();
                return;
            }

            // The one persistent corner button left, and the only way into the hub: the power-up shop,
            // leaderboard, profile and badges icons that used to sit under it are now tabs inside it.
            if (_settingsButtonView.ContainsScreenPoint(screenPosition))
            {
                _hubPanelView.Open();
                return;
            }

            if (_levelPathButtonView.ContainsScreenPoint(screenPosition))
            {
                _levelPathPanelView.Open();
                return;
            }

            // Same tier as the two icons above: a HUD widget that answers "was I tapped" and opens an
            // overlay. Resolved per slot rather than per widget, because the objective row draws one
            // hotspot per tracked objective and the popup has to know which one.
            if (_objectiveIconContainerView.TryGetTappedObjectiveIndex(screenPosition, out int objectiveIndex))
            {
                _objectiveInfoPopupView.Open(objectiveIndex);
                return;
            }

            // Same tier as the three icons above, for the same reason: a HUD widget that answers "was I
            // tapped" and opens an overlay, resolved before anything an armed power-up or a dock press
            // could claim below. Only the "+" disc's own bounds count — the coin icon and the balance
            // number either side of it stay inert, exactly as they are today (issue #271).
            if (_coinTotalHudView.ContainsPlusDisc(screenPosition))
            {
                _hubPanelView.Open(HubTab.PowerUpShop, PowerUpShopView.ShopTab.Coins);
                return;
            }

            // Before the armed branch below: a tap that lands on the armed kind's own icon is the
            // cancel gesture, not an attempt to aim it at whatever is behind the icon.
            //
            // The action itself (arm/cancel/shop) is deferred to release rather than fired here on
            // press-down: a long-press on the icon is the "show info" gesture instead, and the two must
            // not both fire for the same press. Only the hit-test happens here; see OnPressReleased for
            // where TryHandleTap actually runs on an ordinary short tap.
            if (_powerUpInventoryView.GetSlotIndexAt(screenPosition) >= 0)
            {
                // Reaching for the power-up strip is reaching for something else: drop any armed hammer
                // rather than leave it armed behind an aim flow that would clear its slot highlight and
                // make it invisible.
                CancelHammerArm();

                _hasPendingPowerUpTap = true;
                _pendingPowerUpTapScreenPosition = screenPosition;
                _pendingPowerUpTapStartTime = Time.unscaledTime;
                return;
            }

            // Same tier as the strip, for the same gesture: a tap on a pocket with no Hold charge is
            // the "earn one" tap. With a charge the pocket is a drop target and this returns false, so
            // the press falls through to the drag that would fill it.
            if (_holdSlotView.TryHandleTap(screenPosition))
            {
                CancelHammerArm();
                return;
            }

            // TryHandleTap above only handles the "earn one" gesture (no charge held); a tap on the
            // pocket while a charge IS held used to be a dead press. It is now the manual reopen for the
            // Hold info popup.
            if (_powerUpModel.HoldCount.Value > 0 && _holdSlotView.ContainsScreenPoint(screenPosition))
            {
                CancelHammerArm();
                _infoPopupSystem.Open(InfoPopupSubjectKind.Hold, -1);
                return;
            }

            // Everything below this line is the player reaching for the board or the tray, which is
            // exactly what takes a Ghost Fit suggestion down (see the class remarks). Sited under the
            // inventory gate on purpose: a tap on the strip is the player picking another power-up, not
            // an answer to the hint, and Ghost Fit's own icon needs its second tap to reach the System
            // as the dismiss gesture rather than being pre-empted here.
            if (_powerUpModel.Armed.Value != null)
            {
                _ghostFitSystem.Dismiss();
                BeginPowerUpAim(screenPosition);
                return;
            }

            int slotIndex = _trayView.GetSlotIndexAt(screenPosition);

            // An armed hammer owns the next press: it is aimed at the board like a power-up, so it has
            // to be resolved before the press can be read as picking a piece up.
            if (_armedHammerSlot >= 0)
            {
                // Tapping the armed slot again is the cancel gesture, mirroring PowerUpSystem.CancelArm
                // — implemented here because the arm state lives here and nowhere else.
                if (slotIndex == _armedHammerSlot)
                {
                    CancelHammerArm();
                    return;
                }

                if (slotIndex < 0)
                {
                    _ghostFitSystem.Dismiss();
                    BeginHammerAim(screenPosition);
                    return;
                }

                // Reaching for another dock piece: drop the arm and let the press be handled as the
                // ordinary pick-up it is, rather than swallowing it.
                CancelHammerArm();
            }
            else if (slotIndex >= 0
                && _trayModel.GetSpecialKind(slotIndex) == SpecialPieceKind.DemolitionHammer)
            {
                // The one dock piece that is never dragged. Arming instead of dragging is what keeps a
                // hammer from ever entering a drag the System would only refuse to place.
                _ghostFitSystem.Dismiss();
                ArmHammer(slotIndex);
                return;
            }

            if (slotIndex < 0 || _trayModel.GetPiece(slotIndex) == null)
            {
                // A press on the board itself, or on nothing. Resolve it to a board cell first: a press
                // on a special cell is the manual reopen for its info popup, which takes the place of
                // what used to be a dead press here. Any other press (on nothing, or on an ordinary
                // cell) dismisses a Ghost Fit suggestion exactly as before.
                if (_boardView.TryGetCell(screenPosition, out GridPosition pressedCell))
                {
                    SpecialCellKind cellKind = _boardModel.GetSpecialKind(pressedCell);
                    if (cellKind != SpecialCellKind.None)
                    {
                        _infoPopupSystem.Open(InfoPopupSubjectKind.SpecialCell, (int)cellKind);
                        return;
                    }
                }

                _ghostFitSystem.Dismiss();
                return;
            }

            // Picking up a different piece drops the hint; picking up the suggested one keeps the
            // silhouette on screen to aim at.
            _ghostFitSystem.DismissUnlessSuggestedSlot(slotIndex);

            BeginDrag(slotIndex, screenPosition);
        }

        private void OnPressReleased(InputAction.CallbackContext context)
        {
            if (_hasPendingPowerUpTap)
            {
                ResolvePendingPowerUpTap();
                return;
            }

            if (_isAimingPowerUp)
            {
                ReleasePowerUpAim();
                return;
            }

            if (_isAimingHammer)
            {
                ReleaseHammerAim();
                return;
            }

            if (_draggedSlot < 0)
            {
                return;
            }

            int slotIndex = _draggedSlot;
            _draggedSlot = -1;

            _boardView.ClearPreview();
            _boardView.ClearWouldClearHighlight();
            _holdSlotView.SetHovered(false);
            DestroyGhost();

            // Dropping on the Hold slot is resolved before the board placement and is never ambiguous
            // with it: UpdateDrag drops the board anchor for any frame over the pocket, so the two
            // branches are mutually exclusive even where the pocket overlaps the board's preview margin.
            // The park goes through PowerUpSystem because it costs a Hold charge; a refusal (none held)
            // falls into the same "put the piece back" path as a missed board drop.
            bool consumed = _isOverHoldSlot
                ? _powerUpSystem.TryApplyHold(slotIndex)
                : _hasAnchor && _boardSystem.TryPlacePiece(slotIndex, _currentAnchor);

            if (!consumed)
            {
                _trayView.SetSlotVisible(slotIndex, true);

                // A failed placement snaps the piece back to its slot. A short tap that never really
                // dragged — the manual reopen gesture for a special piece's info popup — looks exactly
                // like this from here: a press on the slot followed by a release with no valid anchor.
                SpecialPieceKind specialKind = _trayModel.GetSpecialKind(slotIndex);
                if (specialKind != SpecialPieceKind.None)
                {
                    Vector2 releasePosition = _pointerPositionAction.ReadValue<Vector2>();
                    float movedDistance = Vector2.Distance(_dragStartScreenPosition, releasePosition);
                    if (movedDistance <= TRAY_TAP_MOVE_TOLERANCE)
                    {
                        _infoPopupSystem.Open(InfoPopupSubjectKind.SpecialPiece, (int)specialKind);
                    }
                }
            }

            _hasAnchor = false;
            _isOverHoldSlot = false;
        }

        /// <summary>
        /// Resolves a press that landed on a power-up strip slot and was deferred rather than acted on
        /// immediately (see <see cref="OnPressStarted"/>). Held past <see cref="POWERUP_LONG_PRESS_SECONDS"/>
        /// with little enough movement is the manual reopen gesture for that slot's info popup; anything
        /// else — a normal short tap, or a tap that moved off the icon — is the ordinary arm/cancel/shop
        /// tap, fired here exactly as it used to fire on press-down.
        /// </summary>
        private void ResolvePendingPowerUpTap()
        {
            _hasPendingPowerUpTap = false;

            Vector2 pressPosition = _pendingPowerUpTapScreenPosition;
            float heldSeconds = Time.unscaledTime - _pendingPowerUpTapStartTime;
            Vector2 releasePosition = _pointerPositionAction.ReadValue<Vector2>();
            float movedDistance = Vector2.Distance(pressPosition, releasePosition);

            if (heldSeconds >= POWERUP_LONG_PRESS_SECONDS && movedDistance <= POWERUP_LONG_PRESS_MOVE_TOLERANCE)
            {
                int slotIndex = _powerUpInventoryView.GetSlotIndexAt(pressPosition);
                if (slotIndex >= 0)
                {
                    _infoPopupSystem.Open(
                        InfoPopupSubjectKind.PowerUp, (int)PowerUpInventoryView.KindAt(slotIndex));
                    return;
                }
            }

            if (_powerUpInventoryView.TryHandleTap(pressPosition, out bool wantsShop) && wantsShop)
            {
                // An empty slot's tap is the way into the shop. Routed here rather than opened by the
                // strip itself, because this View is the one that opens the hub for every other button.
                _hubPanelView.Open(HubTab.PowerUpShop);
            }
        }

        private void BeginDrag(int slotIndex, Vector2 screenPosition)
        {
            _draggedSlot = slotIndex;
            _dragStartScreenPosition = screenPosition;
            _hasAnchor = false;
            _isOverHoldSlot = false;
            _boardSystem.BeginPlacementPreview();
            _trayView.SetSlotVisible(slotIndex, false);
            BuildGhost(
                _trayModel.GetPiece(slotIndex),
                _trayModel.GetColourId(slotIndex),
                _trayModel.GetSpecialKind(slotIndex),
                _trayModel.GetCellSkin(slotIndex));
            UpdateDrag(screenPosition);
        }

        private void UpdateDrag(Vector2 screenPosition)
        {
            Vector2 targetScreen = screenPosition + _dragScreenOffset;

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            bool hasLocal = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _dragLayer, targetScreen, eventCamera, out Vector2 local);

            // BuildGhost bails out when no theme is known yet, so the ghost can legitimately be
            // missing while a drag is in flight.
            if (_ghostRoot != null && hasLocal)
            {
                _ghostRoot.anchoredPosition = local;
            }

            Piece piece = _trayModel.GetPiece(_draggedSlot);
            if (piece == null)
            {
                // Nothing left to drop, so no drop target either: drop the pocket highlight rather than
                // leave the last hovered frame's lit plate on screen for the rest of the drag.
                _isOverHoldSlot = false;
                _holdSlotView.SetHovered(false);
                return;
            }

            // The pocket claims the frame outright. It sits close enough to the board to fall inside the
            // board's generous preview-lead margin, so resolving it first is what keeps one pointer
            // position from meaning two different drops.
            // The whole dragged piece, not its centre point, decides this: the pocket is one small plate
            // and the finger sits offset from the piece it is carrying. The same boolean drives the
            // highlight and the release, so what lights up is what registers.
            _isOverHoldSlot = _holdSlotView.Overlaps(
                BuildGhostScreenBounds(piece, targetScreen, local, hasLocal, eventCamera));
            _holdSlotView.SetHovered(_isOverHoldSlot);
            if (_isOverHoldSlot)
            {
                _hasAnchor = false;
                _boardView.ClearPreview();
                _boardView.ClearWouldClearHighlight();
                return;
            }

            if (!_boardView.TryGetCell(targetScreen, out GridPosition pointerCell))
            {
                _hasAnchor = false;
                _boardView.ClearPreview();
                _boardView.ClearWouldClearHighlight();
                return;
            }

            PieceLayout.GetBounds(piece, out int width, out int height);
            var rawAnchor = new GridPosition(
                pointerCell.X - ((width - 1) / 2),
                pointerCell.Y - ((height - 1) / 2));

            _currentAnchor = _boardSystem.ResolvePlacementAnchor(_draggedSlot, rawAnchor, out bool isValid);
            _hasAnchor = true;

            _boardView.ShowPreview(piece, _currentAnchor, isValid);

            if (!isValid)
            {
                _boardView.ClearWouldClearHighlight();
                return;
            }

            // The query already answers "nothing" for an illegal or barren placement, and the view
            // treats empty lines as "outline nothing" — so this needs no extra guard.
            LineClearResult wouldClear = _boardSystem.GetWouldClearLines(_draggedSlot, _currentAnchor);
            _boardView.ShowWouldClearHighlight(wouldClear.ClearedRows, wouldClear.ClearedColumns);
        }

        private void BeginPowerUpAim(Vector2 screenPosition)
        {
            _isAimingPowerUp = true;
            _hasPowerUpTarget = false;
            _powerUpTargetSlot = -1;
            UpdatePowerUpAim(screenPosition);
        }

        /// <summary>
        /// Previews the cells the armed power-up would hit under the pointer. Unlike a piece drag this
        /// applies no screen offset: there is no ghost for a finger to cover, so the target should be
        /// exactly the cell under the finger.
        /// </summary>
        private void UpdatePowerUpAim(Vector2 screenPosition)
        {
            PowerUpKind? armed = _powerUpModel.Armed.Value;

            // The selection can vanish mid-aim (a run ending underneath it, for instance). Aiming
            // nothing is aiming nothing.
            if (armed == null)
            {
                _isAimingPowerUp = false;
                ClearAimHighlights();
                return;
            }

            // Rotate is aimed at a dock slot, not at a board cell, so it never reaches the board
            // resolution below.
            if (armed.Value == PowerUpKind.Rotate)
            {
                UpdateRotateAim(screenPosition);
                return;
            }

            if (_boardModel == null || !_boardView.TryGetCell(screenPosition, out GridPosition pointerCell))
            {
                _hasPowerUpTarget = false;
                _boardView.ClearPowerUpTargetHighlight();
                return;
            }

            _powerUpTargetCell = pointerCell;
            _hasPowerUpTarget = true;
            _boardView.ShowPowerUpTargetHighlight(
                GetTargetCells(armed.Value, pointerCell),
                IsLegalTarget(armed.Value, pointerCell));
        }

        /// <summary>
        /// Previews which dock slot an armed Rotate would turn. Only a slot holding a piece that has a
        /// distinct rotation lights up: a symmetrical piece, or an empty slot, is a dead tap the System
        /// refuses, so it is shown as no target at all rather than as a target that then does nothing.
        /// </summary>
        private void UpdateRotateAim(Vector2 screenPosition)
        {
            int slotIndex = _trayView.GetSlotIndexAt(screenPosition);
            Piece piece = slotIndex >= 0 ? _trayModel.GetPiece(slotIndex) : null;

            _powerUpTargetSlot = piece != null && !PieceRotator.IsFullySymmetrical(piece) ? slotIndex : -1;

            _hasPowerUpTarget = false;
            _boardView.ClearPowerUpTargetHighlight();
            _trayView.SetAimedSlot(_powerUpTargetSlot);
        }

        /// <summary>Drops both aim previews. The board and the tray can each hold one, and exactly one
        /// kind uses the tray, so clearing them together is what keeps a stale highlight from
        /// outliving the aim that drew it.</summary>
        private void ClearAimHighlights()
        {
            _hasPowerUpTarget = false;
            _powerUpTargetSlot = -1;
            _boardView.ClearPowerUpTargetHighlight();
            _trayView.SetAimedSlot(-1);
        }

        /// <summary>
        /// Whether spending the armed kind here would actually do something. Joker fills an empty
        /// cell, so an occupied one is a dead tap; a colour cleanser is the mirror image — it needs an
        /// occupied cell to have a colour to extract, so an empty one is the dead tap. The three
        /// region-clearing kinds (Bomb/RowClear/ColumnClear) are legal on every cell of the board.
        /// </summary>
        private bool IsLegalTarget(PowerUpKind kind, GridPosition cell)
        {
            if (_boardModel == null || !_boardModel.IsPlayable(cell))
            {
                // A hole is never a legal target for anything: nothing stands on one, so there is
                // nothing to fill, cleanse or destroy there.
                return false;
            }

            if (kind == PowerUpKind.Joker)
            {
                return _boardModel.GetCell(cell) == Board.EMPTY;
            }

            if (kind == PowerUpKind.ColorCleanser)
            {
                return _boardModel.GetCell(cell) != Board.EMPTY;
            }

            return true;
        }

        /// <summary>Spends the armed power-up on the cell under the pointer, if there is one. The
        /// System owns whether that succeeds and clears the selection; the highlight comes off either
        /// way, exactly as the drag preview does on drop.</summary>
        private void ReleasePowerUpAim()
        {
            _isAimingPowerUp = false;

            PowerUpKind? armed = _powerUpModel.Armed.Value;
            bool hasTarget = _hasPowerUpTarget;
            GridPosition target = _powerUpTargetCell;
            int targetSlot = _powerUpTargetSlot;
            ClearAimHighlights();

            if (armed == null)
            {
                return;
            }

            // Rotate resolves to a dock slot rather than a board cell; -1 means the release landed on
            // no slot, or on one there is nothing to turn in.
            if (armed.Value == PowerUpKind.Rotate)
            {
                if (targetSlot >= 0)
                {
                    _powerUpSystem.TryApplyRotate(targetSlot);
                }

                return;
            }

            // Releasing off the board keeps the power-up armed, so the player can simply aim again
            // rather than having to re-select it.
            if (!hasTarget)
            {
                return;
            }

            switch (armed.Value)
            {
                case PowerUpKind.RowClear:
                    _powerUpSystem.TryApplyRowClear(target.Y);
                    break;
                case PowerUpKind.ColumnClear:
                    _powerUpSystem.TryApplyColumnClear(target.X);
                    break;
                case PowerUpKind.Joker:
                    // A refusal here means the cell was already occupied. The System leaves the joker
                    // armed and unspent for exactly that case, so there is nothing to do but let the
                    // player aim again — the highlight has already come off above.
                    _powerUpSystem.TryApplyJoker(target);
                    break;
                case PowerUpKind.ColorCleanser:
                    // Mirror image of Joker's refusal case: an empty target leaves this armed and
                    // unspent, and the player just aims again.
                    _powerUpSystem.TryApplyColorCleanser(target);
                    break;
                default:
                    _powerUpSystem.TryApplyBomb(target);
                    break;
            }
        }

        /// <summary>
        /// Arms the hammer sitting in <paramref name="slotIndex"/> and lifts its dock plate, so the
        /// player can see which slot the next board tap will spend. No System is told: there is nothing
        /// to reserve, because <see cref="BoardSystem.TryUseDemolitionHammer"/> peeks before it spends
        /// and refuses a slot that is no longer a hammer.
        /// </summary>
        private void ArmHammer(int slotIndex)
        {
            _armedHammerSlot = slotIndex;
            _isAimingHammer = false;
            _hasHammerTarget = false;
            _trayView.SetAimedSlot(slotIndex);
        }

        /// <summary>Drops the arm and everything drawn for it. A no-op when nothing is armed, so every
        /// path that could end an arm can call it unconditionally.</summary>
        private void CancelHammerArm()
        {
            if (_armedHammerSlot < 0)
            {
                return;
            }

            _armedHammerSlot = -1;
            _isAimingHammer = false;
            _hasHammerTarget = false;

            // Called from OnDisable as well, which also runs on the way to destruction — by then the
            // other Views may already be gone.
            if (_boardView != null)
            {
                _boardView.ClearPowerUpTargetHighlight();
            }

            if (_trayView != null)
            {
                _trayView.SetAimedSlot(-1);
            }
        }

        private void BeginHammerAim(Vector2 screenPosition)
        {
            _isAimingHammer = true;
            _hasHammerTarget = false;
            UpdateHammerAim(screenPosition);
        }

        /// <summary>
        /// Previews the one cell the armed hammer would destroy. Shares the power-up aim's reticle and
        /// its valid/invalid tint so "this tap will do nothing" reads the same whatever is being aimed,
        /// and applies no screen offset for the same reason that flow does not: there is no ghost for a
        /// finger to cover.
        /// <para>
        /// Legality here is <see cref="PowerUpKind.ColorCleanser"/>'s, not the joker's: the hammer
        /// destroys an occupied cell, so an empty one is the dead tap.
        /// </para>
        /// </summary>
        private void UpdateHammerAim(Vector2 screenPosition)
        {
            // The slot can stop being a hammer mid-aim — spent, or rewritten by a new run underneath
            // the finger. Aiming nothing is aiming nothing.
            if (_boardModel == null || _armedHammerSlot < 0
                || _trayModel.GetSpecialKind(_armedHammerSlot) != SpecialPieceKind.DemolitionHammer)
            {
                CancelHammerArm();
                return;
            }

            if (!_boardView.TryGetCell(screenPosition, out GridPosition pointerCell))
            {
                _hasHammerTarget = false;
                _boardView.ClearPowerUpTargetHighlight();
                return;
            }

            _hammerTargetCell = pointerCell;
            _hasHammerTarget = true;
            _boardView.ShowPowerUpTargetHighlight(
                PowerUpTargetCells.ForDemolitionHammer(
                    _boardModel.Shape, pointerCell, _powerUpTargetBuffer),
                _boardModel.GetCell(pointerCell) != Board.EMPTY);
        }

        /// <summary>
        /// Spends the armed hammer on the cell under the pointer. A refusal — an empty target, or a
        /// release off the board — leaves the hammer armed and its slot still lifted, so the player
        /// simply aims again rather than having to re-arm a life-line they were only ever given once.
        /// That mirrors how a refused joker stays armed.
        /// <para>
        /// A successful use consumes the slot, which raises <see cref="TrayModel.SlotChanged"/> and
        /// drops the arm through <see cref="OnTraySlotChanged"/> — so the arm is cleared by the slot
        /// actually emptying rather than by this method assuming it did.
        /// </para>
        /// </summary>
        private void ReleaseHammerAim()
        {
            _isAimingHammer = false;

            bool hasTarget = _hasHammerTarget;
            GridPosition target = _hammerTargetCell;
            _hasHammerTarget = false;
            _boardView.ClearPowerUpTargetHighlight();

            if (!hasTarget || _armedHammerSlot < 0)
            {
                return;
            }

            _boardSystem.TryUseDemolitionHammer(_armedHammerSlot, target);
        }

        /// <summary>The cells <paramref name="kind"/> would hit at <paramref name="cell"/>, straight
        /// from the Core geometry the application itself uses — so the preview can never over-promise a
        /// region the application would not touch, notably where a bomb's 3x3 is clamped at a board
        /// edge. ColorCleanser is the one kind that deliberately under-promises: its real cleared set
        /// depends on board content, so the preview shows only the aim reticle (see
        /// <see cref="PowerUpTargetCells.ForColorCleanser"/>).</summary>
        private IReadOnlyList<GridPosition> GetTargetCells(PowerUpKind kind, GridPosition cell)
        {
            switch (kind)
            {
                case PowerUpKind.RowClear:
                    return PowerUpTargetCells.ForRow(_boardModel.Shape, cell.Y, _powerUpTargetBuffer);
                case PowerUpKind.ColumnClear:
                    return PowerUpTargetCells.ForColumn(_boardModel.Shape, cell.X, _powerUpTargetBuffer);
                case PowerUpKind.Joker:
                    return PowerUpTargetCells.ForJoker(_boardModel.Shape, cell, _powerUpTargetBuffer);
                case PowerUpKind.ColorCleanser:
                    return PowerUpTargetCells.ForColorCleanser(_boardModel.Shape, cell, _powerUpTargetBuffer);
                default:
                    return PowerUpTargetCells.ForBomb(_boardModel.Shape, cell, _powerUpTargetBuffer);
            }
        }

        private void CreateDragLayer()
        {
            Transform parent = _canvas != null ? _canvas.transform : transform.parent;

            var layerObject = new GameObject("DragLayer", typeof(RectTransform));
            _dragLayer = (RectTransform)layerObject.transform;
            _dragLayer.SetParent(parent, false);
            _dragLayer.anchorMin = Vector2.zero;
            _dragLayer.anchorMax = Vector2.one;
            _dragLayer.offsetMin = Vector2.zero;
            _dragLayer.offsetMax = Vector2.zero;
            _dragLayer.SetAsLastSibling();
        }

        /// <summary>Builds the dragged piece's ghost. Painted through the same seam the dock plate is,
        /// so a special piece looks like itself while it is in the air — a golden 1x1 that turned
        /// ordinary the moment it was picked up would read as having been lost. Also carries the piece's
        /// cell-skin overlay (issue #324) for the same reason: a cake/candy/jelly/fruit piece should not
        /// lose its decoration for the length of the drag.</summary>
        private void BuildGhost(Piece piece, int colourId, SpecialPieceKind specialKind, CellSkinKind cellSkin)
        {
            if (_currentTheme == null)
            {
                return;
            }

            var ghostObject = new GameObject("DragGhost", typeof(RectTransform));
            _ghostRoot = (RectTransform)ghostObject.transform;
            _ghostRoot.SetParent(_dragLayer, false);
            _ghostRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _ghostRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _ghostRoot.pivot = new Vector2(0.5f, 0.5f);
            _ghostRoot.sizeDelta = Vector2.zero;

            float pitch = _boardView.CellSize + _boardView.CellSpacing;
            PieceLayout.GetBounds(piece, out int width, out int height);
            float offsetX = -((width - 1) * pitch) * 0.5f;
            float offsetY = -((height - 1) * pitch) * 0.5f;

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition offset = piece.Offsets[i];
                CellView cell = CellFactory.CreateCell(
                    _ghostRoot,
                    $"GhostCell_{i}",
                    _boardView.CellSize,
                    _boardView.CellInset,
                    _boardView.CellBevelThickness);
                var rect = (RectTransform)cell.transform;
                rect.anchoredPosition = new Vector2(offsetX + (offset.X * pitch), offsetY + (offset.Y * pitch));
                Sprite skinOverlay = _cellSkinIconCatalog != null ? _cellSkinIconCatalog.Find(cellSkin) : null;
                SpecialPieceVisuals.Apply(cell, specialKind, _currentTheme, colourId, skinOverlay);

                // After the colours, never before: SetAlpha writes every layer the look just painted,
                // including the special glyph, so the ghost fades as one block.
                cell.SetAlpha(_ghostAlpha);
                _ghostCells.Add(cell);
            }
        }

        /// <summary>The dragged piece's screen-space bounding box, built from the same cell pitch and
        /// centring <see cref="BuildGhost"/> lays the ghost out with, so the box is exactly what the
        /// player sees in the air. Falls back to the drag point itself when the pointer cannot be mapped
        /// into the drag layer — the same frame the ghost would not move either.</summary>
        private Rect BuildGhostScreenBounds(
            Piece piece, Vector2 targetScreen, Vector2 localCentre, bool hasLocal, Camera eventCamera)
        {
            if (!hasLocal)
            {
                return new Rect(targetScreen, Vector2.zero);
            }

            float pitch = _boardView.CellSize + _boardView.CellSpacing;
            PieceLayout.GetBounds(piece, out int width, out int height);
            float halfWidth = (((width - 1) * pitch) + _boardView.CellSize) * 0.5f;
            float halfHeight = (((height - 1) * pitch) + _boardView.CellSize) * 0.5f;

            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                _dragLayer.TransformPoint(
                    new Vector3(localCentre.x - halfWidth, localCentre.y - halfHeight, 0f)));
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                _dragLayer.TransformPoint(
                    new Vector3(localCentre.x + halfWidth, localCentre.y + halfHeight, 0f)));

            return Rect.MinMaxRect(
                Mathf.Min(bottomLeft.x, topRight.x),
                Mathf.Min(bottomLeft.y, topRight.y),
                Mathf.Max(bottomLeft.x, topRight.x),
                Mathf.Max(bottomLeft.y, topRight.y));
        }

        private void DestroyGhost()
        {
            _ghostCells.Clear();
            if (_ghostRoot != null)
            {
                Destroy(_ghostRoot.gameObject);
                _ghostRoot = null;
            }
        }

    }
}
