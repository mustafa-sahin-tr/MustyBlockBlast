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
    /// Persistent HUD icon that opens the badges overlay. Third in the right-edge icon column, under
    /// <see cref="SettingsButtonView"/> and <see cref="LevelPathButtonView"/>: the column is the
    /// established home for overlay entry points, and stacking keeps all three clear of the score at
    /// the top centre, the best score in the top-left corner and the countdown below them.
    /// <para>
    /// Draws a medal — a disc with a ring punched into it, on two crossed ribbons — from the shared
    /// circle and rounded-square sprites, so it needs no art asset and batches with every other UI
    /// Image.
    /// </para>
    /// <para>
    /// Like the two icons above it, it only knows how to draw itself and whether a screen point is on
    /// it. The tap that opens the panel is routed by <see cref="BoardInputView"/>, which is the single
    /// owner of pointer input.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BadgesButtonView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Offset from the top-right corner of the canvas, in reference pixels. Stacked under the level path icon.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(-60f, -332f);
        [SerializeField] private float _buttonSize = 112f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Repaint bucket: every inked part of the glyph — the two ribbons and the medal disc.</summary>
        private readonly List<Image> _inkImages = new List<Image>(4);

        private SettingsModel _settingsModel;
        private RectTransform _buttonRect;
        private Image _plateImage;
        private Image _shadowImage;

        /// <summary>The disc's centre, painted in the plate colour so the medal reads as a ring rather
        /// than a dot. Repainted with the plate, not with the ink.</summary>
        private Image _medalCoreImage;

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
                    $"{nameof(BadgesButtonView)} was not injected. Is it registered in the LifetimeScope?", this);
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
            _medalCoreImage.color = theme.CardBackground;

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

            var shadowObject = new GameObject("BadgesButtonShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(rect, false);
            Centre(shadowRect, new Vector2(_buttonSize + 10f, _buttonSize + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImage = shadowObject.GetComponent<Image>();
            ConfigurePlate(_shadowImage);

            var plateObject = new GameObject("BadgesButtonPlate", typeof(RectTransform), typeof(Image));
            _buttonRect = (RectTransform)plateObject.transform;
            _buttonRect.SetParent(rect, false);
            Centre(_buttonRect, new Vector2(_buttonSize, _buttonSize));
            _plateImage = plateObject.GetComponent<Image>();
            ConfigurePlate(_plateImage);

            BuildMedal();
        }

        // Ribbons first, then the disc on top of their ends, then the disc's core on top of that:
        // sibling order is the only thing keeping the glyph readable, since nothing here is masked.
        private void BuildMedal()
        {
            // The disc has to dominate: shorter ribbons peeking out above a large disc read as a medal,
            // where two long ones either side of a small disc read as a letter A.
            float ribbonLength = _buttonSize * 0.30f;
            float ribbonThickness = _buttonSize * 0.11f;
            float discDiameter = _buttonSize * 0.56f;
            float discCentreY = -_buttonSize * 0.13f;

            for (int ribbonIndex = 0; ribbonIndex < 2; ribbonIndex++)
            {
                float sign = ribbonIndex == 0 ? 1f : -1f;

                var ribbonObject = new GameObject($"MedalRibbon_{ribbonIndex}", typeof(RectTransform), typeof(Image));
                var ribbonRect = (RectTransform)ribbonObject.transform;
                ribbonRect.SetParent(_buttonRect, false);
                Centre(ribbonRect, new Vector2(ribbonThickness, ribbonLength));
                ribbonRect.anchoredPosition = new Vector2(
                    sign * _buttonSize * 0.11f, _buttonSize * 0.20f);
                ribbonRect.localRotation = Quaternion.Euler(0f, 0f, sign * 16f);

                var ribbonImage = ribbonObject.GetComponent<Image>();
                ConfigurePlate(ribbonImage);
                _inkImages.Add(ribbonImage);
            }

            var discObject = new GameObject("MedalDisc", typeof(RectTransform), typeof(Image));
            var discRect = (RectTransform)discObject.transform;
            discRect.SetParent(_buttonRect, false);
            Centre(discRect, new Vector2(discDiameter, discDiameter));
            discRect.anchoredPosition = new Vector2(0f, discCentreY);

            var discImage = discObject.GetComponent<Image>();
            ConfigureCircle(discImage);
            _inkImages.Add(discImage);

            var coreObject = new GameObject("MedalCore", typeof(RectTransform), typeof(Image));
            var coreRect = (RectTransform)coreObject.transform;
            coreRect.SetParent(_buttonRect, false);
            Centre(coreRect, new Vector2(discDiameter * 0.46f, discDiameter * 0.46f));
            coreRect.anchoredPosition = new Vector2(0f, discCentreY);

            _medalCoreImage = coreObject.GetComponent<Image>();
            ConfigureCircle(_medalCoreImage);
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
