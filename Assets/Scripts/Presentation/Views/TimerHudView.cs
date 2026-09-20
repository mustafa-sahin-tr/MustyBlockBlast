using System.Text;
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
    /// The timed-mode countdown as a glossy pill — a clock and "mm:ss" — in the score card's centre
    /// slot (issue #265). Binds to <see cref="TimerModel"/> and shows nothing at all in endless and
    /// path runs. Under the low-time threshold the pill turns the warning pink, which is not
    /// theme-derived: "you are nearly out of time" has to read the same in every season.
    /// <para>
    /// Parented onto <see cref="ScoreView.CentreSlot"/> in <see cref="Start"/> rather than Awake: the
    /// card builds its slot in Awake and sibling Awake order is not guaranteed. When both this and the
    /// streak want the slot the countdown wins; <see cref="StreakPillView"/> yields by mode.
    /// </para>
    /// <para>
    /// This is also the single place the app-lifecycle pause is observed: Unity only delivers
    /// <see cref="OnApplicationPause"/> to MonoBehaviours, so the View forwards it to
    /// <see cref="TimerRunSystem.SetAppPaused"/> rather than the System polling for it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimerHudView : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private float _pillHeight = 88f;
        [SerializeField] private float _paddingX = 30f;
        [SerializeField] private float _glyphSize = 40f;
        [SerializeField] private float _glyphGap = 12f;
        [SerializeField] private int _fontSize = 50;

        [Header("Colour")]
        [Tooltip("Which theme kind's fill the pill takes while there is plenty of time: the blue-ish " +
            "kind of each season, so the clock recolours with the board.")]
        [SerializeField] private int _timerKind = 2;

        [Tooltip("Pill colour once the low-time threshold is crossed. Fixed across themes on purpose.")]
        [SerializeField] private Color _lowTimeWarningColor = new Color(0.94f, 0.38f, 0.57f, 1f);

        [Header("Art")]
        [Tooltip("The chunky display face for the clock. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("White 9-sliced glossy button, shared with the shop. Tinted at runtime from the theme.")]
        [SerializeField] private Sprite _buttonSprite;

        [Tooltip("White clock silhouette on the pill's left. Hidden when unassigned.")]
        [SerializeField] private Sprite _clockSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        private TimerModel _timerModel;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private TimerRunSystem _timerRunSystem;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ScoreView _scoreView;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Image _pillPlate;
        private Image _clockImage;
        private RectTransform _clockRect;
        private Text _timerText;
        private RectTransform _timerRect;

        /// <summary>Last value rendered, so a per-frame tick only touches the label when it changes.</summary>
        private int _displayedSeconds = -1;

        /// <summary>Kept so the low-time toggle can repaint without waiting for a theme switch.</summary>
        private ThemeDefinition _currentTheme;

        /// <summary>Mirrors <see cref="TimerModel.IsLowTime"/>, so a theme switch repaints the right look.</summary>
        private bool _isLowTime;

        [Inject]
        public void Construct(
            TimerModel timerModel,
            GameModeSystem gameModeSystem,
            TimedModeSystem timedModeSystem,
            TimerRunSystem timerRunSystem,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            ScoreView scoreView)
        {
            _timerModel = timerModel;
            _gameModeSystem = gameModeSystem;
            _timedModeSystem = timedModeSystem;
            _timerRunSystem = timerRunSystem;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _scoreView = scoreView;
        }

        private void Awake() => BuildPill();

        private void Start()
        {
            if (_timerModel == null || _gameModeSystem == null || _timedModeSystem == null
                || _timerRunSystem == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null || _scoreView == null)
            {
                Debug.LogError($"{nameof(TimerHudView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _rect.SetParent(_scoreView.CentreSlot, false);
            HudChrome.Centre(_rect, _rect.sizeDelta);
            _rect.SetAsLastSibling();

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);
            _timedModeSystem.SelectedDuration.Subscribe(OnSelectedDurationChanged).AddTo(_disposables);

            _timerModel.IsLowTime.Subscribe(OnLowTimeChanged).AddTo(_disposables);

            // Last: it is the only subscription that renders the label, so it must run after the
            // locale is known.
            _timerModel.RemainingSeconds.Subscribe(OnRemainingChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>
        /// A backgrounded app must neither lose nor gain seconds, so the countdown is suspended for
        /// exactly as long as the app is away. Dragging a piece must not stop the clock; the Settings
        /// panel is the other pause source, handled separately in <c>SettingsPanelView</c>.
        /// </summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (_timerRunSystem == null)
            {
                return;
            }

            _timerRunSystem.SetAppPaused(pauseStatus);
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            _timerText.color = Color.white;
            _clockImage.color = _clockSprite != null ? Color.white : Color.clear;
            ApplyPillColour();
        }

        private void OnLowTimeChanged(bool isLowTime)
        {
            _isLowTime = isLowTime;
            ApplyPillColour();
        }

        /// <summary>
        /// The single writer of the pill's colour, so the theme subscription and the low-time
        /// subscription can never fight over it.
        /// </summary>
        private void ApplyPillColour()
        {
            if (_isLowTime)
            {
                _pillPlate.color = _lowTimeWarningColor;
                return;
            }

            _pillPlate.color = _currentTheme != null ? _currentTheme.GetFill(_timerKind) : Color.clear;
        }

        private void OnModeChanged(GameMode mode) => ApplyVisibility();

        /// <summary>
        /// The selected length decides visibility as much as the mode does: Classic's "Sınırsız" entry
        /// is a timed-mode duration that never counts down (issue #363), so the pill must go away for it
        /// exactly as it does in Endless.
        /// </summary>
        private void OnSelectedDurationChanged(float durationSeconds) => ApplyVisibility();

        /// <summary>
        /// The single writer of the pill's visibility, so the mode subscription and the duration
        /// subscription can never fight over it. Shown through the CanvasGroup rather than by toggling
        /// the GameObject, so a mode switch never rebuilds the shared canvas mesh.
        /// </summary>
        private void ApplyVisibility()
        {
            bool hasCountdown = _gameModeSystem.CurrentMode.Value == GameMode.Timed
                && !TimedModeConfig.IsEndlessDuration(_timedModeSystem.SelectedDuration.Value);
            _group.alpha = hasCountdown ? 1f : 0f;
        }

        /// <summary>
        /// Re-renders the countdown in the new language. Skipped before the first tick, when there is
        /// no second to re-render yet.
        /// </summary>
        private void OnLocaleChanged(LocaleDefinition locale)
        {
            if (_displayedSeconds < 0)
            {
                return;
            }

            RenderCountdown();
        }

        private void OnRemainingChanged(float remainingSeconds)
        {
            // Ceiling, so a fresh 15s round reads "15" for its first frame and "0" only once expired.
            int seconds = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            if (seconds == _displayedSeconds)
            {
                return;
            }

            _displayedSeconds = seconds;
            RenderCountdown();
        }

        /// <summary>
        /// Paints <see cref="_displayedSeconds"/> as <c>mm:ss</c>. Deliberately not a String Table
        /// entry: a zero-padded clock is a universal numeric format, not a phrase, so there is nothing
        /// here for a translator to translate. Built on the cached builder rather than with
        /// interpolation, so a per-second repaint stays free of throwaway strings. The pill is
        /// re-measured around the text each time, since a proportional face draws "11" narrower than "00".
        /// </summary>
        private void RenderCountdown()
        {
            int minutes = _displayedSeconds / 60;
            int seconds = _displayedSeconds % 60;

            _stringBuilder.Clear();
            AppendPadded(minutes);
            _stringBuilder.Append(':');
            AppendPadded(seconds);
            _timerText.text = _stringBuilder.ToString();

            float glyphWidth = _clockSprite != null ? _glyphSize + _glyphGap : 0f;
            float pillWidth = (_paddingX * 2f) + glyphWidth + _timerText.preferredWidth;
            _rect.sizeDelta = new Vector2(pillWidth, _pillHeight);
            ((RectTransform)_pillPlate.transform).sizeDelta = _rect.sizeDelta;

            float left = (-pillWidth * 0.5f) + _paddingX;
            _clockRect.anchoredPosition = new Vector2(left + (_glyphSize * 0.5f), 0f);
            _timerRect.anchoredPosition = new Vector2(left + glyphWidth, 0f);
        }

        /// <summary>Appends <paramref name="value"/> zero-padded to two digits.</summary>
        private void AppendPadded(int value)
        {
            if (value < 10)
            {
                _stringBuilder.Append('0');
            }

            _stringBuilder.Append(value);
        }

        /// <summary>Built hidden, before the theme is known; the theme subscription in Start paints it.</summary>
        private void BuildPill()
        {
            _rect = (RectTransform)transform;
            HudChrome.Centre(_rect, new Vector2(_pillHeight * 3f, _pillHeight));
            _rect.localScale = Vector3.one;

            if (!TryGetComponent(out _group))
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _pillPlate = HudChrome.BuildGlossyPill(_rect, "Pill", _rect.sizeDelta, Vector2.zero, _buttonSprite);

            _clockImage = HudChrome.BuildGlyph(
                _rect, "Clock", _clockSprite, new Vector2(_glyphSize, _glyphSize), Vector2.zero);
            _clockRect = (RectTransform)_clockImage.transform;

            _timerText = HudChrome.CreateLabel(
                _rect, "TimerLabel", _fontSize, FontStyle.Normal, TextAnchor.MiddleLeft, Vector2.zero, _displayFont);
            _timerRect = (RectTransform)_timerText.transform;
        }
    }
}
