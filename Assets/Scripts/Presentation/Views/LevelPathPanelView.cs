using System.Collections.Generic;
using System.Text;
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
    /// The level path overlay: the whole authored ladder as a winding seasonal trail the player
    /// scrolls, with their own position highlighted.
    /// <para>
    /// Whether a node is a button is a property of the mode, not of this card. In
    /// <see cref="GameMode.Path"/> a run is bounded to one level and the player picks which, so an
    /// unlocked node starts a run there; in Endless and Timed there is no such thing as "playing one
    /// level", so a node stays exactly what it has always been — a status light whose tap is a
    /// deliberate no-op. The View states neither rule itself: it asks
    /// <see cref="LevelProgressionSystem.TryStartPathLevel"/>, which refuses outside Path mode.
    /// </para>
    /// <para>
    /// Scrolled rather than paged, which is why this is the one overlay in the scene driven by an
    /// EventSystem instead of by <see cref="BoardInputView"/>'s manual hit-testing: a ScrollRect needs
    /// real drag events, and once drags are real the difference between a tap and the first frame of a
    /// drag has to come from uGUI's drag threshold rather than from a hand-rolled guess (see
    /// <see cref="LevelPathNodeButton"/>). <see cref="BoardInputView"/> therefore only swallows the
    /// press while this panel is open; every interaction inside it belongs to the EventSystem.
    /// </para>
    /// <para>
    /// The trail is drawn from the same primitives as the rest of this UI — rotated rounded-rect bars
    /// between consecutive waypoints for the ribbon, circles for the seasonal specks — so no art asset
    /// is needed and the whole card keeps batching. Its colours come from the active
    /// <see cref="ThemeDefinition"/>'s trail fields, so Kış reads as snow and Sonbahar as leaf litter.
    /// </para>
    /// <para>
    /// Built once in <see cref="Start"/> for the entire catalog and toggled with SetActive, the same
    /// way <see cref="SettingsPanelView"/> is. Layout is computed once because it never changes; only
    /// colours, node state and text are repainted, so scrolling instantiates nothing and allocates
    /// nothing. <see cref="Open"/> jumps straight to the player's own node, so they never have to
    /// scroll-hunt for their position.
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
        // Layout, in canvas reference pixels, matching the settings card's 880pt width and its
        // header/side insets so the two overlays read as one family.
        private const float HEADER_INSET = 92f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;

        /// <summary>Card top edge to the Path-mode running-total line — the band between the header
        /// and the top of the trail.</summary>
        private const float PATH_TOTAL_INSET = 152f;

        /// <summary>Smaller than the other labels on this line: the hint is a sentence rather than a
        /// value, and it shares its line with the running total.</summary>
        private const int TAP_HINT_FONT_SIZE = 28;

        // The scrolling window: everything between the running-total line and the description at the
        // foot of the card.
        private const float TRAIL_TOP_INSET = 194f;
        private const float TRAIL_BOTTOM_INSET = 150f;
        private const float TRAIL_SIDE_INSET = 44f;

        /// <summary>Card bottom edge to the current level's description.</summary>
        private const float DESCRIPTION_INSET = 96f;

        /// <summary>
        /// Distance the trail's sweep keeps from either edge of the scroll content. A node is centred
        /// on its waypoint, so this is its half-width plus a margin — which is what stops the outermost
        /// nodes of the sweep being clipped by the viewport.
        /// </summary>
        private const float TRAIL_SWEEP_INSET = 170f;

        /// <summary>Breathing room above the first node and below the last, so level 1 and the last
        /// level do not sit flush against the clip edge at the ends of the scroll.</summary>
        private const float TRAIL_VERTICAL_PADDING = 120f;

        private const float RIBBON_THICKNESS = 22f;
        private const float ACCENT_SIZE = 18f;

        /// <summary>How far off the ribbon a seasonal speck sits, measured perpendicular to it.</summary>
        private const float ACCENT_OFFSET = 34f;

        /// <summary>One speck per this many gaps. Every gap reads as a dotted line rather than as
        /// weather, and it would double the trail's object count for nothing.</summary>
        private const int ACCENT_GAP_STRIDE = 2;

        private const float NODE_SIZE = 124f;
        private const float NODE_DOT_SIZE = 22f;

        /// <summary>Scale applied to the node the player is on, so "you are here" reads without new art.</summary>
        private const float CURRENT_NODE_SCALE = 1.08f;

        /// <summary>
        /// Alpha applied to a locked node and to the stretch of trail leading to it. Deliberately the
        /// same dim as <see cref="PowerUpInventoryView"/>'s empty slot: "you cannot have this yet" is
        /// one visual idea and should look identical wherever it appears.
        /// </summary>
        private const float LOCKED_NODE_ALPHA = 0.35f;

        /// <summary>How much of the trail's own colour is mixed into the card behind it, so the
        /// scrolling band reads as this theme's ground rather than as bare card.</summary>
        private const float TRAIL_BACKDROP_MIX = 0.38f;

        /// <summary>Separator in the header counter. A symbol, not a word — nothing here for a
        /// translator to translate, so it stays out of the String Table.</summary>
        private const string COUNTER_SEPARATOR = " / ";

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        /// <summary>One widget per authored level, indexed by level number minus one. Sized from the
        /// catalog in <see cref="BuildTrail"/>, so it is only allocated once the catalog is injected.</summary>
        private LevelNode[] _nodes = System.Array.Empty<LevelNode>();

        /// <summary>Repaint bucket: every Image that follows the theme's ink (the close cross), so a
        /// theme switch is one tight loop instead of a hierarchy walk.</summary>
        private readonly List<Image> _inkImages = new List<Image>(4);

        /// <summary>The ribbon bars, in walk order. Each carries the level it leads to, so a locked
        /// stretch dims with the node it ends at.</summary>
        private readonly List<TrailPiece> _ribbonPieces = new List<TrailPiece>(128);

        /// <summary>The seasonal specks, dimmed by the same rule as the ribbon under them.</summary>
        private readonly List<TrailPiece> _accentPieces = new List<TrailPiece>(64);

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1336f);
        [SerializeField] private int _headerFontSize = 64;
        [SerializeField] private int _nodeFontSize = 40;
        [SerializeField] private int _descriptionFontSize = 34;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private LevelProgressionModel _levelProgressionModel;
        private PathRunModel _pathRunModel;
        private LevelCatalog _levelCatalog;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private LevelProgressionSystem _levelProgressionSystem;
        private GameModeSystem _gameModeSystem;
        private TimerRunSystem _timerRunSystem;

        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;

        private ScrollRect _trailScroll;
        private Image _trailBackdropImage;
        private RectTransform _trailViewportRect;
        private RectTransform _trailContentRect;

        private Text _headerText;
        private Text _descriptionText;
        private Text _pathTotalText;
        private Text _tapHintText;

        private ThemeDefinition _currentTheme;

        /// <summary>One built node widget. Rebuilt never, repainted whenever the ladder or theme moves.</summary>
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

        /// <summary>
        /// One drawn scrap of trail — a ribbon bar or a seasonal speck — paired with the level it
        /// belongs to. The pairing is what lets the trail dim exactly where the ladder stops instead of
        /// being uniformly bright past the player's frontier.
        /// </summary>
        private sealed class TrailPiece
        {
            internal TrailPiece(Image image, int levelNumber)
            {
                Image = image;
                LevelNumber = levelNumber;
            }

            internal Image Image { get; }

            internal int LevelNumber { get; }
        }

        [Inject]
        public void Construct(
            LevelProgressionModel levelProgressionModel,
            PathRunModel pathRunModel,
            LevelCatalog levelCatalog,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            LevelProgressionSystem levelProgressionSystem,
            GameModeSystem gameModeSystem,
            TimerRunSystem timerRunSystem)
        {
            _levelProgressionModel = levelProgressionModel;
            _pathRunModel = pathRunModel;
            _levelCatalog = levelCatalog;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _levelProgressionSystem = levelProgressionSystem;
            _gameModeSystem = gameModeSystem;
            _timerRunSystem = timerRunSystem;
        }

        private void Start()
        {
            if (_levelProgressionModel == null || _pathRunModel == null || _levelCatalog == null
                || _settingsModel == null || _localizationModel == null || _localizationSystem == null
                || _levelProgressionSystem == null || _gameModeSystem == null || _timerRunSystem == null)
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

            // Same reason for both: the walk's tally moves and the mode changes while the card is
            // hidden, and each decides something the card renders.
            _pathRunModel.PathTotalScore.Subscribe(OnPathTotalChanged).AddTo(_disposables);
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the panel, scrolled straight to the player's current level. Re-opening never
        /// double-pauses the clock: an already-open panel returns immediately.
        /// </summary>
        internal void Open()
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            Refresh();

            _panel.SetActive(true);
            transform.SetAsLastSibling();

            // After SetActive, so the ScrollRect's rects are live and the scroll it is handed sticks.
            ScrollTo(_levelProgressionModel.CurrentLevelNumber.Value);

            _timerRunSystem.SetMenuPaused(true);
        }

        private void Close()
        {
            _panel.SetActive(false);
            _timerRunSystem.SetMenuPaused(false);
        }

        /// <summary>
        /// Centres the viewport on a level's node, clamped to the content so either end of the walk
        /// stops at the last node rather than scrolling past it into empty trail.
        /// <para>
        /// The content position is written directly rather than through
        /// <c>verticalNormalizedPosition</c>: the normalized form divides by the scrollable span, which
        /// is zero for a catalog short enough to fit the viewport, and this way that case simply
        /// resolves to the top.
        /// </para>
        /// </summary>
        private void ScrollTo(int levelNumber)
        {
            if (_trailScroll == null)
            {
                return;
            }

            // Kills any inertia left over from the previous time the card was open, which would
            // otherwise carry the trail away from the node we just aimed at.
            _trailScroll.StopMovement();

            float viewportHeight = _trailViewportRect.rect.height;
            float scrollableHeight = Mathf.Max(0f, _trailContentRect.rect.height - viewportHeight);

            int levelIndex = Mathf.Max(0, levelNumber - 1);
            float nodeDepth = TRAIL_VERTICAL_PADDING + (levelIndex * LevelPathTrailLayout.ROW_SPACING);

            float scroll = Mathf.Clamp(nodeDepth - (viewportHeight * 0.5f), 0f, scrollableHeight);
            _trailContentRect.anchoredPosition = new Vector2(0f, scroll);
        }

        /// <summary>
        /// Starts a run at this node's level when the active mode allows it.
        /// <para>
        /// The tap is offered to <see cref="LevelProgressionSystem.TryStartPathLevel"/> rather than
        /// gated on the mode or the unlock here. The System already has to refuse a locked or
        /// unauthored level, so letting it also own "and only in Path mode" keeps one answer to "may
        /// this level be started" instead of two that can drift. A refusal leaves the card open and
        /// unchanged, which is exactly the read-only no-op Endless and Timed have always had.
        /// </para>
        /// </summary>
        private void OnNodeClicked(int levelNumber)
        {
            if (_levelProgressionSystem.TryStartPathLevel(levelNumber))
            {
                // Closing is part of starting: the run is under this card, and leaving a modal open
                // over a run that has just begun would also leave the countdown held.
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

        private void OnCurrentLevelChanged(int levelNumber) => Refresh();

        private void OnPathTotalChanged(int pathTotal) => Refresh();

        private void OnModeChanged(GameMode mode) => Refresh();

        /// <summary>
        /// Repaints the whole card from the models: header, description, the trail's colours and every
        /// node's state. Layout is never recomputed — the waypoints are a function of the level count
        /// alone, which cannot change at runtime.
        /// </summary>
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
            _trailBackdropImage.color = Color.Lerp(
                _currentTheme.CardBackground, _currentTheme.TrailPathColor, TRAIL_BACKDROP_MIX);

            for (int inkIndex = 0; inkIndex < _inkImages.Count; inkIndex++)
            {
                _inkImages[inkIndex].color = _currentTheme.Ink;
            }

            _headerText.color = _currentTheme.Ink;
            _headerText.text = FormatCounter(currentLevel, maxLevel);

            _descriptionText.color = _currentTheme.SoftInk;
            _descriptionText.text = DescribeLevel(currentLevel);

            // Both lines are Path-mode-only facts: a total across levels, and a nudge that the nodes
            // are tappable. Hidden rather than blanked in the other modes, so the card keeps the exact
            // layout it has always had there.
            bool isPathMode = _gameModeSystem.CurrentMode.Value == GameMode.Path;
            _pathTotalText.gameObject.SetActive(isPathMode);
            _tapHintText.gameObject.SetActive(isPathMode);

            if (isPathMode)
            {
                _pathTotalText.color = _currentTheme.Ink;
                _stringBuilder.Clear();
                _stringBuilder.Append(_pathRunModel.PathTotalScore.Value);
                _pathTotalText.text = _localizationSystem.Format(
                    LocalizationKeys.LEVEL_PATH_TOTAL, _stringBuilder.ToString());

                _tapHintText.color = _currentTheme.SoftInk;
                _tapHintText.text = _localizationSystem.Translate(LocalizationKeys.LEVEL_PATH_TAP_HINT);
            }

            RefreshTrailPieces(_ribbonPieces, _currentTheme.TrailPathColor);
            RefreshTrailPieces(_accentPieces, _currentTheme.TrailWeatherAccentColor);

            for (int nodeIndex = 0; nodeIndex < _nodes.Length; nodeIndex++)
            {
                RefreshNode(_nodes[nodeIndex], nodeIndex + 1, currentLevel);
            }
        }

        /// <summary>Tints one bucket of trail scraps, dimming the stretch that runs past the player's
        /// frontier so the walk visibly stops where the ladder does.</summary>
        private void RefreshTrailPieces(List<TrailPiece> pieces, Color colour)
        {
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                TrailPiece piece = pieces[pieceIndex];
                float alpha = _levelProgressionSystem.IsUnlocked(piece.LevelNumber) ? 1f : LOCKED_NODE_ALPHA;
                piece.Image.color = WithAlpha(colour, alpha);
            }
        }

        /// <summary>
        /// Paints one node in one of the three ladder states. Locked is asked of
        /// <see cref="LevelProgressionSystem.IsUnlocked"/> rather than re-derived from the current
        /// level here, so what the card draws as reachable and what it will actually let the player
        /// start are the same rule.
        /// </summary>
        private void RefreshNode(LevelNode node, int levelNumber, int currentLevel)
        {
            bool isCurrent = levelNumber == currentLevel;
            bool isLocked = !_levelProgressionSystem.IsUnlocked(levelNumber);
            float alpha = isLocked ? LOCKED_NODE_ALPHA : 1f;

            // The current node inverts — accent plate, number punched out of it — the same way
            // PowerUpInventoryView marks the armed slot.
            Color plateColour = isCurrent ? _currentTheme.Accent : _currentTheme.TrailNodePlateColor;
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

            var panelObject = new GameObject(
                "LevelPathPanel", typeof(RectTransform), typeof(Image), typeof(LevelPathNodeButton));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // The scrim is the "tapped outside the card" target. It is the only graphic under this
            // panel that is allowed to close it, and the card's own background sits above it as a
            // raycast blocker so a tap on the card never falls through — the standard uGUI modal
            // sandwich, and the reason both of them take raycasts when nothing else in this scene does.
            var scrimImage = panelObject.GetComponent<Image>();
            scrimImage.color = _scrimColour;
            scrimImage.raycastTarget = true;
            panelObject.GetComponent<LevelPathNodeButton>().SetClicked(Close);

            _cardRect = CellFactory.CreateCard(
                panelRect, "LevelPathCard", _cardSize, out _cardImage, out _cardShadowImage);
            _cardImage.raycastTarget = true;

            float cardHalfHeight = _cardSize.y * 0.5f;
            float cardHalfWidth = _cardSize.x * 0.5f;
            float headerY = cardHalfHeight - HEADER_INSET;

            _headerText = CreateLabel(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-cardHalfWidth + SIDE_INSET, headerY));

            BuildCloseButton(
                _cardRect, new Vector2(cardHalfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f), headerY));

            // Both sit in the band between the header line and the top of the trail — the one strip of
            // the card that is otherwise empty. The foot of the card is not an option: the level
            // description already has it, and a third line there would crowd the trail's clip edge.
            _pathTotalText = CreateLabel(
                _cardRect, "PathTotal", _descriptionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-cardHalfWidth + SIDE_INSET, cardHalfHeight - PATH_TOTAL_INSET));

            _tapHintText = CreateLabel(
                _cardRect, "TapHint", TAP_HINT_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleRight,
                new Vector2(cardHalfWidth - SIDE_INSET, cardHalfHeight - PATH_TOTAL_INSET));

            BuildTrail(cardHalfHeight);

            _descriptionText = CreateLabel(
                _cardRect, "Description", _descriptionFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, -cardHalfHeight + DESCRIPTION_INSET));

            _panel = panelObject;
        }

        /// <summary>
        /// Builds the scrolling trail: a clipped viewport, a content rect sized exactly to the authored
        /// level count, then the ribbon, the seasonal specks and every node laid onto it.
        /// <para>
        /// The ribbon and specks are added before the nodes so sibling order draws them underneath,
        /// which is also what keeps a node's plate — the only raycast target in here besides the
        /// viewport — on top of the trail it sits on.
        /// </para>
        /// </summary>
        private void BuildTrail(float cardHalfHeight)
        {
            float viewportWidth = _cardSize.x - (TRAIL_SIDE_INSET * 2f);
            float viewportHeight = _cardSize.y - TRAIL_TOP_INSET - TRAIL_BOTTOM_INSET;

            var viewportObject = new GameObject(
                "TrailViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            _trailViewportRect = (RectTransform)viewportObject.transform;
            _trailViewportRect.SetParent(_cardRect, false);
            Centre(_trailViewportRect, new Vector2(viewportWidth, viewportHeight));
            _trailViewportRect.anchoredPosition = new Vector2(
                0f, cardHalfHeight - TRAIL_TOP_INSET - (viewportHeight * 0.5f));

            // The backdrop doubles as the ScrollRect's own raycast target: a drag has to land on a
            // graphic to reach the ScrollRect at all, and this is the one that covers the whole band,
            // so the trail scrolls from anywhere inside it rather than only from a node.
            _trailBackdropImage = viewportObject.GetComponent<Image>();
            ConfigureRounded(_trailBackdropImage);
            _trailBackdropImage.raycastTarget = true;

            int levelCount = Mathf.Max(1, _levelCatalog.MaxLevelNumber);
            float contentHeight = LevelPathTrailLayout.ContentHeight(levelCount, TRAIL_VERTICAL_PADDING);

            var contentObject = new GameObject("TrailContent", typeof(RectTransform));
            _trailContentRect = (RectTransform)contentObject.transform;
            _trailContentRect.SetParent(_trailViewportRect, false);

            // Anchored and pivoted at the top so anchoredPosition.y is simply "how far down the walk
            // we are", which is the form ScrollTo clamps against the content height.
            _trailContentRect.anchorMin = new Vector2(0.5f, 1f);
            _trailContentRect.anchorMax = new Vector2(0.5f, 1f);
            _trailContentRect.pivot = new Vector2(0.5f, 1f);
            _trailContentRect.sizeDelta = new Vector2(viewportWidth, contentHeight);
            _trailContentRect.anchoredPosition = Vector2.zero;

            _trailScroll = viewportObject.GetComponent<ScrollRect>();
            _trailScroll.horizontal = false;
            _trailScroll.vertical = true;

            // Clamped rather than elastic: the content is sized to the node extents, so there is
            // nothing past either end worth bouncing into.
            _trailScroll.movementType = ScrollRect.MovementType.Clamped;
            _trailScroll.viewport = _trailViewportRect;
            _trailScroll.content = _trailContentRect;

            float amplitude = (viewportWidth * 0.5f) - TRAIL_SWEEP_INSET;

            BuildRibbon(levelCount, amplitude);
            BuildNodes(levelCount, amplitude);
        }

        /// <summary>
        /// Draws the ribbon as one rotated rounded bar per gap between consecutive waypoints, with a
        /// seasonal speck dropped beside every few of them. Bars rather than a single generated mesh so
        /// the trail uses the same shared sprite as everything else on the card and costs no draw call
        /// of its own.
        /// </summary>
        private void BuildRibbon(int levelCount, float amplitude)
        {
            for (int gapIndex = 0; gapIndex < levelCount - 1; gapIndex++)
            {
                Vector2 from = LevelPathTrailLayout.WaypointOf(gapIndex, amplitude);
                Vector2 to = LevelPathTrailLayout.WaypointOf(gapIndex + 1, amplitude);
                Vector2 delta = to - from;
                float length = delta.magnitude;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

                // Overlapped by its own thickness so consecutive bars meet without a notch at the
                // corner where the sweep turns.
                Image barImage = CreateTrailImage(
                    $"TrailSegment_{gapIndex}",
                    new Vector2(length + RIBBON_THICKNESS, RIBBON_THICKNESS),
                    (from + to) * 0.5f,
                    angle,
                    rounded: true);

                _ribbonPieces.Add(new TrailPiece(barImage, gapIndex + 2));

                if (gapIndex % ACCENT_GAP_STRIDE != 0)
                {
                    continue;
                }

                // Alternating sides, so the specks scatter along the walk instead of hugging one edge.
                float side = (gapIndex / ACCENT_GAP_STRIDE) % 2 == 0 ? 1f : -1f;
                Vector2 perpendicular = length > 0f
                    ? new Vector2(-delta.y, delta.x) / length
                    : Vector2.right;

                Vector2 accentCentre = LevelPathTrailLayout.PointBetween(gapIndex, 0.5f, amplitude)
                    + (perpendicular * (side * ACCENT_OFFSET));

                Image accentImage = CreateTrailImage(
                    $"TrailAccent_{gapIndex}",
                    new Vector2(ACCENT_SIZE, ACCENT_SIZE),
                    accentCentre,
                    0f,
                    rounded: false);

                _accentPieces.Add(new TrailPiece(accentImage, gapIndex + 2));
            }
        }

        /// <summary>One decorative scrap of trail, parented to the scroll content and painted later
        /// from the theme.</summary>
        private Image CreateTrailImage(
            string objectName, Vector2 size, Vector2 waypoint, float angle, bool rounded)
        {
            var pieceObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var pieceRect = (RectTransform)pieceObject.transform;
            pieceRect.SetParent(_trailContentRect, false);
            AnchorToContentTop(pieceRect, size, waypoint);
            pieceRect.localRotation = Quaternion.Euler(0f, 0f, angle);

            var pieceImage = pieceObject.GetComponent<Image>();
            if (rounded)
            {
                ConfigureRounded(pieceImage);
            }
            else
            {
                ConfigureCircle(pieceImage);
            }

            return pieceImage;
        }

        private void BuildNodes(int levelCount, float amplitude)
        {
            _nodes = new LevelNode[levelCount];
            for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
            {
                _nodes[levelIndex] = BuildNode(
                    levelIndex + 1, LevelPathTrailLayout.WaypointOf(levelIndex, amplitude));
            }
        }

        private LevelNode BuildNode(int levelNumber, Vector2 waypoint)
        {
            var nodeObject = new GameObject(
                $"LevelNode_{levelNumber}", typeof(RectTransform), typeof(LevelPathNodeButton));
            var nodeRect = (RectTransform)nodeObject.transform;
            nodeRect.SetParent(_trailContentRect, false);
            AnchorToContentTop(nodeRect, new Vector2(NODE_SIZE, NODE_SIZE), waypoint);

            // Closes over the level rather than deriving it from an index, because unlike the old paged
            // grid this widget is this level for the panel's whole life.
            nodeObject.GetComponent<LevelPathNodeButton>().SetClicked(() => OnNodeClicked(levelNumber));

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

            // The plate is the node's hit area: the node root carries the click handler but no graphic
            // of its own, and uGUI dispatches a click up the hierarchy from whatever graphic it hit.
            plateImage.raycastTarget = true;

            Text numberText = UiTextFactory.Create(
                nodeRect, "Number", _nodeFontSize, FontStyle.Bold, Color.clear);

            // Same filled accent dot the objective HUD uses for "done", in the corner so it never
            // crowds the number.
            var dotObject = new GameObject("DoneDot", typeof(RectTransform), typeof(Image));
            var dotRect = (RectTransform)dotObject.transform;
            dotRect.SetParent(nodeRect, false);
            Centre(dotRect, new Vector2(NODE_DOT_SIZE, NODE_DOT_SIZE));
            dotRect.anchoredPosition = new Vector2(
                (NODE_SIZE * 0.5f) - 22f, (-NODE_SIZE * 0.5f) + 22f);
            var dotImage = dotObject.GetComponent<Image>();
            ConfigureCircle(dotImage);

            // The number never changes for a given widget, so it is written here rather than in
            // Refresh — one less string built per repaint, times the whole catalog.
            _stringBuilder.Clear();
            _stringBuilder.Append(levelNumber);
            numberText.text = _stringBuilder.ToString();

            return new LevelNode(nodeRect, plateImage, shadowImage, dotImage, numberText);
        }

        /// <summary>Two bars crossed at right angles — the close glyph, as on the settings card — over
        /// an invisible plate that gives it something for the EventSystem to hit.</summary>
        private void BuildCloseButton(RectTransform root, Vector2 anchoredPosition)
        {
            const float CROSS_LENGTH = 46f;
            const float CROSS_THICKNESS = 8f;

            var closeObject = new GameObject(
                "CloseButton", typeof(RectTransform), typeof(Image), typeof(LevelPathNodeButton));
            var closeRect = (RectTransform)closeObject.transform;
            closeRect.SetParent(root, false);
            Centre(closeRect, new Vector2(ICON_BUTTON_SIZE, ICON_BUTTON_SIZE));
            closeRect.anchoredPosition = anchoredPosition;

            // Transparent but raycasting: the tap area is the whole button-sized square, not the two
            // thin bars drawn in it.
            var closeImage = closeObject.GetComponent<Image>();
            ConfigureRounded(closeImage);
            closeImage.raycastTarget = true;

            closeObject.GetComponent<LevelPathNodeButton>().SetClicked(Close);

            for (int barIndex = 0; barIndex < 2; barIndex++)
            {
                var barObject = new GameObject($"CloseBar_{barIndex}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform)barObject.transform;
                barRect.SetParent(closeRect, false);
                Centre(barRect, new Vector2(CROSS_LENGTH, CROSS_THICKNESS));
                barRect.localRotation = Quaternion.Euler(0f, 0f, barIndex == 0 ? 45f : -45f);

                var barImage = barObject.GetComponent<Image>();
                ConfigureRounded(barImage);
                _inkImages.Add(barImage);
            }
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

        /// <summary>
        /// Pins a trail widget to the top-centre of the scroll content, where
        /// <see cref="LevelPathTrailLayout"/>'s origin is, offset down by the content's own padding.
        /// Anchoring to the top rather than the centre is what keeps a waypoint's position independent
        /// of how tall the content happens to be.
        /// </summary>
        private static void AnchorToContentTop(RectTransform rect, Vector2 size, Vector2 waypoint)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(waypoint.x, waypoint.y - TRAIL_VERTICAL_PADDING);
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Raycasts default off and are turned back on one image at a time. This panel is the only
        // thing in the scene an EventSystem may touch — everything else still takes its taps from
        // BoardInputView — so a graphic here is a hit target only when it has been made one on purpose.
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
