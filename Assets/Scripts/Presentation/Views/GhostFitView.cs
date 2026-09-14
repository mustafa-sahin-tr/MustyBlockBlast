using MustyBlockBlast.Core;
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
    /// Draws the Ghost Fit suggestion: a pulsing silhouette on the board cells the suggested piece would
    /// fill, the matching dock slot pulsing in step with it, and — when the search found nothing to
    /// point at — a short "no placements possible" line instead.
    /// <para>
    /// It owns the pulse and nothing else. The move itself comes from <see cref="GhostFitModel"/>, and
    /// the two things that draw it (<see cref="BoardView"/> and <see cref="PieceTrayView"/>) are handed a
    /// 0..1 phase per frame rather than an animation, so neither of them grows a clock. Driving both from
    /// one place is also what keeps the board silhouette and the dock slot breathing together instead of
    /// drifting apart.
    /// </para>
    /// <para>
    /// Unscaled time on purpose: every pause reason the run has (a modal panel, a power-up armed, the app
    /// backgrounded) is a reason the player is looking at something else, and a hint that froze
    /// mid-breath underneath a panel would read as a bug on the way back. It matches the clear-fade
    /// animation in <see cref="BoardView"/>, which is unscaled for the same reason.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostFitView : MonoBehaviour
    {
        [Header("Pulse")]
        [Tooltip("Full breaths per second of the silhouette and the dock slot.")]
        [SerializeField] private float _pulsesPerSecond = 0.8f;

        [Tooltip("Lowest point of the pulse, as a fraction of the full tint. Above zero so the "
            + "silhouette never blinks out entirely.")]
        [SerializeField] private float _minimumPulse = 0.25f;

        [Header("No-placements notice")]
        [Tooltip("Notice centre in canvas space. Sits below the 2x banner so the two can be on screen "
            + "together without overlapping.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -380f);
        [SerializeField] private int _fontSize = 36;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private GhostFitModel _ghostFitModel;
        private TrayModel _trayModel;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private BoardView _boardView;
        private PieceTrayView _trayView;

        private Text _noticeText;

        /// <summary>The suggestion being drawn, cached on the subscription rather than re-read every
        /// frame. Null piece means nothing is being suggested.</summary>
        private Piece _suggestedPiece;
        private GridPosition _suggestedAnchor;
        private int _suggestedSlotIndex = -1;

        [Inject]
        public void Construct(
            GhostFitModel ghostFitModel,
            TrayModel trayModel,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            BoardView boardView,
            PieceTrayView trayView)
        {
            _ghostFitModel = ghostFitModel;
            _trayModel = trayModel;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _boardView = boardView;
            _trayView = trayView;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(700f, 90f);
            rect.anchoredPosition = _anchoredPosition;

            // Built in Awake, before the theme and the locale are known; the subscriptions in Start
            // paint and fill it.
            _noticeText = UiTextFactory.Create(rect, "GhostFitNotice", _fontSize, FontStyle.Bold, Color.clear);
            _noticeText.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (_ghostFitModel == null || _trayModel == null || _settingsModel == null
                || _localizationModel == null || _localizationSystem == null || _boardView == null
                || _trayView == null)
            {
                Debug.LogError(
                    $"{nameof(GhostFitView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);

            // Last: the only subscription that decides what is on screen, so it must run once the theme
            // and the locale are both known.
            _ghostFitModel.Hint.Subscribe(OnHintChanged).AddTo(_disposables);
        }

        private void Update()
        {
            if (_suggestedPiece == null)
            {
                return;
            }

            float pulse = CurrentPulse();
            _boardView.ShowGhostFitSilhouette(_suggestedPiece, _suggestedAnchor, pulse);
            _trayView.SetHintedSlot(_suggestedSlotIndex, pulse);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>A sine breath mapped into <see cref="_minimumPulse"/>..1, so the silhouette pulses
        /// between "clearly there" and "unmistakable" rather than between invisible and solid.</summary>
        private float CurrentPulse()
        {
            float phase = Mathf.Sin(Time.unscaledTime * _pulsesPerSecond * 2f * Mathf.PI);
            return Mathf.Lerp(Mathf.Clamp01(_minimumPulse), 1f, (phase * 0.5f) + 0.5f);
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            // The accent, matching the 2x banner: both are momentary states the player asked for, not
            // permanent HUD furniture.
            _noticeText.color = theme.Accent;
        }

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            if (!_noticeText.gameObject.activeSelf)
            {
                return;
            }

            RenderNotice();
        }

        /// <summary>
        /// Adopts whatever the model now says. The suggestion is cached here rather than polled, so a
        /// dismissal takes the silhouette off the board in the same frame it happens — the per-frame
        /// work below then only ever animates a hint that is still live.
        /// </summary>
        private void OnHintChanged(GhostFitHint hint)
        {
            bool isSuggesting = hint.State == GhostFitHintState.Suggested;

            _suggestedPiece = isSuggesting ? _trayModel.GetPiece(hint.Move.SlotIndex) : null;
            _suggestedAnchor = hint.Move.Anchor;
            _suggestedSlotIndex = isSuggesting ? hint.Move.SlotIndex : -1;

            if (_suggestedPiece == null)
            {
                _boardView.ClearGhostFitSilhouette();
                _trayView.SetHintedSlot(-1, 0f);
            }

            bool showNotice = hint.State == GhostFitHintState.NoPlacements;
            _noticeText.gameObject.SetActive(showNotice);

            if (showNotice)
            {
                RenderNotice();
            }
        }

        private void RenderNotice()
            => _noticeText.text = _localizationSystem.Translate(LocalizationKeys.POWERUP_GHOST_FIT_NO_PLACEMENTS);
    }
}
