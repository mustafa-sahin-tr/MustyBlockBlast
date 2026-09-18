using System.Text;
using MustyBlockBlast.Core;
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
    /// The combo streak as a glossy pill — a flame and "x4 STREAK" — shown while
    /// <see cref="ScoreModel.Streak"/> is above zero and hidden otherwise (issue #265). Binds to the
    /// streak and renders the score multiplier it has earned, via the same
    /// <see cref="ScoreRules.ComboStreakBonus"/> the scoring uses, so the pill can never promise a
    /// bonus the next clear does not pay. Uncapped, so the number on the pill is always exactly the
    /// streak count itself — a chain long enough to matter is its own limiter.
    /// <para>
    /// It lives in <see cref="ScoreView.CentreSlot"/>, unless the countdown wants that slot: in Timed
    /// mode the timer wins and the pill moves to the centre of the goals band
    /// (<see cref="ObjectiveIconContainerView.CentreSlot"/>) instead. The goal row and the level pill
    /// are Path-mode only (issue #269), so in Timed mode that band is otherwise empty and the streak
    /// pill has it to itself; it ignores the row's CanvasGroup so the hidden row does not hide it.
    /// Parented in <see cref="Start"/> rather than Awake: both hosts build their rects in Awake and
    /// sibling Awake order is not guaranteed, so they are only safe to attach to once every Awake
    /// has run.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreakPillView : MonoBehaviour
    {
        /// <summary>Which theme kind's fill the pill takes: the first kind — the coral the mockup's
        /// streak pill wears — so a season swap recolours it with the board.</summary>
        private const int STREAK_KIND = 1;

        [Header("Layout")]
        [SerializeField] private float _pillHeight = 88f;
        [SerializeField] private float _paddingX = 30f;
        [SerializeField] private float _glyphSize = 40f;
        [SerializeField] private float _glyphGap = 12f;
        [SerializeField] private int _fontSize = 44;

        [Header("Art")]
        [Tooltip("The chunky display face for the multiplier. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("White 9-sliced glossy button, shared with the shop. Tinted at runtime from the theme.")]
        [SerializeField] private Sprite _buttonSprite;

        [Tooltip("White flame silhouette on the pill's left. Hidden when unassigned.")]
        [SerializeField] private Sprite _flameSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        private ScoreModel _scoreModel;
        private ScoreView _scoreView;
        private ObjectiveIconContainerView _objectiveIconContainerView;
        private GameModeSystem _gameModeSystem;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Image _pillPlate;
        private Image _flameImage;
        private RectTransform _flameRect;
        private Text _label;
        private RectTransform _labelRect;

        /// <summary>Last streak rendered, so a repaint only re-measures the pill when the number changed.</summary>
        private int _displayedStreak = -1;

        /// <summary>The decimal separator of the current language, for the "2,5" in the multiplier.</summary>
        private char _decimalSeparator = ',';

        [Inject]
        public void Construct(
            ScoreModel scoreModel,
            ScoreView scoreView,
            ObjectiveIconContainerView objectiveIconContainerView,
            GameModeSystem gameModeSystem,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem)
        {
            _scoreModel = scoreModel;
            _scoreView = scoreView;
            _objectiveIconContainerView = objectiveIconContainerView;
            _gameModeSystem = gameModeSystem;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
        }

        private void Awake() => BuildPill();

        private void Start()
        {
            if (_scoreModel == null || _scoreView == null || _objectiveIconContainerView == null
                || _gameModeSystem == null || _settingsModel == null || _localizationModel == null
                || _localizationSystem == null)
            {
                Debug.LogError($"{nameof(StreakPillView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(_disposables);

            // Last: it is the only subscription that renders the label, so it must run after the
            // locale is known.
            _scoreModel.Streak.Subscribe(OnStreakChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _pillPlate.color = theme.GetFill(STREAK_KIND);
            _label.color = Color.white;
            _flameImage.color = _flameSprite != null ? Color.white : Color.clear;
        }

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            // The one number here is a multiplier with a half step, so its separator is the one
            // thing about it that varies by language.
            _decimalSeparator = locale != null && locale.Code.StartsWith("en", System.StringComparison.Ordinal) ? '.' : ',';

            if (_displayedStreak >= 0)
            {
                Render();
            }
        }

        /// <summary>
        /// The countdown has first claim on the card's centre; the pill yields to it in Timed mode and
        /// takes the centre of the goals band, which nothing else uses outside Path mode. Centred in
        /// either slot, so a re-measure grows it evenly from the middle.
        /// </summary>
        private void OnModeChanged(GameMode mode)
        {
            RectTransform host = mode == GameMode.Timed
                ? _objectiveIconContainerView.CentreSlot
                : _scoreView.CentreSlot;

            _rect.SetParent(host, false);
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.anchoredPosition = Vector2.zero;
            _rect.SetAsLastSibling();
        }

        private void OnStreakChanged(int streak)
        {
            if (streak == _displayedStreak)
            {
                return;
            }

            _displayedStreak = streak;
            Render();
        }

        /// <summary>
        /// Shown through the CanvasGroup rather than by toggling the GameObject, so a streak starting
        /// or breaking never rebuilds the shared canvas mesh. The pill is re-measured around its text
        /// on every change: a "x4" is narrower than a "x2,5".
        /// </summary>
        private void Render()
        {
            bool isVisible = _displayedStreak > 0;
            _group.alpha = isVisible ? 1f : 0f;

            if (!isVisible)
            {
                return;
            }

            _label.text = _localizationSystem.Format(LocalizationKeys.HUD_STREAK, FormatMultiplier(_displayedStreak));

            float glyphWidth = _flameSprite != null ? _glyphSize + _glyphGap : 0f;
            float textWidth = _label.preferredWidth;
            float pillWidth = (_paddingX * 2f) + glyphWidth + textWidth;
            _rect.sizeDelta = new Vector2(pillWidth, _pillHeight);
            ((RectTransform)_pillPlate.transform).sizeDelta = _rect.sizeDelta;

            float left = -pillWidth * 0.5f + _paddingX;
            _flameRect.anchoredPosition = new Vector2(left + (_glyphSize * 0.5f), 0f);
            _labelRect.anchoredPosition = new Vector2(left + glyphWidth, 0f);
        }

        /// <summary>
        /// "4": one plus the streak's bonus, which — since <see cref="ScoreRules.ComboStreakBonus"/> is
        /// uncapped and steps by a whole 1x — always resolves to the streak count itself. Still split
        /// into whole/fraction (fraction is always 0 now) rather than simplified to `streak.ToString()`
        /// directly: this stays the one place that reads the score formula, so a future rule change
        /// that reintroduces a fractional step needs no second edit here. Built on the cached builder
        /// rather than through a culture format, so a streak tick allocates the one string it hands to
        /// the template rather than a formatter's scratch.
        /// </summary>
        private string FormatMultiplier(int streak)
        {
            int tenths = Mathf.RoundToInt((float)(1.0 + ScoreRules.ComboStreakBonus(streak)) * 10f);
            int whole = tenths / 10;
            int fraction = tenths % 10;

            _stringBuilder.Clear();
            _stringBuilder.Append(whole);
            if (fraction != 0)
            {
                _stringBuilder.Append(_decimalSeparator);
                _stringBuilder.Append(fraction);
            }

            return _stringBuilder.ToString();
        }

        /// <summary>Built hidden; where it sits is decided by <see cref="OnModeChanged"/> once its hosts exist.</summary>
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

            // The goal row hides itself outside Path mode; a streak earned in Timed mode must still
            // show while the pill is a guest of that row's band.
            _group.ignoreParentGroups = true;

            _pillPlate = HudChrome.BuildGlossyPill(_rect, "Pill", _rect.sizeDelta, Vector2.zero, _buttonSprite);

            _flameImage = HudChrome.BuildGlyph(
                _rect, "Flame", _flameSprite, new Vector2(_glyphSize, _glyphSize), Vector2.zero);
            _flameRect = (RectTransform)_flameImage.transform;

            _label = HudChrome.CreateLabel(
                _rect, "Label", _fontSize, FontStyle.Normal, TextAnchor.MiddleLeft, Vector2.zero, _displayFont);
            _labelRect = (RectTransform)_label.transform;
        }
    }
}
