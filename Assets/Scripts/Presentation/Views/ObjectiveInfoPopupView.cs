using System.Collections.Generic;
using System.Text;
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
    /// The card behind one objective icon: what that objective actually asks for, in words, with its
    /// progress and whether it is done. The icon row is deliberately wordless — this is where the
    /// wording went.
    /// <para>
    /// Modal like the other overlays, and built the same way as <see cref="BadgesPanelView"/>: one card
    /// on a scrim, toggled with SetActive, holding the timed countdown through
    /// <see cref="TimerRunSystem.SetMenuPaused"/> while it is up. The gate chain in
    /// <see cref="BoardInputView"/> is what guarantees it can never be open at the same time as another
    /// panel, which is what keeps that single shared pause flag from having two owners.
    /// </para>
    /// <para>
    /// Repainted on <see cref="Open"/> rather than subscribed to progress: while it is up the run is
    /// paused and no placement can land, so there is nothing for it to miss.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveInfoPopupView : MonoBehaviour
    {
        // Layout, in canvas reference pixels, matching the settings, level path and badge cards so
        // every overlay reads as one family.
        private const float HEADER_INSET = 92f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;

        /// <summary>Side of the large glyph the card repeats from the icon that opened it, so the player
        /// can see which of several icons they tapped.</summary>
        private const float HERO_GLYPH_SIZE = 168f;

        private const string COUNTER_SEPARATOR = " / ";

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        /// <summary>Repaint bucket: every Image that follows the theme's ink (the close cross), so a
        /// theme switch is one tight loop instead of a hierarchy walk.</summary>
        private readonly List<Image> _inkImages = new List<Image>(4);

        /// <summary>The hero glyph's strokes and its punched-out parts. Rebuilt whenever the card is
        /// opened for an objective of a different type — see <see cref="RebuildHeroGlyph"/>.</summary>
        private readonly List<Image> _heroInkImages = new List<Image>(4);
        private readonly List<Image> _heroCoreImages = new List<Image>(2);

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 620f);
        [SerializeField] private int _headerFontSize = 56;
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
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private RectTransform _closeButtonRect;
        private RectTransform _heroGlyphRoot;
        private Image _heroPlateImage;
        private Image _heroCheckMark;
        private Text _headerText;
        private Text _descriptionText;

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
            TimerRunSystem timerRunSystem)
        {
            _objectiveModel = objectiveModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _settingsModel = settingsModel;
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

            if (RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, screenPosition, eventCamera))
            {
                Close();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
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

            _cardImage.color = _currentTheme.CardBackground;
            _cardShadowImage.color = _currentTheme.CardShadow;

            for (int inkIndex = 0; inkIndex < _inkImages.Count; inkIndex++)
            {
                _inkImages[inkIndex].color = _currentTheme.Ink;
            }

            RebuildHeroGlyph(objective.Definition.Type);

            bool isComplete = objective.IsComplete;
            Color plateColour = isComplete
                ? _currentTheme.Accent
                : Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.1f);
            Color glyphInkColour = isComplete ? _currentTheme.CardBackground : _currentTheme.Ink;

            _heroPlateImage.color = plateColour;

            for (int inkIndex = 0; inkIndex < _heroInkImages.Count; inkIndex++)
            {
                _heroInkImages[inkIndex].color = glyphInkColour;
            }

            for (int coreIndex = 0; coreIndex < _heroCoreImages.Count; coreIndex++)
            {
                _heroCoreImages[coreIndex].color = plateColour;
            }

            _heroCheckMark.color = isComplete ? _currentTheme.CardBackground : Color.clear;

            _headerText.color = isComplete ? _currentTheme.Accent : _currentTheme.Ink;
            _headerText.text = FormatCounter(objective.CurrentValue, objective.Definition.TargetValue);

            _descriptionText.color = isComplete ? _currentTheme.Accent : _currentTheme.SoftInk;
            _descriptionText.text =
                ObjectiveDescriptionFormatter.Describe(objective.Definition, _localizationSystem);
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

            ObjectiveIconFactory.Build(
                _heroGlyphRoot, type, HERO_GLYPH_SIZE * 0.62f, _heroInkImages, _heroCoreImages);

            _heroGlyphType = type;
            _hasHeroGlyph = true;
        }

        private string FormatCounter(int value, int total)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(value);
            _stringBuilder.Append(COUNTER_SEPARATOR);
            _stringBuilder.Append(total);
            return _stringBuilder.ToString();
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

            _cardRect = CellFactory.CreateCard(
                panelRect, "ObjectiveInfoCard", _cardSize, out _cardImage, out _cardShadowImage);

            float cardHalfHeight = _cardSize.y * 0.5f;
            float cardHalfWidth = _cardSize.x * 0.5f;
            float headerY = cardHalfHeight - HEADER_INSET;

            _headerText = CreateLabel(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-cardHalfWidth + SIDE_INSET, headerY));

            BuildCloseButton(
                _cardRect, new Vector2(cardHalfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f), headerY));

            BuildHero(new Vector2(0f, headerY - HERO_GLYPH_SIZE));

            _descriptionText = CreateLabel(
                _cardRect, "Description", _descriptionFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, -cardHalfHeight + 120f));

            _panel = panelObject;
        }

        /// <summary>The tapped icon, repeated large: the same plate, glyph and tick the row draws, so
        /// the card is visibly about the icon the player touched.</summary>
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

            var glyphObject = new GameObject("Glyph", typeof(RectTransform));
            _heroGlyphRoot = (RectTransform)glyphObject.transform;
            _heroGlyphRoot.SetParent(heroRect, false);
            Centre(_heroGlyphRoot, new Vector2(HERO_GLYPH_SIZE, HERO_GLYPH_SIZE));

            var checkObject = new GameObject("CheckMark", typeof(RectTransform), typeof(Image));
            var checkRect = (RectTransform)checkObject.transform;
            checkRect.SetParent(heroRect, false);
            Centre(checkRect, new Vector2(HERO_GLYPH_SIZE * 0.38f, HERO_GLYPH_SIZE * 0.38f));
            checkRect.anchoredPosition = new Vector2(HERO_GLYPH_SIZE * 0.28f, -HERO_GLYPH_SIZE * 0.28f);

            _heroCheckMark = checkObject.GetComponent<Image>();

            // The tick sprite has no border, so it must never be sliced.
            _heroCheckMark.sprite = UiSpriteFactory.CheckMark;
            _heroCheckMark.type = Image.Type.Simple;
            _heroCheckMark.color = Color.clear;
            _heroCheckMark.raycastTarget = false;
        }

        /// <summary>Two bars crossed at right angles — the close glyph, as on the other cards.</summary>
        private void BuildCloseButton(RectTransform root, Vector2 anchoredPosition)
        {
            const float CROSS_LENGTH = 46f;
            const float CROSS_THICKNESS = 8f;

            var closeObject = new GameObject("CloseButton", typeof(RectTransform));
            _closeButtonRect = (RectTransform)closeObject.transform;
            _closeButtonRect.SetParent(root, false);
            Centre(_closeButtonRect, new Vector2(ICON_BUTTON_SIZE, ICON_BUTTON_SIZE));
            _closeButtonRect.anchoredPosition = anchoredPosition;

            for (int barIndex = 0; barIndex < 2; barIndex++)
            {
                var barObject = new GameObject($"CloseBar_{barIndex}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform)barObject.transform;
                barRect.SetParent(_closeButtonRect, false);
                Centre(barRect, new Vector2(CROSS_LENGTH, CROSS_THICKNESS));
                barRect.localRotation = Quaternion.Euler(0f, 0f, barIndex == 0 ? 45f : -45f);

                var barImage = barObject.GetComponent<Image>();
                ConfigureRounded(barImage);
                _inkImages.Add(barImage);
            }
        }

        /// <summary>Builds a wordless label; <see cref="Refresh"/> fills every one of them in from the
        /// model, because every label on this card carries a value.</summary>
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

            // Pivot on the aligned edge so the anchored position is that edge, whatever the string ends
            // up measuring — labels overflow their rect by design (see UiTextFactory).
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
