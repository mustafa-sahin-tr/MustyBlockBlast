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
    /// The "2×" frenzy window as a gold bar under the score card (issue #265): the multiplier on the
    /// left, a track whose card-coloured fill drains over the window, and the seconds left on the
    /// right. Binds to <see cref="DoubleMultiplierModel"/> and is hidden entirely whenever no window
    /// is open — which is most of a run.
    /// <para>
    /// It takes the goal row's band while it is open rather than a band of its own: the space between
    /// the card and the board is spoken for, and a fifteen-second bonus reads better as a banner that
    /// briefly stands in for the goals than as one that shoves them. <see cref="ObjectiveIconContainerView"/>
    /// dims itself for the window's duration; nothing else on the HUD moves.
    /// </para>
    /// <para>
    /// Built as a sibling of <see cref="TimerHudView"/> rather than folded into it: the two clocks are
    /// unrelated (one can run in an endless run, the other only in a timed one). It deliberately does
    /// not show a paused state of its own — the fill simply stops draining, and every pause reason
    /// already puts something modal on screen to explain why.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoubleMultiplierHudView : MonoBehaviour
    {
        /// <summary>Which theme kind's fill the bar takes: the third kind — every season's gold.</summary>
        private const int BAR_KIND = 3;

        /// <summary>How far the gold is pulled toward black for the bar's ink (the mockup's #5A3A00).</summary>
        private const float INK_SHADE = 0.6f;

        /// <summary>Alpha of the empty track over the gold.</summary>
        private const float TRACK_ALPHA = 0.25f;

        [Header("Layout")]
        [Tooltip("Centre of the bar, in reference pixels from the canvas centre: the goal row's band.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 590f);

        [SerializeField] private Vector2 _barSize = new Vector2(960f, 80f);
        [SerializeField] private float _paddingX = 34f;
        [SerializeField] private float _trackHeight = 26f;
        [SerializeField] private float _trackGap = 24f;
        [SerializeField] private int _multiplierFontSize = 50;
        [SerializeField] private int _secondsFontSize = 40;

        [Header("Art")]
        [Tooltip("The chunky display face for the multiplier and the seconds. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("White 9-sliced glossy button, shared with the shop. Tinted gold at runtime from the theme.")]
        [SerializeField] private Sprite _buttonSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        private DoubleMultiplierModel _doubleMultiplierModel;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;

        private CanvasGroup _group;
        private Image _barPlate;
        private Image _trackImage;
        private Image _fillImage;
        private RectTransform _fillRect;
        private Text _multiplierText;
        private Text _secondsText;
        private float _trackWidth;

        /// <summary>Last whole second rendered, so a per-frame countdown only touches the label — and so
        /// only rebuilds the canvas mesh — when the number actually changes. The fill, by contrast,
        /// drains every tick: a bar that stepped once a second would read as stuck.</summary>
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

        private void Awake() => BuildBar();

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

            LayOutTrack();

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

            Color gold = theme.GetFill(BAR_KIND);
            Color ink = HudChrome.Darken(gold, INK_SHADE);

            _barPlate.color = gold;
            _trackImage.color = HudChrome.WithAlpha(ink, TRACK_ALPHA);
            _fillImage.color = theme.CardBackground;
            _multiplierText.color = ink;
            _secondsText.color = ink;
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
            // The fill follows the raw seconds so it drains smoothly; the label follows the ceiling,
            // so a fresh window reads its full length for its first frame and never shows "0" — zero
            // is the closed state, which hides the bar instead.
            float fraction = Mathf.Clamp01(remainingSeconds / DoubleMultiplierModel.WINDOW_SECONDS);
            _fillRect.sizeDelta = new Vector2(_trackWidth * fraction, _trackHeight);

            int seconds = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            if (seconds == _displayedSeconds)
            {
                return;
            }

            _displayedSeconds = seconds;
            _group.alpha = seconds > 0 ? 1f : 0f;

            if (seconds > 0)
            {
                RenderCountdown();
            }
        }

        /// <summary>Paints <see cref="_displayedSeconds"/> through the shared seconds format, so this
        /// countdown and the timed-mode one can never disagree on how a duration is spelled. The
        /// "2×" itself is a bare symbol rather than the per-language template the old banner used: on
        /// this bar it is a badge beside a track, not a sentence.</summary>
        private void RenderCountdown()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(_displayedSeconds);
            _secondsText.text = _localizationSystem.Format(LocalizationKeys.FORMAT_SECONDS, _stringBuilder.ToString());
        }

        /// <summary>Built hidden, before the theme is known; the theme subscription in Start paints it.</summary>
        private void BuildBar()
        {
            var rect = (RectTransform)transform;
            HudChrome.Centre(rect, _barSize);
            rect.anchoredPosition = _anchoredPosition;
            rect.localScale = Vector3.one;

            if (!TryGetComponent(out _group))
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _barPlate = HudChrome.BuildGlossyPill(rect, "Bar", _barSize, Vector2.zero, _buttonSprite);

            float leftX = (-_barSize.x * 0.5f) + _paddingX;
            float rightX = (_barSize.x * 0.5f) - _paddingX;

            _multiplierText = HudChrome.CreateLabel(
                rect, "MultiplierLabel", _multiplierFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(leftX, 0f), _displayFont);
            _multiplierText.text = "2×";

            _secondsText = HudChrome.CreateLabel(
                rect, "SecondsLabel", _secondsFontSize, FontStyle.Normal, TextAnchor.MiddleRight,
                new Vector2(rightX, 0f), _displayFont);
        }

        /// <summary>
        /// The track runs between the two labels. Their widths are measured off the widest strings
        /// they will show rather than the current ones, so the track never jumps when "15 sn"
        /// becomes "9 sn". Laid out in Start rather than Awake because the seconds format is read
        /// off the injected localization, which has only certainly arrived by then.
        /// </summary>
        private void LayOutTrack()
        {
            var rect = (RectTransform)transform;
            float leftX = (-_barSize.x * 0.5f) + _paddingX;
            float rightX = (_barSize.x * 0.5f) - _paddingX;

            _secondsText.text = _localizationSystem.Format(LocalizationKeys.FORMAT_SECONDS, "00");
            float trackLeft = leftX + _multiplierText.preferredWidth + _trackGap;
            float trackRight = rightX - _secondsText.preferredWidth - _trackGap;
            _trackWidth = Mathf.Max(0f, trackRight - trackLeft);
            _secondsText.text = string.Empty;

            float trackRadius = _trackHeight * 0.5f;
            _trackImage = HudChrome.BuildRounded(
                rect, "Track", new Vector2(_trackWidth, _trackHeight),
                new Vector2((trackLeft + trackRight) * 0.5f, 0f), trackRadius);

            // Left-pivoted inside the track so shrinking its width drains it from the right.
            _fillImage = HudChrome.BuildRounded(
                rect, "Fill", new Vector2(_trackWidth, _trackHeight), Vector2.zero, trackRadius);
            _fillRect = (RectTransform)_fillImage.transform;
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.anchoredPosition = new Vector2(trackLeft, 0f);
        }
    }
}
