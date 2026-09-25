using System.Text;
using MustyBlockBlast.Gameplay;
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
    /// The lives section at the trailing end of the goal bar (issue #477, mockup 1A): a heart with the
    /// life count inside it and, while lives are below the refill cap, the "mm:ss" left to the next
    /// xx:00 refill underneath. Binds to <see cref="LivesModel"/> reactively — the countdown ticks
    /// because <see cref="LivesSystem"/> rewrites <see cref="LivesModel.SecondsUntilRefill"/> once a
    /// second, not because this view polls.
    /// <para>
    /// Path mode only: lives are a Path idea, so in Endless and Timed the section hides through its own
    /// CanvasGroup and hands its width back to the bar. A count above the cap (a coin pack, issue #479)
    /// shows as-is, e.g. "25"; the countdown is hidden at or above the cap, when no refill is coming.
    /// </para>
    /// <para>
    /// A fixed-width section: the countdown's digits are proportional, and re-measuring the bar every
    /// second as "10:59" became "10:58" would re-pack the chips beside it for nothing. Parented onto
    /// <see cref="ObjectiveIconContainerView.TrailingSlot"/> in <see cref="Start"/> rather than Awake,
    /// for the reason <see cref="LevelPathButtonView"/> is. Non-interactive throughout.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LivesHudView : MonoBehaviour
    {
        /// <summary>A deep red outline round the white count, so the digits read on both the heart's
        /// highlight and its shade.</summary>
        private static readonly Color CountOutlineColour = new Color(0.45f, 0.05f, 0.08f, 0.9f);

        private static readonly Vector2 CountOutlineDistance = new Vector2(2f, -2f);

        [Header("Layout")]
        [Tooltip("Width the section takes in the bar, in reference pixels. Fixed, so the ticking " +
            "countdown never re-packs the chips beside it.")]
        [SerializeField] private float _sectionWidth = 84f;

        [SerializeField] private float _sectionHeight = 80f;
        [SerializeField] private float _heartSize = 60f;

        [Tooltip("How far the heart rises off the bar's centre line while the countdown shows under it.")]
        [SerializeField] private float _heartLift = 11f;

        [Tooltip("Centre of the countdown relative to the bar's centre line.")]
        [SerializeField] private float _countdownY = -29f;

        [SerializeField] private int _countFontSize = 28;
        [SerializeField] private int _countdownFontSize = 22;

        [Header("Art")]
        [Tooltip("The chunky display face for the count and the countdown. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("Full-colour heart the count sits on (HudIcon_Heart).")]
        [SerializeField] private Sprite _heartSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        private LivesModel _livesModel;
        private GameModeSystem _gameModeSystem;
        private SettingsModel _settingsModel;
        private ObjectiveIconContainerView _objectiveIconContainerView;

        private RectTransform _rect;
        private CanvasGroup _group;
        private RectTransform _heartRect;
        private Text _countText;
        private Text _countdownText;
        private ThemeDefinition _currentTheme;

        /// <summary>Whether the section was showing at the last refresh, so the bar is only asked to
        /// re-pack on an actual change.</summary>
        private bool _isVisible;

        [Inject]
        public void Construct(
            LivesModel livesModel,
            GameModeSystem gameModeSystem,
            SettingsModel settingsModel,
            ObjectiveIconContainerView objectiveIconContainerView)
        {
            _livesModel = livesModel;
            _gameModeSystem = gameModeSystem;
            _settingsModel = settingsModel;
            _objectiveIconContainerView = objectiveIconContainerView;
        }

        private void Awake() => BuildSection();

        private void Start()
        {
            if (_livesModel == null || _gameModeSystem == null || _settingsModel == null
                || _objectiveIconContainerView == null)
            {
                Debug.LogError(
                    $"{nameof(LivesHudView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            AttachToGoalBar();

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _livesModel.CurrentLives.Subscribe(OnLivesChanged).AddTo(_disposables);
            _livesModel.SecondsUntilRefill.Subscribe(OnSecondsUntilRefillChanged).AddTo(_disposables);
            _gameModeSystem.CurrentMode.Subscribe(_ => RefreshVisibility()).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;
            PaintCountdown();
        }

        private void OnLivesChanged(int lives)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(lives);
            _countText.text = _stringBuilder.ToString();
        }

        /// <summary>
        /// Paints the countdown as <c>mm:ss</c>, or hides it at 0 — the model's "at or above the cap"
        /// signal. Not a String Table entry, for the reason <see cref="TimerHudView"/>'s clock is not: a
        /// zero-padded clock is a universal numeric format with nothing in it to translate. Built on the
        /// cached builder, so a per-second repaint allocates only the label's string.
        /// </summary>
        private void OnSecondsUntilRefillChanged(int secondsUntilRefill)
        {
            bool hasCountdown = secondsUntilRefill > 0;
            _heartRect.anchoredPosition = new Vector2(0f, hasCountdown ? _heartLift : 0f);

            if (!hasCountdown)
            {
                _countdownText.text = string.Empty;
                return;
            }

            _stringBuilder.Clear();
            AppendPadded(secondsUntilRefill / 60);
            _stringBuilder.Append(':');
            AppendPadded(secondsUntilRefill % 60);
            _countdownText.text = _stringBuilder.ToString();
            PaintCountdown();
        }

        private void PaintCountdown()
        {
            if (_currentTheme != null)
            {
                _countdownText.color = _currentTheme.Ink;
            }
        }

        /// <summary>Path mode only, through the section's own group, which ignores the bar's.</summary>
        private void RefreshVisibility()
        {
            bool isVisible = _gameModeSystem.CurrentMode.Value == GameMode.Path;
            _group.alpha = isVisible ? 1f : 0f;

            if (isVisible == _isVisible)
            {
                return;
            }

            _isVisible = isVisible;

            // The bar packs its trailing group from the children that are showing, so the chips must be
            // re-fitted whenever the section comes or goes.
            _objectiveIconContainerView.NotifySlotsChanged();
        }

        private void AppendPadded(int value)
        {
            if (value < 10)
            {
                _stringBuilder.Append('0');
            }

            _stringBuilder.Append(value);
        }

        /// <summary>
        /// Moves the section under the bar's trailing slot. Last sibling on purpose: the bar places its
        /// trailing children right to left in sibling order, so the lives are always the rightmost.
        /// </summary>
        private void AttachToGoalBar()
        {
            _rect.SetParent(_objectiveIconContainerView.TrailingSlot, false);
            _rect.SetAsLastSibling();
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

            // Every size here is in canvas reference units, so the section owns its own scale rather
            // than inheriting whatever the scene object happened to be created with.
            _rect.localScale = Vector3.one;

            // The bar dims for a 2× window; the lives are not a goal and stay on screen through it, the
            // way the level section does.
            if (!TryGetComponent(out _group))
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _group.ignoreParentGroups = true;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _group.alpha = 0f;

            // Children anchor to the section's centre — anchors are relative to the parent's rect, not
            // its pivot — so they sit in its middle although the section pivots on its right edge.
            _heartRect = HudChrome.CreateRect(_rect, "Heart", new Vector2(_heartSize, _heartSize), Vector2.zero);
            Image heartImage = HudChrome.BuildGlyph(
                _heartRect, "Icon", _heartSprite, new Vector2(_heartSize, _heartSize), Vector2.zero);
            heartImage.color = _heartSprite != null ? Color.white : Color.clear;

            // Nudged up a touch: a heart's visual mass sits above its box's centre, its point below.
            _countText = UiTextFactory.Create(
                _heartRect, "Count", _countFontSize, FontStyle.Normal, Color.white, _displayFont);
            RectTransform countRect = (RectTransform)_countText.transform;
            countRect.sizeDelta = new Vector2(_heartSize, _heartSize);
            countRect.anchoredPosition = new Vector2(0f, _heartSize * 0.06f);

            Outline countOutline = _countText.gameObject.AddComponent<Outline>();
            countOutline.effectColor = CountOutlineColour;
            countOutline.effectDistance = CountOutlineDistance;

            _countdownText = HudChrome.CreateLabel(
                _rect, "Countdown", _countdownFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, _countdownY), _displayFont);
        }
    }
}
