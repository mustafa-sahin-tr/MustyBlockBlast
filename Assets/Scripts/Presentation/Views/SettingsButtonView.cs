using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Persistent HUD button in the top-right corner that opens the hub (issue #265): a card-coloured
    /// rounded plate over a dropped shadow with the hub's gear silhouette on it, tinted with the
    /// season's accent. The one persistent corner button — the shop, leaderboard, profile and badges
    /// that used to sit under it are tabs inside the hub now.
    /// <para>
    /// It only knows how to draw itself and whether a screen point is on it. The tap that opens the
    /// hub is routed by <see cref="BoardInputView"/>, which is the single owner of pointer input.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsButtonView : MonoBehaviour
    {
        /// <summary>Corner radius of the plate as a fraction of its side: the mockup's 15px on a 44px button.</summary>
        private const float PLATE_CORNER_FRACTION = 0.34f;

        [Header("Layout")]
        [Tooltip("Offset of the plate's top-right corner from the safe area's top-right corner, in reference pixels.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(-24f, -32f);

        [Tooltip("Side of the square plate, in reference pixels. The tallest thing on the top bar.")]
        [SerializeField] private float _buttonSize = 108f;

        [Tooltip("Side of the gear glyph inside the plate.")]
        [SerializeField] private float _iconSize = 54f;

        [Header("Art")]
        [Tooltip("White gear silhouette, shared with the hub's settings tab. Tinted with the accent. " +
            "Falls back to a plain accent disc when unassigned.")]
        [SerializeField] private Sprite _gearSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private SettingsModel _settingsModel;
        private RectTransform _buttonRect;
        private Image _plateImage;
        private Image _shadowImage;
        private Image _gearImage;
        private Canvas _canvas;

        [Inject]
        public void Construct(SettingsModel settingsModel)
        {
            _settingsModel = settingsModel;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildButton();
        }

        private void Start()
        {
            if (_settingsModel == null)
            {
                Debug.LogError(
                    $"{nameof(SettingsButtonView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // The button is built in Awake, before the theme is known; this subscription paints it and
            // repaints it on every later theme switch.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True when the given screen point is on the button. Called by <see cref="BoardInputView"/>.</summary>
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
            _gearImage.color = theme.Accent;
        }

        private void BuildButton()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(_buttonSize, _buttonSize);
            rect.anchoredPosition = _cornerOffset;

            // Every size here is in canvas reference units, so the button owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            rect.localScale = Vector3.one;

            _buttonRect = HudChrome.BuildPlate(
                rect, "SettingsButtonPlate", rect.sizeDelta, Vector2.zero, _buttonSize * PLATE_CORNER_FRACTION,
                HudChrome.PLATE_SHADOW_DROP, out _shadowImage, out _plateImage);

            _gearImage = HudChrome.BuildGlyph(
                _buttonRect, "Gear", _gearSprite != null ? _gearSprite : UiSpriteFactory.Circle,
                new Vector2(_iconSize, _iconSize), Vector2.zero);
        }
    }
}
