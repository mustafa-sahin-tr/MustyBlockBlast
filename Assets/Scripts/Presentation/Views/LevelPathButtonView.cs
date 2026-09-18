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
    /// Persistent pill that opens the level path overlay (issue #265): a card-coloured pill with a
    /// purple disc wearing the trail glyph, then "LV" and the frontier level number. In Path mode it
    /// reads "LEVEL n" for the level being played instead, and <see cref="PathLevelBadgeView"/> pins
    /// the accent star to its top-right corner. Sits at the right end of the goal row
    /// (<see cref="ObjectiveIconContainerView.TrailingSlot"/>), not in the top bar: right-of-centre
    /// up there put it under the iPhone's Dynamic Island.
    /// <para>
    /// Parented onto the row in <see cref="Start"/> rather than Awake: the row builds its slot in
    /// Awake and sibling Awake order is not guaranteed. The row hides itself when nothing is tracked
    /// and dims for a 2× window; the pill ignores that group so it stays visible and tappable in every
    /// mode.
    /// </para>
    /// <para>
    /// Like <see cref="SettingsButtonView"/> it only knows how to draw itself and whether a screen
    /// point is on it. The tap that opens the panel is routed by <see cref="BoardInputView"/>, which is
    /// the single owner of pointer input.
    /// </para>
    /// <para>
    /// The badge parents itself onto <see cref="RootRect"/>, the pill's own rect, so it follows the
    /// pill however wide the number makes it. The badge is a sibling of the plate, not a child of
    /// it, so it never widens the plate's hit test.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelPathButtonView : MonoBehaviour
    {
        /// <summary>Which theme kind's fill the disc takes: the fourth kind — every season's purple.</summary>
        private const int DISC_KIND = 4;

        [Header("Layout")]
        [Tooltip("Pill height in reference pixels. A touch under the goal row's 80 so the row stays " +
            "its height with the pill's shadow inside it.")]
        [SerializeField] private float _pillHeight = 70f;

        [SerializeField] private float _discSize = 52f;
        [SerializeField] private float _glyphSize = 32f;
        [SerializeField] private float _paddingLeft = 10f;
        [SerializeField] private float _paddingRight = 22f;
        [SerializeField] private float _gap = 10f;
        [SerializeField] private int _captionFontSize = 24;
        [SerializeField] private int _numberFontSize = 38;

        [Header("Art")]
        [Tooltip("The chunky display face for the level number. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("The heavy label face for the small uppercase caption. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _labelFont;

        [Tooltip("White trail silhouette on the purple disc. The disc is plain when unassigned.")]
        [SerializeField] private Sprite _trailSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        private SettingsModel _settingsModel;
        private LevelProgressionModel _levelProgressionModel;
        private PathRunModel _pathRunModel;
        private GameModeSystem _gameModeSystem;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ObjectiveIconContainerView _objectiveIconContainerView;

        private RectTransform _rect;
        private CanvasGroup _group;
        private RectTransform _buttonRect;
        private Image _plateImage;
        private Image _shadowImage;
        private Image _discLipImage;
        private Image _discImage;
        private Image _trailImage;
        private RectTransform _discRect;
        private Text _captionText;
        private RectTransform _captionRect;
        private Text _numberText;
        private RectTransform _numberRect;
        private Canvas _canvas;

        /// <summary>
        /// The pill's own rect — the right-anchored root that the plate, shadow and contents hang off.
        /// <see cref="PathLevelBadgeView"/> parents onto this so the badge follows the pill wherever
        /// the goal row puts it, without being able to see or alter the plate.
        /// </summary>
        internal RectTransform RootRect => (RectTransform)transform;

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            LevelProgressionModel levelProgressionModel,
            PathRunModel pathRunModel,
            GameModeSystem gameModeSystem,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            ObjectiveIconContainerView objectiveIconContainerView)
        {
            _settingsModel = settingsModel;
            _levelProgressionModel = levelProgressionModel;
            _pathRunModel = pathRunModel;
            _gameModeSystem = gameModeSystem;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _objectiveIconContainerView = objectiveIconContainerView;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildButton();
        }

        private void Start()
        {
            if (_settingsModel == null || _levelProgressionModel == null || _pathRunModel == null
                || _gameModeSystem == null || _localizationModel == null || _localizationSystem == null
                || _objectiveIconContainerView == null)
            {
                Debug.LogError(
                    $"{nameof(LevelPathButtonView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            AttachToGoalRow();

            // The pill is built in Awake, before the theme is known; this subscription paints it and
            // repaints it on every later theme switch.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Which level the pill names depends on the mode, the frontier, the active path level and
            // the language the caption is written in; any of the four repaints it.
            _localizationModel.CurrentLocale.Subscribe(_ => RefreshLabel()).AddTo(_disposables);
            _gameModeSystem.CurrentMode.Subscribe(_ => RefreshLabel()).AddTo(_disposables);
            _levelProgressionModel.CurrentLevelNumber.Subscribe(_ => RefreshLabel()).AddTo(_disposables);
            _pathRunModel.ActiveLevelNumber.Subscribe(_ => RefreshLabel()).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True when the given screen point is on the pill. Called by <see cref="BoardInputView"/>.</summary>
        internal bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (_buttonRect == null)
            {
                return false;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(_buttonRect, screenPosition, eventCamera);
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _plateImage.color = theme.CardBackground;
            _shadowImage.color = theme.CardShadow;
            _discImage.color = theme.GetFill(DISC_KIND);
            _discLipImage.color = theme.GetShade(DISC_KIND);
            _trailImage.color = _trailSprite != null ? Color.white : Color.clear;
            _captionText.color = theme.Ink;
            _numberText.color = theme.Ink;
        }

        /// <summary>
        /// Outside Path mode the pill names the frontier — the furthest level unlocked — since that is
        /// where the path panel will land. In a Path run it names the level being played, which may be
        /// an earlier one the player chose to replay, so the two can legitimately differ.
        /// </summary>
        private void RefreshLabel()
        {
            int activeLevel = _pathRunModel.ActiveLevelNumber.Value;
            bool isPathRun = _gameModeSystem.CurrentMode.Value == GameMode.Path
                && activeLevel != PathRunModel.NO_ACTIVE_LEVEL;

            _stringBuilder.Clear();
            _stringBuilder.Append(isPathRun ? activeLevel : _levelProgressionModel.CurrentLevelNumber.Value);
            string number = _stringBuilder.ToString();

            if (isPathRun)
            {
                // "LEVEL 7" as one caption in the label face, with the number left empty: the mockup
                // spells the full word here and the number is part of the phrase.
                _captionText.text = _localizationSystem.Format(LocalizationKeys.HUD_LEVEL_NUMBER, number);
                _numberText.text = string.Empty;
            }
            else
            {
                _captionText.text = _localizationSystem.Translate(LocalizationKeys.HUD_LEVEL_SHORT);
                _numberText.text = number;
            }

            LayOut();
        }

        private void LayOut()
        {
            float captionWidth = _captionText.preferredWidth;
            float numberWidth = _numberText.text.Length > 0 ? _numberText.preferredWidth + (_gap * 0.5f) : 0f;
            float width = _paddingLeft + _discSize + _gap + captionWidth + numberWidth + _paddingRight;
            var size = new Vector2(width, _pillHeight);

            _rect.sizeDelta = size;
            _buttonRect.sizeDelta = size;
            _shadowImage.rectTransform.sizeDelta = size;

            float x = (-width * 0.5f) + _paddingLeft;
            _discRect.anchoredPosition = new Vector2(x + (_discSize * 0.5f), 0f);
            x += _discSize + _gap;
            _captionRect.anchoredPosition = new Vector2(x, 0f);
            x += captionWidth + (_gap * 0.5f);
            _numberRect.anchoredPosition = new Vector2(x, 0f);

            // The row lays its trailing group out right to left from the pill's width, and clips its
            // chips short of it, so a wider number must reach it.
            _objectiveIconContainerView.NotifyTrailingChanged();
        }

        /// <summary>
        /// Moves the pill under the goal row's trailing slot, flush with the row's right edge. Last
        /// sibling on purpose: the row places its trailing children right to left in sibling order, so
        /// the pill is the rightmost and the streak pill, when it visits, sits to its left.
        /// </summary>
        private void AttachToGoalRow()
        {
            _rect.SetParent(_objectiveIconContainerView.TrailingSlot, false);
            _rect.SetAsLastSibling();
            _rect.anchoredPosition = Vector2.zero;
            _objectiveIconContainerView.NotifyTrailingChanged();
        }

        private void BuildButton()
        {
            _rect = (RectTransform)transform;
            _rect.anchorMin = new Vector2(1f, 0.5f);
            _rect.anchorMax = new Vector2(1f, 0.5f);
            _rect.pivot = new Vector2(1f, 0.5f);
            _rect.sizeDelta = new Vector2(_pillHeight * 2f, _pillHeight);
            _rect.anchoredPosition = Vector2.zero;

            // Every size here is in canvas reference units, so the pill owns its own scale rather than
            // inheriting whatever the scene object happened to be created with.
            _rect.localScale = Vector3.one;

            // The goal row fades itself out when nothing is tracked and dims for a 2× window; the pill
            // is not a goal and must stay on screen through both.
            if (!TryGetComponent(out _group))
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _group.ignoreParentGroups = true;

            _buttonRect = HudChrome.BuildPill(
                _rect, "LevelPathButtonPlate", _rect.sizeDelta, Vector2.zero, out _shadowImage, out _plateImage);

            // Children hang off the root rather than the plate so the plate stays the one hit rect.
            _discRect = HudChrome.CreateRect(_rect, "Disc", new Vector2(_discSize, _discSize), Vector2.zero);
            _discLipImage = HudChrome.BuildCircle(_discRect, "Lip", _discSize, new Vector2(0f, -5f));
            _discImage = HudChrome.BuildCircle(_discRect, "Face", _discSize, Vector2.zero);
            _trailImage = HudChrome.BuildGlyph(
                _discRect, "Trail", _trailSprite, new Vector2(_glyphSize, _glyphSize), Vector2.zero);

            _captionText = HudChrome.CreateLabel(
                _rect, "Caption", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, _labelFont);
            _captionRect = (RectTransform)_captionText.transform;

            _numberText = HudChrome.CreateLabel(
                _rect, "Number", _numberFontSize, FontStyle.Normal, TextAnchor.MiddleLeft, Vector2.zero, _displayFont);
            _numberRect = (RectTransform)_numberText.transform;
        }
    }
}
