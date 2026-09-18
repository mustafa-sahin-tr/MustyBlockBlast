using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Renders the three offered pieces in the storefront tray card below the power-up strip (issue
    /// #265): a card-coloured plate over a dropped shadow, three sunken wells the pieces sit in, a thin
    /// divider, and the space to its right that <see cref="HoldSlotView"/>'s pocket occupies. Reads
    /// <see cref="TrayModel"/> only; picking pieces up is the input View's job.
    /// <para>
    /// The pocket is not built here: it is its own View with its own drag and tap hit-testing, and it
    /// simply sits over the card's right-hand bay. The card reserves that bay so the two never overlap
    /// and the wells are laid out around it.
    /// </para>
    /// <para>
    /// A slot whose piece carries a <see cref="SpecialPieceKind"/> is painted through
    /// <see cref="SpecialPieceVisuals"/> — gold for a golden 1x1, a glyph on an ordinary plate for the
    /// rocket and the hammer — which is the same seam the pocket and the drag ghost paint through, so
    /// one piece looks the same wherever it is.
    /// </para>
    /// <para>
    /// A slot is rebuilt from scratch whenever its piece changes, and the centring offsets are derived
    /// from that piece's own bounds each time — so a slot whose piece changed shape (the Rotate
    /// power-up swapping in another orientation) re-centres its new bounding box with no extra work.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PieceTrayView : MonoBehaviour
    {
        /// <summary>Corner radius of the tray card: the mockup's 22px at the canvas's scale.</summary>
        private const float CARD_CORNER_RADIUS = 60f;

        /// <summary>Corner radius of a well: the mockup's 16px.</summary>
        private const float WELL_CORNER_RADIUS = 44f;

        /// <summary>The card's drop: the mockup's 8px, the same as the board's.</summary>
        private const float CARD_SHADOW_DROP = 16f;

        /// <summary>Width of the divider between the wells and the pocket bay: the mockup's 2px.</summary>
        private const float DIVIDER_WIDTH = 5f;

        /// <summary>Height of the divider as a fraction of the wells' height: the mockup's 70 on 90.</summary>
        private const float DIVIDER_HEIGHT_FRACTION = 0.78f;

        /// <summary>Thickness of the ring on the well a Ghost Fit suggestion points at: the mockup's 3px.</summary>
        private const float HINT_RING_THICKNESS = 8f;

        /// <summary>Which theme kind the hint ring borrows — the same kind as the board's ghost
        /// silhouette, so the two halves of one suggestion are the one colour.</summary>
        private const int HINT_KIND = 2;

        [Header("Layout")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -720f);
        [SerializeField] private Vector2 _cardSize = new Vector2(960f, 260f);

        [Tooltip("Inset from the card's edges to the wells and the pocket bay.")]
        [SerializeField] private float _cardPadding = 28f;

        [Tooltip("Gap between neighbouring wells, and between the last well, the divider and the pocket bay.")]
        [SerializeField] private float _slotGap = 18f;

        [Tooltip("Width reserved at the card's right edge for the pocket (HoldSlotView).")]
        [SerializeField] private float _pocketWidth = 200f;

        [SerializeField] private float _trayCellSize = 37f;
        [SerializeField] private float _trayCellSpacing = 4f;

        [Header("Cell style")]
        [SerializeField] private float _cellInset = 2f;
        [SerializeField] private float _cellBevelThickness = 5f;

        [Header("Rotate aim")]
        [Tooltip("Scale applied to the slot a Rotate is being aimed at, marking it as a live target.")]
        [SerializeField] private float _aimedSlotScale = 1.06f;

        private readonly List<CellView>[] _slotCells = new List<CellView>[TrayModel.SLOT_COUNT];
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private RectTransform _rectTransform;
        private RectTransform[] _slotRects;
        private Image[] _wellLipImages;
        private Image[] _wellFaceImages;
        private Image[] _hintRingImages;

        /// <summary>Per slot: the child the piece's cells hang from. The aim highlight scales this
        /// rather than the slot itself, so lifting a slot never moves the rect
        /// <see cref="GetSlotIndexAt"/> hit-tests against and the target cannot shift under the finger
        /// that is aiming at it.</summary>
        private RectTransform[] _slotContentRects;

        private Image _cardShadowImage;
        private Image _cardPlateImage;
        private Image _dividerImage;

        /// <summary>The slot currently lifted as a Rotate target, or -1 for none.</summary>
        private int _aimedSlot = -1;

        /// <summary>The slot currently pulsing as a Ghost Fit suggestion, or -1 for none.</summary>
        private int _hintedSlot = -1;
        private Canvas _canvas;
        private TrayModel _trayModel;
        private SettingsModel _settingsModel;
        private ThemeDefinition _currentTheme;

        [Inject]
        public void Construct(TrayModel trayModel, SettingsModel settingsModel)
        {
            _trayModel = trayModel;
            _settingsModel = settingsModel;
        }

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();

            HudChrome.Centre(_rectTransform, _cardSize);
            _rectTransform.anchoredPosition = _anchoredPosition;

            // Every size here is in canvas reference units, so the tray owns its own scale rather than
            // inheriting whatever the scene object happened to be created with.
            _rectTransform.localScale = Vector3.one;

            RectTransform card = HudChrome.BuildPlate(
                _rectTransform, "TrayCard", _cardSize, Vector2.zero, CARD_CORNER_RADIUS, CARD_SHADOW_DROP,
                out _cardShadowImage, out _cardPlateImage);

            BuildSlots(card);
        }

        private void Start()
        {
            if (_trayModel == null || _settingsModel == null)
            {
                Debug.LogError($"{nameof(PieceTrayView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Subscribed first so _currentTheme is set before the initial slot rebuild paints a cell.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _trayModel.SlotChanged += OnSlotChanged;
            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                RebuildSlot(i);
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_trayModel != null)
            {
                _trayModel.SlotChanged -= OnSlotChanged;
            }
        }

        /// <summary>Slot index under a screen point, or -1.</summary>
        internal int GetSlotIndexAt(Vector2 screenPosition)
        {
            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_slotRects[i], screenPosition, eventCamera))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The on-screen rect of dock slot <paramref name="slotIndex"/>, for
        /// <see cref="TutorialOverlayView"/> to spotlight and for <see cref="BoardInputView"/>'s
        /// tutorial input guard to hit-test against. Null when out of range or not yet built.</summary>
        internal RectTransform GetSlotRectTransform(int slotIndex)
        {
            if (_slotRects == null || slotIndex < 0 || slotIndex >= _slotRects.Length)
            {
                return null;
            }

            return _slotRects[slotIndex];
        }

        /// <summary>
        /// Marks one dock slot as the live target of an armed Rotate, or -1 for none. Lifting the slot
        /// costs no extra object and no extra draw call — the same trick the armed power-up icon uses —
        /// and the lift is small enough that the widest piece the tray can hold still clears its
        /// neighbours.
        /// </summary>
        internal void SetAimedSlot(int slotIndex)
        {
            if (_aimedSlot == slotIndex || _slotContentRects == null)
            {
                return;
            }

            if (_aimedSlot >= 0)
            {
                _slotContentRects[_aimedSlot].localScale = Vector3.one;
            }

            _aimedSlot = slotIndex;

            if (_aimedSlot >= 0)
            {
                _slotContentRects[_aimedSlot].localScale =
                    new Vector3(_aimedSlotScale, _aimedSlotScale, 1f);
            }
        }

        /// <summary>
        /// Pulses one dock slot as the piece a Ghost Fit suggestion points at, or -1 for none: the
        /// piece lifts a little and the well wears a ring in the suggestion's colour, both breathing
        /// with <paramref name="pulse"/>. It runs 0..1 and is driven by the caller's animation, so this
        /// stays a pure "draw it this big" instruction with no clock of its own.
        /// <para>
        /// Shares <see cref="SetAimedSlot"/>'s scale trick and, deliberately, its scale ceiling, so a
        /// lifted slot reads the same however it was lifted and still clears its neighbours at the
        /// widest piece. The two can never fight over the same slot: a suggestion is dismissed by the
        /// press that arms anything, so no Rotate can be aimed while one is up.
        /// </para>
        /// </summary>
        internal void SetHintedSlot(int slotIndex, float pulse)
        {
            if (_slotContentRects == null)
            {
                return;
            }

            if (_hintedSlot >= 0 && _hintedSlot != slotIndex)
            {
                _slotContentRects[_hintedSlot].localScale = Vector3.one;
                _hintRingImages[_hintedSlot].color = Color.clear;
            }

            _hintedSlot = slotIndex;

            if (_hintedSlot < 0)
            {
                return;
            }

            float breath = Mathf.Clamp01(pulse);
            float scale = Mathf.Lerp(1f, _aimedSlotScale, breath);
            _slotContentRects[_hintedSlot].localScale = new Vector3(scale, scale, 1f);

            if (_currentTheme != null)
            {
                _hintRingImages[_hintedSlot].color = HudChrome.WithAlpha(_currentTheme.GetFill(HINT_KIND), breath);
            }
        }

        internal void SetSlotVisible(int slotIndex, bool isVisible)
        {
            List<CellView> cells = _slotCells[slotIndex];
            for (int i = 0; i < cells.Count; i++)
            {
                cells[i].gameObject.SetActive(isVisible);
            }
        }

        /// <summary>
        /// Lays the three wells out left to right inside the card's padding, leaving the pocket bay at
        /// the right edge, with the divider in the gap before it. The wells' width is whatever is left
        /// once the bay, the divider and the gaps are taken out, so a wider pocket narrows the wells
        /// rather than pushing anything off the card.
        /// </summary>
        private void BuildSlots(RectTransform card)
        {
            _slotRects = new RectTransform[TrayModel.SLOT_COUNT];
            _slotContentRects = new RectTransform[TrayModel.SLOT_COUNT];
            _wellLipImages = new Image[TrayModel.SLOT_COUNT];
            _wellFaceImages = new Image[TrayModel.SLOT_COUNT];
            _hintRingImages = new Image[TrayModel.SLOT_COUNT];

            float wellHeight = _cardSize.y - (_cardPadding * 2f);
            float wellsSpan = _cardSize.x - (_cardPadding * 2f) - _pocketWidth - DIVIDER_WIDTH - (_slotGap * 2f)
                - ((TrayModel.SLOT_COUNT - 1) * _slotGap);
            float wellWidth = wellsSpan / TrayModel.SLOT_COUNT;
            float pitch = wellWidth + _slotGap;
            float originX = (-_cardSize.x * 0.5f) + _cardPadding + (wellWidth * 0.5f);
            var wellSize = new Vector2(wellWidth, wellHeight);

            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                var position = new Vector2(originX + (i * pitch), 0f);
                RectTransform slotRect = HudChrome.BuildWell(
                    card, $"Slot_{i}", wellSize, position, WELL_CORNER_RADIUS,
                    out _wellLipImages[i], out _wellFaceImages[i]);

                _hintRingImages[i] = HudChrome.BuildOutline(
                    slotRect, "HintRing", wellSize, Vector2.zero, WELL_CORNER_RADIUS, HINT_RING_THICKNESS);

                RectTransform contentRect = HudChrome.CreateRect(slotRect, "Content", wellSize, Vector2.zero);

                _slotRects[i] = slotRect;
                _slotContentRects[i] = contentRect;
                _slotCells[i] = new List<CellView>(9);
            }

            float dividerX = originX + ((TrayModel.SLOT_COUNT - 1) * pitch) + (wellWidth * 0.5f) + _slotGap
                + (DIVIDER_WIDTH * 0.5f);
            _dividerImage = HudChrome.BuildRounded(
                card, "Divider", new Vector2(DIVIDER_WIDTH, wellHeight * DIVIDER_HEIGHT_FRACTION),
                new Vector2(dividerX, 0f), DIVIDER_WIDTH * 0.5f);
        }

        private void OnSlotChanged(int slotIndex) => RebuildSlot(slotIndex);

        /// <summary>Adopts a new theme: repaints the card, the wells and the pieces currently sitting
        /// in the tray, so a mid-run theme switch is not deferred until the next draw.</summary>
        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            _cardShadowImage.color = theme.CardShadow;
            _cardPlateImage.color = theme.CardBackground;
            _dividerImage.color = theme.EmptyCellFill;

            Color wellLip = HudChrome.WellLipTint(theme.CardBackground, theme.Ink);
            Color wellFace = HudChrome.WellTint(theme.CardBackground, theme.Ink);

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _wellLipImages[slotIndex].color = wellLip;
                _wellFaceImages[slotIndex].color = wellFace;

                List<CellView> cells = _slotCells[slotIndex];
                if (cells == null || cells.Count == 0)
                {
                    continue;
                }

                int colourId = _trayModel.GetColourId(slotIndex);

                // Re-read rather than cached: a golden piece is painted gold instead of in the theme's
                // colours, so a repaint that ignored the kind would quietly demote it to an ordinary
                // block the next time the player switched theme.
                SpecialPieceKind specialKind = _trayModel.GetSpecialKind(slotIndex);
                for (int i = 0; i < cells.Count; i++)
                {
                    ApplyCellLook(cells[i], colourId, specialKind);
                }
            }
        }

        private void ApplyCellLook(CellView cell, int colourId, SpecialPieceKind specialKind)
        {
            if (_currentTheme == null)
            {
                return;
            }

            SpecialPieceVisuals.Apply(cell, specialKind, _currentTheme, colourId);
        }

        private void RebuildSlot(int slotIndex)
        {
            List<CellView> cells = _slotCells[slotIndex];
            for (int i = 0; i < cells.Count; i++)
            {
                Destroy(cells[i].gameObject);
            }

            cells.Clear();

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                return;
            }

            int colourId = _trayModel.GetColourId(slotIndex);
            SpecialPieceKind specialKind = _trayModel.GetSpecialKind(slotIndex);
            PieceLayout.GetBounds(piece, out int width, out int height);

            float pitch = _trayCellSize + _trayCellSpacing;
            float offsetX = -((width - 1) * pitch) * 0.5f;
            float offsetY = -((height - 1) * pitch) * 0.5f;

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition offset = piece.Offsets[i];
                CellView cell = CellFactory.CreateCell(
                    _slotContentRects[slotIndex],
                    $"TrayCell_{i}",
                    _trayCellSize,
                    _cellInset,
                    _cellBevelThickness);
                var rect = (RectTransform)cell.transform;
                rect.anchoredPosition = new Vector2(offsetX + (offset.X * pitch), offsetY + (offset.Y * pitch));
                ApplyCellLook(cell, colourId, specialKind);
                cells.Add(cell);
            }
        }
    }
}
