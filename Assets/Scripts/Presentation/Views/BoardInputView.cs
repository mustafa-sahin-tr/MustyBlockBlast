using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
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
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardInputView : MonoBehaviour
    {
        [Header("Drag feel")]
        [Tooltip("Screen-space offset applied to the dragged piece so a finger does not cover it.")]
        [SerializeField] private Vector2 _dragScreenOffset = new Vector2(0f, 150f);
        [SerializeField] private float _ghostAlpha = 0.9f;

        [Header("Palette")]
        [SerializeField] private BlockPalette _palette;

        private readonly List<CellView> _ghostCells = new List<CellView>(9);

        private InputAction _pointerPositionAction;
        private InputAction _pointerPressAction;
        private RectTransform _dragLayer;
        private RectTransform _ghostRoot;
        private Canvas _canvas;

        private BoardSystem _boardSystem;
        private TrayModel _trayModel;
        private BoardView _boardView;
        private PieceTrayView _trayView;

        private int _draggedSlot = -1;
        private GridPosition _currentAnchor;
        private bool _hasAnchor;

        [Inject]
        public void Construct(BoardSystem boardSystem, TrayModel trayModel, BoardView boardView, PieceTrayView trayView)
        {
            _boardSystem = boardSystem;
            _trayModel = trayModel;
            _boardView = boardView;
            _trayView = trayView;
        }

        private void Awake()
        {
            if (_palette == null)
            {
                _palette = BlockPalette.CreateDefault();
            }

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

        private void Update()
        {
            if (_draggedSlot < 0)
            {
                return;
            }

            UpdateDrag(_pointerPositionAction.ReadValue<Vector2>());
        }

        private void OnDestroy()
        {
            _pointerPositionAction?.Dispose();
            _pointerPressAction?.Dispose();
        }

        private void OnPressStarted(InputAction.CallbackContext context)
        {
            Vector2 screenPosition = _pointerPositionAction.ReadValue<Vector2>();

            if (_boardSystem.IsGameOver)
            {
                _boardSystem.StartNewRun();
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
            if (_draggedSlot < 0)
            {
                return;
            }

            int slotIndex = _draggedSlot;
            _draggedSlot = -1;

            _boardView.ClearPreview();
            DestroyGhost();

            bool placed = _hasAnchor && _boardSystem.TryPlacePiece(slotIndex, _currentAnchor);
            if (!placed)
            {
                _trayView.SetSlotVisible(slotIndex, true);
            }

            _hasAnchor = false;
        }

        private void BeginDrag(int slotIndex, Vector2 screenPosition)
        {
            _draggedSlot = slotIndex;
            _hasAnchor = false;
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

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _dragLayer, targetScreen, eventCamera, out Vector2 local))
            {
                _ghostRoot.anchoredPosition = local;
            }

            Piece piece = _trayModel.GetPiece(_draggedSlot);
            if (piece == null)
            {
                return;
            }

            if (!_boardView.TryGetCell(targetScreen, out GridPosition pointerCell))
            {
                _hasAnchor = false;
                _boardView.ClearPreview();
                return;
            }

            PieceLayout.GetBounds(piece, out int width, out int height);
            _currentAnchor = new GridPosition(
                pointerCell.X - ((width - 1) / 2),
                pointerCell.Y - ((height - 1) / 2));
            _hasAnchor = true;

            _boardView.ShowPreview(piece, _currentAnchor, _boardSystem.CanPlace(_draggedSlot, _currentAnchor));
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
                    _palette.GetFill(colourId), _palette.GetHighlight(colourId), _palette.GetShade(colourId));
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
