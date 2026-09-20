using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
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
    /// The settings card, drawn in the storefront language the profile card and the shop share
    /// (issue #260): one sunken well, and inside it one plate per setting — a coloured icon tile, a
    /// small uppercase label, the value in the display face, and a chevron disc or a 3D toggle on the
    /// right. Several screens live inside the one card:
    /// <list type="bullet">
    /// <item>Settings — the five plates (mode, theme, language, sound, round length) and, at the foot
    /// of the well, the one call to action: the Remove Ads button, or the "ads removed" strip once it
    /// is owned.</item>
    /// <item>Theme — a 2×2 grid of cards, each previewing its own theme's gradient with a 4×4 mini
    /// board in that theme's real kind colours. Picking one calls <see cref="SettingsSystem.SetTheme"/>
    /// and returns; the recolour itself is handled by the reactive theme subscriptions in every other
    /// View, so nothing else happens here.</item>
    /// <item>Mode — one plate per entry in <see cref="SelectableModes"/>, with a one-line description
    /// and a PLAYING tag on the active one. Picking the active mode just returns; picking any other
    /// opens the confirmation card at the bottom of the same screen, because switching restarts the
    /// run. The Timed plate is taller than the other two: it carries a 3-column row of duration chips
    /// under its header, so the length is picked in place rather than on a separate screen (issue
    /// #270). Picking a chip always applies immediately; it only opens the confirmation card too when
    /// Timed was not already the active mode.</item>
    /// <item>ModeConfirm — the Mode screen with that card showing: KEEP PLAYING steps back, RESTART
    /// calls <see cref="GameModeSystem.SelectMode"/>, which owns the restart.</item>
    /// <item>Language — one plate per shipped language, each labelled in its own language. Picking one
    /// calls <see cref="LocalizationSystem.SetLocale"/>; the re-wording is handled by the reactive
    /// locale subscriptions in every View, this one included.</item>
    /// </list>
    /// Every screen is built once in <see cref="Start"/> and toggled with SetActive — the same "build
    /// once, never rebuild" approach <see cref="CellView"/> uses for its two looks — and every screen
    /// is the same 880 × 1140 card the hub's other tabs use, so the hub's tab bar never has to chase a
    /// height change between sub-screens.
    /// <para>
    /// Every colour on the card comes from the active <see cref="ThemeDefinition"/>: the plates, the
    /// well and the labels from its neutrals, the icon tiles and the toggle from its kind triplets, the
    /// selection rings from its accent. The only constant is white, for the glyphs drawn on coloured
    /// tiles and the toggle thumb — the same constant the hub's tab glyphs use.
    /// </para>
    /// <para>
    /// Like the rest of the UI this View never raycasts: <see cref="BoardInputView"/> owns the pointer
    /// and forwards taps to <see cref="HandleTap"/> while the panel is open.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanelView : MonoBehaviour
    {
        // Layout, in canvas reference pixels, on the 880 × 1140 card the hub's other cards share. The
        // mock-up was drawn on a 358pt card; everything here is that mock-up at 880/358. Vertical
        // offsets are measured down from the card's top edge; TopY turns them into anchored positions.
        private const float HEADER_INSET = 84f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;

        /// <summary>The well: the sunken plate the whole content sits in, inset from the card edge on
        /// every side, with its own padding inside that — the same well the profile card has.</summary>
        private const float WELL_INSET = 16f;
        private const float WELL_PADDING_X = 24f;
        private const float WELL_PADDING_Y = 28f;
        private const float WELL_CORNER_RADIUS = 22f;

        /// <summary>Rendered corner radius of a plate, in reference pixels. The shared rounded sprite
        /// bakes its radius at <see cref="UiSpriteFactory.ROUNDED_RADIUS"/>, so the slice multiplier is
        /// derived from the two, as the hub's tabs and the profile card do.</summary>
        private const float PLATE_CORNER_RADIUS = 16f;

        /// <summary>A plate's drop shadow: a copy of the plate, this far lower, in Ink at low alpha.</summary>
        private const float PLATE_SHADOW_DROP = 4f;

        private const float ROW_HEIGHT = 148f;
        private const float ROW_GAP = 22f;
        private const float ROW_PADDING_X = 28f;

        /// <summary>The icon tile: a rounded square in the row's kind fill over a slightly taller one
        /// in its shade, so the shade shows as a lip along the bottom.</summary>
        private const float TILE_SIZE = 96f;
        private const float TILE_LIP = 7f;
        private const float TILE_CORNER_RADIUS = 28f;

        /// <summary>Left edge of a row's text column: after the padding, the tile and a gap.</summary>
        private const float ROW_TEXT_INSET = ROW_PADDING_X + TILE_SIZE + 28f;

        private const float ROW_LABEL_RISE = 30f;
        private const float ROW_VALUE_DROP = 14f;

        /// <summary>The chevron disc on a row that steps to a picker screen: a disc in the empty-cell
        /// fill over a slightly lower one in the empty-cell outline, so it has the same lip the tiles
        /// have.</summary>
        private const float DISC_SIZE = 84f;
        private const float DISC_LIP = 6f;

        private const float CHEVRON_HALF_SIZE = 14f;
        private const float CHEVRON_THICKNESS = 8f;

        private const float TOGGLE_WIDTH = 138f;
        private const float TOGGLE_HEIGHT = 78f;
        private const float TOGGLE_LIP = 8f;
        private const float TOGGLE_THUMB_SIZE = 62f;
        private const float TOGGLE_THUMB_INSET = 7f;

        /// <summary>Half the slack left in the track once the thumb and its inset are removed.</summary>
        private const float TOGGLE_THUMB_TRAVEL = (TOGGLE_WIDTH - TOGGLE_THUMB_SIZE - (TOGGLE_THUMB_INSET * 2f)) * 0.5f;

        /// <summary>The five kind dots after the theme row's value: the same dots the old badge wore,
        /// moved into the value.</summary>
        private const float THEME_DOT_SIZE = 20f;
        private const float THEME_DOT_PITCH = 26f;
        private const float THEME_DOT_GAP = 18f;

        /// <summary>The call to action at the foot of the well, and the strip that replaces it — the
        /// same size as the profile card's SAVE button and its "Saved with" strip.</summary>
        private const float ACTION_HEIGHT = 96f;
        private const float ACTION_CAPTION_RISE = 30f;

        /// <summary>The glossy button sprite's face sits above a baked darker lip, so a label is lifted
        /// off the button's geometric centre to sit on the face — the same rise the shop uses.</summary>
        private const float BUTTON_LABEL_RISE = 4f;

        /// <summary>Slice scale for the glossy button sprite, matching <see cref="PowerUpShopView"/> and
        /// <see cref="ProfilePanelView"/> so every screen's buttons have the same lip and corner.</summary>
        private const float BUTTON_SLICE_SCALE = 2.5f;

        // Sub-screens: the well's first row is a round back disc and the screen's name.
        private const float BACK_DISC_SIZE = 88f;
        private const float SUB_HEADER_HEIGHT = 88f;

        private const int THEME_COLUMN_COUNT = 2;
        private const float THEME_CARD_GAP = 24f;
        private const float THEME_CARD_HEIGHT = 400f;
        private const float THEME_CARD_PADDING = 20f;
        private const float THEME_PREVIEW_HEIGHT = 300f;
        private const float THEME_NAME_RISE = 34f;

        /// <summary>The 4×4 mini board on a theme card: a plate in that theme's card colour holding
        /// sixteen bevelled cells in its real kind colours.</summary>
        private const int MINI_BOARD_SIZE = 4;
        private const float MINI_BOARD_WIDTH = 192f;
        private const float MINI_BOARD_PADDING = 12f;
        private const float MINI_BOARD_CORNER_RADIUS = 20f;
        private const float MINI_CELL_GAP = 8f;
        private const float MINI_CELL_LIP = 4f;
        private const float MINI_CELL_CORNER_RADIUS = 8f;

        /// <summary>
        /// Which kind fills each cell of the preview board, row by row from the top; 0 is empty. A
        /// fixed checker of four kinds rather than a random scatter, so the four cards are told apart by
        /// their colours alone and not by their layouts.
        /// </summary>
        private static readonly int[] MiniBoardPattern =
        {
            0, 2, 0, 3,
            4, 0, 5, 0,
            0, 3, 0, 2,
            5, 0, 4, 0,
        };

        /// <summary>The selection ring around a chosen card or plate: an accent ring this far outside
        /// the plate, with a card-coloured gap between the two so the ring reads as a ring.</summary>
        private const float RING_OUTSET = 14f;
        private const float RING_GAP_OUTSET = 7f;

        /// <summary>The check disc in a chosen card's corner: accent, with a darker lip and a white tick.</summary>
        private const float CHECK_DISC_SIZE = 64f;
        private const float CHECK_DISC_LIP = 5f;
        private const float CHECK_DISC_INSET = 20f;
        private const float CHECK_GLYPH_SIZE = 34f;

        private const float MODE_NAME_RISE = 26f;
        private const float MODE_DESCRIPTION_DROP = 24f;

        /// <summary>Horizontal room reserved at a mode plate's right edge for the PLAYING tag, so a
        /// description that wraps to a second line (issue #355 follow-up — some locales' copy, e.g.
        /// Spanish's "Infinito" description, does not fit one line) never runs under it.</summary>
        private const float MODE_DESCRIPTION_PLAYING_RESERVE = 150f;

        /// <summary>The restart confirmation: a plate at the foot of the mode screen with a title, a
        /// line of body and the two buttons.</summary>
        private const float CONFIRM_CARD_HEIGHT = 250f;
        private const float CONFIRM_PADDING = 28f;
        private const float CONFIRM_TITLE_DROP = 50f;
        private const float CONFIRM_BODY_DROP = 104f;
        private const float CONFIRM_BUTTON_HEIGHT = 84f;
        private const float CONFIRM_BUTTON_GAP = 24f;

        /// <summary>How many timed (non-"Sınırsız") lengths share the bottom chip row — 5/10/15 dk
        /// today. The endless entry gets its own full-width row above this one instead of a fourth
        /// column, so "5 dk"/"10 dk"/"15 dk" can stay one line each in every locale (issue #355
        /// follow-up) rather than needing the number and unit split onto separate lines.</summary>
        private const int DURATION_COLUMN_COUNT = 3;
        private const float OPTION_GAP = 24f;

        /// <summary>
        /// The Timed plate on the mode screen carries two duration chip rows under its header — the
        /// "Sınırsız" entry full-width on top, the timed lengths in a grid below it (issue #355
        /// follow-up) — so it is taller than the other two mode plates (issue #270). The header fills
        /// the top <see cref="ROW_HEIGHT"/> of the plate exactly as every other mode plate does, and the
        /// two chip rows fill the rest, with one <see cref="ROW_GAP"/> between each pair — the same "no
        /// extra padding, the row height IS the content" language every other row already uses.
        /// </summary>
        private const float TIMED_CHIP_ROW_HEIGHT = 110f;
        private const float TIMED_PLATE_HEIGHT =
            ROW_HEIGHT + (ROW_GAP * 2f) + (TIMED_CHIP_ROW_HEIGHT * 2f);

        // Type sizes. The display face is Bowlby One SC where the mock-up uses it (values, names,
        // buttons); everything else is the built-in face in bold, as on the profile card.
        private const int TITLE_FONT_SIZE = 48;
        private const int LABEL_FONT_SIZE = 24;
        private const int VALUE_FONT_SIZE = 44;
        private const int THEME_NAME_FONT_SIZE = 36;
        private const int MODE_NAME_FONT_SIZE = 40;
        private const int DESCRIPTION_FONT_SIZE = 24;
        private const int BUTTON_FONT_SIZE = 34;
        private const int CONFIRM_TITLE_FONT_SIZE = 40;
        private const int CONFIRM_BUTTON_FONT_SIZE = 30;
        private const int OPTION_FONT_SIZE = 44;

        /// <summary>How far the well sinks below the card: Ink over CardBackground.</summary>
        private const float WELL_TINT = 0.06f;

        /// <summary>A plate's shadow: Ink at this alpha, as the profile card's plates have.</summary>
        private const float PLATE_SHADOW_ALPHA = 0.12f;

        /// <summary>The owned strip's ground: the owned kind's highlight over CardBackground.</summary>
        private const float OWNED_TINT = 0.55f;

        /// <summary>How far the accent is pulled toward black for a check disc's lip.</summary>
        private const float ACCENT_SHADE = 0.35f;

        /// <summary>How far a busy Remove Ads button fades toward SoftInk while the store prompt is up.</summary>
        private const float BUSY_FADE = 0.5f;

        // Which theme kind's bevel triplet each coloured element takes. Chosen by role, so a season swap
        // recolours them together: one kind per row's tile, the call to action, the toggle's "on"
        // track and the owned strip.
        private const int MODE_KIND = 5;
        private const int THEME_KIND = 3;
        private const int LANGUAGE_KIND = 4;
        private const int SOUND_KIND = 2;
        private const int DURATION_KIND = 1;
        private const int PRIMARY_KIND = 1;
        private const int TOGGLE_KIND = 5;
        private const int OWNED_KIND = 5;

        /// <summary>
        /// Every mode the picker offers, in display order. The single source of "which modes exist to
        /// choose from": adding one here (with its tile kind, glyph and description key) is all the
        /// picker needs.
        /// </summary>
        private static readonly GameMode[] SelectableModes =
        {
            GameMode.Timed,
            GameMode.Endless,
            GameMode.Path,
        };

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly List<ThemeOption> _themeOptions = new List<ThemeOption>(4);
        private readonly List<ModeOption> _modeOptions = new List<ModeOption>(SelectableModes.Length);
        private readonly List<DurationOption> _durationOptions = new List<DurationOption>(6);
        private readonly List<LanguageOption> _languageOptions = new List<LanguageOption>(3);
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        // Repaint buckets: every Image and Text built here belongs to exactly one of them, so a theme
        // switch is a handful of tight loops instead of a hierarchy walk.
        private readonly List<Image> _platePlates = new List<Image>(32);
        private readonly List<Image> _plateShadows = new List<Image>(32);
        private readonly List<Image> _inkImages = new List<Image>(24);
        private readonly List<Image> _discFaces = new List<Image>(8);
        private readonly List<Image> _discLips = new List<Image>(8);
        private readonly List<KindImage> _kindFills = new List<KindImage>(24);
        private readonly List<KindImage> _kindShades = new List<KindImage>(12);
        private readonly List<Text> _inkTexts = new List<Text>(24);
        private readonly List<Text> _softInkTexts = new List<Text>(16);
        private readonly Image[] _themeValueDots = new Image[ThemeDefinition.KIND_COUNT];

        /// <summary>
        /// Every label whose wording is a plain String Table lookup, paired with its key. The same
        /// "repaint bucket" idea as <see cref="_inkTexts"/>, applied to words instead of colours: a
        /// language switch is one tight loop rather than a hierarchy walk, and a new label is one
        /// <see cref="RegisterLocalized"/> call rather than a new branch in the locale handler.
        /// Labels that need a value substituted in (the values, the duration chips, the confirmation
        /// title) are not in here — they are re-rendered by their own refresh methods.
        /// </summary>
        private readonly List<LocalizedLabel> _localizedLabels = new List<LocalizedLabel>(24);

        [Header("Layout")]
        [Tooltip("Card size. Every screen inside the card is this one size, so the hub's tab bar never has to chase a sub-screen.")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1272f);

        [Header("Art")]
        [Tooltip("The chunky display face for the values, names and buttons. Falls back to the built-in "
            + "runtime font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("White 9-sliced glossy button with a darker bottom lip, shared with the shop and the "
            + "profile card. Tinted at runtime from the theme.")]
        [SerializeField] private Sprite _buttonSprite;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private SettingsModel _settingsModel;
        private SettingsSystem _settingsSystem;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private SfxModel _sfxModel;
        private ISfxService _sfxService;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private TimerRunSystem _timerRunSystem;
        private ProfileModel _profileModel;
        private AdRemovalSystem _adRemovalSystem;
        private Canvas _canvas;

        /// <summary>
        /// Guards the Remove Ads button against a second tap while a store prompt is already up: a
        /// store prompt is modal and slow, and a second overlapping order is one the store would only
        /// refuse.
        /// </summary>
        private bool _isPurchasingRemoveAds;

        private PanelScreen _screen = PanelScreen.Settings;

        /// <summary>The mode the confirmation card is asking about. Only meaningful on that screen.</summary>
        private GameMode _pendingMode = GameMode.Endless;

        private ThemeDefinition _currentTheme;

        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private Image _wellPlate;

        private GameObject _settingsScreenRoot;
        private GameObject _themeScreenRoot;
        private GameObject _modeScreenRoot;
        private GameObject _languageScreenRoot;

        private RectTransform _closeButtonRect;
        private Text _titleText;

        private RectTransform _modeRowRect;
        private RectTransform _themeRowRect;
        private RectTransform _languageRowRect;
        private RectTransform _soundRowRect;

        private RectTransform _themeBackButtonRect;
        private RectTransform _modeBackButtonRect;
        private RectTransform _languageBackButtonRect;

        private RectTransform _confirmCardRect;
        private RectTransform _confirmYesRect;
        private RectTransform _confirmNoRect;
        private Image _confirmYesPlate;
        private Image _confirmNoFace;
        private Image _confirmNoLip;
        private Text _confirmTitleText;
        private Text _confirmYesText;
        private Text _confirmNoText;

        private RectTransform _toggleThumbRect;
        private Image _toggleFace;
        private Image _toggleLip;

        private Text _themeValueText;
        private RectTransform _themeValueRect;
        private Text _modeValueText;
        private Text _soundValueText;
        private Text _languageValueText;

        /// <summary>The mode row's tile wears the active mode's own glyph, one per selectable mode.</summary>
        private readonly GameObject[] _modeRowGlyphs = new GameObject[SelectableModes.Length];

        private RectTransform _removeAdsButtonRect;
        private Image _removeAdsButtonPlate;
        private Text _removeAdsButtonText;
        private RectTransform _ownedStripRect;
        private Image _ownedStripPlate;
        private Image _ownedCheck;
        private Text _ownedText;

        /// <summary>Which of the screens inside the card is showing.</summary>
        private enum PanelScreen
        {
            Settings,
            Theme,
            Mode,
            ModeConfirm,
            Language,
        }

        /// <summary>A built label together with the String Table key it renders.</summary>
        private readonly struct LocalizedLabel
        {
            internal LocalizedLabel(Text label, string key, bool uppercase)
            {
                Label = label;
                Key = key;
                Uppercase = uppercase;
            }

            internal Text Label { get; }

            internal string Key { get; }

            /// <summary>Rendered in capitals, the way the mock-up sets its small labels. Done at paint
            /// time rather than in the table so the same key can still read in mixed case elsewhere.</summary>
            internal bool Uppercase { get; }
        }

        /// <summary>An Image painted in one theme kind's fill or shade.</summary>
        private readonly struct KindImage
        {
            internal KindImage(Image image, int kind)
            {
                Image = image;
                Kind = kind;
            }

            internal Image Image { get; }

            internal int Kind { get; }
        }

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            SettingsSystem settingsSystem,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            SfxModel sfxModel,
            ISfxService sfxService,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            TimerRunSystem timerRunSystem,
            ProfileModel profileModel,
            AdRemovalSystem adRemovalSystem)
        {
            _settingsModel = settingsModel;
            _settingsSystem = settingsSystem;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _sfxModel = sfxModel;
            _sfxService = sfxService;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
            _timerRunSystem = timerRunSystem;
            _profileModel = profileModel;
            _adRemovalSystem = adRemovalSystem;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_settingsModel == null || _settingsSystem == null || _sfxModel == null || _sfxService == null
                || _localizationModel == null || _localizationSystem == null
                || _gameModeSystem == null || _timedModeSystem == null || _timerRunSystem == null
                || _profileModel == null || _adRemovalSystem == null)
            {
                Debug.LogError(
                    $"{nameof(SettingsPanelView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Built in Start rather than Awake: the theme grid needs the injected theme catalogue,
            // which is only available once VContainer has run Construct.
            BuildPanel();
            SetScreen(PanelScreen.Settings);
            _panel.SetActive(false);

            // Before the theme subscription: the theme handler repaints the round-length row, which can
            // only be worded once the language is known.
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _sfxModel.IsMuted.Subscribe(OnMutedChanged).AddTo(_disposables);

            // Observed rather than read once: the flag is one-way, but it is set while this card is the
            // open screen — the purchase is started from it — so the foot of the well has to repaint on
            // the write rather than only on the next open.
            _profileModel.AdsRemoved.Subscribe(OnAdsRemovedChanged).AddTo(_disposables);
            _timedModeSystem.SelectedDuration.Subscribe(OnSelectedDurationChanged).AddTo(_disposables);

            // Last, because its handler repaints the round-length row, which needs the ones above to
            // have published their first value.
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>The card's own rect. Read by <see cref="HubPanelView"/> to sit its tab bar flush
        /// against whichever card is open.</summary>
        internal RectTransform CardRect => _cardRect;

        /// <summary>This card's own close cross, kept for a stand-alone open. Hidden by
        /// <see cref="HubPanelView"/> once opened there, since the hub's own header carries the one
        /// close button for whichever tab is open.</summary>
        internal RectTransform CloseButtonRect => _closeButtonRect;

        /// <summary>This card's own title — its localized name. Hidden by <see cref="HubPanelView"/>
        /// once opened there, since the hub's own header says the same thing.</summary>
        internal Text HeaderTitleText => _titleText;

        /// <summary>
        /// Shows the panel on top of everything else, including the game-over card. Always lands on
        /// the settings screen, whichever screen it was left on last time.
        /// </summary>
        internal void Open()
        {
            if (_panel == null)
            {
                return;
            }

            SetScreen(PanelScreen.Settings);
            _panel.SetActive(true);
            transform.SetAsLastSibling();
            _timerRunSystem.SetMenuPaused(true);
        }

        /// <summary>
        /// Routes a tap while the panel is open. Controls are tested first, then the card itself
        /// (which swallows the tap and stays open); only the scrim outside the card dismisses.
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

            bool handled = _screen switch
            {
                PanelScreen.Theme => HandleThemeScreenTap(screenPosition, eventCamera),
                PanelScreen.Mode => HandleModeScreenTap(screenPosition, eventCamera),
                PanelScreen.ModeConfirm => HandleConfirmScreenTap(screenPosition, eventCamera),
                PanelScreen.Language => HandleLanguageScreenTap(screenPosition, eventCamera),
                _ => HandleSettingsScreenTap(screenPosition, eventCamera),
            };

            if (handled)
            {
                return;
            }

            // Tapping the card but missing a control keeps the panel open; only the scrim dismisses,
            // and it dismisses outright rather than stepping back a screen.
            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        /// <summary>
        /// Shuts the card and releases the menu pause. Reachable by <c>HubPanelView</c>, which shuts the
        /// outgoing card when the player switches tabs; every other caller is this class's own dismiss
        /// paths.
        /// </summary>
        internal void Close()
        {
            _panel.SetActive(false);
            _timerRunSystem.SetMenuPaused(false);
        }

        // ---------------------------------------------------------------------------- taps

        private bool HandleSettingsScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, screenPosition, eventCamera))
            {
                Close();
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_modeRowRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Mode);
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_themeRowRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Theme);
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_languageRowRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Language);
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_soundRowRect, screenPosition, eventCamera))
            {
                _sfxService.SetMuted(!_sfxModel.IsMuted.Value);
                return true;
            }

            // The button and the owned strip share one rect, so one test covers both. Swallowed even
            // when owned: the strip stays in place so the player can see the purchase went through, and
            // a tap that fell through to the scrim would dismiss the card instead.
            if (RectTransformUtility.RectangleContainsScreenPoint(_removeAdsButtonRect, screenPosition, eventCamera))
            {
                if (!_profileModel.AdsRemoved.Value)
                {
                    PurchaseRemoveAds().Forget();
                }

                return true;
            }

            return false;
        }

        private bool HandleThemeScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
            for (int optionIndex = 0; optionIndex < _themeOptions.Count; optionIndex++)
            {
                ThemeOption option = _themeOptions[optionIndex];
                if (RectTransformUtility.RectangleContainsScreenPoint(option.Rect, screenPosition, eventCamera))
                {
                    _settingsSystem.SetTheme(option.ThemeId);
                    SetScreen(PanelScreen.Settings);
                    return true;
                }
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_themeBackButtonRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Settings);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The duration chips live inside the Timed plate, so they are tested before the plates
        /// themselves — the same "options before the row that hosts them" order
        /// <see cref="HandleThemeScreenTap"/> and the language screen use for their own option lists
        /// (issue #270).
        /// </summary>
        private bool HandleModeScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
            for (int optionIndex = 0; optionIndex < _durationOptions.Count; optionIndex++)
            {
                DurationOption durationOption = _durationOptions[optionIndex];
                if (!RectTransformUtility.RectangleContainsScreenPoint(durationOption.Rect, screenPosition, eventCamera))
                {
                    continue;
                }

                // Applied immediately either way. When Timed is not yet the active mode, this also asks
                // to switch to it, folding "pick Timed, then pick a length" into the one tap plus the
                // one restart confirmation every mode switch already requires. When Timed is already
                // active, a genuinely different length restarts the run right away rather than waiting
                // for the next natural restart — the player is looking at the new length and expects
                // the run in front of them to use it, not the length it started on.
                bool lengthChanged = !Mathf.Approximately(_timedModeSystem.SelectedDuration.Value, durationOption.Seconds);
                _timedModeSystem.SelectDuration(durationOption.Seconds);
                RefreshDurationSelection();

                if (_gameModeSystem.CurrentMode.Value != GameMode.Timed)
                {
                    _pendingMode = GameMode.Timed;
                    SetScreen(PanelScreen.ModeConfirm);
                }
                else if (lengthChanged)
                {
                    // Same as the confirm dialog's RESTART (issue #360): the run is already restarted at
                    // the new length, so drop the player straight back into it instead of the Settings
                    // screen.
                    _gameModeSystem.RestartRun();
                    Close();
                }

                return true;
            }

            for (int optionIndex = 0; optionIndex < _modeOptions.Count; optionIndex++)
            {
                ModeOption option = _modeOptions[optionIndex];
                if (!RectTransformUtility.RectangleContainsScreenPoint(option.Rect, screenPosition, eventCamera))
                {
                    continue;
                }

                // Re-picking the active mode must not restart the run, so it never reaches the
                // confirmation step.
                if (option.Mode == _gameModeSystem.CurrentMode.Value)
                {
                    SetScreen(PanelScreen.Settings);
                    return true;
                }

                _pendingMode = option.Mode;
                SetScreen(PanelScreen.ModeConfirm);
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_modeBackButtonRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Settings);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The confirmation card sits on the mode screen rather than replacing it, so its two buttons
        /// are tested first and everything else falls through to the mode screen's own handling: a tap
        /// on another plate re-asks about that mode, and the back disc still leaves.
        /// </summary>
        private bool HandleConfirmScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_confirmYesRect, screenPosition, eventCamera))
            {
                // RESTART already puts the player back into a fresh run of the new mode (issue #360) —
                // staying on the Settings screen would make them tap Close themselves to see it.
                _gameModeSystem.SelectMode(_pendingMode);
                Close();
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_confirmNoRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Mode);
                return true;
            }

            return HandleModeScreenTap(screenPosition, eventCamera);
        }

        private bool HandleLanguageScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
            for (int optionIndex = 0; optionIndex < _languageOptions.Count; optionIndex++)
            {
                LanguageOption option = _languageOptions[optionIndex];
                if (!RectTransformUtility.RectangleContainsScreenPoint(option.Rect, screenPosition, eventCamera))
                {
                    continue;
                }

                // The System owns the switch and the persistence; every label on every open screen
                // re-words itself through the CurrentLocale subscriptions, this card's included.
                _localizationSystem.SetLocale(option.LocaleCode);
                SetScreen(PanelScreen.Settings);
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_languageBackButtonRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Settings);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Buys the one-time ad-removal product. The only place this card starts anything asynchronous,
        /// and it holds no logic of its own beyond the re-entrancy guard: whether the store completes,
        /// whether the receipt is honoured and what is persisted are all
        /// <see cref="AdRemovalSystem.PurchaseRemoveAdsAsync"/>'s answers, and the foot of the well
        /// repaints off <see cref="ProfileModel.AdsRemoved"/> rather than off the returned bool — so it
        /// shows the owned state whether this tap bought it or something else already had.
        /// <para>
        /// Cancelled on destroy, so a card torn down mid-prompt leaves nothing awaiting a disposed
        /// scope. The transaction itself is left to the store, which replays it on the next launch.
        /// </para>
        /// </summary>
        private async UniTaskVoid PurchaseRemoveAds()
        {
            if (_isPurchasingRemoveAds)
            {
                return;
            }

            _isPurchasingRemoveAds = true;
            RefreshRemoveAdsAction();
            try
            {
                await _adRemovalSystem.PurchaseRemoveAdsAsync(this.GetCancellationTokenOnDestroy());
            }
            finally
            {
                _isPurchasingRemoveAds = false;
                RefreshRemoveAdsAction();
            }
        }

        // ---------------------------------------------------------------------------- screens

        private void SetScreen(PanelScreen screen)
        {
            _screen = screen;

            bool isModeScreen = screen == PanelScreen.Mode || screen == PanelScreen.ModeConfirm;
            _settingsScreenRoot.SetActive(screen == PanelScreen.Settings);
            _themeScreenRoot.SetActive(screen == PanelScreen.Theme);
            _modeScreenRoot.SetActive(isModeScreen);
            _confirmCardRect.gameObject.SetActive(screen == PanelScreen.ModeConfirm);
            _languageScreenRoot.SetActive(screen == PanelScreen.Language);

            if (isModeScreen)
            {
                // The pending ring and the confirmation's wording both depend on which mode was tapped.
                RefreshModeSelection();
                RefreshConfirmTitle();
            }
        }

        // ---------------------------------------------------------------------------- repainting

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;

            // Every neutral in the mock-up is derived from the Ink/CardBackground pair instead of being
            // hard-coded: that pair is guaranteed readable in every theme by design, whereas a fixed
            // light neutral collapses against a light-ink theme (e.g. Kış's near-white ink).
            _wellPlate.color = Color.Lerp(theme.CardBackground, theme.Ink, WELL_TINT);
            Color shadowColour = WithAlpha(theme.Ink, PLATE_SHADOW_ALPHA);

            for (int imageIndex = 0; imageIndex < _platePlates.Count; imageIndex++)
            {
                _platePlates[imageIndex].color = theme.CardBackground;
            }

            for (int imageIndex = 0; imageIndex < _plateShadows.Count; imageIndex++)
            {
                _plateShadows[imageIndex].color = shadowColour;
            }

            for (int imageIndex = 0; imageIndex < _inkImages.Count; imageIndex++)
            {
                _inkImages[imageIndex].color = theme.Ink;
            }

            for (int imageIndex = 0; imageIndex < _discFaces.Count; imageIndex++)
            {
                _discFaces[imageIndex].color = theme.EmptyCellFill;
            }

            for (int imageIndex = 0; imageIndex < _discLips.Count; imageIndex++)
            {
                _discLips[imageIndex].color = theme.EmptyCellOutline;
            }

            for (int imageIndex = 0; imageIndex < _kindFills.Count; imageIndex++)
            {
                KindImage kindImage = _kindFills[imageIndex];
                kindImage.Image.color = theme.GetFill(kindImage.Kind);
            }

            for (int imageIndex = 0; imageIndex < _kindShades.Count; imageIndex++)
            {
                KindImage kindImage = _kindShades[imageIndex];
                kindImage.Image.color = theme.GetShade(kindImage.Kind);
            }

            for (int textIndex = 0; textIndex < _inkTexts.Count; textIndex++)
            {
                _inkTexts[textIndex].color = theme.Ink;
            }

            for (int textIndex = 0; textIndex < _softInkTexts.Count; textIndex++)
            {
                _softInkTexts[textIndex].color = theme.SoftInk;
            }

            // Colour ids are 1-based; 0 means "empty cell".
            for (int kindIndex = 0; kindIndex < _themeValueDots.Length; kindIndex++)
            {
                _themeValueDots[kindIndex].color = theme.GetFill(kindIndex + 1);
            }

            // The confirmation's secondary button is the empty-cell pair, its primary the call-to-action
            // kind — the same split the profile card's EDIT and SAVE buttons make.
            _confirmNoFace.color = theme.EmptyCellFill;
            _confirmNoLip.color = theme.EmptyCellOutline;
            _confirmNoText.color = theme.Ink;
            _confirmYesPlate.color = theme.GetFill(PRIMARY_KIND);
            _confirmYesText.color = theme.CardBackground;

            RefreshThemeNames();
            RefreshThemeSelection();
            RefreshModeSelection();
            RefreshDurationSelection();
            RefreshLanguageSelection();
            RefreshRemoveAdsAction();

            // Last: the bulk loop above repaints the toggle's parts too, so its state-dependent colours
            // have to be reapplied on top of it.
            OnMutedChanged(_sfxModel.IsMuted.Value);
        }

        /// <summary>
        /// Re-words every label on every screen, showing or hidden, so a screen the player has not
        /// opened yet is already in the new language when they do. The mirror image of
        /// <see cref="OnThemeChanged"/>, which does the same for colour.
        /// </summary>
        private void OnLocaleChanged(LocaleDefinition locale)
        {
            for (int labelIndex = 0; labelIndex < _localizedLabels.Count; labelIndex++)
            {
                LocalizedLabel localizedLabel = _localizedLabels[labelIndex];
                string wording = _localizationSystem.Translate(localizedLabel.Key);
                localizedLabel.Label.text = localizedLabel.Uppercase ? Uppercase(wording) : wording;
            }

            RefreshDurationOptionLabels();
            RefreshModeValue();
            RefreshSoundValue();
            RefreshThemeNames();
            RefreshConfirmTitle();

            // The language rows are the one place that shows a language's own name rather than a
            // translated string, so they are driven by the locale itself instead of the table.
            RefreshLanguageValue(locale);
            RefreshLanguageSelection();

            // Its wording is a choice between two keys rather than one fixed key, so it is outside the
            // _localizedLabels loop above and has to be re-rendered by its own handler.
            RefreshRemoveAdsAction();
        }

        private void OnMutedChanged(bool muted)
        {
            if (_toggleFace == null || _currentTheme == null)
            {
                return;
            }

            // On is the toggle kind's bevel pair, off the empty-cell pair — so the switch, like every
            // other element on the card, is painted from the theme rather than a fixed green.
            _toggleFace.color = muted ? _currentTheme.EmptyCellFill : _currentTheme.GetFill(TOGGLE_KIND);
            _toggleLip.color = muted ? _currentTheme.EmptyCellOutline : _currentTheme.GetShade(TOGGLE_KIND);
            _toggleThumbRect.anchoredPosition =
                new Vector2(muted ? -TOGGLE_THUMB_TRAVEL : TOGGLE_THUMB_TRAVEL, TOGGLE_LIP * 0.5f);

            RefreshSoundValue();
        }

        private void OnAdsRemovedChanged(bool adsRemoved) => RefreshRemoveAdsAction();

        private void OnModeChanged(GameMode mode)
        {
            if (_modeValueText == null)
            {
                return;
            }

            RefreshModeValue();
            RefreshModeSelection();
        }

        private void OnSelectedDurationChanged(float seconds) => RefreshDurationSelection();

        /// <summary>
        /// The foot of the well: the one call to action while there is something to buy, or the owned
        /// strip once the product is owned. The two share a rect, so exactly one of them is ever
        /// showing — the same pair the profile card's account row is.
        /// </summary>
        private void RefreshRemoveAdsAction()
        {
            if (_removeAdsButtonRect == null || _currentTheme == null)
            {
                return;
            }

            bool adsRemoved = _profileModel.AdsRemoved.Value;

            if (_removeAdsButtonRect.gameObject.activeSelf == adsRemoved)
            {
                _removeAdsButtonRect.gameObject.SetActive(!adsRemoved);
            }

            if (_ownedStripRect.gameObject.activeSelf != adsRemoved)
            {
                _ownedStripRect.gameObject.SetActive(adsRemoved);
            }

            if (!adsRemoved)
            {
                Color buttonColour = _currentTheme.GetFill(PRIMARY_KIND);
                _removeAdsButtonPlate.color = _isPurchasingRemoveAds
                    ? Color.Lerp(buttonColour, _currentTheme.SoftInk, BUSY_FADE)
                    : buttonColour;
                _removeAdsButtonText.color = _currentTheme.CardBackground;
                _removeAdsButtonText.text = Uppercase(_localizationSystem.Translate(LocalizationKeys.SETTINGS_REMOVE_ADS_BUY));
                return;
            }

            _ownedStripPlate.color = Color.Lerp(
                _currentTheme.CardBackground, _currentTheme.GetHighlight(OWNED_KIND), OWNED_TINT);
            _ownedCheck.color = _currentTheme.GetShade(OWNED_KIND);
            _ownedText.color = _currentTheme.GetShade(OWNED_KIND);
            _ownedText.text = _localizationSystem.Translate(LocalizationKeys.SETTINGS_REMOVE_ADS_OWNED);
        }

        /// <summary>
        /// Re-words the theme row's value and every card name in the picker, then re-seats the kind
        /// dots after the value, whose width the wording decides. Unlike the language row, theme names
        /// ARE translated (Summer/Verano/Yaz differ per locale), so both call sites resolve through
        /// <see cref="ResolveThemeName"/> instead of a raw <c>DisplayName</c>.
        /// </summary>
        private void RefreshThemeNames()
        {
            if (_themeValueText != null)
            {
                _themeValueText.text = ResolveThemeName(_settingsModel.CurrentTheme.Value);

                // preferredWidth forces the label's mesh so the dots can be placed after whatever the
                // new wording measures — a one-off on a wording change, never per frame.
                float dotsStartX = _themeValueRect.anchoredPosition.x + _themeValueText.preferredWidth + THEME_DOT_GAP;
                for (int kindIndex = 0; kindIndex < _themeValueDots.Length; kindIndex++)
                {
                    ((RectTransform)_themeValueDots[kindIndex].transform).anchoredPosition = new Vector2(
                        dotsStartX + (THEME_DOT_SIZE * 0.5f) + (kindIndex * THEME_DOT_PITCH),
                        _themeValueRect.anchoredPosition.y);
                }
            }

            IReadOnlyList<ThemeDefinition> themes = _settingsModel.AvailableThemes;
            for (int optionIndex = 0; optionIndex < _themeOptions.Count; optionIndex++)
            {
                ThemeOption option = _themeOptions[optionIndex];
                for (int themeIndex = 0; themeIndex < themes.Count; themeIndex++)
                {
                    ThemeDefinition theme = themes[themeIndex];
                    if (theme != null && theme.Id == option.ThemeId)
                    {
                        option.NameText.text = ResolveThemeName(theme);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Resolves a theme's player-facing name via its <see cref="ThemeDefinition.TranslationKey"/>,
        /// falling back to <see cref="ThemeDefinition.DisplayName"/> only when a theme asset was shipped
        /// without wiring localization — a data-completeness guard, not a table fallback (the table's
        /// own missing-key fallback lives in <see cref="LocalizationSystem.Translate"/>).
        /// </summary>
        private string ResolveThemeName(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(theme.TranslationKey))
            {
                return theme.DisplayName;
            }

            return _localizationSystem.Translate(theme.TranslationKey);
        }

        /// <summary>Repaints the theme cards' rings: the active theme wears the accent ring and the
        /// check disc, the others nothing.</summary>
        private void RefreshThemeSelection()
        {
            if (_currentTheme == null)
            {
                return;
            }

            for (int optionIndex = 0; optionIndex < _themeOptions.Count; optionIndex++)
            {
                ThemeOption option = _themeOptions[optionIndex];
                PaintSelection(option.Selection, option.ThemeId == _currentTheme.Id, _currentTheme.Accent);
            }
        }

        /// <summary>
        /// Re-words the language row's value. The name is the locale's own
        /// <see cref="LocaleDefinition.DisplayName"/> — "Türkçe", never "Turkish" — so a player who
        /// cannot read the current language can still find their own.
        /// </summary>
        private void RefreshLanguageValue(LocaleDefinition locale)
        {
            if (_languageValueText == null || locale == null)
            {
                return;
            }

            _languageValueText.text = locale.DisplayName;
        }

        /// <summary>Repaints the language plates' rings.</summary>
        private void RefreshLanguageSelection()
        {
            if (_currentTheme == null)
            {
                return;
            }

            LocaleDefinition currentLocale = _localizationModel.CurrentLocale.Value;
            string currentCode = currentLocale == null ? null : currentLocale.Code;

            for (int optionIndex = 0; optionIndex < _languageOptions.Count; optionIndex++)
            {
                LanguageOption option = _languageOptions[optionIndex];
                bool isSelected = string.Equals(option.LocaleCode, currentCode, StringComparison.OrdinalIgnoreCase);
                PaintSelection(option.Selection, isSelected, _currentTheme.Accent);
            }
        }

        /// <summary>Re-words the mode row's value from the active mode and swaps the tile's glyph to match.</summary>
        private void RefreshModeValue()
        {
            if (_modeValueText == null)
            {
                return;
            }

            GameMode current = _gameModeSystem.CurrentMode.Value;
            _modeValueText.text = _localizationSystem.Translate(ModeNameKey(current));

            for (int modeIndex = 0; modeIndex < SelectableModes.Length; modeIndex++)
            {
                bool isCurrent = SelectableModes[modeIndex] == current;
                if (_modeRowGlyphs[modeIndex].activeSelf != isCurrent)
                {
                    _modeRowGlyphs[modeIndex].SetActive(isCurrent);
                }
            }
        }

        private void RefreshSoundValue()
        {
            if (_soundValueText == null)
            {
                return;
            }

            _soundValueText.text = _localizationSystem.Translate(_sfxModel.IsMuted.Value
                ? LocalizationKeys.SETTINGS_SOUND_OFF
                : LocalizationKeys.SETTINGS_SOUND_ON);
        }

        /// <summary>Re-renders the duration tiles, whose wording is a number in the minutes format.</summary>
        private void RefreshDurationOptionLabels()
        {
            for (int optionIndex = 0; optionIndex < _durationOptions.Count; optionIndex++)
            {
                DurationOption option = _durationOptions[optionIndex];
                option.NameText.text = FormatDuration(option.Seconds);
            }
        }

        private static string ModeNameKey(GameMode mode)
        {
            return mode switch
            {
                GameMode.Timed => LocalizationKeys.MODE_TIMED,
                GameMode.Path => LocalizationKeys.MODE_PATH,
                _ => LocalizationKeys.MODE_ENDLESS,
            };
        }

        private static string ModeDescriptionKey(GameMode mode)
        {
            return mode switch
            {
                GameMode.Timed => LocalizationKeys.MODE_TIMED_DESCRIPTION,
                GameMode.Path => LocalizationKeys.MODE_PATH_DESCRIPTION,
                _ => LocalizationKeys.MODE_ENDLESS_DESCRIPTION,
            };
        }

        /// <summary>Which kind's bevel triplet a mode's tile takes, on the row and in the picker.</summary>
        private static int ModeKind(GameMode mode)
        {
            return mode switch
            {
                GameMode.Timed => DURATION_KIND,
                GameMode.Path => LANGUAGE_KIND,
                _ => MODE_KIND,
            };
        }

        /// <summary>
        /// Records <paramref name="label"/> as rendering <paramref name="key"/> and paints it once, so
        /// a label is correct from the moment it is built rather than only after the first switch.
        /// </summary>
        private void RegisterLocalized(Text label, string key, bool uppercase = false)
        {
            _localizedLabels.Add(new LocalizedLabel(label, key, uppercase));
            string wording = _localizationSystem.Translate(key);
            label.text = uppercase ? Uppercase(wording) : wording;
        }

        /// <summary>
        /// Capitalises in the current language rather than invariantly: Turkish has a dotted capital İ,
        /// and the invariant rules would turn "Dil" into "DIL". Falls back to invariant for a locale
        /// code the runtime does not know.
        /// </summary>
        private string Uppercase(string wording)
        {
            if (string.IsNullOrEmpty(wording))
            {
                return wording;
            }

            LocaleDefinition locale = _localizationModel.CurrentLocale.Value;
            if (locale == null || string.IsNullOrEmpty(locale.Code))
            {
                return wording.ToUpperInvariant();
            }

            try
            {
                return wording.ToUpper(CultureInfo.GetCultureInfo(locale.Code));
            }
            catch (CultureNotFoundException)
            {
                return wording.ToUpperInvariant();
            }
        }

        /// <summary>Repaints the duration tiles' rings.</summary>
        private void RefreshDurationSelection()
        {
            if (_currentTheme == null)
            {
                return;
            }

            float selected = _timedModeSystem.SelectedDuration.Value;
            for (int optionIndex = 0; optionIndex < _durationOptions.Count; optionIndex++)
            {
                DurationOption option = _durationOptions[optionIndex];
                PaintSelection(option.Selection, Mathf.Approximately(option.Seconds, selected), _currentTheme.Accent);
            }
        }

        /// <summary>
        /// Renders a round length through the shared minutes format, the same one the best-score suffix
        /// and the game-over card use, so a length is spelled identically everywhere it is named. Round
        /// lengths are whole minutes, so the countdown's own mm:ss clock is not reused here.
        /// </summary>
        private string FormatDuration(float seconds)
        {
            // The "Sınırsız" (endless/no countdown) entry (issue #355) is a duration outside the
            // minutes format's domain — "0 dk" would misname it — so it gets its own key instead.
            if (TimedModeConfig.IsEndlessDuration(seconds))
            {
                return _localizationSystem.Translate(LocalizationKeys.DURATION_ENDLESS);
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(Mathf.RoundToInt(seconds / 60f));
            return _localizationSystem.Format(LocalizationKeys.FORMAT_MINUTES, _stringBuilder.ToString());
        }

        /// <summary>
        /// Repaints the mode plates: the mode being played wears the accent ring and its PLAYING tag;
        /// while the confirmation is up, the mode it asks about wears the call-to-action kind's ring
        /// instead, so the eye goes from that plate to the RESTART button of the same colour.
        /// </summary>
        private void RefreshModeSelection()
        {
            if (_currentTheme == null)
            {
                return;
            }

            GameMode current = _gameModeSystem.CurrentMode.Value;
            bool isConfirming = _screen == PanelScreen.ModeConfirm;

            for (int optionIndex = 0; optionIndex < _modeOptions.Count; optionIndex++)
            {
                ModeOption option = _modeOptions[optionIndex];
                bool isPlaying = option.Mode == current;
                bool isPending = isConfirming && option.Mode == _pendingMode;

                // While a switch is pending confirmation, only the newly-tapped plate rings — the
                // active mode's ring drops immediately rather than sitting lit alongside it, so the
                // player never sees two plates highlighted at once.
                bool showRing = isConfirming ? isPending : isPlaying;
                Color ringColour = isPending ? _currentTheme.GetShade(PRIMARY_KIND) : _currentTheme.Accent;
                PaintSelection(option.Selection, showRing, ringColour);
                option.PlayingText.color = isPlaying ? _currentTheme.Accent : Color.clear;
            }
        }

        /// <summary>Re-words the confirmation's title around the mode it asks about.</summary>
        private void RefreshConfirmTitle()
        {
            if (_confirmTitleText == null)
            {
                return;
            }

            _confirmTitleText.text = _localizationSystem.Format(
                LocalizationKeys.SETTINGS_CONFIRM_SWITCH_TITLE,
                _localizationSystem.Translate(ModeNameKey(_pendingMode)));
        }

        /// <summary>Shows or clears a plate's ring and check disc.</summary>
        private void PaintSelection(Selection selection, bool isSelected, Color ringColour)
        {
            selection.Ring.color = isSelected ? ringColour : Color.clear;
            selection.Gap.color = isSelected ? _currentTheme.CardBackground : Color.clear;

            if (selection.CheckDisc == null)
            {
                return;
            }

            selection.CheckLip.color = isSelected ? Darken(_currentTheme.Accent, ACCENT_SHADE) : Color.clear;
            selection.CheckDisc.color = isSelected ? _currentTheme.Accent : Color.clear;
            selection.CheckGlyph.color = isSelected ? Color.white : Color.clear;
        }

        // ---------------------------------------------------------------------------- building

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
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
                panelRect, "SettingsCard", _cardSize, out _cardImage, out _cardShadowImage);

            // The card's own title and close cross, kept for a stand-alone open; the hub hides both.
            float cardHalfWidth = _cardSize.x * 0.5f;
            float headerY = (_cardSize.y * 0.5f) - HEADER_INSET;
            _titleText = CreateLabel(
                _cardRect, "Title", 64, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-cardHalfWidth + SIDE_INSET, headerY));
            _inkTexts.Add(_titleText);
            RegisterLocalized(_titleText, LocalizationKeys.SETTINGS_TITLE);
            BuildCloseButton(_cardRect, new Vector2(cardHalfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f), headerY));

            BuildWell();

            _settingsScreenRoot = CreateScreenRoot("SettingsScreen");
            _themeScreenRoot = CreateScreenRoot("ThemeScreen");
            _modeScreenRoot = CreateScreenRoot("ModeScreen");
            _languageScreenRoot = CreateScreenRoot("LanguageScreen");

            BuildSettingsScreen((RectTransform)_settingsScreenRoot.transform);
            BuildThemeScreen((RectTransform)_themeScreenRoot.transform);
            BuildModeScreen((RectTransform)_modeScreenRoot.transform);
            BuildLanguageScreen((RectTransform)_languageScreenRoot.transform);

            _panel = panelObject;
        }

        private GameObject CreateScreenRoot(string objectName)
        {
            var screenObject = new GameObject(objectName, typeof(RectTransform));
            var screenRect = (RectTransform)screenObject.transform;
            screenRect.SetParent(_cardRect, false);
            screenRect.anchorMin = Vector2.zero;
            screenRect.anchorMax = Vector2.one;
            screenRect.offsetMin = Vector2.zero;
            screenRect.offsetMax = Vector2.zero;
            return screenObject;
        }

        /// <summary>The sunken plate under everything: one rounded Image, drawn before the screens so
        /// every plate sits on it.</summary>
        private void BuildWell()
        {
            var wellObject = new GameObject("Well", typeof(RectTransform), typeof(Image));
            var wellRect = (RectTransform)wellObject.transform;
            wellRect.SetParent(_cardRect, false);
            wellRect.anchorMin = Vector2.zero;
            wellRect.anchorMax = Vector2.one;
            wellRect.pivot = new Vector2(0.5f, 0.5f);
            wellRect.offsetMin = new Vector2(WELL_INSET, WELL_INSET);
            wellRect.offsetMax = new Vector2(-WELL_INSET, -WELL_INSET);
            _wellPlate = ConfigureRounded(wellObject.GetComponent<Image>(), WELL_CORNER_RADIUS);
        }

        /// <summary>Width of the well's content column: the card less the well inset and padding on both sides.</summary>
        private float ContentWidth => _cardSize.x - ((WELL_INSET + WELL_PADDING_X) * 2f);

        /// <summary>Distance from the card's top edge to the well's first content row.</summary>
        private const float CONTENT_TOP = WELL_INSET + WELL_PADDING_Y;

        /// <summary>Distance from the card's top edge to a sub-screen's first content row, below its header row.</summary>
        private const float SUB_CONTENT_TOP = CONTENT_TOP + SUB_HEADER_HEIGHT + ROW_GAP;

        private void BuildSettingsScreen(RectTransform root)
        {
            float contentWidth = ContentWidth;

            _modeRowRect = BuildRow(root, 0, "ModeRow", MODE_KIND, LocalizationKeys.SETTINGS_ROW_MODE, out _, out _modeValueText, out RectTransform modeTile);
            _themeRowRect = BuildRow(root, 1, "ThemeRow", THEME_KIND, LocalizationKeys.SETTINGS_ROW_THEME, out _, out _themeValueText, out RectTransform themeTile);

            // Next to the theme row rather than at the bottom: both are "how the game looks and
            // reads", and a player hunting for the language in a script they cannot read finds it
            // faster in the top half of the list.
            _languageRowRect = BuildRow(root, 2, "LanguageRow", LANGUAGE_KIND, LocalizationKeys.SETTINGS_ROW_LANGUAGE, out _, out _languageValueText, out RectTransform languageTile);
            _soundRowRect = BuildRow(root, 3, "SoundRow", SOUND_KIND, LocalizationKeys.SETTINGS_ROW_SOUND, out _, out _soundValueText, out RectTransform soundTile);

            _themeValueRect = (RectTransform)_themeValueText.transform;

            // The mode row's tile shows whichever mode is being played; all three glyphs are built and
            // RefreshModeValue shows one.
            for (int modeIndex = 0; modeIndex < SelectableModes.Length; modeIndex++)
            {
                _modeRowGlyphs[modeIndex] = BuildModeGlyph(modeTile, SelectableModes[modeIndex], MODE_KIND);
            }

            BuildPaletteGlyph(themeTile, THEME_KIND);
            BuildGlobeGlyph(languageTile, LANGUAGE_KIND);
            BuildVolumeGlyph(soundTile);

            BuildChevronDisc(_modeRowRect, contentWidth);
            BuildChevronDisc(_themeRowRect, contentWidth);
            BuildChevronDisc(_languageRowRect, contentWidth);
            BuildToggle(_soundRowRect, contentWidth);

            // Same dots as the theme cards' boards use, just smaller, moved into the value: one visual
            // language for "theme". Positioned after the value by RefreshThemeNames, since the wording
            // decides where they start.
            for (int kindIndex = 0; kindIndex < ThemeDefinition.KIND_COUNT; kindIndex++)
            {
                _themeValueDots[kindIndex] = BuildRounded(
                    _themeRowRect, $"Kind_{kindIndex}", new Vector2(THEME_DOT_SIZE, THEME_DOT_SIZE),
                    Vector2.zero, THEME_DOT_SIZE * 0.3f);
            }

            BuildRemoveAdsAction(root, contentWidth);
        }

        /// <summary>
        /// One settings plate: shadow, plate, the kind-coloured tile and the label/value pair. The
        /// value label is handed back for the row's own refresh method to word; the tile for the row's
        /// glyph.
        /// </summary>
        private RectTransform BuildRow(
            RectTransform root, int rowIndex, string objectName, int kind, string labelKey,
            out Text labelText, out Text valueText, out RectTransform tileRect)
        {
            RectTransform rowRect = BuildPlate(
                root, objectName, new Vector2(ContentWidth, ROW_HEIGHT),
                new Vector2(0f, TopY(CONTENT_TOP + (rowIndex * (ROW_HEIGHT + ROW_GAP)), ROW_HEIGHT)));

            tileRect = BuildKindTile(rowRect, kind, new Vector2((-ContentWidth * 0.5f) + ROW_PADDING_X + (TILE_SIZE * 0.5f), 0f));

            float textX = (-ContentWidth * 0.5f) + ROW_TEXT_INSET;
            labelText = CreateLabel(
                rowRect, "Label", LABEL_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(textX, ROW_LABEL_RISE));
            _softInkTexts.Add(labelText);
            RegisterLocalized(labelText, labelKey, uppercase: true);

            valueText = CreateLabel(
                rowRect, "Value", VALUE_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(textX, -ROW_VALUE_DROP), _displayFont);
            _inkTexts.Add(valueText);

            return rowRect;
        }

        /// <summary>
        /// A plate: a rounded rect in the card colour over a copy of itself dropped a few pixels in Ink
        /// at low alpha. The returned rect is the plate's own; children position relative to it, and it
        /// is the tap target.
        /// </summary>
        private RectTransform BuildPlate(RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition)
        {
            var rootObject = new GameObject(objectName, typeof(RectTransform));
            var rootRect = (RectTransform)rootObject.transform;
            rootRect.SetParent(parent, false);
            Centre(rootRect, size);
            rootRect.anchoredPosition = anchoredPosition;

            _plateShadows.Add(BuildRounded(rootRect, "Shadow", size, new Vector2(0f, -PLATE_SHADOW_DROP), PLATE_CORNER_RADIUS));
            _platePlates.Add(BuildRounded(rootRect, "Plate", size, Vector2.zero, PLATE_CORNER_RADIUS));
            return rootRect;
        }

        /// <summary>
        /// A plate that can be chosen: the same as <see cref="BuildPlate"/> with an accent ring and a
        /// card-coloured gap behind it, both clear until <see cref="PaintSelection"/> shows them. The
        /// check disc, if wanted, is added by <see cref="AddCheckDisc"/> once the plate's contents are
        /// in, so it draws on top of them.
        /// </summary>
        private RectTransform BuildSelectablePlate(
            RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition, out Selection selection)
        {
            var rootObject = new GameObject(objectName, typeof(RectTransform));
            var rootRect = (RectTransform)rootObject.transform;
            rootRect.SetParent(parent, false);
            Centre(rootRect, size);
            rootRect.anchoredPosition = anchoredPosition;

            Image ring = BuildRounded(
                rootRect, "Ring", size + (Vector2.one * (RING_OUTSET * 2f)), Vector2.zero, PLATE_CORNER_RADIUS + RING_OUTSET);
            Image gap = BuildRounded(
                rootRect, "RingGap", size + (Vector2.one * (RING_GAP_OUTSET * 2f)), Vector2.zero, PLATE_CORNER_RADIUS + RING_GAP_OUTSET);
            _plateShadows.Add(BuildRounded(rootRect, "Shadow", size, new Vector2(0f, -PLATE_SHADOW_DROP), PLATE_CORNER_RADIUS));
            _platePlates.Add(BuildRounded(rootRect, "Plate", size, Vector2.zero, PLATE_CORNER_RADIUS));

            selection = new Selection(ring, gap);
            return rootRect;
        }

        /// <summary>The check disc in a chosen plate's top-right corner: accent over a darker lip, white tick.</summary>
        private static void AddCheckDisc(RectTransform plateRect, Selection selection)
        {
            Vector2 centre = new Vector2(
                (plateRect.sizeDelta.x * 0.5f) - CHECK_DISC_INSET - (CHECK_DISC_SIZE * 0.5f),
                (plateRect.sizeDelta.y * 0.5f) - CHECK_DISC_INSET - (CHECK_DISC_SIZE * 0.5f));

            selection.CheckLip = BuildCircle(plateRect, "CheckLip", CHECK_DISC_SIZE, centre + new Vector2(0f, -CHECK_DISC_LIP));
            selection.CheckDisc = BuildCircle(plateRect, "CheckDisc", CHECK_DISC_SIZE, centre);
            selection.CheckGlyph = BuildGlyph(
                plateRect, "Check", UiSpriteFactory.CheckMark, new Vector2(CHECK_GLYPH_SIZE, CHECK_GLYPH_SIZE), centre);
        }

        /// <summary>A kind-coloured tile: a rounded square in the kind's fill over a taller one in its
        /// shade, so the shade shows as a lip along the bottom. Returns the tile's rect for the glyph.</summary>
        private RectTransform BuildKindTile(RectTransform parent, int kind, Vector2 anchoredPosition)
        {
            var tileObject = new GameObject("Tile", typeof(RectTransform));
            var tileRect = (RectTransform)tileObject.transform;
            tileRect.SetParent(parent, false);
            Centre(tileRect, new Vector2(TILE_SIZE, TILE_SIZE));
            tileRect.anchoredPosition = anchoredPosition;

            _kindShades.Add(new KindImage(
                BuildRounded(tileRect, "Shade", new Vector2(TILE_SIZE, TILE_SIZE), Vector2.zero, TILE_CORNER_RADIUS), kind));
            _kindFills.Add(new KindImage(
                BuildRounded(tileRect, "Fill", new Vector2(TILE_SIZE, TILE_SIZE - TILE_LIP), new Vector2(0f, TILE_LIP * 0.5f), TILE_CORNER_RADIUS), kind));
            return tileRect;
        }

        /// <summary>The right-hand chevron disc on a row that steps to a picker: a disc in the empty-cell
        /// pair with the same lip the tiles have, and an Ink chevron on it.</summary>
        private void BuildChevronDisc(RectTransform rowRect, float contentWidth)
        {
            var discCentre = new Vector2((contentWidth * 0.5f) - ROW_PADDING_X - (DISC_SIZE * 0.5f), 0f);
            _discLips.Add(BuildCircle(rowRect, "DiscLip", DISC_SIZE, discCentre + new Vector2(0f, -DISC_LIP)));
            _discFaces.Add(BuildCircle(rowRect, "DiscFace", DISC_SIZE, discCentre));
            BuildChevron(rowRect, discCentre + new Vector2(2f, 0f), 1f);
        }

        /// <summary>
        /// The sound toggle: a rounded track with the same lip the tiles have, and a white thumb that
        /// slides between its two ends. Painted by <see cref="OnMutedChanged"/>.
        /// </summary>
        private void BuildToggle(RectTransform rowRect, float contentWidth)
        {
            var trackObject = new GameObject("Toggle", typeof(RectTransform));
            var trackRect = (RectTransform)trackObject.transform;
            trackRect.SetParent(rowRect, false);
            Centre(trackRect, new Vector2(TOGGLE_WIDTH, TOGGLE_HEIGHT));
            trackRect.anchoredPosition = new Vector2((contentWidth * 0.5f) - ROW_PADDING_X - (TOGGLE_WIDTH * 0.5f), 0f);

            _toggleLip = BuildRounded(trackRect, "Lip", new Vector2(TOGGLE_WIDTH, TOGGLE_HEIGHT), Vector2.zero, TOGGLE_HEIGHT * 0.5f);
            _toggleFace = BuildRounded(
                trackRect, "Face", new Vector2(TOGGLE_WIDTH, TOGGLE_HEIGHT - TOGGLE_LIP), new Vector2(0f, TOGGLE_LIP * 0.5f),
                (TOGGLE_HEIGHT - TOGGLE_LIP) * 0.5f);

            Image thumb = BuildCircle(trackRect, "Thumb", TOGGLE_THUMB_SIZE, new Vector2(TOGGLE_THUMB_TRAVEL, TOGGLE_LIP * 0.5f));
            thumb.color = Color.white;
            _toggleThumbRect = (RectTransform)thumb.transform;
        }

        /// <summary>
        /// The foot of the well: the caption, the glossy Remove Ads button and, sharing its rect, the
        /// owned strip that replaces it. <see cref="RefreshRemoveAdsAction"/> shows one of the two.
        /// </summary>
        private void BuildRemoveAdsAction(RectTransform root, float contentWidth)
        {
            var actionSize = new Vector2(contentWidth, ACTION_HEIGHT);
            float actionTop = _cardSize.y - WELL_INSET - WELL_PADDING_Y - ACTION_HEIGHT;
            float actionY = TopY(actionTop, ACTION_HEIGHT);

            Text captionText = CreateLabel(
                root, "AdsCaption", LABEL_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2((-contentWidth * 0.5f) + 12f, TopY(actionTop - ACTION_CAPTION_RISE, 0f)));
            _softInkTexts.Add(captionText);
            RegisterLocalized(captionText, LocalizationKeys.SETTINGS_REMOVE_ADS_CAPTION, uppercase: true);

            _removeAdsButtonRect = BuildGlossyButton(root, "RemoveAdsButton", actionSize, out _removeAdsButtonPlate);
            _removeAdsButtonRect.anchoredPosition = new Vector2(0f, actionY);
            _removeAdsButtonText = CreateLabel(
                _removeAdsButtonRect, "RemoveAdsLabel", BUTTON_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, BUTTON_LABEL_RISE), _displayFont);

            var stripObject = new GameObject("OwnedStrip", typeof(RectTransform), typeof(Image));
            _ownedStripRect = (RectTransform)stripObject.transform;
            _ownedStripRect.SetParent(root, false);
            Centre(_ownedStripRect, actionSize);
            _ownedStripRect.anchoredPosition = new Vector2(0f, actionY);
            _ownedStripPlate = ConfigureRounded(stripObject.GetComponent<Image>(), PLATE_CORNER_RADIUS);

            const float CHECK_SIZE = 32f;
            const float STRIP_PADDING = 28f;
            _ownedCheck = BuildGlyph(
                _ownedStripRect, "Check", UiSpriteFactory.CheckMark, new Vector2(CHECK_SIZE, CHECK_SIZE),
                new Vector2((-contentWidth * 0.5f) + STRIP_PADDING + (CHECK_SIZE * 0.5f), 0f));
            _ownedText = CreateLabel(
                _ownedStripRect, "OwnedLabel", DESCRIPTION_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2((-contentWidth * 0.5f) + STRIP_PADDING + CHECK_SIZE + 16f, 0f));
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
                Image barImage = BuildRounded(
                    _closeButtonRect, $"CloseBar_{barIndex}", new Vector2(CROSS_LENGTH, CROSS_THICKNESS), Vector2.zero,
                    CROSS_THICKNESS * 0.5f);
                barImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, barIndex == 0 ? 45f : -45f);
                _inkImages.Add(barImage);
            }
        }

        /// <summary>
        /// A glossy 3D button: the shop's white button sprite, sliced, tinted at paint time with a
        /// kind's fill — its baked highlight and lip supply the bevel. The returned rect is the hit
        /// area; the caller positions it and adds its label.
        /// </summary>
        private RectTransform BuildGlossyButton(RectTransform parent, string objectName, Vector2 size, out Image plate)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(parent, false);
            Centre(buttonRect, size);

            plate = buttonObject.GetComponent<Image>();
            if (_buttonSprite != null)
            {
                plate.sprite = _buttonSprite;
                plate.type = Image.Type.Sliced;
                plate.pixelsPerUnitMultiplier = BUTTON_SLICE_SCALE;
                plate.color = Color.clear;
                plate.raycastTarget = false;
            }
            else
            {
                ConfigureRounded(plate, PLATE_CORNER_RADIUS);
            }

            return buttonRect;
        }

        // ---------------------------------------------------------------------------- glyphs

        /// <summary>
        /// The glyph a mode's tile wears, white on the kind's fill: two overlapping rings for endless (a
        /// loose nod to the infinity mark), a clock for timed, and two stops joined by a rise for path.
        /// The tile's fill kind is needed for the fake cut-outs that turn discs into rings.
        /// </summary>
        private GameObject BuildModeGlyph(RectTransform tileRect, GameMode mode, int tileKind)
        {
            var glyphObject = new GameObject($"Glyph_{mode}", typeof(RectTransform));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(tileRect, false);
            Centre(glyphRect, new Vector2(TILE_SIZE, TILE_SIZE));

            switch (mode)
            {
                case GameMode.Timed:
                    BuildClockGlyph(glyphRect, tileKind);
                    break;
                case GameMode.Path:
                    BuildTrailGlyph(glyphRect);
                    break;
                default:
                    BuildInfinityGlyph(glyphRect, tileKind);
                    break;
            }

            return glyphObject;
        }

        /// <summary>Two overlapping rings — enough to read as "endless" at tile size.</summary>
        private void BuildInfinityGlyph(RectTransform parent, int tileKind)
        {
            const float RING_DIAMETER = 40f;
            const float RING_OVERLAP = 13f;
            const float RING_THICKNESS = 7f;

            for (int discIndex = 0; discIndex < 2; discIndex++)
            {
                BuildCircle(parent, $"InfinityDisc_{discIndex}", RING_DIAMETER, new Vector2(RingOffsetX(discIndex), 0f)).color = Color.white;
            }

            // Fake cut-out: the tile underneath is one opaque colour, so a smaller circle in that
            // colour turns each disc into a ring. Both holes are drawn after both discs so neither disc
            // fills the other's hole.
            for (int holeIndex = 0; holeIndex < 2; holeIndex++)
            {
                _kindFills.Add(new KindImage(
                    BuildCircle(parent, $"InfinityHole_{holeIndex}", RING_DIAMETER - (RING_THICKNESS * 2f), new Vector2(RingOffsetX(holeIndex), 0f)),
                    tileKind));
            }

            float RingOffsetX(int index)
                => (index == 0 ? -1f : 1f) * ((RING_DIAMETER * 0.5f) - (RING_OVERLAP * 0.5f));
        }

        /// <summary>Ring plus a single off-vertical hand — enough to read as a clock at tile size.</summary>
        private void BuildClockGlyph(RectTransform parent, int tileKind)
        {
            const float DIAL_DIAMETER = 54f;
            const float FACE_DIAMETER = 40f;
            const float HAND_LENGTH = 17f;
            const float HAND_ANGLE = -35f;

            BuildCircle(parent, "ClockDial", DIAL_DIAMETER, Vector2.zero).color = Color.white;
            _kindFills.Add(new KindImage(BuildCircle(parent, "ClockFace", FACE_DIAMETER, Vector2.zero), tileKind));

            Image hand = BuildRounded(parent, "ClockHand", new Vector2(6f, HAND_LENGTH), Vector2.zero, 3f);
            hand.rectTransform.localRotation = Quaternion.Euler(0f, 0f, HAND_ANGLE);

            // Pushed half its own length along its rotated axis so the base sits on the centre.
            hand.rectTransform.anchoredPosition =
                (Vector2)(Quaternion.Euler(0f, 0f, HAND_ANGLE) * new Vector3(0f, HAND_LENGTH * 0.5f, 0f));
            hand.color = Color.white;
        }

        /// <summary>Two stops joined by a rise and a fall — the trail, as the level path draws it.</summary>
        private void BuildTrailGlyph(RectTransform parent)
        {
            const float STOP_DIAMETER = 18f;
            const float STOP_X = 26f;
            const float STOP_Y = -14f;
            const float LEG_LENGTH = 40f;
            const float LEG_THICKNESS = 7f;
            const float LEG_ANGLE = 52f;

            for (int legIndex = 0; legIndex < 2; legIndex++)
            {
                float sign = legIndex == 0 ? -1f : 1f;
                Image leg = BuildRounded(
                    parent, $"TrailLeg_{legIndex}", new Vector2(LEG_LENGTH, LEG_THICKNESS),
                    new Vector2(sign * 13f, 2f), LEG_THICKNESS * 0.5f);
                leg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -sign * LEG_ANGLE);
                leg.color = Color.white;
            }

            for (int stopIndex = 0; stopIndex < 2; stopIndex++)
            {
                float sign = stopIndex == 0 ? -1f : 1f;
                BuildCircle(parent, $"TrailStop_{stopIndex}", STOP_DIAMETER, new Vector2(sign * STOP_X, STOP_Y)).color = Color.white;
            }
        }

        /// <summary>A painter's palette: a white disc with a thumb hole and three paint dabs in the
        /// theme's own kind fills, so the tile previews the very thing the row changes.</summary>
        private void BuildPaletteGlyph(RectTransform tileRect, int tileKind)
        {
            const float PALETTE_DIAMETER = 56f;
            const float HOLE_DIAMETER = 16f;
            const float DAB_DIAMETER = 12f;

            BuildCircle(tileRect, "Palette", PALETTE_DIAMETER, Vector2.zero).color = Color.white;
            _kindFills.Add(new KindImage(BuildCircle(tileRect, "PaletteHole", HOLE_DIAMETER, new Vector2(13f, -13f)), tileKind));

            // Dabs in kinds other than the tile's own, so none of them vanishes into the tile.
            int[] dabKinds = { 1, 2, 5 };
            Vector2[] dabCentres = { new Vector2(-14f, 10f), new Vector2(2f, 16f), new Vector2(-16f, -8f) };
            for (int dabIndex = 0; dabIndex < dabKinds.Length; dabIndex++)
            {
                _kindFills.Add(new KindImage(
                    BuildCircle(tileRect, $"PaletteDab_{dabIndex}", DAB_DIAMETER, dabCentres[dabIndex]), dabKinds[dabIndex]));
            }
        }

        /// <summary>
        /// Ring plus an equator and a meridian — the usual globe, built from the same ring-and-bars
        /// parts as the clock glyph so the tiles stay one family.
        /// </summary>
        private void BuildGlobeGlyph(RectTransform tileRect, int tileKind)
        {
            const float GLOBE_DIAMETER = 54f;
            const float GLOBE_FACE_DIAMETER = 42f;
            const float MERIDIAN_WIDTH = 26f;
            const float LINE_THICKNESS = 6f;

            BuildCircle(tileRect, "GlobeOutline", GLOBE_DIAMETER, Vector2.zero).color = Color.white;

            // Same fake cut-out as the clock dial: the tile underneath is one opaque colour, so a
            // smaller circle in that colour turns the disc into a ring.
            _kindFills.Add(new KindImage(BuildCircle(tileRect, "GlobeFace", GLOBE_FACE_DIAMETER, Vector2.zero), tileKind));

            // A narrow ellipse would be truer, but the sprite set has no ellipse; a narrow rounded
            // rect reads the same at tile size. Drawn after the cut-out so it survives it.
            BuildRounded(tileRect, "GlobeMeridian", new Vector2(MERIDIAN_WIDTH, GLOBE_DIAMETER), Vector2.zero, MERIDIAN_WIDTH * 0.5f).color = Color.white;

            // Hollows the meridian out so it reads as an outline rather than a filled capsule.
            _kindFills.Add(new KindImage(
                BuildRounded(
                    tileRect, "GlobeMeridianHole",
                    new Vector2(MERIDIAN_WIDTH - (LINE_THICKNESS * 2f), GLOBE_DIAMETER - (LINE_THICKNESS * 2f)),
                    Vector2.zero, (MERIDIAN_WIDTH - (LINE_THICKNESS * 2f)) * 0.5f),
                tileKind));

            // Last of all: the meridian's own cut-out would otherwise punch a gap out of its middle.
            BuildRounded(tileRect, "GlobeEquator", new Vector2(GLOBE_DIAMETER, LINE_THICKNESS), Vector2.zero, LINE_THICKNESS * 0.5f).color = Color.white;
        }

        /// <summary>Three ascending bars, bottom-aligned — the usual "volume" glyph.</summary>
        private static void BuildVolumeGlyph(RectTransform tileRect)
        {
            const int BAR_COUNT = 3;
            const float BAR_WIDTH = 11f;
            const float BAR_SPACING = 21f;
            const float BAR_BASE_Y = -26f;

            for (int barIndex = 0; barIndex < BAR_COUNT; barIndex++)
            {
                float barHeight = 24f + (barIndex * 14f);
                BuildRounded(
                    tileRect, $"VolumeBar_{barIndex}", new Vector2(BAR_WIDTH, barHeight),
                    new Vector2((barIndex - ((BAR_COUNT - 1) * 0.5f)) * BAR_SPACING, BAR_BASE_Y + (barHeight * 0.5f)),
                    BAR_WIDTH * 0.5f).color = Color.white;
            }
        }

        /// <summary>
        /// Two rotated bars meeting at a point, the same trick <see cref="CellView"/> uses for its
        /// bevel facets. <paramref name="directionX"/> is +1 for a right chevron, -1 for a left one.
        /// </summary>
        private void BuildChevron(RectTransform parent, Vector2 centre, float directionX)
        {
            float armLength = (CHEVRON_HALF_SIZE * Mathf.Sqrt(2f)) + CHEVRON_THICKNESS;

            for (int armIndex = 0; armIndex < 2; armIndex++)
            {
                float sign = armIndex == 0 ? 1f : -1f;
                Image armImage = BuildRounded(
                    parent, $"ChevronArm_{armIndex}", new Vector2(armLength, CHEVRON_THICKNESS),
                    centre + new Vector2(0f, sign * CHEVRON_HALF_SIZE * 0.5f), CHEVRON_THICKNESS * 0.5f);
                armImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f * sign * directionX);
                _inkImages.Add(armImage);
            }
        }

        // ---------------------------------------------------------------------------- sub-screens

        /// <summary>
        /// A sub-screen's first row: the round back disc and the screen's name in the display face,
        /// with an optional uppercase hint on the right. Returns the back disc's rect, the tap target.
        /// </summary>
        private RectTransform BuildSubHeader(RectTransform root, string titleKey, string hintKey)
        {
            float contentWidth = ContentWidth;
            float headerY = TopY(CONTENT_TOP, SUB_HEADER_HEIGHT);

            var backObject = new GameObject("BackButton", typeof(RectTransform));
            var backRect = (RectTransform)backObject.transform;
            backRect.SetParent(root, false);
            Centre(backRect, new Vector2(BACK_DISC_SIZE, BACK_DISC_SIZE));
            backRect.anchoredPosition = new Vector2((-contentWidth * 0.5f) + (BACK_DISC_SIZE * 0.5f), headerY);

            _plateShadows.Add(BuildCircle(backRect, "Shadow", BACK_DISC_SIZE, new Vector2(0f, -PLATE_SHADOW_DROP)));
            _platePlates.Add(BuildCircle(backRect, "Disc", BACK_DISC_SIZE, Vector2.zero));
            BuildChevron(backRect, new Vector2(-2f, 0f), -1f);

            Text title = CreateLabel(
                root, "Title", TITLE_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2((-contentWidth * 0.5f) + BACK_DISC_SIZE + 24f, headerY), _displayFont);
            _inkTexts.Add(title);
            RegisterLocalized(title, titleKey);

            if (hintKey != null)
            {
                Text hint = CreateLabel(
                    root, "Hint", LABEL_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleRight,
                    new Vector2((contentWidth * 0.5f) - 8f, headerY));
                _softInkTexts.Add(hint);
                RegisterLocalized(hint, hintKey, uppercase: true);
            }

            return backRect;
        }

        private void BuildThemeScreen(RectTransform root)
        {
            _themeBackButtonRect = BuildSubHeader(root, LocalizationKeys.SETTINGS_THEME_SCREEN_TITLE, LocalizationKeys.SETTINGS_THEME_SCREEN_HINT);

            IReadOnlyList<ThemeDefinition> themes = _settingsModel.AvailableThemes;
            float cardWidth = (ContentWidth - (THEME_CARD_GAP * (THEME_COLUMN_COUNT - 1))) / THEME_COLUMN_COUNT;
            var cardSize = new Vector2(cardWidth, THEME_CARD_HEIGHT);

            for (int themeIndex = 0; themeIndex < themes.Count; themeIndex++)
            {
                ThemeDefinition theme = themes[themeIndex];
                if (theme == null)
                {
                    continue;
                }

                int column = themeIndex % THEME_COLUMN_COUNT;
                int row = themeIndex / THEME_COLUMN_COUNT;

                float x = (column - ((THEME_COLUMN_COUNT - 1) * 0.5f)) * (cardWidth + THEME_CARD_GAP);
                float y = TopY(SUB_CONTENT_TOP + (row * (THEME_CARD_HEIGHT + THEME_CARD_GAP)), THEME_CARD_HEIGHT);

                _themeOptions.Add(BuildThemeOption(root, theme, cardSize, new Vector2(x, y)));
            }
        }

        /// <summary>
        /// One theme card: its own gradient with a mini board in its own kind colours, its name below,
        /// and the ring and check disc the active one wears. The preview never changes — it shows its
        /// own theme, not the active one — so only the ring, the disc and the name follow the theme.
        /// </summary>
        private ThemeOption BuildThemeOption(RectTransform root, ThemeDefinition theme, Vector2 cardSize, Vector2 anchoredPosition)
        {
            RectTransform cardRect = BuildSelectablePlate(root, $"Theme_{theme.Id}", cardSize, anchoredPosition, out Selection selection);

            var previewSize = new Vector2(cardSize.x - (THEME_CARD_PADDING * 2f), THEME_PREVIEW_HEIGHT);
            var previewCentre = new Vector2(0f, (cardSize.y * 0.5f) - THEME_CARD_PADDING - (THEME_PREVIEW_HEIGHT * 0.5f));

            // One gradient texture per theme, created once here and never regenerated.
            var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(Image));
            var previewRect = (RectTransform)previewObject.transform;
            previewRect.SetParent(cardRect, false);
            Centre(previewRect, previewSize);
            previewRect.anchoredPosition = previewCentre;
            var previewImage = previewObject.GetComponent<Image>();
            previewImage.sprite = UiSpriteFactory.CreateVerticalGradient(theme.BackgroundBottom, theme.BackgroundTop);
            previewImage.type = Image.Type.Simple;
            previewImage.color = Color.white;
            previewImage.raycastTarget = false;

            BuildMiniBoard(previewRect, theme);

            Text nameText = CreateLabel(
                cardRect, "Name", THEME_NAME_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, (-cardSize.y * 0.5f) + THEME_NAME_RISE), _displayFont);
            _inkTexts.Add(nameText);
            nameText.text = ResolveThemeName(theme);

            AddCheckDisc(cardRect, selection);
            return new ThemeOption(theme.Id, cardRect, selection, nameText);
        }

        /// <summary>
        /// The 4×4 board on a theme card: a plate in that theme's card colour holding sixteen bevelled
        /// cells — fill over shade, as <see cref="CellView"/> draws them — in that theme's real kind
        /// colours, so the card shows exactly what the board will look like. Painted once, here: it is
        /// its own theme's preview, so it never follows the active theme.
        /// </summary>
        private static void BuildMiniBoard(RectTransform previewRect, ThemeDefinition theme)
        {
            Image plate = BuildRounded(
                previewRect, "MiniBoard", new Vector2(MINI_BOARD_WIDTH, MINI_BOARD_WIDTH), Vector2.zero, MINI_BOARD_CORNER_RADIUS);
            plate.color = theme.CardBackground;

            float cellSize = (MINI_BOARD_WIDTH - (MINI_BOARD_PADDING * 2f) - (MINI_CELL_GAP * (MINI_BOARD_SIZE - 1))) / MINI_BOARD_SIZE;
            float pitch = cellSize + MINI_CELL_GAP;
            float origin = -((MINI_BOARD_SIZE - 1) * 0.5f) * pitch;

            for (int cellIndex = 0; cellIndex < MiniBoardPattern.Length; cellIndex++)
            {
                int column = cellIndex % MINI_BOARD_SIZE;
                int row = cellIndex / MINI_BOARD_SIZE;
                var centre = new Vector2(origin + (column * pitch), -origin - (row * pitch));
                int kind = MiniBoardPattern[cellIndex];

                // GetFill/GetShade fall back to the empty-cell pair for kind 0, so one path draws both.
                Image shade = BuildRounded(previewRect, $"CellShade_{cellIndex}", new Vector2(cellSize, cellSize), centre, MINI_CELL_CORNER_RADIUS);
                shade.color = theme.GetShade(kind);
                Image fill = BuildRounded(
                    previewRect, $"CellFill_{cellIndex}", new Vector2(cellSize, cellSize - MINI_CELL_LIP),
                    centre + new Vector2(0f, MINI_CELL_LIP * 0.5f), MINI_CELL_CORNER_RADIUS);
                fill.color = theme.GetFill(kind);
            }
        }

        private void BuildModeScreen(RectTransform root)
        {
            _modeBackButtonRect = BuildSubHeader(root, LocalizationKeys.SETTINGS_MODE_SCREEN_TITLE, null);

            // A running cursor rather than a fixed row stride: the Timed plate is taller than the other
            // two, since it carries its duration chips inline rather than on a separate screen (issue
            // #270), and a genuine N-way picker over SelectableModes means shipping a mode is one entry
            // in that array plus its String Table rows — nothing here moves.
            float cursorY = SUB_CONTENT_TOP;
            for (int modeIndex = 0; modeIndex < SelectableModes.Length; modeIndex++)
            {
                GameMode mode = SelectableModes[modeIndex];
                float plateHeight = mode == GameMode.Timed ? TIMED_PLATE_HEIGHT : ROW_HEIGHT;
                var anchoredPosition = new Vector2(0f, TopY(cursorY, plateHeight));

                _modeOptions.Add(mode == GameMode.Timed
                    ? BuildTimedModeOption(root, anchoredPosition)
                    : BuildModeOption(root, mode, anchoredPosition));

                cursorY += plateHeight + ROW_GAP;
            }

            BuildConfirmCard(root);
        }

        /// <summary>One mode plate: the mode's tile and glyph, its name over a one-line description,
        /// and the PLAYING tag on the right that only the active mode shows.</summary>
        private ModeOption BuildModeOption(RectTransform root, GameMode mode, Vector2 anchoredPosition)
        {
            float contentWidth = ContentWidth;
            RectTransform plateRect = BuildSelectablePlate(
                root, $"ModeOption_{mode}", new Vector2(contentWidth, ROW_HEIGHT), anchoredPosition, out Selection selection);

            int kind = ModeKind(mode);
            RectTransform tileRect = BuildKindTile(plateRect, kind, new Vector2((-contentWidth * 0.5f) + ROW_PADDING_X + (TILE_SIZE * 0.5f), 0f));
            BuildModeGlyph(tileRect, mode, kind);

            float textX = (-contentWidth * 0.5f) + ROW_TEXT_INSET;
            Text nameText = CreateLabel(
                plateRect, "Name", MODE_NAME_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(textX, MODE_NAME_RISE), _displayFont);
            _inkTexts.Add(nameText);
            RegisterLocalized(nameText, ModeNameKey(mode));

            BuildModeDescriptionLabel(plateRect, mode, contentWidth, textX, -MODE_DESCRIPTION_DROP);

            // Painted by RefreshModeSelection rather than the ink bucket: it is accent or nothing.
            Text playingText = CreateLabel(
                plateRect, "Playing", LABEL_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2((contentWidth * 0.5f) - ROW_PADDING_X, 0f));
            RegisterLocalized(playingText, LocalizationKeys.SETTINGS_MODE_PLAYING, uppercase: true);

            return new ModeOption(mode, plateRect, selection, playingText);
        }

        /// <summary>
        /// One mode plate's description line. Wrapping (rather than <see cref="CreateLabel"/>'s usual
        /// overflow) is deliberate — some locales' copy does not fit one line at
        /// <see cref="MODE_DESCRIPTION_PLAYING_RESERVE"/>'s width budget (issue #355 follow-up, e.g.
        /// Spanish's "Infinito" description), and letting it wrap to a second line beats letting it run
        /// under the PLAYING tag or off the plate.
        /// </summary>
        private void BuildModeDescriptionLabel(
            RectTransform plateRect, GameMode mode, float contentWidth, float textX, float y)
        {
            Text descriptionText = CreateLabel(
                plateRect, "Description", DESCRIPTION_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(textX, y));
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Overflow;

            var rect = (RectTransform)descriptionText.transform;
            rect.sizeDelta = new Vector2(
                contentWidth - ROW_TEXT_INSET - ROW_PADDING_X - MODE_DESCRIPTION_PLAYING_RESERVE,
                DESCRIPTION_FONT_SIZE * 1.6f * 2f);

            _softInkTexts.Add(descriptionText);
            RegisterLocalized(descriptionText, ModeDescriptionKey(mode));
        }

        /// <summary>
        /// The Timed plate: the same header <see cref="BuildModeOption"/> draws — tile, name,
        /// description, PLAYING tag — plus a row of duration chips underneath, so picking Timed and
        /// picking its length happen on the one plate instead of two separate screens (issue #270). The
        /// header fills the plate's top <see cref="ROW_HEIGHT"/> exactly as every other mode plate
        /// fills its own height, and the chip row fills the rest below one <see cref="ROW_GAP"/>.
        /// </summary>
        private ModeOption BuildTimedModeOption(RectTransform root, Vector2 anchoredPosition)
        {
            const GameMode mode = GameMode.Timed;
            float contentWidth = ContentWidth;
            RectTransform plateRect = BuildSelectablePlate(
                root, "ModeOption_Timed", new Vector2(contentWidth, TIMED_PLATE_HEIGHT), anchoredPosition, out Selection selection);

            // The header content sits where it would in a plain ROW_HEIGHT plate, just lifted to the
            // top of the taller one.
            float headerCentreY = (TIMED_PLATE_HEIGHT * 0.5f) - (ROW_HEIGHT * 0.5f);

            int kind = ModeKind(mode);
            RectTransform tileRect = BuildKindTile(
                plateRect, kind, new Vector2((-contentWidth * 0.5f) + ROW_PADDING_X + (TILE_SIZE * 0.5f), headerCentreY));
            BuildModeGlyph(tileRect, mode, kind);

            float textX = (-contentWidth * 0.5f) + ROW_TEXT_INSET;
            Text nameText = CreateLabel(
                plateRect, "Name", MODE_NAME_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(textX, headerCentreY + MODE_NAME_RISE), _displayFont);
            _inkTexts.Add(nameText);
            RegisterLocalized(nameText, ModeNameKey(mode));

            BuildModeDescriptionLabel(plateRect, mode, contentWidth, textX, headerCentreY - MODE_DESCRIPTION_DROP);

            Text playingText = CreateLabel(
                plateRect, "Playing", LABEL_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2((contentWidth * 0.5f) - ROW_PADDING_X, headerCentreY));
            RegisterLocalized(playingText, LocalizationKeys.SETTINGS_MODE_PLAYING, uppercase: true);

            BuildDurationChips(plateRect, contentWidth, headerCentreY);

            return new ModeOption(mode, plateRect, selection, playingText);
        }

        /// <summary>
        /// The Timed plate's duration chips, in two rows (issue #355 follow-up): "Sınırsız" alone,
        /// full width, on top — it is a different kind of choice ("play forever") than a length, not a
        /// fourth one to squeeze in beside them — and the timed lengths in a
        /// <see cref="DURATION_COLUMN_COUNT"/>-column grid below it, seated under the Timed plate's
        /// header instead of under a sub-header (issue #270).
        /// </summary>
        private void BuildDurationChips(RectTransform plateRect, float contentWidth, float headerCentreY)
        {
            IReadOnlyList<float> durations = _timedModeSystem.AvailableDurations;

            float endlessRowCentreY = headerCentreY - (ROW_HEIGHT * 0.5f) - ROW_GAP - (TIMED_CHIP_ROW_HEIGHT * 0.5f);
            float durationsRowCentreY = endlessRowCentreY - TIMED_CHIP_ROW_HEIGHT - ROW_GAP;

            float tileWidth = (contentWidth - (OPTION_GAP * (DURATION_COLUMN_COUNT - 1))) / DURATION_COLUMN_COUNT;
            var tileSize = new Vector2(tileWidth, TIMED_CHIP_ROW_HEIGHT);

            // Counts only the timed lengths seen so far, independent of each entry's index in the
            // config — so the endless entry, wherever the config lists it, never opens a gap in the
            // 3-column grid.
            int timedIndex = 0;

            for (int durationIndex = 0; durationIndex < durations.Count; durationIndex++)
            {
                float seconds = durations[durationIndex];

                if (TimedModeConfig.IsEndlessDuration(seconds))
                {
                    var endlessSize = new Vector2(contentWidth, TIMED_CHIP_ROW_HEIGHT);
                    _durationOptions.Add(BuildDurationOption(plateRect, seconds, endlessSize, new Vector2(0f, endlessRowCentreY)));
                    continue;
                }

                float x = (timedIndex - ((DURATION_COLUMN_COUNT - 1) * 0.5f)) * (tileWidth + OPTION_GAP);
                _durationOptions.Add(BuildDurationOption(plateRect, seconds, tileSize, new Vector2(x, durationsRowCentreY)));
                timedIndex++;
            }
        }

        /// <summary>
        /// The restart confirmation, a plate at the foot of the mode screen: "Switch to X?", a line
        /// saying the run restarts, and KEEP PLAYING / RESTART. Hidden until a non-active mode is tapped.
        /// </summary>
        private void BuildConfirmCard(RectTransform root)
        {
            float contentWidth = ContentWidth;
            float cardTop = _cardSize.y - WELL_INSET - WELL_PADDING_Y - CONFIRM_CARD_HEIGHT;
            _confirmCardRect = BuildPlate(
                root, "ConfirmCard", new Vector2(contentWidth, CONFIRM_CARD_HEIGHT), new Vector2(0f, TopY(cardTop, CONFIRM_CARD_HEIGHT)));

            float leftX = (-contentWidth * 0.5f) + CONFIRM_PADDING;
            float halfHeight = CONFIRM_CARD_HEIGHT * 0.5f;

            _confirmTitleText = CreateLabel(
                _confirmCardRect, "Title", CONFIRM_TITLE_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(leftX, halfHeight - CONFIRM_TITLE_DROP), _displayFont);
            _inkTexts.Add(_confirmTitleText);

            Text body = CreateLabel(
                _confirmCardRect, "Body", DESCRIPTION_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX, halfHeight - CONFIRM_BODY_DROP));
            _softInkTexts.Add(body);
            RegisterLocalized(body, LocalizationKeys.SETTINGS_CONFIRM_BODY);

            float buttonWidth = (contentWidth - (CONFIRM_PADDING * 2f) - CONFIRM_BUTTON_GAP) * 0.5f;
            var buttonSize = new Vector2(buttonWidth, CONFIRM_BUTTON_HEIGHT);
            float buttonY = -halfHeight + CONFIRM_PADDING + (CONFIRM_BUTTON_HEIGHT * 0.5f);
            float buttonOffsetX = (buttonWidth + CONFIRM_BUTTON_GAP) * 0.5f;

            // KEEP PLAYING: the empty-cell pair with the tiles' lip — the quiet choice, on the left.
            var noObject = new GameObject("ConfirmNo", typeof(RectTransform));
            _confirmNoRect = (RectTransform)noObject.transform;
            _confirmNoRect.SetParent(_confirmCardRect, false);
            Centre(_confirmNoRect, buttonSize);
            _confirmNoRect.anchoredPosition = new Vector2(-buttonOffsetX, buttonY);
            _confirmNoLip = BuildRounded(_confirmNoRect, "Lip", buttonSize, Vector2.zero, PLATE_CORNER_RADIUS);
            _confirmNoFace = BuildRounded(
                _confirmNoRect, "Face", new Vector2(buttonWidth, CONFIRM_BUTTON_HEIGHT - TILE_LIP), new Vector2(0f, TILE_LIP * 0.5f), PLATE_CORNER_RADIUS);
            _confirmNoText = CreateLabel(
                _confirmNoRect, "Label", CONFIRM_BUTTON_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, BUTTON_LABEL_RISE), _displayFont);
            RegisterLocalized(_confirmNoText, LocalizationKeys.SETTINGS_CONFIRM_NO, uppercase: true);

            // RESTART: the glossy call-to-action, on the right.
            _confirmYesRect = BuildGlossyButton(_confirmCardRect, "ConfirmYes", buttonSize, out _confirmYesPlate);
            _confirmYesRect.anchoredPosition = new Vector2(buttonOffsetX, buttonY);
            _confirmYesText = CreateLabel(
                _confirmYesRect, "Label", CONFIRM_BUTTON_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, BUTTON_LABEL_RISE), _displayFont);
            RegisterLocalized(_confirmYesText, LocalizationKeys.SETTINGS_CONFIRM_YES, uppercase: true);
        }

        /// <summary>One duration chip: built for the Timed mode plate's inline picker (issue #270), the
        /// same look the old stand-alone duration screen used.</summary>
        private DurationOption BuildDurationOption(RectTransform root, float seconds, Vector2 tileSize, Vector2 anchoredPosition)
        {
            RectTransform plateRect = BuildSelectablePlate(
                root, $"DurationOption_{Mathf.RoundToInt(seconds)}", tileSize, anchoredPosition, out Selection selection);

            Text nameText = CreateLabel(
                plateRect, "Name", OPTION_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, _displayFont);
            _inkTexts.Add(nameText);
            nameText.text = FormatDuration(seconds);

            return new DurationOption(seconds, plateRect, selection, nameText);
        }

        /// <summary>
        /// Built from <c>LocalizationModel.AvailableLocales</c> the same way the theme grid is built
        /// from the theme catalogue, so shipping a language is a Locale asset plus its String Table
        /// column — no change here.
        /// </summary>
        private void BuildLanguageScreen(RectTransform root)
        {
            _languageBackButtonRect = BuildSubHeader(root, LocalizationKeys.SETTINGS_LANGUAGE_SCREEN_TITLE, null);

            IReadOnlyList<LocaleDefinition> locales = _localizationModel.AvailableLocales;
            int built = 0;

            for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
            {
                LocaleDefinition locale = locales[localeIndex];
                if (locale == null)
                {
                    continue;
                }

                float y = TopY(SUB_CONTENT_TOP + (built * (ROW_HEIGHT + ROW_GAP)), ROW_HEIGHT);
                _languageOptions.Add(BuildLanguageOption(root, locale, new Vector2(0f, y)));
                built++;
            }
        }

        private LanguageOption BuildLanguageOption(RectTransform root, LocaleDefinition locale, Vector2 anchoredPosition)
        {
            float contentWidth = ContentWidth;
            RectTransform plateRect = BuildSelectablePlate(
                root, $"LanguageOption_{locale.Code}", new Vector2(contentWidth, ROW_HEIGHT), anchoredPosition, out Selection selection);

            // Not a String Table lookup and deliberately never re-worded: a language is always
            // labelled in its own language, so this plate reads the same in every locale.
            Text nameText = CreateLabel(
                plateRect, "Name", MODE_NAME_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2((-contentWidth * 0.5f) + ROW_PADDING_X + 8f, 0f), _displayFont);
            _inkTexts.Add(nameText);
            nameText.text = locale.DisplayName;

            AddCheckDisc(plateRect, selection);
            return new LanguageOption(locale.Code, plateRect, selection);
        }

        // ---------------------------------------------------------------------------- primitives

        /// <summary>
        /// Builds a wordless label. Callers fill it in, either through
        /// <see cref="RegisterLocalized"/> for a plain key or from their own refresh method when the
        /// wording has a value substituted into it.
        /// </summary>
        private static Text CreateLabel(
            RectTransform parent,
            string objectName,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 anchoredPosition,
            Font font = null)
        {
            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, Color.clear, font);
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

        private static Image BuildRounded(RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition, float radius)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var imageRect = (RectTransform)imageObject.transform;
            imageRect.SetParent(parent, false);
            Centre(imageRect, size);
            imageRect.anchoredPosition = anchoredPosition;
            return ConfigureRounded(imageObject.GetComponent<Image>(), radius);
        }

        private static Image BuildCircle(RectTransform parent, string objectName, float diameter, Vector2 anchoredPosition)
            => BuildGlyph(parent, objectName, UiSpriteFactory.Circle, new Vector2(diameter, diameter), anchoredPosition);

        private static Image BuildGlyph(RectTransform parent, string objectName, Sprite sprite, Vector2 size, Vector2 anchoredPosition)
        {
            var glyphObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(parent, false);
            Centre(glyphRect, size);
            glyphRect.anchoredPosition = anchoredPosition;
            return ConfigureGlyph(glyphObject.GetComponent<Image>(), sprite);
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Anchored Y of an element <paramref name="height"/> tall whose top edge sits
        /// <paramref name="offsetFromTop"/> below the card's top edge.</summary>
        private float TopY(float offsetFromTop, float height)
            => (_cardSize.y * 0.5f) - offsetFromTop - (height * 0.5f);

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not through
        // an EventSystem, and this scene has none. Every rounded Image shares the one rounded-square
        // sprite, sliced to its own radius, so the card batches with the rest of the HUD.
        private static Image ConfigureRounded(Image image, float radius)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / radius;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A non-interactive picture: aspect kept, no raycast, painted later. The circle and
        /// check sprites have no border, so they must never be sliced.</summary>
        private static Image ConfigureGlyph(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>The same hue, pulled toward black — a shade of the colour itself, not a blend with the
        /// ink, so a blue-inked season still gets a gold shade rather than an olive one.</summary>
        private static Color Darken(Color colour, float amount)
            => new Color(colour.r * (1f - amount), colour.g * (1f - amount), colour.b * (1f - amount), colour.a);

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        // ---------------------------------------------------------------------------- option records

        /// <summary>The parts of a choosable plate that repaint on selection: the ring, its gap and,
        /// on the plates that have one, the check disc.</summary>
        private sealed class Selection
        {
            internal Selection(Image ring, Image gap)
            {
                Ring = ring;
                Gap = gap;
            }

            internal Image Ring { get; }

            internal Image Gap { get; }

            internal Image CheckLip { get; set; }

            internal Image CheckDisc { get; set; }

            internal Image CheckGlyph { get; set; }
        }

        /// <summary>One tappable theme card: its theme id plus the bits that repaint on selection.</summary>
        private sealed class ThemeOption
        {
            internal ThemeOption(int themeId, RectTransform rect, Selection selection, Text nameText)
            {
                ThemeId = themeId;
                Rect = rect;
                Selection = selection;
                NameText = nameText;
            }

            internal int ThemeId { get; }

            internal RectTransform Rect { get; }

            internal Selection Selection { get; }

            internal Text NameText { get; }
        }

        /// <summary>One tappable mode plate: the mode it selects plus the bits that repaint on selection.</summary>
        private sealed class ModeOption
        {
            internal ModeOption(GameMode mode, RectTransform rect, Selection selection, Text playingText)
            {
                Mode = mode;
                Rect = rect;
                Selection = selection;
                PlayingText = playingText;
            }

            internal GameMode Mode { get; }

            internal RectTransform Rect { get; }

            internal Selection Selection { get; }

            internal Text PlayingText { get; }
        }

        /// <summary>One tappable language plate: the locale it selects plus its selection visuals.</summary>
        private sealed class LanguageOption
        {
            internal LanguageOption(string localeCode, RectTransform rect, Selection selection)
            {
                LocaleCode = localeCode;
                Rect = rect;
                Selection = selection;
            }

            internal string LocaleCode { get; }

            internal RectTransform Rect { get; }

            internal Selection Selection { get; }
        }

        /// <summary>One tappable duration tile: the length it selects plus its selection visuals.</summary>
        private sealed class DurationOption
        {
            internal DurationOption(float seconds, RectTransform rect, Selection selection, Text nameText)
            {
                Seconds = seconds;
                Rect = rect;
                Selection = selection;
                NameText = nameText;
            }

            internal float Seconds { get; }

            internal RectTransform Rect { get; }

            internal Selection Selection { get; }

            internal Text NameText { get; }
        }
    }
}
