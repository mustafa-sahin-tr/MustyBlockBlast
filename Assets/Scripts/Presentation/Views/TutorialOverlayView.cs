using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
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
    /// Renders the one active coach-mark, if any: a scrim over everything except a rectangular cutout
    /// around the spotlighted target, a small arrow marker on the side <see cref="TutorialStep.ArrowDirection"/>
    /// names, and the step's localized body text. Purely a renderer — it observes
    /// <see cref="TutorialModel.ActiveStep"/> and reads whichever View owns the target's on-screen rect;
    /// every decision about <em>which</em> step to show, and when a target counts as "used", belongs to
    /// <see cref="TutorialSystem"/>.
    /// <para>
    /// The cutout is four plain rectangles (top/bottom/left/right bands) sized every frame the active
    /// step changes, rather than a shader mask: simple, parametric, and exactly as precise as the target
    /// rect it is built from. Coordinates are computed in this View's own local space via
    /// <c>RectTransform.GetWorldCorners</c> + <c>InverseTransformPoint</c>, which is correct regardless
    /// of where the target rect sits in the hierarchy, as long as both share this scene's one Canvas.
    /// </para>
    /// <para>
    /// Does not itself decide what blocks input — see <see cref="BoardInputView"/>'s own guard, which
    /// reads <see cref="TutorialModel.ActiveStep"/> independently. This View could be destroyed entirely
    /// and the guard would still hold; it only ever adds the visual explanation on top.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialOverlayView : MonoBehaviour
    {
        private static readonly Color ScrimColour = new Color(0f, 0f, 0f, 0.68f);
        private static readonly Color ArrowColour = new Color(1f, 0.84f, 0.2f, 1f);
        private static readonly Color TextColour = Color.white;

        private const float ARROW_SIZE = 40f;
        private const float ARROW_GAP = 12f;
        private const int BODY_FONT_SIZE = 32;
        private const float BODY_MAX_WIDTH = 560f;

        private RectTransform _root;
        private RectTransform _panel;
        private RectTransform _topBand;
        private RectTransform _bottomBand;
        private RectTransform _leftBand;
        private RectTransform _rightBand;
        private RectTransform _arrow;
        private Text _bodyText;
        private Canvas _canvas;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private TutorialModel _tutorialModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private BoardView _boardView;
        private PowerUpInventoryView _powerUpInventoryView;
        private PieceTrayView _trayView;
        private HoldSlotView _holdSlotView;

        [Inject]
        public void Construct(
            TutorialModel tutorialModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            BoardView boardView,
            PowerUpInventoryView powerUpInventoryView,
            PieceTrayView trayView,
            HoldSlotView holdSlotView)
        {
            _tutorialModel = tutorialModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _boardView = boardView;
            _powerUpInventoryView = powerUpInventoryView;
            _trayView = trayView;
            _holdSlotView = holdSlotView;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            BuildPanel();
            _panel.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (_tutorialModel == null || _localizationModel == null || _localizationSystem == null)
            {
                Debug.LogError(
                    $"{nameof(TutorialOverlayView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            _tutorialModel.ActiveStep.Subscribe(OnActiveStepChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(_ => Render(_tutorialModel.ActiveStep.Value)).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnActiveStepChanged(TutorialStep? step) => Render(step);

        private void Render(TutorialStep? step)
        {
            if (step == null)
            {
                _panel.gameObject.SetActive(false);
                return;
            }

            RectTransform target = ResolveTarget(step.Value);
            if (target == null)
            {
                // The step's target is not on screen right now (e.g. a slot the strip does not show).
                // Nothing to spotlight, so nothing is shown — the step stays active and will render as
                // soon as its target appears, rather than being lost.
                _panel.gameObject.SetActive(false);
                return;
            }

            _panel.gameObject.SetActive(true);
            transform.SetAsLastSibling();

            LayOutCutout(target);
            LayOutArrow(target, step.Value.ArrowDirection);
            _bodyText.text = _localizationSystem.Translate(step.Value.BodyLocalizationKey);
        }

        private RectTransform ResolveTarget(TutorialStep step)
        {
            switch (step.TargetId)
            {
                case TutorialTargetId.BoardCell:
                    return step.BoardPosition != null
                        ? _boardView.GetCellRectTransform(step.BoardPosition.Value)
                        : null;
                case TutorialTargetId.PowerUpStripSlot:
                    return _powerUpInventoryView.GetSlotRectTransform((PowerUpKind)step.SlotIndex);
                case TutorialTargetId.TraySlot:
                    return _trayView.GetSlotRectTransform(step.SlotIndex);
                case TutorialTargetId.HoldSlot:
                    return _holdSlotView.GetPocketRectTransform();
                default:
                    return null;
            }
        }

        /// <summary>Sizes the four scrim bands so together they cover everything except
        /// <paramref name="target"/>'s rect.</summary>
        private void LayOutCutout(RectTransform target)
        {
            GetLocalBounds(target, out Vector2 min, out Vector2 max);

            Rect screen = _root.rect;
            float halfW = screen.width * 0.5f;
            float halfH = screen.height * 0.5f;

            SetBand(_topBand, -halfW, halfW, max.y, halfH);
            SetBand(_bottomBand, -halfW, halfW, -halfH, min.y);
            SetBand(_leftBand, -halfW, min.x, min.y, max.y);
            SetBand(_rightBand, max.x, halfW, min.y, max.y);
        }

        private void LayOutArrow(RectTransform target, TutorialArrowDirection direction)
        {
            if (direction == TutorialArrowDirection.None)
            {
                _arrow.gameObject.SetActive(false);
                return;
            }

            GetLocalBounds(target, out Vector2 min, out Vector2 max);
            Vector2 centre = (min + max) * 0.5f;

            Vector2 position;
            float rotation;
            switch (direction)
            {
                case TutorialArrowDirection.Up:
                    position = new Vector2(centre.x, max.y + ARROW_GAP + (ARROW_SIZE * 0.5f));
                    rotation = 180f;
                    break;
                case TutorialArrowDirection.Down:
                    position = new Vector2(centre.x, min.y - ARROW_GAP - (ARROW_SIZE * 0.5f));
                    rotation = 0f;
                    break;
                case TutorialArrowDirection.Left:
                    position = new Vector2(min.x - ARROW_GAP - (ARROW_SIZE * 0.5f), centre.y);
                    rotation = 90f;
                    break;
                default:
                    position = new Vector2(max.x + ARROW_GAP + (ARROW_SIZE * 0.5f), centre.y);
                    rotation = -90f;
                    break;
            }

            _arrow.gameObject.SetActive(true);
            _arrow.anchoredPosition = position;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, rotation);

            // The body text sits just past the arrow, on the same side, so the line "arrow then text"
            // reads as one pointer rather than two unrelated elements.
            Vector2 textOffset = (position - centre).normalized * (ARROW_SIZE + ARROW_GAP + (BODY_FONT_SIZE * 0.5f));
            ((RectTransform)_bodyText.transform).anchoredPosition = position + textOffset;
        }

        /// <summary>
        /// <paramref name="target"/>'s rect expressed in this overlay's own local space, via world
        /// corners rather than anchors — correct regardless of where the target sits in the hierarchy,
        /// as long as both share the one Canvas this scene draws through.
        /// </summary>
        private void GetLocalBounds(RectTransform target, out Vector2 min, out Vector2 max)
        {
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);

            Vector2 bottomLeft = _root.InverseTransformPoint(corners[0]);
            Vector2 topRight = _root.InverseTransformPoint(corners[2]);

            min = bottomLeft;
            max = topRight;
        }

        private static void SetBand(RectTransform band, float xMin, float xMax, float yMin, float yMax)
        {
            float width = Mathf.Max(0f, xMax - xMin);
            float height = Mathf.Max(0f, yMax - yMin);

            band.gameObject.SetActive(width > 0f && height > 0f);
            band.sizeDelta = new Vector2(width, height);
            band.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
        }

        private void BuildPanel()
        {
            // This component's own RectTransform is the full-screen stretch layer, exactly as
            // CoinConversionView's is — whatever the scene placed it at is overwritten here, so no
            // scene-side anchor setup is required for this GameObject.
            _root = (RectTransform)transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            // The toggle target: everything visible lives under here, so hiding the coach-mark is one
            // SetActive rather than one per element, and — unlike toggling this component's own
            // GameObject — never risks skipping this MonoBehaviour's own Start/OnDestroy.
            GameObject panelObject = new GameObject("Panel", typeof(RectTransform));
            _panel = (RectTransform)panelObject.transform;
            _panel.SetParent(_root, false);
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.one;
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.offsetMin = Vector2.zero;
            _panel.offsetMax = Vector2.zero;

            _topBand = BuildBand("TopBand");
            _bottomBand = BuildBand("BottomBand");
            _leftBand = BuildBand("LeftBand");
            _rightBand = BuildBand("RightBand");

            GameObject arrowObject = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            _arrow = (RectTransform)arrowObject.transform;
            _arrow.SetParent(_panel, false);
            HudChrome.Centre(_arrow, new Vector2(ARROW_SIZE, ARROW_SIZE));
            Image arrowImage = arrowObject.GetComponent<Image>();
            arrowImage.color = ArrowColour;
            arrowImage.raycastTarget = false;

            _bodyText = HudChrome.CreateLabel(
                _panel, "BodyText", BODY_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, null);
            _bodyText.color = TextColour;
            _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            ((RectTransform)_bodyText.transform).sizeDelta = new Vector2(BODY_MAX_WIDTH, BODY_FONT_SIZE * 3f);
        }

        private RectTransform BuildBand(string objectName)
        {
            GameObject bandObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            RectTransform bandRect = (RectTransform)bandObject.transform;
            bandRect.SetParent(_panel, false);
            HudChrome.Centre(bandRect, Vector2.zero);

            Image bandImage = bandObject.GetComponent<Image>();
            bandImage.color = ScrimColour;
            bandImage.raycastTarget = false;

            return bandRect;
        }
    }
}
