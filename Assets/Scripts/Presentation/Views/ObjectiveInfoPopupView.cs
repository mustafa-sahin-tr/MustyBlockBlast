using System.Collections.Generic;
using MustyBlockBlast.Core;
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
    /// The card behind one objective icon: what that objective actually asks for, in words, with
    /// whether it is done. Progress itself is not repeated here — the HUD's GOAL pill already shows
    /// it — so the icon row is deliberately wordless and this card carries the objective's name and
    /// its full description.
    /// <para>
    /// Modal like the other overlays, and built the same way as <see cref="BadgesPanelView"/>: one card
    /// on a scrim, toggled with SetActive, holding the timed countdown through
    /// <see cref="TimerRunSystem.SetMenuPaused"/> while it is up. The gate chain in
    /// <see cref="BoardInputView"/> is what guarantees it can never be open at the same time as another
    /// panel, which is what keeps that single shared pause flag from having two owners.
    /// </para>
    /// <para>
    /// The shared card chrome (rounded card, hero ring/plate, close button, title/description layout)
    /// comes from <see cref="InfoCardChrome"/>, the same builder <see cref="InfoPopupView"/> uses — this
    /// View owns only the hero icon content itself (an authored sprite or the procedural glyph
    /// fallback) and the objective-specific text.
    /// </para>
    /// <para>
    /// Repainted on <see cref="Open"/> rather than subscribed to progress: while it is up the run is
    /// paused and no placement can land, so there is nothing for it to miss.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveInfoPopupView : MonoBehaviour
    {
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>The hero glyph's strokes and its punched-out parts. Rebuilt whenever the card is
        /// opened for an objective of a different type — see <see cref="RebuildHeroGlyph"/>.</summary>
        private readonly List<Image> _heroInkImages = new List<Image>(4);
        private readonly List<Image> _heroCoreImages = new List<Image>(2);

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 640f);
        [SerializeField] private int _titleFontSize = 60;
        [SerializeField] private int _descriptionFontSize = 40;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private ObjectiveModel _objectiveModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private SettingsModel _settingsModel;
        private TimerRunSystem _timerRunSystem;

        private Canvas _canvas;
        private GameObject _panel;
        private InfoCardChrome.Handles _chrome;
        private RectTransform _heroGlyphRoot;
        private Image _heroIconImage;
        private ObjectiveIconCatalog _iconCatalog;
        private Image _heroCheckMark;

        private ThemeDefinition _currentTheme;

        /// <summary>Which tracked objective the card is showing. -1 while it is closed.</summary>
        private int _openObjectiveIndex = -1;

        private ObjectiveType _heroGlyphType;
        private bool _hasHeroGlyph;

        [Inject]
        public void Construct(
            ObjectiveModel objectiveModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            SettingsModel settingsModel,
            ObjectiveIconCatalog iconCatalog,
            TimerRunSystem timerRunSystem)
        {
            _objectiveModel = objectiveModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _settingsModel = settingsModel;
            _iconCatalog = iconCatalog;
            _timerRunSystem = timerRunSystem;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_objectiveModel == null || _localizationModel == null || _localizationSystem == null
                || _settingsModel == null || _timerRunSystem == null)
            {
                Debug.LogError(
                    $"{nameof(ObjectiveInfoPopupView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            BuildPanel();
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the card is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the card for the objective at <paramref name="objectiveIndex"/> in
        /// <see cref="ObjectiveModel.TrackedObjectives"/>. Re-opening never double-pauses the clock: an
        /// already-open card returns immediately. An index that no longer names a tracked objective is
        /// ignored rather than opening an empty card.
        /// </summary>
        internal void Open(int objectiveIndex)
        {
            if (_panel == null || IsOpen || ObjectiveAt(objectiveIndex) == null)
            {
                return;
            }

            _openObjectiveIndex = objectiveIndex;
            Refresh();
            _panel.SetActive(true);
            transform.SetAsLastSibling();
            _timerRunSystem.SetMenuPaused(true);
        }

        /// <summary>
        /// Routes a tap while the card is open. The close cross wins; the card then swallows anything
        /// else, so only the scrim outside it dismisses — which includes the icon that opened it, since
        /// the icon row sits outside the card. That is the same dismiss contract the badge, settings and
        /// level path cards already have.
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

            if (RectTransformUtility.RectangleContainsScreenPoint(_chrome.CloseButtonRect, screenPosition, eventCamera))
            {
                Close();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_chrome.CardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        private void Close()
        {
            _panel.SetActive(false);
            _openObjectiveIndex = -1;
            _timerRunSystem.SetMenuPaused(false);
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

        private ObjectiveProgress ObjectiveAt(int objectiveIndex)
        {
            if (_objectiveModel == null || objectiveIndex < 0)
            {
                return null;
            }

            IReadOnlyList<ObjectiveProgress> tracked = _objectiveModel.TrackedObjectives;
            return objectiveIndex < tracked.Count ? tracked[objectiveIndex] : null;
        }

        /// <summary>Repaints the card from the model. A no-op while closed, so a theme or locale change
        /// mid-run costs nothing.</summary>
        private void Refresh()
        {
            ObjectiveProgress objective = ObjectiveAt(_openObjectiveIndex);
            if (_panel == null || _currentTheme == null || objective == null)
            {
                return;
            }

            _chrome.CardImage.color = _currentTheme.CardBackground;
            _chrome.CardShadowImage.color = _currentTheme.CardShadow;
            _chrome.ClosePlateImage.color = _currentTheme.CardBackground;
            _chrome.ClosePlateShadowImage.color = _currentTheme.CardShadow;

            for (int barIndex = 0; barIndex < _chrome.CloseBarImages.Count; barIndex++)
            {
                _chrome.CloseBarImages[barIndex].color = _currentTheme.Ink;
            }

            RebuildHeroGlyph(objective.Definition.Type);

            bool isComplete = objective.IsComplete;
            Color plateColour = isComplete
                ? _currentTheme.Accent
                : Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.1f);
            Color glyphInkColour = isComplete ? _currentTheme.CardBackground : _currentTheme.Ink;

            _chrome.HeroPlateImage.color = plateColour;
            _chrome.HeroRingImage.color = Color.Lerp(plateColour, _currentTheme.CardBackground, 0.55f);

            for (int inkIndex = 0; inkIndex < _heroInkImages.Count; inkIndex++)
            {
                _heroInkImages[inkIndex].color = glyphInkColour;
            }

            for (int coreIndex = 0; coreIndex < _heroCoreImages.Count; coreIndex++)
            {
                _heroCoreImages[coreIndex].color = plateColour;
            }

            _heroCheckMark.color = isComplete ? _currentTheme.CardBackground : Color.clear;

            _chrome.TitleText.color = isComplete ? _currentTheme.Accent : _currentTheme.Ink;
            _chrome.TitleText.text = _localizationSystem.Translate(
                ObjectiveDescriptionFormatter.TitleKey(objective.Definition.Type));

            _chrome.DescriptionText.color = isComplete ? _currentTheme.Accent : _currentTheme.SoftInk;
            _chrome.DescriptionText.text = ObjectiveDescriptionFormatter.Describe(
                objective.Definition, _localizationSystem, _currentTheme);
        }

        /// <summary>Rebuilds the hero glyph when the card is opened for an objective of a different
        /// type. Reached at most once per open, and never while the card is up.</summary>
        private void RebuildHeroGlyph(ObjectiveType type)
        {
            if (_hasHeroGlyph && _heroGlyphType == type)
            {
                return;
            }

            for (int childIndex = _heroGlyphRoot.childCount - 1; childIndex >= 0; childIndex--)
            {
                Destroy(_heroGlyphRoot.GetChild(childIndex).gameObject);
            }

            _heroInkImages.Clear();
            _heroCoreImages.Clear();

            // Cleared before deciding: an Image with no sprite draws a solid square, so the authored
            // image must be invisible whenever it is not the glyph in use.
            _heroIconImage.sprite = null;
            _heroIconImage.color = Color.clear;

            Sprite authoredIcon = _iconCatalog != null ? _iconCatalog.Find(type) : null;
            if (authoredIcon != null)
            {
                // Full-colour illustrated art (issue #322): rendered as-is, exactly like
                // InfoPopupView.Refresh() does for its own full-colour PowerUp/Hold icons. NOT added
                // to _heroInkImages, so Refresh() never flattens it with the theme's ink colour.
                _heroIconImage.sprite = authoredIcon;
                _heroIconImage.color = Color.white;
            }
            else
            {
                ObjectiveIconFactory.Build(
                    _heroGlyphRoot, type, InfoCardChrome.HERO_CONTENT_SIZE * 0.62f, _heroInkImages, _heroCoreImages);
            }

            _heroGlyphType = type;
            _hasHeroGlyph = true;
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("ObjectiveInfoPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            _chrome = InfoCardChrome.Build(
                panelRect, "ObjectiveInfoCard", _cardSize, _titleFontSize, _descriptionFontSize);

            BuildHero();

            _panel = panelObject;
        }

        /// <summary>The tapped icon, repeated large: the same plate, glyph and tick the row draws, so
        /// the card is visibly about the icon the player touched. Parented into
        /// <see cref="InfoCardChrome.Handles.HeroContentRect"/>, which the chrome has already centred and
        /// sized on the hero plate.</summary>
        private void BuildHero()
        {
            RectTransform heroContentRect = _chrome.HeroContentRect;

            var glyphObject = new GameObject("Glyph", typeof(RectTransform));
            _heroGlyphRoot = (RectTransform)glyphObject.transform;
            _heroGlyphRoot.SetParent(heroContentRect, false);
            Centre(_heroGlyphRoot, new Vector2(InfoCardChrome.HERO_CONTENT_SIZE, InfoCardChrome.HERO_CONTENT_SIZE));

            // The authored icon, same footprint as the procedural glyph, so either can stand in for
            // the other. Full-colour illustrated art rendered as-is (white tint, not added to
            // _heroInkImages) — see RebuildHeroGlyph.
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(heroContentRect, false);
            Centre(iconRect, new Vector2(InfoCardChrome.HERO_CONTENT_SIZE, InfoCardChrome.HERO_CONTENT_SIZE));
            _heroIconImage = iconObject.GetComponent<Image>();
            _heroIconImage.type = Image.Type.Simple;
            _heroIconImage.preserveAspect = true;
            _heroIconImage.color = Color.clear;
            _heroIconImage.raycastTarget = false;

            float checkSize = InfoCardChrome.HERO_CONTENT_SIZE * 0.65f;
            var checkObject = new GameObject("CheckMark", typeof(RectTransform), typeof(Image));
            var checkRect = (RectTransform)checkObject.transform;
            checkRect.SetParent(heroContentRect, false);
            Centre(checkRect, new Vector2(checkSize, checkSize));
            checkRect.anchoredPosition = new Vector2(
                InfoCardChrome.HERO_PLATE_SIZE * 0.28f, -InfoCardChrome.HERO_PLATE_SIZE * 0.28f);

            _heroCheckMark = checkObject.GetComponent<Image>();

            // The tick sprite has no border, so it must never be sliced.
            _heroCheckMark.sprite = UiSpriteFactory.CheckMark;
            _heroCheckMark.type = Image.Type.Simple;
            _heroCheckMark.color = Color.clear;
            _heroCheckMark.raycastTarget = false;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }
    }
}
