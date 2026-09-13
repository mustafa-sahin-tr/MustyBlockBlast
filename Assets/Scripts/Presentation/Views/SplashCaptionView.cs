using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Bottom caption cluster of the splash screen: three loading dots, the "tap to skip" hint and
    /// the note that the boot jingle follows the device sound setting. The hierarchy is built once in
    /// <see cref="Awake"/> and never animated — the mockup's dots are static, only their opacity
    /// steps down left to right. Only the caption wording changes, and only when the language does.
    /// <para>
    /// Purely informative: it neither reads nor drives <see cref="SplashSystem"/>, so the skip hint
    /// can never desynchronise the transition it describes.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SplashCaptionView : MonoBehaviour
    {
        private const int DOT_COUNT = 3;
        private const float DOT_SIZE = 18f;
        private const float DOT_SPACING = 34f;

        private const float CLUSTER_HEIGHT = 380f;
        private const float DOTS_Y = 262f;
        private const float SKIP_TEXT_Y = 180f;
        private const float JINGLE_TEXT_Y = 92f;

        private const int SKIP_FONT_SIZE = 40;
        private const int JINGLE_FONT_SIZE = 30;

        /// <summary>#FFFFFF — white used by the dots and the skip hint.</summary>
        private static readonly Color PrimaryCaption = Color.white;

        /// <summary>#C7C2E8 — soft lavender used by the jingle note.</summary>
        private static readonly Color SecondaryCaption = new Color32(199, 194, 232, 255);

        /// <summary>Dot opacities, fading left to right exactly as in the mockup.</summary>
        private static readonly float[] DotAlphas = { 0.85f, 0.55f, 0.3f };

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private Text _skipHintText;
        private Text _jingleNoteText;

        [Inject]
        public void Construct(LocalizationModel localizationModel, LocalizationSystem localizationSystem)
        {
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
        }

        private void Awake()
        {
            var rootRect = (RectTransform)transform;
            rootRect.anchorMin = new Vector2(0f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            rootRect.sizeDelta = new Vector2(0f, CLUSTER_HEIGHT);
            rootRect.anchoredPosition = Vector2.zero;

            BuildDots(rootRect);

            // Built wordless: the locale subscription in Start fills both captions in and refills them
            // on every later language switch.
            _skipHintText = BuildCaption(
                rootRect, "SkipHint", SKIP_FONT_SIZE, FontStyle.Bold, PrimaryCaption, 0.75f, SKIP_TEXT_Y);
            _jingleNoteText = BuildCaption(
                rootRect, "JingleNote", JINGLE_FONT_SIZE, FontStyle.Normal, SecondaryCaption, 1f, JINGLE_TEXT_Y);
        }

        private void Start()
        {
            if (_localizationModel == null || _localizationSystem == null)
            {
                Debug.LogError(
                    $"{nameof(SplashCaptionView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnLocaleChanged(LocaleDefinition locale)
        {
            _skipHintText.text = _localizationSystem.Translate(LocalizationKeys.SPLASH_SKIP_HINT);
            _jingleNoteText.text = _localizationSystem.Translate(LocalizationKeys.SPLASH_JINGLE_NOTE);
        }

        private static void BuildDots(RectTransform parent)
        {
            var rowObject = new GameObject("LoadingDots", typeof(RectTransform));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(parent, false);
            rowRect.anchorMin = new Vector2(0.5f, 0f);
            rowRect.anchorMax = new Vector2(0.5f, 0f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.sizeDelta = Vector2.zero;
            rowRect.anchoredPosition = new Vector2(0f, DOTS_Y);

            float firstX = -DOT_SPACING * (DOT_COUNT - 1) * 0.5f;

            for (int dotIndex = 0; dotIndex < DOT_COUNT; dotIndex++)
            {
                var dotObject = new GameObject($"Dot_{dotIndex}", typeof(RectTransform), typeof(Image));
                var dotRect = (RectTransform)dotObject.transform;
                dotRect.SetParent(rowRect, false);
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.sizeDelta = new Vector2(DOT_SIZE, DOT_SIZE);
                dotRect.anchoredPosition = new Vector2(firstX + (dotIndex * DOT_SPACING), 0f);

                Color colour = PrimaryCaption;
                colour.a = DotAlphas[dotIndex];

                var image = dotObject.GetComponent<Image>();
                image.sprite = UiSpriteFactory.Circle;
                image.type = Image.Type.Simple;
                image.color = colour;
                image.raycastTarget = false;
            }
        }

        private static Text BuildCaption(
            RectTransform parent,
            string objectName,
            int fontSize,
            FontStyle fontStyle,
            Color colour,
            float alpha,
            float y)
        {
            colour.a = alpha;

            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, colour);

            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, fontSize * 1.4f);
            rect.anchoredPosition = new Vector2(0f, y);
            return text;
        }
    }
}
