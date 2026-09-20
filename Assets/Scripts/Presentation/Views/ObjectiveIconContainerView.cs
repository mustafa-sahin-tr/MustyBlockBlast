using System.Collections.Generic;
using System.Text;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
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
    /// The run's objectives as the goal row under the score card (issue #265): a "goal" caption,
    /// then one chip per objective the current level asks for — a
    /// card-coloured pill holding a kind-tinted plate with the objective's silhouette, a "2/5" counter
    /// in the display face, and a green tick disc once that objective is done.
    /// <para>
    /// A level may carry several objectives at once (<see cref="ObjectiveModel.TrackedObjectives"/>),
    /// and a sentence per objective would not fit the HUD — a chip does, and the full wording is one
    /// tap away in <see cref="ObjectiveInfoPopupView"/>.
    /// </para>
    /// <para>
    /// The row is a fixed size whatever it holds. Chips are built once and shown or hidden by colour
    /// alone — toggling their GameObjects would rebuild the shared canvas mesh every time an objective
    /// appeared or cleared, which is the same reason <see cref="PowerUpInventoryView"/> dims its strip
    /// rather than deactivating it. A chip's silhouette is the one thing rebuilt rather than repainted,
    /// because which one it wears depends on the objective's type — and that only changes on a level
    /// change, never on a progress tick.
    /// </para>
    /// <para>
    /// The row also yields its band to <see cref="DoubleMultiplierHudView"/> for the fifteen seconds
    /// a 2× window is open, dimming itself rather than moving: the goals are still tracked, they are
    /// just not the thing the player needs to see right then.
    /// </para>
    /// <para>
    /// Every refresh re-reads the model rather than accumulating message payloads, so the row cannot
    /// drift from what it is showing: the progress and completion messages only say "this one moved",
    /// and a run reset — which resets progress without publishing anything — is picked up from
    /// <see cref="RunStartedMessage"/> the same way.
    /// </para>
    /// <para>
    /// Goals belong to Path mode only (issue #269): in Endless and Timed the row hides itself whatever
    /// the model tracks, and the level pill in its trailing slot follows the same rule on its own
    /// group. <see cref="CentreSlot"/> stays usable while the row is hidden, so the band it leaves
    /// empty in Timed mode can host the streak pill.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveIconContainerView : MonoBehaviour
    {
        /// <summary>
        /// Chips the row can draw. Five is headroom rather than a target: no level authors more than a
        /// couple of objectives today, and a row of pills would run off a narrow device well before it
        /// held many more. A level authored with more simply shows the first five — a truncated row
        /// reads better than one that overflows the screen.
        /// </summary>
        private const int MAX_SLOT_COUNT = 5;

        /// <summary>Which theme kind each chip's plate takes, by slot: the mockup's teal, purple, gold
        /// order first, so three chips on the spring theme match the design exactly.</summary>
        private static readonly int[] KindBySlot = { 2, 4, 3, 1, 5 };

        /// <summary>Which theme kind the tick disc takes: the fifth kind — every season's green.</summary>
        private const int CHECK_KIND = 5;

        /// <summary>Corner radius of a chip's inner kind-tinted plate: the mockup's 9px on a 30px plate.</summary>
        private const float ICON_PLATE_RADIUS = 20f;

        [Header("Layout")]
        [Tooltip("Centre of the row, in reference pixels from the canvas centre. The band under the score card.")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, 590f);

        [SerializeField] private Vector2 _rowSize = new Vector2(960f, 80f);
        [SerializeField] private float _chipHeight = 80f;
        [SerializeField] private float _iconPlateSize = 66f;
        [SerializeField] private float _iconSize = 36f;
        [SerializeField] private float _checkSize = 40f;
        [SerializeField] private float _chipPaddingLeft = 11f;
        [SerializeField] private float _chipPaddingRight = 22f;
        [SerializeField] private float _chipGap = 13f;

        [Tooltip("Gap between the caption and the first chip, and between chips.")]
        [SerializeField] private float _chipSpacing = 18f;

        [Tooltip("How far in from the row's right edge the trailing group ends, in reference pixels: " +
            "room for the level pill's star badge, which overhangs the pill's corner by its offset plus " +
            "half its diameter, to stay inside the score card's edge (issue #269).")]
        [SerializeField] private float _trailingInset = 30f;

        [SerializeField] private int _progressFontSize = 38;
        [SerializeField] private int _captionFontSize = 28;

        [Header("Art")]
        [Tooltip("The chunky display face for the counters. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("The heavy label face for the small uppercase caption. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _labelFont;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);
        private readonly Chip[] _chips = new Chip[MAX_SLOT_COUNT];

        private ObjectiveModel _objectiveModel;
        private SettingsModel _settingsModel;
        private ObjectiveIconCatalog _iconCatalog;
        private DoubleMultiplierModel _doubleMultiplierModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private GameModeSystem _gameModeSystem;
        private ISubscriber<ObjectiveProgressChangedMessage> _progressChangedSubscriber;
        private ISubscriber<ObjectiveCompletedMessage> _completedSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private RectTransform _rowRect;
        private RectTransform _trailingSlot;
        private RectTransform _centreSlot;
        private Text _captionText;
        private RectTransform _captionRect;
        private ThemeDefinition _currentTheme;

        /// <summary>How many chips are currently showing an objective. Read by the hit test so a tap on
        /// a hidden chip's rectangle cannot open a popup for an objective that is not there.</summary>
        private int _visibleSlotCount;

        /// <summary>True while a 2× window has the band; the row stays dimmed whatever it holds.</summary>
        private bool _isYieldingToMultiplier;

        /// <summary>Width of the trailing group as last laid out, gaps included; zero when it is empty.
        /// The chips may not run into it.</summary>
        private float _trailingWidth;

        /// <summary>One built chip. Rebuilt never — except its silhouette, on a level change.</summary>
        private sealed class Chip
        {
            internal Chip(
                RectTransform root,
                Image shadowImage,
                Image plateImage,
                RectTransform iconPlateRect,
                Image iconPlateImage,
                RectTransform glyphRoot,
                Image iconImage,
                Text progressText,
                RectTransform checkRect,
                Image checkDisc,
                Image checkMark)
            {
                Root = root;
                ShadowImage = shadowImage;
                PlateImage = plateImage;
                IconPlateRect = iconPlateRect;
                IconPlateImage = iconPlateImage;
                GlyphRoot = glyphRoot;
                IconImage = iconImage;
                ProgressText = progressText;
                ProgressRect = (RectTransform)progressText.transform;
                CheckRect = checkRect;
                CheckDisc = checkDisc;
                CheckMark = checkMark;
                GlyphInkImages = new List<Image>(4);
                GlyphCoreImages = new List<Image>(2);
                HasGlyph = false;
            }

            internal RectTransform Root { get; }

            internal Image ShadowImage { get; }

            internal Image PlateImage { get; }

            internal RectTransform IconPlateRect { get; }

            internal Image IconPlateImage { get; }

            internal RectTransform GlyphRoot { get; }

            /// <summary>The authored silhouette, when the catalog has one for the chip's type. Listed
            /// among <see cref="GlyphInkImages"/> while in use so it is tinted like any other ink.</summary>
            internal Image IconImage { get; }

            internal Text ProgressText { get; }

            internal RectTransform ProgressRect { get; }

            internal RectTransform CheckRect { get; }

            internal Image CheckDisc { get; }

            internal Image CheckMark { get; }

            internal List<Image> GlyphInkImages { get; }

            internal List<Image> GlyphCoreImages { get; }

            /// <summary>The type the built glyph draws. Meaningless while <see cref="HasGlyph"/> is
            /// false, which is why the two are read together rather than using a sentinel enum value.</summary>
            internal ObjectiveType GlyphType { get; set; }

            internal bool HasGlyph { get; set; }
        }

        /// <summary>
        /// A zero-width rect pinned to the row's right end: the trailing group. Its children are laid
        /// out right to left in sibling order, last sibling flush with the row's edge, by
        /// <see cref="NotifyTrailingChanged"/>. <see cref="LevelPathButtonView"/> lives here. Built in
        /// Awake, so it is safe to parent onto from any sibling's Start.
        /// </summary>
        internal RectTransform TrailingSlot => _trailingSlot;

        /// <summary>
        /// A zero-sized anchor at the row's centre for a guest that should sit in the goals band
        /// while the row itself is hidden — <see cref="StreakPillView"/> in Timed mode. The guest must
        /// ignore parent groups (both do), since this slot is under the row's CanvasGroup.
        /// </summary>
        internal RectTransform CentreSlot => _centreSlot;

        /// <summary>
        /// Called by a trailing child whenever it is added, shown, hidden or re-measured. Re-packs the
        /// trailing group against the row's right edge and repaints the chips, since how many of them
        /// fit depends on how much of the row the group takes.
        /// </summary>
        internal void NotifyTrailingChanged()
        {
            LayOutTrailingGroup();
            Refresh();
        }

        [Inject]
        public void Construct(
            ObjectiveModel objectiveModel,
            SettingsModel settingsModel,
            ObjectiveIconCatalog iconCatalog,
            DoubleMultiplierModel doubleMultiplierModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            GameModeSystem gameModeSystem,
            ISubscriber<ObjectiveProgressChangedMessage> progressChangedSubscriber,
            ISubscriber<ObjectiveCompletedMessage> completedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _objectiveModel = objectiveModel;
            _settingsModel = settingsModel;
            _iconCatalog = iconCatalog;
            _doubleMultiplierModel = doubleMultiplierModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _gameModeSystem = gameModeSystem;
            _progressChangedSubscriber = progressChangedSubscriber;
            _completedSubscriber = completedSubscriber;
            _runStartedSubscriber = runStartedSubscriber;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();

            // Unity allows several CanvasGroups on one object and then picks between them opaquely, so
            // an existing one is adopted rather than shadowed by a second.
            if (!TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            BuildRow();
            SetVisible(false);
        }

        private void Start()
        {
            if (_objectiveModel == null || _settingsModel == null || _iconCatalog == null
                || _doubleMultiplierModel == null
                || _localizationModel == null || _localizationSystem == null || _gameModeSystem == null
                || _progressChangedSubscriber == null || _completedSubscriber == null
                || _runStartedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(ObjectiveIconContainerView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Subscribed first so _currentTheme is set before the first Refresh paints anything.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _localizationModel.CurrentLocale.Subscribe(_ => Refresh()).AddTo(_disposables);
            _gameModeSystem.CurrentMode.Subscribe(_ => Refresh()).AddTo(_disposables);
            _doubleMultiplierModel.RemainingSeconds.Subscribe(OnMultiplierRemainingChanged).AddTo(_disposables);
            _progressChangedSubscriber.Subscribe(OnObjectiveProgressChanged).AddTo(_disposables);
            _completedSubscriber.Subscribe(OnObjectiveCompleted).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);

            Refresh();
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>
        /// Resolves a tap to the objective whose chip it landed on, if any. Called by
        /// <see cref="BoardInputView"/>, which owns pointer input — this scene has no EventSystem, and
        /// every Image here has its raycast target off.
        /// <para>
        /// Only populated chips answer: the hidden ones are transparent but still occupy their
        /// rectangle, and a tap on one must not open a popup about an objective that is not there. A
        /// dimmed row (see <see cref="_isYieldingToMultiplier"/>) does not answer either.
        /// </para>
        /// </summary>
        internal bool TryGetTappedObjectiveIndex(Vector2 screenPosition, out int objectiveIndex)
        {
            objectiveIndex = -1;

            if (_visibleSlotCount <= 0 || _isYieldingToMultiplier)
            {
                return false;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            for (int slotIndex = 0; slotIndex < _visibleSlotCount; slotIndex++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(
                    _chips[slotIndex].Root, screenPosition, eventCamera))
                {
                    objectiveIndex = slotIndex;
                    return true;
                }
            }

            return false;
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

        private void OnRunStarted(RunStartedMessage message) => Refresh();

        /// <summary>
        /// Runs inside the placement that moved the objective — the publisher is synchronous — so the
        /// row is already correct in the frame the qualifying placement resolves. Repaints the whole row
        /// rather than the one chip that moved: the row is at most five chips, and finding the chip
        /// would cost the same scan the repaint does.
        /// </summary>
        private void OnObjectiveProgressChanged(ObjectiveProgressChangedMessage message) => Refresh();

        /// <summary>
        /// The completed state is read back off the model rather than latched here, so this handler
        /// firing twice could not produce a second completion visual. It cannot fire twice anyway:
        /// the message is published on the false to true edge only.
        /// </summary>
        private void OnObjectiveCompleted(ObjectiveCompletedMessage message) => Refresh();

        /// <summary>Dims the row for as long as the 2× bar has the band, and only repaints on the edges.</summary>
        private void OnMultiplierRemainingChanged(float remainingSeconds)
        {
            bool isYielding = remainingSeconds > 0f;
            if (isYielding == _isYieldingToMultiplier)
            {
                return;
            }

            _isYieldingToMultiplier = isYielding;
            Refresh();
        }

        /// <summary>Repaints the whole row from the model: which chips are filled, with what, and how
        /// far along each is.</summary>
        private void Refresh()
        {
            IReadOnlyList<ObjectiveProgress> tracked =
                _objectiveModel != null ? _objectiveModel.TrackedObjectives : null;

            int objectiveCount = tracked != null ? Mathf.Min(tracked.Count, MAX_SLOT_COUNT) : 0;
            bool isPathMode = _gameModeSystem != null && _gameModeSystem.CurrentMode.Value == GameMode.Path;
            if (!isPathMode || objectiveCount == 0 || _currentTheme == null || _isYieldingToMultiplier)
            {
                // Outside Path mode the goals are not the player's concern (issue #269). Otherwise,
                // nothing tracked (or no theme to paint with): an empty row reads as a bug, so the row
                // hides itself rather than leaving placeholders on screen.
                _visibleSlotCount = 0;
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _visibleSlotCount = 0;

            float x = -_rowSize.x * 0.5f;
            x = RefreshLeading(x);

            // The chips flow from the left and stop short of the trailing group: one that would run
            // into it is hidden rather than drawn underneath, and so are those after it, so the row
            // never shows chip three without chip two.
            float limit = _rowSize.x * 0.5f;
            if (_trailingWidth > 0f)
            {
                limit -= _trailingInset + _trailingWidth + _chipSpacing;
            }

            bool isClipped = false;
            for (int slotIndex = 0; slotIndex < MAX_SLOT_COUNT; slotIndex++)
            {
                ObjectiveProgress objective = !isClipped && slotIndex < objectiveCount ? tracked[slotIndex] : null;

                x = RefreshChip(slotIndex, objective, x, limit, out bool isShown);
                if (isShown)
                {
                    _visibleSlotCount++;
                }
                else if (objective != null)
                {
                    isClipped = true;
                }
            }
        }

        /// <summary>
        /// Packs the trailing slot's visible children against the row's right edge, right to left in
        /// sibling order, a chip's spacing apart. A child hidden through its own CanvasGroup takes no
        /// room, so a streak that breaks hands its width back to the chips.
        /// </summary>
        private void LayOutTrailingGroup()
        {
            float x = 0f;
            for (int childIndex = _trailingSlot.childCount - 1; childIndex >= 0; childIndex--)
            {
                var child = (RectTransform)_trailingSlot.GetChild(childIndex);
                if (child.TryGetComponent(out CanvasGroup childGroup) && childGroup.alpha <= 0f)
                {
                    continue;
                }

                child.anchoredPosition = new Vector2(-x, 0f);
                x += child.sizeDelta.x + _chipSpacing;
            }

            _trailingWidth = x > 0f ? x - _chipSpacing : 0f;
        }

        /// <summary>
        /// The row's head: the "goal" caption. Always the plain caption — the level a Path run is on
        /// is named once, on the level pill at the row's trailing end, and nowhere else. Returns the x the first chip
        /// starts at.
        /// </summary>
        private float RefreshLeading(float x)
        {
            _captionText.text = _localizationSystem.Translate(LocalizationKeys.HUD_GOAL_LABEL);
            _captionText.color = _currentTheme.Ink;
            _captionRect.anchoredPosition = new Vector2(x + 4f, 0f);

            return x + 4f + _captionText.preferredWidth + _chipSpacing;
        }

        /// <summary>
        /// Repaints and re-measures one chip, laid out from <paramref name="x"/>; returns the x the
        /// next chip starts at. A chip whose right edge would pass <paramref name="limit"/> is hidden
        /// instead, and <paramref name="isShown"/> says which happened.
        /// </summary>
        private float RefreshChip(int slotIndex, ObjectiveProgress objective, float x, float limit, out bool isShown)
        {
            Chip chip = _chips[slotIndex];
            isShown = false;

            if (objective == null)
            {
                HideChip(chip);
                return x;
            }

            EnsureGlyph(chip, objective.Definition.Type);

            bool isComplete = objective.IsComplete;
            int kind = KindBySlot[slotIndex % KindBySlot.Length];
            Color plateColour = _currentTheme.GetFill(kind);

            chip.ShadowImage.color = _currentTheme.CardShadow;
            chip.PlateImage.color = _currentTheme.CardBackground;
            chip.IconPlateImage.color = plateColour;

            // White on the kind plate, as every power-up and objective glyph is on its tile.
            for (int inkIndex = 0; inkIndex < chip.GlyphInkImages.Count; inkIndex++)
            {
                chip.GlyphInkImages[inkIndex].color = Color.white;
            }

            // The punched-out parts follow the plate, not the ink — that is what makes them read as
            // holes rather than as another stroke of the glyph.
            for (int coreIndex = 0; coreIndex < chip.GlyphCoreImages.Count; coreIndex++)
            {
                chip.GlyphCoreImages[coreIndex].color = plateColour;
            }

            chip.ProgressText.color = _currentTheme.Ink;
            chip.ProgressText.text = FormatProgress(objective.CurrentValue, objective.Definition.TargetValue);

            chip.CheckDisc.color = isComplete ? _currentTheme.GetFill(CHECK_KIND) : Color.clear;
            chip.CheckMark.color = isComplete ? Color.white : Color.clear;

            // Left to right: plate, counter, and the tick only when earned — the chip grows to fit it.
            float progressWidth = chip.ProgressText.preferredWidth;
            float checkWidth = isComplete ? _chipGap + _checkSize : 0f;
            float chipWidth = _chipPaddingLeft + _iconPlateSize + _chipGap + progressWidth + checkWidth + _chipPaddingRight;

            // Measured after the counter is set, since the text is what decides the width; a chip
            // that would run into the trailing group is cleared again rather than drawn under it.
            if (x + chipWidth > limit)
            {
                HideChip(chip);
                return x;
            }

            isShown = true;

            var chipSize = new Vector2(chipWidth, _chipHeight);
            chip.Root.sizeDelta = chipSize;
            chip.ShadowImage.rectTransform.sizeDelta = chipSize;
            chip.PlateImage.rectTransform.sizeDelta = chipSize;
            chip.Root.anchoredPosition = new Vector2(x + (chipWidth * 0.5f), 0f);

            float innerX = (-chipWidth * 0.5f) + _chipPaddingLeft;
            chip.IconPlateRect.anchoredPosition = new Vector2(innerX + (_iconPlateSize * 0.5f), 0f);
            innerX += _iconPlateSize + _chipGap;
            chip.ProgressRect.anchoredPosition = new Vector2(innerX, 0f);
            innerX += progressWidth + _chipGap;
            chip.CheckRect.anchoredPosition = new Vector2(innerX + (_checkSize * 0.5f), 0f);

            return x + chipWidth + _chipSpacing;
        }

        private static void HideChip(Chip chip)
        {
            chip.ShadowImage.color = Color.clear;
            chip.PlateImage.color = Color.clear;
            chip.IconPlateImage.color = Color.clear;
            chip.CheckDisc.color = Color.clear;
            chip.CheckMark.color = Color.clear;
            chip.ProgressText.color = Color.clear;

            for (int inkIndex = 0; inkIndex < chip.GlyphInkImages.Count; inkIndex++)
            {
                chip.GlyphInkImages[inkIndex].color = Color.clear;
            }

            for (int coreIndex = 0; coreIndex < chip.GlyphCoreImages.Count; coreIndex++)
            {
                chip.GlyphCoreImages[coreIndex].color = Color.clear;
            }
        }

        /// <summary>
        /// Builds this chip's glyph if it is not already drawing <paramref name="type"/>. The authored
        /// silhouette from <see cref="ObjectiveIconCatalog"/> is preferred; a type without one falls
        /// back to the procedural glyph. Destroying and rebuilding is only reached on a level change —
        /// a progress tick never changes an objective's type — so the canvas rebuild it costs is paid
        /// once per level rather than per placement.
        /// </summary>
        private void EnsureGlyph(Chip chip, ObjectiveType type)
        {
            if (chip.HasGlyph && chip.GlyphType == type)
            {
                return;
            }

            for (int childIndex = chip.GlyphRoot.childCount - 1; childIndex >= 0; childIndex--)
            {
                Destroy(chip.GlyphRoot.GetChild(childIndex).gameObject);
            }

            chip.GlyphInkImages.Clear();
            chip.GlyphCoreImages.Clear();

            // Cleared before deciding: an Image with no sprite draws a solid square, so the authored
            // image must be invisible whenever it is not the glyph in use.
            chip.IconImage.sprite = null;
            chip.IconImage.color = Color.clear;

            Sprite authoredIcon = _iconCatalog.Find(type);
            if (authoredIcon != null)
            {
                chip.IconImage.sprite = authoredIcon;
                chip.GlyphInkImages.Add(chip.IconImage);
            }
            else
            {
                ObjectiveIconFactory.Build(
                    chip.GlyphRoot, type, _iconSize * 1.15f, chip.GlyphInkImages, chip.GlyphCoreImages);
            }

            chip.GlyphType = type;
            chip.HasGlyph = true;
        }

        private string FormatProgress(int currentValue, int targetValue)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(currentValue);
            _stringBuilder.Append('/');
            _stringBuilder.Append(targetValue);
            return _stringBuilder.ToString();
        }

        /// <summary>
        /// Shown and hidden through the CanvasGroup rather than by toggling the GameObject: toggling it
        /// would rebuild the shared canvas mesh every time the tracked objectives appear or clear.
        /// </summary>
        private void SetVisible(bool isVisible)
        {
            _canvasGroup.alpha = isVisible ? 1f : 0f;
            _canvasGroup.interactable = isVisible;
            _canvasGroup.blocksRaycasts = isVisible;
        }

        /// <summary>The row, its head (caption and level tag, one of which is always clear), the five
        /// chips and the trailing slot. Built before the theme is known; Refresh paints it.</summary>
        private void BuildRow()
        {
            _rowRect = (RectTransform)transform;
            HudChrome.Centre(_rowRect, _rowSize);
            _rowRect.anchoredPosition = _anchoredPosition;
            _rowRect.localScale = Vector3.one;

            _captionText = HudChrome.CreateLabel(
                _rowRect, "Caption", _captionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, _labelFont);
            _captionRect = (RectTransform)_captionText.transform;

            for (int slotIndex = 0; slotIndex < MAX_SLOT_COUNT; slotIndex++)
            {
                _chips[slotIndex] = BuildChip(slotIndex);
            }

            var trailingObject = new GameObject("TrailingSlot", typeof(RectTransform));
            _trailingSlot = (RectTransform)trailingObject.transform;
            _trailingSlot.SetParent(_rowRect, false);
            _trailingSlot.anchorMin = new Vector2(1f, 0.5f);
            _trailingSlot.anchorMax = new Vector2(1f, 0.5f);
            _trailingSlot.pivot = new Vector2(1f, 0.5f);
            _trailingSlot.sizeDelta = new Vector2(0f, _rowSize.y);
            _trailingSlot.anchoredPosition = new Vector2(-_trailingInset, 0f);

            _centreSlot = HudChrome.CreateRect(_rowRect, "CentreSlot", new Vector2(0f, _rowSize.y), Vector2.zero);
        }

        private Chip BuildChip(int slotIndex)
        {
            var chipSize = new Vector2(_chipHeight * 2f, _chipHeight);
            RectTransform chipRect = HudChrome.CreateRect(_rowRect, $"Chip_{slotIndex}", chipSize, Vector2.zero);

            Image shadowImage = HudChrome.BuildRounded(
                chipRect, "Shadow", chipSize, new Vector2(0f, -HudChrome.PILL_SHADOW_DROP), _chipHeight * 0.5f);
            Image plateImage = HudChrome.BuildRounded(chipRect, "Plate", chipSize, Vector2.zero, _chipHeight * 0.5f);

            RectTransform iconPlateRect = HudChrome.CreateRect(
                chipRect, "IconPlate", new Vector2(_iconPlateSize, _iconPlateSize), Vector2.zero);
            Image iconPlateImage = HudChrome.BuildRounded(
                iconPlateRect, "Plate", iconPlateRect.sizeDelta, Vector2.zero, ICON_PLATE_RADIUS);

            // An empty container: the glyph itself depends on the objective type, so it is filled in on
            // the first repaint and rebuilt only when that type changes.
            RectTransform glyphRoot = HudChrome.CreateRect(
                iconPlateRect, "Glyph", new Vector2(_iconPlateSize, _iconPlateSize), Vector2.zero);

            // The authored silhouette lives beside the procedural glyph root so either can stand in
            // for the other without moving anything else in the chip.
            Image iconImage = HudChrome.BuildGlyph(iconPlateRect, "Icon", null, new Vector2(_iconSize, _iconSize), Vector2.zero);

            Text progressText = HudChrome.CreateLabel(
                chipRect, "Progress", _progressFontSize, FontStyle.Normal, TextAnchor.MiddleLeft, Vector2.zero, _displayFont);

            RectTransform checkRect = HudChrome.CreateRect(chipRect, "Check", new Vector2(_checkSize, _checkSize), Vector2.zero);
            Image checkDisc = HudChrome.BuildCircle(checkRect, "Disc", _checkSize, Vector2.zero);
            Image checkMark = HudChrome.BuildGlyph(
                checkRect, "Mark", UiSpriteFactory.CheckMark, new Vector2(_checkSize * 0.6f, _checkSize * 0.6f), Vector2.zero);

            return new Chip(
                chipRect, shadowImage, plateImage, iconPlateRect, iconPlateImage, glyphRoot, iconImage,
                progressText, checkRect, checkDisc, checkMark);
        }
    }
}
