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
    /// Persistent HUD icon that opens the level path overlay. Sits directly under
    /// <see cref="SettingsButtonView"/> on the right edge, so the two overlay entry points read as one
    /// column of icons and neither collides with the score at the top centre, the best score in the
    /// top-left corner, or the countdown below them.
    /// <para>
    /// Draws a three-stop route — two dots on a rising zig-zag with a flag at the end — from the shared
    /// circle and rounded-square sprites, so it needs no art asset and batches with every other UI
    /// Image.
    /// </para>
    /// <para>
    /// Like <see cref="SettingsButtonView"/> it only knows how to draw itself and whether a screen
    /// point is on it. The tap that opens the panel is routed by <see cref="BoardInputView"/>, which is
    /// the single owner of pointer input.
    /// </para>
    /// <para>
    /// In Path mode it also hosts <see cref="PathLevelBadgeView"/>, which parents itself onto
    /// <see cref="RootRect"/> to sit on the icon's top-right corner. The badge is a sibling of the
    /// plate, not a child of it, so it never widens the plate's hit test.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelPathButtonView : MonoBehaviour
    {
        /// <summary>Stops on the route, in button-relative units (-0.5..0.5 across the plate).</summary>
        private static readonly Vector2[] RouteStops =
        {
            new Vector2(-0.26f, -0.19f),
            new Vector2(-0.02f, 0.10f),
            new Vector2(0.25f, -0.08f),
        };

        [Header("Layout")]
        [Tooltip("Offset from the top-right corner of the canvas, in reference pixels. Stacked under the settings icon.")]
        // Settings sits at -48 and is 104 tall; a 16px gap puts this at -168. See SettingsButtonView
        // for why the column is this tight.
        [SerializeField] private Vector2 _cornerOffset = new Vector2(-60f, -168f);
        [SerializeField] private float _buttonSize = 104f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Repaint bucket: every part of the glyph that is inked — the stops, the segments
        /// between them and the flag on the last stop.</summary>
        private readonly List<Image> _inkImages = new List<Image>(8);

        private SettingsModel _settingsModel;
        private RectTransform _buttonRect;
        private Image _plateImage;
        private Image _shadowImage;
        private Canvas _canvas;

        /// <summary>
        /// The icon's own rect — the 112x112 top-right anchored root that the plate, shadow and glyph
        /// hang off. <see cref="PathLevelBadgeView"/> parents onto this so the badge follows the icon
        /// wherever <see cref="_cornerOffset"/> puts it, without being able to see or alter the plate.
        /// </summary>
        internal RectTransform RootRect => (RectTransform)transform;

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
                    $"{nameof(LevelPathButtonView)} was not injected. Is it registered in the LifetimeScope?", this);
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

            var shadowObject = new GameObject("LevelPathButtonShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(rect, false);
            Centre(shadowRect, new Vector2(_buttonSize + 10f, _buttonSize + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImage = shadowObject.GetComponent<Image>();
            ConfigurePlate(_shadowImage);

            var plateObject = new GameObject("LevelPathButtonPlate", typeof(RectTransform), typeof(Image));
            _buttonRect = (RectTransform)plateObject.transform;
            _buttonRect.SetParent(rect, false);
            Centre(_buttonRect, new Vector2(_buttonSize, _buttonSize));
            _plateImage = plateObject.GetComponent<Image>();
            ConfigurePlate(_plateImage);

            BuildRoute();
        }

        // Segments first, then the stops on top of their ends: sibling order is the only thing keeping
        // the glyph readable, since nothing here is masked.
        private void BuildRoute()
        {
            float stopDiameter = _buttonSize * 0.17f;
            float segmentThickness = _buttonSize * 0.07f;

            for (int stopIndex = 0; stopIndex < RouteStops.Length - 1; stopIndex++)
            {
                Vector2 from = RouteStops[stopIndex] * _buttonSize;
                Vector2 to = RouteStops[stopIndex + 1] * _buttonSize;
                Vector2 delta = to - from;

                var segmentObject = new GameObject($"RouteSegment_{stopIndex}", typeof(RectTransform), typeof(Image));
                var segmentRect = (RectTransform)segmentObject.transform;
                segmentRect.SetParent(_buttonRect, false);
                Centre(segmentRect, new Vector2(delta.magnitude, segmentThickness));
                segmentRect.anchoredPosition = from + (delta * 0.5f);
                segmentRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

                var segmentImage = segmentObject.GetComponent<Image>();
                ConfigurePlate(segmentImage);
                _inkImages.Add(segmentImage);
            }

            for (int stopIndex = 0; stopIndex < RouteStops.Length; stopIndex++)
            {
                var stopObject = new GameObject($"RouteStop_{stopIndex}", typeof(RectTransform), typeof(Image));
                var stopRect = (RectTransform)stopObject.transform;
                stopRect.SetParent(_buttonRect, false);
                Centre(stopRect, new Vector2(stopDiameter, stopDiameter));
                stopRect.anchoredPosition = RouteStops[stopIndex] * _buttonSize;

                var stopImage = stopObject.GetComponent<Image>();
                ConfigureCircle(stopImage);
                _inkImages.Add(stopImage);
            }

            BuildFlag(RouteStops[RouteStops.Length - 1] * _buttonSize, stopDiameter);
        }

        /// <summary>
        /// A mast and a pennant on the last stop, so the glyph reads as "a route with an end" rather
        /// than as a generic scatter of dots.
        /// </summary>
        private void BuildFlag(Vector2 lastStop, float stopDiameter)
        {
            float mastHeight = _buttonSize * 0.30f;
            float mastThickness = _buttonSize * 0.055f;

            var mastObject = new GameObject("FlagMast", typeof(RectTransform), typeof(Image));
            var mastRect = (RectTransform)mastObject.transform;
            mastRect.SetParent(_buttonRect, false);
            Centre(mastRect, new Vector2(mastThickness, mastHeight));
            mastRect.anchoredPosition = lastStop + new Vector2(0f, (mastHeight * 0.5f) - (stopDiameter * 0.25f));

            var mastImage = mastObject.GetComponent<Image>();
            ConfigurePlate(mastImage);
            _inkImages.Add(mastImage);

            var pennantObject = new GameObject("FlagPennant", typeof(RectTransform), typeof(Image));
            var pennantRect = (RectTransform)pennantObject.transform;
            pennantRect.SetParent(_buttonRect, false);
            Centre(pennantRect, new Vector2(_buttonSize * 0.17f, _buttonSize * 0.13f));
            pennantRect.anchoredPosition = lastStop + new Vector2(
                -(_buttonSize * 0.10f), mastHeight - (stopDiameter * 0.25f) - (_buttonSize * 0.08f));

            var pennantImage = pennantObject.GetComponent<Image>();
            ConfigurePlate(pennantImage);
            _inkImages.Add(pennantImage);
        }

        private static void Centre(RectTransform rect, Vector2 size)
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
