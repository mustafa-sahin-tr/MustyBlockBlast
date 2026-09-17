using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The Hold slot ("pocket"): one plate showing the single parked piece, or an empty outline when
    /// nothing is parked. Reads <see cref="TrayModel"/> only.
    /// <para>
    /// Centred beneath <see cref="PieceTrayView"/>'s card, sharing its horizontal centre, rather than
    /// corner-anchored near the score readout: the pocket is a tray affordance, not a HUD button, and
    /// sitting far from the tray it swaps pieces with was read as an unrelated, half-disabled control.
    /// The gap below the tray card is the only clearance the layout has — the power-up strip sits
    /// directly above the tray with almost none to spare, and the tray card itself already spans nearly
    /// the full canvas width — so "below" is the one placement that cannot collide with a tray piece at
    /// its largest footprint or with the power-up strip at any aspect ratio.
    /// </para>
    /// <para>
    /// Like <see cref="PowerUpInventoryView"/> it knows how to draw itself and whether a screen point is
    /// on it, nothing more. The gesture that fills it — dropping a dragged dock piece here — is routed
    /// by <see cref="BoardInputView"/>, the single owner of pointer input in this scene, which also asks
    /// this View to light up while a drag hovers it.
    /// </para>
    /// <para>
    /// There is no gesture for taking a piece <em>out</em> of the pocket, and none is needed: dropping
    /// any dock piece here swaps the two, so the parked piece comes back to the dock the moment another
    /// one is parked. That single gesture is the whole loop.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoldSlotView : MonoBehaviour
    {
        /// <summary>Alpha of the plate while the pocket is empty, so "nothing parked" reads at a glance.</summary>
        private const float EMPTY_PLATE_ALPHA = 0.45f;

        /// <summary>Alpha of the empty-state arrow and label. Kept well above <see cref="EMPTY_PLATE_ALPHA"/>
        /// so the "drop a piece here" hint stays legible even while the plate itself fades.</summary>
        private const float EMPTY_HINT_ALPHA = 0.85f;

        [Header("Layout")]
        [FormerlySerializedAs("_cornerOffset")]
        [Tooltip("Plate centre in canvas space. Sits below the piece tray, sharing its horizontal centre.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -897f);

        [SerializeField] private float _slotSize = 104f;

        [Header("Parked piece")]
        [Tooltip("Side of the square the parked piece's miniature is scaled to fit, inside the plate.")]
        [SerializeField] private float _pieceAreaSize = 84f;

        [Tooltip("Largest cell size the miniature uses. Small pieces stop growing here rather than filling the plate.")]
        [SerializeField] private float _maxPieceCellSize = 26f;

        [Tooltip("Gap between miniature cells, as a fraction of the cell size.")]
        [SerializeField] private float _pieceCellSpacingFraction = 0.12f;

        [SerializeField] private float _cellInset = 1f;
        [SerializeField] private float _cellBevelThickness = 2f;

        [Header("Drag feedback")]
        [Tooltip("Scale applied while a dragged piece hovers the pocket, so the drop target reads without new art.")]
        [SerializeField] private float _hoverScale = 1.14f;

        [Header("Empty-state hint")]
        [Tooltip("Side of the downward arrow shown while the pocket is empty.")]
        [SerializeField] private float _emptyGlyphSize = 40f;

        [Tooltip("Font size of the short label under the plate while the pocket is empty.")]
        [SerializeField] private int _emptyLabelFontSize = 20;

        [Tooltip("Gap between the plate's bottom edge and the empty-state label.")]
        [SerializeField] private float _emptyLabelGap = 10f;

        private readonly List<CellView> _pieceCells = new List<CellView>(9);

        /// <summary>Reused by the per-drag-frame overlap test so it allocates nothing.</summary>
        private readonly Vector3[] _plateCorners = new Vector3[4];

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private RectTransform _rectTransform;
        private RectTransform _plateRect;
        private RectTransform _pieceRoot;
        private Canvas _canvas;
        private Image _plateImage;
        private Image _shadowImage;
        private Image _emptyGlyphImage;
        private Text _emptyLabelText;

        private TrayModel _trayModel;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ThemeDefinition _currentTheme;
        private bool _isHovered;

        [Inject]
        public void Construct(
            TrayModel trayModel,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem)
        {
            _trayModel = trayModel;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
        }

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
            Build();
        }

        private void Start()
        {
            if (_trayModel == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null)
            {
                Debug.LogError(
                    $"{nameof(HoldSlotView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Subscribed first so _currentTheme is set before the initial rebuild paints a cell.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);

            _trayModel.HeldChanged += OnHeldChanged;
            RebuildHeldPiece();
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_trayModel != null)
            {
                _trayModel.HeldChanged -= OnHeldChanged;
            }
        }

        /// <summary>True when <paramref name="screenBounds"/> — the dragged piece's screen-space bounding
        /// box — overlaps the pocket's plate at all. An overlap rather than a point test because the
        /// plate is one small square and the finger both sits offset from the piece and hides it: asking
        /// for the piece's exact centre made parking a piece a multi-attempt gesture.</summary>
        internal bool Overlaps(Rect screenBounds)
        {
            if (_plateRect == null)
            {
                return false;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            // Cached array: this runs every drag frame.
            _plateRect.GetWorldCorners(_plateCorners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(eventCamera, _plateCorners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(eventCamera, _plateCorners[2]);

            Rect plateScreenRect = Rect.MinMaxRect(
                Mathf.Min(bottomLeft.x, topRight.x),
                Mathf.Min(bottomLeft.y, topRight.y),
                Mathf.Max(bottomLeft.x, topRight.x),
                Mathf.Max(bottomLeft.y, topRight.y));

            // The pocket scales up while hovered, so measuring the lit plate would widen the hit area
            // the moment it lights and leave a sticky band the player never asked for. Measured at the
            // resting size instead, so what lights up is exactly what registers on release.
            float hoverScale = _rectTransform.localScale.x;
            if (hoverScale > 0f && !Mathf.Approximately(hoverScale, 1f))
            {
                Vector2 centre = plateScreenRect.center;
                Vector2 halfSize = plateScreenRect.size * (0.5f / hoverScale);
                plateScreenRect = Rect.MinMaxRect(
                    centre.x - halfSize.x, centre.y - halfSize.y, centre.x + halfSize.x, centre.y + halfSize.y);
            }

            return plateScreenRect.Overlaps(screenBounds);
        }

        /// <summary>Lights the pocket while a dragged piece hovers it, so the drop target is obvious
        /// before the player commits. Idempotent — the input View calls this every drag frame.</summary>
        internal void SetHovered(bool isHovered)
        {
            if (_isHovered == isHovered)
            {
                return;
            }

            _isHovered = isHovered;
            RefreshPlate();
        }

        private void OnHeldChanged() => RebuildHeldPiece();

        private void OnLocaleChanged(LocaleDefinition locale)
            => _emptyLabelText.text = _localizationSystem.Translate(LocalizationKeys.HOLD_SLOT_EMPTY_HINT);

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            RefreshPlate();

            int colourId = _trayModel != null ? _trayModel.HeldColourId : Board.EMPTY;

            // Re-read rather than cached, for the reason PieceTrayView re-reads it: a golden piece is
            // painted gold instead of in the theme's colours, and a repaint that ignored the kind would
            // quietly demote it on the next theme switch.
            SpecialPieceKind specialKind =
                _trayModel != null ? _trayModel.HeldSpecialKind : SpecialPieceKind.None;
            for (int i = 0; i < _pieceCells.Count; i++)
            {
                ApplyCellLook(_pieceCells[i], colourId, specialKind);
            }
        }

        /// <summary>Repaints the plate from the two things that change how it looks: whether something
        /// is parked, and whether a drag is hovering it.</summary>
        private void RefreshPlate()
        {
            if (_currentTheme == null || _plateImage == null)
            {
                return;
            }

            bool isOccupied = _trayModel != null && _trayModel.IsHoldOccupied;
            float alpha = isOccupied ? 1f : EMPTY_PLATE_ALPHA;

            // Hovering inverts the plate to the accent colour, the same "selected" language the
            // power-up strip uses for an armed icon — no second sprite needed.
            Color plateColour = _isHovered ? _currentTheme.Accent : _currentTheme.CardBackground;

            _plateImage.color = WithAlpha(plateColour, _isHovered ? 1f : alpha);
            _shadowImage.color = WithAlpha(_currentTheme.CardShadow, alpha);

            Color hintColour = isOccupied ? Color.clear : WithAlpha(_currentTheme.SoftInk, EMPTY_HINT_ALPHA);
            _emptyGlyphImage.color = hintColour;
            _emptyLabelText.color = hintColour;

            float scale = _isHovered ? _hoverScale : 1f;
            _rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Paints one cell of the parked piece. A special piece keeps its look through the
        /// pocket — the kind travels with the piece in both directions (see
        /// <c>BoardSystem.TryHoldPiece</c>), so parking a golden 1x1 must not make it look ordinary.
        /// A hammer can never get here: parking one is refused.</summary>
        private void ApplyCellLook(CellView cell, int colourId, SpecialPieceKind specialKind)
        {
            if (_currentTheme == null)
            {
                return;
            }

            SpecialPieceVisuals.Apply(cell, specialKind, _currentTheme, colourId);
        }

        private void RebuildHeldPiece()
        {
            for (int i = 0; i < _pieceCells.Count; i++)
            {
                Destroy(_pieceCells[i].gameObject);
            }

            _pieceCells.Clear();

            Piece piece = _trayModel != null ? _trayModel.HeldPiece : null;
            if (piece == null)
            {
                RefreshPlate();
                return;
            }

            int colourId = _trayModel.HeldColourId;
            SpecialPieceKind specialKind = _trayModel.HeldSpecialKind;
            PieceLayout.GetBounds(piece, out int width, out int height);

            // Scaled to fit rather than drawn at one fixed cell size: the pocket is a single small
            // plate and the pieces range from 1x1 to 1x5, so a size that lets the longest piece fit
            // would leave the smallest one a speck.
            int longestSide = width > height ? width : height;
            float cellSize = Mathf.Min(
                _maxPieceCellSize, _pieceAreaSize / (longestSide + ((longestSide - 1) * _pieceCellSpacingFraction)));
            float pitch = cellSize * (1f + _pieceCellSpacingFraction);
            float offsetX = -((width - 1) * pitch) * 0.5f;
            float offsetY = -((height - 1) * pitch) * 0.5f;

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition offset = piece.Offsets[i];
                CellView cell = CellFactory.CreateCell(
                    _pieceRoot, $"HoldCell_{i}", cellSize, _cellInset, _cellBevelThickness);
                var rect = (RectTransform)cell.transform;
                rect.anchoredPosition = new Vector2(offsetX + (offset.X * pitch), offsetY + (offset.Y * pitch));
                ApplyCellLook(cell, colourId, specialKind);
                _pieceCells.Add(cell);
            }

            RefreshPlate();
        }

        private void Build()
        {
            // Centre-anchored like PieceTrayView, so _anchoredPosition sits in the same coordinate
            // space as the tray it is placed relative to.
            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.sizeDelta = new Vector2(_slotSize, _slotSize);
            _rectTransform.anchoredPosition = _anchoredPosition;

            // Every size here is in canvas reference units, so the pocket owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            _rectTransform.localScale = Vector3.one;

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(_rectTransform, false);
            Centre(shadowRect, new Vector2(_slotSize + 10f, _slotSize + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImage = ConfigurePlate(shadowObject.GetComponent<Image>());

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            _plateRect = (RectTransform)plateObject.transform;
            _plateRect.SetParent(_rectTransform, false);
            Centre(_plateRect, new Vector2(_slotSize, _slotSize));
            _plateImage = ConfigurePlate(plateObject.GetComponent<Image>());

            // A downward arrow into the plate, not the old translucent square outline: an empty pocket
            // otherwise reads as a dim, disabled button rather than a live drop target. Rotated 180°
            // from the shared upward RocketIcon, so no new art is drawn just for this.
            var glyphObject = new GameObject("EmptyGlyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(_plateRect, false);
            Centre(glyphRect, new Vector2(_emptyGlyphSize, _emptyGlyphSize));
            glyphRect.localRotation = Quaternion.Euler(0f, 0f, 180f);
            _emptyGlyphImage = glyphObject.GetComponent<Image>();
            _emptyGlyphImage.sprite = UiSpriteFactory.RocketIcon;
            _emptyGlyphImage.type = Image.Type.Simple;
            _emptyGlyphImage.color = Color.clear;
            _emptyGlyphImage.raycastTarget = false;

            var pieceObject = new GameObject("HeldPiece", typeof(RectTransform));
            _pieceRoot = (RectTransform)pieceObject.transform;
            _pieceRoot.SetParent(_plateRect, false);
            Centre(_pieceRoot, Vector2.zero);

            // Below the plate rather than inside it: the plate is one small square shared with the
            // parked-piece miniature, with no room to also fit a legible word.
            _emptyLabelText = UiTextFactory.Create(
                _rectTransform, "EmptyLabel", _emptyLabelFontSize, FontStyle.Bold, Color.clear);
            var labelRect = (RectTransform)_emptyLabelText.transform;
            labelRect.anchoredPosition = new Vector2(0f, -((_slotSize * 0.5f) + _emptyLabelGap));
        }

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Raycasts stay off everywhere: pointer events arrive through BoardInputView's own action, not
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
    }
}
