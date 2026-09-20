using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Celebration burst for multi-line clears: a word ("NICE!" / "GREAT!" / "AMAZING!"), a soft glow
    /// and a ring of small coloured squares. Single-line clears are ignored. Purely visual — it only
    /// reads <see cref="LinesClearedMessage.LineCount"/> and never touches game state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LineClearBurstView : MonoBehaviour
    {
        private const int PARTICLE_CAPACITY = 24;
        private const int RAY_COUNT = 10;
        private const float RAY_LENGTH = 360f;
        private const float RAY_WIDTH = 7f;

        // Burst word is always a fixed white fill (see AnimateText) so its outline must stay a fixed
        // dark colour too, independent of theme.Ink — a light-ink theme (e.g. Kış) would otherwise
        // outline white text in near-white and make it unreadable.
        private static readonly Color TextOutlineColor = new Color(0.1686f, 0.1529f, 0.20f, 1f);

        // Phase fractions of a tier's total duration (0.15 / 0.55 / 0.30 == the approved NICE timing).
        private const float GROW_FRACTION = 0.15f;
        private const float SETTLE_FRACTION = 0.10f;
        private const float FADE_FRACTION = 0.30f;
        private const float TRAVEL_FRACTION = 0.55f;
        private const float FLASH_IN_FRACTION = 0.07f;
        private const float FLASH_OUT_FRACTION = 0.33f;

        private static readonly BurstTier[] Tiers =
        {
            // word, font, outline, glow colour id, glow alpha, glow size, rings, particles,
            // particle size, size step, first radius, radius step, duration, flash
            new BurstTier("NICE!", 58, 1.5f, 1, 0.30f, 430f, 1, 8, 18f, 0f, 200f, 0f, 1.0f, false),
            new BurstTier("GREAT!", 66, 1.75f, 3, 0.38f, 540f, 2, 16, 14f, 6f, 180f, 115f, 1.2f, false),
            new BurstTier("AMAZING!", 76, 2f, 3, 0.42f, 640f, 3, 24, 14f, 5f, 165f, 105f, 1.4f, true),
        };

        [Header("Layout")]
        [Tooltip("Centre of the burst, in canvas space. Should match the board centre.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 120f);

        [Header("Timing")]
        [Tooltip("Multiplies every tier's duration. 1 = the approved timings.")]
        [SerializeField] private float _durationScale = 1f;

        [Header("Particles")]
        [Tooltip("Multiplies every tier's particle size. 1 = the approved mockup sizes.")]
        [SerializeField] private float _particleScale = 1f;

        [Header("Flash (AMAZING only)")]
        [SerializeField] private float _flashAlpha = 0.28f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private readonly Vector2[] _particleDirections = new Vector2[PARTICLE_CAPACITY];
        private readonly float[] _particleDistances = new float[PARTICLE_CAPACITY];
        private readonly float[] _particleSpins = new float[PARTICLE_CAPACITY];
        private readonly Color[] _particleColours = new Color[PARTICLE_CAPACITY];

        private ISubscriber<LinesClearedMessage> _linesClearedSubscriber;
        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;
        private SettingsModel _settingsModel;
        private ThemeDefinition _currentTheme;

        private GameObject _layerObject;
        private RectTransform _centreRect;
        private RectTransform _glowRect;
        private Image _glowImage;
        private RectTransform _textRect;
        private Text _text;
        private Outline _textOutline;
        private GameObject _flashObject;
        private CanvasGroup _flashGroup;
        private Image _flashImage;
        private GameObject _raysObject;
        private RectTransform[] _rayRects;
        private Image[] _rayImages;
        private BlockBurstPool _pool;

        private CancellationToken _destroyToken;
        private CancellationTokenSource _burstCts;
        private int _generation;
        private int _activeParticleCount;
        private bool _isDestroyed;

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            ISubscriber<LinesClearedMessage> linesClearedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _settingsModel = settingsModel;
            _linesClearedSubscriber = linesClearedSubscriber;
            _gameOverSubscriber = gameOverSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
        }

        private void Awake()
        {
            _destroyToken = this.GetCancellationTokenOnDestroy();

            BuildHierarchy();
            _layerObject.SetActive(false);
        }

        private void Start()
        {
            if (_linesClearedSubscriber == null || _settingsModel == null)
            {
                Debug.LogError(
                    $"{nameof(LineClearBurstView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Bursts are spawned on demand, so keeping the theme (and the cached particle colours)
            // current is enough — a burst always renders with the theme selected when it starts.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _linesClearedSubscriber.Subscribe(OnLinesCleared).AddTo(_disposables);
            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _disposables.Dispose();
            CancelBurst();
        }

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        private void OnLinesCleared(LinesClearedMessage message)
        {
            int lineCount = message.LineCount;
            if (lineCount < 2 || _currentTheme == null)
            {
                return;
            }

            PlayAsync(Mathf.Clamp(lineCount - 2, 0, Tiers.Length - 1)).Forget();
        }

        private void OnGameOver(GameOverMessage message) => StopBurst();

        private void OnRunStarted(RunStartedMessage message) => StopBurst();

        /// <summary>Cancels whatever is playing and hides everything immediately.</summary>
        private void StopBurst()
        {
            CancelBurst();

            // Bump the generation so the cancelled run's cleanup cannot touch shared state later.
            _generation++;
            Hide();
        }

        private void CancelBurst()
        {
            if (_burstCts == null)
            {
                return;
            }

            _burstCts.Cancel();
            _burstCts.Dispose();
            _burstCts = null;
        }

        private async UniTaskVoid PlayAsync(int tierIndex)
        {
            // A new burst always replaces the running one (acceptance criterion 6).
            CancelBurst();

            _burstCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
            CancellationToken token = _burstCts.Token;
            int generation = ++_generation;

            BurstTier tier = Tiers[tierIndex];
            float duration = Mathf.Max(0.05f, tier.Duration * _durationScale);

            Setup(tier);

            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    Animate(tier, elapsed / duration);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer burst, a game over, a restart or object destruction.
            }
            finally
            {
                // Only clean up when nothing newer has taken over, otherwise this would wipe the
                // freshly-set start state of the burst that replaced this one.
                if (!_isDestroyed && generation == _generation)
                {
                    Hide();
                }
            }
        }

        private void Setup(BurstTier tier)
        {
            _layerObject.SetActive(true);
            transform.SetAsLastSibling();

            _centreRect.anchoredPosition = _anchoredPosition;

            _text.text = tier.Word;
            _text.fontSize = tier.FontSize;
            _textOutline.effectDistance = new Vector2(tier.OutlineWidth, -tier.OutlineWidth);

            _glowRect.sizeDelta = new Vector2(tier.GlowSize, tier.GlowSize);
            _glowImage.color = TintedGlow(tier, 0f);

            _flashObject.SetActive(tier.HasFlash);
            _flashGroup.alpha = 0f;
            _raysObject.SetActive(tier.HasFlash);

            LayOutParticles(tier);
            _pool.Activate(tier.ParticleCount);
            _activeParticleCount = tier.ParticleCount;
        }

        private void LayOutParticles(BurstTier tier)
        {
            int perRing = Mathf.Max(1, tier.ParticleCount / Mathf.Max(1, tier.RingCount));
            int index = 0;

            for (int ring = 0; ring < tier.RingCount; ring++)
            {
                float radius = tier.FirstRadius + (ring * tier.RadiusStep);
                float size = (tier.ParticleSize + (ring * tier.ParticleSizeStep)) * Mathf.Max(0.1f, _particleScale);

                // Offset every other ring by half a step so the rings do not line up.
                float angleStep = 360f / perRing;
                float angleOffset = (ring * angleStep * 0.5f) + (ring * 7f);

                for (int i = 0; i < perRing && index < tier.ParticleCount; i++)
                {
                    float angle = ((angleOffset + (i * angleStep)) * Mathf.Deg2Rad);
                    _particleDirections[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    _particleDistances[index] = radius;
                    _particleSpins[index] = ((index % 2 == 0) ? 1f : -1f) * (90f + (index * 13f));

                    RectTransform rect = _pool.GetRect(index);
                    rect.sizeDelta = new Vector2(size, size);
                    rect.anchoredPosition = Vector2.zero;
                    rect.localRotation = Quaternion.identity;

                    _pool.GetImage(index).color = _particleColours[index];
                    index++;
                }
            }
        }

        private void Animate(BurstTier tier, float normalisedTime)
        {
            float fadeStart = 1f - FADE_FRACTION;
            float fade = normalisedTime < fadeStart
                ? 1f
                : 1f - Mathf.Clamp01((normalisedTime - fadeStart) / FADE_FRACTION);

            AnimateText(normalisedTime, fade);
            AnimateGlow(tier, normalisedTime, fade);
            AnimateParticles(normalisedTime, fade);

            if (tier.HasFlash)
            {
                AnimateFlash(normalisedTime);
                AnimateRays(tier, normalisedTime, fade);
            }
        }

        private void AnimateText(float normalisedTime, float fade)
        {
            float scale;
            if (normalisedTime < GROW_FRACTION)
            {
                scale = Mathf.Lerp(0.55f, 1.08f, EaseOutCubic(normalisedTime / GROW_FRACTION));
            }
            else if (normalisedTime < GROW_FRACTION + SETTLE_FRACTION)
            {
                scale = Mathf.Lerp(1.08f, 1f, (normalisedTime - GROW_FRACTION) / SETTLE_FRACTION);
            }
            else
            {
                scale = 1f + (0.06f * (normalisedTime - GROW_FRACTION - SETTLE_FRACTION));
            }

            _textRect.localScale = new Vector3(scale, scale, 1f);

            Color fill = Color.white;
            fill.a = fade;
            _text.color = fill;

            Color outline = TextOutlineColor;
            outline.a = fade;
            _textOutline.effectColor = outline;
        }

        private void AnimateGlow(BurstTier tier, float normalisedTime, float fade)
        {
            float grow = Mathf.Clamp01(normalisedTime / TRAVEL_FRACTION);
            float scale = Mathf.Lerp(0.45f, 1f, EaseOutCubic(grow));
            _glowRect.localScale = new Vector3(scale, scale, 1f);
            _glowImage.color = TintedGlow(tier, fade);
        }

        private void AnimateParticles(float normalisedTime, float fade)
        {
            float travel = EaseOutCubic(Mathf.Clamp01(normalisedTime / TRAVEL_FRACTION));

            for (int i = 0; i < _activeParticleCount; i++)
            {
                RectTransform rect = _pool.GetRect(i);
                Vector2 direction = _particleDirections[i];
                float distance = _particleDistances[i] * travel;
                rect.anchoredPosition = new Vector2(direction.x * distance, direction.y * distance);
                rect.localRotation = Quaternion.Euler(0f, 0f, _particleSpins[i] * travel);

                float scale = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(normalisedTime / GROW_FRACTION));
                rect.localScale = new Vector3(scale, scale, 1f);

                Color colour = _particleColours[i];
                colour.a = fade;
                _pool.GetImage(i).color = colour;
            }
        }

        private void AnimateFlash(float normalisedTime)
        {
            float alpha = normalisedTime < FLASH_IN_FRACTION
                ? _flashAlpha * (normalisedTime / FLASH_IN_FRACTION)
                : _flashAlpha * (1f - Mathf.Clamp01((normalisedTime - FLASH_IN_FRACTION) / FLASH_OUT_FRACTION));

            _flashGroup.alpha = alpha;
        }

        private void AnimateRays(BurstTier tier, float normalisedTime, float fade)
        {
            float grow = EaseOutCubic(Mathf.Clamp01(normalisedTime / TRAVEL_FRACTION));
            float length = Mathf.Lerp(0.25f, 1f, grow);
            float alpha = fade * Mathf.Clamp01(1f - (grow * 0.85f)) * 0.85f;

            Color colour = _currentTheme.GetFill(tier.GlowColourId);
            colour.a = alpha;

            for (int i = 0; i < _rayRects.Length; i++)
            {
                _rayRects[i].localScale = new Vector3(1f, length, 1f);
                _rayImages[i].color = colour;
            }
        }

        private Color TintedGlow(BurstTier tier, float fade)
        {
            Color colour = _currentTheme.GetFill(tier.GlowColourId);
            colour.a = tier.GlowAlpha * fade;
            return colour;
        }

        private void Hide()
        {
            _pool.DeactivateAll();
            _activeParticleCount = 0;
            _flashGroup.alpha = 0f;
            _flashObject.SetActive(false);
            _raysObject.SetActive(false);
            _layerObject.SetActive(false);
        }

        private void BuildHierarchy()
        {
            var rootRect = (RectTransform)transform;
            Stretch(rootRect);

            // Own nested Canvas: the per-frame particle/text changes below only rebuild this canvas,
            // not the shared UICanvas that holds the board and the score.
            var layerObject = new GameObject("BurstLayer", typeof(RectTransform), typeof(Canvas));
            _layerObject = layerObject;
            var layerRect = (RectTransform)layerObject.transform;
            layerRect.SetParent(rootRect, false);
            Stretch(layerRect);

            BuildFlash(layerRect);

            var centreObject = new GameObject("BurstCentre", typeof(RectTransform));
            _centreRect = (RectTransform)centreObject.transform;
            _centreRect.SetParent(layerRect, false);
            _centreRect.anchorMin = new Vector2(0.5f, 0.5f);
            _centreRect.anchorMax = new Vector2(0.5f, 0.5f);
            _centreRect.pivot = new Vector2(0.5f, 0.5f);
            _centreRect.sizeDelta = Vector2.zero;
            _centreRect.anchoredPosition = _anchoredPosition;

            BuildGlow(_centreRect);
            BuildRays(_centreRect);
            BuildParticles(_centreRect);
            BuildText(_centreRect);
        }

        private void BuildFlash(RectTransform parent)
        {
            var flashObject = new GameObject("Flash", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            _flashObject = flashObject;
            var flashRect = (RectTransform)flashObject.transform;
            flashRect.SetParent(parent, false);
            Stretch(flashRect);

            _flashGroup = flashObject.GetComponent<CanvasGroup>();
            _flashGroup.alpha = 0f;
            _flashGroup.interactable = false;
            _flashGroup.blocksRaycasts = false;

            _flashImage = flashObject.GetComponent<Image>();
            _flashImage.raycastTarget = false;

            flashObject.SetActive(false);
        }

        private void BuildGlow(RectTransform parent)
        {
            var glowObject = new GameObject("Glow", typeof(RectTransform), typeof(Image));
            _glowRect = (RectTransform)glowObject.transform;
            _glowRect.SetParent(parent, false);
            _glowRect.anchorMin = new Vector2(0.5f, 0.5f);
            _glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            _glowRect.pivot = new Vector2(0.5f, 0.5f);

            _glowImage = glowObject.GetComponent<Image>();
            _glowImage.sprite = UiSpriteFactory.RadialGlow;
            _glowImage.type = Image.Type.Simple;
            _glowImage.raycastTarget = false;
        }

        private void BuildRays(RectTransform parent)
        {
            var raysObject = new GameObject("Rays", typeof(RectTransform));
            _raysObject = raysObject;
            var raysRect = (RectTransform)raysObject.transform;
            raysRect.SetParent(parent, false);
            raysRect.anchorMin = new Vector2(0.5f, 0.5f);
            raysRect.anchorMax = new Vector2(0.5f, 0.5f);
            raysRect.pivot = new Vector2(0.5f, 0.5f);
            raysRect.sizeDelta = Vector2.zero;

            _rayRects = new RectTransform[RAY_COUNT];
            _rayImages = new Image[RAY_COUNT];

            for (int i = 0; i < RAY_COUNT; i++)
            {
                var rayObject = new GameObject($"Ray_{i}", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)rayObject.transform;
                rect.SetParent(raysRect, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(RAY_WIDTH, RAY_LENGTH);
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.Euler(0f, 0f, i * (360f / RAY_COUNT));

                var image = rayObject.GetComponent<Image>();
                image.sprite = UiSpriteFactory.RoundedSquare;
                image.type = Image.Type.Simple;
                image.raycastTarget = false;

                _rayRects[i] = rect;
                _rayImages[i] = image;
            }

            raysObject.SetActive(false);
        }

        private void BuildParticles(RectTransform parent)
        {
            var particlesObject = new GameObject("Particles", typeof(RectTransform));
            var particlesRect = (RectTransform)particlesObject.transform;
            particlesRect.SetParent(parent, false);
            particlesRect.anchorMin = new Vector2(0.5f, 0.5f);
            particlesRect.anchorMax = new Vector2(0.5f, 0.5f);
            particlesRect.pivot = new Vector2(0.5f, 0.5f);
            particlesRect.sizeDelta = Vector2.zero;

            _pool = new BlockBurstPool(particlesRect, PARTICLE_CAPACITY, UiSpriteFactory.RoundedSquare);
        }

        private void BuildText(RectTransform parent)
        {
            _text = UiTextFactory.Create(parent, "BurstWord", Tiers[0].FontSize, FontStyle.Bold, Color.white);
            _textRect = (RectTransform)_text.transform;
            _textOutline = _text.gameObject.AddComponent<Outline>();
            _textOutline.useGraphicAlpha = false;
        }

        /// <summary>Adopts a new theme. Nothing is repainted retroactively: a burst lives for about a
        /// second, so the next one simply starts in the new theme.</summary>
        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            _flashImage.color = theme.CardBackground;
            _textOutline.effectColor = TextOutlineColor;
            CacheParticleColours();
        }

        private void CacheParticleColours()
        {
            for (int i = 0; i < PARTICLE_CAPACITY; i++)
            {
                int colourId = (i % ThemeDefinition.KIND_COUNT) + 1;
                _particleColours[i] =
                    (i % 2 == 0) ? _currentTheme.GetFill(colourId) : _currentTheme.GetShade(colourId);
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Frozen per-tier presentation constants. Three fixed tiers — no asset needed.</summary>
        private readonly struct BurstTier
        {
            internal BurstTier(
                string word,
                int fontSize,
                float outlineWidth,
                int glowColourId,
                float glowAlpha,
                float glowSize,
                int ringCount,
                int particleCount,
                float particleSize,
                float particleSizeStep,
                float firstRadius,
                float radiusStep,
                float duration,
                bool hasFlash)
            {
                Word = word;
                FontSize = fontSize;
                OutlineWidth = outlineWidth;
                GlowColourId = glowColourId;
                GlowAlpha = glowAlpha;
                GlowSize = glowSize;
                RingCount = ringCount;
                ParticleCount = particleCount;
                ParticleSize = particleSize;
                ParticleSizeStep = particleSizeStep;
                FirstRadius = firstRadius;
                RadiusStep = radiusStep;
                Duration = duration;
                HasFlash = hasFlash;
            }

            internal string Word { get; }

            internal int FontSize { get; }

            internal float OutlineWidth { get; }

            internal int GlowColourId { get; }

            internal float GlowAlpha { get; }

            internal float GlowSize { get; }

            internal int RingCount { get; }

            internal int ParticleCount { get; }

            internal float ParticleSize { get; }

            internal float ParticleSizeStep { get; }

            internal float FirstRadius { get; }

            internal float RadiusStep { get; }

            internal float Duration { get; }

            internal bool HasFlash { get; }
        }
    }
}
