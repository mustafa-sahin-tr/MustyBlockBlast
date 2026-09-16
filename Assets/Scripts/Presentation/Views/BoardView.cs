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
        [SerializeField] private float _cellBevelThickness = 12f;

        [Header("Drag Preview")]
        [Tooltip("Fraction of a cell's size added as a margin around the grid rect where the placement preview starts appearing, ahead of the pointer strictly entering the grid.")]
        [SerializeField] private float _previewLeadMarginFraction = 0.5f;

        [Header("Line Clear Fade")]
        [Tooltip("Seconds a cleared cell takes to fade from its colour to fully transparent.")]
        [SerializeField] private float _fadeDuration = 0.2f;

        [Tooltip("Seconds of white flash on a cell that sits on both a cleared row and a cleared column.")]
        [SerializeField] private float _intersectionFlashDuration = 0.08f;

        private static readonly Color FlashTint = Color.white;

        /// <summary>Colour the special-cell icon is drawn in. Fixed rather than themed: it is a
        /// readability mark, not decoration, and a warm near-white reads on every theme's block fills
        /// without each theme having to author (and keep legible) a colour for it.</summary>
        private static readonly Color SpecialIconTint = new Color(1f, 0.95f, 0.72f, 1f);

        /// <summary>
        /// Colour a <see cref="SpecialCellKind.ScoreGem"/>'s icon is drawn in. Fixed and unthemed for
        /// the reason <see cref="SpecialIconTint"/> is, but deliberately a different hue from it: a gem
        /// destroys nothing and instead multiplies what the player scores, so it must be told apart at a
        /// glance from the kinds that do blow a hole in the board — reaching for one is a very different
        /// decision from reaching for a core or a laser.
        /// <para>
        /// The sprite is shared with them on purpose (<c>UiSpriteFactory.Starburst</c>): one sprite for
        /// every icon is what keeps an icon on any number of cells batching with the rest of the board,
        /// so the kinds are separated by tint rather than by a second texture.
        /// </para>
        /// </summary>
        private static readonly Color ScoreGemIconTint = new Color(0.44f, 1f, 0.72f, 1f);

        /// <summary>
        /// How far a hole cell's fill is pushed towards black relative to an empty cell's, and how far
        /// its alpha is pulled down. Derived from the active theme rather than authored per theme, and
        /// deliberately a placeholder: a hole is "not part of the board", and until it has real art it
        /// reads as a recessed gap that no theme has to author a colour for and none can make look
        /// mistakable for a cell a piece could be dropped on.
        /// <para>
        /// See the Developer Action Required note in this issue's report: replacing this with a proper
        /// hole sprite is an Editor asset job an agent cannot do, and this keeps the code path complete
        /// and testable until it is done.
        /// </para>
        /// </summary>
        private const float HOLE_DARKEN = 0.62f;
        private const float HOLE_ALPHA = 0.55f;

        private readonly GridPosition[] _previewCells = new GridPosition[16];

        // Its own claim set, kept apart from the drag preview's: the silhouette and a drag can be on
        // screen together, so neither may restore the other's cells.
        private readonly GridPosition[] _ghostFitCells = new GridPosition[16];

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        // A power-up never targets more than a full line or a 3x3 block, but the array is sized to
        // the board so a future kind with a wider footprint cannot silently truncate its preview.
        // Allocated with the grid (see BuildCells), because the board's dimensions are not known until
        // the model has been injected.
        private GridPosition[] _powerUpTargetCells;

        private bool[] _rowClearMask;
        private bool[] _columnClearMask;

        // Which cells currently wear the would-clear outline, and the set being built for this
        // frame. Two fixed masks so the per-frame update is a diff against what is already on
        // screen: no allocation, and only the cells that actually changed are touched.
        private bool[] _highlightMask;
        private bool[] _pendingHighlightMask;

        /// <summary>Which cells are holes, mirrored from the model's shape once at build time and
        /// indexed like every other per-cell array. Mirrored rather than re-queried per repaint because
        /// the shape cannot change while the grid it built exists, and a repaint touches every cell.</summary>
        private bool[] _holeMask;

        /// <summary>The board's dimensions, taken from the model. Default to the standard square only
        /// so a grid built before injection has something to build; <see cref="EnsureBuilt"/> rebuilds
        /// the grid if the model turns out to describe a different shape.</summary>
        private int _width = Board.SIZE;
        private int _height = Board.SIZE;

        private RectTransform _rectTransform;
        private RectTransform _cardRoot;
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

        // The special kind each cell has settled on, parallel to the colour bookkeeping above. A cell
        // that is fading out has already settled on None and keeps showing its icon until the fade
        // ends, so the icon follows its block out instead of vanishing the instant the model empties
        // the cell — which is why no "pending kind" counterpart is needed.
        private SpecialCellKind[] _cellSpecialKinds;

        private BoardModel _boardModel;
        private SettingsModel _settingsModel;
        private ThemeDefinition _currentTheme;
        private ISubscriber<LinesClearedMessage> _linesClearedSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private ISubscriber<PowerUpAppliedMessage> _powerUpAppliedSubscriber;
        private ISubscriber<ExplosiveCoreDetonatedMessage> _explosiveCoreDetonatedSubscriber;
        private ISubscriber<LaserFiredMessage> _laserFiredSubscriber;

        private CancellationToken _destroyToken;
        private int _previewCount;
        private int _ghostFitCount;
        private int _powerUpTargetCount;
        private float _gridExtent;
        private bool _isDestroyed;
        private bool _hasHighlight;

        [Inject]
        public void Construct(
            BoardModel boardModel,
            SettingsModel settingsModel,
            ISubscriber<LinesClearedMessage> linesClearedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<PowerUpAppliedMessage> powerUpAppliedSubscriber,
            ISubscriber<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedSubscriber,
            ISubscriber<LaserFiredMessage> laserFiredSubscriber)
        {
            _explosiveCoreDetonatedSubscriber = explosiveCoreDetonatedSubscriber;
            _laserFiredSubscriber = laserFiredSubscriber;
            _boardModel = boardModel;
            _settingsModel = settingsModel;
            _linesClearedSubscriber = linesClearedSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
            _powerUpAppliedSubscriber = powerUpAppliedSubscriber;
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

            EnsureBuilt();
        }

        /// <summary>
        /// Builds the card and the cell grid for the model's board shape, once.
        /// <para>
        /// Called from <c>Awake</c> and again from <c>Start</c> because injection order against
        /// <c>Awake</c> is not guaranteed: the first call may have to fall back to the standard square,
        /// and the second — which always has the model — rebuilds only if the real shape turns out to
        /// differ. On the standard board the second call is a no-op, so nothing about the existing
        /// build path changes.
        /// </para>
        /// </summary>
        private void EnsureBuilt()
        {
            int width = _boardModel != null ? _boardModel.Width : Board.SIZE;
            int height = _boardModel != null ? _boardModel.Height : Board.SIZE;

            if (_cells != null && _width == width && _height == height)
            {
                return;
            }

            if (_cardRoot != null)
            {
                Destroy(_cardRoot.gameObject);
                _cardRoot = null;
            }

            _width = width;
            _height = height;

            float gridWidth = (_width * _cellSize) + ((_width - 1) * _cellSpacing);
            float gridHeight = (_height * _cellSize) + ((_height - 1) * _cellSpacing);

            // One extent for both axes on a square board, which is every board today. A non-square one
            // takes the larger, so the card still frames the whole grid.
            _gridExtent = Mathf.Max(gridWidth, gridHeight);
            float cardExtent = _gridExtent + (_cardPadding * 2f);

            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.sizeDelta = new Vector2(cardExtent, cardExtent);
            _rectTransform.anchoredPosition = _anchoredPosition;

            _cardRoot = CellFactory.CreateCard(
                _rectTransform,
                "BoardCard",
                new Vector2(cardExtent, cardExtent),
                out _cardImage,
                out _cardShadowImage);

            BuildCells(BuildCellLayer(_cardRoot));
        }

        private void Start()
        {
            if (_boardModel == null || _settingsModel == null)
            {
                Debug.LogError($"{nameof(BoardView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // The model is guaranteed injected by now, so this adopts the real board shape if Awake
            // had to guess at it.
            EnsureBuilt();

            // Subscribed first so _currentTheme is set before anything below paints a cell.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _boardModel.CellChanged += OnCellChanged;
            _boardModel.SpecialKindChanged += OnSpecialKindChanged;
            _linesClearedSubscriber.Subscribe(OnLinesCleared).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
            _powerUpAppliedSubscriber.Subscribe(OnPowerUpApplied).AddTo(_disposables);

            // Same handler as a power-up's: a blast empties a region rather than whole lines, so its
            // cells need claiming exactly the way a power-up's cleared region does.
            _explosiveCoreDetonatedSubscriber.Subscribe(OnExplosiveCoreDetonated).AddTo(_disposables);

            // Same again for a laser's wipe, which empties a line whether or not it was full — so no
            // LinesClearedMessage describes it either.
            _laserFiredSubscriber.Subscribe(OnLaserFired).AddTo(_disposables);

            RedrawAll();
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _disposables.Dispose();

            if (_boardModel != null)
            {
                _boardModel.CellChanged -= OnCellChanged;
                _boardModel.SpecialKindChanged -= OnSpecialKindChanged;
            }
        }

        /// <summary>Maps a screen point to a board cell. Points within <see cref="_previewLeadMarginFraction"/>
        /// of a cell's width/height outside the grid still resolve to the nearest edge cell, so the
        /// placement preview can appear slightly ahead of the pointer entering the grid. False when
        /// the point is farther than that margin from the grid.</summary>
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

            float halfExtent = _gridExtent * 0.5f;
            float margin = _cellSize * _previewLeadMarginFraction;

            if (local.x < -halfExtent - margin || local.x > halfExtent + margin
                || local.y < -halfExtent - margin || local.y > halfExtent + margin)
            {
                return false;
            }

            float pitch = _cellSize + _cellSpacing;
            float originX = local.x + halfExtent;
            float originY = local.y + halfExtent;

            int x = Mathf.Clamp(Mathf.FloorToInt(originX / pitch), 0, _width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(originY / pitch), 0, _height - 1);

            cell = new GridPosition(x, y);
            return true;
        }

        internal Vector3 GetCellWorldPosition(GridPosition cell)
            => _cells[CellIndex(cell)].transform.position;

        /// <summary>Tints the cells a piece would occupy. Safe to call every frame while dragging.
        /// Shows nothing when the placement is not legal — an invalid drop spot gets no shadow at all.</summary>
        internal void ShowPreview(Piece piece, GridPosition anchor, bool isValid)
        {
            ClearPreview();

            if (piece == null || _currentTheme == null || !isValid)
            {
                return;
            }

            Color tint = _currentTheme.ValidPreview;
            for (int i = 0; i < piece.Offsets.Count && _previewCount < _previewCells.Length; i++)
            {
                GridPosition cell = anchor + piece.Offsets[i];

                // A hole is refused here exactly as an off-board cell is, so no legality feedback can
                // ever paint the valid-placement tint over one (AC4).
                if (!IsPlayableCell(cell))
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

        /// <summary>
        /// Tints exactly the cells an armed power-up would hit, occupied or not. The caller reads the
        /// set from <see cref="PowerUpTargetCells"/> — the same geometry the application itself uses —
        /// so the tinted region is never a promise the power-up would not keep.
        /// <para>
        /// Shares the valid-placement tint on purpose: the project has no power-up-specific colour,
        /// and both mean the same thing to the player — "this is what the thing in your hand lands
        /// on". Safe to call every pointer-move frame.
        /// </para>
        /// <para>
        /// <paramref name="isValid"/> exists for the joker, the one kind that can be aimed at a cell
        /// it cannot legally use (an occupied one). It borrows <see cref="ShowPreview"/>'s
        /// valid/invalid tint pair so "this tap will do nothing" reads the same way whether the player
        /// is holding a piece or a power-up. The three region-clearing kinds are legal anywhere on the
        /// board and simply leave it at its default.
        /// </para>
        /// </summary>
        /// <remarks>The caller's list is read here and never stored.</remarks>
        internal void ShowPowerUpTargetHighlight(IReadOnlyList<GridPosition> cells, bool isValid = true)
        {
            ClearPowerUpTargetHighlight();

            if (cells == null || _currentTheme == null || _cells == null)
            {
                return;
            }

            Color tint = isValid ? _currentTheme.ValidPreview : _currentTheme.InvalidPreview;
            for (int i = 0; i < cells.Count && _powerUpTargetCount < _powerUpTargetCells.Length; i++)
            {
                GridPosition cell = cells[i];
                if (!IsPlayableCell(cell))
                {
                    continue;
                }

                int index = CellIndex(cell);

                // Same reason as ShowPreview: a cell still fading out from a clear must drop the fade
                // the instant the tint claims it, or the tint would be drawn at partial alpha.
                CancelFade(index);

                _cells[index].SetColours(tint, tint);
                _powerUpTargetCells[_powerUpTargetCount] = cell;
                _powerUpTargetCount++;
            }
        }

        /// <summary>Restores every cell the power-up target tint claimed. Called when the pointer
        /// leaves the board, on release, and whenever the run is rebuilt underneath it.</summary>
        internal void ClearPowerUpTargetHighlight()
        {
            for (int i = 0; i < _powerUpTargetCount; i++)
            {
                GridPosition cell = _powerUpTargetCells[i];
                ApplyCellColour(cell, _boardModel != null ? _boardModel.GetCell(cell) : Board.EMPTY);
            }

            _powerUpTargetCount = 0;
        }

        /// <summary>
        /// Projects the Ghost Fit silhouette: the cells the suggested piece would occupy, tinted at
        /// <paramref name="pulse"/> of the way from an empty cell's own fill to the valid-placement
        /// tint. The caller drives <paramref name="pulse"/> (0..1) every frame, so the animation lives
        /// with the thing that owns the suggestion and this stays a stateless "draw it like this".
        /// <para>
        /// A separate claim set from <see cref="ShowPreview"/>'s, so the two can be up at once — which
        /// they are whenever the player drags the very piece being suggested. Where they overlap, each
        /// repaints every frame, so the worst case is one frame of the other's tint.
        /// </para>
        /// </summary>
        internal void ShowGhostFitSilhouette(Piece piece, GridPosition anchor, float pulse)
        {
            ClearGhostFitSilhouette();

            if (piece == null || _currentTheme == null || _cells == null)
            {
                return;
            }

            Color tint = Color.Lerp(
                _currentTheme.EmptyCellFill, _currentTheme.ValidPreview, Mathf.Clamp01(pulse));

            for (int i = 0; i < piece.Offsets.Count && _ghostFitCount < _ghostFitCells.Length; i++)
            {
                GridPosition cell = anchor + piece.Offsets[i];
                if (!IsPlayableCell(cell))
                {
                    continue;
                }

                int index = CellIndex(cell);

                // Same reason as ShowPreview: a cell still fading out from a clear must drop the fade
                // the instant the tint claims it, or the tint would be drawn at partial alpha.
                CancelFade(index);

                _cells[index].SetColours(tint, tint);
                _ghostFitCells[_ghostFitCount] = cell;
                _ghostFitCount++;
            }
        }

        /// <summary>Restores every cell the silhouette claimed. Called when the suggestion is dismissed,
        /// and whenever the run is rebuilt underneath it.</summary>
        internal void ClearGhostFitSilhouette()
        {
            for (int i = 0; i < _ghostFitCount; i++)
            {
                GridPosition cell = _ghostFitCells[i];
                ApplyCellColour(cell, _boardModel != null ? _boardModel.GetCell(cell) : Board.EMPTY);
            }

            _ghostFitCount = 0;
        }

        /// <summary>
        /// Outlines every cell of the rows/columns the in-flight drag would clear — filled cells and
        /// the empty ones the piece would occupy alike. Safe to call every frame: it diffs against
        /// what is already outlined, so an unchanged set costs nothing and a line that stopped
        /// qualifying is switched off in the same pass. Empty lists mean "outline nothing".
        /// <para>
        /// The outline is its own layer inside <see cref="CellView"/>, so it stacks on top of the
        /// <see cref="ShowPreview"/> tint instead of competing with it.
        /// </para>
        /// </summary>
        /// <remarks>The caller's lists are read here and never stored — <c>BoardSystem</c> hands back
        /// scratch buffers it overwrites on the next query.</remarks>
        internal void ShowWouldClearHighlight(IReadOnlyList<int> rows, IReadOnlyList<int> columns)
        {
            Array.Clear(_pendingHighlightMask, 0, _pendingHighlightMask.Length);

            if (_currentTheme == null || _cells == null)
            {
                ApplyHighlightMask();
                return;
            }

            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    int y = rows[i];
                    if (y < 0 || y >= _height)
                    {
                        continue;
                    }

                    for (int x = 0; x < _width; x++)
                    {
                        int index = (y * _width) + x;
                        if (_holeMask[index])
                        {
                            continue;
                        }

                        _pendingHighlightMask[index] = true;
                    }
                }
            }

            if (columns != null)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    int x = columns[i];
                    if (x < 0 || x >= _width)
                    {
                        continue;
                    }

                    for (int y = 0; y < _height; y++)
                    {
                        int index = (y * _width) + x;
                        if (_holeMask[index])
                        {
                            continue;
                        }

                        _pendingHighlightMask[index] = true;
                    }
                }
            }

            ApplyHighlightMask();
        }

        /// <summary>Drops every would-clear outline. Called on drop, cancel and whenever the drag has
        /// no legal anchor.</summary>
        internal void ClearWouldClearHighlight()
        {
            if (!_hasHighlight)
            {
                return;
            }

            Array.Clear(_pendingHighlightMask, 0, _pendingHighlightMask.Length);
            ApplyHighlightMask();
        }

        private int CellIndex(GridPosition cell) => (cell.Y * _width) + cell.X;

        /// <summary>True when <paramref name="cell"/> is on the board and not a hole — the one gate
        /// every legality-feedback path (drag preview, ghost-fit silhouette, power-up target highlight)
        /// passes through, so none of them can tint a cell a piece could never occupy.</summary>
        private bool IsPlayableCell(GridPosition cell)
        {
            if (cell.X < 0 || cell.X >= _width || cell.Y < 0 || cell.Y >= _height)
            {
                return false;
            }

            return !_holeMask[(cell.Y * _width) + cell.X];
        }

        /// <summary>A hole's fill: the theme's empty-cell fill pushed towards black and made partly
        /// transparent, so the card shows through and the cell reads as a gap in the board.</summary>
        private static Color HoleFill(ThemeDefinition theme) => Recess(theme.EmptyCellFill);

        private static Color HoleOutline(ThemeDefinition theme) => Recess(theme.EmptyCellOutline);

        private static Color Recess(Color source)
            => new Color(
                source.r * HOLE_DARKEN,
                source.g * HOLE_DARKEN,
                source.b * HOLE_DARKEN,
                source.a * HOLE_ALPHA);

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
            int cellCount = _width * _height;
            _cells = new CellView[cellCount];
            _powerUpTargetCells = new GridPosition[cellCount];
            _rowClearMask = new bool[_height];
            _columnClearMask = new bool[_width];
            _highlightMask = new bool[cellCount];
            _pendingHighlightMask = new bool[cellCount];
            _holeMask = new bool[cellCount];
            _cellColourIds = new int[cellCount];
            _cellGenerations = new int[cellCount];
            _pendingColourIds = new int[cellCount];
            _pendingGenerations = new int[cellCount];
            _cellPending = new bool[cellCount];
            _cellSpecialKinds = new SpecialCellKind[cellCount];

            for (int i = 0; i < cellCount; i++)
            {
                _cellColourIds[i] = Board.EMPTY;
                _pendingColourIds[i] = Board.EMPTY;
            }

            float pitch = _cellSize + _cellSpacing;
            float originX = (-((_width * _cellSize) + ((_width - 1) * _cellSpacing)) * 0.5f) + (_cellSize * 0.5f);
            float originY = (-((_height * _cellSize) + ((_height - 1) * _cellSpacing)) * 0.5f) + (_cellSize * 0.5f);

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var position = new GridPosition(x, y);
                    int index = (y * _width) + x;
                    _holeMask[index] = _boardModel != null && _boardModel.IsHole(position);

                    CellView cell = CellFactory.CreateCell(
                        parent, $"Cell_{x}_{y}", _cellSize, _cellInset, _cellBevelThickness);
                    var rect = (RectTransform)cell.transform;
                    rect.anchoredPosition = new Vector2(originX + (x * pitch), originY + (y * pitch));

                    // Cells are built in Awake, before the theme is known; the theme subscription in
                    // Start paints them (and repaints them on every later theme switch).
                    cell.SetColours(Color.clear, Color.clear);
                    _cells[index] = cell;
                }
            }
        }

        /// <summary>Forces every cell back to the model's state, opaque and with no fade in flight.</summary>
        private void RedrawAll()
        {
            // A run restart wipes the board underneath any drag or power-up aim that was in flight,
            // so nothing either of them tinted or outlined may survive it.
            ClearWouldClearHighlight();
            _powerUpTargetCount = 0;
            _ghostFitCount = 0;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var cell = new GridPosition(x, y);
                    int index = CellIndex(cell);

                    _cellGenerations[index]++;
                    _cellPending[index] = false;

                    int colourId = _boardModel.GetCell(cell);
                    _cellColourIds[index] = colourId;
                    ApplyCellColour(cell, colourId);

                    // Re-derived from the model, never carried over from the bookkeeping: a full
                    // repaint must be able to correct any icon state a cancelled fade or a rebuilt run
                    // left behind.
                    _cellSpecialKinds[index] = _boardModel.GetSpecialKind(cell);
                    ApplyCellIcon(index, _cellSpecialKinds[index]);

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

                // Read back rather than assumed empty: this is also the notification a spawner raises
                // when it occupies the cell it is about to tag, and the tag's own notification follows.
                _cellSpecialKinds[index] = _boardModel.GetSpecialKind(cell);
                ApplyCellIcon(index, _cellSpecialKinds[index]);

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

            // The icon is left on screen and fades with the block it belongs to; the settled state is
            // already "no kind", because Board.Clear resets a destroyed cell's kind with its colour.
            _cellSpecialKinds[index] = SpecialCellKind.None;
        }

        private void OnLinesCleared(LinesClearedMessage message)
        {
            Array.Clear(_rowClearMask, 0, _rowClearMask.Length);
            Array.Clear(_columnClearMask, 0, _columnClearMask.Length);

            IReadOnlyList<int> rows = message.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                int y = rows[i];
                if (y >= 0 && y < _height)
                {
                    _rowClearMask[y] = true;
                }
            }

            IReadOnlyList<int> columns = message.Columns;
            for (int i = 0; i < columns.Count; i++)
            {
                int x = columns[i];
                if (x >= 0 && x < _width)
                {
                    _columnClearMask[x] = true;
                }
            }

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    bool inRow = _rowClearMask[y];
                    bool inColumn = _columnClearMask[x];
                    if (!inRow && !inColumn)
                    {
                        continue;
                    }

                    int index = (y * _width) + x;
                    if (!_cellPending[index] || _pendingGenerations[index] != _cellGenerations[index])
                    {
                        continue;
                    }

                    PlayClearAsync(new GridPosition(x, y), index, _cellGenerations[index], inRow && inColumn)
                        .Forget();
                }
            }
        }

        /// <summary>
        /// Claims the cells a power-up emptied, exactly as <see cref="OnLinesCleared"/> claims the
        /// ones a placement emptied.
        /// <para>
        /// <see cref="OnCellChanged"/> deliberately holds an emptied cell's pre-clear look on screen
        /// and waits for whoever cleared it to start the fade. A power-up publishes no
        /// <see cref="LinesClearedMessage"/> — its clear is a region, not a set of lines — so without
        /// this the cells it emptied would sit showing their old colour until the next run started.
        /// </para>
        /// <para>
        /// It sweeps every pending cell rather than the message's own, because the message reports how
        /// many cells cleared, not which. A placement's own pending cells stay claimed by their
        /// original <see cref="OnLinesCleared"/> fade — <c>_cellPending</c> stays true for that fade's
        /// whole duration, not just its first frame — so a power-up applied within that window will
        /// also catch them and restart their fade from full opacity. The <see cref="_cellGenerations"/>
        /// bump above still guarantees a single final writer either way; this is a cosmetic re-fade in
        /// the rare case of overlap, not a correctness bug. No intersection flash — that is a
        /// two-full-lines concept, and a power-up clears a region.
        /// </para>
        /// </summary>
        private void OnPowerUpApplied(PowerUpAppliedMessage message) => SweepPendingCells(message.ClearedCellCount);

        /// <summary>Starts a region-clear fade on every cell still waiting for one. Shared by the
        /// power-up and explosive-core paths, which differ only in what emptied the cells.</summary>
        private void SweepPendingCells(int clearedCellCount)
        {
            if (clearedCellCount <= 0 || _cells == null)
            {
                return;
            }

            for (int index = 0; index < _cells.Length; index++)
            {
                if (!_cellPending[index])
                {
                    continue;
                }

                // Bumped first so this fade is the cell's only owner. A placement's fade can still be
                // in flight here (it holds the cell pending for its whole duration), and two fades
                // writing one cell's alpha would fight; the bump makes the older one bail on its next
                // tick instead.
                _cellGenerations[index]++;

                var cell = new GridPosition(index % _width, index / _width);
                PlayClearAsync(cell, index, _cellGenerations[index], false).Forget();
            }
        }

        /// <summary>Safety net for <c>BoardModel.ClearAll</c>, which empties cells without ever
        /// publishing a <see cref="LinesClearedMessage"/> to claim them.</summary>
        private void OnRunStarted(RunStartedMessage message) => RedrawAll();

        /// <summary>A cell became special — a placement that closed a row and a column spawning an
        /// explosive core on their intersection, or a combo streak converting a block into a laser. The
        /// icon is drawn per kind, not per spawn rule, so a new kind needs nothing here. Losing a kind
        /// needs no counterpart either: a cell only
        /// loses one by being destroyed, which already arrives through <see cref="OnCellChanged"/>.</summary>
        private void OnSpecialKindChanged(GridPosition cell, SpecialCellKind kind)
        {
            int index = CellIndex(cell);
            _cellSpecialKinds[index] = kind;
            ApplyCellIcon(index, kind);
        }

        /// <summary>An explosive core blasted a region. Claimed exactly as a power-up's cleared region
        /// is, and for the same reason: no <see cref="LinesClearedMessage"/> follows a blast, so
        /// without this the cells it emptied would sit showing their old colour.</summary>
        private void OnExplosiveCoreDetonated(ExplosiveCoreDetonatedMessage message)
            => SweepPendingCells(message.ClearedCellCount);

        /// <summary>A laser wiped a line. Claimed exactly as a blast's cells are, and for the same
        /// reason: a wipe does not need the line to be full, so no <see cref="LinesClearedMessage"/>
        /// follows it.</summary>
        private void OnLaserFired(LaserFiredMessage message) => SweepPendingCells(message.WipedCellCount);

        /// <summary>Pushes <see cref="_pendingHighlightMask"/> to the cells, touching only the ones
        /// whose state actually changed, and adopts it as the current mask.</summary>
        private void ApplyHighlightMask()
        {
            if (_cells == null)
            {
                return;
            }

            Color colour = _currentTheme != null ? _currentTheme.WouldClearHighlight : Color.clear;
            _hasHighlight = false;

            for (int index = 0; index < _highlightMask.Length; index++)
            {
                bool wanted = _pendingHighlightMask[index];
                _hasHighlight |= wanted;

                if (wanted == _highlightMask[index])
                {
                    continue;
                }

                _highlightMask[index] = wanted;

                if (wanted)
                {
                    _cells[index].SetHighlight(colour);
                }
                else
                {
                    _cells[index].ClearHighlight();
                }
            }
        }

        /// <summary>Re-tints the outlines already on screen after a theme switch.</summary>
        private void RepaintHighlight()
        {
            if (!_hasHighlight || _currentTheme == null)
            {
                return;
            }

            Color colour = _currentTheme.WouldClearHighlight;
            for (int index = 0; index < _highlightMask.Length; index++)
            {
                if (_highlightMask[index])
                {
                    _cells[index].SetHighlight(colour);
                }
            }
        }

        /// <summary>Invalidates any fade on a cell and restores it to full opacity.</summary>
        private void CancelFade(int index)
        {
            if (!_cellPending[index])
            {
                return;
            }

            _cellPending[index] = false;
            _cellGenerations[index]++;

            // The fade owned the icon that was still on screen; dropping the fade settles the cell on
            // its post-clear state, which has no kind.
            ApplyCellIcon(index, _cellSpecialKinds[index]);
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
            ApplyCellIcon(index, _cellSpecialKinds[index]);
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
            RepaintHighlight();
        }

        private void RepaintCells()
        {
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int index = (y * _width) + x;

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

            int index = CellIndex(cell);
            CellView view = _cells[index];

            // Checked before occupancy, and before the empty-cell look: a hole is never occupied, and
            // drawing it like an empty cell would tell the player they could drop a piece there.
            if (_holeMask[index])
            {
                view.SetColours(HoleFill(_currentTheme), HoleOutline(_currentTheme));
                return;
            }

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

        /// <summary>Shows or hides one cell's special-cell icon. Allocation-free and idempotent, like
        /// the outline toggle it mirrors, so every repaint path can call it unconditionally.</summary>
        private void ApplyCellIcon(int index, SpecialCellKind kind)
        {
            if (kind == SpecialCellKind.None)
            {
                _cells[index].ClearSpecialIcon();
                return;
            }

            _cells[index].SetSpecialIcon(
                kind == SpecialCellKind.ScoreGem ? ScoreGemIconTint : SpecialIconTint);
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
