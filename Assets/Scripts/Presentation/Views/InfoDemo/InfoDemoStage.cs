using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Plays an <see cref="InfoDemoTimeline"/> inside an info card's demo slot
    /// (<see cref="InfoCardChrome.Handles.DemoRootRect"/>, issue #446): an 8x8 mini board over a tray
    /// strip, a thin loop-progress bar under both, and pooled visuals for every element the timeline
    /// declares. Owned by the hosting View — a plain class, not a component — and driven by one looping
    /// <c>async UniTaskVoid</c> on unscaled time, cancelled by <see cref="Stop"/>, <see cref="Dispose"/>
    /// or the host's destruction (issue #445 AC4).
    /// <para>
    /// Everything is built lazily on the first <see cref="Play"/> and then only reused: the 64 board
    /// blocks are permanent, every other element kind comes from a grow-only pool re-bound per
    /// timeline. A frame samples the timeline into a preallocated state array and re-applies only the
    /// elements whose state changed, all under the stage's own nested Canvas so the animation never
    /// rebuilds the card's — zero managed allocations per frame in steady state (AC8).
    /// </para>
    /// <para>
    /// Never reads or writes any board, run or gameplay state (AC5): its only inputs are the timeline,
    /// the theme and <see cref="IInfoDemoResources"/>.
    /// </para>
    /// </summary>
    internal sealed class InfoDemoStage : IDisposable
    {
        /// <summary>The stage's width in reference pixels: the mockup's 257-wide stage on its 312-wide
        /// card, scaled to the info card's 880 width.</summary>
        private const float STAGE_WIDTH = 725f;

        /// <summary>Reference pixels per mockup unit.</summary>
        private const float SCALE = STAGE_WIDTH / InfoDemoLayout.MOCK_BOARD_EXTENT;

        private const float MOCK_STAGE_CORNER = 14f;
        private const float MOCK_DIVIDER_THICKNESS = 1.5f;
        private const float MOCK_PROGRESS_GAP = 8f;
        private const float MOCK_PROGRESS_HEIGHT = 4f;

        /// <summary>Longest frame the loop advances by — a hitch (or the first frame after the app
        /// resumes) skips ahead a little instead of jumping half a loop.</summary>
        private const float MAX_FRAME_DELTA = 0.1f;

        /// <summary>Below this an element counts as invisible and its GameObject is switched off.</summary>
        private const float MIN_VISIBLE_ALPHA = 0.004f;

        /// <summary>The Vortex fill's deep indigo (#4B4FA8), and its bevel shades.</summary>
        private static readonly Color VortexBlockFill = new Color(0.294f, 0.310f, 0.659f, 1f);
        private static readonly Color VortexBlockHighlight = new Color(0.600f, 0.620f, 0.900f, 1f);
        private static readonly Color VortexBlockShade = new Color(0.200f, 0.210f, 0.470f, 1f);

        private readonly RectTransform _slot;
        private readonly IInfoDemoResources _resources;
        private readonly CancellationToken _destroyToken;

        private readonly CellView[] _blockCells = new CellView[InfoDemoLayout.BOARD_CELL_COUNT];
        private readonly Image[] _emptyCellOutlines = new Image[InfoDemoLayout.BOARD_CELL_COUNT];
        private readonly Image[] _emptyCellFaces = new Image[InfoDemoLayout.BOARD_CELL_COUNT];

        private readonly List<Image> _bandPool = new List<Image>(4);
        private readonly List<Image> _outlinePool = new List<Image>(4);
        private readonly List<Image> _glowPool = new List<Image>(4);
        private readonly List<Image> _iconPool = new List<Image>(4);
        private readonly List<Text> _labelPool = new List<Text>(2);
        private readonly List<Outline> _labelOutlinePool = new List<Outline>(2);
        private readonly List<PieceVisual> _piecePool = new List<PieceVisual>(4);

        private float _cellPixels;
        private float _cellInset;
        private float _cellBevel;

        private RectTransform _root;
        private CanvasGroup _contentGroup;
        private RectTransform _bandLayer;
        private RectTransform _blockLayer;
        private RectTransform _outlineLayer;
        private RectTransform _glowLayer;
        private RectTransform _iconLayer;
        private RectTransform _pieceLayer;
        private RectTransform _labelLayer;

        private Image _stageBaseImage;
        private Image _boardPlateImage;
        private Image _boardPlateFillerImage;
        private Image _dividerImage;
        private Image _progressTrackImage;
        private Image _progressFillImage;
        private RectTransform _progressFillRect;

        private ThemeDefinition _theme;
        private InfoDemoTimeline _timeline;

        // Per-element bindings, indexed by element id; grown (never shrunk) when a bigger timeline binds.
        private InfoDemoElementState[] _states = new InfoDemoElementState[0];
        private InfoDemoElementState[] _applied = new InfoDemoElementState[0];
        private bool[] _appliedValid = new bool[0];
        private bool[] _visible = new bool[0];
        private RectTransform[] _elementRects = new RectTransform[0];
        private Image[] _elementImages = new Image[0];
        private Text[] _elementTexts = new Text[0];
        private Outline[] _elementTextOutlines = new Outline[0];
        private PieceVisual[] _elementPieces = new PieceVisual[0];
        private Color[] _elementTints = new Color[0];

        private CancellationTokenSource _runCts;
        private float _elapsed;
        private bool _isPlaying;
        private bool _isDisposed;

        /// <summary>A pooled multi-cell piece: a root that moves/scales/fades as one, and its cells.</summary>
        private sealed class PieceVisual
        {
            internal RectTransform Root;
            internal CanvasGroup Group;
            internal readonly List<CellView> Cells = new List<CellView>(4);
            internal int ActiveCellCount;
        }

        internal InfoDemoStage(RectTransform slot, IInfoDemoResources resources, CancellationToken destroyToken)
        {
            _slot = slot;
            _resources = resources;
            _destroyToken = destroyToken;
        }

        /// <summary>The whole stage footprint — board, strip and progress bar — in reference pixels.
        /// What <see cref="InfoCardChrome.ShowDemo"/> reserves in the card.</summary>
        internal static Vector2 Size => new Vector2(
            STAGE_WIDTH,
            (InfoDemoLayout.MOCK_STAGE_HEIGHT + MOCK_PROGRESS_GAP + MOCK_PROGRESS_HEIGHT) * SCALE);

        internal bool IsPlaying => _isPlaying;

        /// <summary>
        /// Starts looping <paramref name="timeline"/>, rendering its first visible frame synchronously
        /// so the demo is on screen the moment the card is (no pop-in, issue #446). Calling it again
        /// with the timeline already playing keeps the loop running and just repaints — a theme or
        /// locale change never restarts the demo.
        /// </summary>
        internal void Play(InfoDemoTimeline timeline)
        {
            if (_isDisposed || timeline == null)
            {
                return;
            }

            EnsureBuilt();

            if (_timeline == timeline && _isPlaying)
            {
                RefreshLabels();
                InvalidateAll();
                Render();
                return;
            }

            Stop();
            Bind(timeline);

            // Opened straight onto a fully faded-in board rather than an empty one fading up: the
            // fade-in only smooths each later loop restart.
            _elapsed = timeline.FadeInDuration;
            Render();

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
            _isPlaying = true;
            RunAsync(_runCts.Token).Forget();
        }

        /// <summary>Stops the loop immediately. Safe to call when nothing is playing.</summary>
        internal void Stop()
        {
            _isPlaying = false;

            if (_runCts == null)
            {
                return;
            }

            _runCts.Cancel();
            _runCts.Dispose();
            _runCts = null;
        }

        /// <summary>Repaints every themed surface — called by the host whenever the theme changes.</summary>
        internal void SetTheme(ThemeDefinition theme)
        {
            _theme = theme;

            if (_root == null || _theme == null)
            {
                return;
            }

            PaintStatics();
            InvalidateAll();

            if (_timeline != null)
            {
                Render();
            }
        }

        public void Dispose()
        {
            Stop();
            _isDisposed = true;
        }

        private async UniTaskVoid RunAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);

                    _elapsed = _timeline.WrapTime(_elapsed + Mathf.Min(Time.unscaledDeltaTime, MAX_FRAME_DELTA));
                    Render();
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped (card closed / subject changed) or the host was destroyed — nothing to undo:
                // the stage's visuals simply stay where the last frame left them, hidden with the card.
            }
        }

        private void Render()
        {
            if (_timeline == null || _theme == null || _root == null)
            {
                return;
            }

            _timeline.Evaluate(_elapsed, _states);

            for (int elementIndex = 0; elementIndex < _timeline.ElementCount; elementIndex++)
            {
                ApplyElement(elementIndex);
            }

            _contentGroup.alpha = _timeline.LoopAlpha(_elapsed);

            float progress = _elapsed / _timeline.Duration;
            float trackHeight = MOCK_PROGRESS_HEIGHT * SCALE;
            _progressFillRect.sizeDelta = new Vector2(Mathf.Max(trackHeight, STAGE_WIDTH * progress), trackHeight);
        }

        private void ApplyElement(int elementIndex)
        {
            InfoDemoElementState state = _states[elementIndex];
            if (_appliedValid[elementIndex] && state.SameAs(_applied[elementIndex]))
            {
                return;
            }

            _applied[elementIndex] = state;
            _appliedValid[elementIndex] = true;

            InfoDemoElement element = _timeline.GetElement(elementIndex);
            bool needsPaint = element.Kind == InfoDemoElementKind.BoardBlock || element.Kind == InfoDemoElementKind.Piece;
            bool visible = state.Alpha > MIN_VISIBLE_ALPHA && (!needsPaint || state.Paint != InfoDemoPaint.NONE);

            RectTransform rect = _elementRects[elementIndex];
            if (_visible[elementIndex] != visible)
            {
                _visible[elementIndex] = visible;
                rect.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            rect.anchoredPosition = ToPixels(state.Position);
            rect.localScale = new Vector3(state.Scale, state.Scale, 1f);
            rect.localRotation = Quaternion.Euler(0f, 0f, state.Rotation);

            switch (element.Kind)
            {
                case InfoDemoElementKind.BoardBlock:
                    PaintBlock(_blockCells[elementIndex], state.Paint, state.Flash, state.Alpha);
                    break;
                case InfoDemoElementKind.Piece:
                {
                    PieceVisual piece = _elementPieces[elementIndex];
                    piece.Group.alpha = state.Alpha;
                    for (int cellIndex = 0; cellIndex < piece.ActiveCellCount; cellIndex++)
                    {
                        PaintBlock(piece.Cells[cellIndex], state.Paint, state.Flash, 1f);
                    }

                    break;
                }
                case InfoDemoElementKind.Icon:
                    _elementImages[elementIndex].color = HudChrome.WithAlpha(_elementTints[elementIndex], state.Alpha);
                    break;
                case InfoDemoElementKind.Label:
                {
                    Color labelColour = Color.Lerp(ResolveFlatColour(state.Paint), Color.white, state.Flash);
                    _elementTexts[elementIndex].color = HudChrome.WithAlpha(labelColour, state.Alpha);
                    _elementTextOutlines[elementIndex].effectColor = new Color(1f, 1f, 1f, 0.9f * state.Alpha);
                    break;
                }
                default:
                {
                    Color colour = Color.Lerp(ResolveFlatColour(state.Paint), Color.white, state.Flash);
                    _elementImages[elementIndex].color = HudChrome.WithAlpha(colour, state.Alpha);
                    break;
                }
            }
        }

        private void PaintBlock(CellView cell, int paint, float flash, float alpha)
        {
            ResolveBlockColours(paint, out Color fill, out Color highlight, out Color shade);
            if (flash > 0f)
            {
                fill = Color.Lerp(fill, Color.white, flash);
                highlight = Color.Lerp(highlight, Color.white, flash);
                shade = Color.Lerp(shade, Color.white, flash);
            }

            cell.SetEmbossedColours(fill, highlight, shade);
            cell.SetAlpha(alpha);
        }

        private void ResolveBlockColours(int paint, out Color fill, out Color highlight, out Color shade)
        {
            if (paint == InfoDemoPaint.VORTEX_BLOCK)
            {
                fill = VortexBlockFill;
                highlight = VortexBlockHighlight;
                shade = VortexBlockShade;
                return;
            }

            fill = _theme.GetFill(paint);
            highlight = _theme.GetHighlight(paint);
            shade = _theme.GetShade(paint);
        }

        private Color ResolveFlatColour(int paint)
        {
            switch (paint)
            {
                case InfoDemoPaint.VORTEX_BLOCK:
                    return VortexBlockFill;
                case InfoDemoPaint.VORTEX_GLOW:
                    return BoardView.VortexIconTint;
                case InfoDemoPaint.WHITE:
                    return Color.white;
                case InfoDemoPaint.ACCENT:
                    return _theme.Accent;
                case InfoDemoPaint.INK:
                    return _theme.Ink;
                case InfoDemoPaint.LINE_HIGHLIGHT:
                    return _theme.WouldClearHighlight;
                default:
                    return _theme.GetFill(paint);
            }
        }

        /// <summary>A board-unit point (see <see cref="InfoDemoLayout"/>) in the stage's local pixels,
        /// origin at the stage centre.</summary>
        private static Vector2 ToPixels(Vector2 boardPoint)
        {
            Vector2 mock = InfoDemoLayout.ToMockPoint(boardPoint);
            float stageTop = Size.y * 0.5f;
            return new Vector2((mock.x - (InfoDemoLayout.MOCK_BOARD_EXTENT * 0.5f)) * SCALE, stageTop - (mock.y * SCALE));
        }

        /// <summary>A span of <paramref name="cells"/> cell pitches, less the trailing gap, in pixels —
        /// one cell is exactly one cell wide, three cells exactly three cells and their two gaps.</summary>
        private static float SpanPixels(float cells) => ((cells * InfoDemoLayout.MOCK_PITCH) - InfoDemoLayout.MOCK_GAP) * SCALE;

        private void InvalidateAll()
        {
            for (int elementIndex = 0; elementIndex < _appliedValid.Length; elementIndex++)
            {
                _appliedValid[elementIndex] = false;
            }
        }

        private void RefreshLabels()
        {
            if (_timeline == null)
            {
                return;
            }

            for (int elementIndex = 0; elementIndex < _timeline.ElementCount; elementIndex++)
            {
                InfoDemoElement element = _timeline.GetElement(elementIndex);
                if (element.Kind == InfoDemoElementKind.Label)
                {
                    _elementTexts[elementIndex].text = _resources.Translate(element.LabelKey);
                }
            }
        }

        /// <summary>Assigns a pooled visual to every element of <paramref name="timeline"/> and parks
        /// every unused pooled visual. Runs once per <see cref="Play"/> of a new timeline, never per
        /// frame, so it is free to grow pools.</summary>
        private void Bind(InfoDemoTimeline timeline)
        {
            _timeline = timeline;
            int elementCount = timeline.ElementCount;
            EnsureBindingCapacity(elementCount);

            int bandsUsed = 0;
            int outlinesUsed = 0;
            int glowsUsed = 0;
            int iconsUsed = 0;
            int labelsUsed = 0;
            int piecesUsed = 0;

            for (int elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                InfoDemoElement element = timeline.GetElement(elementIndex);
                _elementImages[elementIndex] = null;
                _elementTexts[elementIndex] = null;
                _elementTextOutlines[elementIndex] = null;
                _elementPieces[elementIndex] = null;

                switch (element.Kind)
                {
                    case InfoDemoElementKind.BoardBlock:
                        _elementRects[elementIndex] = (RectTransform)_blockCells[elementIndex].transform;
                        break;
                    case InfoDemoElementKind.Band:
                    {
                        Image band = TakeImage(_bandPool, bandsUsed++, _bandLayer, "Band");
                        HudChrome.ConfigureRounded(band, _cellPixels * 0.35f);
                        BindImage(elementIndex, band, new Vector2(SpanPixels(element.Size.x), SpanPixels(element.Size.y)));
                        break;
                    }
                    case InfoDemoElementKind.Outline:
                    {
                        Image outline = TakeImage(_outlinePool, outlinesUsed++, _outlineLayer, "Outline");
                        ConfigureDashedOutline(outline);
                        BindImage(elementIndex, outline, new Vector2(SpanPixels(element.Size.x), SpanPixels(element.Size.y)));
                        break;
                    }
                    case InfoDemoElementKind.Glow:
                    {
                        Image glow = TakeImage(_glowPool, glowsUsed++, _glowLayer, "Glow");
                        HudChrome.ConfigureGlyph(glow, UiSpriteFactory.RadialGlow);
                        BindImage(elementIndex, glow, element.Size * (InfoDemoLayout.MOCK_CELL * SCALE));
                        break;
                    }
                    case InfoDemoElementKind.Icon:
                    {
                        Image icon = TakeImage(_iconPool, iconsUsed++, _iconLayer, "Icon");
                        bool hasSprite = _resources.TryGetSprite(
                            element.Sprite, element.SpriteParameter, out Sprite sprite, out Color tint);
                        HudChrome.ConfigureGlyph(icon, sprite);
                        _elementTints[elementIndex] = hasSprite ? tint : Color.clear;
                        BindImage(elementIndex, icon, element.Size * (InfoDemoLayout.MOCK_CELL * SCALE));
                        break;
                    }
                    case InfoDemoElementKind.Label:
                    {
                        Text label = TakeLabel(labelsUsed, element, out Outline labelOutline);
                        labelsUsed++;
                        _elementTexts[elementIndex] = label;
                        _elementTextOutlines[elementIndex] = labelOutline;
                        label.text = _resources.Translate(element.LabelKey);
                        _elementRects[elementIndex] = (RectTransform)label.transform;
                        break;
                    }
                    case InfoDemoElementKind.Piece:
                    {
                        PieceVisual piece = TakePiece(piecesUsed++, element.Shape);
                        _elementPieces[elementIndex] = piece;
                        _elementRects[elementIndex] = piece.Root;
                        break;
                    }
                }

                _appliedValid[elementIndex] = false;

                // Parked until the first Render decides; ApplyElement flips it on when visible.
                _visible[elementIndex] = false;
                _elementRects[elementIndex].gameObject.SetActive(false);
            }

            ParkUnused(_bandPool, bandsUsed);
            ParkUnused(_outlinePool, outlinesUsed);
            ParkUnused(_glowPool, glowsUsed);
            ParkUnused(_iconPool, iconsUsed);

            for (int labelIndex = labelsUsed; labelIndex < _labelPool.Count; labelIndex++)
            {
                _labelPool[labelIndex].gameObject.SetActive(false);
            }

            for (int pieceIndex = piecesUsed; pieceIndex < _piecePool.Count; pieceIndex++)
            {
                _piecePool[pieceIndex].Root.gameObject.SetActive(false);
            }
        }

        private void BindImage(int elementIndex, Image image, Vector2 size)
        {
            _elementImages[elementIndex] = image;
            RectTransform rect = (RectTransform)image.transform;
            rect.sizeDelta = size;
            _elementRects[elementIndex] = rect;
        }

        private void EnsureBindingCapacity(int elementCount)
        {
            if (_states.Length >= elementCount)
            {
                return;
            }

            _states = new InfoDemoElementState[elementCount];
            _applied = new InfoDemoElementState[elementCount];
            _appliedValid = new bool[elementCount];
            _visible = new bool[elementCount];
            _elementRects = new RectTransform[elementCount];
            _elementImages = new Image[elementCount];
            _elementTexts = new Text[elementCount];
            _elementTextOutlines = new Outline[elementCount];
            _elementPieces = new PieceVisual[elementCount];
            _elementTints = new Color[elementCount];
        }

        private static Image TakeImage(List<Image> pool, int index, RectTransform layer, string objectName)
        {
            if (index < pool.Count)
            {
                return pool[index];
            }

            GameObject imageObject = new GameObject($"{objectName}_{index}", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)imageObject.transform;
            rect.SetParent(layer, false);
            HudChrome.Centre(rect, Vector2.one);

            Image image = imageObject.GetComponent<Image>();
            image.raycastTarget = false;
            pool.Add(image);
            return image;
        }

        private static void ParkUnused(List<Image> pool, int used)
        {
            for (int poolIndex = used; poolIndex < pool.Count; poolIndex++)
            {
                pool[poolIndex].gameObject.SetActive(false);
            }
        }

        private void ConfigureDashedOutline(Image image)
        {
            float radius = _cellPixels * CellFactory.CORNER_RADIUS_FRACTION * 1.4f;
            image.sprite = UiSpriteFactory.DashedRoundedOutline;
            image.type = Image.Type.Tiled;
            image.fillCenter = false;
            image.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / Mathf.Max(1f, radius);
            image.color = Color.clear;
            image.raycastTarget = false;
        }

        private Text TakeLabel(int index, InfoDemoElement element, out Outline outline)
        {
            Text label;
            if (index < _labelPool.Count)
            {
                label = _labelPool[index];
                outline = _labelOutlinePool[index];
            }
            else
            {
                label = UiTextFactory.Create(_labelLayer, $"Label_{index}", 10, FontStyle.Bold, Color.clear);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                outline = label.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(3f, -3f);
                _labelPool.Add(label);
                _labelOutlinePool.Add(outline);
            }

            float pitchPixels = InfoDemoLayout.MOCK_PITCH * SCALE;
            label.fontSize = Mathf.RoundToInt(element.Size.y * pitchPixels);
            ((RectTransform)label.transform).sizeDelta = new Vector2(element.Size.x * pitchPixels, element.Size.y * pitchPixels * 1.6f);
            return label;
        }

        private PieceVisual TakePiece(int index, Vector2Int[] shape)
        {
            PieceVisual piece;
            if (index < _piecePool.Count)
            {
                piece = _piecePool[index];
            }
            else
            {
                GameObject rootObject = new GameObject($"Piece_{index}", typeof(RectTransform), typeof(CanvasGroup));
                piece = new PieceVisual
                {
                    Root = (RectTransform)rootObject.transform,
                    Group = rootObject.GetComponent<CanvasGroup>(),
                };
                piece.Root.SetParent(_pieceLayer, false);
                HudChrome.Centre(piece.Root, Vector2.one);
                piece.Group.interactable = false;
                piece.Group.blocksRaycasts = false;
                _piecePool.Add(piece);
            }

            int cellCount = shape != null ? shape.Length : 0;
            while (piece.Cells.Count < cellCount)
            {
                piece.Cells.Add(CellFactory.CreateCell(
                    piece.Root, $"PieceCell_{piece.Cells.Count}", _cellPixels, _cellInset, _cellBevel));
            }

            if (cellCount > 0)
            {
                InfoDemoLayout.ShapeBounds(shape, out Vector2Int min, out Vector2Int max);
                Vector2 centre = new Vector2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
                float pitchPixels = InfoDemoLayout.MOCK_PITCH * SCALE;

                for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    Vector2 offset = (Vector2)shape[cellIndex] - centre;
                    RectTransform cellRect = (RectTransform)piece.Cells[cellIndex].transform;
                    cellRect.anchoredPosition = new Vector2(offset.x * pitchPixels, -offset.y * pitchPixels);
                    cellRect.gameObject.SetActive(true);
                }
            }

            for (int cellIndex = cellCount; cellIndex < piece.Cells.Count; cellIndex++)
            {
                piece.Cells[cellIndex].gameObject.SetActive(false);
            }

            piece.ActiveCellCount = cellCount;
            return piece;
        }

        /// <summary>Builds the permanent hierarchy on first use: stage plate and strip, the 64 empty
        /// board cells and 64 board blocks, the element layers, and the progress bar.</summary>
        private void EnsureBuilt()
        {
            if (_root != null)
            {
                return;
            }

            _resources.GetBoardCellMetrics(out float boardCellSize, out float boardInset, out float boardBevel);
            _cellPixels = InfoDemoLayout.MOCK_CELL * SCALE;
            float cellRatio = boardCellSize > 0f ? _cellPixels / boardCellSize : 1f;
            _cellInset = boardInset * cellRatio;
            _cellBevel = boardBevel * cellRatio;

            Vector2 size = Size;
            float stageHeight = InfoDemoLayout.MOCK_STAGE_HEIGHT * SCALE;
            float boardHeight = InfoDemoLayout.MOCK_BOARD_EXTENT * SCALE;
            float stageTop = size.y * 0.5f;
            float corner = MOCK_STAGE_CORNER * SCALE;

            // Its own nested Canvas so the per-frame animation only ever rebuilds this canvas, never
            // the card (and shared UI canvas) around it.
            GameObject rootObject = new GameObject("InfoDemoStage", typeof(RectTransform), typeof(Canvas));
            _root = (RectTransform)rootObject.transform;
            _root.SetParent(_slot, false);
            HudChrome.Centre(_root, size);

            _stageBaseImage = HudChrome.BuildRounded(
                _root, "StageBase", new Vector2(STAGE_WIDTH, stageHeight), new Vector2(0f, stageTop - (stageHeight * 0.5f)), corner);
            _boardPlateImage = HudChrome.BuildRounded(
                _root, "BoardPlate", new Vector2(STAGE_WIDTH, boardHeight), new Vector2(0f, stageTop - (boardHeight * 0.5f)), corner);

            // Squares off the board plate's bottom corners so it meets the strip in a straight line.
            RectTransform fillerRect = HudChrome.CreateRect(
                _root, "BoardPlateFiller", new Vector2(STAGE_WIDTH, corner),
                new Vector2(0f, stageTop - boardHeight + (corner * 0.5f)));
            _boardPlateFillerImage = fillerRect.gameObject.AddComponent<Image>();
            _boardPlateFillerImage.raycastTarget = false;

            float dividerThickness = MOCK_DIVIDER_THICKNESS * SCALE;
            RectTransform dividerRect = HudChrome.CreateRect(
                _root, "StripDivider", new Vector2(STAGE_WIDTH, dividerThickness), new Vector2(0f, stageTop - boardHeight));
            _dividerImage = dividerRect.gameObject.AddComponent<Image>();
            _dividerImage.raycastTarget = false;

            BuildEmptyCells();

            GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(CanvasGroup), typeof(RectMask2D));
            RectTransform contentRect = (RectTransform)contentObject.transform;
            contentRect.SetParent(_root, false);
            HudChrome.Centre(contentRect, new Vector2(STAGE_WIDTH, stageHeight));
            contentRect.anchoredPosition = new Vector2(0f, stageTop - (stageHeight * 0.5f));
            _contentGroup = contentObject.GetComponent<CanvasGroup>();
            _contentGroup.interactable = false;
            _contentGroup.blocksRaycasts = false;

            // Layers back to front; each re-centred on the stage so every element positions in the same
            // stage-local space ToPixels returns.
            _bandLayer = CreateLayer(contentRect, "Bands", contentRect.anchoredPosition);
            _blockLayer = CreateLayer(contentRect, "Blocks", contentRect.anchoredPosition);
            _outlineLayer = CreateLayer(contentRect, "Outlines", contentRect.anchoredPosition);
            _glowLayer = CreateLayer(contentRect, "Glows", contentRect.anchoredPosition);
            _iconLayer = CreateLayer(contentRect, "Icons", contentRect.anchoredPosition);
            _pieceLayer = CreateLayer(contentRect, "Pieces", contentRect.anchoredPosition);
            _labelLayer = CreateLayer(contentRect, "Labels", contentRect.anchoredPosition);

            for (int cellIndex = 0; cellIndex < InfoDemoLayout.BOARD_CELL_COUNT; cellIndex++)
            {
                CellView block = CellFactory.CreateCell(_blockLayer, $"Block_{cellIndex}", _cellPixels, _cellInset, _cellBevel);
                block.gameObject.SetActive(false);
                _blockCells[cellIndex] = block;
            }

            float trackHeight = MOCK_PROGRESS_HEIGHT * SCALE;
            float trackCentreY = -stageTop + (trackHeight * 0.5f);
            _progressTrackImage = HudChrome.BuildRounded(
                _root, "ProgressTrack", new Vector2(STAGE_WIDTH, trackHeight), new Vector2(0f, trackCentreY), trackHeight * 0.5f);
            _progressFillImage = HudChrome.BuildRounded(
                _root, "ProgressFill", new Vector2(trackHeight, trackHeight), new Vector2(-STAGE_WIDTH * 0.5f, trackCentreY), trackHeight * 0.5f);
            _progressFillRect = (RectTransform)_progressFillImage.transform;
            _progressFillRect.pivot = new Vector2(0f, 0.5f);

            if (_theme != null)
            {
                PaintStatics();
            }
        }

        /// <summary>A layer the size of the stage whose origin sits at the stage centre, parented under
        /// the (clipped, fading) content rect centred at <paramref name="contentOffset"/>.</summary>
        private static RectTransform CreateLayer(RectTransform contentRect, string objectName, Vector2 contentOffset)
        {
            GameObject layerObject = new GameObject(objectName, typeof(RectTransform));
            RectTransform layerRect = (RectTransform)layerObject.transform;
            layerRect.SetParent(contentRect, false);
            HudChrome.Centre(layerRect, Size);
            layerRect.anchoredPosition = -contentOffset;
            return layerRect;
        }

        /// <summary>The board's empty cells: the same flat look as <see cref="CellView.SetColours"/> —
        /// an outline-coloured rounded square with an inset face — as two plain Images each, since an
        /// empty demo cell never changes and does not need a whole <see cref="CellView"/>.</summary>
        private void BuildEmptyCells()
        {
            GameObject layerObject = new GameObject("EmptyCells", typeof(RectTransform));
            RectTransform layerRect = (RectTransform)layerObject.transform;
            layerRect.SetParent(_root, false);
            HudChrome.Centre(layerRect, Size);

            float cornerRadius = _cellPixels * CellFactory.CORNER_RADIUS_FRACTION;
            float faceSize = _cellPixels - (_cellInset * 2f);

            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    int cellIndex = InfoDemoLayout.BoardBlockId(row, column);
                    Vector2 centre = ToPixels(InfoDemoLayout.Cell(row, column));
                    _emptyCellOutlines[cellIndex] = HudChrome.BuildRounded(
                        layerRect, $"Empty_{cellIndex}", new Vector2(_cellPixels, _cellPixels), centre, cornerRadius);
                    _emptyCellFaces[cellIndex] = HudChrome.BuildRounded(
                        layerRect, $"EmptyFace_{cellIndex}", new Vector2(faceSize, faceSize), centre,
                        Mathf.Max(1f, cornerRadius - _cellInset));
                }
            }
        }

        private void PaintStatics()
        {
            _stageBaseImage.color = Color.Lerp(_theme.CardBackground, _theme.Ink, 0.06f);
            Color boardTone = Color.Lerp(_theme.CardBackground, _theme.EmptyCellOutline, 0.35f);
            _boardPlateImage.color = boardTone;
            _boardPlateFillerImage.color = boardTone;
            _dividerImage.color = _theme.EmptyCellOutline;
            _progressTrackImage.color = Color.Lerp(_theme.CardBackground, _theme.Ink, 0.08f);
            _progressFillImage.color = _theme.Accent;

            for (int cellIndex = 0; cellIndex < InfoDemoLayout.BOARD_CELL_COUNT; cellIndex++)
            {
                _emptyCellOutlines[cellIndex].color = _theme.EmptyCellOutline;
                _emptyCellFaces[cellIndex].color = _theme.EmptyCellFill;
            }
        }
    }
}
