using System.Collections.Generic;
using System.Text;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The run's objectives, as a left-aligned row of icons pinned to the top-left corner of the
    /// screen, directly below <see cref="ScoreView"/>'s "Best" block and growing rightward under the
    /// score. One disc per objective the current level asks for, the size of the level-path icon: an
    /// authored silhouette (or the procedural glyph, for a type without one) for what it measures, a
    /// "2/3" counter for how far along it is, and a tick once that particular objective is done.
    /// <para>
    /// Replaces the old single-line text strip. A level may now carry several objectives at once
    /// (<see cref="ObjectiveModel.TrackedObjectives"/>), and a sentence per objective would not fit the
    /// HUD — an icon does, and the full wording is one tap away in
    /// <see cref="ObjectiveInfoPopupView"/>.
    /// </para>
    /// <para>
    /// The row is a fixed size whatever it holds, so the score, the best score and the countdown can
    /// never push it around or be pushed by it. Slots are built once and shown or hidden by colour
    /// alone — toggling their GameObjects would rebuild the shared canvas mesh every time an objective
    /// appeared or cleared, which is the same reason <see cref="PowerUpInventoryView"/> dims its strip
    /// rather than deactivating it.
    /// </para>
    /// <para>
    /// A slot's glyph is the one thing rebuilt rather than repainted, because which silhouette a slot
    /// wears depends on the objective's type. That only ever happens when the tracked objectives are
    /// replaced — a level change — and never on a progress tick, so no placement can cost a rebuild.
    /// </para>
    /// <para>
    /// Every refresh re-reads the model rather than accumulating message payloads, so the row cannot
    /// drift from what it is showing: the progress and completion messages only say "this one moved",
    /// and a run reset — which resets progress without publishing anything — is picked up from
    /// <see cref="RunStartedMessage"/> the same way.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveIconContainerView : MonoBehaviour
    {
        /// <summary>
        /// Icons the row can draw. Five is headroom rather than a target: no level authors more than a
        /// couple of objectives today, and a row growing rightward from under the "Best" label would
        /// reach the right-hand icon column on a narrow device well before it held many more. A level
        /// authored with more simply shows the first five — a truncated row reads better than one that
        /// overflows the screen.
        /// </summary>
        private const int MAX_SLOT_COUNT = 5;

        /// <summary>Alpha of an objective that is still in progress, against the completed one's full
        /// opacity. The same "not yet" dim the badge wall and the level path use.</summary>
        private const float IN_PROGRESS_ALPHA = 0.88f;

        [Header("Layout")]
        [Tooltip("Top-left corner of the row, offset from the top-left corner of the canvas in " +
            "reference pixels. Anchored to that corner so the first icon sits directly under " +
            "ScoreView's \"Best\" block at every aspect ratio, with the rest of the row growing " +
            "rightward under the score.")]
        [SerializeField] private Vector2 _topLeftOffset = new Vector2(16f, -272f);

        [Tooltip("Diameter of one icon disc, in reference pixels. Matches the level-path icon so the " +
            "HUD's top band reads as one row of same-sized controls.")]
        [SerializeField] private float _slotSize = 112f;

        [Tooltip("Centre-to-centre distance between icons, in reference pixels.")]
        [SerializeField] private float _slotSpacing = 124f;

        [SerializeField] private int _progressFontSize = 26;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _progressBuilder = new StringBuilder(8);
        private readonly IconSlot[] _slots = new IconSlot[MAX_SLOT_COUNT];

        private ObjectiveModel _objectiveModel;
        private SettingsModel _settingsModel;
        private ObjectiveIconCatalog _iconCatalog;
        private ISubscriber<ObjectiveProgressChangedMessage> _progressChangedSubscriber;
        private ISubscriber<ObjectiveCompletedMessage> _completedSubscriber;
        private ISubscriber<RunStartedMessage> _runStartedSubscriber;

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private RectTransform _rowRect;
        private ThemeDefinition _currentTheme;

        /// <summary>How many slots are currently showing an objective. Read by the hit test so a tap on
        /// a hidden slot's rectangle cannot open a popup for an objective that is not there.</summary>
        private int _visibleSlotCount;

        /// <summary>One built icon widget. Rebuilt never — except its glyph, on a level change.</summary>
        private sealed class IconSlot
        {
            internal IconSlot(
                RectTransform root,
                RectTransform plateRect,
                Image plateImage,
                Image shadowImage,
                RectTransform glyphRoot,
                Image iconImage,
                Image checkMark,
                Text progressText)
            {
                Root = root;
                PlateRect = plateRect;
                PlateImage = plateImage;
                ShadowImage = shadowImage;
                GlyphRoot = glyphRoot;
                IconImage = iconImage;
                CheckMark = checkMark;
                ProgressText = progressText;
                GlyphInkImages = new List<Image>(4);
                GlyphCoreImages = new List<Image>(2);
                HasGlyph = false;
            }

            internal RectTransform Root { get; }

            internal RectTransform PlateRect { get; }

            internal Image PlateImage { get; }

            internal Image ShadowImage { get; }

            internal RectTransform GlyphRoot { get; }

            /// <summary>The authored silhouette, when the catalog has one for the slot's type. Listed
            /// among <see cref="GlyphInkImages"/> while in use so it is tinted like any other ink.</summary>
            internal Image IconImage { get; }

            internal Image CheckMark { get; }

            internal Text ProgressText { get; }

            internal List<Image> GlyphInkImages { get; }

            internal List<Image> GlyphCoreImages { get; }

            /// <summary>The type the built glyph draws. Meaningless while <see cref="HasGlyph"/> is
            /// false, which is why the two are read together rather than using a sentinel enum value.</summary>
            internal ObjectiveType GlyphType { get; set; }

            internal bool HasGlyph { get; set; }
        }

        [Inject]
        public void Construct(
            ObjectiveModel objectiveModel,
            SettingsModel settingsModel,
            ObjectiveIconCatalog iconCatalog,
            ISubscriber<ObjectiveProgressChangedMessage> progressChangedSubscriber,
            ISubscriber<ObjectiveCompletedMessage> completedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _objectiveModel = objectiveModel;
            _settingsModel = settingsModel;
            _iconCatalog = iconCatalog;
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

            _progressChangedSubscriber.Subscribe(OnObjectiveProgressChanged).AddTo(_disposables);
            _completedSubscriber.Subscribe(OnObjectiveCompleted).AddTo(_disposables);
            _runStartedSubscriber.Subscribe(OnRunStarted).AddTo(_disposables);

            Refresh();
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>
        /// Resolves a tap to the objective whose icon it landed on, if any. Called by
        /// <see cref="BoardInputView"/>, which owns pointer input — this scene has no EventSystem, and
        /// every Image here has its raycast target off.
        /// <para>
        /// Only populated slots answer: the hidden ones are transparent but still occupy their
        /// rectangle, and a tap on one must not open a popup about an objective that is not there.
        /// </para>
        /// </summary>
        internal bool TryGetTappedObjectiveIndex(Vector2 screenPosition, out int objectiveIndex)
        {
            objectiveIndex = -1;

            if (_visibleSlotCount <= 0)
            {
                return false;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            for (int slotIndex = 0; slotIndex < _visibleSlotCount; slotIndex++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(
                    _slots[slotIndex].PlateRect, screenPosition, eventCamera))
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
        /// rather than the one slot that moved: the row is at most five slots, and finding the slot
        /// would cost the same scan the repaint does.
        /// </summary>
        private void OnObjectiveProgressChanged(ObjectiveProgressChangedMessage message) => Refresh();

        /// <summary>
        /// The completed state is read back off the model rather than latched here, so this handler
        /// firing twice could not produce a second completion visual. It cannot fire twice anyway:
        /// the message is published on the false to true edge only.
        /// </summary>
        private void OnObjectiveCompleted(ObjectiveCompletedMessage message) => Refresh();

        /// <summary>Repaints the whole row from the model: which slots are filled, with what, and how
        /// far along each is.</summary>
        private void Refresh()
        {
            IReadOnlyList<ObjectiveProgress> tracked =
                _objectiveModel != null ? _objectiveModel.TrackedObjectives : null;

            int objectiveCount = tracked != null ? Mathf.Min(tracked.Count, MAX_SLOT_COUNT) : 0;
            if (objectiveCount == 0 || _currentTheme == null)
            {
                // Nothing tracked (or no theme to paint with): an empty row reads as a bug, so the row
                // hides itself rather than leaving placeholders on screen.
                _visibleSlotCount = 0;
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _visibleSlotCount = objectiveCount;
            LayOutSlots(objectiveCount);

            for (int slotIndex = 0; slotIndex < MAX_SLOT_COUNT; slotIndex++)
            {
                RefreshSlot(slotIndex, slotIndex < objectiveCount ? tracked[slotIndex] : null);
            }
        }

        private void RefreshSlot(int slotIndex, ObjectiveProgress objective)
        {
            IconSlot slot = _slots[slotIndex];

            if (objective == null)
            {
                HideSlot(slot);
                return;
            }

            EnsureGlyph(slot, objective.Definition.Type);

            bool isComplete = objective.IsComplete;
            float alpha = isComplete ? 1f : IN_PROGRESS_ALPHA;

            // Complete inverts onto the accent plate, the same way the badge wall marks an unlocked
            // badge and the level path the node the player is on.
            Color plateColour = isComplete
                ? _currentTheme.Accent
                : Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.1f);
            Color inkColour = isComplete ? _currentTheme.CardBackground : _currentTheme.Ink;

            slot.PlateImage.color = WithAlpha(plateColour, alpha);
            slot.ShadowImage.color = WithAlpha(_currentTheme.CardShadow, alpha);

            for (int inkIndex = 0; inkIndex < slot.GlyphInkImages.Count; inkIndex++)
            {
                slot.GlyphInkImages[inkIndex].color = WithAlpha(inkColour, alpha);
            }

            // The punched-out parts follow the plate, not the ink — that is what makes them read as
            // holes rather than as another stroke of the glyph.
            for (int coreIndex = 0; coreIndex < slot.GlyphCoreImages.Count; coreIndex++)
            {
                slot.GlyphCoreImages[coreIndex].color = WithAlpha(plateColour, alpha);
            }

            slot.CheckMark.color = isComplete ? _currentTheme.CardBackground : Color.clear;

            // The tick already says "done", so the counter would only repeat it — and at target it
            // always reads "3/3", which is the least informative thing the badge could show.
            slot.ProgressText.color = isComplete ? Color.clear : WithAlpha(_currentTheme.SoftInk, alpha);
            slot.ProgressText.text = isComplete
                ? string.Empty
                : FormatProgress(objective.CurrentValue, objective.Definition.TargetValue);
        }

        private static void HideSlot(IconSlot slot)
        {
            slot.PlateImage.color = Color.clear;
            slot.ShadowImage.color = Color.clear;
            slot.CheckMark.color = Color.clear;
            slot.ProgressText.color = Color.clear;

            for (int inkIndex = 0; inkIndex < slot.GlyphInkImages.Count; inkIndex++)
            {
                slot.GlyphInkImages[inkIndex].color = Color.clear;
            }

            for (int coreIndex = 0; coreIndex < slot.GlyphCoreImages.Count; coreIndex++)
            {
                slot.GlyphCoreImages[coreIndex].color = Color.clear;
            }
        }

        /// <summary>
        /// Builds this slot's glyph if it is not already drawing <paramref name="type"/>. The authored
        /// silhouette from <see cref="ObjectiveIconCatalog"/> is preferred; a type without one falls
        /// back to the procedural glyph. Destroying and rebuilding is only reached on a level change —
        /// a progress tick never changes an objective's type — so the canvas rebuild it costs is paid
        /// once per level rather than per placement.
        /// </summary>
        private void EnsureGlyph(IconSlot slot, ObjectiveType type)
        {
            if (slot.HasGlyph && slot.GlyphType == type)
            {
                return;
            }

            for (int childIndex = slot.GlyphRoot.childCount - 1; childIndex >= 0; childIndex--)
            {
                Destroy(slot.GlyphRoot.GetChild(childIndex).gameObject);
            }

            slot.GlyphInkImages.Clear();
            slot.GlyphCoreImages.Clear();

            // Cleared before deciding: an Image with no sprite draws a solid square, so the authored
            // image must be invisible whenever it is not the glyph in use.
            slot.IconImage.sprite = null;
            slot.IconImage.color = Color.clear;

            Sprite authoredIcon = _iconCatalog.Find(type);
            if (authoredIcon != null)
            {
                slot.IconImage.sprite = authoredIcon;
                slot.GlyphInkImages.Add(slot.IconImage);
            }
            else
            {
                ObjectiveIconFactory.Build(
                    slot.GlyphRoot, type, _slotSize * 0.62f, slot.GlyphInkImages, slot.GlyphCoreImages);
            }

            slot.GlyphType = type;
            slot.HasGlyph = true;
        }

        /// <summary>Lays the populated slots out left to right from the row's top-left corner, the
        /// first icon's left edge flush with the row's. The row's own size never changes — only how
        /// much of it the icons fill — so nothing else in the HUD can be pushed around by an objective
        /// appearing or clearing.</summary>
        private void LayOutSlots(int objectiveCount)
        {
            // Slot roots are anchored to the row's top-left corner but centre-pivoted (see BuildSlot),
            // so the first one is pushed in by half a slot to put its edge, not its centre, on the corner.
            float y = -_slotSize * 0.5f;

            for (int slotIndex = 0; slotIndex < objectiveCount; slotIndex++)
            {
                float x = (slotIndex * _slotSpacing) + (_slotSize * 0.5f);
                _slots[slotIndex].Root.anchoredPosition = new Vector2(x, y);
            }
        }

        private string FormatProgress(int currentValue, int targetValue)
        {
            _progressBuilder.Clear();
            _progressBuilder.Append(currentValue);
            _progressBuilder.Append('/');
            _progressBuilder.Append(targetValue);
            return _progressBuilder.ToString();
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

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        private void BuildRow()
        {
            _rowRect = (RectTransform)transform;

            // Anchored to the top-left corner, like ScoreView's "Best" block above it, so the row keeps
            // the same gap from that block whatever the aspect ratio. The slots inside are laid out
            // from this corner too (see LayOutSlots), which is what makes the row left-aligned.
            _rowRect.anchorMin = new Vector2(0f, 1f);
            _rowRect.anchorMax = new Vector2(0f, 1f);
            _rowRect.pivot = new Vector2(0f, 1f);

            // Fixed, and deliberately independent of how many objectives there are: the row is a
            // reserved band, not a widget that grows.
            _rowRect.sizeDelta = new Vector2(MAX_SLOT_COUNT * _slotSpacing, _slotSize);
            _rowRect.anchoredPosition = _topLeftOffset;

            // Every size here is in canvas reference units, so the row owns its own scale rather than
            // inheriting whatever the scene object happened to be created with.
            _rowRect.localScale = Vector3.one;

            for (int slotIndex = 0; slotIndex < MAX_SLOT_COUNT; slotIndex++)
            {
                _slots[slotIndex] = BuildSlot(slotIndex);
            }
        }

        private IconSlot BuildSlot(int slotIndex)
        {
            var slotSize = new Vector2(_slotSize, _slotSize);

            var slotObject = new GameObject($"ObjectiveIcon_{slotIndex}", typeof(RectTransform));
            var slotRect = (RectTransform)slotObject.transform;
            slotRect.SetParent(_rowRect, false);
            Centre(slotRect, slotSize);

            // Anchored to the row's top-left corner, not its centre, so LayOutSlots can measure from
            // the row's left edge — the row is left-aligned and its slots must be too.
            slotRect.anchorMin = new Vector2(0f, 1f);
            slotRect.anchorMax = new Vector2(0f, 1f);

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(slotRect, false);
            Centre(shadowRect, slotSize + new Vector2(8f, 8f));
            shadowRect.anchoredPosition = new Vector2(0f, -5f);
            Image shadowImage = ConfigureDisc(shadowObject.GetComponent<Image>());

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(slotRect, false);
            Centre(plateRect, slotSize);
            Image plateImage = ConfigureDisc(plateObject.GetComponent<Image>());

            // An empty container: the glyph itself depends on the objective type, so it is filled in on
            // the first repaint and rebuilt only when that type changes. Lifted off centre so the
            // progress counter fits inside the disc's lower curve beneath it.
            var glyphObject = new GameObject("Glyph", typeof(RectTransform));
            var glyphRoot = (RectTransform)glyphObject.transform;
            glyphRoot.SetParent(slotRect, false);
            Centre(glyphRoot, slotSize);
            glyphRoot.anchoredPosition = new Vector2(0f, _slotSize * 0.09f);

            // The authored silhouette lives beside the procedural glyph root, at the same offset, so
            // either can stand in for the other without moving anything else in the slot.
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(slotRect, false);
            Centre(iconRect, new Vector2(_slotSize * 0.54f, _slotSize * 0.54f));
            iconRect.anchoredPosition = glyphRoot.anchoredPosition;

            var iconImage = iconObject.GetComponent<Image>();
            iconImage.type = Image.Type.Simple;
            iconImage.preserveAspect = true;
            iconImage.color = Color.clear;
            iconImage.raycastTarget = false;

            // Pulled in from the corner compared with a square plate: a disc has no corner to tuck
            // the tick into, so it sits on the rim's 45° point instead.
            var checkObject = new GameObject("CheckMark", typeof(RectTransform), typeof(Image));
            var checkRect = (RectTransform)checkObject.transform;
            checkRect.SetParent(slotRect, false);
            Centre(checkRect, new Vector2(_slotSize * 0.36f, _slotSize * 0.36f));
            checkRect.anchoredPosition = new Vector2(_slotSize * 0.22f, -_slotSize * 0.22f);

            var checkImage = checkObject.GetComponent<Image>();

            // The tick sprite has no border, so it must never be sliced.
            checkImage.sprite = UiSpriteFactory.CheckMark;
            checkImage.type = Image.Type.Simple;
            checkImage.color = Color.clear;
            checkImage.raycastTarget = false;

            Text progressText = UiTextFactory.Create(
                slotRect, "Progress", _progressFontSize, FontStyle.Bold, Color.clear);
            var progressRect = (RectTransform)progressText.transform;
            progressRect.anchorMin = new Vector2(0.5f, 0f);
            progressRect.anchorMax = new Vector2(0.5f, 0f);
            progressRect.pivot = new Vector2(0.5f, 0f);
            progressRect.anchoredPosition = new Vector2(0f, _slotSize * 0.11f);
            progressText.alignment = TextAnchor.LowerCenter;

            return new IconSlot(
                slotRect, plateRect, plateImage, shadowImage, glyphRoot, iconImage, checkImage, progressText);
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not
        // through an EventSystem, and this scene has none. The circle sprite has no border, so it
        // must never be sliced.
        private static Image ConfigureDisc(Image image)
        {
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }
    }
}
