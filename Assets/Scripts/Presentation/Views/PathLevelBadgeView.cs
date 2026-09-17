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
    /// Accent-coloured circular badge showing the active path level number, pinned to the top-left
    /// corner group. Hidden outside Path mode and while no level is active.
    /// <para>
    /// Sits where <see cref="HoldSlotView"/> used to live, top-left under <see cref="ScoreView"/>'s
    /// "BestValue" label — free once the Hold slot moved to sit beside the piece tray instead. Reusing
    /// the spot keeps the corner group's footprint unchanged rather than adding a second occupied-and-
    /// vacated layout to reason about.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PathLevelBadgeView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Offset from the top-left corner of the safe area, in reference pixels.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(26f, -246f);
        [SerializeField] private float _badgeDiameter = 68f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private PathRunModel _pathRunModel;
        private GameModeSystem _gameModeSystem;
        private SettingsModel _settingsModel;

        private CanvasGroup _badgeGroup;
        private Image _badgeCircleImage;
        private Text _badgeText;

        [Inject]
        public void Construct(PathRunModel pathRunModel, GameModeSystem gameModeSystem, SettingsModel settingsModel)
        {
            _pathRunModel = pathRunModel;
            _gameModeSystem = gameModeSystem;
            _settingsModel = settingsModel;
        }

        private void Awake() => BuildBadge();

        private void Start()
        {
            if (_pathRunModel == null || _gameModeSystem == null || _settingsModel == null)
            {
                Debug.LogError(
                    $"{nameof(PathLevelBadgeView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

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
        /// Built hidden and non-interactive — visibility is driven entirely by <see cref="RefreshBadge"/>
        /// once the reactive subscriptions are live.
        /// </summary>
        private void BuildBadge()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(_badgeDiameter, _badgeDiameter);
            rect.anchoredPosition = _anchoredPosition;

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
