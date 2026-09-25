using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
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
    /// Renders the 8x8 board as a grid of rounded cells inside a soft card. Subscribes to
    /// <see cref="BoardModel"/>; contains no game logic and never mutates the model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardView : MonoBehaviour
    {
        /// <summary>Corner radius of the board card: the mockup's 24px at the canvas's scale.</summary>
        private const float CARD_CORNER_RADIUS = 66f;

        /// <summary>Corner radius of the sunken well inside the card: the mockup's 16px.</summary>
        private const float WELL_CORNER_RADIUS = 44f;

        /// <summary>The card's drop: the mockup's 8px, the deepest on the HUD — the board is the one
        /// plate everything else is arranged around.</summary>
        private const float CARD_SHADOW_DROP = 16f;

        /// <summary>How much of <see cref="_cardPadding"/> is card rim, outside the well; the rest is
        /// the well's own padding around the grid.</summary>
        private const float WELL_RIM = 12f;

        /// <summary>Which theme kind the Ghost Fit silhouette borrows: the second kind, every season's
        /// cool blue-green — the mockup's blue ghost, told apart from the green valid preview by hue
        /// rather than by a role no theme authors.</summary>
        private const int GHOST_KIND = 2;

        /// <summary>Alpha of the ghost silhouette's fill at full pulse: the mockup's 0.22.</summary>
        private const float GHOST_FILL_ALPHA = 0.3f;

        [Header("Layout")]
        [Tooltip("Centre of the board card, in reference pixels from the canvas centre.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 10f);
        [SerializeField] private float _cellSize = 100f;
        [SerializeField] private float _cellSpacing = 8f;

        [Tooltip("Distance from the grid's edge to the card's edge: the card rim plus the well's padding.")]
        [SerializeField] private float _cardPadding = 22f;

        [Header("Cell style")]
        [SerializeField] private float _cellInset = 3f;
        [SerializeField] private float _cellBevelThickness = 11f;

        [Header("Drag Preview")]
        [Tooltip("Fraction of a cell's size added as a margin around the grid rect where the placement preview starts appearing, ahead of the pointer strictly entering the grid.")]
        [SerializeField] private float _previewLeadMarginFraction = 0.5f;

        [Header("Line Clear Fade")]
        [Tooltip("Seconds a cleared cell takes to fade from its colour to fully transparent.")]
        [SerializeField] private float _fadeDuration = 0.2f;

        [Tooltip("Seconds of white flash on a cell that sits on both a cleared row and a cleared column.")]
        [SerializeField] private float _intersectionFlashDuration = 0.08f;

        [Tooltip("Total seconds spread across a single cleared line's cells when it is the only line cleared (issue #328) — cell 0 starts immediately, the line's last cell starts this many seconds later. Two or more simultaneous line clears never stagger, but are floored to last at least as long overall (issue #350).")]
        [SerializeField] private float _singleLineStaggerDuration = 0.15f;

        [Header("Single Line Clear Effect Variety")]
        [Tooltip("Side of one shatter shard / ember particle, as a fraction of the cell size (issue #331).")]
        [SerializeField] private float _shatterShardSizeFraction = 0.22f;

        [Tooltip("How far a shatter shard flies from the cell's centre over the fade, as a fraction of the cell size.")]
        [SerializeField] private float _shatterFlyDistanceFraction = 0.9f;

        [Tooltip("Side of one burn ember particle, as a fraction of the cell size.")]
        [SerializeField] private float _emberSizeFraction = 0.16f;

        [Tooltip("How far a burn ember drifts upward over the fade, as a fraction of the cell size.")]
        [SerializeField] private float _emberDriftFraction = 0.6f;

        [Tooltip("Smallest scale a fly-to-corner cell shrinks to just before it finishes fading.")]
        [SerializeField] private float _flyToCornerMinScale = 0.15f;

        [Header("Cross-Clear Combo (issue #330)")]
        [Tooltip("Seconds the additional combo flash a cross-clear (exactly one row and one column at once) plays at their intersection, on top of whatever LineClearBurstView already plays for the same event.")]
        [SerializeField] private float _crossClearComboDuration = 0.3f;

        [Tooltip("Thickness of the cross-clear combo's two crossing bars, as a fraction of the cell size.")]
        [SerializeField] private float _crossClearBarThicknessFraction = 0.34f;

        [Header("Color Cleanser Beam (issue #332)")]
        [Tooltip("Seconds one Color Cleanser beam takes to grow from the trigger cell to its target, hold, then fade.")]
        [SerializeField] private float _colorCleanserBeamDuration = 0.32f;

        [Tooltip("Thickness of a Color Cleanser beam, as a fraction of the cell size.")]
        [SerializeField] private float _colorCleanserBeamThicknessFraction = 0.22f;

        [Header("Special Cell Spawn-In (issue #330)")]
        [Tooltip("Seconds a freshly spawned special cell's icon takes to spin and shrink from _specialSpawnHeroScale down to its resting size.")]
        [SerializeField] private float _specialSpawnPopDuration = 0.55f;

        [Tooltip("Icon scale a spawn-in pop starts from before growing past 1 (see _specialSpawnOvershootScale) and settling back to 1. Only used by the vortex hand-off pop (see _vortexHandOffPopDuration) — the birth pop uses _specialSpawnHeroScale instead.")]
        [SerializeField] private float _specialSpawnStartScale = 0.05f;

        [Tooltip("Icon scale a spawn-in pop overshoots to on its way from _specialSpawnStartScale before settling back to 1. Only used by the vortex hand-off pop (see _vortexHandOffPopDuration) — the birth pop uses _specialSpawnLandingOvershootScale instead.")]
        [SerializeField] private float _specialSpawnOvershootScale = 1.18f;

        [Tooltip("Icon scale a freshly spawned special cell's icon starts at, as a multiple of its resting size (so it reads as roughly this many cells wide) before it spins down into its cell.")]
        [SerializeField] private float _specialSpawnHeroScale = 4f;

        [Tooltip("Degrees a freshly spawned special cell's icon spins around itself while shrinking from _specialSpawnHeroScale down into its cell.")]
        [SerializeField] private float _specialSpawnSpinDegrees = 360f;

        [Tooltip("Icon scale the birth pop dips to just past 1 right before landing, for a soft settle rather than stopping dead at 1.")]
        [SerializeField] private float _specialSpawnLandingOvershootScale = 1.08f;

        [Header("Vortex Island Fill (issue #349)")]
        [Tooltip("Seconds a cell a vortex reclaimed takes to pop in from _islandFillStartScale to its resting size. At least 0.4s so filling many cells still reads clearly rather than looking instant.")]
        [SerializeField] private float _islandFillDuration = 0.5f;

        [Tooltip("Block scale an island fill starts from before growing past 1 (see _islandFillOvershootScale) and settling back to 1.")]
        [SerializeField] private float _islandFillStartScale = 0.05f;

        [Tooltip("Block scale an island fill overshoots to on its way from _islandFillStartScale before settling back to 1.")]
        [SerializeField] private float _islandFillOvershootScale = 1.12f;

        [Tooltip("Seconds a vortex's hand-off target's icon takes to pop in — reuses the special-cell spawn-in pop's shape at its own duration, since a hand-off is a fresh icon appearing exactly as a genuine spawn is.")]
        [SerializeField] private float _vortexHandOffPopDuration = 0.28f;

        [Header("Special Cell Icons")]
        [Tooltip("Falls back to UiSpriteFactory.Starburst, tinted, for any kind left unassigned here.")]
        [SerializeField] private Sprite _explosiveCoreIconSprite;
        [SerializeField] private Sprite _laserIconSprite;
        [SerializeField] private Sprite _scoreGemIconSprite;
        [SerializeField] private Sprite _vortexIconSprite;
        [SerializeField] private Sprite _chainLightningIconSprite;
        [SerializeField] private Sprite _coinIconSprite;
        [SerializeField] private Sprite _timerIconSprite;

        [Tooltip("White silhouette, tinted at runtime in the gem's own theme colour (issue #395). Also drawn on decorated tray, pocket and drag-ghost cells through DiamondVisuals.")]
        [SerializeField] private Sprite _diamondIconSprite;

        [Header("Empty-Cell Bonus Count (issue #424)")]
        [Tooltip("The chunky display face the level-completion empty-cell bonus count is drawn in — the same asset ScoreView draws the score and best figures in, so the on-board count reads as the same digits the score counter is about to gain. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _bonusNumberFont;

        /// <summary>How far the bonus number's ink is pulled toward black from the theme's accent —
        /// identical to <c>ScoreView.BEST_VALUE_SHADE</c>, so the count on the board and the record
        /// figure on the score card share one ink formula (issue #424).</summary>
        private const float BONUS_NUMBER_INK_SHADE = 0.35f;

        private static readonly Color FlashTint = Color.white;

        /// <summary>The colour a <see cref="SingleLineClearEffect.Burn"/> cell's fill blends towards as
        /// it fades — a hot ember orange, so the block reads as smouldering rather than simply fading
        /// like the plain clear.</summary>
        private static readonly Color EmberTint = new Color(1f, 0.35f, 0.08f, 1f);

        /// <summary>The colour <see cref="SingleLineClearEffect.Burn"/>'s drifting ember particles are
        /// drawn in — brighter than <see cref="EmberTint"/> so the sparks read as hotter than the block
        /// they left.</summary>
        private static readonly Color EmberParticleTint = new Color(1f, 0.62f, 0.18f, 1f);

        /// <summary>
        /// Colour the issue #330 cross-clear combo flash is drawn in — an electric cyan-white, chosen to
        /// read as a distinct "combo spark" apart from every other tint this file already uses (the
        /// white intersection flash, <see cref="EmberTint"/>'s orange, every special-icon hue below), so
        /// a row+column cross-clear is never mistaken on screen for a same-tier two-line clear that
        /// happened to be two rows or two columns.
        /// </summary>
        private static readonly Color CrossClearComboTint = new Color(0.42f, 0.92f, 1f, 1f);

        /// <summary>
        /// Colour issue #332's Color Cleanser beam is drawn in — a saturated magenta-white, chosen to
        /// read as its own distinct "power-up beam" apart from <see cref="CrossClearComboTint"/>'s cyan
        /// (a same-turn placement combo) and every special-icon hue: a Color Cleanser's beams are a
        /// player-triggered power-up effect, never a placement's own line-clear combo.
        /// </summary>
        private static readonly Color ColorCleanserBeamTint = new Color(1f, 0.48f, 0.94f, 1f);

        /// <summary>Colour the special-cell icon is drawn in. Fixed rather than themed: it is a
        /// readability mark, not decoration.
        /// <para>
        /// Issue #421: the original (1, 0.95, 0.72) sat at ~86% HSL lightness — nearly white, which read
        /// as pale/washed-out regardless of the block underneath (it was never undersaturated; HSL
        /// already reported S=1). Vortex/Coin/Explosive Core, which read clearly, sit at 41–54%
        /// lightness, so this value is the same hue pulled down into that band (target L=0.5, blended
        /// 82% toward it) with a small saturation bump — "vivid tint" rather than "brighter".
        /// </para>
        /// </summary>
        private static readonly Color SpecialIconTint = new Color(1f, 0.8446f, 0.1296f, 1f);

        /// <summary>
        /// Colour a <see cref="SpecialCellKind.ScoreGem"/>'s icon is drawn in. Fixed and unthemed for
        /// the reason <see cref="SpecialIconTint"/> is, but deliberately a different hue from it: a gem
        /// destroys nothing and instead multiplies what the player scores, so it must be told apart at a
        /// glance from the kinds that do blow a hole in the board — reaching for one is a very different
        /// decision from reaching for a core or a laser.
        /// <para>
        /// The sprite is shared with them on purpose (<c>UiSpriteFactory.Starburst</c>): one sprite for
        /// every icon is what keeps an icon on any number of cells batching with the rest of the board,
        /// so the kinds are separated by tint rather than by a second texture.
        /// </para>
        /// Retuned for issue #421 the same way as <see cref="SpecialIconTint"/> — pulled from ~72% HSL
        /// lightness down to the ~50% band Vortex/Coin/Explosive Core already read clearly at.
        /// </summary>
        private static readonly Color ScoreGemIconTint = new Color(0.0792f, 1f, 0.5396f, 1f);

        /// <summary>
        /// Colour a <see cref="SpecialCellKind.Vortex"/>'s glow halo is drawn in (see
        /// <see cref="GlowIdentityColor"/>). A cool violet, as far from the warm near-white of the
        /// destructive kinds and the green of the gem as the palette allows, so the halo still reads as
        /// this kind's own colour at a glance.
        /// <para>
        /// No longer the icon's own tint: Vortex now uses a full-colour hand-picked sprite
        /// (<c>vortex_icon.png</c>) instead of a white silhouette, so <see cref="IconTint"/> returns
        /// <see cref="Color.white"/> for it and lets the sprite's own colours show through untouched.
        /// </para>
        /// <para>
        /// Internal so the Vortex info demo (<see cref="InfoDemoStage"/>, issue #446) paints its halo,
        /// burst and island outlines in this same identity hue rather than a second copy of it.
        /// </para>
        /// </summary>
        internal static readonly Color VortexIconTint = new Color(0.62f, 0.66f, 1f, 1f);

        /// <summary>
        /// Colour a <see cref="SpecialCellKind.ChainLightning"/>'s icon is drawn in. A fourth distinct
        /// hue, for the reason the two above are distinct: it is destructive like a core and a laser, but
        /// its targets are scattered rather than shaped, so the player has to be able to tell at a glance
        /// which of the destructive tiles they are looking at. A hot amber — the warm end of the palette,
        /// as far from the vortex's violet and the gem's green as the destructive kinds allow.
        /// <para>
        /// The sprite is shared with every other kind on purpose (<c>UiSpriteFactory.Starburst</c>): one
        /// sprite for every icon is what keeps an icon on any number of cells batching with the rest of
        /// the board, so the kinds are separated by tint rather than by a second texture.
        /// </para>
        /// Retuned for issue #421 the same way as <see cref="SpecialIconTint"/> — pulled from ~64% HSL
        /// lightness down to the ~50% band Vortex/Coin/Explosive Core already read clearly at.
        /// </summary>
        private static readonly Color ChainLightningIconTint = new Color(1f, 0.7998f, 0.0522f, 1f);

        /// <summary>
        /// Colour a <see cref="SpecialCellKind.Coin"/>'s glow halo is drawn in (see
        /// <see cref="GlowIdentityColor"/>). Gold, which is the one colour a player reads as currency
        /// without being told, and the same tint <see cref="CoinTotalHudView"/> paints the HUD total
        /// with so the cell and the counter it feeds are recognisably the same thing.
        /// <para>
        /// No longer the icon's own tint: Coin now uses a full-colour hand-picked sprite
        /// (<c>coin_icon.png</c>) instead of a white silhouette, so <see cref="IconTint"/> returns
        /// <see cref="Color.white"/> for it and lets the sprite's own colours show through untouched.
        /// </para>
        /// </summary>
        private static readonly Color CoinIconTint = new Color(1f, 0.82f, 0.25f, 1f);

        /// <summary>
        /// Colour a <see cref="SpecialCellKind.ExplosiveCore"/>'s glow halo is drawn in (see
        /// <see cref="GlowIdentityColor"/>). A deep crimson, chosen to sit far from both
        /// <see cref="CoinIconTint"/>'s gold and <see cref="VortexIconTint"/>'s violet — the core's own
        /// full-colour sprite (<c>explosive_core_icon.png</c>) is a red/magenta plasma, and an amber or
        /// gold glow behind it would read as the same kind as a coin at a glance.
        /// <para>
        /// Like Vortex and Coin, <see cref="IconTint"/> returns <see cref="Color.white"/> for this kind
        /// so the sprite's own colours show through untouched; this constant only tints the glow.
        /// </para>
        /// </summary>
        private static readonly Color ExplosiveCoreGlowTint = new Color(1f, 0.2f, 0.35f, 1f);

        /// <summary>How far a kind's glow halo (issue #365) is blended towards white on top of its own
        /// <see cref="IconTint"/> — bright enough to read as an emissive backing against any theme's
        /// fill (AC3) while the blend keeps enough of the source hue that a kind's glow still looks
        /// unmistakably its own colour rather than one shared white halo for all seven (AC5).
        /// <para>
        /// Tuned against the worst case found by checking every theme's 5 <c>_kindFills</c> against every
        /// kind's glow (issue #365 AC3): a saturated near-white-luminance fill (Yaz's yellow,
        /// <c>0.299R+0.587G+0.114B</c> &#8776; 0.82) sitting next to a cool glow (Vortex) at the pulse's
        /// dimmest instant still separates further in luminance than the original bug's flat-icon
        /// contrast gap (&#8776; 0.035) did — and unlike that flat icon, this glow adds a moving pulse and
        /// a separate near-white rim-light on the icon itself, neither of which a single luminance number
        /// captures.
        /// </para>
        /// <para>
        /// Issue #421 lowered this from 0.6 to 0.5 (and raised <see cref="GLOW_BASE_ALPHA"/>) as part of
        /// "vivid tint, stronger glow": keeping more of the source hue and more alpha makes the halo read
        /// as an emissive backing rather than a soft white smudge, closer to how Vortex/Coin/Explosive
        /// Core's own saturated art already reads.
        /// </para>
        /// </summary>
        private const float GLOW_TINT_WHITEN = 0.5f;

        /// <summary>The glow halo's resting alpha at rest (before the per-frame pulse in
        /// <see cref="Update"/> multiplies it) — bright enough to lift a special cell's contrast on every
        /// theme fill (AC3) while staying inside the halo's own soft falloff rather than reading as a
        /// solid disc (AC4's "stays subtle"). Raised from 0.68 for issue #421 — see
        /// <see cref="GLOW_TINT_WHITEN"/>'s remarks.</summary>
        private const float GLOW_BASE_ALPHA = 0.8f;

        /// <summary>The glow pulse's alpha multiplier range (issue #365 AC4): never fully off, so the
        /// halo does not flicker out every cycle, and never brighter than its resting alpha, so the pulse
        /// reads as a gentle breathe rather than a flash. Kept fairly narrow (0.75..1) rather than
        /// dropping further, so the dimmest instant of the pulse never gives up the contrast margin
        /// <see cref="GLOW_TINT_WHITEN"/> and <see cref="GLOW_BASE_ALPHA"/> were tuned for.</summary>
        private const float GLOW_PULSE_MIN_MULTIPLIER = 0.75f;
        private const float GLOW_PULSE_MAX_MULTIPLIER = 1f;

        /// <summary>The glow pulse's speed, in radians of <see cref="Mathf.Sin"/> per second — chosen for
        /// a slow, deliberate breathe (about one full cycle every 3-4 seconds) rather than anything that
        /// could read as urgent or distracting on a board with several special cells pulsing at once.</summary>
        private const float GLOW_PULSE_SPEED = 1.8f;

        /// <summary>
        /// How far a hole cell's fill is pushed towards black relative to an empty cell's, and how far
        /// its alpha is pulled down. Derived from the active theme rather than authored per theme, and
        /// deliberately a placeholder: a hole is "not part of the board", and until it has real art it
        /// reads as a recessed gap that no theme has to author a colour for and none can make look
        /// mistakable for a cell a piece could be dropped on.
        /// <para>
        /// See the Developer Action Required note in this issue's report: replacing this with a proper
        /// hole sprite is an Editor asset job an agent cannot do, and this keeps the code path complete
        /// and testable until it is done.
        /// </para>
        /// </summary>
        private const float HOLE_DARKEN = 0.62f;
        private const float HOLE_ALPHA = 0.55f;

        /// <summary>
        /// The reference ceiling an ice socket's overlay opacity is spread across (issue #433) — the
        /// highest ice level a level may author. A socket authored with fewer levels simply starts
        /// partway down the same ramp, so every socket with one level left looks equally thin whatever
        /// it started at, which is the reading that matters to the player: "one more and it goes".
        /// </summary>
        private const int MAX_ICE_LEVEL = TargetIceCellAuthoring.MAX_ICE_LEVEL;

        private readonly GridPosition[] _previewCells = new GridPosition[16];

        // Its own claim set, kept apart from the drag preview's: the silhouette and a drag can be on
        // screen together, so neither may restore the other's cells.
        private readonly GridPosition[] _ghostFitCells = new GridPosition[16];

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        // A power-up never targets more than a full line or a 3x3 block, but the array is sized to
        // the board so a future kind with a wider footprint cannot silently truncate its preview.
        // Allocated with the grid (see BuildCells), because the board's dimensions are not known until
        // the model has been injected.
        private GridPosition[] _powerUpTargetCells;

        private bool[] _rowClearMask;
        private bool[] _columnClearMask;

        // Which cells currently wear the would-clear outline, and the set being built for this
        // frame. Two fixed masks so the per-frame update is a diff against what is already on
        // screen: no allocation, and only the cells that actually changed are touched.
        private bool[] _highlightMask;
        private bool[] _pendingHighlightMask;

        /// <summary>Which cells are holes, mirrored from the model's shape once at build time and
        /// indexed like every other per-cell array. Mirrored rather than re-queried per repaint because
        /// the shape cannot change while the grid it built exists, and a repaint touches every cell.</summary>
        private bool[] _holeMask;

        /// <summary>The board's dimensions, taken from the model. Default to the standard square only
        /// so a grid built before injection has something to build; <see cref="EnsureBuilt"/> rebuilds
        /// the grid if the model turns out to describe a different shape.</summary>
        private int _width = Board.SIZE;
        private int _height = Board.SIZE;

        private RectTransform _rectTransform;
        private RectTransform _cardRoot;

        /// <summary>The cell grid's own nested Canvas root (see <see cref="BuildCellLayer"/>), kept so
        /// the single-line clear effects (issue #331) can parent their transient shard/ember visuals
        /// alongside the cells rather than allocating a layer of their own.</summary>
        private RectTransform _cellLayerRoot;

        private Canvas _canvas;
        private CellView[] _cells;
        private Image _cardImage;
        private Image _cardShadowImage;
        private Image _wellLipImage;
        private Image _wellFaceImage;

        // Per-cell bookkeeping, parallel to _cells and indexed by CellIndex.
        private int[] _cellColourIds;
        private int[] _cellGenerations;
        private int[] _pendingColourIds;
        private int[] _pendingGenerations;
        private bool[] _cellPending;

        // The special kind each cell has settled on, parallel to the colour bookkeeping above. A cell
        // that is fading out has already settled on None and keeps showing its icon until the fade
        // ends, so the icon follows its block out instead of vanishing the instant the model empties
        // the cell — which is why no "pending kind" counterpart is needed.
        private SpecialCellKind[] _cellSpecialKinds;

        /// <summary>Hits each cell has left, parallel to the bookkeeping above. 0 for every ordinary
        /// cell, which is every cell on a board no level reinforced. Kept in step with the model's own
        /// <see cref="BoardModel.HitCountChanged"/> so that notification can be told from a re-announce
        /// of an unchanged count; the look it drives is the skin overlay, read through
        /// <see cref="ReadOverlayState"/> rather than from this array (issue #438).</summary>
        private int[] _cellHitCounts;

        /// <summary>Ice levels left at each position (issue #433), parallel to the bookkeeping above.
        /// 0 for every position no level marked. Unlike <see cref="_cellHitCounts"/> it is NOT zeroed
        /// when a cell empties — the ice belongs to the position and survives the block above it — and
        /// is only ever written from the model's own <see cref="BoardModel.TargetIceLevelChanged"/> or a
        /// full redraw. Drives <see cref="CellView.SetIceOverlay"/>.</summary>
        private int[] _cellIceLevels;

        /// <summary>Layers each cell's skin overlay still shows, parallel to the bookkeeping above: a
        /// locked cell's threshold minus the distinct neighbours already counted (issue #434), or a
        /// reinforced cell's hits left (issue #438 — the two wear the same art and a cell is never both).
        /// 0 for every other cell, which is every cell on a board no level locked or reinforced. Mirrors
        /// what <see cref="CellView.SetLockedOverlay"/> was last given, so a re-announce of an unchanged
        /// state can be skipped; always derived through <see cref="ReadOverlayState"/>, never written
        /// from a notification's payload directly.</summary>
        private int[] _cellLockedStages;

        /// <summary>Which skin each overlay-wearing cell shows, parallel to <see cref="_cellLockedStages"/>
        /// and meaningless where that reads 0.</summary>
        private int[] _cellLockedSkins;

        /// <summary>Placements left before each <see cref="SpecialCellKind.Timer"/> cell converts to an
        /// ordinary one, parallel to the bookkeeping above. 0 for every cell that is not a timer cell —
        /// drives the countdown number in <see cref="ApplyTimerCountdown"/> (issue #307 AC6a).</summary>
        private int[] _cellTimerCountdowns;

        /// <summary>The gem colour of each <see cref="SpecialCellKind.Diamond"/> cell, parallel to the
        /// bookkeeping above. <see cref="TrayModel.NO_DIAMOND"/> for every cell that is not a diamond
        /// cell — drives the per-cell icon and glow tint in <see cref="ApplyCellIcon"/> (issue #395),
        /// which is the one kind whose tint is a per-cell value rather than a per-kind constant.</summary>
        private int[] _cellDiamondColourIds;

        /// <summary>Which cells currently show the special-cell glow halo (issue #365), parallel to the
        /// bookkeeping above — mirrors <see cref="_activeGlowCells"/>'s membership so
        /// <see cref="ApplyCellIcon"/> can add/remove a cell exactly once per actual kind change instead
        /// of scanning the list.</summary>
        private bool[] _glowActiveMask;

        /// <summary>Every cell currently glowing, in no particular order — the one collection
        /// <see cref="Update"/> walks each frame to drive the pulse, so a full board never has to poll
        /// all 64 cells for the handful that are actually special. Pre-allocated once and maintained by
        /// adding/removing single entries in <see cref="ApplyCellIcon"/>, never rebuilt, so the per-frame
        /// walk allocates nothing.</summary>
        private readonly List<CellView> _activeGlowCells = new List<CellView>(Board.SIZE * Board.SIZE);

        private BoardModel _boardModel;
        private SettingsModel _settingsModel;
        private TimerRunSystem _timerRunSystem;
        private ThemeDefinition _currentTheme;
        private ISubscriber<LinesClearedMessage> _linesClearedSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private ISubscriber<PowerUpAppliedMessage> _powerUpAppliedSubscriber;
        private ISubscriber<ExplosiveCoreDetonatedMessage> _explosiveCoreDetonatedSubscriber;
        private ISubscriber<LaserFiredMessage> _laserFiredSubscriber;
        private ISubscriber<PiercingRocketFiredMessage> _piercingRocketFiredSubscriber;
        private ISubscriber<VortexIslandFilledMessage> _vortexIslandFilledSubscriber;
        private ISubscriber<ChainLightningTriggeredMessage> _chainLightningTriggeredSubscriber;
        private ISubscriber<SpecialCellSpawnedMessage> _specialCellSpawnedSubscriber;
        private ISubscriber<EmptyCellBonusCountingMessage> _emptyCellBonusCountingSubscriber;
        private IPublisher<EmptyCellBonusCountingCompletedMessage> _emptyCellBonusCountingCompletedPublisher;

        /// <summary>The grid's layout origin and pitch, kept from <see cref="BuildCells"/> so the pull
        /// animation can work out where a cell one step away sits without re-deriving the layout.</summary>
        private float _cellOriginX;
        private float _cellOriginY;
        private float _cellPitch;

        private CancellationToken _destroyToken;
        private int _previewCount;
        private int _ghostFitCount;
        private int _powerUpTargetCount;
        private float _gridExtent;
        private bool _isDestroyed;
        private bool _hasHighlight;

        [Inject]
        public void Construct(
            BoardModel boardModel,
            SettingsModel settingsModel,
            TimerRunSystem timerRunSystem,
            ISubscriber<LinesClearedMessage> linesClearedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<PowerUpAppliedMessage> powerUpAppliedSubscriber,
            ISubscriber<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedSubscriber,
            ISubscriber<LaserFiredMessage> laserFiredSubscriber,
            ISubscriber<PiercingRocketFiredMessage> piercingRocketFiredSubscriber,
            ISubscriber<VortexIslandFilledMessage> vortexIslandFilledSubscriber,
            ISubscriber<ChainLightningTriggeredMessage> chainLightningTriggeredSubscriber,
            ISubscriber<SpecialCellSpawnedMessage> specialCellSpawnedSubscriber,
            ISubscriber<EmptyCellBonusCountingMessage> emptyCellBonusCountingSubscriber,
            IPublisher<EmptyCellBonusCountingCompletedMessage> emptyCellBonusCountingCompletedPublisher)
        {
            _emptyCellBonusCountingSubscriber = emptyCellBonusCountingSubscriber;
            _emptyCellBonusCountingCompletedPublisher = emptyCellBonusCountingCompletedPublisher;
            _explosiveCoreDetonatedSubscriber = explosiveCoreDetonatedSubscriber;
            _laserFiredSubscriber = laserFiredSubscriber;
            _piercingRocketFiredSubscriber = piercingRocketFiredSubscriber;
            _vortexIslandFilledSubscriber = vortexIslandFilledSubscriber;
            _chainLightningTriggeredSubscriber = chainLightningTriggeredSubscriber;
            _specialCellSpawnedSubscriber = specialCellSpawnedSubscriber;
            _boardModel = boardModel;
            _settingsModel = settingsModel;
            _timerRunSystem = timerRunSystem;
            _linesClearedSubscriber = linesClearedSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
            _powerUpAppliedSubscriber = powerUpAppliedSubscriber;
        }

        internal float CellSize => _cellSize;

        /// <summary>
        /// Y of the board card's top edge for the standard <see cref="Board.SIZE"/> board, in canvas
        /// reference units from the vertical centre. Derived from the serialized layout alone so it is
        /// valid before any board is built and never moves with a level's shape: the HUD row that sits
        /// on the board (objective icons, coin) hangs from this rather than from the screen's top edge,
        /// so a taller screen or a notch can no longer push it down into the card (issue #229).
        /// </summary>
        internal float StandardCardTopEdge
        {
            get
            {
                float gridExtent = (Board.SIZE * _cellSize) + ((Board.SIZE - 1) * _cellSpacing);
                return _anchoredPosition.y + ((gridExtent + (_cardPadding * 2f)) * 0.5f);
            }
        }

        internal float CellSpacing => _cellSpacing;

        internal float CellInset => _cellInset;

        internal float CellBevelThickness => _cellBevelThickness;

        private void Awake()
        {
            _destroyToken = this.GetCancellationTokenOnDestroy();

            _rectTransform = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();

            EnsureBuilt();
        }

        /// <summary>
        /// Builds the card and the cell grid for the model's board shape, once.
        /// <para>
        /// Called from <c>Awake</c> and again from <c>Start</c> because injection order against
        /// <c>Awake</c> is not guaranteed: the first call may have to fall back to the standard square,
        /// and the second — which always has the model — rebuilds only if the real shape turns out to
        /// differ. On the standard board the second call is a no-op, so nothing about the existing
        /// build path changes.
        /// </para>
        /// </summary>
        private void EnsureBuilt()
        {
            int width = _boardModel != null ? _boardModel.Width : Board.SIZE;
            int height = _boardModel != null ? _boardModel.Height : Board.SIZE;

            if (_cells != null && _width == width && _height == height)
            {
                return;
            }

            if (_cardRoot != null)
            {
                Destroy(_cardRoot.gameObject);
                _cardRoot = null;
            }

            _width = width;
            _height = height;

            float gridWidth = (_width * _cellSize) + ((_width - 1) * _cellSpacing);
            float gridHeight = (_height * _cellSize) + ((_height - 1) * _cellSpacing);

            // One extent for both axes on a square board, which is every board today. A non-square one
            // takes the larger, so the card still frames the whole grid.
            _gridExtent = Mathf.Max(gridWidth, gridHeight);
            float cardExtent = _gridExtent + (_cardPadding * 2f);

            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.sizeDelta = new Vector2(cardExtent, cardExtent);
            _rectTransform.anchoredPosition = _anchoredPosition;

            // The storefront plate (issue #265): a card over a dropped shadow, with the grid sunk into
            // a well inset by the rim. Both are painted by the theme subscription in Start.
            _cardRoot = HudChrome.BuildPlate(
                _rectTransform,
                "BoardCard",
                new Vector2(cardExtent, cardExtent),
                Vector2.zero,
                CARD_CORNER_RADIUS,
                CARD_SHADOW_DROP,
                out _cardShadowImage,
                out _cardImage);

            float wellExtent = cardExtent - (WELL_RIM * 2f);
            HudChrome.BuildWell(
                _cardRoot, "Well", new Vector2(wellExtent, wellExtent), Vector2.zero, WELL_CORNER_RADIUS,
                out _wellLipImage, out _wellFaceImage);

            _cellLayerRoot = BuildCellLayer(_cardRoot);
            BuildCells(_cellLayerRoot);
        }

        private void Start()
        {
            if (_boardModel == null || _settingsModel == null)
            {
                Debug.LogError($"{nameof(BoardView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // The model is guaranteed injected by now, so this adopts the real board shape if Awake
            // had to guess at it.
            EnsureBuilt();

            // Subscribed first so _currentTheme is set before anything below paints a cell.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _boardModel.CellChanged += OnCellChanged;
            _boardModel.SpecialKindChanged += OnSpecialKindChanged;
            _boardModel.HitCountChanged += OnHitCountChanged;
            _boardModel.TimerCountdownChanged += OnTimerCountdownChanged;
            _boardModel.TimerCellExpired += OnTimerCellExpired;
            _boardModel.TargetIceLevelChanged += OnTargetIceLevelChanged;
            _boardModel.LockedCellChanged += OnLockedCellChanged;
            _linesClearedSubscriber.Subscribe(OnLinesCleared).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
            _powerUpAppliedSubscriber.Subscribe(OnPowerUpApplied).AddTo(_disposables);

            // Same handler as a power-up's: a core's bonus wipe empties a line whether or not it was
            // full, so its cells need claiming exactly the way a power-up's cleared region does.
            _explosiveCoreDetonatedSubscriber.Subscribe(OnExplosiveCoreDetonated).AddTo(_disposables);

            // Same again for a laser's wipe, which empties a line whether or not it was full — so no
            // LinesClearedMessage describes it either.
            _laserFiredSubscriber.Subscribe(OnLaserFired).AddTo(_disposables);

            // And once more for a piercing rocket's wipe, which empties a whole row and a whole column
            // whether or not either was full — so, exactly like a laser's, no LinesClearedMessage
            // describes it and nothing else would ever claim the cells it emptied (issue #354).
            _piercingRocketFiredSubscriber.Subscribe(OnPiercingRocketFired).AddTo(_disposables);

            // Not a sweep, unlike the four above: a vortex destroys nothing, so there is no cell to
            // fade — a fill pops a cell in and a hand-off pops a fresh icon in.
            _vortexIslandFilledSubscriber.Subscribe(OnVortexIslandFilled).AddTo(_disposables);

            // Back to the sweep: a chain lightning strike destroys, and its cells are scattered rather
            // than lined up, so nothing else would ever claim them.
            _chainLightningTriggeredSubscriber.Subscribe(OnChainLightningTriggered).AddTo(_disposables);

            // Issue #330 AC2: the one signal that means "this cell's kind just arrived by genuine
            // creation" (see SpecialCellSpawnedMessage's own doc comment) — not a level-authored Timer
            // cell's seeding, not a vortex carrying an existing kind to a new position, both of which
            // also raise BoardModel.SpecialKindChanged but neither of which publish this message.
            _specialCellSpawnedSubscriber.Subscribe(OnSpecialCellSpawned).AddTo(_disposables);

            // Issue #424: a cleared Path level pays one point per empty cell, and the system holds the
            // result screen back until this view has counted those cells out over the board.
            _emptyCellBonusCountingSubscriber.Subscribe(OnEmptyCellBonusCounting).AddTo(_disposables);

            RedrawAll();
        }

        /// <summary>
        /// Drives the special-cell glow halo's gentle pulse (issue #365 AC4) — one
        /// <see cref="Mathf.Sin"/>-based scalar computed once per frame and applied to every currently
        /// glowing cell, rather than polling all 64 cells: <see cref="_activeGlowCells"/> is maintained
        /// incrementally by <see cref="ApplyCellIcon"/> and holds nothing but cells whose kind is not
        /// <see cref="SpecialCellKind.None"/>. Allocates nothing — the list's <c>Count</c> and indexer
        /// do not allocate, <see cref="CellView.SetGlowPulse"/> only writes one float, and so does
        /// <see cref="CellView.SetIconShine"/> (issue #421's icon shine, added on the same wave rather
        /// than a second timer, so it breathes in lockstep with the glow instead of drifting against it).
        /// </summary>
        private void Update()
        {
            int glowingCount = _activeGlowCells.Count;
            if (glowingCount == 0)
            {
                return;
            }

            float wave = 0.5f + (0.5f * Mathf.Sin(Time.time * GLOW_PULSE_SPEED));
            float multiplier = GLOW_PULSE_MIN_MULTIPLIER
                + ((GLOW_PULSE_MAX_MULTIPLIER - GLOW_PULSE_MIN_MULTIPLIER) * wave);

            for (int glowIndex = 0; glowIndex < glowingCount; glowIndex++)
            {
                CellView cell = _activeGlowCells[glowIndex];
                cell.SetGlowPulse(multiplier);
                cell.SetIconShine(wave);
            }
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _disposables.Dispose();

            if (_boardModel != null)
            {
                _boardModel.CellChanged -= OnCellChanged;
                _boardModel.SpecialKindChanged -= OnSpecialKindChanged;
                _boardModel.HitCountChanged -= OnHitCountChanged;
                _boardModel.TimerCountdownChanged -= OnTimerCountdownChanged;
                _boardModel.TimerCellExpired -= OnTimerCellExpired;
                _boardModel.TargetIceLevelChanged -= OnTargetIceLevelChanged;
                _boardModel.LockedCellChanged -= OnLockedCellChanged;
            }
        }

        /// <summary>Maps a screen point to a board cell. Points within <see cref="_previewLeadMarginFraction"/>
        /// of a cell's width/height outside the grid still resolve to the nearest edge cell, so the
        /// placement preview can appear slightly ahead of the pointer entering the grid. False when
        /// the point is farther than that margin from the grid.</summary>
        internal bool TryGetCell(Vector2 screenPosition, out GridPosition cell)
        {
            cell = default;

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rectTransform, screenPosition, eventCamera, out Vector2 local))
            {
                return false;
            }

            float halfExtent = _gridExtent * 0.5f;
            float margin = _cellSize * _previewLeadMarginFraction;

            if (local.x < -halfExtent - margin || local.x > halfExtent + margin
                || local.y < -halfExtent - margin || local.y > halfExtent + margin)
            {
                return false;
            }

            float pitch = _cellSize + _cellSpacing;
            float originX = local.x + halfExtent;
            float originY = local.y + halfExtent;

            int x = Mathf.Clamp(Mathf.FloorToInt(originX / pitch), 0, _width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(originY / pitch), 0, _height - 1);

            cell = new GridPosition(x, y);
            return true;
        }

        internal Vector3 GetCellWorldPosition(GridPosition cell)
            => _cells[CellIndex(cell)].transform.position;

        /// <summary>Tints the cells a piece would occupy. Safe to call every frame while dragging.
        /// Shows nothing when the placement is not legal — an invalid drop spot gets no shadow at all.</summary>
        internal void ShowPreview(Piece piece, GridPosition anchor, bool isValid)
        {
            ClearPreview();

            if (piece == null || _currentTheme == null || !isValid)
            {
                return;
            }

            Color tint = _currentTheme.ValidPreview;
            for (int i = 0; i < piece.Offsets.Count && _previewCount < _previewCells.Length; i++)
            {
                GridPosition cell = anchor + piece.Offsets[i];

                // A hole is refused here exactly as an off-board cell is, so no legality feedback can
                // ever paint the valid-placement tint over one (AC4).
                if (!IsPlayableCell(cell))
                {
                    continue;
                }

                int index = CellIndex(cell);

                // A cell still fading out from a line clear must drop the fade the instant the
                // preview claims it, otherwise the tint would be drawn at partial alpha.
                CancelFade(index);

                _cells[index].SetColours(tint, tint);
                _previewCells[_previewCount] = cell;
                _previewCount++;
            }
        }

        internal void ClearPreview()
        {
            for (int i = 0; i < _previewCount; i++)
            {
                GridPosition cell = _previewCells[i];
                ApplyCellColour(cell, _boardModel != null ? _boardModel.GetCell(cell) : Board.EMPTY);
            }

            _previewCount = 0;
        }

        /// <summary>
        /// Tints exactly the cells an armed power-up would hit, occupied or not. The caller reads the
        /// set from <see cref="PowerUpTargetCells"/> — the same geometry the application itself uses —
        /// so the tinted region is never a promise the power-up would not keep.
        /// <para>
        /// Shares the valid-placement tint on purpose: the project has no power-up-specific colour,
        /// and both mean the same thing to the player — "this is what the thing in your hand lands
        /// on". Safe to call every pointer-move frame.
        /// </para>
        /// <para>
        /// <paramref name="isValid"/> exists for the joker, the one kind that can be aimed at a cell
        /// it cannot legally use (an occupied one). It borrows <see cref="ShowPreview"/>'s
        /// valid/invalid tint pair so "this tap will do nothing" reads the same way whether the player
        /// is holding a piece or a power-up. The three region-clearing kinds are legal anywhere on the
        /// board and simply leave it at its default.
        /// </para>
        /// </summary>
        /// <remarks>The caller's list is read here and never stored.</remarks>
        internal void ShowPowerUpTargetHighlight(IReadOnlyList<GridPosition> cells, bool isValid = true)
        {
            ClearPowerUpTargetHighlight();

            if (cells == null || _currentTheme == null || _cells == null)
            {
                return;
            }

            Color tint = isValid ? _currentTheme.ValidPreview : _currentTheme.InvalidPreview;
            for (int i = 0; i < cells.Count && _powerUpTargetCount < _powerUpTargetCells.Length; i++)
            {
                GridPosition cell = cells[i];
                if (!IsPlayableCell(cell))
                {
                    continue;
                }

                int index = CellIndex(cell);

                // Same reason as ShowPreview: a cell still fading out from a clear must drop the fade
                // the instant the tint claims it, or the tint would be drawn at partial alpha.
                CancelFade(index);

                _cells[index].SetColours(tint, tint);
                _powerUpTargetCells[_powerUpTargetCount] = cell;
                _powerUpTargetCount++;
            }
        }

        /// <summary>Restores every cell the power-up target tint claimed. Called when the pointer
        /// leaves the board, on release, and whenever the run is rebuilt underneath it.</summary>
        internal void ClearPowerUpTargetHighlight()
        {
            for (int i = 0; i < _powerUpTargetCount; i++)
            {
                GridPosition cell = _powerUpTargetCells[i];
                ApplyCellColour(cell, _boardModel != null ? _boardModel.GetCell(cell) : Board.EMPTY);
            }

            _powerUpTargetCount = 0;
        }

        /// <summary>
        /// Projects the Ghost Fit silhouette: the cells the suggested piece would occupy, tinted at
        /// <paramref name="pulse"/> of the way from an empty cell's own fill to the valid-placement
        /// tint. The caller drives <paramref name="pulse"/> (0..1) every frame, so the animation lives
        /// with the thing that owns the suggestion and this stays a stateless "draw it like this".
        /// <para>
        /// A separate claim set from <see cref="ShowPreview"/>'s, so the two can be up at once — which
        /// they are whenever the player drags the very piece being suggested. Where they overlap, each
        /// repaints every frame, so the worst case is one frame of the other's tint.
        /// </para>
        /// </summary>
        internal void ShowGhostFitSilhouette(Piece piece, GridPosition anchor, float pulse)
        {
            ClearGhostFitSilhouette();

            if (piece == null || _currentTheme == null || _cells == null)
            {
                return;
            }

            // The mockup's ghost: the kind's fill at a translucent alpha inside a ring of the same
            // kind, both breathing with the pulse. The fill is blended over the empty cell rather than
            // drawn with alpha so the cell underneath never shows through at a partial tint.
            Color ghost = _currentTheme.GetFill(GHOST_KIND);
            float breath = Mathf.Clamp01(pulse);
            Color fill = Color.Lerp(_currentTheme.EmptyCellFill, ghost, GHOST_FILL_ALPHA * breath);
            Color ring = HudChrome.WithAlpha(ghost, breath);

            for (int i = 0; i < piece.Offsets.Count && _ghostFitCount < _ghostFitCells.Length; i++)
            {
                GridPosition cell = anchor + piece.Offsets[i];
                if (!IsPlayableCell(cell))
                {
                    continue;
                }

                int index = CellIndex(cell);

                // Same reason as ShowPreview: a cell still fading out from a clear must drop the fade
                // the instant the tint claims it, or the tint would be drawn at partial alpha.
                CancelFade(index);

                _cells[index].SetColours(fill, fill);
                _cells[index].SetGhostRing(ring);
                _ghostFitCells[_ghostFitCount] = cell;
                _ghostFitCount++;
            }
        }

        /// <summary>Restores every cell the silhouette claimed. Called when the suggestion is dismissed,
        /// and whenever the run is rebuilt underneath it.</summary>
        internal void ClearGhostFitSilhouette()
        {
            for (int i = 0; i < _ghostFitCount; i++)
            {
                GridPosition cell = _ghostFitCells[i];
                _cells[CellIndex(cell)].ClearGhostRing();
                ApplyCellColour(cell, _boardModel != null ? _boardModel.GetCell(cell) : Board.EMPTY);
            }

            _ghostFitCount = 0;
        }

        /// <summary>
        /// Outlines every cell of the rows/columns the in-flight drag would clear — filled cells and
        /// the empty ones the piece would occupy alike. Safe to call every frame: it diffs against
        /// what is already outlined, so an unchanged set costs nothing and a line that stopped
        /// qualifying is switched off in the same pass. Empty lists mean "outline nothing".
        /// <para>
        /// The outline is its own layer inside <see cref="CellView"/>, so it stacks on top of the
        /// <see cref="ShowPreview"/> tint instead of competing with it.
        /// </para>
        /// </summary>
        /// <remarks>The caller's lists are read here and never stored — <c>BoardSystem</c> hands back
        /// scratch buffers it overwrites on the next query.</remarks>
        internal void ShowWouldClearHighlight(IReadOnlyList<int> rows, IReadOnlyList<int> columns)
        {
            Array.Clear(_pendingHighlightMask, 0, _pendingHighlightMask.Length);

            if (_currentTheme == null || _cells == null)
            {
                ApplyHighlightMask();
                return;
            }

            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    int y = rows[i];
                    if (y < 0 || y >= _height)
                    {
                        continue;
                    }

                    for (int x = 0; x < _width; x++)
                    {
                        int index = (y * _width) + x;
                        if (_holeMask[index])
                        {
                            continue;
                        }

                        _pendingHighlightMask[index] = true;
                    }
                }
            }

            if (columns != null)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    int x = columns[i];
                    if (x < 0 || x >= _width)
                    {
                        continue;
                    }

                    for (int y = 0; y < _height; y++)
                    {
                        int index = (y * _width) + x;
                        if (_holeMask[index])
                        {
                            continue;
                        }

                        _pendingHighlightMask[index] = true;
                    }
                }
            }

            ApplyHighlightMask();
        }

        /// <summary>Drops every would-clear outline. Called on drop, cancel and whenever the drag has
        /// no legal anchor.</summary>
        internal void ClearWouldClearHighlight()
        {
            if (!_hasHighlight)
            {
                return;
            }

            Array.Clear(_pendingHighlightMask, 0, _pendingHighlightMask.Length);
            ApplyHighlightMask();
        }

        private int CellIndex(GridPosition cell) => (cell.Y * _width) + cell.X;

        /// <summary>The on-screen rect of one board cell, for <see cref="TutorialOverlayView"/> to
        /// spotlight and for <see cref="BoardInputView"/>'s tutorial input guard to hit-test against.
        /// Null when <paramref name="cell"/> is off the board or the cells have not been built yet.</summary>
        internal RectTransform GetCellRectTransform(GridPosition cell)
        {
            if (_cells == null || !IsPlayableCell(cell))
            {
                return null;
            }

            return (RectTransform)_cells[CellIndex(cell)].transform;
        }

        /// <summary>The on-screen rect of the whole board, for <see cref="TutorialOverlayView"/> to
        /// spotlight and for <see cref="BoardInputView"/>'s tutorial input guard to hit-test against
        /// (issue #279's power-up effect-area coach-mark step).</summary>
        internal RectTransform GetBoardRectTransform() => _rectTransform;

        /// <summary>True when <paramref name="cell"/> is on the board and not a hole — the one gate
        /// every legality-feedback path (drag preview, ghost-fit silhouette, power-up target highlight)
        /// passes through, so none of them can tint a cell a piece could never occupy.</summary>
        private bool IsPlayableCell(GridPosition cell)
        {
            if (cell.X < 0 || cell.X >= _width || cell.Y < 0 || cell.Y >= _height)
            {
                return false;
            }

            return !_holeMask[(cell.Y * _width) + cell.X];
        }

        /// <summary>A hole's fill: the theme's empty-cell fill pushed towards black and made partly
        /// transparent, so the card shows through and the cell reads as a gap in the board.</summary>
        private static Color HoleFill(ThemeDefinition theme) => Recess(theme.EmptyCellFill);

        private static Color HoleOutline(ThemeDefinition theme) => Recess(theme.EmptyCellOutline);

        private static Color Recess(Color source)
            => new Color(
                source.r * HOLE_DARKEN,
                source.g * HOLE_DARKEN,
                source.b * HOLE_DARKEN,
                source.a * HOLE_ALPHA);

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        /// <summary>Own nested Canvas for the cell grid: the per-frame colour/alpha writes from
        /// clear fades only rebuild this canvas, not the shared UICanvas that also holds the score
        /// and the tray.</summary>
        private static RectTransform BuildCellLayer(RectTransform parent)
        {
            var layerObject = new GameObject("CellLayer", typeof(RectTransform), typeof(Canvas));
            var layerRect = (RectTransform)layerObject.transform;
            layerRect.SetParent(parent, false);
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;
            return layerRect;
        }

        private void BuildCells(RectTransform parent)
        {
            int cellCount = _width * _height;
            _cells = new CellView[cellCount];
            _powerUpTargetCells = new GridPosition[cellCount];
            _rowClearMask = new bool[_height];
            _columnClearMask = new bool[_width];
            _highlightMask = new bool[cellCount];
            _pendingHighlightMask = new bool[cellCount];
            _holeMask = new bool[cellCount];
            _cellColourIds = new int[cellCount];
            _cellGenerations = new int[cellCount];
            _pendingColourIds = new int[cellCount];
            _pendingGenerations = new int[cellCount];
            _cellPending = new bool[cellCount];
            _cellSpecialKinds = new SpecialCellKind[cellCount];
            _cellHitCounts = new int[cellCount];
            _cellIceLevels = new int[cellCount];
            _cellLockedStages = new int[cellCount];
            _cellLockedSkins = new int[cellCount];
            _cellTimerCountdowns = new int[cellCount];
            _cellDiamondColourIds = new int[cellCount];
            _glowActiveMask = new bool[cellCount];

            // A rebuild (EnsureBuilt on a board-shape change) destroys every previous CellView, so any
            // entries left over from the old grid would be stale references — cleared rather than left
            // to be pruned lazily, since nothing else ever removes from this list.
            _activeGlowCells.Clear();

            for (int i = 0; i < cellCount; i++)
            {
                _cellColourIds[i] = Board.EMPTY;
                _pendingColourIds[i] = Board.EMPTY;
            }

            float pitch = _cellSize + _cellSpacing;
            float originX = (-((_width * _cellSize) + ((_width - 1) * _cellSpacing)) * 0.5f) + (_cellSize * 0.5f);
            float originY = (-((_height * _cellSize) + ((_height - 1) * _cellSpacing)) * 0.5f) + (_cellSize * 0.5f);

            // Kept so the pull animation can place a cell at a neighbour's position without re-deriving
            // the layout — and so there is exactly one definition of where a cell sits.
            _cellPitch = pitch;
            _cellOriginX = originX;
            _cellOriginY = originY;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var position = new GridPosition(x, y);
                    int index = (y * _width) + x;
                    _holeMask[index] = _boardModel != null && _boardModel.IsHole(position);

                    CellView cell = CellFactory.CreateCell(
                        parent, $"Cell_{x}_{y}", _cellSize, _cellInset, _cellBevelThickness);
                    var rect = (RectTransform)cell.transform;
                    rect.anchoredPosition = new Vector2(originX + (x * pitch), originY + (y * pitch));

                    // Cells are built in Awake, before the theme is known; the theme subscription in
                    // Start paints them (and repaints them on every later theme switch).
                    cell.SetColours(Color.clear, Color.clear);
                    _cells[index] = cell;
                }
            }
        }

        /// <summary>Forces every cell back to the model's state, opaque and with no fade in flight.</summary>
        private void RedrawAll()
        {
            // A run restart wipes the board underneath any drag or power-up aim that was in flight,
            // so nothing either of them tinted or outlined may survive it.
            ClearWouldClearHighlight();
            _powerUpTargetCount = 0;
            _ghostFitCount = 0;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var cell = new GridPosition(x, y);
                    int index = CellIndex(cell);

                    _cellGenerations[index]++;
                    _cellPending[index] = false;

                    int colourId = _boardModel.GetCell(cell);
                    _cellColourIds[index] = colourId;

                    // Re-derived from the model: a level's reinforced cells are on the board before the
                    // first repaint ever runs, so a full redraw has to pick their hit count up from the
                    // model rather than wait for a change event. Their look is the overlay below.
                    _cellHitCounts[index] = _boardModel.GetHitCount(cell);
                    ApplyCellColour(cell, colourId);

                    // Re-derived from the model, never carried over from the bookkeeping: a full
                    // repaint must be able to correct any icon state a cancelled fade or a rebuilt run
                    // left behind. The diamond colour is read before the icon paint, which reads it.
                    _cellSpecialKinds[index] = _boardModel.GetSpecialKind(cell);
                    _cellDiamondColourIds[index] = _boardModel.GetDiamondColourId(cell);
                    ApplyCellIcon(index, _cellSpecialKinds[index]);

                    // Re-derived from the model for the same reason the special kind just above is: a
                    // level's timer cells are on the board before the first repaint ever runs.
                    _cellTimerCountdowns[index] = _boardModel.GetTimerCountdown(cell);
                    ApplyTimerCountdown(index, _cellSpecialKinds[index], _cellTimerCountdowns[index]);

                    // Re-derived from the model for the same reason: a level's ice sockets are marked
                    // before the first repaint ever runs, and a socket is empty, so no cell-change
                    // notification would ever carry its level (issue #433).
                    _cellIceLevels[index] = _boardModel.GetIceLevel(cell);
                    _cells[index].SetIceOverlay(_cellIceLevels[index], MAX_ICE_LEVEL);

                    // Re-derived from the model for the same reason: a level's locked and reinforced
                    // cells are seeded before the first repaint ever runs (issues #434, #438).
                    ApplyOverlay(cell, index);

                    _cells[index].SetAlpha(1f);
                }
            }
        }

        /// <summary>
        /// The skin overlay state of <paramref name="cell"/>, read back off the model: how many layers
        /// its skin still shows (0 when it wears none) and which skin. The ONE place that decides what
        /// <see cref="CellView.SetLockedOverlay"/> shows, for both mechanics that use that layer: a
        /// locked cell's layers are its threshold minus the neighbours counted (issue #434); a reinforced
        /// cell's are its hits left (issue #438), which map 1:1 onto the art's stages because
        /// <c>ReinforcedCellAuthoring.MAX_HIT_COUNT</c> is the stage count. A cell is never both, so the
        /// order of the two checks is a formality — but a single reader is not: two independent paths
        /// each calling <see cref="CellView.SetLockedOverlay"/> for the same cell would stomp each other,
        /// whichever ran last hiding an overlay the other had legitimately set.
        /// </summary>
        private void ReadOverlayState(GridPosition cell, out int stagesRemaining, out int skin)
        {
            if (_boardModel.IsLocked(cell))
            {
                stagesRemaining = Mathf.Max(
                    1, _boardModel.GetLockedThreshold(cell) - _boardModel.GetLockedProgressCount(cell));
                skin = _boardModel.GetLockedSkin(cell);
                return;
            }

            int hitCount = _boardModel.GetHitCount(cell);
            if (hitCount > 0)
            {
                stagesRemaining = hitCount;
                skin = _boardModel.GetReinforcedSkin(cell);
                return;
            }

            stagesRemaining = 0;
            skin = 0;
        }

        /// <summary>Reads <paramref name="cell"/>'s overlay state off the model, records it and shows it.
        /// Unconditional — <see cref="CellView.SetLockedOverlay"/> is idempotent — so every path that
        /// may have changed what the cell wears can call it without first working out whether it did.</summary>
        private void ApplyOverlay(GridPosition cell, int index)
        {
            ReadOverlayState(cell, out _cellLockedStages[index], out _cellLockedSkins[index]);
            _cells[index].SetLockedOverlay(_cellLockedSkins[index], _cellLockedStages[index]);
        }

        private void OnCellChanged(GridPosition cell, int colourId)
        {
            int index = CellIndex(cell);

            if (colourId != Board.EMPTY)
            {
                // A new piece always wins over an in-flight fade: invalidate it and draw opaque now.
                _cellGenerations[index]++;
                _cellPending[index] = false;
                _cellColourIds[index] = colourId;

                // Read back for the reason the special kind below is: this is also the notification
                // raised when a reinforced cell is seeded, and the hit count's own notification follows.
                _cellHitCounts[index] = _boardModel.GetHitCount(cell);
                ApplyCellColour(cell, colourId);

                // And its skin overlay is applied here, not left to that following notification: the
                // hit count was just recorded, so OnHitCountChanged will see nothing new and return
                // early, and a freshly seeded reinforced cell would stand bare until its first real
                // hit (issue #438). A locked cell never needed this — its LockedCellChanged is not
                // gated on a payload — but the one reader serves both, so it costs nothing to share.
                ApplyOverlay(cell, index);

                // Read back rather than assumed empty: this is also the notification a spawner raises
                // when it occupies the cell it is about to tag, and the tag's own notification follows.
                // OccupyDiamond writes the gem colour before raising this, so it is read here too.
                _cellSpecialKinds[index] = _boardModel.GetSpecialKind(cell);
                _cellDiamondColourIds[index] = _boardModel.GetDiamondColourId(cell);
                ApplyCellIcon(index, _cellSpecialKinds[index]);

                // Read back for the same reason: this is also the notification OccupyTimer raises when
                // a timer cell is seeded, and the countdown's own notification follows.
                _cellTimerCountdowns[index] = _boardModel.GetTimerCountdown(cell);
                ApplyTimerCountdown(index, _cellSpecialKinds[index], _cellTimerCountdowns[index]);

                _cells[index].SetAlpha(1f);
                return;
            }

            // NotifyCleared raises this twice for a row/column intersection cell — the first
            // notification owns the pre-clear colour, the second must not overwrite it.
            if (_cellPending[index])
            {
                return;
            }

            // Hold the pre-clear look on screen; the fade started by OnLinesCleared owns it from
            // here. If no LinesClearedMessage follows (ClearAll), OnRunStarted flushes it.
            _cellPending[index] = true;
            _pendingColourIds[index] = _cellColourIds[index];
            _pendingGenerations[index] = ++_cellGenerations[index];
            _cellColourIds[index] = Board.EMPTY;

            // The icon is left on screen and fades with the block it belongs to; the settled state is
            // already "no kind", because Board.Clear resets a destroyed cell's kind with its colour —
            // and its gem colour with it.
            _cellSpecialKinds[index] = SpecialCellKind.None;
            _cellDiamondColourIds[index] = TrayModel.NO_DIAMOND;

            // And so is its hit count: a cell only ever reaches "empty" by being destroyed, which is
            // the last hit by definition. This is why a damaged cell needs a signal of its own but a
            // destroyed one does not.
            _cellHitCounts[index] = 0;

            // And so is its skin overlay, dropped now rather than faded with the block: Board.Clear
            // reset the reinforcement and the lock with the colour, and no later signal is guaranteed
            // to say so — NotifyHitCountsRefreshed skips a cell at 0, and not every resolution path
            // re-announces lock state — while the fade's closing SetAlpha(1) would bring a still-shown
            // plate back over an empty cell (issue #438 AC6). A lock a Bomb destroyed outright loses
            // its plate the same instant, which is exactly when OnLockedCellChanged dropped it before.
            _cellLockedStages[index] = 0;
            _cellLockedSkins[index] = 0;
            _cells[index].SetLockedOverlay(0, 0);

            // A cleared timer cell stops counting down immediately (issue #307 AC3/AC6a) — unlike the
            // icon, the number does not fade with the block; it simply has nothing left to count for.
            _cellTimerCountdowns[index] = 0;
            _cells[index].ClearTimerCountdown();
        }

        /// <summary>A reinforced cell survived a clear and is closer to breaking. Only its skin overlay
        /// changes — one layer fewer, the same block in the same place (issue #438) — so this refreshes
        /// that and nothing else. Idempotent: the model re-announces every standing count after each
        /// resolution, so an unchanged cell returns early.</summary>
        private void OnHitCountChanged(GridPosition cell, int hitCount)
        {
            int index = CellIndex(cell);
            if (_cellHitCounts[index] == hitCount)
            {
                return;
            }

            _cellHitCounts[index] = hitCount;
            ApplyOverlay(cell, index);
        }

        /// <summary>An ice socket's level may have changed (issue #433) — thinner after the block on it
        /// was destroyed, 0 once fully melted, or freshly marked at run start. Only the overlay changes:
        /// the cell's occupancy, colour and fade are all somebody else's, and the overlay is deliberately
        /// left alone by every other path so the ice survives the block above it emptying.</summary>
        private void OnTargetIceLevelChanged(GridPosition cell, int iceLevel)
        {
            int index = CellIndex(cell);
            if (_cellIceLevels[index] == iceLevel)
            {
                return;
            }

            _cellIceLevels[index] = iceLevel;
            _cells[index].SetIceOverlay(iceLevel, MAX_ICE_LEVEL);
        }

        /// <summary>
        /// A locked cell's state may have changed (issue #434): freshly seeded, one more distinct
        /// neighbour counted (same block, one layer fewer on its skin), or unlocked. Idempotent — the
        /// model re-announces every position after each resolution, so an unchanged cell returns early.
        /// <para>
        /// An unlock is the one case that repaints more than the overlay. The cell went EMPTY inside
        /// <c>Board.TryDamage</c> as a neighbour of a destroyed cell, never as part of a cleared line, so
        /// no <see cref="OnCellChanged"/> fade was ever started for it — and none should be: an unlock is
        /// a pure state change, not a destruction (AC8), so the cell simply snaps to its ordinary empty
        /// look, read back off the model. A cell that IS mid-fade (a lock a Bomb destroyed outright,
        /// announced through <see cref="OnCellChanged"/> a moment earlier) keeps its fade; that path
        /// already dropped the overlay with the block, so this sees nothing left to change.
        /// </para>
        /// <para>
        /// Since issue #438 the state read here covers a reinforced cell's overlay too (see
        /// <see cref="ReadOverlayState"/>), so the post-resolution re-announce also refreshes those —
        /// harmlessly, as <see cref="OnHitCountChanged"/> will normally have done it already.
        /// </para>
        /// </summary>
        private void OnLockedCellChanged(GridPosition cell)
        {
            int index = CellIndex(cell);
            ReadOverlayState(cell, out int stagesRemaining, out int skin);
            if (_cellLockedStages[index] == stagesRemaining && _cellLockedSkins[index] == skin)
            {
                return;
            }

            bool unlocked = _cellLockedStages[index] > 0 && stagesRemaining == 0;
            _cellLockedStages[index] = stagesRemaining;
            _cellLockedSkins[index] = skin;
            _cells[index].SetLockedOverlay(skin, stagesRemaining);

            if (!unlocked || _cellPending[index])
            {
                return;
            }

            _cellGenerations[index]++;
            _cellColourIds[index] = _boardModel.GetCell(cell);
            _cellHitCounts[index] = _boardModel.GetHitCount(cell);
            ApplyCellColour(cell, _cellColourIds[index]);

            _cellSpecialKinds[index] = _boardModel.GetSpecialKind(cell);
            _cellDiamondColourIds[index] = _boardModel.GetDiamondColourId(cell);
            ApplyCellIcon(index, _cellSpecialKinds[index]);

            _cellTimerCountdowns[index] = _boardModel.GetTimerCountdown(cell);
            ApplyTimerCountdown(index, _cellSpecialKinds[index], _cellTimerCountdowns[index]);

            _cells[index].SetAlpha(1f);
        }

        /// <summary>A timer cell survived a placement and ticked down by one (issue #307 AC6a). Only the
        /// countdown number changes — it is the same block in the same place.</summary>
        private void OnTimerCountdownChanged(GridPosition cell, int countdown)
        {
            int index = CellIndex(cell);
            if (_cellTimerCountdowns[index] == countdown)
            {
                return;
            }

            _cellTimerCountdowns[index] = countdown;
            _cells[index].SetTimerCountdown(countdown);
        }

        /// <summary>A timer cell's countdown reached 0 and converted to an ordinary cell (issue #307
        /// AC4/AC6a): the block stays exactly as it was, so only the countdown number stops.</summary>
        private void OnTimerCellExpired(GridPosition cell)
        {
            int index = CellIndex(cell);
            _cellTimerCountdowns[index] = 0;
            _cells[index].ClearTimerCountdown();
        }

        /// <summary>Shows or hides <paramref name="index"/>'s countdown number to match
        /// <paramref name="kind"/>/<paramref name="countdown"/> — the one place "should this cell show a
        /// number right now" is decided, shared by every repaint path (issue #307 AC6a).</summary>
        private void ApplyTimerCountdown(int index, SpecialCellKind kind, int countdown)
        {
            if (kind == SpecialCellKind.Timer && countdown > 0)
            {
                _cells[index].SetTimerCountdown(countdown);
                return;
            }

            _cells[index].ClearTimerCountdown();
        }

        private void OnLinesCleared(LinesClearedMessage message)
        {
            Array.Clear(_rowClearMask, 0, _rowClearMask.Length);
            Array.Clear(_columnClearMask, 0, _columnClearMask.Length);

            IReadOnlyList<int> rows = message.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                int y = rows[i];
                if (y >= 0 && y < _height)
                {
                    _rowClearMask[y] = true;
                }
            }

            IReadOnlyList<int> columns = message.Columns;
            for (int i = 0; i < columns.Count; i++)
            {
                int x = columns[i];
                if (x >= 0 && x < _width)
                {
                    _columnClearMask[x] = true;
                }
            }

            // Issue #328: stagger a single cleared line's cells left-to-right (row) or bottom-to-top
            // (column, Y increases upward — see the cell layout in InitializeBoard) instead of fading
            // all 8 at once. Only exactly one line clearing qualifies — two or more must keep the
            // existing simultaneous fade (AC3), which is also why this can never coincide with the
            // intersection flash below: a lone row leaves every column mask false, so inRow && inColumn
            // is always false here.
            bool staggerSingleLine = message.LineCount == 1;
            int staggerRowY = staggerSingleLine && rows.Count == 1 ? rows[0] : -1;
            int staggerColumnX = staggerSingleLine && columns.Count == 1 ? columns[0] : -1;
            float rowStaggerStep = _width > 1 ? Mathf.Max(0f, _singleLineStaggerDuration) / (_width - 1) : 0f;
            float columnStaggerStep = _height > 1 ? Mathf.Max(0f, _singleLineStaggerDuration) / (_height - 1) : 0f;

            // Issue #331: a single-line clear also gets one of at least two visual treatments, chosen
            // uniformly at random ONCE for the whole clear event (never per cell) so every cell in the
            // line shows the same look. Two or more simultaneous line clears keep the plain fade, same
            // gate the stagger above uses — this is deliberately the one random call in the method, not
            // one per cell.
            SingleLineClearEffect? lineClearEffect = staggerSingleLine ? PickRandomSingleLineClearEffect() : null;

            // Issue #350 AC2: a simultaneous multi-line clear has no stagger, so left alone its total
            // on-screen time is just _fadeDuration — the single fastest case of all, even though it
            // clears more of the board than a single line does. Floor its fade to a single-line clear's
            // own worst case (last staggered cell's start delay plus its own fade) so a multi-line
            // clear is never the least-visible one.
            float multiLineFadeDurationOverride = staggerSingleLine
                ? 0f
                : Mathf.Max(0f, _singleLineStaggerDuration) + Mathf.Max(0.01f, _fadeDuration);

            // Issue #330 AC1: exactly one row and one column clearing together is a "cross-clear" — an
            // additional combo flash plays at their intersection, ON TOP OF (never instead of) whatever
            // LineClearBurstView's own tier already plays for this same message: that view is its own
            // subscriber to LinesClearedMessage and is untouched by this. AC3's negative case (a
            // cross-clear that spawns no special cell) needs nothing extra here — this reads only
            // Rows/Columns, never a spawn message, so it always fires on the shape of the clear alone.
            if (rows.Count == 1 && columns.Count == 1)
            {
                PlayCrossClearComboAsync(new GridPosition(columns[0], rows[0])).Forget();
            }

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    bool inRow = _rowClearMask[y];
                    bool inColumn = _columnClearMask[x];
                    if (!inRow && !inColumn)
                    {
                        continue;
                    }

                    int index = (y * _width) + x;
                    if (!_cellPending[index] || _pendingGenerations[index] != _cellGenerations[index])
                    {
                        continue;
                    }

                    float startDelay = 0f;
                    if (staggerRowY == y)
                    {
                        startDelay = x * rowStaggerStep;
                    }
                    else if (staggerColumnX == x)
                    {
                        startDelay = y * columnStaggerStep;
                    }

                    PlayClearAsync(
                            new GridPosition(x, y), index, _cellGenerations[index], inRow && inColumn, startDelay,
                            lineClearEffect, multiLineFadeDurationOverride)
                        .Forget();
                }
            }
        }

        /// <summary>
        /// Claims the cells a power-up emptied, exactly as <see cref="OnLinesCleared"/> claims the
        /// ones a placement emptied.
        /// <para>
        /// <see cref="OnCellChanged"/> deliberately holds an emptied cell's pre-clear look on screen
        /// and waits for whoever cleared it to start the fade. A power-up publishes no
        /// <see cref="LinesClearedMessage"/> — its clear is a region, not a set of lines — so without
        /// this the cells it emptied would sit showing their old colour until the next run started.
        /// </para>
        /// <para>
        /// It sweeps every pending cell rather than the message's own, because the message reports how
        /// many cells cleared, not which. A placement's own pending cells stay claimed by their
        /// original <see cref="OnLinesCleared"/> fade — <c>_cellPending</c> stays true for that fade's
        /// whole duration, not just its first frame — so a power-up applied within that window will
        /// also catch them and restart their fade from full opacity. The <see cref="_cellGenerations"/>
        /// bump above still guarantees a single final writer either way; this is a cosmetic re-fade in
        /// the rare case of overlap, not a correctness bug. No intersection flash — that is a
        /// two-full-lines concept, and a power-up clears a region.
        /// </para>
        /// <para>
        /// Issue #331 AC2: <see cref="PowerUpKind.RowClear"/> and <see cref="PowerUpKind.ColumnClear"/>
        /// are the power-up kinds whose clear is a single row/column — the same shape of event a
        /// placement's own single-line clear is — so they get the same per-application random effect
        /// choice and the same left-to-right/bottom-to-top stagger <see cref="OnLinesCleared"/> gives a
        /// placement-triggered single-line clear. Every other kind (Bomb, Joker, Color Cleanser) clears
        /// a region rather than a line and keeps the plain simultaneous sweep it always had.
        /// </para>
        /// </summary>
        private void OnPowerUpApplied(PowerUpAppliedMessage message)
        {
            if (message.Kind == PowerUpKind.ColorCleanser)
            {
                // Fired before the sweep below starts fading the targeted cells, so every beam is
                // already growing while the cells it points at are still fully opaque (AC1: "before/
                // while cells are cleared") rather than appearing after they have already vanished.
                PlayColorCleanserBeamsAsync(message.TargetCell, message.ClearedCellPositions).Forget();
            }

            bool isSingleLineClear =
                message.Kind == PowerUpKind.RowClear || message.Kind == PowerUpKind.ColumnClear;
            SingleLineClearEffect? effect = isSingleLineClear ? PickRandomSingleLineClearEffect() : null;

            SweepPendingCells(message.ClearedCellCount, isSingleLineClear ? message.Kind : (PowerUpKind?)null, effect);
        }

        /// <summary>Starts a region-clear fade on every cell still waiting for one. Shared by the
        /// power-up and explosive-core paths, which differ only in what emptied the cells.
        /// <para>
        /// <paramref name="singleLineKind"/> and <paramref name="effect"/> exist for issue #331 AC2's
        /// Row Clear/Column Clear path only — every other caller leaves both null and gets exactly the
        /// unstaggered, un-effected sweep this always did. When set, every swept cell is staggered by
        /// its own x (<see cref="PowerUpKind.RowClear"/>) or y (<see cref="PowerUpKind.ColumnClear"/>),
        /// mirroring <see cref="OnLinesCleared"/>'s stagger arithmetic exactly — a Row/Column Clear
        /// power-up only ever has the cells of its own single target line pending at once, so sweeping
        /// "every pending cell" this way is equivalent to staggering that one line.
        /// </para>
        /// </summary>
        private void SweepPendingCells(
            int clearedCellCount, PowerUpKind? singleLineKind = null, SingleLineClearEffect? effect = null)
        {
            if (clearedCellCount <= 0 || _cells == null)
            {
                return;
            }

            float rowStaggerStep = _width > 1 ? Mathf.Max(0f, _singleLineStaggerDuration) / (_width - 1) : 0f;
            float columnStaggerStep = _height > 1 ? Mathf.Max(0f, _singleLineStaggerDuration) / (_height - 1) : 0f;

            for (int index = 0; index < _cells.Length; index++)
            {
                if (!_cellPending[index])
                {
                    continue;
                }

                // Bumped first so this fade is the cell's only owner. A placement's fade can still be
                // in flight here (it holds the cell pending for its whole duration), and two fades
                // writing one cell's alpha would fight; the bump makes the older one bail on its next
                // tick instead.
                _cellGenerations[index]++;

                var cell = new GridPosition(index % _width, index / _width);

                float startDelay = 0f;
                if (singleLineKind == PowerUpKind.RowClear)
                {
                    startDelay = cell.X * rowStaggerStep;
                }
                else if (singleLineKind == PowerUpKind.ColumnClear)
                {
                    startDelay = cell.Y * columnStaggerStep;
                }

                PlayClearAsync(cell, index, _cellGenerations[index], false, startDelay, effect).Forget();
            }
        }

        /// <summary>Safety net for <c>BoardModel.ClearAll</c>, which empties cells without ever
        /// publishing a <see cref="LinesClearedMessage"/> to claim them.</summary>
        private void OnRunStarted(RunStartedMessage message) => RedrawAll();

        /// <summary>
        /// Claims every cell still waiting for a fade, exactly as <see cref="OnPowerUpApplied"/> does.
        /// The one caller today is the development paint tool (<c>DebugCheatInputView</c>): its single
        /// cell erases go straight through <c>BoardModel.Clear</c>, the same low-level write
        /// <c>BoardModel.ClearAll</c> makes, so they leave the same unclaimed pending cell —
        /// <see cref="OnRunStarted"/>'s <c>RedrawAll</c> would flush it, but nothing here should ever
        /// trigger a run reset just to repaint one erased cell.
        /// </summary>
        internal void ForceSweepPendingCells() => SweepPendingCells(1);

        /// <summary>A cell became special — a placement that closed a row and a column spawning an
        /// explosive core on their intersection, or a combo streak converting a block into a laser. The
        /// icon is drawn per kind, not per spawn rule, so a new kind needs nothing here. Losing a kind
        /// needs no counterpart either: a cell only
        /// loses one by being destroyed, which already arrives through <see cref="OnCellChanged"/>.</summary>
        private void OnSpecialKindChanged(GridPosition cell, SpecialCellKind kind)
        {
            int index = CellIndex(cell);
            _cellSpecialKinds[index] = kind;

            // Read back rather than carried: a vortex hand-off can move a diamond kind onto a cell
            // whose gem colour this view has never seen (issue #395).
            _cellDiamondColourIds[index] = kind == SpecialCellKind.Diamond
                ? _boardModel.GetDiamondColourId(cell)
                : TrayModel.NO_DIAMOND;
            ApplyCellIcon(index, kind);
        }

        /// <summary>
        /// Issue #330 AC2: a special cell that was just genuinely created pops its icon in from a small
        /// scale instead of sitting at the instant size <see cref="OnSpecialKindChanged"/> just drew it
        /// at. Scoped strictly to <see cref="SpecialCellSpawnedMessage"/> rather than to every
        /// <see cref="OnSpecialKindChanged"/> call — that message is published "never on a kind being
        /// cleared or consumed, only on genuine creation" (see its own doc comment), which is exactly the
        /// "born" moment AC2 asks for, and it is the only thing that fires for every spawn path this
        /// message covers (an explosive core / vortex / chain lightning / score gem, wherever they land)
        /// without also firing for a level-authored Timer cell's start-of-run seeding or a vortex carrying
        /// an already-existing kind to a new position — both of which raise
        /// <see cref="BoardModel.SpecialKindChanged"/> too, but are not a birth AC2 is about.
        /// <para>
        /// Runs synchronously, in the same call stack as the <see cref="OnSpecialKindChanged"/> call that
        /// always precedes it here (MessagePipe's default publisher is synchronous, and
        /// <c>BoardSystem</c> always calls <c>BoardModel.SetSpecialKind</c> before publishing this
        /// message) — so the icon's scale is pulled back down before Unity ever renders the
        /// instantly-drawn frame <see cref="OnSpecialKindChanged"/> left it at, and the player only ever
        /// sees the pop-in, never a flash of the resting icon first.
        /// </para>
        /// </summary>
        private void OnSpecialCellSpawned(SpecialCellSpawnedMessage message)
        {
            if (_cells == null || !IsPlayableCell(message.Position))
            {
                return;
            }

            int index = CellIndex(message.Position);
            RectTransform iconTransform = _cells[index].SpecialIconTransform;
            if (iconTransform == null)
            {
                return;
            }

            PlaySpecialSpawnPopAsync(iconTransform, index, _cellGenerations[index]).Forget();
        }

        /// <summary>The on-screen anchor a special cell's icon sits at, for a flight animation (e.g.
        /// <see cref="PowerUpGrantAnimationView"/>'s centre-screen-to-destination flight) to land on.
        /// Null for an out-of-range position or before the board has laid out its cells.</summary>
        internal RectTransform GetCellIconRectTransform(GridPosition position)
        {
            if (_cells == null || !IsPlayableCell(position))
            {
                return null;
            }

            return _cells[CellIndex(position)].SpecialIconTransform;
        }

        private void OnEmptyCellBonusCounting(EmptyCellBonusCountingMessage message)
            => PlayEmptyCellBonusCountingAsync(message.EmptyCellCount).Forget();

        /// <summary>
        /// The level-completion empty-cell bonus reveal (issue #424): writes 1, 2, 3… over the board's
        /// empty cells one after another, in board index order, holds the finished count for a beat,
        /// clears the numbers and then publishes <see cref="EmptyCellBonusCountingCompletedMessage"/>
        /// so <c>LevelProgressionSystem</c> can end the run and bring up the result screen.
        /// <para>
        /// Counts what this view actually shows as empty (<see cref="_cellColourIds"/>, minus holes)
        /// rather than trusting <paramref name="totalCount"/> blindly, so the numbers on screen can
        /// never disagree with the board they are written on. The per-cell delay shrinks with the count,
        /// so a handful of cells still feels deliberate while a whole empty board finishes in about a
        /// second and a half. The completion is published even if this view is destroyed mid-count —
        /// the system has its own timeout, but there is no reason to make it wait for one.
        /// </para>
        /// </summary>
        private async UniTaskVoid PlayEmptyCellBonusCountingAsync(int totalCount)
        {
            if (totalCount <= 0 || _cells == null)
            {
                _emptyCellBonusCountingCompletedPublisher.Publish(new EmptyCellBonusCountingCompletedMessage());
                return;
            }

            var emptyIndices = new List<int>(totalCount);
            for (int index = 0; index < _cells.Length; index++)
            {
                if (!_holeMask[index] && _cellColourIds[index] == Board.EMPTY)
                {
                    emptyIndices.Add(index);
                }
            }

            int count = emptyIndices.Count;
            float perCellDelay = Mathf.Clamp(1.6f / Mathf.Max(1, count), 0.03f, 0.12f);
            TimeSpan perCellWait = TimeSpan.FromSeconds(perCellDelay);

            try
            {
                for (int revealIndex = 0; revealIndex < count; revealIndex++)
                {
                    _cells[emptyIndices[revealIndex]].SetBonusNumber(revealIndex + 1);
                    await UniTask.Delay(perCellWait, cancellationToken: _destroyToken);
                }

                // A short hold so the final total is readable before the numbers go.
                if (count > 0)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(0.4f), cancellationToken: _destroyToken);
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-count — the numbers are going with it.
            }
            finally
            {
                if (!_isDestroyed)
                {
                    for (int revealIndex = 0; revealIndex < count; revealIndex++)
                    {
                        _cells[emptyIndices[revealIndex]].ClearBonusNumber();
                    }
                }

                _emptyCellBonusCountingCompletedPublisher.Publish(new EmptyCellBonusCountingCompletedMessage());
            }
        }

        /// <summary>
        /// A special cell's birth pop: <paramref name="iconTransform"/> starts at
        /// <see cref="_specialSpawnHeroScale"/> — roughly a 4x4-cell icon — spinning
        /// <see cref="_specialSpawnSpinDegrees"/> around itself as it shrinks down past
        /// <see cref="_specialSpawnLandingOvershootScale"/> and settles at 1, its resting size. The cell
        /// is brought to the front of the shared <see cref="_cellLayerRoot"/> sibling order first (see
        /// <see cref="BuildCellLayer"/>) so the oversized icon draws over every neighbouring cell rather
        /// than being clipped behind whichever one happens to sit later in that order. Watches
        /// <see cref="_cellGenerations"/> without owning it (never bumps it): any destructive write to
        /// this cell — a new placement, a clear — bumps that counter on its own and this simply bails,
        /// leaving whatever repaint path caused the bump to settle the icon's final look, scale and
        /// rotation.
        /// </summary>
        private async UniTaskVoid PlaySpecialSpawnPopAsync(RectTransform iconTransform, int index, int generation)
        {
            const float ShrinkFraction = 0.7f;

            float heroScale = Mathf.Max(1f, _specialSpawnHeroScale);
            float landingOvershoot = Mathf.Max(1f, _specialSpawnLandingOvershootScale);
            float spinDegrees = _specialSpawnSpinDegrees;
            float duration = Mathf.Max(0.01f, _specialSpawnPopDuration);

            _cells[index].transform.SetAsLastSibling();

            iconTransform.localScale = Vector3.one * heroScale;
            iconTransform.localRotation = Quaternion.identity;

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (_cellGenerations[index] != generation)
                    {
                        return;
                    }

                    float t = elapsed / duration;
                    float scale = t < ShrinkFraction
                        ? Mathf.Lerp(heroScale, landingOvershoot, EaseOutCubic(t / ShrinkFraction))
                        : Mathf.Lerp(landingOvershoot, 1f, (t - ShrinkFraction) / (1f - ShrinkFraction));

                    iconTransform.localScale = Vector3.one * scale;
                    iconTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, spinDegrees, EaseOutCubic(t)));

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-pop — the icon is going with it.
                return;
            }

            if (_isDestroyed || _cellGenerations[index] != generation)
            {
                return;
            }

            iconTransform.localScale = Vector3.one;
            iconTransform.localRotation = Quaternion.identity;
        }

        /// <summary>An explosive core detonated and its bonus wipe emptied the line at right angles to
        /// whatever destroyed it (issue #398). Claimed exactly as a laser's wipe is, and for the same
        /// reason: a wipe does not need the line to be full, so no <see cref="LinesClearedMessage"/>
        /// follows it — the same line-sweep VFX path <see cref="OnLaserFired"/> plays.</summary>
        private void OnExplosiveCoreDetonated(ExplosiveCoreDetonatedMessage message)
            => SweepPendingCells(message.WipedCellCount);

        /// <summary>A laser wiped a line. Claimed exactly as a blast's cells are, and for the same
        /// reason: a wipe does not need the line to be full, so no <see cref="LinesClearedMessage"/>
        /// follows it.</summary>
        private void OnLaserFired(LaserFiredMessage message) => SweepPendingCells(message.WipedCellCount);

        /// <summary>A piercing rocket wiped its row and its column. Claimed exactly as a laser's wipe
        /// is, and for exactly the same reason: the wipe does not need either line to be full, so no
        /// <see cref="LinesClearedMessage"/> follows it and without this sweep every cell it emptied
        /// would sit showing its pre-wipe block — a board the model has already emptied but the player
        /// still sees as full, which is issue #354's "nothing happened, then the row would not
        /// clear".</summary>
        private void OnPiercingRocketFired(PiercingRocketFiredMessage message)
            => SweepPendingCells(message.WipedCellCount);

        /// <summary>A chain lightning arced across the board. Claimed exactly as a blast's cells are:
        /// the cells it emptied are scattered rather than lined up, so no <see cref="LinesClearedMessage"/>
        /// describes them and without this they would sit showing their old colour. The fade <em>is</em>
        /// the zap — every struck cell flashes and dies in the same beat, which is what a strike looks
        /// like — so there is no second animation to invent here.</summary>
        private void OnChainLightningTriggered(ChainLightningTriggeredMessage message)
            => SweepPendingCells(message.VaporizedCellCount);

        /// <summary>
        /// A vortex reclaimed an island, handed its tag off, or both (issue #349). Deliberately not a
        /// sweep: nothing was destroyed, so nothing may fade — a fill creates a cell and a hand-off only
        /// relabels one.
        /// <para>
        /// The model has already announced every filled cell's new colour and the hand-off's new icon by
        /// the time this runs (<see cref="BoardModel.NotifyIslandFilled"/>/
        /// <see cref="BoardModel.NotifyVortexHandedOff"/> both fire synchronously before this message
        /// publishes), so every cell involved is already showing its final look and the only thing left
        /// is the pop that makes the change read as an event rather than a silent repaint.
        /// </para>
        /// </summary>
        private void OnVortexIslandFilled(VortexIslandFilledMessage message)
        {
            if (_cells == null)
            {
                return;
            }

            IReadOnlyList<GridPosition> filledCells = message.FilledCells;
            if (filledCells != null)
            {
                for (int i = 0; i < filledCells.Count; i++)
                {
                    GridPosition cell = filledCells[i];
                    if (!IsPlayableCell(cell))
                    {
                        continue;
                    }

                    int index = CellIndex(cell);

                    // Empty means a later cascade phase already cleared this fill — the completed-line
                    // fade already claimed it, and popping it in first would make a cell that is fading
                    // out grow while it does so.
                    if (_cellColourIds[index] == Board.EMPTY)
                    {
                        continue;
                    }

                    RectTransform blockTransform = _cells[index].BlockTransform;
                    if (blockTransform == null)
                    {
                        continue;
                    }

                    PlayIslandFillPopAsync(blockTransform, index, ++_cellGenerations[index]).Forget();
                }
            }

            IReadOnlyList<GridPosition> handOffTargets = message.HandOffTargets;
            if (handOffTargets != null)
            {
                for (int i = 0; i < handOffTargets.Count; i++)
                {
                    GridPosition cell = handOffTargets[i];
                    if (!IsPlayableCell(cell))
                    {
                        continue;
                    }

                    int index = CellIndex(cell);
                    RectTransform iconTransform = _cells[index].SpecialIconTransform;
                    if (iconTransform == null)
                    {
                        continue;
                    }

                    PlayVortexHandOffPopAsync(iconTransform, index, ++_cellGenerations[index]).Forget();
                }
            }
        }

        /// <summary>
        /// Scales the cell at <paramref name="index"/>'s block from <see cref="_islandFillStartScale"/>
        /// up past <see cref="_islandFillOvershootScale"/> and back to 1 — the same grow-then-settle
        /// shape <see cref="PlaySpecialSpawnPopAsync"/> uses, one layer down (the whole block rather than
        /// just its icon), and at its own, longer duration (issue #349 AC4: at least 0.4s, deliberately
        /// longer and more deliberate than the removed 0.18s pull). Watches
        /// <see cref="_cellGenerations"/> without owning it, exactly as <see cref="PlaySpecialSpawnPopAsync"/>
        /// does: any destructive write to this cell bumps that counter on its own and this simply bails,
        /// leaving whatever repaint path caused the bump to settle the block's final look and scale.
        /// </summary>
        private async UniTaskVoid PlayIslandFillPopAsync(RectTransform blockTransform, int index, int generation)
        {
            const float GrowFraction = 0.7f;

            float startScale = Mathf.Clamp(_islandFillStartScale, 0.001f, 1f);
            float overshootScale = Mathf.Max(1f, _islandFillOvershootScale);
            float duration = Mathf.Max(0.4f, _islandFillDuration);

            blockTransform.localScale = Vector3.one * startScale;

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (_cellGenerations[index] != generation)
                    {
                        return;
                    }

                    float t = elapsed / duration;
                    float scale = t < GrowFraction
                        ? Mathf.Lerp(startScale, overshootScale, EaseOutCubic(t / GrowFraction))
                        : Mathf.Lerp(overshootScale, 1f, (t - GrowFraction) / (1f - GrowFraction));

                    blockTransform.localScale = Vector3.one * scale;

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-pop — the block is going with it.
                return;
            }

            if (_isDestroyed || _cellGenerations[index] != generation)
            {
                return;
            }

            blockTransform.localScale = Vector3.one;
        }

        /// <summary>
        /// A hand-off's fresh icon pops in exactly as a genuinely spawned special cell's does — reusing
        /// <see cref="PlaySpecialSpawnPopAsync"/>'s shape at <see cref="_vortexHandOffPopDuration"/>
        /// rather than <see cref="_specialSpawnPopDuration"/>: a hand-off is not the "born" moment
        /// <see cref="SpecialCellSpawnedMessage"/> reports (see <see cref="OnSpecialCellSpawned"/>'s own
        /// remarks — the kind already existed, it just moved cells), so it is played from here rather
        /// than through that message, with its own duration so the two can be tuned independently.
        /// </summary>
        private async UniTaskVoid PlayVortexHandOffPopAsync(RectTransform iconTransform, int index, int generation)
        {
            const float GrowFraction = 0.7f;

            float startScale = Mathf.Clamp(_specialSpawnStartScale, 0.001f, 1f);
            float overshootScale = Mathf.Max(1f, _specialSpawnOvershootScale);
            float duration = Mathf.Max(0.01f, _vortexHandOffPopDuration);

            iconTransform.localScale = Vector3.one * startScale;

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (_cellGenerations[index] != generation)
                    {
                        return;
                    }

                    float t = elapsed / duration;
                    float scale = t < GrowFraction
                        ? Mathf.Lerp(startScale, overshootScale, EaseOutCubic(t / GrowFraction))
                        : Mathf.Lerp(overshootScale, 1f, (t - GrowFraction) / (1f - GrowFraction));

                    iconTransform.localScale = Vector3.one * scale;

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-pop — the icon is going with it.
                return;
            }

            if (_isDestroyed || _cellGenerations[index] != generation)
            {
                return;
            }

            iconTransform.localScale = Vector3.one;
        }

        /// <summary>Where one cell's rect sits in the grid — the same arithmetic
        /// <see cref="BuildCells"/> laid it out with, reusing the origin and pitch it recorded.</summary>
        private Vector2 CellAnchoredPosition(GridPosition cell)
            => new Vector2(_cellOriginX + (cell.X * _cellPitch), _cellOriginY + (cell.Y * _cellPitch));

        /// <summary>Pushes <see cref="_pendingHighlightMask"/> to the cells, touching only the ones
        /// whose state actually changed, and adopts it as the current mask.</summary>
        private void ApplyHighlightMask()
        {
            if (_cells == null)
            {
                return;
            }

            Color colour = _currentTheme != null ? _currentTheme.WouldClearHighlight : Color.clear;
            _hasHighlight = false;

            for (int index = 0; index < _highlightMask.Length; index++)
            {
                bool wanted = _pendingHighlightMask[index];
                _hasHighlight |= wanted;

                if (wanted == _highlightMask[index])
                {
                    continue;
                }

                _highlightMask[index] = wanted;

                if (wanted)
                {
                    _cells[index].SetHighlight(colour);
                }
                else
                {
                    _cells[index].ClearHighlight();
                }
            }
        }

        /// <summary>Re-tints the outlines already on screen after a theme switch.</summary>
        private void RepaintHighlight()
        {
            if (!_hasHighlight || _currentTheme == null)
            {
                return;
            }

            Color colour = _currentTheme.WouldClearHighlight;
            for (int index = 0; index < _highlightMask.Length; index++)
            {
                if (_highlightMask[index])
                {
                    _cells[index].SetHighlight(colour);
                }
            }
        }

        /// <summary>Invalidates any fade on a cell and restores it to full opacity.</summary>
        private void CancelFade(int index)
        {
            if (!_cellPending[index])
            {
                return;
            }

            _cellPending[index] = false;
            _cellGenerations[index]++;

            // The fade owned the icon that was still on screen; dropping the fade settles the cell on
            // its post-clear state, which has no kind.
            ApplyCellIcon(index, _cellSpecialKinds[index]);
            _cells[index].SetAlpha(1f);
        }

        /// <summary>
        /// Picks one of the pool of single-line clear looks uniformly at random. The one place
        /// <see cref="UnityEngine.Random"/> is touched for issue #331 — called exactly once per
        /// single-line clear event, by <see cref="OnLinesCleared"/> or <see cref="OnPowerUpApplied"/>,
        /// never per cell. Presentation-only randomness: nothing here is read by or fed back into
        /// Core/Gameplay, so it cannot affect score, timing gates or objective tracking (AC3), and
        /// Core/Gameplay's own tests never execute this method (AC4).
        /// </summary>
        private static SingleLineClearEffect PickRandomSingleLineClearEffect()
            => (SingleLineClearEffect)UnityEngine.Random.Range(0, 4);

        /// <summary>
        /// Plays one cell's stagger delay, optional intersection flash, and fade — the single code path
        /// every row/column clear animation runs through, whatever triggered it (an ordinary placement,
        /// a power-up-forced clear via <see cref="SweepPendingCells"/>, or a cross-clear combo's
        /// intersection cell). Issue #350: for exactly that reason, this is also the single place that
        /// holds <see cref="TimerRunSystem"/>'s clear-animation pause for the Timed-mode countdown — one
        /// reference-counted <c>true</c>/<c>false</c> pair per cell, so overlapping clears each hold
        /// their own slot and the clock resumes only once every animating cell has finished.
        /// </summary>
        private async UniTaskVoid PlayClearAsync(
            GridPosition cell, int index, int generation, bool isIntersection, float startDelay = 0f,
            SingleLineClearEffect? effect = null, float fadeDurationOverride = 0f)
        {
            CellView view = _cells[index];
            int colourId = _pendingColourIds[index];
            bool completed;

            _timerRunSystem.SetClearAnimationPlaying(true);
            try
            {
                try
                {
                    float delayElapsed = 0f;
                    while (delayElapsed < startDelay)
                    {
                        if (_cellGenerations[index] != generation)
                        {
                            return;
                        }

                        await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                        delayElapsed += Time.unscaledDeltaTime;
                    }

                    if (isIntersection)
                    {
                        float flashDuration = Mathf.Max(0.01f, _intersectionFlashDuration);
                        float flashElapsed = 0f;

                        while (flashElapsed < flashDuration)
                        {
                            if (_cellGenerations[index] != generation)
                            {
                                return;
                            }

                            // Triangle ramp: colour goes to white and back over the flash window.
                            float blend = 1f - Mathf.Abs(((flashElapsed / flashDuration) * 2f) - 1f);
                            ApplyClearTint(view, colourId, blend);

                            await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                            flashElapsed += Time.unscaledDeltaTime;
                        }

                        if (_cellGenerations[index] != generation)
                        {
                            return;
                        }

                        ApplyClearTint(view, colourId, 0f);
                    }

                    // Issue #331: WHAT a cell does once its stagger delay (and, on the rare intersection
                    // cell, its flash) is done varies by the effect chosen once for this whole clear event.
                    // No effect (multi-line clears, and every power-up kind that is not a single line) keeps
                    // exactly the plain fade this method always played.
                    switch (effect)
                    {
                        case SingleLineClearEffect.Shatter:
                            completed = await PlayShatterFadeAsync(view, cell, index, generation, colourId);
                            break;
                        case SingleLineClearEffect.Burn:
                            completed = await PlayBurnFadeAsync(view, cell, index, generation, colourId);
                            break;
                        case SingleLineClearEffect.FlyToCorner:
                            completed = await PlayFlyToCornerFadeAsync(view, cell, index, generation);
                            break;
                        case SingleLineClearEffect.Drop:
                            completed = await PlayDropFadeAsync(view, index, generation);
                            break;
                        default:
                            completed = await PlayPlainFadeAsync(view, index, generation, fadeDurationOverride);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // The board view was destroyed mid-fade — nothing left to restore.
                    return;
                }

                if (!completed)
                {
                    return;
                }

                // Only settle the cell when nothing newer claimed it, otherwise this would stomp the
                // colour of a piece that was placed here while the fade was running.
                if (_isDestroyed || _cellGenerations[index] != generation)
                {
                    return;
                }

                _cellPending[index] = false;
                ApplyCellColour(cell, Board.EMPTY);
                ApplyCellIcon(index, _cellSpecialKinds[index]);
                view.SetAlpha(1f);
            }
            finally
            {
                _timerRunSystem.SetClearAnimationPlaying(false);
            }
        }

        /// <summary>The plain opacity fade every clear played before issue #331, and what every clear
        /// with no chosen effect (multi-line clears, region-clearing power-ups) still plays. Returns
        /// false the instant a newer write claims the cell mid-fade, so the caller knows not to settle
        /// it.
        /// <para>
        /// <paramref name="fadeDurationOverride"/> is issue #350 AC2's multi-line clear floor: a value
        /// greater than zero replaces <see cref="_fadeDuration"/> for this one call so a multi-line
        /// clear — which never staggers — lasts at least as long on screen as a single-line clear's own
        /// worst case. Every other caller passes the default 0, which keeps <see cref="_fadeDuration"/>.
        /// </para>
        /// </summary>
        private async UniTask<bool> PlayPlainFadeAsync(
            CellView view, int index, int generation, float fadeDurationOverride = 0f)
        {
            float fadeDuration = fadeDurationOverride > 0f
                ? fadeDurationOverride
                : Mathf.Max(0.01f, _fadeDuration);
            float fadeElapsed = 0f;

            while (fadeElapsed < fadeDuration)
            {
                if (_cellGenerations[index] != generation)
                {
                    return false;
                }

                view.SetAlpha(1f - (fadeElapsed / fadeDuration));

                await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                fadeElapsed += Time.unscaledDeltaTime;
            }

            return true;
        }

        /// <summary>
        /// <see cref="SingleLineClearEffect.Shatter"/>: the cell plays its ordinary opacity fade while
        /// a handful of shards — <see cref="UiSpriteFactory.RoundedSquare"/> tinted the cell's own fill
        /// — fly outward from its centre and fade on their own, independent, fire-and-forget timeline
        /// (<see cref="SpawnShatterShardsAsync"/>). The cell's own fade is the timeline the caller
        /// tracks for cancellability; the shards are decorative and never write back to cell state.
        /// </summary>
        private async UniTask<bool> PlayShatterFadeAsync(
            CellView view, GridPosition cell, int index, int generation, int colourId)
        {
            SpawnShatterShardsAsync(cell, colourId).Forget();
            return await PlayPlainFadeAsync(view, index, generation);
        }

        /// <summary>
        /// <see cref="SingleLineClearEffect.Burn"/>: the cell's fill blends towards
        /// <see cref="EmberTint"/> as it fades — reusing <see cref="ApplyClearTint"/>'s blend, the same
        /// idiom the intersection flash already uses — while a couple of ember particles drift upward
        /// off it on their own fire-and-forget timeline (<see cref="SpawnEmberParticlesAsync"/>).
        /// </summary>
        private async UniTask<bool> PlayBurnFadeAsync(
            CellView view, GridPosition cell, int index, int generation, int colourId)
        {
            SpawnEmberParticlesAsync(cell).Forget();

            float fadeDuration = Mathf.Max(0.01f, _fadeDuration);
            float fadeElapsed = 0f;

            while (fadeElapsed < fadeDuration)
            {
                if (_cellGenerations[index] != generation)
                {
                    return false;
                }

                float t = fadeElapsed / fadeDuration;
                ApplyClearTint(view, colourId, t, EmberTint);
                view.SetAlpha(1f - t);

                await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                fadeElapsed += Time.unscaledDeltaTime;
            }

            return true;
        }

        /// <summary>
        /// <see cref="SingleLineClearEffect.FlyToCorner"/>: the cell's own rect shrinks and slides
        /// towards whichever board corner it is already closest to (its quadrant relative to the
        /// grid's centre), fading as it goes, instead of fading in place. The rect is always restored
        /// to its layout position and scale before this returns — on early cancellation as much as on
        /// a full run — because the cell is reused by whatever the model puts there next and nothing
        /// else resets a cell's transform (unlike <see cref="PlayIslandFillPopAsync"/>'s scale, which
        /// always settles back to 1 on its own exit paths).
        /// </summary>
        private async UniTask<bool> PlayFlyToCornerFadeAsync(
            CellView view, GridPosition cell, int index, int generation)
        {
            var rect = (RectTransform)view.transform;
            Vector2 start = CellAnchoredPosition(cell);
            float halfExtent = _gridExtent * 0.5f;
            var target = new Vector2(
                start.x >= 0f ? halfExtent : -halfExtent, start.y >= 0f ? halfExtent : -halfExtent);
            float minScale = Mathf.Clamp01(_flyToCornerMinScale);

            float fadeDuration = Mathf.Max(0.01f, _fadeDuration);
            float fadeElapsed = 0f;
            bool completed = true;

            try
            {
                while (fadeElapsed < fadeDuration)
                {
                    if (_cellGenerations[index] != generation)
                    {
                        completed = false;
                        break;
                    }

                    float linearT = fadeElapsed / fadeDuration;
                    float travelT = EaseOutCubic(linearT);
                    rect.anchoredPosition = Vector2.LerpUnclamped(start, target, travelT);
                    rect.localScale = Vector3.one * Mathf.Lerp(1f, minScale, travelT);
                    view.SetAlpha(1f - linearT);

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    fadeElapsed += Time.unscaledDeltaTime;
                }
            }
            finally
            {
                if (!_isDestroyed)
                {
                    rect.anchoredPosition = start;
                    rect.localScale = Vector3.one;
                }
            }

            return completed;
        }

        /// <summary>
        /// <see cref="SingleLineClearEffect.Drop"/>: the cell falls straight down off the board with
        /// an accelerating, gravity-like motion (quadratic ease-in), fading out on a plain linear curve
        /// so it stays clearly visible while it falls rather than vanishing early. Combined with the
        /// existing per-cell stagger, a whole line reads as falling away one cell after another.
        /// The rect is always restored to its layout position and scale before this returns, exactly
        /// like <see cref="PlayFlyToCornerFadeAsync"/>.
        /// </summary>
        private async UniTask<bool> PlayDropFadeAsync(CellView view, int index, int generation)
        {
            var rect = (RectTransform)view.transform;
            Vector2 start = rect.anchoredPosition;
            float fallDistance = (_cellSize + _cellSpacing) * 1.5f;

            float fadeDuration = Mathf.Max(0.01f, _fadeDuration);
            float fadeElapsed = 0f;
            bool completed = true;

            try
            {
                while (fadeElapsed < fadeDuration)
                {
                    if (_cellGenerations[index] != generation)
                    {
                        completed = false;
                        break;
                    }

                    float t = fadeElapsed / fadeDuration;
                    float fallT = t * t;
                    rect.anchoredPosition = start + new Vector2(0f, -fallDistance * fallT);
                    view.SetAlpha(1f - t);

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    fadeElapsed += Time.unscaledDeltaTime;
                }
            }
            finally
            {
                if (!_isDestroyed)
                {
                    rect.anchoredPosition = start;
                }
            }

            return completed;
        }

        /// <summary>
        /// Fire-and-forget shard burst for <see cref="PlayShatterFadeAsync"/>: four small
        /// <see cref="UiSpriteFactory.RoundedSquare"/> copies, tinted the cell's own fill, flying to the
        /// four diagonals from the cell's centre and fading over one fade duration. Parented to
        /// <see cref="_cellLayerRoot"/> rather than to the cell itself so the shards' own transform is
        /// independent of whatever the cell's rect does (a fly-to-corner effect on a neighbouring cell,
        /// a later placement snapping this cell back) — never generation-checked because they touch no
        /// cell state and outlive the cell's own fade being cancelled or not on purpose.
        /// </summary>
        private async UniTaskVoid SpawnShatterShardsAsync(GridPosition cell, int colourId)
        {
            if (_cellLayerRoot == null || _currentTheme == null)
            {
                return;
            }

            Vector2 basePosition = CellAnchoredPosition(cell);
            float size = _cellSize * Mathf.Max(0.01f, _shatterShardSizeFraction);
            float flyDistance = _cellSize * Mathf.Max(0f, _shatterFlyDistanceFraction);
            float duration = Mathf.Max(0.01f, _fadeDuration);
            Color tint = colourId == Board.EMPTY ? _currentTheme.EmptyCellFill : _currentTheme.GetFill(colourId);

            RectTransform shardNE = CreateEffectParticle(basePosition, size, tint);
            RectTransform shardNW = CreateEffectParticle(basePosition, size, tint);
            RectTransform shardSE = CreateEffectParticle(basePosition, size, tint);
            RectTransform shardSW = CreateEffectParticle(basePosition, size, tint);

            Image imageNE = shardNE.GetComponent<Image>();
            Image imageNW = shardNW.GetComponent<Image>();
            Image imageSE = shardSE.GetComponent<Image>();
            Image imageSW = shardSW.GetComponent<Image>();

            Vector2 dirNE = new Vector2(1f, 1f).normalized;
            Vector2 dirNW = new Vector2(-1f, 1f).normalized;
            Vector2 dirSE = new Vector2(1f, -1f).normalized;
            Vector2 dirSW = new Vector2(-1f, -1f).normalized;

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    float t = elapsed / duration;
                    float travel = flyDistance * EaseOutCubic(t);
                    float alpha = 1f - t;

                    shardNE.anchoredPosition = basePosition + (dirNE * travel);
                    shardNW.anchoredPosition = basePosition + (dirNW * travel);
                    shardSE.anchoredPosition = basePosition + (dirSE * travel);
                    shardSW.anchoredPosition = basePosition + (dirSW * travel);

                    SetImageAlpha(imageNE, alpha);
                    SetImageAlpha(imageNW, alpha);
                    SetImageAlpha(imageSE, alpha);
                    SetImageAlpha(imageSW, alpha);

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-burst — the shards are going with it.
            }
            finally
            {
                Destroy(shardNE.gameObject);
                Destroy(shardNW.gameObject);
                Destroy(shardSE.gameObject);
                Destroy(shardSW.gameObject);
            }
        }

        /// <summary>
        /// Fire-and-forget ember drift for <see cref="PlayBurnFadeAsync"/>: two small
        /// <see cref="UiSpriteFactory.RoundedSquare"/> copies, tinted <see cref="EmberParticleTint"/>,
        /// drifting straight up off the cell and fading over one fade duration. See
        /// <see cref="SpawnShatterShardsAsync"/> for why this is parented independently and never
        /// generation-checked.
        /// </summary>
        private async UniTaskVoid SpawnEmberParticlesAsync(GridPosition cell)
        {
            if (_cellLayerRoot == null)
            {
                return;
            }

            Vector2 basePosition = CellAnchoredPosition(cell);
            float size = _cellSize * Mathf.Max(0.01f, _emberSizeFraction);
            float driftDistance = _cellSize * Mathf.Max(0f, _emberDriftFraction);
            float duration = Mathf.Max(0.01f, _fadeDuration);

            Vector2 startA = basePosition + new Vector2(-size * 0.7f, 0f);
            Vector2 startB = basePosition + new Vector2(size * 0.7f, 0f);
            RectTransform emberA = CreateEffectParticle(startA, size, EmberParticleTint);
            RectTransform emberB = CreateEffectParticle(startB, size, EmberParticleTint);
            Image imageA = emberA.GetComponent<Image>();
            Image imageB = emberB.GetComponent<Image>();

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    float t = elapsed / duration;
                    float rise = driftDistance * t;
                    float alpha = 1f - t;

                    emberA.anchoredPosition = startA + new Vector2(0f, rise);
                    emberB.anchoredPosition = startB + new Vector2(0f, rise);
                    SetImageAlpha(imageA, alpha);
                    SetImageAlpha(imageB, alpha);

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-drift — the embers are going with it.
            }
            finally
            {
                Destroy(emberA.gameObject);
                Destroy(emberB.gameObject);
            }
        }

        /// <summary>
        /// Issue #330 AC1: the additional visual a cross-clear (exactly one row and one column clearing
        /// together) gets on top of whatever <see cref="LineClearBurstView"/>'s own tier already plays
        /// for the same event — two bars crossing at the intersection, growing outward from it along the
        /// row and the column, holding briefly at full length, then fading. A plus shape rather than the
        /// burst's own circular glow on purpose: it reads as "a row AND a column", never as a bigger
        /// version of the same-tier two-line clear a plain two-rows (or two-columns) clear would
        /// otherwise look identical to.
        /// <para>
        /// Fire-and-forget and never generation-checked, exactly like <see cref="SpawnShatterShardsAsync"/>:
        /// it touches no cell state, only its own transient bars, so it always plays its one short run to
        /// completion (or dies with the view) rather than being cancellable by a later placement.
        /// </para>
        /// </summary>
        private async UniTaskVoid PlayCrossClearComboAsync(GridPosition intersection)
        {
            if (_cellLayerRoot == null)
            {
                return;
            }

            const float GrowFraction = 0.4f;
            const float HoldFraction = 0.15f;

            Vector2 centre = CellAnchoredPosition(intersection);
            float thickness = _cellSize * Mathf.Max(0.01f, _crossClearBarThicknessFraction);
            float fullWidth = (_width * _cellSize) + ((_width - 1) * _cellSpacing);
            float fullHeight = (_height * _cellSize) + ((_height - 1) * _cellSpacing);
            float duration = Mathf.Max(0.01f, _crossClearComboDuration);

            RectTransform horizontalBar = CreateComboBar(centre, thickness, thickness);
            RectTransform verticalBar = CreateComboBar(centre, thickness, thickness);
            Image horizontalImage = horizontalBar.GetComponent<Image>();
            Image verticalImage = verticalBar.GetComponent<Image>();

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    float t = elapsed / duration;
                    float grow = t < GrowFraction ? EaseOutCubic(t / GrowFraction) : 1f;
                    float fadeStart = GrowFraction + HoldFraction;
                    float alpha = t < fadeStart ? 1f : 1f - Mathf.Clamp01((t - fadeStart) / (1f - fadeStart));

                    horizontalBar.sizeDelta = new Vector2(fullWidth * grow, thickness);
                    verticalBar.sizeDelta = new Vector2(thickness, fullHeight * grow);

                    SetImageAlpha(horizontalImage, alpha);
                    SetImageAlpha(verticalImage, alpha);

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-flash — the bars are going with it.
            }
            finally
            {
                // Guarded rather than unconditional (unlike the shatter/ember cleanup this mirrors):
                // a scene teardown mid-flash can destroy _cellLayerRoot's whole hierarchy — these bars
                // included — before this finally runs, and Destroy()-ing an already-destroyed object
                // throws MissingReferenceException instead of silently no-opping.
                if (horizontalBar != null)
                {
                    Destroy(horizontalBar.gameObject);
                }

                if (verticalBar != null)
                {
                    Destroy(verticalBar.gameObject);
                }
            }
        }

        /// <summary>
        /// Issue #332 AC1: draws one beam from <paramref name="targetCell"/> (the cell the player
        /// tapped) to every OTHER cell in <paramref name="clearedCellPositions"/> that Color Cleanser
        /// cleared — the trigger cell itself is skipped, so tapping a cell whose colour appears nowhere
        /// else on the board (AC3) leaves <c>beamCount</c> at 0 and returns before a single beam is
        /// created. Fire-and-forget and never generation-checked, exactly like
        /// <see cref="PlayCrossClearComboAsync"/>: the beams are decorative, touch no cell state, and
        /// always play their one short run to completion (or die with the view).
        /// <para>
        /// Every beam is driven by the same elapsed-time loop so they all grow/hold/fade in lockstep,
        /// each stretching from the shared origin at its own length and <see cref="Mathf.Atan2"/> angle
        /// — the only per-beam geometry, since Color Cleanser's matches can be in any direction, not the
        /// row/column axes <see cref="PlayCrossClearComboAsync"/>'s bars stretch along.
        /// </para>
        /// </summary>
        private async UniTaskVoid PlayColorCleanserBeamsAsync(
            GridPosition? targetCell, IReadOnlyList<GridPosition> clearedCellPositions)
        {
            if (_cellLayerRoot == null || !targetCell.HasValue || clearedCellPositions == null)
            {
                return;
            }

            GridPosition target = targetCell.Value;
            Vector2 origin = CellAnchoredPosition(target);

            int beamCount = 0;
            for (int i = 0; i < clearedCellPositions.Count; i++)
            {
                if (!clearedCellPositions[i].Equals(target))
                {
                    beamCount++;
                }
            }

            // AC3: nothing beyond the trigger cell itself was cleared, so there is nothing to point a
            // beam at — zero beams, not a zero-length one.
            if (beamCount == 0)
            {
                return;
            }

            const float GrowFraction = 0.45f;
            const float HoldFraction = 0.1f;

            float thickness = _cellSize * Mathf.Max(0.01f, _colorCleanserBeamThicknessFraction);
            float duration = Mathf.Max(0.01f, _colorCleanserBeamDuration);

            var beams = new RectTransform[beamCount];
            var images = new Image[beamCount];
            var lengths = new float[beamCount];

            int beamIndex = 0;
            for (int i = 0; i < clearedCellPositions.Count; i++)
            {
                GridPosition cell = clearedCellPositions[i];
                if (cell.Equals(target))
                {
                    continue;
                }

                Vector2 endPoint = CellAnchoredPosition(cell);
                Vector2 delta = endPoint - origin;
                float angleDegrees = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

                RectTransform beam = CreateBeam(origin, thickness, angleDegrees);
                beams[beamIndex] = beam;
                images[beamIndex] = beam.GetComponent<Image>();
                lengths[beamIndex] = delta.magnitude;
                beamIndex++;
            }

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    float t = elapsed / duration;
                    float grow = t < GrowFraction ? EaseOutCubic(t / GrowFraction) : 1f;
                    float fadeStart = GrowFraction + HoldFraction;
                    float alpha = t < fadeStart ? 1f : 1f - Mathf.Clamp01((t - fadeStart) / (1f - fadeStart));

                    for (int i = 0; i < beams.Length; i++)
                    {
                        beams[i].sizeDelta = new Vector2(lengths[i] * grow, thickness);
                        SetImageAlpha(images[i], alpha);
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board view was destroyed mid-beam — the bars are going with it.
            }
            finally
            {
                for (int i = 0; i < beams.Length; i++)
                {
                    if (beams[i] != null)
                    {
                        Destroy(beams[i].gameObject);
                    }
                }
            }
        }

        /// <summary>One Color Cleanser beam for <see cref="PlayColorCleanserBeamsAsync"/>: a
        /// <see cref="UiSpriteFactory.RoundedSquare"/> sliced so its corner radius holds at any size,
        /// tinted <see cref="ColorCleanserBeamTint"/>, pivoted at its own start (<paramref name="origin"/>)
        /// rather than centred — unlike <see cref="CreateComboBar"/>'s bars, which grow symmetrically
        /// from an intersection along a fixed row/column axis, a beam grows outward from the trigger
        /// cell along an arbitrary <paramref name="angleDegrees"/>, so only its start end may stay
        /// anchored while <c>sizeDelta.x</c> stretches toward the target.</summary>
        private RectTransform CreateBeam(Vector2 origin, float thickness, float angleDegrees)
        {
            var beamObject = new GameObject("ColorCleanserBeam", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)beamObject.transform;
            rect.SetParent(_cellLayerRoot, false);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = origin;
            rect.sizeDelta = new Vector2(0f, thickness);
            rect.localEulerAngles = new Vector3(0f, 0f, angleDegrees);

            Image image = beamObject.GetComponent<Image>();
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            image.color = ColorCleanserBeamTint;

            return rect;
        }

        /// <summary>One combo-flash bar for <see cref="PlayCrossClearComboAsync"/>: a
        /// <see cref="UiSpriteFactory.RoundedSquare"/> sliced so its corner radius holds at any size,
        /// tinted <see cref="CrossClearComboTint"/>, centred on the intersection. The caller resizes it
        /// every frame via <c>sizeDelta</c> directly rather than scaling it, so its fixed thickness never
        /// stretches along with the axis that is growing.</summary>
        private RectTransform CreateComboBar(Vector2 centre, float width, float height)
        {
            var barObject = new GameObject("CrossClearComboBar", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)barObject.transform;
            rect.SetParent(_cellLayerRoot, false);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = centre;

            Image image = barObject.GetComponent<Image>();
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            image.color = CrossClearComboTint;

            return rect;
        }

        /// <summary>One small square Image, shared shape for every shatter shard and burn ember —
        /// <see cref="UiSpriteFactory.RoundedSquare"/> so it costs no new texture, sliced so its corner
        /// radius holds at any size. Parented to <see cref="_cellLayerRoot"/> and left for its spawner
        /// to destroy once its own timeline ends.</summary>
        private RectTransform CreateEffectParticle(Vector2 position, float size, Color tint)
        {
            var particleObject = new GameObject("LineClearParticle", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)particleObject.transform;
            rect.SetParent(_cellLayerRoot, false);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = position;

            Image image = particleObject.GetComponent<Image>();
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            image.color = tint;

            return rect;
        }

        private static void SetImageAlpha(Image image, float alpha)
        {
            Color colour = image.color;
            colour.a = alpha;
            image.color = colour;
        }

        /// <summary>Adopts a new theme: repaints the card and every cell already on the board, so a
        /// mid-run theme switch changes the pieces that are already placed, not just future ones.</summary>
        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;
            _wellLipImage.color = HudChrome.WellLipTint(theme.CardBackground, theme.Ink);
            _wellFaceImage.color = HudChrome.WellTint(theme.CardBackground, theme.Ink);

            RepaintCells();
            RepaintHighlight();
            RepaintBonusNumberStyle(theme);
        }

        /// <summary>Repaints every cell's (currently hidden) empty-cell bonus number to the score card's
        /// own ink (issue #424) — see <see cref="CellView.SetBonusNumberStyle"/>. Done on every theme
        /// switch, not just once, since the accent — and so this ink — changes per season.</summary>
        private void RepaintBonusNumberStyle(ThemeDefinition theme)
        {
            if (_cells == null)
            {
                return;
            }

            Color bonusInk = HudChrome.Darken(theme.Accent, BONUS_NUMBER_INK_SHADE);
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i].SetBonusNumberStyle(_bonusNumberFont, bonusInk);
            }
        }

        private void RepaintCells()
        {
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int index = (y * _width) + x;

                    // A cell fading out from a line clear still shows its pre-clear colour; repaint
                    // that one instead of the (already empty) settled colour. The fade in flight
                    // restores the alpha on its next tick.
                    int colourId = _cellPending[index] ? _pendingColourIds[index] : _cellColourIds[index];
                    ApplyCellColour(new GridPosition(x, y), colourId);

                    // A diamond's icon is tinted from the theme (issue #395), unlike every other kind's
                    // fixed tint, so it is the one icon a theme switch has to repaint. Settled cells
                    // only: a fading cell's kind is already None, and re-applying that would take its
                    // still-fading icon down early.
                    if (_cellSpecialKinds[index] == SpecialCellKind.Diamond && !_cellPending[index])
                    {
                        ApplyCellIcon(index, SpecialCellKind.Diamond);
                    }
                }
            }
        }

        private void ApplyCellColour(GridPosition cell, int colourId)
        {
            if (_currentTheme == null)
            {
                return;
            }

            int index = CellIndex(cell);
            CellView view = _cells[index];

            // Checked before occupancy, and before the empty-cell look: a hole is never occupied, and
            // drawing it like an empty cell would tell the player they could drop a piece there.
            if (_holeMask[index])
            {
                view.SetColours(HoleFill(_currentTheme), HoleOutline(_currentTheme));
                return;
            }

            if (colourId == Board.EMPTY)
            {
                view.SetColours(_currentTheme.EmptyCellFill, _currentTheme.EmptyCellOutline);
                return;
            }

            // A reinforced cell takes this same plain paint (issue #438): its "reinforced" look is the
            // skin overlay ApplyOverlay lays over the block, not a tint of the block itself, exactly as
            // a locked cell's is.
            view.SetEmbossedColours(
                _currentTheme.GetFill(colourId),
                _currentTheme.GetHighlight(colourId),
                _currentTheme.GetShade(colourId));
        }

        /// <summary>Shows or hides one cell's special-cell icon. Allocation-free and idempotent, like
        /// the outline toggle it mirrors, so every repaint path can call it unconditionally.</summary>
        private void ApplyCellIcon(int index, SpecialCellKind kind)
        {
            CellView cell = _cells[index];

            // A locked cell (issue #434) wears its skin overlay and nothing else: no starburst, no glow
            // pulse — its "special" look is the whole plate, driven by OnLockedCellChanged, not a mark
            // on top of it. So it takes the no-icon path exactly as an ordinary cell does.
            if (kind == SpecialCellKind.None || kind == SpecialCellKind.Locked)
            {
                cell.ClearSpecialIcon();
                cell.ClearSpecialGlow();

                // Negative test (issue #365 AC7): a None cell never glows, so it never has an entry to
                // remove — this only fires for a cell that actually was glowing a moment ago.
                if (_glowActiveMask[index])
                {
                    _glowActiveMask[index] = false;
                    _activeGlowCells.Remove(cell);
                }

                return;
            }

            if (kind == SpecialCellKind.Diamond)
            {
                // The one kind whose hue is a per-cell value (the gem's own colour, issue #395) rather
                // than a per-kind constant: painted through the shared seam the tray, pocket and drag
                // ghost paint through, so a diamond lands on the board looking exactly as it did in
                // the air. Falls back to the kind's flat tint only while no theme is known yet, or for
                // a diamond cell the model reports no gem colour for (an upstream invariant breach,
                // but still a cell that must show it is special rather than draw nothing).
                if (_currentTheme != null && _cellDiamondColourIds[index] != TrayModel.NO_DIAMOND)
                {
                    DiamondVisuals.Apply(
                        cell, _cellDiamondColourIds[index], _currentTheme, IconSprite(kind));
                }
                else
                {
                    cell.SetSpecialIcon(IconTint(kind), IconSprite(kind));
                    cell.SetSpecialGlow(GlowTint(kind));
                }
            }
            else
            {
                cell.SetSpecialIcon(IconTint(kind), IconSprite(kind));
                cell.SetSpecialGlow(GlowTint(kind));
            }

            if (!_glowActiveMask[index])
            {
                _glowActiveMask[index] = true;
                _activeGlowCells.Add(cell);
            }
        }

        /// <summary>The tint one kind's icon is drawn in. Stated once, as a switch rather than a chain
        /// of conditionals, so a new kind is one line here and nothing else. Internal so
        /// <see cref="InfoPopupView"/> can reuse it for a special cell's popup hero icon rather than
        /// duplicating this table.
        /// <para>
        /// Vortex, Coin, and ExplosiveCore return <see cref="Color.white"/> (no-op tint): those three
        /// ship as full-colour hand-picked sprites rather than white silhouettes, so multiplying them by
        /// anything but white would recolour art that already carries its own palette. Their
        /// distinguishing hue lives only in <see cref="GlowIdentityColor"/> now, for the halo behind
        /// them. Every other kind is still a white silhouette tinted here, exactly as before.
        /// </para>
        /// </summary>
        internal static Color IconTint(SpecialCellKind kind)
        {
            switch (kind)
            {
                case SpecialCellKind.Vortex:
                case SpecialCellKind.Coin:
                case SpecialCellKind.ExplosiveCore:
                    return Color.white;
                case SpecialCellKind.ScoreGem:
                    return ScoreGemIconTint;
                case SpecialCellKind.ChainLightning:
                    return ChainLightningIconTint;
                case SpecialCellKind.Timer:
                    return SpecialIconTint;
                default:
                    return SpecialIconTint;
            }
        }

        /// <summary>
        /// The hue one kind's glow halo (issue #365) is identified by — kept as its own table rather
        /// than reusing <see cref="IconTint"/> now that Vortex, Coin, and ExplosiveCore tint their icon
        /// with plain white (their sprite already carries full colour): without this split, their glow
        /// would go white too and stop reading as their own kind. Every kind not in that trio still maps
        /// 1:1 with its <see cref="IconTint"/> entry, so nothing else changes.
        /// </summary>
        private static Color GlowIdentityColor(SpecialCellKind kind)
        {
            switch (kind)
            {
                case SpecialCellKind.Vortex:
                    return VortexIconTint;
                case SpecialCellKind.Coin:
                    return CoinIconTint;
                case SpecialCellKind.ExplosiveCore:
                    return ExplosiveCoreGlowTint;
                case SpecialCellKind.ScoreGem:
                    return ScoreGemIconTint;
                case SpecialCellKind.ChainLightning:
                    return ChainLightningIconTint;
                case SpecialCellKind.Timer:
                    return SpecialIconTint;
                default:
                    return SpecialIconTint;
            }
        }

        /// <summary>
        /// The colour one kind's glow halo (issue #365) is drawn in behind its icon — derived from
        /// <see cref="GlowIdentityColor"/> rather than <see cref="IconTint"/> (see that method's remarks
        /// for why the two split). Blended towards white (<see cref="GLOW_TINT_WHITEN"/>) for brightness
        /// against any theme fill (AC3) while keeping enough of the source hue that a kind's glow is
        /// still recognisably its own colour, not a shared white halo for all seven (AC5). Not reused by
        /// <see cref="InfoPopupView"/> — its hero icon stays flat by design (AC6) — so, unlike
        /// <see cref="IconTint"/>, this is private.
        /// </summary>
        private static Color GlowTint(SpecialCellKind kind) => GlowTintFrom(GlowIdentityColor(kind));

        /// <summary>
        /// The glow halo colour for an identity hue that is not a per-kind constant — a
        /// <see cref="SpecialCellKind.Diamond"/>'s gem colour (issue #395), which is per cell and comes
        /// from the theme. The same whiten-and-alpha step <see cref="GlowTint"/> applies to every fixed
        /// kind, split out so <see cref="DiamondVisuals"/> can give a decorated tray, pocket or ghost
        /// cell the very halo the board will draw once it lands.
        /// </summary>
        internal static Color GlowTintFrom(Color identity)
        {
            Color glow = Color.Lerp(identity, Color.white, GLOW_TINT_WHITEN);
            glow.a = GLOW_BASE_ALPHA;
            return glow;
        }

        /// <summary>
        /// The glyph one kind's icon is drawn with — a distinct shape per kind (issue: "her özel hücrenin
        /// kendine özgü bir ikonu olsun"), tinted by <see cref="IconTint"/> on top exactly as the shared
        /// <c>UiSpriteFactory.Starburst</c> was. Falls back to that starburst, unfilled inspector slots
        /// included, so a kind added before its art exists still renders something rather than nothing.
        /// Internal so <see cref="InfoPopupView"/> can reuse it, for the same reason as
        /// <see cref="IconTint"/>.
        /// </summary>
        internal Sprite IconSprite(SpecialCellKind kind)
        {
            Sprite sprite;
            switch (kind)
            {
                case SpecialCellKind.Laser:
                    sprite = _laserIconSprite;
                    break;
                case SpecialCellKind.ScoreGem:
                    sprite = _scoreGemIconSprite;
                    break;
                case SpecialCellKind.Vortex:
                    sprite = _vortexIconSprite;
                    break;
                case SpecialCellKind.ChainLightning:
                    sprite = _chainLightningIconSprite;
                    break;
                case SpecialCellKind.Coin:
                    sprite = _coinIconSprite;
                    break;
                case SpecialCellKind.ExplosiveCore:
                    sprite = _explosiveCoreIconSprite;
                    break;
                case SpecialCellKind.Timer:
                    sprite = _timerIconSprite;
                    break;
                case SpecialCellKind.Diamond:
                    sprite = _diamondIconSprite;
                    break;
                default:
                    sprite = null;
                    break;
            }

            return sprite != null ? sprite : UiSpriteFactory.Starburst;
        }

        /// <summary>Draws a clearing cell blended towards the flash tint, keeping it on the same
        /// layer set it was already showing so the fade never switches looks mid-flight.</summary>
        private void ApplyClearTint(CellView view, int colourId, float blend) => ApplyClearTint(view, colourId, blend, FlashTint);

        /// <summary>
        /// As the two-argument overload, but blending towards <paramref name="targetTint"/> instead of
        /// the fixed <see cref="FlashTint"/> white — issue #331's <see cref="SingleLineClearEffect.Burn"/>
        /// reuses this exact blend idiom to tint a cell towards <see cref="EmberTint"/> as it fades,
        /// rather than towards white for an instant then back.
        /// </summary>
        private void ApplyClearTint(CellView view, int colourId, float blend, Color targetTint)
        {
            if (_currentTheme == null)
            {
                return;
            }

            if (colourId == Board.EMPTY)
            {
                view.SetColours(
                    Color.Lerp(_currentTheme.EmptyCellFill, targetTint, blend),
                    Color.Lerp(_currentTheme.EmptyCellOutline, targetTint, blend));
                return;
            }

            view.SetEmbossedColours(
                Color.Lerp(_currentTheme.GetFill(colourId), targetTint, blend),
                Color.Lerp(_currentTheme.GetHighlight(colourId), targetTint, blend),
                Color.Lerp(_currentTheme.GetShade(colourId), targetTint, blend));
        }
    }
}
