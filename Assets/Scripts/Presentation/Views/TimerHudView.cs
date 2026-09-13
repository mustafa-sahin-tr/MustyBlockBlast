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
    /// The timed-mode countdown, sitting just under the score. Binds to <see cref="TimerModel"/> and
    /// shows nothing at all in endless runs.
    /// <para>
    /// This is also the single place the app-lifecycle pause is observed: Unity only delivers
    /// <see cref="OnApplicationPause"/> to MonoBehaviours, so the View forwards it to
    /// <see cref="TimerRunSystem.SetAppPaused"/> rather than the System polling for it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimerHudView : MonoBehaviour
    {
        // Reads as a third line under the score/best pair: the band between the "BEST" label (~665)
        // and the top of the board card (~604) is the only free strip up there.
        [Header("Layout")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 634f);
        [SerializeField] private int _fontSize = 48;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        private TimerModel _timerModel;
        private GameModeSystem _gameModeSystem;
        private TimerRunSystem _timerRunSystem;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private Text _timerText;

        /// <summary>Last value rendered, so a per-frame tick only touches the label when it changes.</summary>
        private int _displayedSeconds = -1;

        [Inject]
        public void Construct(
            TimerModel timerModel,
            GameModeSystem gameModeSystem,
            TimerRunSystem timerRunSystem,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem)
        {
            _timerModel = timerModel;
            _gameModeSystem = gameModeSystem;
            _timerRunSystem = timerRunSystem;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(400f, 100f);
            rect.anchoredPosition = _anchoredPosition;

            // Built in Awake, before the theme is known; the theme subscription in Start paints it.
            _timerText = UiTextFactory.Create(rect, "TimerLabel", _fontSize, FontStyle.Bold, Color.clear);
            _timerText.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (_timerModel == null || _gameModeSystem == null || _timerRunSystem == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null)
            {
                Debug.LogError($"{nameof(TimerHudView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);

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

            _timerText.color = theme.Ink;
        }

        private void OnModeChanged(GameMode mode) => _timerText.gameObject.SetActive(mode == GameMode.Timed);

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
        /// Paints <see cref="_displayedSeconds"/> through the shared seconds format, so the countdown
        /// and the "best" suffix can never disagree on how a duration is spelled.
        /// </summary>
        private void RenderCountdown()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(_displayedSeconds);
            _timerText.text = _localizationSystem.Format(LocalizationKeys.FORMAT_SECONDS, _stringBuilder.ToString());
        }
    }
}
