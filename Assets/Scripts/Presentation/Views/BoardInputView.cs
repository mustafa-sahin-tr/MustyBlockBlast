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
    /// three modal overlays (settings, level path, badges), game over, the settings icon, the level
    /// path icon, the badges icon, a
    /// power-up inventory icon, an armed power-up being aimed at the board, and finally a tray piece
    /// being picked up. HUD icons are resolved
    /// here rather than by an EventSystem — this scene has none, and every UI Image in it has its
    /// raycast target off.
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
        private ThemeDefinition _currentTheme;
        private BoardView _boardView;
        private PieceTrayView _trayView;
        private HoldSlotView _holdSlotView;
        private SettingsButtonView _settingsButtonView;
        private SettingsPanelView _settingsPanelView;
        private LevelPathButtonView _levelPathButtonView;
        private LevelPathPanelView _levelPathPanelView;
        private BadgesButtonView _badgesButtonView;
        private BadgesPanelView _badgesPanelView;
        private GameOverView _gameOverView;
        private PowerUpInventoryView _powerUpInventoryView;

        private int _draggedSlot = -1;
        private GridPosition _currentAnchor;
        private bool _hasAnchor;

        /// <summary>Whether this drag frame's ghost sits over the Hold slot. Resolved during the drag
        /// rather than on release, so the drop and the highlight can never disagree about the target.</summary>
        private bool _isOverHoldSlot;

        private bool _isAimingPowerUp;
        private GridPosition _powerUpTargetCell;
        private bool _hasPowerUpTarget;

        [Inject]
        public void Construct(
            BoardSystem boardSystem,
            BoardModel boardModel,
            TrayModel trayModel,
            SettingsModel settingsModel,
            PowerUpModel powerUpModel,
            PowerUpSystem powerUpSystem,
            BoardView boardView,
            PieceTrayView trayView,
            HoldSlotView holdSlotView,
            SettingsButtonView settingsButtonView,
            SettingsPanelView settingsPanelView,
            LevelPathButtonView levelPathButtonView,
            LevelPathPanelView levelPathPanelView,
            BadgesButtonView badgesButtonView,
            BadgesPanelView badgesPanelView,
            GameOverView gameOverView,
            PowerUpInventoryView powerUpInventoryView)
        {
            _boardSystem = boardSystem;
            _boardModel = boardModel;
            _trayModel = trayModel;
            _settingsModel = settingsModel;
            _powerUpModel = powerUpModel;
            _powerUpSystem = powerUpSystem;
            _boardView = boardView;
            _trayView = trayView;
            _holdSlotView = holdSlotView;
            _settingsButtonView = settingsButtonView;
            _settingsPanelView = settingsPanelView;
            _levelPathButtonView = levelPathButtonView;
            _levelPathPanelView = levelPathPanelView;
            _badgesButtonView = badgesButtonView;
            _badgesPanelView = badgesPanelView;
            _gameOverView = gameOverView;
            _powerUpInventoryView = powerUpInventoryView;
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

            if (_draggedSlot < 0)
            {
                return;
            }

            UpdateDrag(_pointerPositionAction.ReadValue<Vector2>());
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            _pointerPositionAction?.Dispose();
            _pointerPressAction?.Dispose();
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

            // While any overlay is open it is modal and swallows every tap. These three gates are also
            // what keeps the overlays mutually exclusive, and the argument scales with their number
            // rather than pairing them off: *every* "is a panel open, route the tap into it" gate sits
            // above *every* "tapped an icon, open that panel" gate, so an icon tap is only ever reached
            // with all three panels closed. No panel can therefore stack on another, and the single
            // TimerRunSystem menu-pause flag all three share can never be held by two owners at once.
            if (_settingsPanelView.IsOpen)
            {
                _settingsPanelView.HandleTap(screenPosition);
                return;
            }

            if (_levelPathPanelView.IsOpen)
            {
                _levelPathPanelView.HandleTap(screenPosition);
                return;
            }

            if (_badgesPanelView.IsOpen)
            {
                _badgesPanelView.HandleTap(screenPosition);
                return;
            }

            // Game over is checked before the HUD icon: the game-over card covers the whole screen,
            // so honouring a tap on the icon hidden underneath it would be a hidden hotspot. The
            // "change mode" link is the one exception — it is drawn on the card itself, so it is
            // visible and must win over the card-wide restart tap.
            if (_boardSystem.IsGameOver)
            {
                if (_gameOverView.ContainsChangeModeScreenPoint(screenPosition))
                {
                    _settingsPanelView.Open();
                    return;
                }

                _boardSystem.StartNewRun();
                return;
            }

            if (_settingsButtonView.ContainsScreenPoint(screenPosition))
            {
                _settingsPanelView.Open();
                return;
            }

            if (_levelPathButtonView.ContainsScreenPoint(screenPosition))
            {
                _levelPathPanelView.Open();
                return;
            }

            if (_badgesButtonView.ContainsScreenPoint(screenPosition))
            {
                _badgesPanelView.Open();
                return;
            }

            // Before the armed branch below: a tap that lands on the armed kind's own icon is the
            // cancel gesture, not an attempt to aim it at whatever is behind the icon.
            if (_powerUpInventoryView.TryHandleTap(screenPosition))
            {
                return;
            }

            if (_powerUpModel.Armed.Value != null)
            {
                BeginPowerUpAim(screenPosition);
                return;
            }

            int slotIndex = _trayView.GetSlotIndexAt(screenPosition);
            if (slotIndex < 0 || _trayModel.GetPiece(slotIndex) == null)
            {
                return;
            }

            BeginDrag(slotIndex, screenPosition);
        }

        private void OnPressReleased(InputAction.CallbackContext context)
        {
            if (_isAimingPowerUp)
            {
                ReleasePowerUpAim();
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
            bool consumed = _isOverHoldSlot
                ? _boardSystem.TryHoldPiece(slotIndex)
                : _hasAnchor && _boardSystem.TryPlacePiece(slotIndex, _currentAnchor);

            if (!consumed)
            {
                _trayView.SetSlotVisible(slotIndex, true);
            }

            _hasAnchor = false;
            _isOverHoldSlot = false;
        }

        private void BeginDrag(int slotIndex, Vector2 screenPosition)
        {
            _draggedSlot = slotIndex;
            _hasAnchor = false;
            _isOverHoldSlot = false;
            _boardSystem.BeginPlacementPreview();
            _trayView.SetSlotVisible(slotIndex, false);
            BuildGhost(_trayModel.GetPiece(slotIndex), _trayModel.GetColourId(slotIndex));
            UpdateDrag(screenPosition);
        }

        private void UpdateDrag(Vector2 screenPosition)
        {
            Vector2 targetScreen = screenPosition + _dragScreenOffset;

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            // BuildGhost bails out when no theme is known yet, so the ghost can legitimately be
            // missing while a drag is in flight.
            if (_ghostRoot != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _dragLayer, targetScreen, eventCamera, out Vector2 local))
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
            _isOverHoldSlot = _holdSlotView.ContainsScreenPoint(targetScreen);
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
                _hasPowerUpTarget = false;
                _boardView.ClearPowerUpTargetHighlight();
                return;
            }

            if (!_boardView.TryGetCell(screenPosition, out GridPosition pointerCell))
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
        /// Whether spending the armed kind here would actually do something. Joker fills an empty
        /// cell, so an occupied one is a dead tap; a colour cleanser is the mirror image — it needs an
        /// occupied cell to have a colour to extract, so an empty one is the dead tap. The three
        /// region-clearing kinds (Bomb/RowClear/ColumnClear) are legal on every cell of the board.
        /// </summary>
        private bool IsLegalTarget(PowerUpKind kind, GridPosition cell)
        {
            if (kind == PowerUpKind.Joker)
            {
                return _boardModel != null && _boardModel.GetCell(cell) == Board.EMPTY;
            }

            if (kind == PowerUpKind.ColorCleanser)
            {
                return _boardModel != null && _boardModel.GetCell(cell) != Board.EMPTY;
            }

            return true;
        }

        /// <summary>Spends the armed power-up on the cell under the pointer, if there is one. The
        /// System owns whether that succeeds and clears the selection; the highlight comes off either
        /// way, exactly as the drag preview does on drop.</summary>
        private void ReleasePowerUpAim()
        {
            _isAimingPowerUp = false;
            _boardView.ClearPowerUpTargetHighlight();

            PowerUpKind? armed = _powerUpModel.Armed.Value;
            bool hasTarget = _hasPowerUpTarget;
            GridPosition target = _powerUpTargetCell;
            _hasPowerUpTarget = false;

            // Releasing off the board keeps the power-up armed, so the player can simply aim again
            // rather than having to re-select it.
            if (armed == null || !hasTarget)
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
                    return PowerUpTargetCells.ForRow(cell.Y, _powerUpTargetBuffer);
                case PowerUpKind.ColumnClear:
                    return PowerUpTargetCells.ForColumn(cell.X, _powerUpTargetBuffer);
                case PowerUpKind.Joker:
                    return PowerUpTargetCells.ForJoker(cell, _powerUpTargetBuffer);
                case PowerUpKind.ColorCleanser:
                    return PowerUpTargetCells.ForColorCleanser(cell, _powerUpTargetBuffer);
                default:
                    return PowerUpTargetCells.ForBomb(cell, _powerUpTargetBuffer);
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

        private void BuildGhost(Piece piece, int colourId)
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
                cell.SetEmbossedColours(
                    _currentTheme.GetFill(colourId),
                    _currentTheme.GetHighlight(colourId),
                    _currentTheme.GetShade(colourId));
                cell.SetAlpha(_ghostAlpha);
                _ghostCells.Add(cell);
            }
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
