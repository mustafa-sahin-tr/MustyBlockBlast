using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Persistent HUD icon in the top-right corner that opens the settings panel. Draws a rounded
    /// plate with a cog glyph — a ring plus eight teeth — assembled from the shared circle and
    /// rounded-square sprites, so it needs no art asset and batches with every other UI Image.
    /// <para>
    /// The ring's hole is faked the same way the rest of the UI fakes cut-outs: a smaller circle is
    /// drawn on top in the plate's own opaque colour. That only works because the plate underneath is
    /// always painted with a solid colour first.
    /// </para>
    /// <para>
    /// It only knows how to draw itself and whether a screen point is on it. The tap that opens the
    /// panel is routed by <see cref="BoardInputView"/>, which is the single owner of pointer input.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsButtonView : MonoBehaviour
    {
        private const int TOOTH_COUNT = 8;

        /// <summary>Ring plus one Image per tooth — every part of the glyph that is inked.</summary>
        private const int INK_PART_COUNT = TOOTH_COUNT + 1;

        [Header("Layout")]
        [Tooltip("Offset from the top-right corner of the canvas, in reference pixels.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(-60f, -60f);
        [SerializeField] private float _buttonSize = 112f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly Image[] _inkImages = new Image[INK_PART_COUNT];

        private SettingsModel _settingsModel;
        private RectTransform _buttonRect;
        private Image _plateImage;
        private Image _shadowImage;
        private Image _gearHoleImage;
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

            // The icon is built in Awake, before the theme is known; this subscription paints it and
            // repaints it on every later theme switch.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True when the given screen point is on the icon. Called by <see cref="BoardInputView"/>.</summary>
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

            for (int inkIndex = 0; inkIndex < _inkImages.Length; inkIndex++)
            {
                _inkImages[inkIndex].color = theme.Ink;
            }

            // The hole is a fake cut-out, so it has to track the plate colour exactly.
            _gearHoleImage.color = theme.CardBackground;
        }

        private void BuildButton()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(_buttonSize, _buttonSize);
            rect.anchoredPosition = _cornerOffset;

            var shadowObject = new GameObject("SettingsButtonShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(rect, false);
            CentreFill(shadowRect, new Vector2(_buttonSize + 10f, _buttonSize + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImage = shadowObject.GetComponent<Image>();
            ConfigurePlate(_shadowImage);

            var plateObject = new GameObject("SettingsButtonPlate", typeof(RectTransform), typeof(Image));
            _buttonRect = (RectTransform)plateObject.transform;
            _buttonRect.SetParent(rect, false);
            CentreFill(_buttonRect, new Vector2(_buttonSize, _buttonSize));
            _plateImage = plateObject.GetComponent<Image>();
            ConfigurePlate(_plateImage);

            BuildGear();
        }

        // Teeth first, then the ring on top of their inner ends, then the hole punched through both:
        // sibling order is the only thing keeping the glyph readable, since nothing here is masked.
        private void BuildGear()
        {
            float toothWidth = _buttonSize * 0.15f;
            float toothLength = _buttonSize * 0.20f;
            float toothDistance = _buttonSize * 0.27f;
            float ringDiameter = _buttonSize * 0.52f;
            float holeDiameter = _buttonSize * 0.24f;

            for (int toothIndex = 0; toothIndex < TOOTH_COUNT; toothIndex++)
            {
                float angle = (360f / TOOTH_COUNT) * toothIndex;
                var toothObject = new GameObject($"Tooth_{toothIndex}", typeof(RectTransform), typeof(Image));
                var toothRect = (RectTransform)toothObject.transform;
                toothRect.SetParent(_buttonRect, false);
                CentreFill(toothRect, new Vector2(toothWidth, toothLength));

                // The tooth points away from the centre, so it is both rotated by the angle and
                // pushed out along that same rotated axis.
                toothRect.localRotation = Quaternion.Euler(0f, 0f, angle);
                toothRect.anchoredPosition =
                    (Vector2)(Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, toothDistance, 0f));

                var toothImage = toothObject.GetComponent<Image>();
                ConfigurePlate(toothImage);
                _inkImages[toothIndex] = toothImage;
            }

            var ringObject = new GameObject("GearRing", typeof(RectTransform), typeof(Image));
            var ringRect = (RectTransform)ringObject.transform;
            ringRect.SetParent(_buttonRect, false);
            CentreFill(ringRect, new Vector2(ringDiameter, ringDiameter));
            var ringImage = ringObject.GetComponent<Image>();
            ConfigureCircle(ringImage);
            _inkImages[TOOTH_COUNT] = ringImage;

            var holeObject = new GameObject("GearHole", typeof(RectTransform), typeof(Image));
            var holeRect = (RectTransform)holeObject.transform;
            holeRect.SetParent(_buttonRect, false);
            CentreFill(holeRect, new Vector2(holeDiameter, holeDiameter));
            _gearHoleImage = holeObject.GetComponent<Image>();
            ConfigureCircle(_gearHoleImage);
        }

        private static void CentreFill(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Every Image here shares the one rounded-square sprite, so the whole icon batches into the
        // surrounding UI instead of adding draw calls of its own.
        private static void ConfigurePlate(Image image)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = Color.clear;
            image.raycastTarget = false;
        }

        // The circle sprite has no border, so it must never be sliced.
        private static void ConfigureCircle(Image image)
        {
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            image.raycastTarget = false;
        }
    }
}
