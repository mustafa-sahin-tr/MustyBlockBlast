using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The colour picker a <see cref="PowerUpKind.PaintCross"/> tap opens (issue #295): a bottom sheet
    /// with one swatch per block colour, a confirm button and the family's floating close cross. The
    /// tapped cell is held here, and only here, until the player confirms — at which point this View
    /// makes the one System call it exists to make, <see cref="PowerUpSystem.TryApplyPaintCross"/>.
    /// Cancelling (the close cross, or a tap on the scrim) makes no System call at all, which is what
    /// leaves the power-up armed and unspent so the player can aim again.
    /// <para>
    /// Deliberately not an <see cref="InfoCardChrome"/> card: that chrome is for the static icon + title
    /// + body explainers, and this is an interactive component with a swatch grid and an action button.
    /// It borrows only the floating close button, so "close" reads the same on every card.
    /// </para>
    /// <para>
    /// Modal like the other overlays and hit-tested by <see cref="BoardInputView"/> like them, but
    /// unlike them it holds no menu-pause of its own: it only ever opens while a Paint Cross is armed,
    /// and an armed power-up already holds the run's clock (see <see cref="PowerUpSystem.Arm"/>). It
    /// watches <see cref="PowerUpModel.Armed"/> instead, and closes itself the moment the armed
    /// selection stops being a Paint Cross — a run ending underneath it, or the confirm's own
    /// disarm — so it can never outlive the arm it belongs to.
    /// </para>
    /// <para>
    /// The palette is the game's whole colour set (<see cref="Board.COLOUR_COUNT"/> swatches, painted
    /// from the current theme's fills), not only the colours currently on the board: bringing an absent
    /// colour onto the board is part of what the power-up is for.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class PaintCrossColourPickerView : MonoBehaviour
    {
        /// <summary>One swatch per block colour id, <c>1..COLOUR_COUNT</c>.</summary>
        private const int SWATCH_COUNT = Board.COLOUR_COUNT;

        /// <summary>The first colour id, and the swatch selected the first time the sheet opens.</summary>
        private const int FIRST_COLOUR_ID = 1;

        /// <summary>The swatch's bottom bevel as a fraction of its side — the same lift a board block wears.</summary>
        private const float SWATCH_BEVEL_FRACTION = 0.08f;

        /// <summary>Corner radius of a swatch as a fraction of its side.</summary>
        private const float SWATCH_RADIUS_FRACTION = 0.22f;

        /// <summary>Thickness of the selected swatch's accent ring, and how far it stands off the swatch.</summary>
        private const float SELECTED_RING_THICKNESS = 8f;

        /// <summary>Corner multiplier the confirm plate is sliced at — the same as the picker's other
        /// wide buttons (<see cref="CoinSowerPickerView"/>).</summary>
        private const float BUTTON_CORNER_MULTIPLIER = 0.7f;

        [Header("Layout")]
        [Tooltip("Size of the sheet. Sits on the bottom edge of the safe area, centred.")]
        [SerializeField] private Vector2 _cardSize = new Vector2(920f, 560f);

        [Tooltip("Gap between the sheet's bottom edge and the safe area's.")]
        [SerializeField] private float _bottomMargin = 40f;

        [SerializeField] private float _titleY = 200f;
        [SerializeField] private float _swatchRowY = 40f;
        [SerializeField] private float _confirmButtonY = -170f;
        [SerializeField] private float _swatchSize = 120f;
        [SerializeField] private float _swatchGap = 28f;
        [SerializeField] private Vector2 _confirmButtonSize = new Vector2(520f, 110f);

        [Header("Type")]
        [SerializeField] private int _titleFontSize = 52;
        [SerializeField] private int _buttonFontSize = 44;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.35f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private readonly RectTransform[] _swatchRects = new RectTransform[SWATCH_COUNT];
        private readonly Image[] _swatchShadeImages = new Image[SWATCH_COUNT];
        private readonly Image[] _swatchFillImages = new Image[SWATCH_COUNT];
        private readonly Image[] _swatchRingImages = new Image[SWATCH_COUNT];

        private PowerUpModel _powerUpModel;
        private PowerUpSystem _powerUpSystem;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private InfoCardChrome.CloseButtonHandles _close;
        private Text _titleText;
        private RectTransform _confirmButtonRect;
        private Image _confirmPlateImage;
        private Image _confirmShadowImage;
        private Text _confirmText;

        private ThemeDefinition _currentTheme;

        /// <summary>The cell the player aimed at, valid while the sheet is open. The one piece of
        /// transient state this View holds between the board tap and the confirm.</summary>
        private GridPosition _targetCell;

        /// <summary>The swatch currently lit. Kept across opens rather than reset, so a player painting
        /// towards one objective colour does not re-pick it every time.</summary>
        private int _selectedColourId = FIRST_COLOUR_ID;

        [Inject]
        public void Construct(
            PowerUpModel powerUpModel,
            PowerUpSystem powerUpSystem,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem)
        {
            _powerUpModel = powerUpModel;
            _powerUpSystem = powerUpSystem;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
        }

        private void Awake()
        {
            // Spawned onto a new GameObject by the LifetimeScope rather than authored in the scene, so
            // the rect is shaped here: a full-stretch layer over its parent, the same footprint every
            // other overlay's root has.
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);

            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_powerUpModel == null || _powerUpSystem == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null)
            {
                Debug.LogError(
                    $"{nameof(PaintCrossColourPickerView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            BuildPanel();
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _powerUpModel.Armed.Subscribe(OnArmedChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the sheet is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the sheet for a Paint Cross aimed at <paramref name="targetCell"/>. Nothing is spent
        /// here: the power-up stays armed and the System is not told until the confirm. An already-open
        /// sheet returns immediately.
        /// </summary>
        internal void Open(GridPosition targetCell)
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            _targetCell = targetCell;
            Refresh();
            _panel.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// <summary>
        /// Routes a tap while the sheet is open. The close cross cancels; a swatch selects; the confirm
        /// button applies; anything else on the sheet is swallowed; the scrim outside it cancels —
        /// the same dismiss contract every other card here has.
        /// </summary>
        internal void HandleTap(Vector2 screenPosition)
        {
            if (!IsOpen)
            {
                return;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (RectTransformUtility.RectangleContainsScreenPoint(_close.HitRect, screenPosition, eventCamera))
            {
                Close();
                return;
            }

            for (int swatchIndex = 0; swatchIndex < SWATCH_COUNT; swatchIndex++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_swatchRects[swatchIndex], screenPosition, eventCamera))
                {
                    _selectedColourId = swatchIndex + FIRST_COLOUR_ID;
                    Refresh();
                    return;
                }
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_confirmButtonRect, screenPosition, eventCamera))
            {
                // The System owns whether this succeeds: a cross that has emptied out since the tap, a
                // count that reached zero, a level that banned the kind — all are refused there and
                // leave the power-up armed. Either way the sheet comes down, so a refusal leaves the
                // player back at aiming rather than staring at a button that does nothing.
                _powerUpSystem.TryApplyPaintCross(_targetCell, _selectedColourId);
                Close();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        /// <summary>Takes the sheet down. Safe to call when it is already down: the confirm's own
        /// disarm reaches <see cref="OnArmedChanged"/> before the confirm branch calls this.</summary>
        private void Close()
        {
            if (_panel == null)
            {
                return;
            }

            _panel.SetActive(false);
        }

        /// <summary>The sheet belongs to an armed Paint Cross and nothing else: whatever drops that
        /// selection — a confirm, a cancel from the strip, a run boundary — takes the sheet with it.</summary>
        private void OnArmedChanged(PowerUpKind? armed)
        {
            if (IsOpen && armed != PowerUpKind.PaintCross)
            {
                Close();
            }
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            Refresh();
        }

        private void OnLocaleChanged(LocaleDefinition locale) => Refresh();

        /// <summary>Repaints the sheet from the theme, the locale and the selection. Cheap enough to run
        /// on every swatch tap; a theme or locale change while it is closed still repaints so the next
        /// open needs nothing.</summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            _cardImage.color = _currentTheme.CardBackground;
            _cardShadowImage.color = _currentTheme.CardShadow;
            _close.PlateImage.color = _currentTheme.CardBackground;
            _close.PlateShadowImage.color = _currentTheme.CardShadow;
            for (int barIndex = 0; barIndex < _close.BarImages.Count; barIndex++)
            {
                _close.BarImages[barIndex].color = _currentTheme.Ink;
            }

            _titleText.color = _currentTheme.Ink;
            _titleText.text = _localizationSystem.Translate(LocalizationKeys.PAINT_CROSS_PICKER_TITLE);

            for (int swatchIndex = 0; swatchIndex < SWATCH_COUNT; swatchIndex++)
            {
                int colourId = swatchIndex + FIRST_COLOUR_ID;
                _swatchShadeImages[swatchIndex].color = _currentTheme.GetShade(colourId);
                _swatchFillImages[swatchIndex].color = _currentTheme.GetFill(colourId);
                _swatchRingImages[swatchIndex].color = colourId == _selectedColourId
                    ? _currentTheme.Accent
                    : Color.clear;
            }

            _confirmPlateImage.color = _currentTheme.Accent;
            _confirmShadowImage.color = _currentTheme.CardShadow;
            _confirmText.color = _currentTheme.CardBackground;
            _confirmText.text = _localizationSystem.Translate(LocalizationKeys.PAINT_CROSS_PICKER_CONFIRM);
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;

            var panelObject = new GameObject("PaintCrossColourPickerPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // Lighter than the info cards' scrim on purpose: the board stays readable behind the sheet,
            // because the cross the player is about to paint is what they are choosing a colour for.
            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            _cardRect = CellFactory.CreateCard(panelRect, "PaintCrossSheet", _cardSize, out _cardImage, out _cardShadowImage);
            _cardImage.raycastTarget = false;
            _cardShadowImage.raycastTarget = false;

            // A bottom sheet, not a centred card: both the card and its shadow are re-anchored to the
            // panel's bottom edge. The children below keep CreateCard's centre anchoring, which is
            // relative to the card's own rect and so unaffected by where the card sits.
            AnchorToBottom(_cardRect, _bottomMargin);
            AnchorToBottom((RectTransform)_cardShadowImage.transform, _bottomMargin - 8f);

            _close = InfoCardChrome.CreateFloatingCloseButton(_cardRect);
            InfoCardChrome.PositionFloatingCloseButton(_close, _cardSize * 0.5f);

            _titleText = UiTextFactory.Create(_cardRect, "Title", _titleFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)_titleText.transform).anchoredPosition = new Vector2(0f, _titleY);

            BuildSwatches();
            BuildConfirmButton();

            _panel = panelObject;
        }

        /// <summary>One swatch per colour id, laid out in a centred row: a shade base with the fill
        /// lifted off its bottom edge — the same bevel a board block wears, so a swatch reads as "the
        /// block you will get" — and an accent ring around the selected one.</summary>
        private void BuildSwatches()
        {
            float pitch = _swatchSize + _swatchGap;
            float originX = -pitch * ((SWATCH_COUNT - 1) * 0.5f);
            float radius = _swatchSize * SWATCH_RADIUS_FRACTION;
            float bevel = _swatchSize * SWATCH_BEVEL_FRACTION;
            var swatchSize = new Vector2(_swatchSize, _swatchSize);
            float ringSide = _swatchSize + (SELECTED_RING_THICKNESS * 2f);

            for (int swatchIndex = 0; swatchIndex < SWATCH_COUNT; swatchIndex++)
            {
                var position = new Vector2(originX + (swatchIndex * pitch), _swatchRowY);
                RectTransform swatchRect = HudChrome.CreateRect(_cardRect, $"Swatch_{swatchIndex + FIRST_COLOUR_ID}", swatchSize, position);
                _swatchRects[swatchIndex] = swatchRect;

                _swatchRingImages[swatchIndex] = HudChrome.BuildOutline(
                    swatchRect, "SelectedRing", new Vector2(ringSide, ringSide), Vector2.zero,
                    radius + SELECTED_RING_THICKNESS, SELECTED_RING_THICKNESS);
                _swatchShadeImages[swatchIndex] = HudChrome.BuildRounded(
                    swatchRect, "Shade", swatchSize, Vector2.zero, radius);
                _swatchFillImages[swatchIndex] = HudChrome.BuildRounded(
                    swatchRect, "Fill", new Vector2(_swatchSize, _swatchSize - bevel), new Vector2(0f, bevel * 0.5f), radius);
            }
        }

        /// <summary>The one action plate, lifted off the sheet with the same offset shadow every card
        /// here wears — the shape <see cref="CoinSowerPickerView"/> gives its own wide buttons.</summary>
        private void BuildConfirmButton()
        {
            _confirmButtonRect = CellFactory.CreateCard(
                _cardRect, "ConfirmButton", _confirmButtonSize, out _confirmPlateImage, out _confirmShadowImage,
                BUTTON_CORNER_MULTIPLIER);
            _confirmButtonRect.anchoredPosition = new Vector2(0f, _confirmButtonY);
            ((RectTransform)_confirmShadowImage.transform).anchoredPosition = new Vector2(0f, _confirmButtonY - 8f);
            _confirmPlateImage.raycastTarget = false;
            _confirmShadowImage.raycastTarget = false;

            _confirmText = UiTextFactory.Create(_confirmButtonRect, "Caption", _buttonFontSize, FontStyle.Bold, Color.clear);
        }

        private static void AnchorToBottom(RectTransform rect, float bottomMargin)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottomMargin);
        }
    }
}
