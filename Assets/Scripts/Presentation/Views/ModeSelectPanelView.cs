using System.Collections.Generic;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Views.Shared;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The mode-select screen (issue #379): shown after the splash on every launch, before gameplay,
    /// so the player always chooses what to play rather than silently resuming the last mode. No card
    /// and no well — the choices sit straight on the splash screen's own blue gradient
    /// (<see cref="SplashBackgroundView"/>), so the screen reads as a continuation of the splash rather
    /// than a popup over the game. Top to bottom: the title, the Klasik box with its four duration
    /// chips inside, then Şölen and Macera as two upright buttons side by side. All of it comes from
    /// <see cref="ModePlateBuilder"/>, the same builder the settings card's Mode screen draws from, so
    /// the last-played mode wears the same PLAYING tag and accent ring it wears in Settings.
    /// <para>
    /// A tap is the whole interaction: a duration chip or the Klasik box's header starts a Klasik run
    /// at that length (the header uses the length already ringed), Şölen starts an endless run, and
    /// Macera goes into gameplay with the level path picker opened on top. Every one of those hands
    /// over to <see cref="ModeSelectSystem"/>, which records the pick and loads gameplay; there is no
    /// back, dismiss or skip — nothing else leaves this screen.
    /// </para>
    /// <para>
    /// Built once in <see cref="Start"/>, painted from the active <see cref="ThemeDefinition"/> and
    /// re-worded from the active locale, like every other card. Never raycasts: taps arrive through
    /// <see cref="ModeSelectInputView"/>'s pointer action and are hit-tested here, the way
    /// <see cref="BoardInputView"/> feeds the settings card.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModeSelectPanelView : MonoBehaviour
    {
        /// <summary>The title row above the choices.</summary>
        private const float HEADER_HEIGHT = 88f;

        private const int TITLE_FONT_SIZE = 48;

        /// <summary>Breathing room between the Klasik box and the two mode buttons under it — wider than
        /// a plate gap, since the two are different kinds of thing rather than neighbours in a list.</summary>
        private const float SECTION_GAP = 36f;

        /// <summary>Horizontal gap between the Şölen and Macera buttons.</summary>
        private const float BUTTON_GAP = ModePlateBuilder.ROW_GAP;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        [Header("Layout")]
        [Tooltip("Width of the whole stack: the Klasik box, and the two mode buttons together.")]
        [FormerlySerializedAs("_cardWidth")]
        [SerializeField] private float _contentWidth = 880f;

        [Header("Art")]
        [Tooltip("The chunky display face for the title, the mode names and the duration chips. Falls "
            + "back to the built-in runtime font when unassigned.")]
        [SerializeField] private Font _displayFont;

        private GameModeModel _gameModeModel;
        private TimedModeSystem _timedModeSystem;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ModeSelectSystem _modeSelectSystem;
        private Canvas _canvas;

        private ModePlateBuilder _modePlates;
        private ThemeDefinition _currentTheme;

        private Text _titleText;

        [Inject]
        public void Construct(
            GameModeModel gameModeModel,
            TimedModeSystem timedModeSystem,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            ModeSelectSystem modeSelectSystem)
        {
            _gameModeModel = gameModeModel;
            _timedModeSystem = timedModeSystem;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _modeSelectSystem = modeSelectSystem;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_gameModeModel == null || _timedModeSystem == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null || _modeSelectSystem == null)
            {
                Debug.LogError(
                    $"{nameof(ModeSelectPanelView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Built in Start rather than Awake: the chips come from the injected duration ladder, which
            // only exists once VContainer has run Construct.
            _modePlates = new ModePlateBuilder(_localizationSystem, _localizationModel, _timedModeSystem, _displayFont);
            BuildPanel();

            // Locale before theme: the theme handler repaints labels that have to be worded first.
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // The pre-highlight: the persisted mode and length, already loaded into the Model and the
            // System by the time this runs, paint their ring and tag on first subscription.
            _gameModeModel.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);
            _timedModeSystem.SelectedDuration.Subscribe(OnSelectedDurationChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>
        /// Routes a tap. The duration chips live inside the Klasik box, so they are tested before the
        /// mode options themselves — the same "options before the row that hosts them" order the
        /// settings card uses. A tap that hits nothing is nothing: there is no scrim to dismiss.
        /// </summary>
        internal void HandleTap(Vector2 screenPosition)
        {
            if (_modePlates == null)
            {
                return;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            IReadOnlyList<DurationOption> durationOptions = _modePlates.DurationOptions;
            for (int optionIndex = 0; optionIndex < durationOptions.Count; optionIndex++)
            {
                DurationOption option = durationOptions[optionIndex];
                if (RectTransformUtility.RectangleContainsScreenPoint(option.Rect, screenPosition, eventCamera))
                {
                    _modeSelectSystem.ChooseTimed(option.Seconds);
                    return;
                }
            }

            IReadOnlyList<ModeOption> modeOptions = _modePlates.ModeOptions;
            for (int optionIndex = 0; optionIndex < modeOptions.Count; optionIndex++)
            {
                ModeOption option = modeOptions[optionIndex];
                if (!RectTransformUtility.RectangleContainsScreenPoint(option.Rect, screenPosition, eventCamera))
                {
                    continue;
                }

                switch (option.Mode)
                {
                    case GameMode.Timed:
                        // The box's header, outside every chip: the length already ringed is the one
                        // the player is looking at, so it is the one they get.
                        _modeSelectSystem.ChooseTimed(_timedModeSystem.SelectedDuration.Value);
                        break;
                    case GameMode.Path:
                        _modeSelectSystem.ChoosePath();
                        break;
                    default:
                        _modeSelectSystem.ChooseEndless();
                        break;
                }

                return;
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

            // The title is not themed: it sits on the splash's fixed blue gradient, where white is the
            // one colour that reads in every season.
            _modePlates.Repaint(theme);
            RefreshModeSelection();
            RefreshDurationSelection();
        }

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            _titleText.text = _localizationSystem.Translate(LocalizationKeys.SETTINGS_MODE_SCREEN_TITLE);
            _modePlates.Relocalize();
        }

        private void OnModeChanged(GameMode mode)
        {
            RefreshModeSelection();
            RefreshDurationSelection();
        }

        private void OnSelectedDurationChanged(float seconds) => RefreshDurationSelection();

        /// <summary>The last-played mode wears both the accent ring and the PLAYING tag — the same
        /// pre-highlight the settings card's Mode screen shows on open.</summary>
        private void RefreshModeSelection()
        {
            if (_currentTheme == null)
            {
                return;
            }

            GameMode current = _gameModeModel.CurrentMode.Value;
            // The PLAYING tag sits on a coloured plate here, not the neutral card the settings screen
            // uses — plain white reads regardless of which mode's colour is under it.
            _modePlates.RefreshModeSelection(_currentTheme, current, current, _currentTheme.Accent, Color.white);
        }

        /// <summary>Rings the selected length only while Klasik is the mode actually playing — otherwise
        /// a length ring would compete with another mode's PLAYING tag for "this is what's active",
        /// when the persisted length has nothing to do with what's playing right now.</summary>
        private void RefreshDurationSelection()
        {
            if (_currentTheme == null)
            {
                return;
            }

            bool klasikPlaying = _gameModeModel.CurrentMode.Value == GameMode.Timed;
            float ringedSeconds = klasikPlaying ? _timedModeSystem.SelectedDuration.Value : float.NaN;
            _modePlates.RefreshDurationSelection(_currentTheme, ringedSeconds);
        }

        // ---------------------------------------------------------------------------- building

        /// <summary>
        /// Lays the stack out straight on the panel, vertically centred: title, Klasik box, then the
        /// Şölen and Macera buttons side by side. A running cursor walks down from the stack's top.
        /// </summary>
        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            float stackHeight = HEADER_HEIGHT + ModePlateBuilder.ROW_GAP
                + ModePlateBuilder.DURATION_BOX_HEIGHT + SECTION_GAP
                + ModePlateBuilder.STANDALONE_BUTTON_HEIGHT;
            float stackTop = stackHeight * 0.5f;
            float cursorY = 0f;

            _titleText = UiTextFactory.Create(rect, "Title", TITLE_FONT_SIZE, FontStyle.Normal, Color.white, _displayFont);
            var titleRect = (RectTransform)_titleText.transform;
            titleRect.sizeDelta = new Vector2(_contentWidth, HEADER_HEIGHT);
            titleRect.anchoredPosition = new Vector2(0f, stackTop - (HEADER_HEIGHT * 0.5f));
            cursorY += HEADER_HEIGHT + ModePlateBuilder.ROW_GAP;

            _modePlates.BuildDurationBox(
                rect, _contentWidth,
                new Vector2(0f, stackTop - cursorY - (ModePlateBuilder.DURATION_BOX_HEIGHT * 0.5f)));
            cursorY += ModePlateBuilder.DURATION_BOX_HEIGHT + SECTION_GAP;

            float buttonWidth = (_contentWidth - BUTTON_GAP) * 0.5f;
            var buttonSize = new Vector2(buttonWidth, ModePlateBuilder.STANDALONE_BUTTON_HEIGHT);
            float buttonY = stackTop - cursorY - (ModePlateBuilder.STANDALONE_BUTTON_HEIGHT * 0.5f);
            float buttonX = (buttonWidth + BUTTON_GAP) * 0.5f;

            _modePlates.BuildStandaloneModeButton(rect, GameMode.Endless, buttonSize, new Vector2(-buttonX, buttonY));
            _modePlates.BuildStandaloneModeButton(rect, GameMode.Path, buttonSize, new Vector2(buttonX, buttonY));
        }
    }
}
