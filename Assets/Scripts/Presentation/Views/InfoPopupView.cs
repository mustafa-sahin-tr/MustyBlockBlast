using Cysharp.Threading.Tasks;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
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
    /// The one info popup on screen: an icon, a header and a short body explaining what it is about,
    /// shown automatically the first time its subject appears and reopenable on demand afterward (see
    /// <see cref="InfoPopupSystem"/>). Replaces the old spotlight/coach-mark overlay entirely — this is
    /// an ordinary, optional modal, never a forced action.
    /// <para>
    /// Built the same way <see cref="ObjectiveInfoPopupView"/> is: one card on a scrim, toggled from
    /// <see cref="InfoPopupModel.OpenContent"/> rather than an imperative Open/Close pair. Unlike every
    /// other modal in <see cref="BoardInputView"/>'s gate chain, this one CAN be open at the same time as
    /// the hub/Power-up Shop — reached by tapping a shop row's icon — so it only takes ownership of
    /// <see cref="TimerRunSystem.SetMenuPaused"/>'s single shared flag when nothing else already holds
    /// it (see <see cref="_ownsMenuPause"/>), and its own gate in <see cref="BoardInputView"/> is checked
    /// before the hub's so a tap on this card (its close cross included) is never swallowed by the panel
    /// still open underneath it.
    /// </para>
    /// <para>
    /// The hero icon is never authored here for a special cell, a power-up or the Hold pocket: it
    /// borrows whichever View already owns the authored sprite for that subject —
    /// <see cref="BoardView"/>, <see cref="PowerUpInventoryView"/>, <see cref="HoldSlotView"/> — so one
    /// glyph is never drawn twice. A special piece has no other View that owns a hero-sized glyph for
    /// it (dock plates show <c>SpecialPieceVisuals</c>' small mark, not this card's icon), so this View
    /// is the one owner of the three <see cref="SpecialPieceKind"/> hero sprites, authored the same
    /// white-silhouette-plus-runtime-tint way <see cref="BoardView"/>'s special cell icons are (see
    /// <see cref="ResolveIcon"/>).
    /// </para>
    /// <para>
    /// A subject with an authored animated demo (<see cref="InfoDemoCatalog"/>, issue #446 — the
    /// Vortex special cell so far) swaps the hero icon for the chrome's demo slot and loops the demo on
    /// an <see cref="InfoDemoStage"/> while the card is open; closing the card stops it on the spot.
    /// Every other subject keeps its hero icon exactly as before.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InfoPopupView : MonoBehaviour
    {
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 640f);
        [SerializeField] private int _headerFontSize = 60;
        [SerializeField] private int _bodyFontSize = 40;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        [Header("Special piece icons (issue #283)")]
        [SerializeField] private Sprite _goldenIconSprite;
        [SerializeField] private Sprite _piercingRocketIconSprite;
        [SerializeField] private Sprite _demolitionHammerIconSprite;

        private InfoPopupModel _infoPopupModel;
        private InfoPopupSystem _infoPopupSystem;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private SettingsModel _settingsModel;
        private TimerRunSystem _timerRunSystem;
        private RunPauseModel _runPauseModel;
        private BoardView _boardView;
        private PowerUpInventoryView _powerUpInventoryView;
        private HoldSlotView _holdSlotView;
        private CoinTotalHudView _coinTotalHudView;

        /// <summary>
        /// Whether THIS popup is the one holding <see cref="TimerRunSystem"/>'s menu-pause flag. This
        /// card can now open two ways: standing alone (a board tap), where nothing else is paused and it
        /// must own the flag itself — or on top of the Power-up Shop (a tap on a shop row's icon), where
        /// the hub already owns the pause for its own lifetime. Read <see cref="RunPauseModel.IsPaused"/>
        /// at the moment this card opens to tell the two apart, so closing never clears a pause another
        /// still-open modal depends on (that was a real bug: the shop's own pause got cancelled the
        /// instant this card closed on top of it, even though the shop stayed open).
        /// </summary>
        private bool _ownsMenuPause;

        private Canvas _canvas;
        private GameObject _panel;
        private InfoCardChrome.Handles _chrome;
        private Image _heroIconImage;

        private readonly InfoDemoCatalog _demoCatalog = new InfoDemoCatalog();
        private InfoDemoStage _demoStage;

        private ThemeDefinition _currentTheme;
        private InfoPopupContent? _currentContent;

        /// <summary>
        /// Colour a <see cref="SpecialPieceKind.Golden"/>'s hero icon is drawn in. The same gold
        /// <c>SpecialPieceVisuals.GoldFill</c> paints the piece's own dock plate with — "this one is
        /// golden" should read as the one consistent hue everywhere it appears.
        /// </summary>
        private static readonly Color GoldenIconTint = new Color(1f, 0.78f, 0.20f, 1f);

        /// <summary>Colour a <see cref="SpecialPieceKind.PiercingRocket"/>'s hero icon is drawn in. A
        /// distinct warm hue from <see cref="GoldenIconTint"/>, matching what the piece itself
        /// launches: fire, not gold — see the negative-test requirement in issue #283 that Golden and
        /// Demolition Hammer must never again share one undistinguished placeholder.</summary>
        private static readonly Color PiercingRocketIconTint = new Color(1f, 0.47f, 0.24f, 1f);

        /// <summary>Colour a <see cref="SpecialPieceKind.DemolitionHammer"/>'s hero icon is drawn in. A
        /// cool steel hue, deliberately the furthest from <see cref="GoldenIconTint"/> of the three, for
        /// the reason <see cref="PiercingRocketIconTint"/> is.</summary>
        private static readonly Color DemolitionHammerIconTint = new Color(0.72f, 0.76f, 0.84f, 1f);

        [Inject]
        public void Construct(
            InfoPopupModel infoPopupModel,
            InfoPopupSystem infoPopupSystem,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            SettingsModel settingsModel,
            TimerRunSystem timerRunSystem,
            RunPauseModel runPauseModel,
            BoardView boardView,
            PowerUpInventoryView powerUpInventoryView,
            HoldSlotView holdSlotView,
            CoinTotalHudView coinTotalHudView)
        {
            _infoPopupModel = infoPopupModel;
            _infoPopupSystem = infoPopupSystem;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _settingsModel = settingsModel;
            _timerRunSystem = timerRunSystem;
            _runPauseModel = runPauseModel;
            _boardView = boardView;
            _powerUpInventoryView = powerUpInventoryView;
            _holdSlotView = holdSlotView;
            _coinTotalHudView = coinTotalHudView;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_infoPopupModel == null || _infoPopupSystem == null || _localizationModel == null
                || _localizationSystem == null || _settingsModel == null || _timerRunSystem == null
                || _runPauseModel == null
                || _boardView == null || _powerUpInventoryView == null || _holdSlotView == null
                || _coinTotalHudView == null)
            {
                Debug.LogError(
                    $"{nameof(InfoPopupView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            BuildPanel();
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
            _infoPopupModel.OpenContent.Subscribe(OnContentChanged).AddTo(_disposables);
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_demoStage != null)
            {
                _demoStage.Dispose();
            }
        }

        /// <summary>True while the card is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>
        /// Routes a tap while the card is open. The close cross wins; the card then swallows anything
        /// else, so only the scrim outside it dismisses.
        /// </summary>
        internal void HandleTap(Vector2 screenPosition)
        {
            if (!IsOpen)
            {
                return;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (RectTransformUtility.RectangleContainsScreenPoint(_chrome.Close.HitRect, screenPosition, eventCamera))
            {
                _infoPopupSystem.Close();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_chrome.CardRect, screenPosition, eventCamera))
            {
                return;
            }

            _infoPopupSystem.Close();
        }

        private void OnContentChanged(InfoPopupContent? content)
        {
            _currentContent = content;

            if (_panel == null)
            {
                return;
            }

            if (content == null)
            {
                // Stopped first and unconditionally: a closed card must never keep a demo loop alive.
                _demoStage.Stop();

                if (_panel.activeSelf)
                {
                    _panel.SetActive(false);

                    if (_ownsMenuPause)
                    {
                        _timerRunSystem.SetMenuPaused(false);
                        _ownsMenuPause = false;
                    }
                }

                return;
            }

            bool wasOpen = _panel.activeSelf;
            Refresh();
            _panel.SetActive(true);
            transform.SetAsLastSibling();

            if (!wasOpen)
            {
                // Already paused (e.g. the Power-up Shop is open underneath, reached by tapping one of
                // its rows) means some other modal owns the flag for its own lifetime — leave it alone,
                // and don't clear it out from under that modal when this card closes.
                _ownsMenuPause = !_runPauseModel.IsPaused.Value;
                if (_ownsMenuPause)
                {
                    _timerRunSystem.SetMenuPaused(true);
                }
            }
        }

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

        /// <summary>Repaints the card from the current content and theme. A no-op while closed or
        /// before either is known.</summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null || _currentContent == null)
            {
                return;
            }

            InfoPopupContent content = _currentContent.Value;

            _chrome.CardImage.color = _currentTheme.CardBackground;
            _chrome.CardShadowImage.color = _currentTheme.CardShadow;
            _chrome.Close.PlateImage.color = _currentTheme.CardBackground;
            _chrome.Close.PlateShadowImage.color = _currentTheme.CardShadow;

            for (int barIndex = 0; barIndex < _chrome.Close.BarImages.Count; barIndex++)
            {
                _chrome.Close.BarImages[barIndex].color = _currentTheme.Ink;
            }

            _chrome.HeroPlateImage.color = _currentTheme.Accent;
            _chrome.HeroRingImage.color = Color.Lerp(_currentTheme.Accent, _currentTheme.CardBackground, 0.55f);

            InfoDemoTimeline demo = _demoCatalog.Find(content.SubjectKind, content.KindValue);
            if (demo != null)
            {
                InfoCardChrome.ShowDemo(_chrome, InfoDemoStage.Size);
                _demoStage.SetTheme(_currentTheme);
                _demoStage.Play(demo);
            }
            else
            {
                _demoStage.Stop();
                InfoCardChrome.ShowHeroIcon(_chrome);

                ResolveIcon(content, out Sprite icon, out Color iconTint);
                _heroIconImage.sprite = icon;
                _heroIconImage.color = icon != null ? iconTint : Color.clear;
            }

            _chrome.TitleText.color = _currentTheme.Ink;
            _chrome.TitleText.text = _localizationSystem.Translate(content.HeaderLocalizationKey);

            _chrome.DescriptionText.color = _currentTheme.SoftInk;
            _chrome.DescriptionText.text = _localizationSystem.Translate(content.BodyLocalizationKey);

            // Title and description text just changed length — reflow so a long, multi-line
            // description grows the card downward instead of overlapping the title above it.
            InfoCardChrome.Reflow(_chrome, _cardSize);
        }

        /// <summary>
        /// The hero icon for <paramref name="content"/>'s subject, borrowed from whichever View already
        /// owns the authored sprite for it — never drawn twice. A special piece has no authored icon of
        /// its own (special pieces are painted as a colour/pattern treatment on the piece's own cells via
        /// <c>SpecialPieceVisuals</c>, not as a separate glyph), so every <see cref="SpecialPieceKind"/>
        /// falls back to the shared starburst placeholder — the same fallback
        /// <c>ObjectiveIconFactory</c>/<see cref="ObjectiveInfoPopupView"/> use for an unauthored
        /// objective glyph.
        /// </summary>
        private void ResolveIcon(InfoPopupContent content, out Sprite icon, out Color tint)
        {
            switch (content.SubjectKind)
            {
                case InfoPopupSubjectKind.SpecialCell:
                    var cellKind = (SpecialCellKind)content.KindValue;
                    icon = _boardView.IconSprite(cellKind);
                    tint = BoardView.IconTint(cellKind);
                    return;
                case InfoPopupSubjectKind.PowerUp:
                    icon = _powerUpInventoryView.IconFor((PowerUpKind)content.KindValue);
                    tint = Color.white;
                    return;
                case InfoPopupSubjectKind.Hold:
                    icon = _holdSlotView.PocketSprite;
                    tint = Color.white;
                    return;
                case InfoPopupSubjectKind.SpecialPiece:
                    ResolveSpecialPieceIcon((SpecialPieceKind)content.KindValue, out icon, out tint);
                    return;
                default:
                    icon = UiSpriteFactory.Starburst;
                    tint = Color.white;
                    return;
            }
        }

        /// <summary>The authored hero sprite and tint for <paramref name="kind"/>, falling back to the
        /// shared starburst placeholder only for a kind with no authored art (there is none as of issue
        /// #283 — every <see cref="SpecialPieceKind"/> but <see cref="SpecialPieceKind.None"/> has one).</summary>
        private void ResolveSpecialPieceIcon(SpecialPieceKind kind, out Sprite icon, out Color tint)
        {
            switch (kind)
            {
                case SpecialPieceKind.Golden:
                    icon = _goldenIconSprite;
                    tint = GoldenIconTint;
                    break;
                case SpecialPieceKind.PiercingRocket:
                    icon = _piercingRocketIconSprite;
                    tint = PiercingRocketIconTint;
                    break;
                case SpecialPieceKind.DemolitionHammer:
                    icon = _demolitionHammerIconSprite;
                    tint = DemolitionHammerIconTint;
                    break;
                default:
                    icon = UiSpriteFactory.Starburst;
                    tint = Color.white;
                    break;
            }

            if (icon == null)
            {
                icon = UiSpriteFactory.Starburst;
                tint = Color.white;
            }
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("InfoPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            _chrome = InfoCardChrome.Build(panelRect, "InfoCard", _cardSize, _headerFontSize, _bodyFontSize);

            BuildHero();
            _demoStage = new InfoDemoStage(
                _chrome.DemoRootRect,
                new InfoDemoResources(
                    _boardView, _powerUpInventoryView, _localizationSystem, _holdSlotView, _coinTotalHudView),
                this.GetCancellationTokenOnDestroy());

            _panel = panelObject;
        }

        /// <summary>The icon content only — the ring, plate and its footprint come from
        /// <see cref="InfoCardChrome"/>. Parented into <see cref="InfoCardChrome.Handles.HeroContentRect"/>,
        /// which the chrome has already centred and sized on the hero plate.</summary>
        private void BuildHero()
        {
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(_chrome.HeroContentRect, false);
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(InfoCardChrome.HERO_CONTENT_SIZE, InfoCardChrome.HERO_CONTENT_SIZE);
            iconRect.anchoredPosition = Vector2.zero;
            _heroIconImage = iconObject.GetComponent<Image>();
            _heroIconImage.type = Image.Type.Simple;
            _heroIconImage.preserveAspect = true;
            _heroIconImage.color = Color.clear;
            _heroIconImage.raycastTarget = false;
        }
    }
}
