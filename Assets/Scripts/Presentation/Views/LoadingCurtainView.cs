using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Views.Shared;
using Mtafasahin.Reactive;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The loading curtain between the mode-select screen and gameplay. Choosing a mode kicks off a
    /// single-scene load whose first frames stall the main thread — the gameplay scope's construction,
    /// then the service warm-ups behind it — and until now that read as a frozen screen. This View
    /// drops a full-screen copy of the splash gradient with the chosen mode's tile animating on it the
    /// instant <see cref="GameModeChosenMessage"/> lands, survives the scene swap, and fades itself
    /// out once gameplay has actually drawn a frame underneath.
    /// <para>
    /// Self-contained on purpose: no Model, no System. It observes one message, one scene event and
    /// its own clock, and destroys itself at the end. It is the only object in the project that uses
    /// <see cref="Object.DontDestroyOnLoad"/> — it has to outlive the scene that owns its container —
    /// and it holds no reference into that container once shown, so nothing dangles after the swap.
    /// </para>
    /// <para>
    /// The tile and glyph are <see cref="ModePlateBuilder"/>'s own, built the way the mode buttons
    /// build theirs and painted from the active theme, so the curtain shows exactly the button the
    /// player just tapped. The glyph's parts are then animated per mode: the clock's hand sweeps,
    /// the infinity rings breathe out of phase, and the trail's stops pop in turn. The builder's
    /// localization and duration dependencies are injected only because its constructor requires
    /// them; the tile and glyph paths never touch them.
    /// </para>
    /// <para>
    /// Sits dormant in the mode-select scene as an active object with its Canvas disabled, rather
    /// than an inactive one: it has to be enabled for <see cref="Start"/> to subscribe.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
    public sealed class LoadingCurtainView : MonoBehaviour
    {
        /// <summary>The scene the curtain covers the load of — <c>ModeSelectSystem</c>'s target.</summary>
        private const string GAMEPLAY_SCENE_NAME = "SampleScene";

        /// <summary>Frames gameplay gets to draw under the curtain before the fade begins, so the
        /// fade reveals a settled board rather than the first half-built frame.</summary>
        private const int SETTLE_FRAMES = 2;

        private const float FADE_OUT_SECONDS = 0.25f;

        /// <summary>The tile's height as a share of the screen's, so it holds its proportion across
        /// device sizes (the approved mockup's 132px tile on an 844pt-tall phone).</summary>
        private const float TILE_SCREEN_HEIGHT_SHARE = 0.156f;

        /// <summary>Above every gameplay canvas, whatever order they sort at.</summary>
        private const int SORTING_ORDER = 1000;

        // Klasik: the hand sweeps one full turn this often.
        private const float CLOCK_REVOLUTION_SECONDS = 2.5f;

        // Şölen: each ring breathes 1.0 → 1.14 → 1.0 over one period, the second this far behind the first.
        private const float RING_PULSE_PERIOD_SECONDS = 1.4f;
        private const float RING_PULSE_PEAK_SCALE = 1.14f;
        private const float RING_PULSE_STAGGER_SECONDS = 0.35f;

        // Macera: the stops pop one after another along the trail, then the sequence restarts.
        private const int TRAIL_STOP_COUNT = 3;
        private const float TRAIL_POP_STAGGER_SECONDS = 0.55f;
        private const float TRAIL_POP_SECONDS = 0.3f;
        private const float TRAIL_POP_PEAK_SCALE = 1.4f;

        /// <summary>The trail glyph has two end stops; the curtain adds a third at the legs' apex so
        /// the pop reads as three steps along the path. Position derived from the legs' geometry.</summary>
        private const float TRAIL_APEX_DIAMETER = 14f;
        private static readonly Vector2 TrailApexPosition = new Vector2(0f, 18f);

        /// <summary>The splash's two brand blues, so the curtain reads as the screen it drops over.</summary>
        private static readonly Color TopColour = new Color32(21, 101, 192, 255);
        private static readonly Color BottomColour = new Color32(41, 182, 246, 255);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        [Header("Parts")]
        [Tooltip("Full-bleed image behind everything; its sprite is replaced with the splash gradient at runtime.")]
        [SerializeField] private Image _backgroundImage;

        [Tooltip("Centred, empty rect the chosen mode's tile is built into.")]
        [SerializeField] private RectTransform _iconRoot;

        private ISubscriber<GameModeChosenMessage> _modeChosenSubscriber;
        private SettingsModel _settingsModel;
        private LocalizationSystem _localizationSystem;
        private LocalizationModel _localizationModel;
        private TimedModeSystem _timedModeSystem;

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;

        private bool _isShowing;
        private bool _sceneLoadedHooked;
        private float _animationTime;
        private GameMode _mode;

        // Animated glyph parts, found once when the glyph is built. Which set is populated depends on the mode.
        private RectTransform _clockHand;
        private readonly RectTransform[] _ringDiscs = new RectTransform[2];
        private readonly RectTransform[] _ringHoles = new RectTransform[2];
        private readonly RectTransform[] _trailStops = new RectTransform[TRAIL_STOP_COUNT];

        [Inject]
        public void Construct(
            ISubscriber<GameModeChosenMessage> modeChosenSubscriber,
            SettingsModel settingsModel,
            LocalizationSystem localizationSystem,
            LocalizationModel localizationModel,
            TimedModeSystem timedModeSystem)
        {
            _modeChosenSubscriber = modeChosenSubscriber;
            _settingsModel = settingsModel;
            _localizationSystem = localizationSystem;
            _localizationModel = localizationModel;
            _timedModeSystem = timedModeSystem;
        }

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvasGroup = GetComponent<CanvasGroup>();

            _canvas.overrideSorting = true;
            _canvas.sortingOrder = SORTING_ORDER;

            // Dormant until a mode is chosen: nothing drawn, nothing blocked.
            _canvas.enabled = false;
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
        }

        private void Start()
        {
            if (_modeChosenSubscriber == null || _settingsModel == null || _localizationSystem == null
                || _localizationModel == null || _timedModeSystem == null)
            {
                Debug.LogError(
                    $"{nameof(LoadingCurtainView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _modeChosenSubscriber.Subscribe(OnModeChosen).AddTo(_disposables);
        }

        private void Update()
        {
            if (!_isShowing)
            {
                return;
            }

            // Unscaled: gameplay may boot paused or time-scaled, and the curtain's motion is not part of it.
            _animationTime += Time.unscaledDeltaTime;

            switch (_mode)
            {
                case GameMode.Timed:
                    AnimateClock();
                    break;
                case GameMode.Path:
                    AnimateTrail();
                    break;
                default:
                    AnimateRings();
                    break;
            }
        }

        private void OnDestroy()
        {
            UnhookSceneLoaded();
            _disposables.Dispose();
        }

        private void OnModeChosen(GameModeChosenMessage message)
        {
            if (_isShowing)
            {
                return;
            }

            _isShowing = true;
            _mode = message.Mode;

            // The message's own subscription dies with the container that owns it; from here on the
            // curtain listens only to the scene manager.
            _disposables.Dispose();

            BuildBackdrop();
            BuildIcon(message.Mode);

            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            _canvas.enabled = true;

            DontDestroyOnLoad(gameObject);
            HookSceneLoaded();
        }

        // ---------------------------------------------------------------------------- building

        private void BuildBackdrop()
        {
            var backgroundRect = (RectTransform)_backgroundImage.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            _backgroundImage.sprite = UiSpriteFactory.CreateVerticalGradient(BottomColour, TopColour);
            _backgroundImage.type = Image.Type.Simple;
            _backgroundImage.color = Color.white;
            _backgroundImage.raycastTarget = false;
        }

        /// <summary>The chosen mode's tile and glyph, exactly as its button wears them, in the active
        /// theme, scaled from the plates' 96px up to its share of the screen.</summary>
        private void BuildIcon(GameMode mode)
        {
            var plates = new ModePlateBuilder(_localizationSystem, _localizationModel, _timedModeSystem, null);
            int kind = ModePlateBuilder.ModeKind(mode);
            RectTransform tileRect = plates.BuildKindTile(_iconRoot, kind, Vector2.zero);
            GameObject glyph = plates.BuildModeGlyph(tileRect, mode, kind);
            plates.Repaint(_settingsModel.CurrentTheme.Value);

            var canvasRect = (RectTransform)_canvas.transform;
            float tileScale = (canvasRect.rect.height * TILE_SCREEN_HEIGHT_SHARE) / ModePlateBuilder.TILE_SIZE;
            _iconRoot.localScale = new Vector3(tileScale, tileScale, 1f);

            CacheGlyphParts(mode, glyph.transform);
            _animationTime = 0f;
        }

        /// <summary>Finds the glyph parts the animation moves, by the names the builder gives them.
        /// Done once here so <see cref="Update"/> never searches the hierarchy.</summary>
        private void CacheGlyphParts(GameMode mode, Transform glyph)
        {
            switch (mode)
            {
                case GameMode.Timed:
                    _clockHand = (RectTransform)glyph.Find("ClockHand");
                    // The builder offsets the hand so its base sits on the dial's centre; re-pivoting
                    // it on that base lets a plain rotation sweep it around the dial.
                    _clockHand.pivot = new Vector2(0.5f, 0f);
                    _clockHand.anchoredPosition = Vector2.zero;
                    break;

                case GameMode.Path:
                    // Left stop, then the apex the curtain adds where the legs meet, then the right stop.
                    _trailStops[0] = (RectTransform)glyph.Find("TrailStop_0");
                    _trailStops[1] = BuildTrailApex(glyph);
                    _trailStops[2] = (RectTransform)glyph.Find("TrailStop_1");
                    break;

                default:
                    // A disc and its cut-out hole share a centre, so scaling both by the same factor
                    // keeps the ring's thickness in proportion.
                    for (int ringIndex = 0; ringIndex < 2; ringIndex++)
                    {
                        _ringDiscs[ringIndex] = (RectTransform)glyph.Find(ringIndex == 0 ? "InfinityDisc_0" : "InfinityDisc_1");
                        _ringHoles[ringIndex] = (RectTransform)glyph.Find(ringIndex == 0 ? "InfinityHole_0" : "InfinityHole_1");
                    }
                    break;
            }
        }

        /// <summary>A third white stop at the trail's apex, drawn over the legs so the pop has three
        /// steps to walk. Same disc sprite the builder's stops use.</summary>
        private static RectTransform BuildTrailApex(Transform glyph)
        {
            var apexObject = new GameObject("TrailApex", typeof(RectTransform), typeof(Image));
            var apexRect = (RectTransform)apexObject.transform;
            apexRect.SetParent(glyph, false);
            apexRect.anchorMin = new Vector2(0.5f, 0.5f);
            apexRect.anchorMax = new Vector2(0.5f, 0.5f);
            apexRect.pivot = new Vector2(0.5f, 0.5f);
            apexRect.sizeDelta = new Vector2(TRAIL_APEX_DIAMETER, TRAIL_APEX_DIAMETER);
            apexRect.anchoredPosition = TrailApexPosition;

            var image = apexObject.GetComponent<Image>();
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
            return apexRect;
        }

        // ---------------------------------------------------------------------------- animating

        /// <summary>The hand sweeps clockwise, one turn per <see cref="CLOCK_REVOLUTION_SECONDS"/>.</summary>
        private void AnimateClock()
        {
            float angle = -(_animationTime / CLOCK_REVOLUTION_SECONDS) * 360f;
            _clockHand.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>Each ring breathes on a cosine, the second a beat behind the first.</summary>
        private void AnimateRings()
        {
            for (int ringIndex = 0; ringIndex < 2; ringIndex++)
            {
                float phase = (_animationTime - (ringIndex * RING_PULSE_STAGGER_SECONDS)) / RING_PULSE_PERIOD_SECONDS;
                float breath = 0.5f - (0.5f * Mathf.Cos(phase * Mathf.PI * 2f));
                float scale = 1f + ((RING_PULSE_PEAK_SCALE - 1f) * breath);
                var scaleVector = new Vector3(scale, scale, 1f);
                _ringDiscs[ringIndex].localScale = scaleVector;
                _ringHoles[ringIndex].localScale = scaleVector;
            }
        }

        /// <summary>The stops pop in turn along the trail — a short half-sine bump each, staggered —
        /// then the sequence loops.</summary>
        private void AnimateTrail()
        {
            float cycle = TRAIL_STOP_COUNT * TRAIL_POP_STAGGER_SECONDS;
            for (int stopIndex = 0; stopIndex < TRAIL_STOP_COUNT; stopIndex++)
            {
                float local = Mathf.Repeat(_animationTime - (stopIndex * TRAIL_POP_STAGGER_SECONDS), cycle);
                float scale = 1f;
                if (local < TRAIL_POP_SECONDS)
                {
                    scale = 1f + ((TRAIL_POP_PEAK_SCALE - 1f) * Mathf.Sin((local / TRAIL_POP_SECONDS) * Mathf.PI));
                }

                _trailStops[stopIndex].localScale = new Vector3(scale, scale, 1f);
            }
        }

        // ---------------------------------------------------------------------------- dismissing

        private void HookSceneLoaded()
        {
            if (_sceneLoadedHooked)
            {
                return;
            }

            _sceneLoadedHooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void UnhookSceneLoaded()
        {
            if (!_sceneLoadedHooked)
            {
                return;
            }

            _sceneLoadedHooked = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            if (scene.name != GAMEPLAY_SCENE_NAME)
            {
                return;
            }

            UnhookSceneLoaded();
            FadeOutAndDestroyAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid FadeOutAndDestroyAsync(CancellationToken cancellationToken)
        {
            await UniTask.DelayFrame(SETTLE_FRAMES, cancellationToken: cancellationToken);

            float elapsed = 0f;
            while (elapsed < FADE_OUT_SECONDS)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / FADE_OUT_SECONDS);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _isShowing = false;
            Destroy(gameObject);
        }
    }
}
