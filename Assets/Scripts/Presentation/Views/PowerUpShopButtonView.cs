using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Persistent HUD icon that opens the power-up shop. Sixth and last in the right-edge icon column,
    /// under <see cref="LeaderboardButtonView"/>: the column is the established home for overlay entry
    /// points, and stacking keeps all six clear of the score at the top centre and the countdown below
    /// it.
    /// <para>
    /// Draws a coin — a disc with a smaller disc punched into it — for the same reason the medal above
    /// it is drawn from the shared circle and rounded-square sprites: no art asset, and it batches with
    /// every other UI Image.
    /// </para>
    /// <para>
    /// Like the five icons above it, it only knows how to draw itself and whether a screen point is on
    /// it. The tap that opens the shop is routed by <see cref="BoardInputView"/>, which is the single
    /// owner of pointer input.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerUpShopButtonView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Offset from the top-right corner of the canvas, in reference pixels. Stacked under the leaderboard icon.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(-60f, -740f);
        [SerializeField] private float _buttonSize = 112f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private SettingsModel _settingsModel;
        private RectTransform _buttonRect;
        private Image _plateImage;
        private Image _shadowImage;
        private Image _coinImage;

        /// <summary>The coin's centre, painted in the plate colour so the glyph reads as a ring rather
        /// than a dot. Repainted with the plate, not with the ink.</summary>
        private Image _coinCoreImage;

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
                    $"{nameof(PowerUpShopButtonView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
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

            // The accent, not the ink: a coin is the one icon in the column that stands for a currency,
            // and the accent is what the coin balance is already drawn in elsewhere.
            _coinImage.color = theme.Accent;
            _coinCoreImage.color = theme.CardBackground;
        }

        private void BuildButton()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(_buttonSize, _buttonSize);
            rect.anchoredPosition = _cornerOffset;

            // Every size here is in canvas reference units, so the icon owns its own scale rather than
            // inheriting whatever the scene object happened to be created with.
            rect.localScale = Vector3.one;

            var shadowObject = new GameObject("PowerUpShopButtonShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(rect, false);
            Centre(shadowRect, new Vector2(_buttonSize + 10f, _buttonSize + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImage = shadowObject.GetComponent<Image>();
            ConfigurePlate(_shadowImage);

            var plateObject = new GameObject("PowerUpShopButtonPlate", typeof(RectTransform), typeof(Image));
            _buttonRect = (RectTransform)plateObject.transform;
            _buttonRect.SetParent(rect, false);
            Centre(_buttonRect, new Vector2(_buttonSize, _buttonSize));
            _plateImage = plateObject.GetComponent<Image>();
            ConfigurePlate(_plateImage);

            BuildCoin();
        }

        // The disc first, then its core on top: sibling order is the only thing keeping the ring
        // readable, since nothing here is masked.
        private void BuildCoin()
        {
            float diameter = _buttonSize * 0.58f;

            var discObject = new GameObject("CoinDisc", typeof(RectTransform), typeof(Image));
            var discRect = (RectTransform)discObject.transform;
            discRect.SetParent(_buttonRect, false);
            Centre(discRect, new Vector2(diameter, diameter));
            _coinImage = discObject.GetComponent<Image>();
            ConfigureCircle(_coinImage);

            var coreObject = new GameObject("CoinCore", typeof(RectTransform), typeof(Image));
            var coreRect = (RectTransform)coreObject.transform;
            coreRect.SetParent(_buttonRect, false);
            Centre(coreRect, new Vector2(diameter * 0.42f, diameter * 0.42f));
            _coinCoreImage = coreObject.GetComponent<Image>();
            ConfigureCircle(_coinCoreImage);
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Every rounded Image here shares the one rounded-square sprite, so the whole icon batches into
        // the surrounding UI instead of adding draw calls of its own.
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
