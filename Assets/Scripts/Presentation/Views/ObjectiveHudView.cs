using System.Text;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The run's current objective, as one line under the tray: what to do on the left and how far
    /// along it is ("2/3") on the right. Binds to <see cref="ObjectiveModel.CurrentObjective"/> — the
    /// game shows exactly one objective at a time.
    /// <para>
    /// Every refresh re-reads the model rather than accumulating message payloads, so the HUD cannot
    /// drift from the objective it is showing: the progress and completion messages only say "this
    /// one moved", and a run reset — which resets progress without publishing anything — is picked up
    /// from <see cref="RunStartedMessage"/> the same way.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveHudView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Strip centre in canvas space. Sits in the band below the piece tray.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -862f);

        [SerializeField] private Vector2 _size = new Vector2(920f, 80f);
        [SerializeField] private int _descriptionFontSize = 38;
        [SerializeField] private int _progressFontSize = 44;

        [Tooltip("Diameter of the dot that marks the objective as complete.")]
        [SerializeField] private float _completeDotSize = 26f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _progressBuilder = new StringBuilder(8);

        private ObjectiveModel _objectiveModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private SettingsModel _settingsModel;
        private ISubscriber<ObjectiveProgressChangedMessage> _progressChangedSubscriber;
        private ISubscriber<ObjectiveCompletedMessage> _completedSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;

        private CanvasGroup _canvasGroup;
        private Text _descriptionText;
        private Text _progressText;
        private Image _completeDot;
        private ThemeDefinition _currentTheme;

        [Inject]
        public void Construct(
            ObjectiveModel objectiveModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            SettingsModel settingsModel,
            ISubscriber<ObjectiveProgressChangedMessage> progressChangedSubscriber,
            ISubscriber<ObjectiveCompletedMessage> completedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _objectiveModel = objectiveModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _settingsModel = settingsModel;
            _progressChangedSubscriber = progressChangedSubscriber;
            _completedSubscriber = completedSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
        }

        private void Awake()
        {
            // Unity allows several CanvasGroups on one object and then picks between them opaquely, so
            // an existing one is adopted rather than shadowed by a second.
            if (!TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            BuildLabels();
            SetVisible(false);
        }

        private void Start()
        {
            if (_objectiveModel == null || _localizationModel == null || _localizationSystem == null
                || _settingsModel == null || _progressChangedSubscriber == null
                || _completedSubscriber == null || _runStartedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(ObjectiveHudView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so _currentTheme is set before the first Refresh paints anything.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);

            _progressChangedSubscriber.Subscribe(OnObjectiveProgressChanged).AddTo(_disposables);
            _completedSubscriber.Subscribe(OnObjectiveCompleted).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);

            Refresh();
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            Refresh();
        }

        private void OnLocaleChanged(LocaleDefinition locale) => Refresh();

        private void OnRunStarted(RunStartedMessage message) => Refresh();

        /// <summary>
        /// Runs inside the placement that moved the objective — the publisher is synchronous — so the
        /// label is already correct in the frame the qualifying placement resolves.
        /// </summary>
        private void OnObjectiveProgressChanged(ObjectiveProgressChangedMessage message)
        {
            if (IsCurrentObjective(message.ObjectiveId))
            {
                Refresh();
            }
        }

        /// <summary>
        /// The completed state is read back off the model rather than latched here, so this handler
        /// firing twice could not produce a second completion visual. It cannot fire twice anyway:
        /// the message is published on the false to true edge only.
        /// </summary>
        private void OnObjectiveCompleted(ObjectiveCompletedMessage message)
        {
            if (IsCurrentObjective(message.ObjectiveId))
            {
                Refresh();
            }
        }

        private bool IsCurrentObjective(string objectiveId)
        {
            ObjectiveProgress current = _objectiveModel.CurrentObjective;
            return current != null && current.Definition.Id == objectiveId;
        }

        /// <summary>Repaints the whole strip from the model: text, progress, colours and visibility.</summary>
        private void Refresh()
        {
            ObjectiveProgress current = _objectiveModel != null ? _objectiveModel.CurrentObjective : null;
            if (current == null || _currentTheme == null)
            {
                // Nothing tracked yet (or no theme to paint with): an empty strip reads as a bug, so
                // the strip hides itself instead.
                SetVisible(false);
                return;
            }

            SetVisible(true);

            _descriptionText.text = ObjectiveDescriptionFormatter.Describe(current.Definition, _localizationSystem);

            _progressBuilder.Clear();
            _progressBuilder.Append(current.CurrentValue);
            _progressBuilder.Append('/');
            _progressBuilder.Append(current.Definition.TargetValue);
            _progressText.text = _progressBuilder.ToString();

            // Complete reads as the accent colour plus a filled dot; in progress is the usual ink with
            // the dot punched out to fully transparent, so the two states never need new art.
            bool isComplete = current.IsComplete;
            Color textColour = isComplete ? _currentTheme.Accent : _currentTheme.Ink;
            _descriptionText.color = isComplete ? _currentTheme.Accent : _currentTheme.SoftInk;
            _progressText.color = textColour;
            _completeDot.color = isComplete ? _currentTheme.Accent : Color.clear;
        }

        /// <summary>
        /// Shown and hidden through the CanvasGroup rather than by toggling the GameObject: toggling it
        /// would rebuild the shared canvas mesh every time the tracked objective appears or clears.
        /// </summary>
        private void SetVisible(bool isVisible)
        {
            _canvasGroup.alpha = isVisible ? 1f : 0f;
            _canvasGroup.interactable = isVisible;
            _canvasGroup.blocksRaycasts = isVisible;
        }

        private void BuildLabels()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = _size;
            rect.anchoredPosition = _anchoredPosition;

            // Every size here is in canvas reference units, so the strip owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            rect.localScale = Vector3.one;

            // Labels are built before the theme and the objective are known; Refresh fills in both.
            _descriptionText = UiTextFactory.Create(
                rect, "ObjectiveDescription", _descriptionFontSize, FontStyle.Bold, Color.clear);
            var descriptionRect = (RectTransform)_descriptionText.transform;
            descriptionRect.anchorMin = new Vector2(0f, 0.5f);
            descriptionRect.anchorMax = new Vector2(0f, 0.5f);
            descriptionRect.pivot = new Vector2(0f, 0.5f);
            descriptionRect.anchoredPosition = new Vector2(_completeDotSize + 20f, 0f);
            _descriptionText.alignment = TextAnchor.MiddleLeft;

            _progressText = UiTextFactory.Create(
                rect, "ObjectiveProgress", _progressFontSize, FontStyle.Bold, Color.clear);
            var progressRect = (RectTransform)_progressText.transform;
            progressRect.anchorMin = new Vector2(1f, 0.5f);
            progressRect.anchorMax = new Vector2(1f, 0.5f);
            progressRect.pivot = new Vector2(1f, 0.5f);
            progressRect.anchoredPosition = Vector2.zero;
            _progressText.alignment = TextAnchor.MiddleRight;

            var dotObject = new GameObject("CompleteDot", typeof(RectTransform), typeof(Image));
            var dotRect = (RectTransform)dotObject.transform;
            dotRect.SetParent(rect, false);
            dotRect.anchorMin = new Vector2(0f, 0.5f);
            dotRect.anchorMax = new Vector2(0f, 0.5f);
            dotRect.pivot = new Vector2(0f, 0.5f);
            dotRect.sizeDelta = new Vector2(_completeDotSize, _completeDotSize);
            dotRect.anchoredPosition = Vector2.zero;

            _completeDot = dotObject.GetComponent<Image>();
            _completeDot.sprite = UiSpriteFactory.Circle;
            _completeDot.type = Image.Type.Simple;
            _completeDot.color = Color.clear;

            // Raycasts stay off: taps arrive through BoardInputView's pointer action, not through an
            // EventSystem, and this scene has none.
            _completeDot.raycastTarget = false;
        }
    }
}
