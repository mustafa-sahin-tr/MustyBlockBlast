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
    /// A node tap no longer starts that run directly. It opens <see cref="LevelStartCardView"/> for the
    /// tapped level — the level-start card showing what the level pays (issue #464) — and that card is
    /// what asks the System to start the run once the player commits. A locked node opens the same card
    /// as a read-only preview.
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
    /// Two more things are drawn onto this same built-once trail (issue #278): the level's power-up
    /// reward badges — one per kind a first clear pays, three on a milestone (issue #462, see
    /// <see cref="LevelCompletionRewards"/> and <see cref="RefreshNode"/>) — and a decorative
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

        /// <summary>Card top edge to the Path-mode running-total line — the band between the header
        /// and the top of the trail.</summary>
        private const float PATH_TOTAL_INSET = 152f;

        /// <summary>Smaller than the other labels on this line: the hint is a sentence rather than a
        /// value, and it shares its line with the running total.</summary>
        private const int TAP_HINT_FONT_SIZE = 28;

        /// <summary>Least room kept between the header counter and the right-aligned streak hint that
        /// shares its line (issue #464); the hint is truncated rather than allowed to close it.</summary>
        private const float STREAK_HINT_GAP = 32f;

        /// <summary>Separator between the streak's progress and the rule's threshold in the hint. A
        /// symbol, not a word, like <see cref="COUNTER_SEPARATOR"/>.</summary>
        private const string STREAK_PROGRESS_SEPARATOR = "/";

        // The scrolling window: everything between the running-total line and the description at the
        // foot of the card.
        private const float TRAIL_TOP_INSET = 194f;
        private const float TRAIL_BOTTOM_INSET = 150f;
        private const float TRAIL_SIDE_INSET = 44f;

        /// <summary>Card bottom edge to the current level's objective name — the bold upper of the two
        /// description lines.</summary>
        private const float DESCRIPTION_INSET = 118f;

        /// <summary>Card bottom edge to the smaller detail line under it.</summary>
        private const float DESCRIPTION_DETAIL_INSET = 66f;

        /// <summary>How much smaller the detail line is than the objective name above it.</summary>
        private const float DESCRIPTION_DETAIL_FONT_SCALE = 0.8f;

        /// <summary>
        /// Width each description line is truncated to, as a margin either side of the card. The two
        /// lines were one sentence that ran off the card (issue #416); split, each is short enough to
        /// fit, and this is the budget that guarantees it for a locale where one still is not.
        /// </summary>
        private const float DESCRIPTION_SIDE_PADDING = 70f;

        /// <summary>Tail of a truncated description line. Legacy uGUI <see cref="Text"/> has no
        /// ellipsis overflow mode, so the character is appended by hand.</summary>
        private const string ELLIPSIS = "…";

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

        /// <summary>The level's icon inside the bead (issue #464). Sized so the rounded-square icon's
        /// corners fall under <see cref="NODE_RIM"/>, which is what makes it read as a round window
        /// without a stencil mask per node.</summary>
        private const float NODE_ICON_SIZE = 92f;

        /// <summary>The bead's coloured rim over the icon: a ring from the bead's edge inwards.</summary>
        private const float NODE_RIM = (NODE_SIZE - NODE_ICON_SIZE) * 0.5f + 2f;

        /// <summary>The "12 · Daisy Hill" label under every node (issue #464): its gap below the bead's
        /// lip, height, side padding and widest allowed width (a longer name is truncated).</summary>
        private const float NODE_LABEL_GAP = 8f;
        private const float NODE_LABEL_HEIGHT = 40f;
        private const int NODE_LABEL_FONT_SIZE = 25;
        private const float NODE_LABEL_PADDING = 28f;
        private const float NODE_LABEL_MAX_WIDTH = 300f;
        private const float NODE_LABEL_BACKDROP_ALPHA = 0.9f;

        /// <summary>The big level number beside the bead (issue #464): its size, gap from the bead's edge
        /// and the width reserved for it, so "100" never reaches back onto the bead.</summary>
        private const int NODE_NUMBER_FONT_SIZE = 50;
        private const float NODE_NUMBER_GAP = 14f;
        private const float NODE_NUMBER_WIDTH = 110f;
        private static readonly Vector2 NodeNumberOutline = new Vector2(3f, -3f);

        /// <summary>The sheen is kept faint now that it sits over artwork rather than a flat plate.</summary>
        private const float NODE_ICON_SHEEN_ALPHA = 0.16f;
        private const float NODE_DOT_SIZE = 22f;

        /// <summary>Milestone level-up reward plate, mirroring the corner treatment of
        /// <see cref="NODE_DOT_SIZE"/>'s done dot but in the opposite corner so the two never collide.</summary>
        private const float REWARD_BADGE_SIZE = 44f;

        private const float REWARD_BADGE_ICON_SIZE = 28f;

        /// <summary>
        /// Where a node's reward badges sit, as angles (degrees, counter-clockwise from +X) around the
        /// node's centre at <see cref="REWARD_BADGE_RADIUS"/>. A single reward keeps the original
        /// top-right corner; a milestone's three fan across the top so they never cover the number or
        /// the bottom-right done dot (issue #462).
        /// </summary>
        private static readonly float[] SingleRewardBadgeAngles = { 45f };
        private static readonly float[] MilestoneRewardBadgeAngles = { 90f, 45f, 135f };

        /// <summary>Centre-to-badge distance: the diagonal of the original corner offset
        /// (<c>NODE_SIZE / 2 − 22</c> on each axis), so a single badge lands exactly where it always has.</summary>
        private const float REWARD_BADGE_RADIUS = ((NODE_SIZE * 0.5f) - 22f) * 1.41421356f;

        /// <summary>Scale applied to the node the player is on, so "you are here" reads without new art.</summary>
        private const float CURRENT_NODE_SCALE = 1.08f;

        /// <summary>
        /// Alpha applied to a locked node and to the stretch of trail leading to it. Deliberately the
        /// same dim as <see cref="PowerUpInventoryView"/>'s empty slot: "you cannot have this yet" is
        /// one visual idea and should look identical wherever it appears.
        /// </summary>
        private const float LOCKED_NODE_ALPHA = 0.35f;

        /// <summary>The 3D bead: how far the darker lip shows below the plate, how dark it is, how far
        /// the soft drop shadow falls, and how bright the top sheen is.</summary>
        private const float NODE_LIP_DEPTH = 10f;
        private const float NODE_LIP_SHADE = 0.3f;
        private const float NODE_SHADOW_DROP = 18f;
        private const float NODE_SHEEN_ALPHA = 0.38f;


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
        private LevelIdentityCatalog _levelIdentityCatalog;
        private RewardRuleModel _rewardRuleModel;
        private RewardRuleCatalog _rewardRuleCatalog;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private LevelProgressionSystem _levelProgressionSystem;
        private GameModeSystem _gameModeSystem;
        private TimerRunSystem _timerRunSystem;

        /// <summary>Source of a power-up's glyph for a milestone node's reward badge — the same
        /// authored art <see cref="InfoPopupView"/> borrows for its own PowerUp subject.</summary>
        private PowerUpInventoryView _powerUpInventoryView;

        /// <summary>The level-start screen a node tap opens. A View dependency rather than a System one
        /// because the insertion is entirely presentational: what a node tap does now is show another
        /// card, which is what eventually starts the run.</summary>
        private LevelStartCardView _levelStartCardView;

        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;

        private ScrollRect _trailScroll;
        private Image _trailBackdropImage;
        private RectTransform _trailViewportRect;
        private RectTransform _trailContentRect;

        private Text _headerText;

        /// <summary>The bold upper description line: the current level's objective name.</summary>
        private Text _descriptionText;

        /// <summary>The smaller lower description line: the objective's sentence and its target count.</summary>
        private Text _descriptionDetailText;

        private Text _pathTotalText;
        private Text _tapHintText;

        /// <summary>Path-mode progress towards the next first-try streak bonus (issue #464), right-aligned
        /// on the header line.</summary>
        private Text _streakHintText;

        /// <summary>The floating circular close button, borrowed whole from the info card family so
        /// this overlay's close reads exactly like theirs (issue #416).</summary>
        private InfoCardChrome.CloseButtonHandles _closeButton;

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
                Image lipImage,
                Image highlightImage,
                Image doneDot,
                Text numberText,
                Image[] rewardBadgeImages,
                Image[] rewardIconImages,
                Image iconImage,
                Image rimImage,
                Image labelBackdrop,
                Text sideNumberText,
                Outline sideNumberOutline)
            {
                SideNumberText = sideNumberText;
                SideNumberOutline = sideNumberOutline;
                IconImage = iconImage;
                RimImage = rimImage;
                LabelBackdrop = labelBackdrop;
                Root = root;
                PlateImage = plateImage;
                ShadowImage = shadowImage;
                LipImage = lipImage;
                HighlightImage = highlightImage;
                DoneDot = doneDot;
                NumberText = numberText;
                RewardBadgeImages = rewardBadgeImages;
                RewardIconImages = rewardIconImages;
            }

            internal RectTransform Root { get; }

            internal Image PlateImage { get; }

            internal Image ShadowImage { get; }

            /// <summary>The bead's darker underside, peeking out below the plate.</summary>
            internal Image LipImage { get; }

            /// <summary>The glossy sheen across the top of the plate.</summary>
            internal Image HighlightImage { get; }

            internal Image DoneDot { get; }

            /// <summary>The level's name label under the bead (issue #464).</summary>
            internal Text NumberText { get; }

            /// <summary>The big level number beside the bead (issue #464), so "which level is this"
            /// reads at a glance now that the icon fills the bead.</summary>
            internal Text SideNumberText { get; }

            /// <summary>The card-coloured outline that keeps <see cref="SideNumberText"/> legible.</summary>
            internal Outline SideNumberOutline { get; }

            /// <summary>The level's icon inside the bead (issue #464).</summary>
            internal Image IconImage { get; }

            /// <summary>The coloured ring over the icon's corners — the bead's colour now that the icon
            /// covers its middle.</summary>
            internal Image RimImage { get; }

            /// <summary>The soft pill behind <see cref="NumberText"/>, so it reads over the trail.</summary>
            internal Image LabelBackdrop { get; }

            /// <summary>One plate per power-up a first clear of this level pays (issue #462): one for a
            /// regular level, three for a milestone, none for the last level (which advances nowhere, so
            /// pays nothing).</summary>
            internal Image[] RewardBadgeImages { get; }

            /// <summary>Each rewarded <see cref="PowerUpKind"/>'s glyph, parallel to
            /// <see cref="RewardBadgeImages"/>.</summary>
            internal Image[] RewardIconImages { get; }
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
            LevelIdentityCatalog levelIdentityCatalog,
            RewardRuleModel rewardRuleModel,
            RewardRuleCatalog rewardRuleCatalog,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            LevelProgressionSystem levelProgressionSystem,
            GameModeSystem gameModeSystem,
            TimerRunSystem timerRunSystem,
            LevelStartCardView levelStartCardView,
            PowerUpInventoryView powerUpInventoryView)
        {
            _levelStartCardView = levelStartCardView;
            _levelProgressionModel = levelProgressionModel;
            _pathRunModel = pathRunModel;
            _levelCatalog = levelCatalog;
            _levelIdentityCatalog = levelIdentityCatalog;
            _rewardRuleModel = rewardRuleModel;
            _rewardRuleCatalog = rewardRuleCatalog;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _levelProgressionSystem = levelProgressionSystem;
            _gameModeSystem = gameModeSystem;
            _timerRunSystem = timerRunSystem;
            _powerUpInventoryView = powerUpInventoryView;
        }

        private void Start()
        {
            if (_levelProgressionModel == null || _pathRunModel == null || _levelCatalog == null
                || _levelIdentityCatalog == null
                || _rewardRuleModel == null || _rewardRuleCatalog == null || _settingsModel == null || _localizationModel == null || _localizationSystem == null
                || _levelProgressionSystem == null || _gameModeSystem == null || _timerRunSystem == null
                || _levelStartCardView == null || _powerUpInventoryView == null)
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
            _rewardRuleModel.FirstTryStreak.Subscribe(OnFirstTryStreakChanged).AddTo(_disposables);
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
        /// resolves to the foot of the walk.
        /// </para>
        /// <para>
        /// The content is pinned by its bottom edge (issue #416), so a node's own offset is measured up
        /// from the foot of the walk and the scroll that centres it runs from zero — level 1, the
        /// ground — down to minus the scrollable span, which is the sky at the far end.
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
            float nodeHeight = ContentY(levelIndex);

            float scroll = Mathf.Clamp((viewportHeight * 0.5f) - nodeHeight, -scrollableHeight, 0f);
            _trailContentRect.anchoredPosition = new Vector2(0f, scroll);
        }

        /// <summary>
        /// Hands this node's level to <see cref="LevelStartCardView"/>, which shows what the level pays
        /// and is what goes on to ask <see cref="LevelProgressionSystem.TryStartPathLevel"/> once the
        /// player commits (issues #167, #464).
        /// <para>
        /// A locked node opens the same card as a read-only preview of its rewards — no Start, no Watch
        /// Ad (issue #464). Only a node the catalog does not author does nothing.
        /// </para>
        /// <para>
        /// This card is closed before the level-start card opens because the two are mutually exclusive
        /// overlays and both hold the countdown through the same single <see cref="TimerRunSystem"/>
        /// flag — see the gate chain in <see cref="BoardInputView"/>.
        /// </para>
        /// </summary>
        private void OnNodeClicked(int levelNumber)
        {
            if (_levelCatalog.Find(levelNumber) == null)
            {
                return;
            }

            Close();
            _levelStartCardView.Open(levelNumber);
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

        private void OnFirstTryStreakChanged(int streak) => Refresh();

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

            _closeButton.PlateImage.color = _currentTheme.CardBackground;
            _closeButton.PlateShadowImage.color = _currentTheme.CardShadow;

            RefreshDescription(currentLevel);

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

            RefreshStreakHint(isPathMode);

            RefreshTrailPieces(_ribbonPieces, _currentTheme.TrailPathColor);
            RefreshTrailPieces(_accentPieces, _currentTheme.TrailWeatherAccentColor);

            for (int nodeIndex = 0; nodeIndex < _nodes.Length; nodeIndex++)
            {
                RefreshNode(_nodes[nodeIndex], nodeIndex + 1, currentLevel);
            }
        }

        /// <summary>
        /// The "next reward" hint (issue #464): how far the first-try streak is into the cycle of the
        /// rule closest to paying, and what that rule pays — e.g. "First-try streak 2/3 · +1". Read
        /// through <see cref="RewardRules.TryGetNextPayout"/>, the same rule the payout uses, so the hint
        /// cannot promise something the system will not pay. Path mode only, and hidden when no rule is
        /// authored.
        /// </summary>
        private void RefreshStreakHint(bool isPathMode)
        {
            bool hasRule = RewardRules.TryGetNextPayout(
                _rewardRuleCatalog.Rules, RewardRuleCondition.ConsecutiveFirstTryClears,
                _rewardRuleModel.FirstTryStreak.Value, out RewardRuleConfig nextRule, out int progress);
            bool show = isPathMode && hasRule;
            _streakHintText.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            _streakHintText.color = _currentTheme.Accent;

            _stringBuilder.Clear();
            _stringBuilder.Append(progress);
            _stringBuilder.Append(STREAK_PROGRESS_SEPARATOR);
            _stringBuilder.Append(nextRule.Threshold);
            string progressText = _stringBuilder.ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append(nextRule.RewardCount);
            string hint = _localizationSystem.Format(
                LocalizationKeys.LEVEL_PATH_STREAK_HINT, progressText, _stringBuilder.ToString());

            float maxWidth = _cardSize.x - (SIDE_INSET * 2f) - _headerText.preferredWidth - STREAK_HINT_GAP;
            SetTruncated(_streakHintText, hint, maxWidth);
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
        /// The reward badges dim by the exact same alpha as the plate around it — a locked node has to
        /// read as one locked thing, not as bright badges sitting on a dimmed plate. A cleared node's
        /// badges are hidden: the reward is paid on the first clear only, so a replay has nothing to
        /// offer and the node must not promise it (issue #462).
        /// </para>
        /// <para>
        /// A node's fill cycles through the game's five piece kinds by level number (issue #416), so
        /// the walk reads as a string of coloured beads rather than as one repeated plate. The palette
        /// is the theme's own <see cref="ThemeDefinition.GetFill"/> — the very colours the blocks and
        /// the Path pill's disc are painted with — so a season switch recolours the trail with
        /// everything else instead of needing a palette of its own.
        /// </para>
        /// </summary>
        private void RefreshNode(LevelNode node, int levelNumber, int currentLevel)
        {
            bool isCurrent = levelNumber == currentLevel;
            bool isLocked = !_levelProgressionSystem.IsUnlocked(levelNumber);

            // The current node still inverts to the accent — the same way PowerUpInventoryView marks
            // the armed slot — which is what keeps "you are here" legible against the cycling colours.
            Color plateColour = isCurrent
                ? _currentTheme.Accent
                : _currentTheme.GetFill(KindOf(levelNumber));

            // A locked node is faded toward the card rather than made translucent: the bead is stacked
            // layers (shadow, lip, plate, sheen), and translucent layers would show through each other.
            node.PlateImage.color = FadeIfLocked(plateColour, isLocked);
            node.LipImage.color = FadeIfLocked(Darken(plateColour, NODE_LIP_SHADE), isLocked);
            node.ShadowImage.color = WithAlpha(_currentTheme.CardShadow, isLocked ? LOCKED_NODE_ALPHA : 1f);
            node.HighlightImage.color = WithAlpha(
                Color.white, isLocked ? NODE_ICON_SHEEN_ALPHA * LOCKED_NODE_ALPHA : NODE_ICON_SHEEN_ALPHA);
            node.RimImage.color = FadeIfLocked(plateColour, isLocked);
            node.IconImage.color = FadeIfLocked(Color.white, isLocked);
            RefreshNodeLabel(node, levelNumber, isLocked);
            node.SideNumberText.color = FadeIfLocked(isCurrent ? _currentTheme.Accent : _currentTheme.Ink, isLocked);
            node.SideNumberOutline.effectColor = WithAlpha(
                _currentTheme.CardBackground, isLocked ? LOCKED_NODE_ALPHA : 1f);

            bool isCleared = levelNumber < currentLevel;
            // On the current node the plate is itself accent-filled, so the badges take the plate's
            // own darker lip shade there — otherwise they would melt into the plate they sit on.
            Color badgeFill = isCurrent ? Darken(_currentTheme.Accent, NODE_LIP_SHADE) : _currentTheme.Accent;
            Color badgeColour = isCleared ? Color.clear : FadeIfLocked(badgeFill, isLocked);
            Color rewardIconColour = isCleared
                ? Color.clear
                : WithAlpha(Color.white, isLocked ? LOCKED_NODE_ALPHA : 1f);
            for (int rewardIndex = 0; rewardIndex < node.RewardBadgeImages.Length; rewardIndex++)
            {
                node.RewardBadgeImages[rewardIndex].color = badgeColour;
                node.RewardIconImages[rewardIndex].color = rewardIconColour;
            }

            // Only a cleared node carries the accent dot: the current node is already accent-filled,
            // and a locked one has nothing to mark.
            node.DoneDot.color = !isCurrent && !isLocked ? _currentTheme.Accent : Color.clear;

            float scale = isCurrent ? CURRENT_NODE_SCALE : 1f;
            node.Root.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// The name label under a node (issue #464): the level's localized name on a soft card-coloured
        /// pill, truncated to <see cref="NODE_LABEL_MAX_WIDTH"/>. Rewritten
        /// on every repaint so a language switch renames the whole walk.
        /// </summary>
        private void RefreshNodeLabel(LevelNode node, int levelNumber, bool isLocked)
        {
            LevelIdentityConfig identity = _levelIdentityCatalog.Find(levelNumber);
            string levelName = identity != null && !string.IsNullOrEmpty(identity.NameKey)
                ? _localizationSystem.Translate(identity.NameKey)
                : string.Empty;
            node.NumberText.gameObject.SetActive(levelName.Length > 0);
            node.LabelBackdrop.gameObject.SetActive(levelName.Length > 0);
            SetTruncated(node.NumberText, levelName, NODE_LABEL_MAX_WIDTH - NODE_LABEL_PADDING);
            node.NumberText.color = FadeIfLocked(_currentTheme.Ink, isLocked);

            float width = node.NumberText.preferredWidth + NODE_LABEL_PADDING;
            node.LabelBackdrop.rectTransform.sizeDelta = new Vector2(width, NODE_LABEL_HEIGHT);
            node.LabelBackdrop.color = WithAlpha(
                _currentTheme.CardBackground, NODE_LABEL_BACKDROP_ALPHA * (isLocked ? LOCKED_NODE_ALPHA + 0.3f : 1f));
        }

        /// <summary>
        /// The colour id a level's node plate takes: the five piece kinds, cycled by level number.
        /// Kinds are 1-based, hence the shift either side of the modulo.
        /// </summary>
        private static int KindOf(int levelNumber)
            => ((Mathf.Max(1, levelNumber) - 1) % ThemeDefinition.KIND_COUNT) + 1;

        /// <summary>
        /// The current level's goal on the two lines at the foot of the card: its objective's short
        /// name in bold, then the sentence and target beneath. Worded by the same formatter the
        /// objective HUD uses, so the card and the HUD can never describe the same level differently.
        /// An unauthored or invalid entry shows nothing rather than throwing out of
        /// <c>ToObjectiveDefinition</c>.
        /// </summary>
        private void RefreshDescription(int levelNumber)
        {
            string headline = string.Empty;
            string detail = string.Empty;

            LevelObjectiveConfig config = _levelCatalog.Find(levelNumber);
            if (config != null && config.IsValid(out _))
            {
                ObjectiveDefinition definition = config.ToObjectiveDefinition();
                ObjectiveDescriptionFormatter.DescribeSplit(
                    definition, _localizationSystem, _currentTheme, out headline, out detail);
            }

            float maxWidth = _cardSize.x - (DESCRIPTION_SIDE_PADDING * 2f);

            _descriptionText.color = _currentTheme.Ink;
            SetTruncated(_descriptionText, headline, maxWidth);

            _descriptionDetailText.color = _currentTheme.SoftInk;
            SetTruncated(_descriptionDetailText, detail, maxWidth);
        }

        /// <summary>
        /// Writes <paramref name="value"/> into <paramref name="label"/>, cut short with an ellipsis if
        /// it measures wider than <paramref name="maxWidth"/>. Binary-searched on the label's own
        /// measurement rather than on a character budget, because the built-in font is proportional and
        /// a budget that fits Turkish would still overflow in a wider locale.
        /// <para>
        /// A string carrying rich text — the objective colour swatch's <c>&lt;color&gt;</c> tag — is
        /// left whole: cutting one mid-tag would print the markup. Those fall back to the label's own
        /// wrapping, which is why both description labels keep <see cref="HorizontalWrapMode.Wrap"/>.
        /// </para>
        /// </summary>
        private static void SetTruncated(Text label, string value, float maxWidth)
        {
            label.text = value;

            if (string.IsNullOrEmpty(value) || value.IndexOf('<') >= 0 || label.preferredWidth <= maxWidth)
            {
                return;
            }

            int fits = 0;
            int tooLong = value.Length;
            while (fits < tooLong)
            {
                int candidate = (fits + tooLong + 1) / 2;
                label.text = value.Substring(0, candidate) + ELLIPSIS;
                if (label.preferredWidth <= maxWidth)
                {
                    fits = candidate;
                }
                else
                {
                    tooLong = candidate - 1;
                }
            }

            label.text = value.Substring(0, fits) + ELLIPSIS;
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

        /// <summary>What <paramref name="colour"/> at <see cref="LOCKED_NODE_ALPHA"/> would look like
        /// over the card, but opaque — see <see cref="RefreshNode"/>.</summary>
        private Color FadeIfLocked(Color colour, bool isLocked)
        {
            if (!isLocked)
            {
                return colour;
            }

            Color faded = Color.Lerp(_currentTheme.CardBackground, colour, LOCKED_NODE_ALPHA);
            faded.a = 1f;
            return faded;
        }

        private static Color Darken(Color colour, float amount)
            => new Color(colour.r * (1f - amount), colour.g * (1f - amount), colour.b * (1f - amount), colour.a);

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

            // The header line's right half is otherwise empty; the band under it already holds the
            // running total and the tap hint.
            _streakHintText = CreateLabel(
                _cardRect, "StreakHint", TAP_HINT_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(cardHalfWidth - SIDE_INSET, headerY));

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

            _descriptionText = CreateDescriptionLabel(
                "Description", _descriptionFontSize, FontStyle.Bold, -cardHalfHeight + DESCRIPTION_INSET);

            int detailFontSize = Mathf.RoundToInt(_descriptionFontSize * DESCRIPTION_DETAIL_FONT_SCALE);
            _descriptionDetailText = CreateDescriptionLabel(
                "DescriptionDetail", detailFontSize, FontStyle.Normal,
                -cardHalfHeight + DESCRIPTION_DETAIL_INSET);

            // Last, so the plate that floats past the card's corner is drawn over the trail rather than
            // under it, whatever the scroll band's own sibling order.
            BuildCloseButton(new Vector2(cardHalfWidth, cardHalfHeight));

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

            // Anchored and pivoted at the bottom (issue #416): level 1 sits on the content's bottom
            // edge and the walk climbs from there, so scrolling reads as walking up out of the ground
            // towards the sky rather than as running a list downwards.
            _trailContentRect.anchorMin = new Vector2(0.5f, 0f);
            _trailContentRect.anchorMax = new Vector2(0.5f, 0f);
            _trailContentRect.pivot = new Vector2(0.5f, 0f);
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
                // the next, so neither node reads as belonging to the "wrong" band's colour. The first
                // and last instead run to the content edges, so there is no gap of bare backdrop below
                // level 1 or above the last level. Heights are measured up from the content's bottom,
                // where the walk now starts.
                float bottomY = bandIndex == 0
                    ? 0f
                    : (ContentY(startIndex - 1) + ContentY(startIndex)) * 0.5f;
                float topY = endIndexExclusive >= levelCount
                    ? contentHeight
                    : (ContentY(endIndexExclusive - 1) + ContentY(endIndexExclusive)) * 0.5f;

                BuildZoneBand(bandIndex, zone, viewportWidth, topY, bottomY);
            }
        }

        /// <summary>How far up the scroll content a node's row sits, measured from the content's bottom
        /// edge and independent of sweep amplitude — the same value <see cref="AnchorToContent"/>
        /// offsets a node to, so a scenery band lines up exactly with the nodes it surrounds.</summary>
        private static float ContentY(int levelIndex)
            => TRAIL_VERTICAL_PADDING - LevelPathTrailLayout.WaypointOf(levelIndex, 0f).y;

        /// <summary>One zone's flat backdrop band plus its scattered scenery, all parented to the
        /// scroll content so they pan with it.</summary>
        private void BuildZoneBand(int bandIndex, LevelPathZoneKind zone, float viewportWidth, float topY, float bottomY)
        {
            float bandHeight = topY - bottomY;

            var bandObject = new GameObject($"ZoneBand_{bandIndex}_{zone}", typeof(RectTransform), typeof(Image));
            var bandRect = (RectTransform)bandObject.transform;
            bandRect.SetParent(_trailContentRect, false);
            bandRect.anchorMin = new Vector2(0.5f, 0f);
            bandRect.anchorMax = new Vector2(0.5f, 0f);
            bandRect.pivot = new Vector2(0.5f, 0f);
            bandRect.sizeDelta = new Vector2(viewportWidth, bandHeight);
            bandRect.anchoredPosition = new Vector2(0f, bottomY);

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
            pieceRect.anchorMin = new Vector2(0.5f, 0f);
            pieceRect.anchorMax = new Vector2(0.5f, 0f);
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
                // Negated y: the layout's walk descends while the content is pinned bottom-up
                // (see AnchorToContent), so a bar must lean the way it will actually be drawn.
                float angle = Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg;

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
            AnchorToContent(pieceRect, size, waypoint);
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
            AnchorToContent(nodeRect, new Vector2(NODE_SIZE, NODE_SIZE), waypoint);

            // Closes over the level rather than deriving it from an index, because unlike the old paged
            // grid this widget is this level for the panel's whole life.
            nodeObject.GetComponent<LevelPathNodeButton>().SetClicked(() => OnNodeClicked(levelNumber));

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(nodeRect, false);
            Centre(shadowRect, new Vector2(NODE_SIZE + 6f, NODE_SIZE + 6f));
            shadowRect.anchoredPosition = new Vector2(0f, -NODE_SHADOW_DROP);
            var shadowImage = shadowObject.GetComponent<Image>();
            ConfigureCircle(shadowImage);

            // The bead's depth: a darker copy of the plate dropped below it, so the node reads as a
            // pressable button with a side rather than a flat disc (the shop's chunky-button look).
            var lipObject = new GameObject("Lip", typeof(RectTransform), typeof(Image));
            var lipRect = (RectTransform)lipObject.transform;
            lipRect.SetParent(nodeRect, false);
            Centre(lipRect, new Vector2(NODE_SIZE, NODE_SIZE));
            lipRect.anchoredPosition = new Vector2(0f, -NODE_LIP_DEPTH);
            var lipImage = lipObject.GetComponent<Image>();
            ConfigureCircle(lipImage);

            // Round rather than round-cornered (issue #416): a node is a bead on the trail, and the
            // circle is what stops the walk reading as a column of tiles.
            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(nodeRect, false);
            Centre(plateRect, new Vector2(NODE_SIZE, NODE_SIZE));
            var plateImage = plateObject.GetComponent<Image>();
            ConfigureCircle(plateImage);

            // The plate is the node's hit area: the node root carries the click handler but no graphic
            // of its own, and uGUI dispatches a click up the hierarchy from whatever graphic it hit.
            plateImage.raycastTarget = true;

            // The level's own icon fills the bead (issue #464), under a rim in the bead's colour that
            // hides the icon's square corners — the node still reads as a coloured bead, now with the
            // level's picture in it.
            LevelIdentityConfig identity = _levelIdentityCatalog.Find(levelNumber);
            Image iconImage = HudChrome.BuildGlyph(
                nodeRect, "Icon", identity != null ? identity.Icon : null,
                new Vector2(NODE_ICON_SIZE, NODE_ICON_SIZE), Vector2.zero);
            iconImage.enabled = iconImage.sprite != null;
            Image rimImage = HudChrome.BuildOutline(
                nodeRect, "Rim", new Vector2(NODE_SIZE, NODE_SIZE), Vector2.zero, NODE_SIZE * 0.5f, NODE_RIM);

            Image highlightImage = HudChrome.BuildRounded(
                nodeRect, "Sheen", new Vector2(NODE_SIZE * 0.56f, NODE_SIZE * 0.2f),
                new Vector2(0f, NODE_SIZE * 0.27f), NODE_SIZE * 0.1f);

            LevelObjectiveConfig config = _levelCatalog.Find(levelNumber);

            // The number moved off the bead when the icon moved in (issue #464): it heads the name
            // label under the node instead, "12 · Daisy Hill", on a soft pill so it reads over the
            // ribbon and the scenery. RefreshNodeLabel writes and sizes it.
            float labelY = (-NODE_SIZE * 0.5f) - NODE_LIP_DEPTH - NODE_LABEL_GAP - (NODE_LABEL_HEIGHT * 0.5f);
            Image labelBackdrop = HudChrome.BuildRounded(
                nodeRect, "LabelBackdrop", new Vector2(NODE_LABEL_HEIGHT, NODE_LABEL_HEIGHT), new Vector2(0f, labelY),
                NODE_LABEL_HEIGHT * 0.5f);
            Text numberText = UiTextFactory.Create(
                nodeRect, "Label", NODE_LABEL_FONT_SIZE, FontStyle.Bold, Color.clear);
            numberText.rectTransform.anchoredPosition = new Vector2(0f, labelY);

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

            // The reward badges (issue #278, every level since #462): one per power-up a first clear
            // pays, straight from the same rule LevelProgressionSystem grants by, so the preview is the
            // payout. The last level advances nowhere and so pays nothing — it gets none.
            Image[] rewardBadgeImages = System.Array.Empty<Image>();
            Image[] rewardIconImages = System.Array.Empty<Image>();
            if (_levelCatalog.Find(levelNumber + 1) != null)
            {
                IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(levelNumber);
                float[] angles = rewards.Count > 1 ? MilestoneRewardBadgeAngles : SingleRewardBadgeAngles;
                int badgeCount = Mathf.Min(rewards.Count, angles.Length);
                rewardBadgeImages = new Image[badgeCount];
                rewardIconImages = new Image[badgeCount];
                for (int rewardIndex = 0; rewardIndex < badgeCount; rewardIndex++)
                {
                    float radians = angles[rewardIndex] * Mathf.Deg2Rad;
                    Vector2 badgePosition = new Vector2(
                        Mathf.Cos(radians) * REWARD_BADGE_RADIUS, Mathf.Sin(radians) * REWARD_BADGE_RADIUS);
                    BuildRewardBadge(
                        nodeRect, rewards[rewardIndex], badgePosition,
                        out rewardBadgeImages[rewardIndex], out rewardIconImages[rewardIndex]);
                }
            }

            // The big number beside the bead, on the side facing the middle of the trail so it never
            // runs off the card at the far end of a sweep. A card-coloured outline keeps it legible over
            // the ribbon and the scenery.
            float side = waypoint.x > 0.5f ? -1f : 1f;
            Text sideNumberText = UiTextFactory.Create(
                nodeRect, "Number", NODE_NUMBER_FONT_SIZE, FontStyle.Bold, Color.clear);
            sideNumberText.alignment = side > 0f ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            RectTransform sideNumberRect = sideNumberText.rectTransform;
            sideNumberRect.pivot = new Vector2(side > 0f ? 0f : 1f, 0.5f);
            sideNumberRect.sizeDelta = new Vector2(NODE_NUMBER_WIDTH, NODE_NUMBER_FONT_SIZE * 1.4f);
            sideNumberRect.anchoredPosition = new Vector2(side * ((NODE_SIZE * 0.5f) + NODE_NUMBER_GAP), 0f);
            var sideNumberOutline = sideNumberText.gameObject.AddComponent<Outline>();
            sideNumberOutline.effectDistance = NodeNumberOutline;
            _stringBuilder.Clear();
            _stringBuilder.Append(levelNumber);
            sideNumberText.text = _stringBuilder.ToString();

            return new LevelNode(
                nodeRect, plateImage, shadowImage, lipImage, highlightImage, dotImage, numberText,
                rewardBadgeImages, rewardIconImages, iconImage, rimImage, labelBackdrop, sideNumberText, sideNumberOutline);
        }

        /// <summary>One reward badge: an accent disc carrying <paramref name="kind"/>'s strip glyph.
        /// Mirrors ResolveIcon's PowerUp case in InfoPopupView: an authored power-up glyph is rendered
        /// as-is, tinted plain white rather than the theme's ink.</summary>
        private void BuildRewardBadge(
            RectTransform nodeRect, PowerUpKind kind, Vector2 position, out Image badgeImage, out Image iconImage)
        {
            var badgeObject = new GameObject("RewardBadge", typeof(RectTransform), typeof(Image));
            var badgeRect = (RectTransform)badgeObject.transform;
            badgeRect.SetParent(nodeRect, false);
            Centre(badgeRect, new Vector2(REWARD_BADGE_SIZE, REWARD_BADGE_SIZE));
            badgeRect.anchoredPosition = position;
            badgeImage = badgeObject.GetComponent<Image>();
            ConfigureCircle(badgeImage);

            var rewardIconObject = new GameObject("RewardIcon", typeof(RectTransform), typeof(Image));
            var rewardIconRect = (RectTransform)rewardIconObject.transform;
            rewardIconRect.SetParent(badgeRect, false);
            Centre(rewardIconRect, new Vector2(REWARD_BADGE_ICON_SIZE, REWARD_BADGE_ICON_SIZE));
            iconImage = rewardIconObject.GetComponent<Image>();
            iconImage.type = Image.Type.Simple;
            iconImage.preserveAspect = true;
            iconImage.color = Color.clear;
            iconImage.raycastTarget = false;
            iconImage.sprite = _powerUpInventoryView.IconFor(kind);
        }

        /// <summary>
        /// The floating circular close button, taken whole from <see cref="InfoCardChrome"/> — the same
        /// plate, shadow and × the power-up, cell and objective info cards close with (issue #416),
        /// rather than a square one of this card's own that could drift from theirs.
        /// <para>
        /// The one thing this card adds is the tap: its chrome is drawn for the info cards, which are
        /// hit-tested by <see cref="BoardInputView"/>, while this overlay runs on the EventSystem — so
        /// the hit rect's graphic is made a raycast target here and given the same
        /// <see cref="LevelPathNodeButton"/> every other tappable thing on this card carries.
        /// </para>
        /// </summary>
        private void BuildCloseButton(Vector2 cardCorner)
        {
            _closeButton = InfoCardChrome.CreateFloatingCloseButton(_cardRect);
            InfoCardChrome.PositionFloatingCloseButton(_closeButton, cardCorner);

            _closeButton.HitImage.enabled = true;
            _closeButton.HitImage.raycastTarget = true;
            _closeButton.HitRect.gameObject.AddComponent<LevelPathNodeButton>().SetClicked(Close);

            for (int barIndex = 0; barIndex < _closeButton.BarImages.Count; barIndex++)
            {
                _inkImages.Add(_closeButton.BarImages[barIndex]);
            }
        }

        /// <summary>One of the two centred lines at the foot of the card. Wrapping is the fallback for
        /// the rich-text strings <see cref="SetTruncated"/> refuses to cut, so the rect is given the
        /// same width that truncation measures against.</summary>
        private Text CreateDescriptionLabel(string objectName, int fontSize, FontStyle fontStyle, float y)
        {
            Text label = CreateLabel(
                _cardRect, objectName, fontSize, fontStyle, TextAnchor.MiddleCenter, new Vector2(0f, y));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;

            var rect = (RectTransform)label.transform;
            rect.sizeDelta = new Vector2(_cardSize.x - (DESCRIPTION_SIDE_PADDING * 2f), fontSize * 1.6f);
            return label;
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
        /// Pins a trail widget to the bottom-centre of the scroll content, where the walk now starts,
        /// offset up by the content's own padding and by how far along the walk its waypoint is —
        /// <see cref="LevelPathTrailLayout"/>'s y descends, so it is negated here (issue #416).
        /// Anchoring to an edge rather than the centre is what keeps a waypoint's position independent
        /// of how tall the content happens to be.
        /// </summary>
        private static void AnchorToContent(RectTransform rect, Vector2 size, Vector2 waypoint)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(waypoint.x, TRAIL_VERTICAL_PADDING - waypoint.y);
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
