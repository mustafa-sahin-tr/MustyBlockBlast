using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The Hold slot ("pocket"): the bay at the right end of <see cref="PieceTrayView"/>'s card showing
    /// the single parked piece, or the pocket glyph and its caption when nothing is parked, with a
    /// badge on its corner counting the Hold charges left. Reads <see cref="TrayModel"/> for what is
    /// parked and <see cref="PowerUpModel.HoldCount"/> for whether parking can be paid for.
    /// <para>
    /// Drawn in the storefront vocabulary (issue #265) and in the same language as the power-up strip:
    /// empty, the pocket is an outline ring with the glyph and the "pocket" caption inside it;
    /// occupied, it is a sunken well like the tray's own with the parked piece drawn in it. The badge
    /// is green with the count while the player holds a charge, and pink when none is left — "+" on
    /// an empty pocket, whose tap is the "earn one" gesture, and "x0" on an occupied one, whose piece
    /// is locked in until a charge is earned. The parked-piece miniature never dims: a piece stuck
    /// behind an empty inventory is still a real piece the player owns and must be able to see.
    /// </para>
    /// <para>
    /// It sits over the tray card's right-hand bay rather than being built by the tray: the pocket
    /// has its own drag-overlap and tap hit-testing, routed by <see cref="BoardInputView"/>, the
    /// single owner of pointer input in this scene, which also asks this View to light up while a drag
    /// hovers it. Its anchored position is authored to land in the bay the tray reserves.
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
        /// <summary>Corner radius of the pocket: the mockup's 16px, the tray wells' radius.</summary>
        private const float CORNER_RADIUS = 44f;

        /// <summary>Thickness of the empty pocket's outline: the mockup's 2px dashed border, drawn solid.</summary>
        private const float OUTLINE_THICKNESS = 6f;

        /// <summary>Thickness of the accent ring a hovering drag lights, and how far it stands off the
        /// pocket: the strip's armed halo.</summary>
        private const float HOVER_RING_THICKNESS = 8f;

        /// <summary>How far the well's face is pulled toward the accent while a drag hovers it.</summary>
        private const float HOVER_FACE_TINT = 0.3f;

        /// <summary>Alpha of the glyph and caption while no charge is left to park with: the invitation
        /// is still there, dimmed, and the pink badge says what to do about it.</summary>
        private const float NO_CHARGE_HINT_ALPHA = 0.5f;

        /// <summary>Diameter of the charge badge: the mockup's 22px chip.</summary>
        private const float BADGE_DIAMETER = 61f;

        /// <summary>How far the badge's centre sits inside the pocket's top-right corner: the mockup's 4px.</summary>
        private const float BADGE_INSET = 11f;

        /// <summary>Gap between the glyph's bottom edge and the caption's centre line.</summary>
        private const float CAPTION_GAP = 26f;

        /// <summary>Drawn in place of the count when the player holds no charge and nothing is parked:
        /// that state's tap is the "earn one" gesture, so the chip reads as an offer — the strip's label.</summary>
        private const string EARN_AFFORDANCE_LABEL = "+";

        /// <summary>Prefix of the charge count: the mockup's "x1" / "x0".</summary>
        private const string COUNT_PREFIX = "x";

        [Header("Layout")]
        [FormerlySerializedAs("_cornerOffset")]
        [Tooltip("Pocket centre in canvas space. Authored to sit in the bay PieceTrayView reserves at its card's right edge.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(352f, -720f);

        [Tooltip("Size of the pocket, in reference pixels. Fits inside the tray card's bay.")]
        [SerializeField] private Vector2 _pocketSize = new Vector2(200f, 204f);

        [Header("Parked piece")]
        [Tooltip("Side of the square the parked piece's miniature is scaled to fit, inside the pocket.")]
        [SerializeField] private float _pieceAreaSize = 150f;

        [Tooltip("Largest cell size the miniature uses. Small pieces stop growing here rather than filling the pocket.")]
        [SerializeField] private float _maxPieceCellSize = 44f;

        [Tooltip("Gap between miniature cells, as a fraction of the cell size.")]
        [SerializeField] private float _pieceCellSpacingFraction = 0.12f;

        [SerializeField] private float _cellInset = 1f;
        [SerializeField] private float _cellBevelThickness = 5f;

        [Header("Drag feedback")]
        [Tooltip("Scale applied while a dragged piece hovers the pocket, so the drop target reads without new art.")]
        [SerializeField] private float _hoverScale = 1.08f;

        [Header("Empty-state hint")]
        [Tooltip("Side of the pocket glyph shown while the pocket is empty.")]
        [FormerlySerializedAs("_emptyGlyphSize")]
        [SerializeField] private float _glyphSize = 66f;

        [Tooltip("Font size of the short uppercase caption under the glyph while the pocket is empty.")]
        [FormerlySerializedAs("_emptyLabelFontSize")]
        [SerializeField] private int _captionFontSize = 26;

        [Header("Art")]
        [Tooltip("White pocket silhouette shown while the pocket is empty. Tinted with the soft ink; hidden when unassigned.")]
        [SerializeField] private Sprite _pocketSprite;

        [Tooltip("The heavy label face for the caption and the charge badge. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _labelFont;

        private readonly List<CellView> _pieceCells = new List<CellView>(9);
        private readonly StringBuilder _countBuilder = new StringBuilder(8);

        /// <summary>Reused by the per-drag-frame overlap test so it allocates nothing.</summary>
        private readonly Vector3[] _plateCorners = new Vector3[4];

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private RectTransform _rectTransform;
        private RectTransform _plateRect;
        private RectTransform _pieceRoot;
        private Canvas _canvas;
        private Image _hoverRingImage;
        private Image _wellLipImage;
        private Image _wellFaceImage;
        private Image _outlineImage;
        private Image _glyphImage;
        private Text _captionText;
        private Image _badgeRimImage;
        private Image _badgeDiscImage;
        private Text _countText;

        private TrayModel _trayModel;
        private PowerUpModel _powerUpModel;
        private PowerUpSystem _powerUpSystem;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private BoardView _boardView;
        private ThemeDefinition _currentTheme;
        private bool _isHovered;
        private int _holdCount;
        private bool _isRequestingReward;

        [Inject]
        public void Construct(
            TrayModel trayModel,
            PowerUpModel powerUpModel,
            PowerUpSystem powerUpSystem,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            BoardView boardView)
        {
            _trayModel = trayModel;
            _powerUpModel = powerUpModel;
            _powerUpSystem = powerUpSystem;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;

            // Consulted for the diamond glyph sprite only (issue #395), exactly as PieceTrayView does,
            // so a parked decorated piece keeps the very gems it had in the tray.
            _boardView = boardView;
        }

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
            Build();
        }

        private void Start()
        {
            if (_trayModel == null || _powerUpModel == null || _powerUpSystem == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null || _boardView == null)
            {
                Debug.LogError(
                    $"{nameof(HoldSlotView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Subscribed first so _currentTheme is set before the initial rebuild paints a cell.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _powerUpModel.HoldCount.Subscribe(OnHoldCountChanged).AddTo(_disposables);

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
        /// box — overlaps the pocket at all. An overlap rather than a point test because the pocket is
        /// one small plate and the finger both sits offset from the piece and hides it: asking for the
        /// piece's exact centre made parking a piece a multi-attempt gesture.</summary>
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

        /// <summary>
        /// Resolves a press at <paramref name="screenPosition"/>. Returns true when it landed on the
        /// pocket and was consumed as the "earn one" gesture — only while the player holds no charge,
        /// because that is the one state in which a tap on the pocket means anything: with a charge the
        /// pocket is a drop target, reached by a drag that starts on the tray, never by a tap.
        /// </summary>
        internal bool TryHandleTap(Vector2 screenPosition)
        {
            if (_plateRect == null || _holdCount > 0 || !ContainsScreenPoint(screenPosition))
            {
                return false;
            }

            RequestReward();
            return true;
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

        /// <summary>The pocket's on-screen rect.</summary>
        internal RectTransform GetPocketRectTransform() => _plateRect;

        /// <summary>The white pocket silhouette shown while the pocket is empty, for
        /// <see cref="InfoPopupView"/> to reuse as the Hold popup's hero icon rather than authoring a
        /// second copy of it.</summary>
        internal Sprite PocketSprite => _pocketSprite;

        /// <summary>True when <paramref name="screenPosition"/> lands on the pocket. Used by
        /// <see cref="TryHandleTap"/> for the "earn one" gesture, and by <see cref="BoardInputView"/> to
        /// resolve the manual reopen gesture for the Hold info popup when a charge is already held (the
        /// one state <see cref="TryHandleTap"/> itself refuses).</summary>
        internal bool ContainsScreenPoint(Vector2 screenPosition)
        {
            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(_plateRect, screenPosition, eventCamera);
        }

        /// <summary>
        /// Fire-and-forget earn request, mirroring the strip's. The View banks nothing itself: the
        /// System increments and persists the inventory, and the count subscription bound in
        /// <see cref="Start"/> repaints the badge from that. A grant never parks anything.
        /// </summary>
        private void RequestReward()
        {
            if (_isRequestingReward)
            {
                return;
            }

            RequestRewardAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid RequestRewardAsync(CancellationToken cancellationToken)
        {
            _isRequestingReward = true;
            try
            {
                await _powerUpSystem.GrantRewardAsync(PowerUpKind.Hold, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The View went away mid-request. Nothing to undo: the System only banks a reward it
                // was actually handed.
            }
            finally
            {
                _isRequestingReward = false;
            }
        }

        private void OnHeldChanged() => RebuildHeldPiece();

        private void OnHoldCountChanged(int count)
        {
            _holdCount = count;
            RefreshPlate();
        }

        private void OnLocaleChanged(LocaleDefinition locale)
            => _captionText.text = _localizationSystem.Translate(LocalizationKeys.HUD_POCKET_LABEL);

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
                // Cells were added in Piece.Offsets order (RebuildHeldPiece), so the list index is the
                // offset index the model keys the diamond decoration by.
                ApplyCellLook(_pieceCells[i], colourId, specialKind, _trayModel.GetHeldDiamondColourId(i));
            }
        }

        /// <summary>Repaints the pocket from the three things that change how it looks: whether something
        /// is parked, whether a charge is left to park with, and whether a drag is hovering it.</summary>
        private void RefreshPlate()
        {
            if (_currentTheme == null || _wellFaceImage == null)
            {
                return;
            }

            bool isOccupied = _trayModel != null && _trayModel.IsHoldOccupied;
            bool hasCharge = _holdCount > 0;

            // Hovering lights the pocket in the accent, the same "selected" language the power-up strip
            // uses for an armed slot. Only when the drop could be paid for: lighting a pocket that will
            // refuse the piece would promise a park the System is about to decline.
            bool isLit = _isHovered && hasCharge;

            // Occupied, the pocket is a well like the tray's own; empty, an outline with the invitation
            // inside it. Never both.
            Color wellLip = HudChrome.WellLipTint(_currentTheme.CardBackground, _currentTheme.Ink);
            Color wellFace = HudChrome.WellTint(_currentTheme.CardBackground, _currentTheme.Ink);
            if (isLit)
            {
                wellFace = Color.Lerp(wellFace, _currentTheme.Accent, HOVER_FACE_TINT);
            }

            bool showWell = isOccupied || isLit;
            _wellLipImage.color = showWell ? wellLip : Color.clear;
            _wellFaceImage.color = showWell ? wellFace : Color.clear;
            _outlineImage.color = isOccupied ? Color.clear : _currentTheme.EmptyCellOutline;
            _hoverRingImage.color = isLit ? _currentTheme.Accent : Color.clear;

            // The glyph and caption are the invitation to park, so they show only while there is
            // room; dimmed while no charge could pay for the park, but not hidden — the pink badge
            // beside them says how to fix that.
            Color hintColour = isOccupied
                ? Color.clear
                : HudChrome.WithAlpha(_currentTheme.SoftInk, hasCharge ? 1f : NO_CHARGE_HINT_ALPHA);
            _glyphImage.color = _pocketSprite != null ? hintColour : Color.clear;
            _captionText.color = hintColour;

            // The badge states a number and has to be legible in every state. Two tones, as on the
            // strip: green means "you hold this many", pink means "none left" — "+" (tap to earn one)
            // while the pocket is empty, "x0" (the piece is locked in) while something is parked.
            _badgeRimImage.color = _currentTheme.CardBackground;
            _badgeDiscImage.color = hasCharge ? _currentTheme.GetFill(HudChrome.GREEN_KIND) : HudChrome.OfferPink;
            _countText.color = Color.white;

            _countBuilder.Clear();
            if (hasCharge || isOccupied)
            {
                _countBuilder.Append(COUNT_PREFIX);
                _countBuilder.Append(_holdCount);
            }
            else
            {
                _countBuilder.Append(EARN_AFFORDANCE_LABEL);
            }

            _countText.text = _countBuilder.ToString();

            float scale = isLit ? _hoverScale : 1f;
            _rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Paints one cell of the parked piece. A special piece keeps its look through the
        /// pocket — the kind travels with the piece in both directions (see
        /// <c>BoardSystem.TryParkPiece</c>), so parking a golden 1x1 must not make it look ordinary.
        /// A hammer can never get here: parking one is refused. The diamond decoration (issue #395)
        /// travels through the pocket the same way, and is painted on top of that look, never instead
        /// of it; <paramref name="diamondColourId"/> is <see cref="TrayModel.NO_DIAMOND"/> for an
        /// undecorated cell.</summary>
        private void ApplyCellLook(CellView cell, int colourId, SpecialPieceKind specialKind, int diamondColourId)
        {
            if (_currentTheme == null)
            {
                return;
            }

            SpecialPieceVisuals.Apply(cell, specialKind, _currentTheme, colourId);
            DiamondVisuals.Apply(cell, diamondColourId, _currentTheme, _boardView.CollectibleSprite(diamondColourId));
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
                ApplyCellLook(cell, colourId, specialKind, _trayModel.GetHeldDiamondColourId(i));
                _pieceCells.Add(cell);
            }

            RefreshPlate();
        }

        /// <summary>
        /// Bottom to top: the hover ring standing off the pocket, the well (lip and face), the empty
        /// outline on the same rect, the glyph with the caption under it, the parked piece's root, and
        /// the charge badge hung on the top-right corner. Everything is painted clear here and coloured
        /// by <see cref="RefreshPlate"/>.
        /// </summary>
        private void Build()
        {
            // Centre-anchored like PieceTrayView, so _anchoredPosition sits in the same coordinate
            // space as the tray it is placed relative to.
            HudChrome.Centre(_rectTransform, _pocketSize);
            _rectTransform.anchoredPosition = _anchoredPosition;

            // Every size here is in canvas reference units, so the pocket owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            _rectTransform.localScale = Vector3.one;

            var ringSize = new Vector2(
                _pocketSize.x + (HOVER_RING_THICKNESS * 2f), _pocketSize.y + (HOVER_RING_THICKNESS * 2f));
            _hoverRingImage = HudChrome.BuildOutline(
                _rectTransform, "HoverRing", ringSize, Vector2.zero,
                CORNER_RADIUS + HOVER_RING_THICKNESS, HOVER_RING_THICKNESS);

            _plateRect = HudChrome.BuildWell(
                _rectTransform, "Plate", _pocketSize, Vector2.zero, CORNER_RADIUS,
                out _wellLipImage, out _wellFaceImage);

            _outlineImage = HudChrome.BuildOutline(
                _plateRect, "Outline", _pocketSize, Vector2.zero, CORNER_RADIUS, OUTLINE_THICKNESS);

            // The glyph sits a little above centre so the caption under it leaves the pair centred.
            float captionHalfHeight = _captionFontSize * 0.7f;
            float glyphY = (CAPTION_GAP + captionHalfHeight) * 0.5f;
            _glyphImage = HudChrome.BuildGlyph(
                _plateRect, "PocketGlyph", _pocketSprite, new Vector2(_glyphSize, _glyphSize), new Vector2(0f, glyphY));

            _captionText = HudChrome.CreateLabel(
                _plateRect, "Caption", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, glyphY - (_glyphSize * 0.5f) - CAPTION_GAP), _labelFont);

            _pieceRoot = HudChrome.CreateRect(_plateRect, "HeldPiece", Vector2.zero, Vector2.zero);

            // A sibling of the plate rather than a child of it, and built last, so it draws over both
            // the plate and the parked-piece miniature — the same construction as the strip's chip, in
            // the same corner.
            var badgePosition = new Vector2((_pocketSize.x * 0.5f) - BADGE_INSET, (_pocketSize.y * 0.5f) - BADGE_INSET);
            HudChrome.BuildBadge(
                _rectTransform, "CountBadge", BADGE_DIAMETER, badgePosition, _labelFont,
                out _badgeRimImage, out _badgeDiscImage, out _countText);
        }
    }
}
