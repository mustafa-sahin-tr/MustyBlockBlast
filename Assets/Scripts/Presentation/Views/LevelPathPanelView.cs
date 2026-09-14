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
    /// The level path overlay: the whole authored ladder as a gated sequence of numbered nodes, with
    /// the player's own position highlighted. Read-only in this slice — a node is a status light, not
    /// a button, so tapping one deliberately does nothing.
    /// <para>
    /// Paged rather than scrolled: this scene has no EventSystem (taps arrive through
    /// <see cref="BoardInputView"/>'s pointer action) and therefore no ScrollRect, so a hundred nodes
    /// are shown a page at a time with two chevrons. <see cref="Open"/> jumps straight to the page
    /// holding <see cref="LevelProgressionModel.CurrentLevelNumber"/>, so the player never has to page
    /// -hunt for their own position.
    /// </para>
    /// <para>
    /// Built once in <see cref="Start"/> and toggled with SetActive, the same way
    /// <see cref="SettingsPanelView"/> is; the node widgets are built once too and repainted per page,
    /// so paging allocates nothing beyond the page-indicator string.
    /// </para>
    /// <para>
    /// Modal like the settings card: while it is open it holds the timed countdown through
    /// <see cref="TimerRunSystem.SetMenuPaused"/> — the same flag the settings panel uses, because
    /// "a menu is open" is one state, and the gate chain in <see cref="BoardInputView"/> guarantees
    /// the two panels can never be open at once.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelPathPanelView : MonoBehaviour
    {
        private const int PAGE_COLUMN_COUNT = 4;
        private const int PAGE_ROW_COUNT = 5;

        /// <summary>Nodes shown per page. A 4x5 grid fills the card width at a comfortable node size.</summary>
        private const int PAGE_SIZE = PAGE_COLUMN_COUNT * PAGE_ROW_COUNT;

        // Layout, in canvas reference pixels, matching the settings card's 880pt width and its
        // header/side insets so the two overlays read as one family.
        private const float HEADER_INSET = 92f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float GRID_TOP_INSET = 194f;

        private const float NODE_SIZE = 160f;
        private const float NODE_SPACING_X = 200f;
        private const float NODE_SPACING_Y = 190f;
        private const float NODE_DOT_SIZE = 28f;

        /// <summary>Scale applied to the node the player is on, so "you are here" reads without new art.</summary>
        private const float CURRENT_NODE_SCALE = 1.08f;

        /// <summary>
        /// Alpha applied to a locked node. Deliberately the same dim as
        /// <see cref="PowerUpInventoryView"/>'s empty slot: "you cannot have this yet" is one visual
        /// idea and should look identical wherever it appears.
        /// </summary>
        private const float LOCKED_NODE_ALPHA = 0.35f;

        /// <summary>Separator in the header and pager counters. A symbol, not a word — nothing here
        /// for a translator to translate, so it stays out of the String Table.</summary>
        private const string COUNTER_SEPARATOR = " / ";

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);
        private readonly LevelNode[] _nodes = new LevelNode[PAGE_SIZE];

        /// <summary>Repaint bucket: every Image that follows the theme's ink (the close cross, the two
        /// pager chevrons), so a theme switch is one tight loop instead of a hierarchy walk.</summary>
        private readonly List<Image> _inkImages = new List<Image>(8);

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1336f);
        [SerializeField] private int _headerFontSize = 64;
        [SerializeField] private int _nodeFontSize = 48;
        [SerializeField] private int _descriptionFontSize = 34;
        [SerializeField] private int _pageFontSize = 38;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private LevelProgressionModel _levelProgressionModel;
        private LevelCatalog _levelCatalog;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private TimerRunSystem _timerRunSystem;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;

        private RectTransform _closeButtonRect;
        private RectTransform _previousPageRect;
        private RectTransform _nextPageRect;
        private Text _headerText;
        private Text _descriptionText;
        private Text _pageText;

        private ThemeDefinition _currentTheme;
        private int _pageIndex;

        /// <summary>One built node widget. Rebuilt never, repainted on every page change.</summary>
        private sealed class LevelNode
        {
            internal LevelNode(RectTransform root, Image plateImage, Image shadowImage, Image doneDot, Text numberText)
            {
                Root = root;
                PlateImage = plateImage;
                ShadowImage = shadowImage;
                DoneDot = doneDot;
                NumberText = numberText;
            }

            internal RectTransform Root { get; }

            internal Image PlateImage { get; }

            internal Image ShadowImage { get; }

            internal Image DoneDot { get; }

            internal Text NumberText { get; }
        }

        [Inject]
        public void Construct(
            LevelProgressionModel levelProgressionModel,
            LevelCatalog levelCatalog,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            TimerRunSystem timerRunSystem)
        {
            _levelProgressionModel = levelProgressionModel;
            _levelCatalog = levelCatalog;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _timerRunSystem = timerRunSystem;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_levelProgressionModel == null || _levelCatalog == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null || _timerRunSystem == null)
            {
                Debug.LogError(
                    $"{nameof(LevelPathPanelView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Built in Start rather than Awake: the node count comes from the injected catalog, which
            // only exists once VContainer has run Construct.
            BuildPanel();
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);

            // The ladder can advance while the card is closed; repainting from the model keeps a
            // reopened card truthful without any "is it stale" bookkeeping.
            _levelProgressionModel.CurrentLevelNumber.Subscribe(OnCurrentLevelChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the panel, opened on the page that holds the player's current level. Re-opening never
        /// double-pauses the clock: an already-open panel returns immediately.
        /// </summary>
        internal void Open()
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            // Clamped like SetPage does: the progression never runs past the catalog today, but a page
            // index derived from it must not be able to show an empty grid under a "6 / 5" counter.
            _pageIndex = Mathf.Clamp(
                PageIndexOf(_levelProgressionModel.CurrentLevelNumber.Value), 0, PageCount - 1);
            Refresh();

            _panel.SetActive(true);
            transform.SetAsLastSibling();
            _timerRunSystem.SetMenuPaused(true);
        }

        /// <summary>
        /// Routes a tap while the panel is open. The two pagers and the close cross are tested first;
        /// the card then swallows anything else — a node is read-only in this slice, so landing on one
        /// is a deliberate no-op rather than a dismissal. Only the scrim outside the card closes.
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

            if (RectTransformUtility.RectangleContainsScreenPoint(_previousPageRect, screenPosition, eventCamera))
            {
                SetPage(_pageIndex - 1);
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_nextPageRect, screenPosition, eventCamera))
            {
                SetPage(_pageIndex + 1);
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
            _timerRunSystem.SetMenuPaused(false);
        }

        private void SetPage(int pageIndex)
        {
            int clamped = Mathf.Clamp(pageIndex, 0, PageCount - 1);
            if (clamped == _pageIndex)
            {
                return;
            }

            _pageIndex = clamped;
            Refresh();
        }

        /// <summary>Pages needed for the whole catalog; never zero, so an empty catalog still shows "1 / 1".</summary>
        private int PageCount => Mathf.Max(1, Mathf.CeilToInt(_levelCatalog.MaxLevelNumber / (float)PAGE_SIZE));

        private static int PageIndexOf(int levelNumber) => Mathf.Max(0, (levelNumber - 1) / PAGE_SIZE);

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

        private void OnCurrentLevelChanged(int levelNumber) => Refresh();

        /// <summary>Repaints the whole card from the models: header, nodes, description and pager.</summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            int currentLevel = _levelProgressionModel.CurrentLevelNumber.Value;
            int maxLevel = _levelCatalog.MaxLevelNumber;

            _cardImage.color = _currentTheme.CardBackground;
            _cardShadowImage.color = _currentTheme.CardShadow;

            for (int inkIndex = 0; inkIndex < _inkImages.Count; inkIndex++)
            {
                _inkImages[inkIndex].color = _currentTheme.Ink;
            }

            _headerText.color = _currentTheme.Ink;
            _headerText.text = FormatCounter(currentLevel, maxLevel);

            _pageText.color = _currentTheme.SoftInk;
            _pageText.text = FormatCounter(_pageIndex + 1, PageCount);

            _descriptionText.color = _currentTheme.SoftInk;
            _descriptionText.text = DescribeLevel(currentLevel);

            for (int nodeIndex = 0; nodeIndex < _nodes.Length; nodeIndex++)
            {
                RefreshNode(nodeIndex, (_pageIndex * PAGE_SIZE) + nodeIndex + 1, currentLevel, maxLevel);
            }
        }

        /// <summary>
        /// Paints one node in one of the three ladder states. Progression is strictly linear, so the
        /// state is a comparison against the current level rather than a stored unlock set.
        /// </summary>
        private void RefreshNode(int nodeIndex, int levelNumber, int currentLevel, int maxLevel)
        {
            LevelNode node = _nodes[nodeIndex];

            // The last page is rarely full; the surplus widgets are hidden rather than drawn empty.
            bool exists = levelNumber <= maxLevel;
            if (node.Root.gameObject.activeSelf != exists)
            {
                node.Root.gameObject.SetActive(exists);
            }

            if (!exists)
            {
                return;
            }

            bool isCurrent = levelNumber == currentLevel;
            bool isLocked = levelNumber > currentLevel;
            float alpha = isLocked ? LOCKED_NODE_ALPHA : 1f;

            // Cleared nodes sit on the same neutral plate the settings list uses for its badges, which
            // is derived from the Ink/CardBackground pair and so stays readable in every theme.
            Color clearedPlate = Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.16f);

            // The current node inverts — accent plate, number punched out of it — the same way
            // PowerUpInventoryView marks the armed slot.
            Color plateColour = isCurrent ? _currentTheme.Accent : clearedPlate;
            Color numberColour = isCurrent
                ? _currentTheme.CardBackground
                : (isLocked ? _currentTheme.SoftInk : _currentTheme.Ink);

            node.PlateImage.color = WithAlpha(plateColour, alpha);
            node.ShadowImage.color = WithAlpha(_currentTheme.CardShadow, alpha);
            node.NumberText.color = WithAlpha(numberColour, alpha);

            // Only a cleared node carries the accent dot: the current node is already accent-filled,
            // and a locked one has nothing to mark.
            node.DoneDot.color = !isCurrent && !isLocked ? _currentTheme.Accent : Color.clear;

            float scale = isCurrent ? CURRENT_NODE_SCALE : 1f;
            node.Root.localScale = new Vector3(scale, scale, 1f);

            _stringBuilder.Clear();
            _stringBuilder.Append(levelNumber);
            node.NumberText.text = _stringBuilder.ToString();
        }

        /// <summary>
        /// The current level's goal, worded by the same formatter the objective HUD uses, so the card
        /// and the HUD can never describe the same level differently. An unauthored or invalid entry
        /// shows nothing rather than throwing out of <c>ToObjectiveDefinition</c>.
        /// </summary>
        private string DescribeLevel(int levelNumber)
        {
            LevelObjectiveConfig config = _levelCatalog.Find(levelNumber);
            if (config == null || !config.IsValid(out _))
            {
                return string.Empty;
            }

            ObjectiveDefinition definition = config.ToObjectiveDefinition();
            return ObjectiveDescriptionFormatter.Describe(definition, _localizationSystem);
        }

        private string FormatCounter(int value, int total)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(value);
            _stringBuilder.Append(COUNTER_SEPARATOR);
            _stringBuilder.Append(total);
            return _stringBuilder.ToString();
        }

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("LevelPathPanel", typeof(RectTransform), typeof(Image));
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
                panelRect, "LevelPathCard", _cardSize, out _cardImage, out _cardShadowImage);

            float cardHalfHeight = _cardSize.y * 0.5f;
            float cardHalfWidth = _cardSize.x * 0.5f;
            float headerY = cardHalfHeight - HEADER_INSET;

            _headerText = CreateLabel(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-cardHalfWidth + SIDE_INSET, headerY));

            BuildCloseButton(
                _cardRect, new Vector2(cardHalfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f), headerY));

            BuildGrid(cardHalfHeight);

            float pagerY = -cardHalfHeight + HEADER_INSET - 14f;

            _descriptionText = CreateLabel(
                _cardRect, "Description", _descriptionFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, pagerY + 90f));

            _pageText = CreateLabel(
                _cardRect, "PageCounter", _pageFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, pagerY));

            float pagerX = cardHalfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f);
            _previousPageRect = BuildChevronButton(_cardRect, "PreviousPage", new Vector2(-pagerX, pagerY), -1f);
            _nextPageRect = BuildChevronButton(_cardRect, "NextPage", new Vector2(pagerX, pagerY), 1f);

            _panel = panelObject;
        }

        private void BuildGrid(float cardHalfHeight)
        {
            float gridHeight = ((PAGE_ROW_COUNT - 1) * NODE_SPACING_Y) + NODE_SIZE;
            float gridCentreY = cardHalfHeight - GRID_TOP_INSET - (gridHeight * 0.5f);

            for (int nodeIndex = 0; nodeIndex < PAGE_SIZE; nodeIndex++)
            {
                int column = nodeIndex % PAGE_COLUMN_COUNT;
                int row = nodeIndex / PAGE_COLUMN_COUNT;

                float x = (column - ((PAGE_COLUMN_COUNT - 1) * 0.5f)) * NODE_SPACING_X;
                float y = gridCentreY + ((((PAGE_ROW_COUNT - 1) * 0.5f) - row) * NODE_SPACING_Y);

                _nodes[nodeIndex] = BuildNode(nodeIndex, new Vector2(x, y));
            }
        }

        private LevelNode BuildNode(int nodeIndex, Vector2 anchoredPosition)
        {
            var nodeObject = new GameObject($"LevelNode_{nodeIndex}", typeof(RectTransform));
            var nodeRect = (RectTransform)nodeObject.transform;
            nodeRect.SetParent(_cardRect, false);
            Centre(nodeRect, new Vector2(NODE_SIZE, NODE_SIZE));
            nodeRect.anchoredPosition = anchoredPosition;

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(nodeRect, false);
            Centre(shadowRect, new Vector2(NODE_SIZE + 10f, NODE_SIZE + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            var shadowImage = shadowObject.GetComponent<Image>();
            ConfigureRounded(shadowImage);

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(nodeRect, false);
            Centre(plateRect, new Vector2(NODE_SIZE, NODE_SIZE));
            var plateImage = plateObject.GetComponent<Image>();
            ConfigureRounded(plateImage);

            Text numberText = UiTextFactory.Create(
                nodeRect, "Number", _nodeFontSize, FontStyle.Bold, Color.clear);

            // Same filled accent dot the objective HUD uses for "done", in the corner so it never
            // crowds the number.
            var dotObject = new GameObject("DoneDot", typeof(RectTransform), typeof(Image));
            var dotRect = (RectTransform)dotObject.transform;
            dotRect.SetParent(nodeRect, false);
            Centre(dotRect, new Vector2(NODE_DOT_SIZE, NODE_DOT_SIZE));
            dotRect.anchoredPosition = new Vector2(
                (NODE_SIZE * 0.5f) - 26f, (-NODE_SIZE * 0.5f) + 26f);
            var dotImage = dotObject.GetComponent<Image>();
            ConfigureCircle(dotImage);

            return new LevelNode(nodeRect, plateImage, shadowImage, dotImage, numberText);
        }

        /// <summary>Two bars crossed at right angles — the close glyph, as on the settings card.</summary>
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

        /// <summary>
        /// One pager: an invisible tap rect with a chevron drawn in it. <paramref name="directionX"/>
        /// is +1 for the next-page chevron, -1 for the previous-page one.
        /// </summary>
        private RectTransform BuildChevronButton(
            RectTransform root, string objectName, Vector2 anchoredPosition, float directionX)
        {
            const float CHEVRON_HALF_SIZE = 16f;
            const float CHEVRON_THICKNESS = 8f;

            var buttonObject = new GameObject(objectName, typeof(RectTransform));
            var buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(root, false);
            Centre(buttonRect, new Vector2(ICON_BUTTON_SIZE, ICON_BUTTON_SIZE));
            buttonRect.anchoredPosition = anchoredPosition;

            float armLength = (CHEVRON_HALF_SIZE * Mathf.Sqrt(2f)) + CHEVRON_THICKNESS;

            for (int armIndex = 0; armIndex < 2; armIndex++)
            {
                float sign = armIndex == 0 ? 1f : -1f;
                var armObject = new GameObject($"ChevronArm_{armIndex}", typeof(RectTransform), typeof(Image));
                var armRect = (RectTransform)armObject.transform;
                armRect.SetParent(buttonRect, false);
                Centre(armRect, new Vector2(armLength, CHEVRON_THICKNESS));
                armRect.anchoredPosition = new Vector2(0f, sign * CHEVRON_HALF_SIZE * 0.5f);
                armRect.localRotation = Quaternion.Euler(0f, 0f, -45f * sign * directionX);

                var armImage = armObject.GetComponent<Image>();
                ConfigureRounded(armImage);
                _inkImages.Add(armImage);
            }

            return buttonRect;
        }

        /// <summary>
        /// Builds a wordless label. Every caller fills it in from <see cref="Refresh"/>, because every
        /// label on this card carries a value rather than a plain String Table lookup.
        /// </summary>
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

            // Pivot on the aligned edge so the anchored position is that edge, whatever the string
            // ends up measuring — labels overflow their rect by design (see UiTextFactory).
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

        // The circle sprite has no border, so it must never be sliced.
        private static void ConfigureCircle(Image image)
        {
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            image.raycastTarget = false;
        }
    }
}
