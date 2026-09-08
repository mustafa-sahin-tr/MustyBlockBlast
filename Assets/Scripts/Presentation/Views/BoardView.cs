using System;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Renders the 8x8 board as a grid of rounded cells inside a soft card. Subscribes to
    /// <see cref="BoardModel"/>; contains no game logic and never mutates the model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardView : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 120f);
        [SerializeField] private float _cellSize = 108f;
        [SerializeField] private float _cellSpacing = 8f;
        [SerializeField] private float _cardPadding = 24f;

        [Header("Cell style")]
        [SerializeField] private float _cellInset = 3f;
        [SerializeField] private float _cellBevelThickness = 6f;

        [Header("Palette")]
        [SerializeField] private BlockPalette _palette;

        private readonly GridPosition[] _previewCells = new GridPosition[16];

        private RectTransform _rectTransform;
        private Canvas _canvas;
        private CellView[] _cells;
        private BoardModel _boardModel;
        private int _previewCount;
        private float _gridExtent;

        [Inject]
        public void Construct(BoardModel boardModel)
        {
            _boardModel = boardModel;
        }

        internal float CellSize => _cellSize;

        internal float CellSpacing => _cellSpacing;

        private void Awake()
        {
            if (_palette == null)
            {
                _palette = BlockPalette.CreateDefault();
            }

            _rectTransform = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();

            _gridExtent = (Board.SIZE * _cellSize) + ((Board.SIZE - 1) * _cellSpacing);
            float cardExtent = _gridExtent + (_cardPadding * 2f);

            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.sizeDelta = new Vector2(cardExtent, cardExtent);
            _rectTransform.anchoredPosition = _anchoredPosition;

            RectTransform card = CellFactory.CreateCard(
                _rectTransform,
                "BoardCard",
                new Vector2(cardExtent, cardExtent),
                _palette.CardBackground,
                _palette.CardShadow);

            BuildCells(card);
        }

        private void Start()
        {
            if (_boardModel == null)
            {
                Debug.LogError($"{nameof(BoardView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _boardModel.CellChanged += OnCellChanged;
            RedrawAll();
        }

        private void OnDestroy()
        {
            if (_boardModel != null)
            {
                _boardModel.CellChanged -= OnCellChanged;
            }
        }

        /// <summary>Maps a screen point to a board cell. False when the point is off the grid.</summary>
        internal bool TryGetCell(Vector2 screenPosition, out GridPosition cell)
        {
            cell = default;

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rectTransform, screenPosition, eventCamera, out Vector2 local))
            {
                return false;
            }

            float pitch = _cellSize + _cellSpacing;
            float originX = local.x + (_gridExtent * 0.5f);
            float originY = local.y + (_gridExtent * 0.5f);

            int x = Mathf.FloorToInt(originX / pitch);
            int y = Mathf.FloorToInt(originY / pitch);

            if (x < 0 || x >= Board.SIZE || y < 0 || y >= Board.SIZE)
            {
                return false;
            }

            cell = new GridPosition(x, y);
            return true;
        }

        internal Vector3 GetCellWorldPosition(GridPosition cell)
            => _cells[CellIndex(cell)].transform.position;

        /// <summary>Tints the cells a piece would occupy. Safe to call every frame while dragging.</summary>
        internal void ShowPreview(Piece piece, GridPosition anchor, bool isValid)
        {
            ClearPreview();

            if (piece == null)
            {
                return;
            }

            Color tint = isValid ? _palette.ValidPreview : _palette.InvalidPreview;
            for (int i = 0; i < piece.Offsets.Count && _previewCount < _previewCells.Length; i++)
            {
                GridPosition cell = anchor + piece.Offsets[i];
                if (!Board.IsInside(cell))
                {
                    continue;
                }

                _cells[CellIndex(cell)].SetColours(tint, tint);
                _previewCells[_previewCount] = cell;
                _previewCount++;
            }
        }

        internal void ClearPreview()
        {
            for (int i = 0; i < _previewCount; i++)
            {
                GridPosition cell = _previewCells[i];
                ApplyCellColour(cell, _boardModel != null ? _boardModel.GetCell(cell) : Board.EMPTY);
            }

            _previewCount = 0;
        }

        private static int CellIndex(GridPosition cell) => (cell.Y * Board.SIZE) + cell.X;

        private void BuildCells(RectTransform parent)
        {
            _cells = new CellView[Board.SIZE * Board.SIZE];
            float pitch = _cellSize + _cellSpacing;
            float origin = (-_gridExtent * 0.5f) + (_cellSize * 0.5f);

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    CellView cell = CellFactory.CreateCell(
                        parent, $"Cell_{x}_{y}", _cellSize, _cellInset, _cellBevelThickness);
                    var rect = (RectTransform)cell.transform;
                    rect.anchoredPosition = new Vector2(origin + (x * pitch), origin + (y * pitch));
                    cell.SetColours(_palette.EmptyCellFill, _palette.EmptyCellOutline);
                    _cells[(y * Board.SIZE) + x] = cell;
                }
            }
        }

        private void RedrawAll()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var cell = new GridPosition(x, y);
                    ApplyCellColour(cell, _boardModel.GetCell(cell));
                }
            }
        }

        private void OnCellChanged(GridPosition cell, int colourId) => ApplyCellColour(cell, colourId);

        private void ApplyCellColour(GridPosition cell, int colourId)
        {
            CellView view = _cells[CellIndex(cell)];
            if (colourId == Board.EMPTY)
            {
                view.SetColours(_palette.EmptyCellFill, _palette.EmptyCellOutline);
                return;
            }

            view.SetColours(_palette.GetFill(colourId), _palette.GetShade(colourId));
        }
    }
}
