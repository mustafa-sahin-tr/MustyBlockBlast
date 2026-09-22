using System.Collections.Generic;
using System.Text;
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
    /// The level path overlay: the whole authored ladder as a winding seasonal trail the player
    /// scrolls, with their own position highlighted.
    /// <para>
    /// Whether a node is a button is a property of the mode, not of this card. In
    /// <see cref="GameMode.Path"/> a run is bounded to one level and the player picks which, so an
    /// unlocked node starts a run there; in Endless and Timed there is no such thing as "playing one
    /// level", so a node stays exactly what it has always been — a status light whose tap is a
    /// deliberate no-op. The View states neither rule itself: it reaches
    /// <see cref="LevelProgressionSystem.TryStartPathLevel"/>, which refuses outside Path mode.
    /// </para>
    /// <para>
    /// A node tap no longer starts that run directly. It opens <see cref="CoinSowerPickerView"/> for the
    /// tapped level — the level-start screen where extra coin cells may be bought — and that card is
    /// what asks the System to start the run once the player commits, with a purchase or without one.
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
    /// <para>
    /// Two more things are drawn onto this same built-once trail (issue #278): a per-level objective
    /// glyph and milestone-reward badge on every node (see <see cref="RefreshNode"/>), and a decorative
    /// scenery backdrop that cycles through four zones every ten levels regardless of the active
    /// theme's season (see <see cref="LevelPathZones"/>, <see cref="BuildScenery"/>). Both reuse
    /// <see cref="LevelPathTrailLayout"/>'s content-local Y so neither can ever drift out of sync with
    /// the trail as it scrolls.
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

        /// <summary>The per-level objective glyph. Sized to dominate the plate — big icon, small
        /// number, per the approved path redesign — while still leaving the plate's rounded edge
        /// visible all round.</summary>
        private const float NODE_ICON_SIZE = 72f;

        /// <summary>Nudged up from dead-centre so the icon does not crowd the number badge beneath it.</summary>
        private const float NODE_ICON_OFFSET_Y = 10f;

        /// <summary>Fraction of <see cref="_nodeFontSize"/> the number shrinks to once the icon takes
        /// the centre of the plate.</summary>
        private const float NODE_NUMBER_FONT_SCALE = 0.55f;

        private const int NODE_NUMBER_MIN_FONT_SIZE = 18;

        /// <summary>How close to the plate's bottom edge the shrunken number sits.</summary>
        private const float NODE_NUMBER_OFFSET_Y = -46f;

        /// <summary>Milestone level-up reward plate, mirroring the corner treatment of
        /// <see cref="NODE_DOT_SIZE"/>'s done dot but in the opposite corner so the two never collide.</summary>
        private const float REWARD_BADGE_SIZE = 44f;

        private const float REWARD_BADGE_ICON_SIZE = 28f;

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

        // Flat zone-band colours (issue #278). Independent of ThemeDefinition on purpose — the zone a
        // stretch of path shows is a function of level number alone, never of the active season theme.
        private static readonly Color MeadowZoneColour = new Color(0.93f, 0.87f, 0.62f, 1f);
        private static readonly Color WinterZoneColour = new Color(0.85f, 0.92f, 0.97f, 1f);
        private static readonly Color CityZoneColour = new Color(0.97f, 0.75f, 0.62f, 1f);
        private static readonly Color NeighborhoodZoneColour = new Color(0.98f, 0.85f, 0.70f, 1f);

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

        [Header("Path Zone Scenery")]
        [SerializeField] private Sprite _meadowHillsSprite;
        [SerializeField] private Sprite _meadowTreeSprite;
        [SerializeField] private Sprite _meadowRiverBridgeSprite;
        [SerializeField] private Sprite _winterMountainsSprite;
        [SerializeField] private Sprite _winterPineTreeSprite;
        [SerializeField] private Sprite _winterCloudSprite;
        [SerializeField] private Sprite _citySkylineSprite;
        [SerializeField] private Sprite _cityStadiumSprite;
        [SerializeField] private Sprite _cityStreetlampSprite;
        [SerializeField] private Sprite _neighborhoodBakerySprite;
        [SerializeField] private Sprite _neighborhoodSchoolSprite;
        [SerializeField] private Sprite _neighborhoodTreeSprite;

        private LevelProgressionModel _levelProgressionModel;
        private PathRunModel _pathRunModel;
        private LevelCatalog _levelCatalog;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private LevelProgressionSystem _levelProgressionSystem;
        private GameModeSystem _gameModeSystem;
        private TimerRunSystem _timerRunSystem;

        /// <summary>The authored per-objective-type glyph, shared with the objective HUD and its info
        /// popup (see <see cref="ObjectiveIconCatalog"/>'s own doc). Reused rather than re-registered:
        /// it is already bound once in <c>GameLifetimeScope</c>.</summary>
        private ObjectiveIconCatalog _objectiveIconCatalog;

        /// <summary>Source of a power-up's glyph for a milestone node's reward badge — the same
        /// authored art <see cref="InfoPopupView"/> borrows for its own PowerUp subject.</summary>
        private PowerUpInventoryView _powerUpInventoryView;

        /// <summary>The level-start screen a node tap opens. A View dependency rather than a System one
        /// because the insertion is entirely presentational: what a node tap does now is show another
        /// card, which is what eventually starts the run.</summary>
        private CoinSowerPickerView _coinSowerPickerView;

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

        /// <summary>Set by an <see cref="Open"/> that arrives before <see cref="Start"/> has built the
        /// card; honoured at the end of Start. See <see cref="Open"/>.</summary>
        private bool _openRequestedBeforeBuild;

        /// <summary>One built node widget. Rebuilt never, repainted whenever the ladder or theme moves.</summary>
        private sealed class LevelNode
        {
            internal LevelNode(
                RectTransform root,
                Image plateImage,
                Image shadowImage,
                Image iconImage,
                Image doneDot,
                Text numberText,
                Image rewardBadgeImage,
                Image rewardIconImage)
            {
                Root = root;
                PlateImage = plateImage;
                ShadowImage = shadowImage;
                IconImage = iconImage;
                DoneDot = doneDot;
                NumberText = numberText;
                RewardBadgeImage = rewardBadgeImage;
                RewardIconImage = rewardIconImage;
            }

            internal RectTransform Root { get; }

            internal Image PlateImage { get; }

            internal Image ShadowImage { get; }

            /// <summary>The level's objective glyph. Sprite assigned once in <see cref="BuildNode"/> —
            /// it never changes — only its alpha is repainted, alongside the rest of the node.</summary>
            internal Image IconImage { get; }

            internal Image DoneDot { get; }

            internal Text NumberText { get; }

            /// <summary>Null for the great majority of nodes: only built for a level where
            /// <see cref="LevelObjectiveConfig.GrantsLevelUpReward"/> is true.</summary>
            internal Image RewardBadgeImage { get; }

            /// <summary>The granted <see cref="PowerUpKind"/>'s glyph, null alongside
            /// <see cref="RewardBadgeImage"/>.</summary>
            internal Image RewardIconImage { get; }
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
            TimerRunSystem timerRunSystem,
            CoinSowerPickerView coinSowerPickerView,
            ObjectiveIconCatalog objectiveIconCatalog,
            PowerUpInventoryView powerUpInventoryView)
        {
            _coinSowerPickerView = coinSowerPickerView;
            _levelProgressionModel = levelProgressionModel;
            _pathRunModel = pathRunModel;
            _levelCatalog = levelCatalog;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _levelProgressionSystem = levelProgressionSystem;
            _gameModeSystem = gameModeSystem;
            _timerRunSystem = timerRunSystem;
            _objectiveIconCatalog = objectiveIconCatalog;
            _powerUpInventoryView = powerUpInventoryView;
        }

        private void Start()
        {
            if (_levelProgressionModel == null || _pathRunModel == null || _levelCatalog == null
                || _settingsModel == null || _localizationModel == null || _localizationSystem == null
                || _levelProgressionSystem == null || _gameModeSystem == null || _timerRunSystem == null
                || _coinSowerPickerView == null || _objectiveIconCatalog == null || _powerUpInventoryView == null)
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

            // Last, once the card is built and painted: an Open() that arrived before Start — the
            // mode-select scene's "Macera Modu picked, open the picker on boot" request (issue #379),
            // delivered by PendingLevelPathOpenSystem's entry point — is honoured now.
            if (_openRequestedBeforeBuild)
            {
                _openRequestedBeforeBuild = false;
                Open();
            }
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Shows the panel, scrolled straight to the player's current level. Re-opening never
        /// double-pauses the clock: an already-open panel returns immediately. A call that lands
        /// before the card is built (a boot-time request from a VContainer entry point, which may run
        /// ahead of this MonoBehaviour's Start) is remembered and carried out at the end of Start.
        /// </summary>
        internal void Open()
        {
            if (_panel == null)
            {
                _openRequestedBeforeBuild = true;
                return;
            }

            if (IsOpen)
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
        /// Hands this node's level to <see cref="CoinSowerPickerView"/>, which offers the level-start
        /// purchase and is what goes on to ask
        /// <see cref="LevelProgressionSystem.TryStartPathLevel"/> once the player has committed (see
        /// issue #167).
        /// <para>
        /// The tap is still not gated on the mode or the unlock here, exactly as it was not when it
        /// started the run directly: the System already has to refuse a locked or unauthored level, and
        /// letting it also own "and only in Path mode" keeps one answer to "may this level be started"
        /// instead of two that can drift. The picker simply asks later and reports a refusal on its own
        /// card, so the read-only no-op Endless and Timed have always had costs one extra tap to dismiss
        /// and still starts nothing.
        /// </para>
        /// <para>
        /// This card is closed before the picker opens because the two are mutually exclusive overlays
        /// and both hold the countdown through the same single <see cref="TimerRunSystem"/> flag — see
        /// the gate chain in <see cref="BoardInputView"/>. Closing releases the hold this card took, and
        /// the picker takes its own; leaving both open would give that flag two owners.
        /// </para>
        /// </summary>
        private void OnNodeClicked(int levelNumber)
        {
            Close();
            _coinSowerPickerView.Open(levelNumber);
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
        /// <para>
        /// The objective icon and the milestone reward badge dim by the exact same alpha as the plate
        /// around them — a locked node has to read as one locked thing, not as a bright icon sitting on
        /// a dimmed plate.
        /// </para>
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
            node.IconImage.color = WithAlpha(Color.white, alpha);

            if (node.RewardBadgeImage != null)
            {
                node.RewardBadgeImage.color = WithAlpha(_currentTheme.Accent, alpha);
                node.RewardIconImage.color = WithAlpha(Color.white, alpha);
            }

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
            return ObjectiveDescriptionFormatter.Describe(definition, _localizationSystem, _currentTheme);
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
        /// level count, then the zone scenery, the ribbon, the seasonal specks and every node laid onto
        /// it.
        /// <para>
        /// The scenery is added before the ribbon and specks, which are in turn added before the nodes,
        /// so sibling order draws the backdrop under both — which is also what keeps a node's plate —
        /// the only raycast target in here besides the viewport — on top of everything it sits on.
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

            BuildScenery(levelCount, viewportWidth, contentHeight);
            BuildRibbon(levelCount, amplitude);
            BuildNodes(levelCount, amplitude);
        }

        /// <summary>
        /// Builds the decorative backdrop behind the trail and its nodes: one flat-coloured band per
        /// ten-level zone (see <see cref="LevelPathZones"/>), each carrying a handful of that zone's
        /// scenery sprites at fixed offsets from the same source fal.ai mockups converted for issue
        /// #278. Built once, up front, for the whole catalog — same as the ribbon and the nodes — using
        /// <see cref="LevelPathTrailLayout"/>'s content-local Y (via <see cref="ContentY"/>), so the
        /// backdrop scrolls in lockstep with the trail with no separate coordinate space to drift out
        /// of sync.
        /// </summary>
        private void BuildScenery(int levelCount, float viewportWidth, float contentHeight)
        {
            int zoneBandCount = Mathf.CeilToInt(levelCount / (float)LevelPathZones.LEVELS_PER_ZONE);
            for (int bandIndex = 0; bandIndex < zoneBandCount; bandIndex++)
            {
                int startIndex = bandIndex * LevelPathZones.LEVELS_PER_ZONE;
                int endIndexExclusive = Mathf.Min(startIndex + LevelPathZones.LEVELS_PER_ZONE, levelCount);
                LevelPathZoneKind zone = LevelPathZones.ZoneFor(startIndex + 1);

                // The seam between two bands sits halfway between the last node of one and the first of
                // the next, so neither node reads as belonging to the "wrong" band's colour. The very
                // top and bottom instead run to the content edges, so there is no gap of bare backdrop
                // above level 1 or below the last level.
                float topY = bandIndex == 0
                    ? 0f
                    : (ContentY(startIndex - 1) + ContentY(startIndex)) * 0.5f;
                float bottomY = endIndexExclusive >= levelCount
                    ? -contentHeight
                    : (ContentY(endIndexExclusive - 1) + ContentY(endIndexExclusive)) * 0.5f;

                BuildZoneBand(bandIndex, zone, viewportWidth, topY, bottomY);
            }
        }

        /// <summary>Content-local Y of a node's row, independent of sweep amplitude — the same value
        /// <see cref="AnchorToContentTop"/> offsets a node to, so a scenery band lines up exactly with
        /// the nodes it surrounds.</summary>
        private static float ContentY(int levelIndex)
            => LevelPathTrailLayout.WaypointOf(levelIndex, 0f).y - TRAIL_VERTICAL_PADDING;

        /// <summary>One zone's flat backdrop band plus its scattered scenery, all parented to the
        /// scroll content so they pan with it.</summary>
        private void BuildZoneBand(int bandIndex, LevelPathZoneKind zone, float viewportWidth, float topY, float bottomY)
        {
            float bandHeight = topY - bottomY;

            var bandObject = new GameObject($"ZoneBand_{bandIndex}_{zone}", typeof(RectTransform), typeof(Image));
            var bandRect = (RectTransform)bandObject.transform;
            bandRect.SetParent(_trailContentRect, false);
            bandRect.anchorMin = new Vector2(0.5f, 1f);
            bandRect.anchorMax = new Vector2(0.5f, 1f);
            bandRect.pivot = new Vector2(0.5f, 1f);
            bandRect.sizeDelta = new Vector2(viewportWidth, bandHeight);
            bandRect.anchoredPosition = new Vector2(0f, topY);

            var bandImage = bandObject.GetComponent<Image>();
            bandImage.type = Image.Type.Simple;
            bandImage.raycastTarget = false;
            bandImage.color = ZoneColour(zone);

            switch (zone)
            {
                case LevelPathZoneKind.Winter:
                    CreateSceneryPiece(_winterMountainsSprite, viewportWidth, new Vector2(0f, topY - (bandHeight * 0.80f)));
                    CreateSceneryPiece(_winterPineTreeSprite, 0f, new Vector2(-160f, topY - (bandHeight * 0.32f)));
                    CreateSceneryPiece(_winterPineTreeSprite, 0f, new Vector2(160f, topY - (bandHeight * 0.58f)));
                    CreateSceneryPiece(_winterCloudSprite, 0f, new Vector2(-90f, topY - (bandHeight * 0.14f)));
                    break;
                case LevelPathZoneKind.City:
                    CreateSceneryPiece(_citySkylineSprite, viewportWidth, new Vector2(0f, topY - (bandHeight * 0.82f)));
                    CreateSceneryPiece(_cityStadiumSprite, 0f, new Vector2(130f, topY - (bandHeight * 0.40f)));
                    CreateSceneryPiece(_cityStreetlampSprite, 0f, new Vector2(-160f, topY - (bandHeight * 0.55f)));
                    break;
                case LevelPathZoneKind.Neighborhood:
                    CreateSceneryPiece(_neighborhoodBakerySprite, 0f, new Vector2(-150f, topY - (bandHeight * 0.35f)));
                    CreateSceneryPiece(_neighborhoodSchoolSprite, 0f, new Vector2(150f, topY - (bandHeight * 0.58f)));
                    CreateSceneryPiece(_neighborhoodTreeSprite, 0f, new Vector2(0f, topY - (bandHeight * 0.78f)));
                    break;
                default:
                    CreateSceneryPiece(_meadowHillsSprite, viewportWidth, new Vector2(0f, topY - (bandHeight * 0.78f)));
                    CreateSceneryPiece(_meadowRiverBridgeSprite, 0f, new Vector2(0f, topY - (bandHeight * 0.45f)));
                    CreateSceneryPiece(_meadowTreeSprite, 0f, new Vector2(-170f, topY - (bandHeight * 0.28f)));
                    CreateSceneryPiece(_meadowTreeSprite, 0f, new Vector2(170f, topY - (bandHeight * 0.62f)));
                    break;
            }
        }

        private static Color ZoneColour(LevelPathZoneKind zone)
        {
            switch (zone)
            {
                case LevelPathZoneKind.Winter:
                    return WinterZoneColour;
                case LevelPathZoneKind.City:
                    return CityZoneColour;
                case LevelPathZoneKind.Neighborhood:
                    return NeighborhoodZoneColour;
                default:
                    return MeadowZoneColour;
            }
        }

        /// <summary>
        /// One scenery sprite pinned at a fixed content-local position. <paramref name="stripWidth"/>
        /// greater than zero stretches the piece to that width, preserving the sprite's own aspect
        /// ratio — the wide strip backdrops (hills, mountains, skyline); zero sizes the piece from the
        /// sprite's own imported pixel dimensions — the single-object pieces (trees, stadium, lamp,
        /// buildings). A null sprite (scenery not yet assigned in the Inspector) is skipped rather than
        /// drawing an empty Image, so a missing asset degrades to "no decoration there" instead of a
        /// grey box.
        /// </summary>
        private void CreateSceneryPiece(Sprite sprite, float stripWidth, Vector2 anchoredPosition)
        {
            if (sprite == null)
            {
                return;
            }

            bool isStrip = stripWidth > 0f;
            Vector2 size = isStrip
                ? new Vector2(stripWidth, stripWidth * (sprite.rect.height / sprite.rect.width))
                : new Vector2(sprite.rect.width, sprite.rect.height);

            var pieceObject = new GameObject(sprite.name, typeof(RectTransform), typeof(Image));
            var pieceRect = (RectTransform)pieceObject.transform;
            pieceRect.SetParent(_trailContentRect, false);
            pieceRect.anchorMin = new Vector2(0.5f, 1f);
            pieceRect.anchorMax = new Vector2(0.5f, 1f);
            pieceRect.pivot = new Vector2(0.5f, 0.5f);
            pieceRect.sizeDelta = size;
            pieceRect.anchoredPosition = anchoredPosition;

            var pieceImage = pieceObject.GetComponent<Image>();
            pieceImage.type = Image.Type.Simple;
            pieceImage.preserveAspect = true;
            pieceImage.raycastTarget = false;
            pieceImage.sprite = sprite;
            pieceImage.color = Color.white;
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

            // The per-level objective glyph (issue #278) — the dominant visual on the plate, per the
            // approved "big icon, small number" balance. Sprite assigned once here from the catalog:
            // a level's objective cannot change at runtime, so there is nothing for Refresh to update
            // beyond the alpha it already repaints every node with.
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(nodeRect, false);
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(NODE_ICON_SIZE, NODE_ICON_SIZE);
            iconRect.anchoredPosition = new Vector2(0f, NODE_ICON_OFFSET_Y);
            var iconImage = iconObject.GetComponent<Image>();
            iconImage.type = Image.Type.Simple;
            iconImage.preserveAspect = true;
            iconImage.color = Color.clear;
            iconImage.raycastTarget = false;

            LevelObjectiveConfig config = _levelCatalog.Find(levelNumber);
            iconImage.sprite = config != null ? _objectiveIconCatalog.Find(config.ObjectiveType) : null;

            // Shrunk and moved to the foot of the plate now that the icon owns the centre — the number
            // is still legible, it is simply no longer the dominant glyph on the node.
            int numberFontSize = Mathf.Max(
                NODE_NUMBER_MIN_FONT_SIZE, Mathf.RoundToInt(_nodeFontSize * NODE_NUMBER_FONT_SCALE));
            Text numberText = UiTextFactory.Create(
                nodeRect, "Number", numberFontSize, FontStyle.Bold, Color.clear);
            ((RectTransform)numberText.transform).anchoredPosition = new Vector2(0f, NODE_NUMBER_OFFSET_Y);

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

            // The milestone reward badge (issue #278): only built for a level where
            // GrantsLevelUpReward is true — a handful out of the whole catalog — in the opposite
            // corner from the done dot so the two can never collide.
            Image rewardBadgeImage = null;
            Image rewardIconImage = null;
            if (config != null && config.GrantsLevelUpReward)
            {
                var badgeObject = new GameObject("RewardBadge", typeof(RectTransform), typeof(Image));
                var badgeRect = (RectTransform)badgeObject.transform;
                badgeRect.SetParent(nodeRect, false);
                Centre(badgeRect, new Vector2(REWARD_BADGE_SIZE, REWARD_BADGE_SIZE));
                badgeRect.anchoredPosition = new Vector2(
                    (NODE_SIZE * 0.5f) - 22f, (NODE_SIZE * 0.5f) - 22f);
                rewardBadgeImage = badgeObject.GetComponent<Image>();
                ConfigureCircle(rewardBadgeImage);

                // Mirrors ResolveIcon's PowerUp case in InfoPopupView: an authored power-up glyph is
                // rendered as-is, tinted plain white rather than the theme's ink.
                var rewardIconObject = new GameObject("RewardIcon", typeof(RectTransform), typeof(Image));
                var rewardIconRect = (RectTransform)rewardIconObject.transform;
                rewardIconRect.SetParent(badgeRect, false);
                Centre(rewardIconRect, new Vector2(REWARD_BADGE_ICON_SIZE, REWARD_BADGE_ICON_SIZE));
                rewardIconImage = rewardIconObject.GetComponent<Image>();
                rewardIconImage.type = Image.Type.Simple;
                rewardIconImage.preserveAspect = true;
                rewardIconImage.color = Color.clear;
                rewardIconImage.raycastTarget = false;
                rewardIconImage.sprite = _powerUpInventoryView.IconFor(config.LevelUpReward);
            }

            // The number never changes for a given widget, so it is written here rather than in
            // Refresh — one less string built per repaint, times the whole catalog.
            _stringBuilder.Clear();
            _stringBuilder.Append(levelNumber);
            numberText.text = _stringBuilder.ToString();

            return new LevelNode(
                nodeRect, plateImage, shadowImage, iconImage, dotImage, numberText, rewardBadgeImage, rewardIconImage);
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
