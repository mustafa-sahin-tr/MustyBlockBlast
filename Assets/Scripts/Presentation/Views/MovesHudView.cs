using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The moves-left counter of a "score within N moves" level (issue #465, mockup artboards "HUD: kalan
    /// hamle" and "az hamle uyarısı"): the objective's block-and-flag icon, the number of moves left in the
    /// display face and a small "MOVES" caption under it, on a soft plate. At
    /// <see cref="MoveBudgetConfig.LowMovesThreshold"/> moves or fewer the plate turns coral and the section
    /// pulses gently until the budget is topped up or the run ends.
    /// <para>
    /// A guest of the goal bar's trailing slot, like <see cref="LivesHudView"/>, placed first in it so it
    /// sits between the goal chips and the lives. It shows only while <see cref="MoveBudgetModel.IsActive"/>
    /// — a Path run on a move-limited level — and hides through its own CanvasGroup otherwise, handing its
    /// width back to the chips. Binds to the model reactively; nothing here is polled.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MovesHudView : MonoBehaviour
    {
        /// <summary>The mockup's calm plate (#EEF5FC) and its warning pair (#FFE3DC fill, #C23A22 ink).</summary>
        private static readonly Color CalmPlate = new Color32(0xEE, 0xF5, 0xFC, 0xFF);
        private static readonly Color WarningPlate = new Color32(0xFF, 0xE3, 0xDC, 0xFF);
        private static readonly Color WarningInk = new Color32(0xC2, 0x3A, 0x22, 0xFF);
        private static readonly Color WarningOutline = new Color32(0xE8, 0x78, 0x5A, 0xFF);

        private const float PULSE_PERIOD_SECONDS = 0.9f;
        private const float PULSE_SCALE = 0.08f;

        [Header("Layout")]
        [Tooltip("Width the section takes in the bar, in reference pixels. Fixed, so a figure going from " +
            "10 to 9 never re-packs the chips beside it.")]
        [SerializeField] private float _sectionWidth = 132f;

        [SerializeField] private float _sectionHeight = 76f;
        [SerializeField] private float _plateRadius = 26f;
        [SerializeField] private float _outlineThickness = 4f;
        [SerializeField] private float _iconSize = 54f;
        [SerializeField] private int _figureFontSize = 36;
        [SerializeField] private int _captionFontSize = 16;

        [Header("Art")]
        [Tooltip("The chunky display face for the figure. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("The body face for the small caption.")]
        [SerializeField] private Font _bodyFont;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(4);

        private MoveBudgetModel _moveBudgetModel;
        private MoveBudgetConfig _moveBudgetConfig;
        private ObjectiveIconCatalog _iconCatalog;
        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ObjectiveIconContainerView _objectiveIconContainerView;

        private RectTransform _rect;
        private CanvasGroup _group;
        private RectTransform _bodyRect;
        private Image _plateImage;
        private Image _outlineImage;
        private Image _iconImage;
        private Text _figureText;
        private Text _captionText;
        private ThemeDefinition _currentTheme;

        private bool _isVisible;
        private bool _isLow;
        private CancellationTokenSource _pulseCts;

        [Inject]
        public void Construct(
            MoveBudgetModel moveBudgetModel,
            MoveBudgetConfig moveBudgetConfig,
            ObjectiveIconCatalog iconCatalog,
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            ObjectiveIconContainerView objectiveIconContainerView)
        {
            _moveBudgetModel = moveBudgetModel;
            _moveBudgetConfig = moveBudgetConfig;
            _iconCatalog = iconCatalog;
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _objectiveIconContainerView = objectiveIconContainerView;
        }

        private void Awake() => BuildSection();

        private void Start()
        {
            if (_moveBudgetModel == null || _moveBudgetConfig == null || _iconCatalog == null
                || _settingsModel == null || _localizationModel == null || _localizationSystem == null
                || _objectiveIconContainerView == null)
            {
                Debug.LogError(
                    $"{nameof(MovesHudView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            Sprite icon = _iconCatalog.Find(ObjectiveType.ScoreInMoves);
            _iconImage.sprite = icon;
            _iconImage.color = icon != null ? Color.white : Color.clear;

            AttachToGoalBar();

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(_ => PaintCaption()).AddTo(_disposables);
            _moveBudgetModel.MovesLeft.Subscribe(_ => Repaint()).AddTo(_disposables);
            _moveBudgetModel.IsActive.Subscribe(_ => RefreshVisibility()).AddTo(_disposables);
        }

        private void OnDestroy()
        {
            StopPulse();
            _disposables.Dispose();
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            Repaint();
        }

        private void PaintCaption()
            => _captionText.text = _localizationSystem.Translate(LocalizationKeys.MOVES_HUD_LABEL);

        /// <summary>The figure, and the calm or warning look for it. Pulses only while low and showing.</summary>
        private void Repaint()
        {
            int movesLeft = _moveBudgetModel.MovesLeft.Value;
            _stringBuilder.Clear();
            _stringBuilder.Append(movesLeft);
            _figureText.text = _stringBuilder.ToString();

            bool isLow = _moveBudgetModel.IsActive.Value && movesLeft <= _moveBudgetConfig.LowMovesThreshold;
            Color ink = _currentTheme != null ? _currentTheme.Ink : Color.black;
            _plateImage.color = isLow ? WarningPlate : CalmPlate;
            _outlineImage.color = isLow ? WarningOutline : Color.clear;
            _figureText.color = isLow ? WarningInk : ink;
            _captionText.color = isLow ? WarningInk : ink;

            if (isLow == _isLow)
            {
                return;
            }

            _isLow = isLow;
            if (isLow)
            {
                StartPulse();
            }
            else
            {
                StopPulse();
            }
        }

        private void RefreshVisibility()
        {
            bool isVisible = _moveBudgetModel.IsActive.Value;
            _group.alpha = isVisible ? 1f : 0f;
            Repaint();

            if (isVisible == _isVisible)
            {
                return;
            }

            _isVisible = isVisible;
            _objectiveIconContainerView.NotifySlotsChanged();
        }

        private void StartPulse()
        {
            StopPulse();
            _pulseCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            PulseAsync(_pulseCts.Token).Forget();
        }

        private void StopPulse()
        {
            if (_pulseCts != null)
            {
                _pulseCts.Cancel();
                _pulseCts.Dispose();
                _pulseCts = null;
            }

            if (_bodyRect != null)
            {
                _bodyRect.localScale = Vector3.one;
            }
        }

        /// <summary>A soft breathing scale on the section's body — the mockup's pulse — until cancelled.</summary>
        private async UniTaskVoid PulseAsync(CancellationToken cancellationToken)
        {
            float elapsed = 0f;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    float wave = 0.5f - (0.5f * Mathf.Cos(elapsed * Mathf.PI * 2f / PULSE_PERIOD_SECONDS));
                    float scale = 1f + (PULSE_SCALE * wave);
                    _bodyRect.localScale = new Vector3(scale, scale, 1f);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped — StopPulse resets the scale.
            }
        }

        /// <summary>
        /// Moves the section under the bar's trailing slot, first sibling: the bar places its trailing
        /// children right to left, so the lives (last sibling) stay rightmost and this sits left of them.
        /// </summary>
        private void AttachToGoalBar()
        {
            _rect.SetParent(_objectiveIconContainerView.TrailingSlot, false);
            _rect.SetAsFirstSibling();
            _rect.anchoredPosition = Vector2.zero;
            _objectiveIconContainerView.NotifySlotsChanged();
        }

        /// <summary>Built hidden; the subscriptions in <see cref="Start"/> fill it in and show it.</summary>
        private void BuildSection()
        {
            _rect = (RectTransform)transform;
            _rect.anchorMin = new Vector2(1f, 0.5f);
            _rect.anchorMax = new Vector2(1f, 0.5f);
            _rect.pivot = new Vector2(1f, 0.5f);
            _rect.sizeDelta = new Vector2(_sectionWidth, _sectionHeight);
            _rect.anchoredPosition = Vector2.zero;
            _rect.localScale = Vector3.one;

            if (!TryGetComponent(out _group))
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _group.ignoreParentGroups = true;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _group.alpha = 0f;

            var size = new Vector2(_sectionWidth, _sectionHeight);
            _bodyRect = HudChrome.CreateRect(_rect, "Body", size, Vector2.zero);
            _plateImage = HudChrome.BuildRounded(_bodyRect, "Plate", size, Vector2.zero, _plateRadius);
            _outlineImage = HudChrome.BuildOutline(_bodyRect, "Outline", size, Vector2.zero, _plateRadius, _outlineThickness);
            _outlineImage.color = Color.clear;

            float left = -_sectionWidth * 0.5f;
            _iconImage = HudChrome.BuildGlyph(
                _bodyRect, "Icon", null, new Vector2(_iconSize, _iconSize), new Vector2(left + 8f + (_iconSize * 0.5f), 0f));
            _iconImage.preserveAspect = true;
            _iconImage.color = Color.clear;

            float textCentreX = left + 8f + _iconSize + ((_sectionWidth - 16f - _iconSize) * 0.5f);
            _figureText = HudChrome.CreateLabel(
                _bodyRect, "Figure", _figureFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(textCentreX, 9f), _displayFont);
            _captionText = HudChrome.CreateLabel(
                _bodyRect, "Caption", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(textCentreX, -22f), _bodyFont);
        }
    }
}
