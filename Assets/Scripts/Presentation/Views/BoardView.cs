using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
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

        [Header("Line Clear Fade")]
        [Tooltip("Seconds a cleared cell takes to fade from its colour to fully transparent.")]
        [SerializeField] private float _fadeDuration = 0.2f;

        [Tooltip("Seconds of white flash on a cell that sits on both a cleared row and a cleared column.")]
        [SerializeField] private float _intersectionFlashDuration = 0.08f;

        private static readonly Color FlashTint = Color.white;

        private readonly GridPosition[] _previewCells = new GridPosition[16];
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private readonly bool[] _rowClearMask = new bool[Board.SIZE];
        private readonly bool[] _columnClearMask = new bool[Board.SIZE];

        private RectTransform _rectTransform;
        private Canvas _canvas;
        private CellView[] _cells;
        private Image _cardImage;
        private Image _cardShadowImage;

        // Per-cell bookkeeping, parallel to _cells and indexed by CellIndex.
        private int[] _cellColourIds;
        private int[] _cellGenerations;
        private int[] _pendingColourIds;
        private int[] _pendingGenerations;
        private bool[] _cellPending;

        private BoardModel _boardModel;
        private SettingsModel _settingsModel;
        private ThemeDefinition _currentTheme;
        private ISubscriber<LinesClearedMessage> _linesClearedSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;

        private CancellationToken _destroyToken;
        private int _previewCount;
        private float _gridExtent;
        private bool _isDestroyed;

        [Inject]
        public void Construct(
            BoardModel boardModel,
            SettingsModel settingsModel,
            ISubscriber<LinesClearedMessage> linesClearedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _boardModel = boardModel;
            _settingsModel = settingsModel;
            _linesClearedSubscriber = linesClearedSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
        }

        internal float CellSize => _cellSize;

        internal float CellSpacing => _cellSpacing;

        internal float CellInset => _cellInset;

        internal float CellBevelThickness => _cellBevelThickness;

        private void Awake()
        {
            _destroyToken = this.GetCancellationTokenOnDestroy();

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
                out _cardImage,
                out _cardShadowImage);

            BuildCells(BuildCellLayer(card));
        }

        private void Start()
        {
            if (_boardModel == null || _settingsModel == null)
            {
                Debug.LogError($"{nameof(BoardView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Subscribed first so _currentTheme is set before anything below paints a cell.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _boardModel.CellChanged += OnCellChanged;
            _linesClearedSubscriber.Subscribe(OnLinesCleared).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);

            RedrawAll();
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _disposables.Dispose();

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

            if (piece == null || _currentTheme == null)
            {
                return;
            }

            Color tint = isValid ? _currentTheme.ValidPreview : _currentTheme.InvalidPreview;
            for (int i = 0; i < piece.Offsets.Count && _previewCount < _previewCells.Length; i++)
            {
                GridPosition cell = anchor + piece.Offsets[i];
                if (!Board.IsInside(cell))
                {
                    continue;
                }

                int index = CellIndex(cell);

                // A cell still fading out from a line clear must drop the fade the instant the
                // preview claims it, otherwise the tint would be drawn at partial alpha.
                CancelFade(index);

                _cells[index].SetColours(tint, tint);
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

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        /// <summary>Own nested Canvas for the cell grid: the per-frame colour/alpha writes from
        /// clear fades only rebuild this canvas, not the shared UICanvas that also holds the score
        /// and the tray.</summary>
        private static RectTransform BuildCellLayer(RectTransform parent)
        {
            var layerObject = new GameObject("CellLayer", typeof(RectTransform), typeof(Canvas));
            var layerRect = (RectTransform)layerObject.transform;
            layerRect.SetParent(parent, false);
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;
            return layerRect;
        }

        private void BuildCells(RectTransform parent)
        {
            int cellCount = Board.SIZE * Board.SIZE;
            _cells = new CellView[cellCount];
            _cellColourIds = new int[cellCount];
            _cellGenerations = new int[cellCount];
            _pendingColourIds = new int[cellCount];
            _pendingGenerations = new int[cellCount];
            _cellPending = new bool[cellCount];

            for (int i = 0; i < cellCount; i++)
            {
                _cellColourIds[i] = Board.EMPTY;
                _pendingColourIds[i] = Board.EMPTY;
            }

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

                    // Cells are built in Awake, before the theme is known; the theme subscription in
                    // Start paints them (and repaints them on every later theme switch).
                    cell.SetColours(Color.clear, Color.clear);
                    _cells[(y * Board.SIZE) + x] = cell;
                }
            }
        }

        /// <summary>Forces every cell back to the model's state, opaque and with no fade in flight.</summary>
        private void RedrawAll()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var cell = new GridPosition(x, y);
                    int index = CellIndex(cell);

                    _cellGenerations[index]++;
                    _cellPending[index] = false;

                    int colourId = _boardModel.GetCell(cell);
                    _cellColourIds[index] = colourId;
                    ApplyCellColour(cell, colourId);
                    _cells[index].SetAlpha(1f);
                }
            }
        }

        private void OnCellChanged(GridPosition cell, int colourId)
        {
            int index = CellIndex(cell);

            if (colourId != Board.EMPTY)
            {
                // A new piece always wins over an in-flight fade: invalidate it and draw opaque now.
                _cellGenerations[index]++;
                _cellPending[index] = false;
                _cellColourIds[index] = colourId;
                ApplyCellColour(cell, colourId);
                _cells[index].SetAlpha(1f);
                return;
            }

            // NotifyCleared raises this twice for a row/column intersection cell — the first
            // notification owns the pre-clear colour, the second must not overwrite it.
            if (_cellPending[index])
            {
                return;
            }

            // Hold the pre-clear look on screen; the fade started by OnLinesCleared owns it from
            // here. If no LinesClearedMessage follows (ClearAll), OnRunStarted flushes it.
            _cellPending[index] = true;
            _pendingColourIds[index] = _cellColourIds[index];
            _pendingGenerations[index] = ++_cellGenerations[index];
            _cellColourIds[index] = Board.EMPTY;
        }

        private void OnLinesCleared(LinesClearedMessage message)
        {
            Array.Clear(_rowClearMask, 0, _rowClearMask.Length);
            Array.Clear(_columnClearMask, 0, _columnClearMask.Length);

            IReadOnlyList<int> rows = message.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                int y = rows[i];
                if (y >= 0 && y < Board.SIZE)
                {
                    _rowClearMask[y] = true;
                }
            }

            IReadOnlyList<int> columns = message.Columns;
            for (int i = 0; i < columns.Count; i++)
            {
                int x = columns[i];
                if (x >= 0 && x < Board.SIZE)
                {
                    _columnClearMask[x] = true;
                }
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    bool inRow = _rowClearMask[y];
                    bool inColumn = _columnClearMask[x];
                    if (!inRow && !inColumn)
                    {
                        continue;
                    }

                    int index = (y * Board.SIZE) + x;
                    if (!_cellPending[index] || _pendingGenerations[index] != _cellGenerations[index])
                    {
                        continue;
                    }

                    PlayClearAsync(new GridPosition(x, y), index, _cellGenerations[index], inRow && inColumn)
                        .Forget();
                }
            }
        }

        /// <summary>Safety net for <c>BoardModel.ClearAll</c>, which empties cells without ever
        /// publishing a <see cref="LinesClearedMessage"/> to claim them.</summary>
        private void OnRunStarted(RunStartedMessage message) => RedrawAll();

        /// <summary>Invalidates any fade on a cell and restores it to full opacity.</summary>
        private void CancelFade(int index)
        {
            if (!_cellPending[index])
            {
                return;
            }

            _cellPending[index] = false;
            _cellGenerations[index]++;
            _cells[index].SetAlpha(1f);
        }

        private async UniTaskVoid PlayClearAsync(GridPosition cell, int index, int generation, bool isIntersection)
        {
            CellView view = _cells[index];
            int colourId = _pendingColourIds[index];

            try
            {
                if (isIntersection)
                {
                    float flashDuration = Mathf.Max(0.01f, _intersectionFlashDuration);
                    float flashElapsed = 0f;

                    while (flashElapsed < flashDuration)
                    {
                        if (_cellGenerations[index] != generation)
                        {
                            return;
                        }

                        // Triangle ramp: colour goes to white and back over the flash window.
                        float blend = 1f - Mathf.Abs(((flashElapsed / flashDuration) * 2f) - 1f);
                        ApplyClearTint(view, colourId, blend);

                        await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                        flashElapsed += Time.unscaledDeltaTime;
                    }

                    if (_cellGenerations[index] != generation)
                    {
                        return;
                    }

                    ApplyClearTint(view, colourId, 0f);
                }

                float fadeDuration = Mathf.Max(0.01f, _fadeDuration);
                float fadeElapsed = 0f;

                while (fadeElapsed < fadeDuration)
                {
                    if (_cellGenerations[index] != generation)
                    {
                        return;
                    }

                    view.SetAlpha(1f - EaseOutCubic(fadeElapsed / fadeDuration));

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    fadeElapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-fade — nothing left to restore.
                return;
            }

            // Only settle the cell when nothing newer claimed it, otherwise this would stomp the
            // colour of a piece that was placed here while the fade was running.
            if (_isDestroyed || _cellGenerations[index] != generation)
            {
                return;
            }

            _cellPending[index] = false;
            ApplyCellColour(cell, Board.EMPTY);
            view.SetAlpha(1f);
        }

        /// <summary>Adopts a new theme: repaints the card and every cell already on the board, so a
        /// mid-run theme switch changes the pieces that are already placed, not just future ones.</summary>
        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;

            RepaintCells();
        }

        private void RepaintCells()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    int index = (y * Board.SIZE) + x;

                    // A cell fading out from a line clear still shows its pre-clear colour; repaint
                    // that one instead of the (already empty) settled colour. The fade in flight
                    // restores the alpha on its next tick.
                    int colourId = _cellPending[index] ? _pendingColourIds[index] : _cellColourIds[index];
                    ApplyCellColour(new GridPosition(x, y), colourId);
                }
            }
        }

        private void ApplyCellColour(GridPosition cell, int colourId)
        {
            if (_currentTheme == null)
            {
                return;
            }

            CellView view = _cells[CellIndex(cell)];
            if (colourId == Board.EMPTY)
            {
                view.SetColours(_currentTheme.EmptyCellFill, _currentTheme.EmptyCellOutline);
                return;
            }

            view.SetEmbossedColours(
                _currentTheme.GetFill(colourId),
                _currentTheme.GetHighlight(colourId),
                _currentTheme.GetShade(colourId));
        }

        /// <summary>Draws a clearing cell blended towards the flash tint, keeping it on the same
        /// layer set it was already showing so the fade never switches looks mid-flight.</summary>
        private void ApplyClearTint(CellView view, int colourId, float blend)
        {
            if (_currentTheme == null)
            {
                return;
            }

            if (colourId == Board.EMPTY)
            {
                view.SetColours(
                    Color.Lerp(_currentTheme.EmptyCellFill, FlashTint, blend),
                    Color.Lerp(_currentTheme.EmptyCellOutline, FlashTint, blend));
                return;
            }

            view.SetEmbossedColours(
                Color.Lerp(_currentTheme.GetFill(colourId), FlashTint, blend),
                Color.Lerp(_currentTheme.GetHighlight(colourId), FlashTint, blend),
                Color.Lerp(_currentTheme.GetShade(colourId), FlashTint, blend));
        }
    }
}
