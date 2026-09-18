using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The one info popup on screen: an icon, a header and a short body explaining what it is about,
    /// shown automatically the first time its subject appears and reopenable on demand afterward (see
    /// <see cref="InfoPopupSystem"/>). Replaces the old spotlight/coach-mark overlay entirely — this is
    /// an ordinary, optional modal, never a forced action.
    /// <para>
    /// Built the same way <see cref="ObjectiveInfoPopupView"/> is: one card on a scrim, toggled from
    /// <see cref="InfoPopupModel.OpenContent"/> rather than an imperative Open/Close pair, holding the
    /// timed countdown through <see cref="TimerRunSystem.SetMenuPaused"/> while it is up. The gate chain
    /// in <see cref="BoardInputView"/> is what guarantees it can never be open at the same time as
    /// another panel, which is what keeps that single shared pause flag from having two owners.
    /// </para>
    /// <para>
    /// The hero icon is never authored here: it borrows whichever View already owns the authored sprite
    /// for the popup's subject — <see cref="BoardView"/> for a special cell, <see cref="PowerUpInventoryView"/>
    /// for a power-up, <see cref="HoldSlotView"/> for the Hold pocket — so one glyph is never drawn
    /// twice. A special piece has no authored icon of its own (see <see cref="ResolveIcon"/>) and falls
    /// back to the shared starburst placeholder every other unauthored glyph in this game uses.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InfoPopupView : MonoBehaviour
    {
        // Layout, in canvas reference pixels, matching the objective, settings, level path and badge
        // cards so every overlay reads as one family.
        private const float HEADER_INSET = 92f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float HERO_GLYPH_SIZE = 168f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 560f);
        [SerializeField] private int _headerFontSize = 56;
        [SerializeField] private int _bodyFontSize = 40;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private InfoPopupModel _infoPopupModel;
        private InfoPopupSystem _infoPopupSystem;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private SettingsModel _settingsModel;
        private TimerRunSystem _timerRunSystem;
        private BoardView _boardView;
        private PowerUpInventoryView _powerUpInventoryView;
        private HoldSlotView _holdSlotView;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private RectTransform _closeButtonRect;
        private Image _closeBarImageA;
        private Image _closeBarImageB;
        private Image _heroPlateImage;
        private Image _heroIconImage;
        private Text _headerText;
        private Text _bodyText;

        private ThemeDefinition _currentTheme;
        private InfoPopupContent? _currentContent;

        [Inject]
        public void Construct(
            InfoPopupModel infoPopupModel,
            InfoPopupSystem infoPopupSystem,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            SettingsModel settingsModel,
            TimerRunSystem timerRunSystem,
            BoardView boardView,
            PowerUpInventoryView powerUpInventoryView,
            HoldSlotView holdSlotView)
        {
            _infoPopupModel = infoPopupModel;
            _infoPopupSystem = infoPopupSystem;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _settingsModel = settingsModel;
            _timerRunSystem = timerRunSystem;
            _boardView = boardView;
            _powerUpInventoryView = powerUpInventoryView;
            _holdSlotView = holdSlotView;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_infoPopupModel == null || _infoPopupSystem == null || _localizationModel == null
                || _localizationSystem == null || _settingsModel == null || _timerRunSystem == null
                || _boardView == null || _powerUpInventoryView == null || _holdSlotView == null)
            {
                Debug.LogError(
                    $"{nameof(InfoPopupView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            BuildPanel();
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _infoPopupModel.OpenContent.Subscribe(OnContentChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the card is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Routes a tap while the card is open. The close cross wins; the card then swallows anything
        /// else, so only the scrim outside it dismisses.
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

            if (RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, screenPosition, eventCamera))
            {
                _infoPopupSystem.Close();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            _infoPopupSystem.Close();
        }

        private void OnContentChanged(InfoPopupContent? content)
        {
            _currentContent = content;

            if (_panel == null)
            {
                return;
            }

            if (content == null)
            {
                if (_panel.activeSelf)
                {
                    _panel.SetActive(false);
                    _timerRunSystem.SetMenuPaused(false);
                }

                return;
            }

            bool wasOpen = _panel.activeSelf;
            Refresh();
            _panel.SetActive(true);
            transform.SetAsLastSibling();

            if (!wasOpen)
            {
                _timerRunSystem.SetMenuPaused(true);
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

        /// <summary>Repaints the card from the current content and theme. A no-op while closed or
        /// before either is known.</summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null || _currentContent == null)
            {
                return;
            }

            InfoPopupContent content = _currentContent.Value;

            _cardImage.color = _currentTheme.CardBackground;
            _cardShadowImage.color = _currentTheme.CardShadow;
            _closeBarImageA.color = _currentTheme.Ink;
            _closeBarImageB.color = _currentTheme.Ink;

            _heroPlateImage.color = _currentTheme.Accent;

            ResolveIcon(content, out Sprite icon, out Color iconTint);
            _heroIconImage.sprite = icon;
            _heroIconImage.color = icon != null ? iconTint : Color.clear;

            _headerText.color = _currentTheme.Ink;
            _headerText.text = _localizationSystem.Translate(content.HeaderLocalizationKey);

            _bodyText.color = _currentTheme.SoftInk;
            _bodyText.text = _localizationSystem.Translate(content.BodyLocalizationKey);
        }

        /// <summary>
        /// The hero icon for <paramref name="content"/>'s subject, borrowed from whichever View already
        /// owns the authored sprite for it — never drawn twice. A special piece has no authored icon of
        /// its own (special pieces are painted as a colour/pattern treatment on the piece's own cells via
        /// <c>SpecialPieceVisuals</c>, not as a separate glyph), so every <see cref="SpecialPieceKind"/>
        /// falls back to the shared starburst placeholder — the same fallback
        /// <c>ObjectiveIconFactory</c>/<see cref="ObjectiveInfoPopupView"/> use for an unauthored
        /// objective glyph.
        /// </summary>
        private void ResolveIcon(InfoPopupContent content, out Sprite icon, out Color tint)
        {
            switch (content.SubjectKind)
            {
                case InfoPopupSubjectKind.SpecialCell:
                    var cellKind = (SpecialCellKind)content.KindValue;
                    icon = _boardView.IconSprite(cellKind);
                    tint = BoardView.IconTint(cellKind);
                    return;
                case InfoPopupSubjectKind.PowerUp:
                    icon = _powerUpInventoryView.IconFor((PowerUpKind)content.KindValue);
                    tint = Color.white;
                    return;
                case InfoPopupSubjectKind.Hold:
                    icon = _holdSlotView.PocketSprite;
                    tint = Color.white;
                    return;
                default:
                    icon = UiSpriteFactory.Starburst;
                    tint = Color.white;
                    return;
            }
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("InfoPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            _cardRect = CellFactory.CreateCard(panelRect, "InfoCard", _cardSize, out _cardImage, out _cardShadowImage);

            float cardHalfHeight = _cardSize.y * 0.5f;
            float cardHalfWidth = _cardSize.x * 0.5f;
            float headerY = cardHalfHeight - HEADER_INSET;

            _headerText = CreateLabel(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-cardHalfWidth + SIDE_INSET, headerY));

            BuildCloseButton(
                _cardRect, new Vector2(cardHalfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f), headerY));

            BuildHero(new Vector2(0f, headerY - HERO_GLYPH_SIZE));

            _bodyText = CreateLabel(
                _cardRect, "Body", _bodyFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, -cardHalfHeight + 120f));

            _panel = panelObject;
        }

        private void BuildHero(Vector2 anchoredPosition)
        {
            var heroObject = new GameObject("HeroIcon", typeof(RectTransform));
            var heroRect = (RectTransform)heroObject.transform;
            heroRect.SetParent(_cardRect, false);
            Centre(heroRect, new Vector2(HERO_GLYPH_SIZE, HERO_GLYPH_SIZE));
            heroRect.anchoredPosition = anchoredPosition;

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(heroRect, false);
            Centre(plateRect, new Vector2(HERO_GLYPH_SIZE, HERO_GLYPH_SIZE));
            _heroPlateImage = plateObject.GetComponent<Image>();
            ConfigureRounded(_heroPlateImage);

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(heroRect, false);
            Centre(iconRect, new Vector2(HERO_GLYPH_SIZE * 0.58f, HERO_GLYPH_SIZE * 0.58f));
            _heroIconImage = iconObject.GetComponent<Image>();
            _heroIconImage.type = Image.Type.Simple;
            _heroIconImage.preserveAspect = true;
            _heroIconImage.color = Color.clear;
            _heroIconImage.raycastTarget = false;
        }

        private void BuildCloseButton(RectTransform root, Vector2 anchoredPosition)
        {
            const float CROSS_LENGTH = 46f;
            const float CROSS_THICKNESS = 8f;

            var closeObject = new GameObject("CloseButton", typeof(RectTransform));
            _closeButtonRect = (RectTransform)closeObject.transform;
            _closeButtonRect.SetParent(root, false);
            Centre(_closeButtonRect, new Vector2(ICON_BUTTON_SIZE, ICON_BUTTON_SIZE));
            _closeButtonRect.anchoredPosition = anchoredPosition;

            var barObjectA = new GameObject("CloseBar_0", typeof(RectTransform), typeof(Image));
            var barRectA = (RectTransform)barObjectA.transform;
            barRectA.SetParent(_closeButtonRect, false);
            Centre(barRectA, new Vector2(CROSS_LENGTH, CROSS_THICKNESS));
            barRectA.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _closeBarImageA = barObjectA.GetComponent<Image>();
            ConfigureRounded(_closeBarImageA);

            var barObjectB = new GameObject("CloseBar_1", typeof(RectTransform), typeof(Image));
            var barRectB = (RectTransform)barObjectB.transform;
            barRectB.SetParent(_closeButtonRect, false);
            Centre(barRectB, new Vector2(CROSS_LENGTH, CROSS_THICKNESS));
            barRectB.localRotation = Quaternion.Euler(0f, 0f, -45f);
            _closeBarImageB = barObjectB.GetComponent<Image>();
            ConfigureRounded(_closeBarImageB);
        }

        private static Text CreateLabel(
            RectTransform parent,
            string objectName,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 anchoredPosition)
        {
            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, Color.clear);
            text.alignment = alignment;

            var rect = (RectTransform)text.transform;
            float pivotX = alignment == TextAnchor.MiddleRight ? 1f : (alignment == TextAnchor.MiddleLeft ? 0f : 0.5f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.sizeDelta = new Vector2(0f, fontSize * 1.6f);
            rect.anchoredPosition = anchoredPosition;
            return text;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not
        // through an EventSystem, and this scene has none.
        private static void ConfigureRounded(Image image)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = Color.clear;
            image.raycastTarget = false;
        }
    }
}
