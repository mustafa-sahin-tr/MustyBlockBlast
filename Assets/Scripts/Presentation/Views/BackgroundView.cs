using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Full-screen background behind everything else. Normally the current theme's vertical gradient;
    /// while a Path level is played with "Level Backgrounds" on, that level group's image instead
    /// (issue #523). Both go on the same <see cref="Image"/> — a sprite swap, never a second layer — so
    /// the level image costs no extra draw or overdraw. The image is cover-filled: an
    /// <see cref="AspectRatioFitter"/> envelopes the screen and crops whatever overflows.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class BackgroundView : MonoBehaviour
    {
        /// <summary>Envelope ratio while the gradient shows. Any ratio covers the screen; this one
        /// matches the level art so a swap does not change the rect.</summary>
        private const float DEFAULT_ASPECT = 720f / 1280f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private SettingsModel _settingsModel;
        private GameModeModel _gameModeModel;
        private PathRunModel _pathRunModel;
        private LevelBackgroundCatalog _levelBackgroundCatalog;
        private Image _image;
        private AspectRatioFitter _fitter;

        private Sprite _gradientSprite;
        private ThemeDefinition _gradientTheme;

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            GameModeModel gameModeModel,
            PathRunModel pathRunModel,
            LevelBackgroundCatalog levelBackgroundCatalog)
        {
            _settingsModel = settingsModel;
            _gameModeModel = gameModeModel;
            _pathRunModel = pathRunModel;
            _levelBackgroundCatalog = levelBackgroundCatalog;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            transform.SetAsFirstSibling();

            _image = GetComponent<Image>();
            _image.type = Image.Type.Simple;
            _image.color = Color.white;
            _image.raycastTarget = false;

            if (!TryGetComponent(out _fitter))
            {
                _fitter = gameObject.AddComponent<AspectRatioFitter>();
            }

            _fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _fitter.aspectRatio = DEFAULT_ASPECT;
        }

        private void Start()
        {
            if (_settingsModel == null)
            {
                Debug.LogError(
                    $"{nameof(BackgroundView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Each fires once on subscribe; the first calls are harmless repeats of the same paint.
            _settingsModel.CurrentTheme.Subscribe(_ => Refresh()).AddTo(_disposables);
            _settingsModel.LevelBackgroundsEnabled.Subscribe(_ => Refresh()).AddTo(_disposables);
            _gameModeModel.CurrentMode.Subscribe(_ => Refresh()).AddTo(_disposables);
            _pathRunModel.ActiveLevelNumber.Subscribe(_ => Refresh()).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void Refresh()
        {
            Sprite levelBackground = _levelBackgroundCatalog.Resolve(
                _settingsModel.LevelBackgroundsEnabled.Value,
                _gameModeModel.CurrentMode.Value,
                _pathRunModel.ActiveLevelNumber.Value);

            if (levelBackground != null)
            {
                _image.sprite = levelBackground;
                Rect spriteRect = levelBackground.rect;
                _fitter.aspectRatio = spriteRect.height > 0f ? spriteRect.width / spriteRect.height : DEFAULT_ASPECT;
                return;
            }

            ThemeDefinition theme = _settingsModel.CurrentTheme.Value;
            if (theme == null)
            {
                return;
            }

            // Rebuilt only when the theme actually changed, not on every level or setting change.
            if (_gradientSprite == null || _gradientTheme != theme)
            {
                _gradientSprite = UiSpriteFactory.CreateVerticalGradient(theme.BackgroundBottom, theme.BackgroundTop);
                _gradientTheme = theme;
            }

            _image.sprite = _gradientSprite;
            _fitter.aspectRatio = DEFAULT_ASPECT;
        }
    }
}
