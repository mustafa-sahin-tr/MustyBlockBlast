using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Full-screen vertical gradient behind everything else. Reactive: the gradient comes from the
    /// currently selected theme and is rebuilt whenever the player switches themes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class BackgroundView : MonoBehaviour
    {
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private SettingsModel _settingsModel;
        private Image _image;

        [Inject]
        public void Construct(SettingsModel settingsModel)
        {
            _settingsModel = settingsModel;
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
        }

        private void Start()
        {
            if (_settingsModel == null)
            {
                Debug.LogError(
                    $"{nameof(BackgroundView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _image.sprite = UiSpriteFactory.CreateVerticalGradient(theme.BackgroundBottom, theme.BackgroundTop);
        }
    }
}
