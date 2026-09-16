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
    /// Persistent HUD icon that opens the leaderboard overlay. Fifth in the right-edge icon column,
    /// under <see cref="ProfileButtonView"/>, at the same 136-unit pitch the four above it use.
    /// <para>
    /// Draws a winner's podium — three bars of stepped height on a common baseline — from the shared
    /// rounded-square sprite, so like its neighbours it needs no art asset and batches with every other
    /// UI Image.
    /// </para>
    /// <para>
    /// Knows only how to draw itself and whether a screen point is on it. The tap that opens the panel
    /// is routed by <see cref="BoardInputView"/>, the single owner of pointer input.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeaderboardButtonView : MonoBehaviour
    {
        /// <summary>Bars in the podium glyph, tallest in the middle.</summary>
        private const int BAR_COUNT = 3;

        [Header("Layout")]
        [Tooltip("Offset from the top-right corner of the canvas, in reference pixels. Stacked under the profile icon.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(-60f, -604f);
        [SerializeField] private float _buttonSize = 112f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Repaint bucket: every inked part of the glyph — the three podium bars.</summary>
        private readonly List<Image> _inkImages = new List<Image>(BAR_COUNT);

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
                    $"{nameof(LeaderboardButtonView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
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

            var shadowObject = new GameObject("LeaderboardButtonShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(rect, false);
            Centre(shadowRect, new Vector2(_buttonSize + 10f, _buttonSize + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _shadowImage = shadowObject.GetComponent<Image>();
            ConfigurePlate(_shadowImage);

            var plateObject = new GameObject("LeaderboardButtonPlate", typeof(RectTransform), typeof(Image));
            _buttonRect = (RectTransform)plateObject.transform;
            _buttonRect.SetParent(rect, false);
            Centre(_buttonRect, new Vector2(_buttonSize, _buttonSize));
            _plateImage = plateObject.GetComponent<Image>();
            ConfigurePlate(_plateImage);

            BuildPodium();
        }

        /// <summary>
        /// Three bars sharing a baseline, the middle one tallest. Growing them from a common bottom
        /// edge rather than a common centre is what makes them read as a podium instead of as a bar
        /// chart hanging in the air.
        /// </summary>
        private void BuildPodium()
        {
            float barWidth = _buttonSize * 0.20f;
            float barPitch = _buttonSize * 0.24f;
            float baselineY = -_buttonSize * 0.26f;

            for (int barIndex = 0; barIndex < BAR_COUNT; barIndex++)
            {
                // 0.34 / 0.54 / 0.42 of the icon: second place to the left of the winner, third to the
                // right, which is how a podium is ordered.
                float heightScale = barIndex == 1 ? 0.54f : (barIndex == 0 ? 0.34f : 0.42f);
                float barHeight = _buttonSize * heightScale;

                var barObject = new GameObject($"PodiumBar_{barIndex}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform)barObject.transform;
                barRect.SetParent(_buttonRect, false);
                Centre(barRect, new Vector2(barWidth, barHeight));
                barRect.anchoredPosition = new Vector2(
                    (barIndex - ((BAR_COUNT - 1) * 0.5f)) * barPitch,
                    baselineY + (barHeight * 0.5f));

                var barImage = barObject.GetComponent<Image>();
                ConfigurePlate(barImage);
                _inkImages.Add(barImage);
            }
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
    }
}
