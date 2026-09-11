using System.Collections.Generic;
using System.Text;
using MustyBlockBlast.Gameplay;
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
    /// The settings overlay. Four screens live inside one card:
    /// <list type="bullet">
    /// <item>Settings — a grouped list with the current mode, the current theme, the sound toggle and
    /// a (visual only) round duration row.</item>
    /// <item>Theme — the swatch grid; picking one calls <see cref="SettingsSystem.SetTheme"/> and
    /// returns to the settings screen. The recolour itself is handled by the existing reactive theme
    /// subscriptions in every other View, so nothing else happens here.</item>
    /// <item>Mode — the two mode cards. Picking the active mode just returns; picking the other one
    /// steps to the confirmation screen, because switching restarts the run.</item>
    /// <item>ModeConfirm — the "this restarts your run" prompt. Confirming calls
    /// <see cref="GameModeSystem.SelectMode"/>, which owns the restart.</item>
    /// </list>
    /// Both screens are built once in <see cref="Start"/> and toggled with SetActive — the same
    /// "build once, never rebuild" approach <see cref="CellView"/> uses for its two looks.
    /// <para>
    /// The swatch list is built from <c>SettingsModel.AvailableThemes</c>, so shipping a new theme is
    /// a new ScriptableObject plus a LifetimeScope entry — no change to this class.
    /// </para>
    /// <para>
    /// Like the rest of the UI this View never raycasts: <see cref="BoardInputView"/> owns the pointer
    /// and forwards taps to <see cref="HandleTap"/> while the panel is open.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanelView : MonoBehaviour
    {
        private const int COLUMN_COUNT = 2;

        // Layout, in canvas reference pixels. The mock-up was drawn against a 380pt card; everything
        // here is that mock-up scaled to the 880pt card the rest of the UI already uses.
        private const float HEADER_INSET = 92f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float LIST_WIDTH = 760f;
        private const float ROW_HEIGHT = 128f;
        private const int ROW_COUNT = 4;

        /// <summary>Card top edge to list top edge: the header band plus the gap under it.</summary>
        private const float LIST_TOP_INSET = 194f;

        /// <summary>List bottom edge to card bottom edge.</summary>
        private const float LIST_BOTTOM_INSET = 62f;

        private const float BADGE_SIZE = 84f;
        private const float DIVIDER_THICKNESS = 3f;
        private const float PILL_WIDTH = 240f;
        private const float PILL_HEIGHT = 76f;
        private const float TOGGLE_WIDTH = 106f;
        private const float TOGGLE_HEIGHT = 62f;
        private const float TOGGLE_THUMB_SIZE = 50f;

        /// <summary>Half the slack left in the track once the thumb and its 6pt inset are removed.</summary>
        private const float TOGGLE_THUMB_TRAVEL = (TOGGLE_WIDTH - TOGGLE_THUMB_SIZE - 12f) * 0.5f;

        private const float CHEVRON_HALF_SIZE = 13f;
        private const float CHEVRON_THICKNESS = 7f;

        /// <summary>Shown in the duration pill while endless is active, where a length means nothing.</summary>
        private const string DURATION_NOT_APPLICABLE = "—";

        private const string DURATION_UNIT_SUFFIX = " sn";

        private const string ENDLESS_MODE_NAME = "Sınırsız";
        private const string TIMED_MODE_NAME = "Süreli";

        private static readonly Vector2 ModeOptionSize = new Vector2(320f, 180f);
        private static readonly Vector2 ConfirmButtonSize = new Vector2(340f, 96f);
        private const float MODE_OPTION_SPACING_X = 360f;

        private const int DURATION_COLUMN_COUNT = 3;
        private static readonly Vector2 DurationOptionSize = new Vector2(200f, 130f);
        private static readonly Vector2 DurationOptionSpacing = new Vector2(232f, 162f);

        // The switch is a universal affordance, so unlike everything else on the card it keeps the
        // same colours in every theme.
        private static readonly Color ToggleOnColour = new Color(0.298f, 0.686f, 0.510f, 1f);
        private static readonly Color ToggleOffColour = new Color(0.851f, 0.835f, 0.871f, 1f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly List<ThemeOption> _options = new List<ThemeOption>(4);
        private readonly List<ModeOption> _modeOptions = new List<ModeOption>(2);
        private readonly List<DurationOption> _durationOptions = new List<DurationOption>(6);
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        // Repaint buckets: every Image built here belongs to exactly one of them, so a theme switch is
        // a handful of tight loops instead of a hierarchy walk.
        private readonly List<Image> _inkImages = new List<Image>(24);
        private readonly List<Image> _badgeImages = new List<Image>(4);
        private readonly List<Image> _pillImages = new List<Image>(2);
        private readonly List<Image> _dividerImages = new List<Image>(2);
        private readonly List<Text> _inkTexts = new List<Text>(8);
        private readonly Image[] _themeBadgeDots = new Image[ThemeDefinition.KIND_COUNT];

        [Header("Layout")]
        [Tooltip("Card size while the theme grid is showing.")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 980f);
        [Tooltip("Minimum card size while the settings list or the mode picker is showing. Grown automatically when the row list no longer fits.")]
        [SerializeField] private Vector2 _settingsCardSize = new Vector2(880f, 640f);
        [Tooltip("Card size while the mode-change confirmation is showing.")]
        [SerializeField] private Vector2 _confirmCardSize = new Vector2(880f, 460f);
        [SerializeField] private Vector2 _optionSize = new Vector2(380f, 300f);
        [SerializeField] private Vector2 _optionSpacing = new Vector2(400f, 340f);

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);
        [Tooltip("Outline thickness drawn around the currently selected theme swatch.")]
        [SerializeField] private float _selectionBorderThickness = 8f;

        private SettingsModel _settingsModel;
        private SettingsSystem _settingsSystem;
        private SfxModel _sfxModel;
        private ISfxService _sfxService;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private TimerRunSystem _timerRunSystem;
        private Canvas _canvas;

        private PanelScreen _screen = PanelScreen.Settings;

        /// <summary>The mode the confirmation screen is asking about. Only meaningful on that screen.</summary>
        private GameMode _pendingMode = GameMode.Endless;

        private GameObject _panel;
        private RectTransform _cardRect;
        private RectTransform _cardShadowRect;
        private Image _cardImage;
        private Image _cardShadowImage;

        private GameObject _settingsScreenRoot;
        private GameObject _themeScreenRoot;
        private GameObject _modeScreenRoot;
        private GameObject _confirmScreenRoot;
        private GameObject _durationScreenRoot;

        private Image _listImage;
        private RectTransform _closeButtonRect;
        private RectTransform _modeRowRect;
        private RectTransform _themeRowRect;
        private RectTransform _soundRowRect;
        private RectTransform _durationRowRect;
        private RectTransform _themeBackButtonRect;
        private RectTransform _modeBackButtonRect;
        private RectTransform _durationBackButtonRect;
        private RectTransform _confirmYesRect;
        private RectTransform _confirmNoRect;
        private RectTransform _toggleThumbRect;
        private Image _toggleTrackImage;
        private Text _themeValueText;
        private Text _modeValueText;
        private Text _durationValueText;
        private Text _durationLabelText;

        /// <summary>Which of the screens inside the card is showing.</summary>
        private enum PanelScreen
        {
            Settings,
            Theme,
            Mode,
            ModeConfirm,
            Duration,
        }

        /// <summary>
        /// Settings/mode card size. Derived from the row count so adding a row never has to be
        /// mirrored into the scene-serialized <see cref="_settingsCardSize"/>; the serialized value
        /// is a floor, so a designer can still make the card roomier.
        /// </summary>
        private Vector2 SettingsCardSize => new Vector2(
            _settingsCardSize.x,
            Mathf.Max(_settingsCardSize.y, (ROW_HEIGHT * ROW_COUNT) + LIST_TOP_INSET + LIST_BOTTOM_INSET));

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            SettingsSystem settingsSystem,
            SfxModel sfxModel,
            ISfxService sfxService,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            TimerRunSystem timerRunSystem)
        {
            _settingsModel = settingsModel;
            _settingsSystem = settingsSystem;
            _sfxModel = sfxModel;
            _sfxService = sfxService;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
            _timerRunSystem = timerRunSystem;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_settingsModel == null || _settingsSystem == null || _sfxModel == null || _sfxService == null
                || _gameModeSystem == null || _timedModeSystem == null || _timerRunSystem == null)
            {
                Debug.LogError(
                    $"{nameof(SettingsPanelView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Built in Start rather than Awake: the swatch list needs the injected theme catalogue,
            // which is only available once VContainer has run Construct.
            BuildPanel();
            SetScreen(PanelScreen.Settings);
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _sfxModel.IsMuted.Subscribe(OnMutedChanged).AddTo(_disposables);
            _timedModeSystem.SelectedDuration.Subscribe(OnSelectedDurationChanged).AddTo(_disposables);

            // Last, because its handler repaints the duration row, which needs the two above to have
            // published their first value.
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

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
                PanelScreen.Duration => HandleDurationScreenTap(screenPosition, eventCamera),
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

            if (RectTransformUtility.RectangleContainsScreenPoint(_soundRowRect, screenPosition, eventCamera))
            {
                _sfxService.SetMuted(!_sfxModel.IsMuted.Value);
                return true;
            }

            if (!RectTransformUtility.RectangleContainsScreenPoint(_durationRowRect, screenPosition, eventCamera))
            {
                return false;
            }

            // A round length is meaningless in an endless run, so the row is greyed out and inert
            // there — but it still swallows the tap, so it never behaves like the scrim.
            if (_gameModeSystem.CurrentMode.Value == GameMode.Timed)
            {
                SetScreen(PanelScreen.Duration);
            }

            return true;
        }

        private bool HandleThemeScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
            for (int optionIndex = 0; optionIndex < _options.Count; optionIndex++)
            {
                ThemeOption option = _options[optionIndex];
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

        private bool HandleModeScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
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

        private bool HandleConfirmScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_confirmYesRect, screenPosition, eventCamera))
            {
                _gameModeSystem.SelectMode(_pendingMode);
                SetScreen(PanelScreen.Settings);
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_confirmNoRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Mode);
                return true;
            }

            return false;
        }

        private bool HandleDurationScreenTap(Vector2 screenPosition, Camera eventCamera)
        {
            for (int optionIndex = 0; optionIndex < _durationOptions.Count; optionIndex++)
            {
                DurationOption option = _durationOptions[optionIndex];
                if (!RectTransformUtility.RectangleContainsScreenPoint(option.Rect, screenPosition, eventCamera))
                {
                    continue;
                }

                // No confirmation step: unlike a mode switch this does not restart the run. The new
                // length takes effect on the next tray refill.
                _timedModeSystem.SelectDuration(option.Seconds);
                SetScreen(PanelScreen.Settings);
                return true;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_durationBackButtonRect, screenPosition, eventCamera))
            {
                SetScreen(PanelScreen.Settings);
                return true;
            }

            return false;
        }

        private void Close()
        {
            _panel.SetActive(false);
            _timerRunSystem.SetMenuPaused(false);
        }

        private void SetScreen(PanelScreen screen)
        {
            _screen = screen;

            _settingsScreenRoot.SetActive(screen == PanelScreen.Settings);
            _themeScreenRoot.SetActive(screen == PanelScreen.Theme);
            _modeScreenRoot.SetActive(screen == PanelScreen.Mode);
            _confirmScreenRoot.SetActive(screen == PanelScreen.ModeConfirm);
            _durationScreenRoot.SetActive(screen == PanelScreen.Duration);

            // The screens need very different heights, so the shared card resizes with them rather
            // than leaving the shorter content stranded in a tall empty card.
            Vector2 size = screen switch
            {
                PanelScreen.Theme => _cardSize,
                PanelScreen.ModeConfirm => _confirmCardSize,
                _ => SettingsCardSize,
            };

            _cardRect.sizeDelta = size;
            _cardShadowRect.sizeDelta = size + new Vector2(10f, 10f);
        }

        private void OnMutedChanged(bool muted)
        {
            if (_toggleTrackImage == null)
            {
                return;
            }

            // Deliberately not theme-derived: the switch is a universal affordance, so it keeps the
            // same green/grey in every theme.
            _toggleTrackImage.color = muted ? ToggleOffColour : ToggleOnColour;
            _toggleThumbRect.anchoredPosition =
                new Vector2(muted ? -TOGGLE_THUMB_TRAVEL : TOGGLE_THUMB_TRAVEL, 0f);
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _cardImage.color = theme.CardBackground;
            _cardShadowImage.color = theme.CardShadow;

            // Every neutral in the mock-up is derived from the Ink/CardBackground pair instead of
            // being hard-coded: that pair is guaranteed readable in every theme by design, whereas a
            // fixed light neutral collapses against a light-ink theme (e.g. Kış's near-white ink).
            _listImage.color = Color.Lerp(theme.CardBackground, theme.Ink, 0.06f);
            Color badgeColour = Color.Lerp(theme.CardBackground, theme.Ink, 0.16f);

            for (int imageIndex = 0; imageIndex < _badgeImages.Count; imageIndex++)
            {
                _badgeImages[imageIndex].color = badgeColour;
            }

            for (int imageIndex = 0; imageIndex < _dividerImages.Count; imageIndex++)
            {
                _dividerImages[imageIndex].color = badgeColour;
            }

            for (int imageIndex = 0; imageIndex < _pillImages.Count; imageIndex++)
            {
                _pillImages[imageIndex].color = theme.CardBackground;
            }

            for (int imageIndex = 0; imageIndex < _inkImages.Count; imageIndex++)
            {
                _inkImages[imageIndex].color = theme.Ink;
            }

            for (int textIndex = 0; textIndex < _inkTexts.Count; textIndex++)
            {
                _inkTexts[textIndex].color = theme.Ink;
            }

            // Colour ids are 1-based; 0 means "empty cell".
            for (int kindIndex = 0; kindIndex < _themeBadgeDots.Length; kindIndex++)
            {
                _themeBadgeDots[kindIndex].color = theme.GetFill(kindIndex + 1);
            }

            _themeValueText.text = theme.DisplayName;

            // The swatches themselves show their own theme's colours and never change; only the
            // selection outline and the labels follow the active theme.
            for (int optionIndex = 0; optionIndex < _options.Count; optionIndex++)
            {
                ThemeOption option = _options[optionIndex];
                bool isSelected = option.ThemeId == theme.Id;
                option.BorderImage.color = isSelected ? theme.Ink : Color.clear;
                option.NameText.color = isSelected ? theme.Ink : theme.SoftInk;
            }

            RefreshModeSelection();
            RefreshDurationSelection();

            // Last: the bulk ink loops above repaint the duration row's label and pill too, so its
            // greyed-out state has to be reapplied on top of them.
            RefreshDurationRow();
        }

        private void OnModeChanged(GameMode mode)
        {
            if (_modeValueText == null)
            {
                return;
            }

            _modeValueText.text = mode == GameMode.Timed ? TIMED_MODE_NAME : ENDLESS_MODE_NAME;
            RefreshModeSelection();
            RefreshDurationRow();
        }

        private void OnSelectedDurationChanged(float seconds)
        {
            RefreshDurationRow();
            RefreshDurationSelection();
        }

        /// <summary>
        /// Repaints the duration row's pill and greys it out outside timed mode. Shared by the theme,
        /// mode and duration handlers, all three of which can invalidate it.
        /// </summary>
        private void RefreshDurationRow()
        {
            if (_durationValueText == null || _durationLabelText == null)
            {
                return;
            }

            bool isTimed = _gameModeSystem.CurrentMode.Value == GameMode.Timed;
            _durationValueText.text = isTimed
                ? FormatDuration(_timedModeSystem.SelectedDuration.Value)
                : DURATION_NOT_APPLICABLE;

            ThemeDefinition theme = _settingsModel.CurrentTheme.Value;
            if (theme == null)
            {
                return;
            }

            // Greyed rather than hidden: the row staying in place keeps the list height stable and
            // tells the player the setting exists and which mode unlocks it.
            Color rowColour = isTimed ? theme.Ink : theme.SoftInk;
            _durationValueText.color = rowColour;
            _durationLabelText.color = rowColour;
        }

        /// <summary>Repaints the duration chips' selection outline.</summary>
        private void RefreshDurationSelection()
        {
            ThemeDefinition theme = _settingsModel.CurrentTheme.Value;
            if (theme == null)
            {
                return;
            }

            float selected = _timedModeSystem.SelectedDuration.Value;
            for (int optionIndex = 0; optionIndex < _durationOptions.Count; optionIndex++)
            {
                DurationOption option = _durationOptions[optionIndex];
                bool isSelected = Mathf.Approximately(option.Seconds, selected);
                option.BorderImage.color = isSelected ? theme.Ink : Color.clear;
                option.NameText.color = isSelected ? theme.Ink : theme.SoftInk;
            }
        }

        private string FormatDuration(float seconds)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(Mathf.RoundToInt(seconds));
            _stringBuilder.Append(DURATION_UNIT_SUFFIX);
            return _stringBuilder.ToString();
        }

        /// <summary>Repaints the mode cards' selection outline. Shared by the theme and mode handlers.</summary>
        private void RefreshModeSelection()
        {
            ThemeDefinition theme = _settingsModel.CurrentTheme.Value;
            if (theme == null)
            {
                return;
            }

            GameMode current = _gameModeSystem.CurrentMode.Value;
            for (int optionIndex = 0; optionIndex < _modeOptions.Count; optionIndex++)
            {
                ModeOption option = _modeOptions[optionIndex];
                bool isSelected = option.Mode == current;
                option.BorderImage.color = isSelected ? theme.Ink : Color.clear;
                option.NameText.color = isSelected ? theme.Ink : theme.SoftInk;
            }
        }

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
            _cardShadowRect = (RectTransform)_cardShadowImage.transform;

            _settingsScreenRoot = CreateScreenRoot("SettingsScreen");
            _themeScreenRoot = CreateScreenRoot("ThemeScreen");
            _modeScreenRoot = CreateScreenRoot("ModeScreen");
            _confirmScreenRoot = CreateScreenRoot("ModeConfirmScreen");
            _durationScreenRoot = CreateScreenRoot("DurationScreen");

            BuildSettingsScreen((RectTransform)_settingsScreenRoot.transform, SettingsCardSize.y * 0.5f);
            BuildThemeScreen((RectTransform)_themeScreenRoot.transform, _cardSize.y * 0.5f);
            BuildModeScreen((RectTransform)_modeScreenRoot.transform, SettingsCardSize.y * 0.5f);
            BuildConfirmScreen((RectTransform)_confirmScreenRoot.transform, _confirmCardSize.y * 0.5f);
            BuildDurationScreen((RectTransform)_durationScreenRoot.transform, SettingsCardSize.y * 0.5f);

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

        private void BuildSettingsScreen(RectTransform root, float cardHalfHeight)
        {
            float headerY = cardHalfHeight - HEADER_INSET;
            float halfWidth = SettingsCardSize.x * 0.5f;

            Text title = CreateLabel(
                root, "Title", "Ayarlar", 64, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-halfWidth + SIDE_INSET, headerY));
            _inkTexts.Add(title);

            BuildCloseButton(
                root,
                new Vector2(halfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f), headerY));

            float listHeight = ROW_HEIGHT * ROW_COUNT;
            float listCentreY = headerY - (ICON_BUTTON_SIZE * 0.5f) - 56f - (listHeight * 0.5f);

            var listObject = new GameObject("SettingsList", typeof(RectTransform), typeof(Image));
            var listRect = (RectTransform)listObject.transform;
            listRect.SetParent(root, false);
            Centre(listRect, new Vector2(LIST_WIDTH, listHeight));
            listRect.anchoredPosition = new Vector2(0f, listCentreY);
            _listImage = listObject.GetComponent<Image>();
            ConfigureRounded(_listImage);

            _modeRowRect = BuildRow(listRect, 0, "ModeRow", "Mod", out _);
            _themeRowRect = BuildRow(listRect, 1, "ThemeRow", "Tema", out _);
            _soundRowRect = BuildRow(listRect, 2, "SoundRow", "Ses", out _);
            _durationRowRect = BuildRow(listRect, 3, "DurationRow", "Süre", out _durationLabelText);

            for (int dividerIndex = 1; dividerIndex < ROW_COUNT; dividerIndex++)
            {
                BuildDivider(listRect, (listHeight * 0.5f) - (ROW_HEIGHT * dividerIndex));
            }

            BuildModeRowContent(_modeRowRect);
            BuildThemeRowContent(_themeRowRect);
            BuildSoundRowContent(_soundRowRect);
            BuildDurationRowContent(_durationRowRect);
        }

        /// <summary>
        /// Builds one list row. <paramref name="labelText"/> is handed back for the rows that need to
        /// repaint their label outside the bulk ink loop — currently only the duration row, which
        /// greys out in endless mode.
        /// </summary>
        private RectTransform BuildRow(
            RectTransform listRect, int rowIndex, string objectName, string label, out Text labelText)
        {
            var rowObject = new GameObject(objectName, typeof(RectTransform));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(listRect, false);
            Centre(rowRect, new Vector2(LIST_WIDTH, ROW_HEIGHT));
            rowRect.anchoredPosition = new Vector2(
                0f, (((ROW_COUNT - 1) * 0.5f) - rowIndex) * ROW_HEIGHT);

            labelText = CreateLabel(
                rowRect, "Label", label, 40, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2((-LIST_WIDTH * 0.5f) + 140f, 0f));
            _inkTexts.Add(labelText);

            return rowRect;
        }

        private RectTransform BuildBadge(RectTransform rowRect)
        {
            var badgeObject = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            var badgeRect = (RectTransform)badgeObject.transform;
            badgeRect.SetParent(rowRect, false);
            Centre(badgeRect, new Vector2(BADGE_SIZE, BADGE_SIZE));
            badgeRect.anchoredPosition = new Vector2((-LIST_WIDTH * 0.5f) + 76f, 0f);

            var badgeImage = badgeObject.GetComponent<Image>();
            ConfigureRounded(badgeImage);
            _badgeImages.Add(badgeImage);
            return badgeRect;
        }

        private void BuildDivider(RectTransform listRect, float y)
        {
            var dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            var dividerRect = (RectTransform)dividerObject.transform;
            dividerRect.SetParent(listRect, false);
            Centre(dividerRect, new Vector2(LIST_WIDTH - 144f, DIVIDER_THICKNESS));
            dividerRect.anchoredPosition = new Vector2(0f, y);

            var dividerImage = dividerObject.GetComponent<Image>();
            ConfigureRounded(dividerImage);
            _dividerImages.Add(dividerImage);
        }

        private void BuildModeRowContent(RectTransform rowRect)
        {
            RectTransform badgeRect = BuildBadge(rowRect);
            BuildModeGlyph(badgeRect);
            _modeValueText = BuildPill(rowRect, string.Empty);
        }

        /// <summary>
        /// Two overlapping rings — a loose nod to the infinity mark, which is enough to read as
        /// "mode" at badge size without inventing a bespoke glyph.
        /// </summary>
        private void BuildModeGlyph(RectTransform badgeRect)
        {
            const float RING_DIAMETER = 40f;
            const float RING_OVERLAP = 13f;

            for (int discIndex = 0; discIndex < 2; discIndex++)
            {
                var discObject = new GameObject($"ModeDisc_{discIndex}", typeof(RectTransform), typeof(Image));
                var discRect = (RectTransform)discObject.transform;
                discRect.SetParent(badgeRect, false);
                Centre(discRect, new Vector2(RING_DIAMETER, RING_DIAMETER));
                discRect.anchoredPosition = new Vector2(RingOffsetX(discIndex), 0f);

                var discImage = discObject.GetComponent<Image>();
                ConfigureCircle(discImage);
                _inkImages.Add(discImage);
            }

            // Same fake cut-out as the clock glyph: a smaller circle in the badge colour turns each
            // disc into a ring. Both holes are drawn after both discs so neither disc fills the
            // other's hole.
            for (int holeIndex = 0; holeIndex < 2; holeIndex++)
            {
                var holeObject = new GameObject($"ModeDiscHole_{holeIndex}", typeof(RectTransform), typeof(Image));
                var holeRect = (RectTransform)holeObject.transform;
                holeRect.SetParent(badgeRect, false);
                Centre(holeRect, new Vector2(RING_DIAMETER - 14f, RING_DIAMETER - 14f));
                holeRect.anchoredPosition = new Vector2(RingOffsetX(holeIndex), 0f);

                var holeImage = holeObject.GetComponent<Image>();
                ConfigureCircle(holeImage);
                _badgeImages.Add(holeImage);
            }

            float RingOffsetX(int index)
                => (index == 0 ? -1f : 1f) * ((RING_DIAMETER * 0.5f) - (RING_OVERLAP * 0.5f));
        }

        private void BuildThemeRowContent(RectTransform rowRect)
        {
            RectTransform badgeRect = BuildBadge(rowRect);

            // Same three dots as the swatches use, just smaller: one visual language for "theme".
            BuildKindDots(badgeRect, null, 20f, 26f, Vector2.zero, _themeBadgeDots);

            _themeValueText = BuildPill(rowRect, string.Empty);
        }

        private void BuildSoundRowContent(RectTransform rowRect)
        {
            RectTransform badgeRect = BuildBadge(rowRect);
            BuildVolumeGlyph(badgeRect);
            BuildToggle(rowRect);
        }

        private void BuildDurationRowContent(RectTransform rowRect)
        {
            RectTransform badgeRect = BuildBadge(rowRect);
            BuildClockGlyph(badgeRect);
            _durationValueText = BuildPill(rowRect, string.Empty);
        }

        /// <summary>Three ascending bars, bottom-aligned — the usual "volume" glyph.</summary>
        private void BuildVolumeGlyph(RectTransform badgeRect)
        {
            const int BAR_COUNT = 3;
            const float BAR_WIDTH = 11f;
            const float BAR_SPACING = 21f;
            const float BAR_BASE_Y = -26f;

            for (int barIndex = 0; barIndex < BAR_COUNT; barIndex++)
            {
                float barHeight = 24f + (barIndex * 14f);
                var barObject = new GameObject($"VolumeBar_{barIndex}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform)barObject.transform;
                barRect.SetParent(badgeRect, false);
                Centre(barRect, new Vector2(BAR_WIDTH, barHeight));
                barRect.anchoredPosition = new Vector2(
                    (barIndex - ((BAR_COUNT - 1) * 0.5f)) * BAR_SPACING, BAR_BASE_Y + (barHeight * 0.5f));

                var barImage = barObject.GetComponent<Image>();
                ConfigureRounded(barImage);
                _inkImages.Add(barImage);
            }
        }

        /// <summary>Ring plus a single off-vertical hand — enough to read as a clock at badge size.</summary>
        private void BuildClockGlyph(RectTransform badgeRect)
        {
            const float DIAL_DIAMETER = 54f;
            const float FACE_DIAMETER = 38f;
            const float HAND_LENGTH = 17f;
            const float HAND_ANGLE = -35f;

            var dialObject = new GameObject("ClockDial", typeof(RectTransform), typeof(Image));
            var dialRect = (RectTransform)dialObject.transform;
            dialRect.SetParent(badgeRect, false);
            Centre(dialRect, new Vector2(DIAL_DIAMETER, DIAL_DIAMETER));
            var dialImage = dialObject.GetComponent<Image>();
            ConfigureCircle(dialImage);
            _inkImages.Add(dialImage);

            // Fake cut-out: the badge underneath is always painted with one opaque colour, so a
            // smaller circle in that colour turns the dial into a ring.
            var faceObject = new GameObject("ClockFace", typeof(RectTransform), typeof(Image));
            var faceRect = (RectTransform)faceObject.transform;
            faceRect.SetParent(badgeRect, false);
            Centre(faceRect, new Vector2(FACE_DIAMETER, FACE_DIAMETER));
            var faceImage = faceObject.GetComponent<Image>();
            ConfigureCircle(faceImage);
            _badgeImages.Add(faceImage);

            var handObject = new GameObject("ClockHand", typeof(RectTransform), typeof(Image));
            var handRect = (RectTransform)handObject.transform;
            handRect.SetParent(badgeRect, false);
            Centre(handRect, new Vector2(5f, HAND_LENGTH));
            handRect.localRotation = Quaternion.Euler(0f, 0f, HAND_ANGLE);

            // Pushed half its own length along its rotated axis so the base sits on the centre.
            handRect.anchoredPosition =
                (Vector2)(Quaternion.Euler(0f, 0f, HAND_ANGLE) * new Vector3(0f, HAND_LENGTH * 0.5f, 0f));

            var handImage = handObject.GetComponent<Image>();
            ConfigureRounded(handImage);
            _inkImages.Add(handImage);
        }

        /// <summary>Right-aligned value pill with a chevron. Returns its value label.</summary>
        private Text BuildPill(RectTransform rowRect, string value)
        {
            var pillObject = new GameObject("Pill", typeof(RectTransform), typeof(Image));
            var pillRect = (RectTransform)pillObject.transform;
            pillRect.SetParent(rowRect, false);
            Centre(pillRect, new Vector2(PILL_WIDTH, PILL_HEIGHT));
            pillRect.anchoredPosition = new Vector2((LIST_WIDTH * 0.5f) - 36f - (PILL_WIDTH * 0.5f), 0f);

            var pillImage = pillObject.GetComponent<Image>();
            ConfigureRounded(pillImage);
            _pillImages.Add(pillImage);

            BuildChevron(pillRect, new Vector2((PILL_WIDTH * 0.5f) - 30f, 0f), 1f);

            Text valueText = CreateLabel(
                pillRect, "Value", value, 34, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2((PILL_WIDTH * 0.5f) - 56f, 0f));
            _inkTexts.Add(valueText);
            return valueText;
        }

        private void BuildToggle(RectTransform rowRect)
        {
            var trackObject = new GameObject("ToggleTrack", typeof(RectTransform), typeof(Image));
            var trackRect = (RectTransform)trackObject.transform;
            trackRect.SetParent(rowRect, false);
            Centre(trackRect, new Vector2(TOGGLE_WIDTH, TOGGLE_HEIGHT));
            trackRect.anchoredPosition = new Vector2((LIST_WIDTH * 0.5f) - 36f - (TOGGLE_WIDTH * 0.5f), 0f);

            _toggleTrackImage = trackObject.GetComponent<Image>();
            ConfigureRounded(_toggleTrackImage);
            _toggleTrackImage.color = ToggleOnColour;

            var thumbObject = new GameObject("ToggleThumb", typeof(RectTransform), typeof(Image));
            _toggleThumbRect = (RectTransform)thumbObject.transform;
            _toggleThumbRect.SetParent(trackRect, false);
            Centre(_toggleThumbRect, new Vector2(TOGGLE_THUMB_SIZE, TOGGLE_THUMB_SIZE));
            _toggleThumbRect.anchoredPosition = new Vector2(TOGGLE_THUMB_TRAVEL, 0f);

            var thumbImage = thumbObject.GetComponent<Image>();
            ConfigureCircle(thumbImage);
            thumbImage.color = Color.white;
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
                var armObject = new GameObject($"ChevronArm_{armIndex}", typeof(RectTransform), typeof(Image));
                var armRect = (RectTransform)armObject.transform;
                armRect.SetParent(parent, false);
                Centre(armRect, new Vector2(armLength, CHEVRON_THICKNESS));
                armRect.anchoredPosition = centre + new Vector2(0f, sign * CHEVRON_HALF_SIZE * 0.5f);
                armRect.localRotation = Quaternion.Euler(0f, 0f, -45f * sign * directionX);

                var armImage = armObject.GetComponent<Image>();
                ConfigureRounded(armImage);
                _inkImages.Add(armImage);
            }
        }

        /// <summary>Two bars crossed at right angles — the close glyph.</summary>
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

        private void BuildThemeScreen(RectTransform root, float cardHalfHeight)
        {
            float headerY = cardHalfHeight - HEADER_INSET;
            float leftEdge = -(_cardSize.x * 0.5f);

            _themeBackButtonRect = BuildBackButton(root, leftEdge, headerY);

            Text title = CreateLabel(
                root, "Title", "Tema Seç", 64, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftEdge + SIDE_INSET + ICON_BUTTON_SIZE + 30f, headerY));
            _inkTexts.Add(title);

            BuildOptions(root);
        }

        /// <summary>
        /// Left chevron in the header. Each screen owns its own instance: the cards differ in height,
        /// so a shared one would sit at the wrong header line on all but one screen.
        /// </summary>
        private RectTransform BuildBackButton(RectTransform root, float leftEdge, float headerY)
        {
            var backObject = new GameObject("BackButton", typeof(RectTransform));
            var backRect = (RectTransform)backObject.transform;
            backRect.SetParent(root, false);
            Centre(backRect, new Vector2(ICON_BUTTON_SIZE, ICON_BUTTON_SIZE));
            backRect.anchoredPosition = new Vector2(leftEdge + SIDE_INSET + (ICON_BUTTON_SIZE * 0.5f), headerY);

            BuildChevron(backRect, Vector2.zero, -1f);
            return backRect;
        }

        private void BuildModeScreen(RectTransform root, float cardHalfHeight)
        {
            float headerY = cardHalfHeight - HEADER_INSET;
            float leftEdge = -(SettingsCardSize.x * 0.5f);

            _modeBackButtonRect = BuildBackButton(root, leftEdge, headerY);

            Text title = CreateLabel(
                root, "Title", "Mod Seç", 64, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftEdge + SIDE_INSET + ICON_BUTTON_SIZE + 30f, headerY));
            _inkTexts.Add(title);

            // Two options only, so the same column-centring the theme grid uses collapses to one
            // centred row.
            for (int modeIndex = 0; modeIndex < 2; modeIndex++)
            {
                GameMode mode = modeIndex == 0 ? GameMode.Endless : GameMode.Timed;
                string label = modeIndex == 0 ? ENDLESS_MODE_NAME : TIMED_MODE_NAME;
                float x = (modeIndex - ((COLUMN_COUNT - 1) * 0.5f)) * MODE_OPTION_SPACING_X;
                _modeOptions.Add(BuildModeOption(root, mode, label, new Vector2(x, 0f)));
            }
        }

        private ModeOption BuildModeOption(
            RectTransform root, GameMode mode, string label, Vector2 anchoredPosition)
        {
            var optionObject = new GameObject($"ModeOption_{mode}", typeof(RectTransform));
            var optionRect = (RectTransform)optionObject.transform;
            optionRect.SetParent(root, false);
            Centre(optionRect, ModeOptionSize);
            optionRect.anchoredPosition = anchoredPosition;

            // Same trick as the theme swatches: an outset rect behind the card reads as an outline
            // once it is tinted.
            var borderObject = new GameObject("Border", typeof(RectTransform), typeof(Image));
            var borderRect = (RectTransform)borderObject.transform;
            borderRect.SetParent(optionRect, false);
            Centre(borderRect, ModeOptionSize + (Vector2.one * (_selectionBorderThickness * 2f)));
            var borderImage = borderObject.GetComponent<Image>();
            ConfigureRounded(borderImage);

            var faceObject = new GameObject("Face", typeof(RectTransform), typeof(Image));
            var faceRect = (RectTransform)faceObject.transform;
            faceRect.SetParent(optionRect, false);
            Centre(faceRect, ModeOptionSize);
            var faceImage = faceObject.GetComponent<Image>();
            ConfigureRounded(faceImage);
            _badgeImages.Add(faceImage);

            Text nameText = UiTextFactory.Create(optionRect, "Name", 42, FontStyle.Bold, Color.clear);
            nameText.text = label;

            return new ModeOption(mode, optionRect, borderImage, nameText);
        }

        private void BuildDurationScreen(RectTransform root, float cardHalfHeight)
        {
            float headerY = cardHalfHeight - HEADER_INSET;
            float leftEdge = -(SettingsCardSize.x * 0.5f);

            _durationBackButtonRect = BuildBackButton(root, leftEdge, headerY);

            Text title = CreateLabel(
                root, "Title", "Süre Seç", 64, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftEdge + SIDE_INSET + ICON_BUTTON_SIZE + 30f, headerY));
            _inkTexts.Add(title);

            // Same centred grid as the theme swatches, so adding a duration to the config asset needs
            // no change here.
            IReadOnlyList<float> durations = _timedModeSystem.AvailableDurations;
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(durations.Count / (float)DURATION_COLUMN_COUNT));

            for (int durationIndex = 0; durationIndex < durations.Count; durationIndex++)
            {
                int column = durationIndex % DURATION_COLUMN_COUNT;
                int row = durationIndex / DURATION_COLUMN_COUNT;

                float x = (column - ((DURATION_COLUMN_COUNT - 1) * 0.5f)) * DurationOptionSpacing.x;
                float y = (((rowCount - 1) * 0.5f) - row) * DurationOptionSpacing.y;

                _durationOptions.Add(
                    BuildDurationOption(root, durations[durationIndex], new Vector2(x, y)));
            }
        }

        private DurationOption BuildDurationOption(RectTransform root, float seconds, Vector2 anchoredPosition)
        {
            var optionObject = new GameObject($"DurationOption_{Mathf.RoundToInt(seconds)}", typeof(RectTransform));
            var optionRect = (RectTransform)optionObject.transform;
            optionRect.SetParent(root, false);
            Centre(optionRect, DurationOptionSize);
            optionRect.anchoredPosition = anchoredPosition;

            // Same outset-rect-as-outline trick the theme swatches and mode cards use.
            var borderObject = new GameObject("Border", typeof(RectTransform), typeof(Image));
            var borderRect = (RectTransform)borderObject.transform;
            borderRect.SetParent(optionRect, false);
            Centre(borderRect, DurationOptionSize + (Vector2.one * (_selectionBorderThickness * 2f)));
            var borderImage = borderObject.GetComponent<Image>();
            ConfigureRounded(borderImage);

            var faceObject = new GameObject("Face", typeof(RectTransform), typeof(Image));
            var faceRect = (RectTransform)faceObject.transform;
            faceRect.SetParent(optionRect, false);
            Centre(faceRect, DurationOptionSize);
            var faceImage = faceObject.GetComponent<Image>();
            ConfigureRounded(faceImage);
            _badgeImages.Add(faceImage);

            Text nameText = UiTextFactory.Create(optionRect, "Name", 46, FontStyle.Bold, Color.clear);
            nameText.text = FormatDuration(seconds);

            return new DurationOption(seconds, optionRect, borderImage, nameText);
        }

        private void BuildConfirmScreen(RectTransform root, float cardHalfHeight)
        {
            float headerY = cardHalfHeight - HEADER_INSET;

            Text title = CreateLabel(
                root, "Title", "Modu Değiştir", 58, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, headerY));
            _inkTexts.Add(title);

            Text body = CreateLabel(
                root, "Body", "Oyun yeniden başlayacak. Devam edilsin mi?", 34, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Vector2(0f, headerY - 92f));
            _inkTexts.Add(body);

            float buttonY = -cardHalfHeight + HEADER_INSET;
            float buttonOffsetX = (ConfirmButtonSize.x * 0.5f) + 24f;

            _confirmYesRect = BuildConfirmButton(
                root, "ConfirmYes", "Evet, Başlat", new Vector2(-buttonOffsetX, buttonY));
            _confirmNoRect = BuildConfirmButton(
                root, "ConfirmNo", "Vazgeç", new Vector2(buttonOffsetX, buttonY));
        }

        private RectTransform BuildConfirmButton(
            RectTransform root, string objectName, string label, Vector2 anchoredPosition)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(root, false);
            Centre(buttonRect, ConfirmButtonSize);
            buttonRect.anchoredPosition = anchoredPosition;

            var buttonImage = buttonObject.GetComponent<Image>();
            ConfigureRounded(buttonImage);
            _badgeImages.Add(buttonImage);

            Text labelText = CreateLabel(
                buttonRect, "Label", label, 38, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero);
            _inkTexts.Add(labelText);

            return buttonRect;
        }

        private void BuildOptions(RectTransform root)
        {
            IReadOnlyList<ThemeDefinition> themes = _settingsModel.AvailableThemes;
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(themes.Count / (float)COLUMN_COUNT));

            for (int themeIndex = 0; themeIndex < themes.Count; themeIndex++)
            {
                ThemeDefinition theme = themes[themeIndex];
                if (theme == null)
                {
                    continue;
                }

                int column = themeIndex % COLUMN_COUNT;
                int row = themeIndex / COLUMN_COUNT;

                // Centre the grid on the card: columns spread around x = 0, rows around y = 0.
                float x = (column - ((COLUMN_COUNT - 1) * 0.5f)) * _optionSpacing.x;
                float y = (((rowCount - 1) * 0.5f) - row) * _optionSpacing.y;

                _options.Add(BuildOption(root, theme, new Vector2(x, y)));
            }
        }

        private ThemeOption BuildOption(RectTransform root, ThemeDefinition theme, Vector2 anchoredPosition)
        {
            var optionObject = new GameObject($"Option_{theme.Id}", typeof(RectTransform));
            var optionRect = (RectTransform)optionObject.transform;
            optionRect.SetParent(root, false);
            Centre(optionRect, _optionSize);
            optionRect.anchoredPosition = anchoredPosition;

            var swatchSize = new Vector2(_optionSize.x - 24f, _optionSize.y - 100f);
            var swatchPosition = new Vector2(0f, 40f);

            // Sits behind the swatch and is inset-matched to it, so when it is tinted it reads as an
            // outline around the swatch only — the label below stays legible.
            var borderObject = new GameObject("Border", typeof(RectTransform), typeof(Image));
            var borderRect = (RectTransform)borderObject.transform;
            borderRect.SetParent(optionRect, false);
            Centre(borderRect, swatchSize + (Vector2.one * (_selectionBorderThickness * 2f)));
            borderRect.anchoredPosition = swatchPosition;
            var borderImage = borderObject.GetComponent<Image>();
            ConfigureRounded(borderImage);

            // One gradient texture per theme, created once here and never regenerated — the swatch
            // always previews its own theme, not the active one.
            var swatchObject = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
            var swatchRect = (RectTransform)swatchObject.transform;
            swatchRect.SetParent(optionRect, false);
            Centre(swatchRect, swatchSize);
            swatchRect.anchoredPosition = swatchPosition;

            var swatchImage = swatchObject.GetComponent<Image>();
            swatchImage.sprite = UiSpriteFactory.CreateVerticalGradient(theme.BackgroundBottom, theme.BackgroundTop);
            swatchImage.type = Image.Type.Simple;
            swatchImage.color = Color.white;
            swatchImage.raycastTarget = false;

            BuildKindDots(
                swatchRect, theme, 44f, 72f, new Vector2(0f, (-swatchSize.y * 0.5f) + 44f), null);

            Text nameText = UiTextFactory.Create(optionRect, "Name", 38, FontStyle.Bold, Color.clear);
            nameText.text = theme.DisplayName;
            ((RectTransform)nameText.transform).anchoredPosition =
                new Vector2(0f, (-_optionSize.y * 0.5f) + 40f);

            return new ThemeOption(theme.Id, optionRect, borderImage, nameText);
        }

        /// <summary>
        /// A row of one dot per piece kind, in that kind's fill colour. Pass a null
        /// <paramref name="theme"/> to leave them unpainted and collect them in
        /// <paramref name="output"/> instead, for dots that must follow the active theme.
        /// </summary>
        private static void BuildKindDots(
            RectTransform parent, ThemeDefinition theme, float dotSize, float spacing, Vector2 centre, Image[] output)
        {
            for (int kindIndex = 0; kindIndex < ThemeDefinition.KIND_COUNT; kindIndex++)
            {
                var dotObject = new GameObject($"Kind_{kindIndex}", typeof(RectTransform), typeof(Image));
                var dotRect = (RectTransform)dotObject.transform;
                dotRect.SetParent(parent, false);
                Centre(dotRect, new Vector2(dotSize, dotSize));
                dotRect.anchoredPosition = centre + new Vector2(
                    (kindIndex - ((ThemeDefinition.KIND_COUNT - 1) * 0.5f)) * spacing, 0f);

                var dotImage = dotObject.GetComponent<Image>();
                ConfigureRounded(dotImage);

                if (theme != null)
                {
                    // Colour ids are 1-based; 0 means "empty cell".
                    dotImage.color = theme.GetFill(kindIndex + 1);
                }

                if (output != null)
                {
                    output[kindIndex] = dotImage;
                }
            }
        }

        private static Text CreateLabel(
            RectTransform parent,
            string objectName,
            string content,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 anchoredPosition)
        {
            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, Color.clear);
            text.text = content;
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

        /// <summary>One tappable swatch: its theme id plus the bits that repaint on selection.</summary>
        private sealed class ThemeOption
        {
            internal ThemeOption(int themeId, RectTransform rect, Image borderImage, Text nameText)
            {
                ThemeId = themeId;
                Rect = rect;
                BorderImage = borderImage;
                NameText = nameText;
            }

            internal int ThemeId { get; }

            internal RectTransform Rect { get; }

            internal Image BorderImage { get; }

            internal Text NameText { get; }
        }

        /// <summary>One tappable mode card: the mode it selects plus the bits that repaint on selection.</summary>
        private sealed class ModeOption
        {
            internal ModeOption(GameMode mode, RectTransform rect, Image borderImage, Text nameText)
            {
                Mode = mode;
                Rect = rect;
                BorderImage = borderImage;
                NameText = nameText;
            }

            internal GameMode Mode { get; }

            internal RectTransform Rect { get; }

            internal Image BorderImage { get; }

            internal Text NameText { get; }
        }

        /// <summary>One tappable duration chip: the length it selects plus its selection visuals.</summary>
        private sealed class DurationOption
        {
            internal DurationOption(float seconds, RectTransform rect, Image borderImage, Text nameText)
            {
                Seconds = seconds;
                Rect = rect;
                BorderImage = borderImage;
                NameText = nameText;
            }

            internal float Seconds { get; }

            internal RectTransform Rect { get; }

            internal Image BorderImage { get; }

            internal Text NameText { get; }
        }
    }
}
