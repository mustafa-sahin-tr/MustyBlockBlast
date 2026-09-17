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
    /// Accent-coloured circular badge showing the active path level number, worn on the top-right
    /// corner of <see cref="LevelPathButtonView"/> the way an app icon wears a notification count.
    /// Hidden outside Path mode and while no level is active.
    /// <para>
    /// Parented onto the icon's <see cref="LevelPathButtonView.RootRect"/> in <see cref="Start"/>
    /// rather than in Awake: both views build their hierarchy in Awake and sibling Awake order is not
    /// guaranteed, so the icon's rect is only safe to attach to once every Awake has run. The badge is
    /// a sibling of the icon's plate, not a child, so it neither widens nor blocks the plate's tap
    /// target — it is non-interactive throughout.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PathLevelBadgeView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Badge centre relative to the top-right corner of the level-path icon, in reference " +
            "pixels. Positive x and y push it outward past the corner, notification-badge style, so " +
            "it covers as little of the route glyph as possible.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(14f, 14f);
        [SerializeField] private float _badgeDiameter = 68f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private PathRunModel _pathRunModel;
        private GameModeSystem _gameModeSystem;
        private SettingsModel _settingsModel;
        private LevelPathButtonView _levelPathButtonView;

        private CanvasGroup _badgeGroup;
        private Image _badgeCircleImage;
        private Text _badgeText;

        [Inject]
        public void Construct(
            PathRunModel pathRunModel,
            GameModeSystem gameModeSystem,
            SettingsModel settingsModel,
            LevelPathButtonView levelPathButtonView)
        {
            _pathRunModel = pathRunModel;
            _gameModeSystem = gameModeSystem;
            _settingsModel = settingsModel;
            _levelPathButtonView = levelPathButtonView;
        }

        private void Awake() => BuildBadge();

        private void Start()
        {
            if (_pathRunModel == null || _gameModeSystem == null || _settingsModel == null
                || _levelPathButtonView == null)
            {
                Debug.LogError(
                    $"{nameof(PathLevelBadgeView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            AttachToLevelPathIcon();

            // The badge is built in Awake, before the theme is known; this subscription paints it and
            // repaints it on every later theme switch.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Either the mode or the active level can change independently (e.g. leaving Path mode
            // keeps the last ActiveLevelNumber around); both must repaint the badge.
            _gameModeSystem.CurrentMode.Subscribe(_ => RefreshBadge()).AddTo(_disposables);
            _pathRunModel.ActiveLevelNumber.Subscribe(_ => RefreshBadge()).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _badgeCircleImage.color = theme.Accent;
        }

        /// <summary>
        /// Shows the badge only for a Path-mode run that has actually started a level — closing the
        /// path panel without tapping a node never touches <see cref="PathRunModel.ActiveLevelNumber"/>,
        /// so the badge keeps reading whatever level the run started on.
        /// </summary>
        private void RefreshBadge()
        {
            int activeLevelNumber = _pathRunModel.ActiveLevelNumber.Value;
            bool isVisible = _gameModeSystem.CurrentMode.Value == GameMode.Path
                && activeLevelNumber != PathRunModel.NO_ACTIVE_LEVEL;

            _badgeGroup.alpha = isVisible ? 1f : 0f;

            if (isVisible)
            {
                _badgeText.text = activeLevelNumber.ToString();
            }
        }

        /// <summary>
        /// Moves the badge under the icon's root and pins it to that rect's top-right corner. Last
        /// sibling on purpose: the icon's shadow and plate are earlier siblings, and sibling order is
        /// the only thing keeping the badge drawn over them, since nothing here is masked.
        /// </summary>
        private void AttachToLevelPathIcon()
        {
            var rect = (RectTransform)transform;
            rect.SetParent(_levelPathButtonView.RootRect, false);
            rect.SetAsLastSibling();

            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = _cornerOffset;
        }

        /// <summary>
        /// Built hidden and non-interactive — visibility is driven entirely by <see cref="RefreshBadge"/>
        /// once the reactive subscriptions are live. Only the badge's own size and children are laid out
        /// here; where it sits is decided by <see cref="AttachToLevelPathIcon"/> once its host exists.
        /// </summary>
        private void BuildBadge()
        {
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(_badgeDiameter, _badgeDiameter);

            // Every size here is in canvas reference units, so the badge owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            rect.localScale = Vector3.one;

            _badgeGroup = gameObject.GetComponent<CanvasGroup>();
            if (_badgeGroup == null)
            {
                _badgeGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _badgeGroup.alpha = 0f;
            _badgeGroup.interactable = false;
            _badgeGroup.blocksRaycasts = false;

            var circleObject = new GameObject("PathLevelBadgeCircle", typeof(RectTransform), typeof(Image));
            var circleRect = (RectTransform)circleObject.transform;
            circleRect.SetParent(rect, false);
            circleRect.anchorMin = new Vector2(0.5f, 0.5f);
            circleRect.anchorMax = new Vector2(0.5f, 0.5f);
            circleRect.pivot = new Vector2(0.5f, 0.5f);
            circleRect.sizeDelta = new Vector2(_badgeDiameter, _badgeDiameter);
            circleRect.anchoredPosition = Vector2.zero;

            // The circle sprite has no border, so it must never be sliced.
            _badgeCircleImage = circleObject.GetComponent<Image>();
            _badgeCircleImage.sprite = UiSpriteFactory.Circle;
            _badgeCircleImage.type = Image.Type.Simple;
            _badgeCircleImage.color = Color.clear;
            _badgeCircleImage.raycastTarget = false;

            _badgeText = UiTextFactory.Create(
                rect, "PathLevelBadgeText", Mathf.RoundToInt(_badgeDiameter * 0.72f), FontStyle.Bold, Color.white);
        }
    }
}
