using System.Text;
using MessagePipe;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The level section of the goal bar, and the tap that opens the level path overlay: the level's
    /// own icon (from <see cref="LevelIdentityCatalog"/>, the same art the path's beads and the
    /// level-start card show) with its number on a pill over the icon's bottom edge (issue #508 — it
    /// used to sit beside the icon and cost the goals their room). Since issue #477 it sits in the
    /// bar's leading slot (<see cref="ObjectiveIconContainerView.LeadingSlot"/>) and draws no plate of
    /// its own — the bar is its plate — and the "LEVEL" word is gone: the icon says "level" on its own.
    /// A level with no authored icon falls back to the purple disc wearing the trail glyph that the
    /// pill used to carry (issue #265).
    /// <para>
    /// Shown in Path mode only (issue #269): Endless and Timed have no level, so there it is hidden and
    /// does not answer the hit test. In a Path run it names the level being played; in the moment
    /// between entering the mode and the first run starting it names the frontier instead.
    /// </para>
    /// <para>
    /// Parented onto the bar in <see cref="Start"/> rather than Awake: the bar builds its slot in Awake
    /// and sibling Awake order is not guaranteed. The bar hides itself outside Path mode and dims for a
    /// 2× window; the section ignores that group so it stays visible and tappable through the window,
    /// and hides through its own group instead.
    /// </para>
    /// <para>
    /// Like <see cref="SettingsButtonView"/> it only knows how to draw itself and whether a screen
    /// point is on it. The tap that opens the panel is routed by <see cref="BoardInputView"/>, which is
    /// the single owner of pointer input.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelPathButtonView : MonoBehaviour
    {
        /// <summary>Which theme kind's fill the fallback disc takes: the fourth kind — every season's purple.</summary>
        private const int DISC_KIND = 4;

        [Header("Layout")]
        [Tooltip("Height of the section's tap area in reference pixels. A touch under the goal row's 80.")]
        [SerializeField] private float _pillHeight = 70f;

        [Tooltip("Side of the level's icon, in reference pixels.")]
        [SerializeField] private float _levelIconSize = 64f;

        [Tooltip("Diameter of the fallback disc shown when a level has no authored icon.")]
        [SerializeField] private float _discSize = 52f;

        [SerializeField] private float _glyphSize = 32f;

        [Tooltip("How far the icon sits above the bar's centre line, so the number badge under it stays " +
            "inside the bar.")]
        [SerializeField] private float _iconLift = 8f;

        [Header("Number badge")]
        [Tooltip("Height of the pill that carries the level number over the icon's bottom edge (issue #508).")]
        [SerializeField] private float _badgeHeight = 34f;

        [Tooltip("Space either side of the number inside the badge. The badge grows sideways with the digits.")]
        [SerializeField] private float _badgePaddingX = 9f;

        [Tooltip("Width of the card-coloured rim that lifts the badge off the icon art.")]
        [SerializeField] private float _badgeRim = 4f;

        [SerializeField] private int _badgeFontSize = 24;

        [Header("Art")]
        [Tooltip("The chunky display face for the level number. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("White trail silhouette on the fallback disc. The disc is plain when unassigned.")]
        [SerializeField] private Sprite _trailSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        private SettingsModel _settingsModel;
        private LevelProgressionModel _levelProgressionModel;
        private PathRunModel _pathRunModel;
        private GameModeSystem _gameModeSystem;
        private LevelIdentityCatalog _levelIdentityCatalog;
        private ObjectiveIconContainerView _objectiveIconContainerView;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;

        private RectTransform _rect;
        private CanvasGroup _group;
        private RectTransform _buttonRect;
        private Image _levelIconImage;
        private Image _discLipImage;
        private Image _discImage;
        private Image _trailImage;
        private RectTransform _discRect;
        private RectTransform _badgeRect;
        private Image _badgeRimImage;
        private Image _badgeFillImage;
        private Text _numberText;
        private Canvas _canvas;
        private ThemeDefinition _currentTheme;

        /// <summary>True while the section is drawn; the hit test answers only then.</summary>
        private bool _isVisible;

        /// <summary>True while the level shown has an authored icon, so the fallback disc is hidden.</summary>
        private bool _hasLevelIcon;

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            LevelProgressionModel levelProgressionModel,
            PathRunModel pathRunModel,
            GameModeSystem gameModeSystem,
            LevelIdentityCatalog levelIdentityCatalog,
            ObjectiveIconContainerView objectiveIconContainerView,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _settingsModel = settingsModel;
            _levelProgressionModel = levelProgressionModel;
            _pathRunModel = pathRunModel;
            _gameModeSystem = gameModeSystem;
            _levelIdentityCatalog = levelIdentityCatalog;
            _objectiveIconContainerView = objectiveIconContainerView;
            _runStartedSubscriber = runStartedSubscriber;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildButton();
        }

        private void Start()
        {
            if (_settingsModel == null || _levelProgressionModel == null || _pathRunModel == null
                || _gameModeSystem == null || _levelIdentityCatalog == null
                || _objectiveIconContainerView == null || _runStartedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(LevelPathButtonView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            AttachToGoalBar();

            // The section is built in Awake, before the theme is known; this subscription paints it and
            // repaints it on every later theme switch.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Which level the section names depends on the mode, the frontier and the active path
            // level; any of the three repaints it.
            _gameModeSystem.CurrentMode.Subscribe(_ => RefreshLabel()).AddTo(_disposables);
            _levelProgressionModel.CurrentLevelNumber.Subscribe(_ => RefreshLabel()).AddTo(_disposables);
            _pathRunModel.ActiveLevelNumber.Subscribe(_ => RefreshLabel()).AddTo(_disposables);

            // Whether the section shows at all follows the mode; a run starting is re-checked too,
            // since a mode switch restarts the run and this is the one edge that is sure to land after it.
            _gameModeSystem.CurrentMode.Subscribe(_ => RefreshVisibility()).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(_ => RefreshVisibility()).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True when the given screen point is on the section. Called by <see cref="BoardInputView"/>.</summary>
        internal bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (_buttonRect == null || !_isVisible)
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

            _currentTheme = theme;
            _badgeRimImage.color = theme.CardBackground;
            _badgeFillImage.color = theme.Ink;
            _numberText.color = Color.white;
            PaintIcon();
        }

        /// <summary>
        /// Path mode only (issue #269). Through the section's own CanvasGroup, which ignores the bar's:
        /// the bar hides itself for the same reason, and the section must not depend on that.
        /// </summary>
        private void RefreshVisibility()
        {
            _isVisible = _gameModeSystem.CurrentMode.Value == GameMode.Path;
            _group.alpha = _isVisible ? 1f : 0f;
            _group.interactable = _isVisible;
            _group.blocksRaycasts = _isVisible;

            // The bar packs its leading group from the children that are showing, so it must re-pack
            // whenever the section comes or goes — otherwise the chips would run under it.
            _objectiveIconContainerView.NotifySlotsChanged();
        }

        /// <summary>
        /// Outside a Path run the section names the frontier — the furthest level unlocked — since
        /// that is where the path panel will land. In a Path run it names the level being played,
        /// which may be an earlier one the player chose to replay, so the two can legitimately differ.
        /// </summary>
        private void RefreshLabel()
        {
            int activeLevel = _pathRunModel.ActiveLevelNumber.Value;
            bool isPathRun = _gameModeSystem.CurrentMode.Value == GameMode.Path
                && activeLevel != PathRunModel.NO_ACTIVE_LEVEL;
            int levelNumber = isPathRun ? activeLevel : _levelProgressionModel.CurrentLevelNumber.Value;

            _stringBuilder.Clear();
            _stringBuilder.Append(levelNumber);
            _numberText.text = _stringBuilder.ToString();

            LevelIdentityConfig identity = _levelIdentityCatalog.Find(levelNumber);
            _levelIconImage.sprite = identity != null ? identity.Icon : null;
            _hasLevelIcon = _levelIconImage.sprite != null;
            PaintIcon();

            LayOut();
        }

        /// <summary>Either the level's own icon, drawn as authored, or the themed fallback disc —
        /// never both. An Image with no sprite would draw a solid square, hence the clear.</summary>
        private void PaintIcon()
        {
            _levelIconImage.color = _hasLevelIcon ? Color.white : Color.clear;

            bool showDisc = !_hasLevelIcon && _currentTheme != null;
            _discImage.color = showDisc ? _currentTheme.GetFill(DISC_KIND) : Color.clear;
            _discLipImage.color = showDisc ? _currentTheme.GetShade(DISC_KIND) : Color.clear;
            _trailImage.color = showDisc && _trailSprite != null ? Color.white : Color.clear;
        }

        private void LayOut()
        {
            // The number rides on the icon rather than beside it (issue #508), so the section is only
            // as wide as the icon and the goals get the room back.
            float iconWidth = Mathf.Max(_levelIconSize, _discSize);
            Vector2 size = new Vector2(iconWidth, _pillHeight);

            _rect.sizeDelta = size;
            _buttonRect.sizeDelta = size;

            // Children are centred on the root, whose pivot is its left edge; the rects' own centres
            // therefore land at the section's middle.
            Vector2 iconCentre = new Vector2(0f, _iconLift);
            _levelIconImage.rectTransform.anchoredPosition = iconCentre;
            _discRect.anchoredPosition = iconCentre;

            // Centred on the icon's bottom edge, half over the art and half below it, and as wide as
            // the digits need — never narrower than it is tall, so a single digit still reads as a pill.
            float badgeWidth = Mathf.Max(_badgeHeight, _numberText.preferredWidth + (_badgePaddingX * 2f) + (_badgeRim * 2f));
            _badgeRect.anchoredPosition = new Vector2(0f, _iconLift - (iconWidth * 0.5f));
            _badgeRect.sizeDelta = new Vector2(badgeWidth, _badgeHeight);
            _badgeRimImage.rectTransform.sizeDelta = new Vector2(badgeWidth, _badgeHeight);
            _badgeFillImage.rectTransform.sizeDelta =
                new Vector2(badgeWidth - (_badgeRim * 2f), _badgeHeight - (_badgeRim * 2f));

            // The bar lays its leading group out from the section's width, and starts its chips after
            // it, so a change of width must reach it.
            _objectiveIconContainerView.NotifySlotsChanged();
        }

        /// <summary>
        /// Moves the section under the bar's leading slot, flush with the bar's left edge. First sibling
        /// on purpose: the bar places its leading children left to right in sibling order.
        /// </summary>
        private void AttachToGoalBar()
        {
            _rect.SetParent(_objectiveIconContainerView.LeadingSlot, false);
            _rect.SetAsFirstSibling();
            _rect.anchoredPosition = Vector2.zero;
            _objectiveIconContainerView.NotifySlotsChanged();
        }

        private void BuildButton()
        {
            _rect = (RectTransform)transform;
            _rect.anchorMin = new Vector2(0f, 0.5f);
            _rect.anchorMax = new Vector2(0f, 0.5f);
            _rect.pivot = new Vector2(0f, 0.5f);
            _rect.sizeDelta = new Vector2(_pillHeight * 2f, _pillHeight);
            _rect.anchoredPosition = Vector2.zero;

            // Every size here is in canvas reference units, so the section owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            _rect.localScale = Vector3.one;

            // The bar fades itself out for a 2× window; the level is not a goal and must stay on screen
            // (and tappable) through it.
            if (!TryGetComponent(out _group))
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _group.ignoreParentGroups = true;

            // A bare rect drawing nothing: the bar is the plate, and this is only the tap target.
            _buttonRect = HudChrome.CreateRect(_rect, "LevelPathButtonHitArea", _rect.sizeDelta, Vector2.zero);

            // Children hang off the root rather than the hit rect, as before.
            _discRect = HudChrome.CreateRect(_rect, "Disc", new Vector2(_discSize, _discSize), Vector2.zero);
            _discLipImage = HudChrome.BuildCircle(_discRect, "Lip", _discSize, new Vector2(0f, -5f));
            _discImage = HudChrome.BuildCircle(_discRect, "Face", _discSize, Vector2.zero);
            _trailImage = HudChrome.BuildGlyph(
                _discRect, "Trail", _trailSprite, new Vector2(_glyphSize, _glyphSize), Vector2.zero);

            _levelIconImage = HudChrome.BuildGlyph(
                _rect, "LevelIcon", null, new Vector2(_levelIconSize, _levelIconSize), Vector2.zero);

            // Built after the icon so it draws over the art's bottom edge. Its root sits at the
            // section's centre line (the root rect's pivot is its left edge, so centre-anchored children
            // are laid out from the middle); LayOut places and sizes it.
            _badgeRect = HudChrome.CreateRect(_rect, "NumberBadge", new Vector2(_badgeHeight, _badgeHeight), Vector2.zero);
            _badgeRimImage = HudChrome.BuildRounded(
                _badgeRect, "Rim", new Vector2(_badgeHeight, _badgeHeight), Vector2.zero, _badgeHeight * 0.5f);
            float fillHeight = _badgeHeight - (_badgeRim * 2f);
            _badgeFillImage = HudChrome.BuildRounded(
                _badgeRect, "Fill", new Vector2(fillHeight, fillHeight), Vector2.zero, fillHeight * 0.5f);

            _numberText = HudChrome.CreateLabel(
                _badgeRect, "Number", _badgeFontSize, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, _displayFont);
        }
    }
}
