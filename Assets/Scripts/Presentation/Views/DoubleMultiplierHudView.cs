using System.Text;
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
    /// The "2x" frenzy banner, sitting in the gap between the board card and the power-up strip. Binds
    /// to <see cref="DoubleMultiplierModel"/> and is hidden entirely whenever no window is open — which
    /// is most of a run.
    /// <para>
    /// Built as a sibling of <see cref="TimerHudView"/> rather than folded into it: the two clocks are
    /// unrelated (one can run in an endless run, the other only in a timed one) and they would have to
    /// share a label. It deliberately does not show a paused state of its own — the countdown simply
    /// stops moving, and every pause reason already puts something modal on screen to explain why.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoubleMultiplierHudView : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -330f);
        [SerializeField] private int _fontSize = 44;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        private DoubleMultiplierModel _doubleMultiplierModel;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private Text _bannerText;

        /// <summary>Last whole second rendered, so a per-frame countdown only touches the label — and so
        /// only rebuilds the canvas mesh — when the number actually changes.</summary>
        private int _displayedSeconds = -1;

        [Inject]
        public void Construct(
            DoubleMultiplierModel doubleMultiplierModel,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem)
        {
            _doubleMultiplierModel = doubleMultiplierModel;
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
            rect.sizeDelta = new Vector2(400f, 90f);
            rect.anchoredPosition = _anchoredPosition;

            // Built in Awake, before the theme is known; the theme subscription in Start paints it.
            _bannerText = UiTextFactory.Create(rect, "DoubleMultiplierLabel", _fontSize, FontStyle.Bold, Color.clear);
            _bannerText.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (_doubleMultiplierModel == null || _settingsModel == null || _localizationModel == null
                || _localizationSystem == null)
            {
                Debug.LogError(
                    $"{nameof(DoubleMultiplierHudView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);

            // Last: it is the only subscription that renders the label, so it must run after the locale
            // is known.
            _doubleMultiplierModel.RemainingSeconds.Subscribe(OnRemainingChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            // The accent, not the ink: a frenzy is a temporary, celebratory state and must not read as
            // just another permanent HUD line.
            _bannerText.color = theme.Accent;
        }

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            if (_displayedSeconds <= 0)
            {
                return;
            }

            RenderCountdown();
        }

        private void OnRemainingChanged(float remainingSeconds)
        {
            // Ceiling, so a fresh window reads its full length for its first frame and never shows "0"
            // — zero is the closed state, which hides the banner instead.
            int seconds = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            if (seconds == _displayedSeconds)
            {
                return;
            }

            _displayedSeconds = seconds;
            _bannerText.gameObject.SetActive(seconds > 0);

            if (seconds > 0)
            {
                RenderCountdown();
            }
        }

        /// <summary>Paints <see cref="_displayedSeconds"/> through the shared seconds format, so this
        /// countdown and the timed-mode one can never disagree on how a duration is spelled — then
        /// wraps that through the per-language "2x" template, since the multiplier idiom itself
        /// (unlike a bare symbol) differs by language.</summary>
        private void RenderCountdown()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(_displayedSeconds);
            string formattedSeconds =
                _localizationSystem.Format(LocalizationKeys.FORMAT_SECONDS, _stringBuilder.ToString());
            _bannerText.text = _localizationSystem.Format(
                LocalizationKeys.POWERUP_DOUBLE_MULTIPLIER_ACTIVE, formattedSeconds);
        }
    }
}
