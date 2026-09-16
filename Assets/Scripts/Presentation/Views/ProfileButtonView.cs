using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Persistent HUD icon that opens the profile overlay. Fourth in the right-edge icon column, under
    /// <see cref="SettingsButtonView"/>, <see cref="LevelPathButtonView"/> and
    /// <see cref="BadgesButtonView"/>, at the same 136-unit pitch the three above it use.
    /// <para>
    /// Draws the usual avatar glyph — a head disc over a shoulders bar — from the shared circle and
    /// rounded-square sprites, so like its neighbours it needs no art asset and batches with every
    /// other UI Image.
    /// </para>
    /// <para>
    /// Knows only how to draw itself and whether a screen point is on it. The tap that opens the panel
    /// is routed by <see cref="BoardInputView"/>, the single owner of pointer input.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProfileButtonView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Offset from the top-right corner of the canvas, in reference pixels. Stacked under the badges icon.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(-60f, -468f);
        [SerializeField] private float _buttonSize = 112f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Repaint bucket: every inked part of the glyph — the head and the shoulders.</summary>
        private readonly List<Image> _inkImages = new List<Image>(2);

        private SettingsModel _settingsModel;
        private RectTransform _buttonRect;
        private Image _plateImage;
        private Image _shadowImage;
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
                    $"{nameof(ProfileButtonView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Built in Awake, before the theme is known; this subscription paints it and repaints it on
            // every later theme switch.
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

            for (int inkIndex = 0; inkIndex < _inkImages.Count; inkIndex++)
            {
                _inkImages[inkIndex].color = theme.Ink;
            }
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

            var shadowObject = new GameObject("ProfileButtonShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(rect, false);
            Centre(shadowRect, new Vector2(_buttonSize + 10f, _buttonSize + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImage = shadowObject.GetComponent<Image>();
            ConfigurePlate(_shadowImage);

            var plateObject = new GameObject("ProfileButtonPlate", typeof(RectTransform), typeof(Image));
            _buttonRect = (RectTransform)plateObject.transform;
            _buttonRect.SetParent(rect, false);
            Centre(_buttonRect, new Vector2(_buttonSize, _buttonSize));
            _plateImage = plateObject.GetComponent<Image>();
            ConfigurePlate(_plateImage);

            BuildAvatarGlyph();
        }

        /// <summary>
        /// Head over shoulders. The shoulders are a heavily rounded bar whose bottom half is pushed past
        /// the plate's lower edge, which is what makes it read as a torso rather than as a pill.
        /// </summary>
        private void BuildAvatarGlyph()
        {
            float headDiameter = _buttonSize * 0.34f;
            float shouldersWidth = _buttonSize * 0.60f;
            float shouldersHeight = _buttonSize * 0.42f;

            var headObject = new GameObject("AvatarHead", typeof(RectTransform), typeof(Image));
            var headRect = (RectTransform)headObject.transform;
            headRect.SetParent(_buttonRect, false);
            Centre(headRect, new Vector2(headDiameter, headDiameter));
            headRect.anchoredPosition = new Vector2(0f, _buttonSize * 0.18f);

            var headImage = headObject.GetComponent<Image>();
            ConfigureCircle(headImage);
            _inkImages.Add(headImage);

            var shouldersObject = new GameObject("AvatarShoulders", typeof(RectTransform), typeof(Image));
            var shouldersRect = (RectTransform)shouldersObject.transform;
            shouldersRect.SetParent(_buttonRect, false);
            Centre(shouldersRect, new Vector2(shouldersWidth, shouldersHeight));
            shouldersRect.anchoredPosition = new Vector2(0f, -_buttonSize * 0.20f);

            var shouldersImage = shouldersObject.GetComponent<Image>();
            ConfigurePlate(shouldersImage);
            _inkImages.Add(shouldersImage);
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
