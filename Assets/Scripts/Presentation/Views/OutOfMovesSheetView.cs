using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
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
    /// The out-of-moves sheet (issue #465, mockup artboard "Hamle bitti → +6 reklam"): a modal card over a
    /// dimmed scrim that opens when a move-limited level's budget runs out short of its target. It shows
    /// the score against the target with a progress bar and how many points are still missing, explains
    /// the one-time offer, and holds a blue "+6 MOVES" rewarded-ad button over a quiet "No thanks".
    /// <para>
    /// It holds no rule. The ad is <see cref="MoveBudgetSystem.TryRequestExtraMovesAsync"/>'s, the decline
    /// <see cref="MoveBudgetSystem.DeclineExtraMoves"/>'s; the sheet closes whenever
    /// <see cref="MoveBudgetModel.IsOfferOpen"/> goes false — moves granted, offer declined, or the run
    /// ended under it some other way. There is no close cross and a scrim tap does nothing: the player
    /// must answer, because the run is held until they do.
    /// </para>
    /// <para>
    /// Modal while open: <see cref="BoardInputView"/> routes every tap into <see cref="HandleTap"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutOfMovesSheetView : MonoBehaviour
    {
        // Layout, in canvas reference pixels, in the out-of-lives sheet's vocabulary so the two read as one family.
        private const float CARD_WIDTH = 900f;
        private const float CARD_PADDING_TOP = 64f;
        private const float CARD_PADDING_BOTTOM = 40f;
        private const float CARD_RADIUS = 64f;
        private const float CARD_SHADOW_DROP = 26f;
        private const float SIDE_INSET = 50f;
        private const float CONTENT_WIDTH = CARD_WIDTH - (SIDE_INSET * 2f);
        private const float STACK_GAP = 26f;

        private const float TITLE_HEIGHT = 80f;
        private const float SCORE_BOX_HEIGHT = 210f;
        private const float SCORE_BOX_RADIUS = 36f;
        private const float BAR_HEIGHT = 30f;
        private const float OFFER_BOX_HEIGHT = 150f;
        private const float OFFER_ICON_SIZE = 108f;
        private const float BUTTON_HEIGHT = 140f;
        private const float BUTTON_SLICE_SCALE = 2.5f;
        private const float BUTTON_LABEL_RISE = 6f;
        private const float DECLINE_HEIGHT = 80f;

        private const string SLASH = " / ";

        /// <summary>The mockup's "▶" before the ad caption — a symbol, kept out of the String Table.</summary>
        private const string PLAY_GLYPH_PREFIX = "▶ ";

        private static readonly Color CardFace = new Color32(0xFD, 0xFC, 0xFB, 0xFF);
        private static readonly Color CardShadow = new Color32(0x2B, 0x26, 0x33, 0x3A);
        private static readonly Color TitleInk = new Color32(0xC2, 0x5A, 0x3F, 0xFF);
        private static readonly Color ScoreBoxFill = new Color32(0xEE, 0xF5, 0xFC, 0xFF);
        private static readonly Color ScoreInk = new Color32(0x01, 0x57, 0x9B, 0xFF);
        private static readonly Color ScoreSoftInk = new Color32(0x60, 0x97, 0xC2, 0xFF);
        private static readonly Color CaptionInk = new Color32(0x3A, 0x6F, 0xB0, 0xFF);
        private static readonly Color BarTrack = new Color32(0xD5, 0xE4, 0xF4, 0xFF);
        private static readonly Color BarFill = new Color32(0xF0, 0xB8, 0x3A, 0xFF);
        private static readonly Color NeededInk = new Color32(0x4A, 0x6A, 0x90, 0xFF);
        private static readonly Color OfferFill = new Color32(0xFF, 0xF6, 0xDC, 0xFF);
        private static readonly Color OfferOutline = new Color32(0xF3, 0xDC, 0x9A, 0xFF);
        private static readonly Color OfferInk = new Color32(0x7A, 0x5A, 0x12, 0xFF);
        private static readonly Color AdBlue = new Color32(0x3F, 0x86, 0xEE, 0xFF);
        private static readonly Color DisabledGrey = new Color32(0xC9, 0xC4, 0xD2, 0xFF);
        private static readonly Color DeclineInk = new Color32(0x8A, 0x84, 0x96, 0xFF);
        private static readonly Color TextShadowColour = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Vector2 TextShadowOffset = new Vector2(0f, -4f);

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(24);

        [Header("Type")]
        [Tooltip("Display face (Bowlby One SC): title, score and the button.")]
        [SerializeField] private Font _displayFont;
        [Tooltip("Body face: captions and copy.")]
        [SerializeField] private Font _bodyFont;
        [SerializeField] private int _titleFontSize = 68;
        [SerializeField] private int _scoreFontSize = 64;
        [SerializeField] private int _bodyFontSize = 32;
        [SerializeField] private int _buttonFontSize = 52;

        [Header("Sprites")]
        [Tooltip("White 9-sliced glossy button with a darker bottom lip (the shop's). Tinted at runtime.")]
        [SerializeField] private Sprite _buttonSprite;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.2f, 0.55f);

        private MoveBudgetModel _moveBudgetModel;
        private MoveBudgetConfig _moveBudgetConfig;
        private MoveBudgetSystem _moveBudgetSystem;
        private ScoreModel _scoreModel;
        private ObjectiveModel _objectiveModel;
        private ObjectiveIconCatalog _iconCatalog;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private ISubscriber<MovesRanOutMessage> _movesRanOutSubscriber;

        private Canvas _canvas;
        private CancellationToken _destroyToken;
        private GameObject _panel;
        private RectTransform _cardRect;
        private RectTransform _cardShadowRect;

        private Text _titleText;
        private RectTransform _scoreBoxRect;
        private Text _scoreCaptionText;
        private Text _scoreText;
        private RectTransform _barFillRect;
        private Text _neededText;

        private RectTransform _offerRect;
        private Image _offerIcon;
        private Text _offerText;

        private RectTransform _adButtonRect;
        private Image _adButtonPlate;
        private Text _adButtonText;
        private RectTransform _declineRect;
        private Text _declineText;

        private bool _isRequestingAd;

        [Inject]
        public void Construct(
            MoveBudgetModel moveBudgetModel,
            MoveBudgetConfig moveBudgetConfig,
            MoveBudgetSystem moveBudgetSystem,
            ScoreModel scoreModel,
            ObjectiveModel objectiveModel,
            ObjectiveIconCatalog iconCatalog,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            ISubscriber<MovesRanOutMessage> movesRanOutSubscriber)
        {
            _moveBudgetModel = moveBudgetModel;
            _moveBudgetConfig = moveBudgetConfig;
            _moveBudgetSystem = moveBudgetSystem;
            _scoreModel = scoreModel;
            _objectiveModel = objectiveModel;
            _iconCatalog = iconCatalog;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _movesRanOutSubscriber = movesRanOutSubscriber;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            _destroyToken = this.GetCancellationTokenOnDestroy();
            BuildPanel();
            _panel.SetActive(false);
        }

        private void Start()
        {
            if (_moveBudgetModel == null || _moveBudgetConfig == null || _moveBudgetSystem == null
                || _scoreModel == null || _objectiveModel == null || _iconCatalog == null
                || _localizationModel == null || _localizationSystem == null || _movesRanOutSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(OutOfMovesSheetView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            Sprite icon = _iconCatalog.Find(ObjectiveType.ScoreInMoves);
            _offerIcon.sprite = icon;
            _offerIcon.color = icon != null ? Color.white : Color.clear;

            _movesRanOutSubscriber.Subscribe(OnMovesRanOut).AddTo(_disposables);
            _moveBudgetModel.IsOfferOpen.Subscribe(OnOfferOpenChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(_ => RefreshIfOpen()).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the sheet is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>Routes a tap while open: the ad button or the decline link; anything else is swallowed.</summary>
        internal void HandleTap(Vector2 screenPosition)
        {
            if (!IsOpen || _isRequestingAd)
            {
                return;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (RectTransformUtility.RectangleContainsScreenPoint(_adButtonRect, screenPosition, eventCamera))
            {
                RequestAdAsync(_destroyToken).Forget();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_declineRect, screenPosition, eventCamera))
            {
                _moveBudgetSystem.DeclineExtraMoves();
            }
        }

        private void OnMovesRanOut(MovesRanOutMessage message)
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            _panel.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
        }

        /// <summary>The offer closed — granted, declined or overtaken by the run ending — so the sheet goes.</summary>
        private void OnOfferOpenChanged(bool isOpen)
        {
            if (!isOpen && IsOpen)
            {
                _panel.SetActive(false);
            }
        }

        private void RefreshIfOpen()
        {
            if (IsOpen)
            {
                Refresh();
            }
        }

        /// <summary>Fire-and-forget ad request. Granted or not, the model's offer flag closes the sheet; a
        /// cancellation (the View going away) leaves the offer up for the next View to answer.</summary>
        private async UniTaskVoid RequestAdAsync(CancellationToken cancellationToken)
        {
            if (!_moveBudgetSystem.CanRequestExtraMoves)
            {
                return;
            }

            _isRequestingAd = true;
            PaintAdButton();
            try
            {
                await _moveBudgetSystem.TryRequestExtraMovesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // The ad SDK's failure is not the player's problem: logged, and read as "no ad" by the System.
                Debug.LogException(exception, this);
            }
            finally
            {
                _isRequestingAd = false;
            }

            RefreshIfOpen();
        }

        /// <summary>Repaints every row from the models. Runs on open and on a locale change — never per frame.</summary>
        private void Refresh()
        {
            int score = _scoreModel.Score.Value;
            int target = FindTarget();

            _titleText.text = _localizationSystem.Translate(LocalizationKeys.MOVES_SHEET_TITLE);
            _scoreCaptionText.text = _localizationSystem.Translate(LocalizationKeys.HUD_SCORE_LABEL);

            _stringBuilder.Clear();
            _stringBuilder.Append(score);
            _stringBuilder.Append(SLASH);
            _stringBuilder.Append(target);
            _scoreText.text = _stringBuilder.ToString();

            float fraction = target > 0 ? Mathf.Clamp01(score / (float)target) : 0f;
            _barFillRect.sizeDelta = new Vector2(Mathf.Max(BAR_HEIGHT, CONTENT_WIDTH * 0.84f * fraction), BAR_HEIGHT);
            _barFillRect.anchoredPosition = new Vector2(
                (-CONTENT_WIDTH * 0.42f) + (_barFillRect.sizeDelta.x * 0.5f), _barFillRect.anchoredPosition.y);

            _neededText.text = _localizationSystem.Format(
                LocalizationKeys.MOVES_SHEET_NEEDED, FormatCount(Mathf.Max(0, target - score)));
            _offerText.text = _localizationSystem.Format(
                LocalizationKeys.MOVES_SHEET_BODY, FormatCount(_moveBudgetConfig.ExtraMovesFromAd));
            _declineText.text = _localizationSystem.Translate(LocalizationKeys.MOVES_SHEET_DECLINE);

            PaintAdButton();
            LayoutCard();
        }

        private void PaintAdButton()
        {
            _adButtonPlate.color = _isRequestingAd ? DisabledGrey : AdBlue;

            // Worded before the builder is reused for the prefix: FormatCount shares it.
            string caption = _localizationSystem.Format(
                LocalizationKeys.MOVES_SHEET_WATCH_AD, FormatCount(_moveBudgetConfig.ExtraMovesFromAd));
            _stringBuilder.Clear();
            _stringBuilder.Append(PLAY_GLYPH_PREFIX);
            _stringBuilder.Append(caption);
            _adButtonText.text = _stringBuilder.ToString();
        }

        /// <summary>The target of the level's move-limited objective — the score the bar fills towards.</summary>
        private int FindTarget()
        {
            IReadOnlyList<ObjectiveProgress> objectives = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
            {
                ObjectiveDefinition definition = objectives[objectiveIndex].Definition;
                if (definition.Type == ObjectiveType.ScoreInMoves)
                {
                    return definition.TargetValue;
                }
            }

            return 0;
        }

        private string FormatCount(int value)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(value);
            return _stringBuilder.ToString();
        }

        /// <summary>Stacks the rows top to bottom and sizes the card around them, measure then place, as the
        /// out-of-lives sheet does.</summary>
        private void LayoutCard()
        {
            float contentHeight = StackRows(0f, false);
            float cardHeight = CARD_PADDING_TOP + contentHeight + CARD_PADDING_BOTTOM;
            StackRows((cardHeight * 0.5f) - CARD_PADDING_TOP, true);

            var cardSize = new Vector2(CARD_WIDTH, cardHeight);
            _cardRect.sizeDelta = cardSize;
            _cardShadowRect.sizeDelta = cardSize;
            _cardShadowRect.anchoredPosition = new Vector2(0f, -CARD_SHADOW_DROP);
        }

        private float StackRows(float top, bool apply)
        {
            float cursor = 0f;
            cursor = PlaceRow(_titleText.rectTransform, TITLE_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_scoreBoxRect, SCORE_BOX_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_offerRect, OFFER_BOX_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_adButtonRect, BUTTON_HEIGHT, cursor, top, apply);
            cursor = PlaceRow(_declineRect, DECLINE_HEIGHT, cursor, top, apply);
            return cursor;
        }

        private static float PlaceRow(RectTransform row, float height, float cursor, float top, bool apply)
        {
            if (cursor > 0f)
            {
                cursor += STACK_GAP;
            }

            if (apply)
            {
                row.anchoredPosition = new Vector2(row.anchoredPosition.x, top - cursor - (height * 0.5f));
            }

            return cursor + height;
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            var panelObject = new GameObject("OutOfMovesPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            Image cardShadow = HudChrome.BuildRounded(
                panelRect, "OutOfMovesCardShadow", new Vector2(CARD_WIDTH, 1f), Vector2.zero, CARD_RADIUS);
            cardShadow.color = CardShadow;
            _cardShadowRect = cardShadow.rectTransform;

            Image card = HudChrome.BuildRounded(
                panelRect, "OutOfMovesCard", new Vector2(CARD_WIDTH, 1f), Vector2.zero, CARD_RADIUS);
            card.color = CardFace;
            _cardRect = card.rectTransform;

            _titleText = CreateText(_cardRect, "Title", _titleFontSize, _displayFont, TitleInk);
            _titleText.rectTransform.sizeDelta = new Vector2(CONTENT_WIDTH, TITLE_HEIGHT);
            _titleText.resizeTextForBestFit = true;
            _titleText.resizeTextMinSize = 36;
            _titleText.resizeTextMaxSize = _titleFontSize;

            BuildScoreBox();
            BuildOffer();
            BuildAdButton();

            _declineText = CreateText(_cardRect, "Decline", _bodyFontSize + 2, _bodyFont, DeclineInk);
            _declineRect = _declineText.rectTransform;
            _declineRect.sizeDelta = new Vector2(CONTENT_WIDTH * 0.6f, DECLINE_HEIGHT);

            _panel = panelObject;
        }

        /// <summary>The blue box: "SCORE" left, "1310 / 1500" right, the gold progress bar and the missing points.</summary>
        private void BuildScoreBox()
        {
            var size = new Vector2(CONTENT_WIDTH, SCORE_BOX_HEIGHT);
            _scoreBoxRect = HudChrome.CreateRect(_cardRect, "ScoreBox", size, Vector2.zero);
            HudChrome.BuildRounded(_scoreBoxRect, "Fill", size, Vector2.zero, SCORE_BOX_RADIUS).color = ScoreBoxFill;

            float left = (-CONTENT_WIDTH * 0.5f) + 40f;
            float right = (CONTENT_WIDTH * 0.5f) - 40f;
            _scoreCaptionText = HudChrome.CreateLabel(
                _scoreBoxRect, "Caption", _bodyFontSize - 4, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(left, 52f), _bodyFont);
            _scoreCaptionText.color = CaptionInk;
            _scoreText = HudChrome.CreateLabel(
                _scoreBoxRect, "Score", _scoreFontSize, FontStyle.Normal, TextAnchor.MiddleRight,
                new Vector2(right, 52f), _displayFont);
            _scoreText.color = ScoreInk;

            var trackSize = new Vector2(CONTENT_WIDTH * 0.84f, BAR_HEIGHT);
            HudChrome.BuildRounded(_scoreBoxRect, "BarTrack", trackSize, new Vector2(0f, -10f), BAR_HEIGHT * 0.5f).color = BarTrack;
            Image fill = HudChrome.BuildRounded(
                _scoreBoxRect, "BarFill", new Vector2(BAR_HEIGHT, BAR_HEIGHT), new Vector2(0f, -10f), BAR_HEIGHT * 0.5f);
            fill.color = BarFill;
            _barFillRect = fill.rectTransform;

            _neededText = HudChrome.CreateLabel(
                _scoreBoxRect, "Needed", _bodyFontSize - 4, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-CONTENT_WIDTH * 0.42f, -62f), _bodyFont);
            _neededText.color = NeededInk;
        }

        /// <summary>The cream box: the objective's icon and the one-time offer, worded from the config.</summary>
        private void BuildOffer()
        {
            var size = new Vector2(CONTENT_WIDTH, OFFER_BOX_HEIGHT);
            _offerRect = HudChrome.CreateRect(_cardRect, "Offer", size, Vector2.zero);
            HudChrome.BuildRounded(_offerRect, "Fill", size, Vector2.zero, SCORE_BOX_RADIUS).color = OfferFill;
            HudChrome.BuildOutline(_offerRect, "Outline", size, Vector2.zero, SCORE_BOX_RADIUS, 4f).color = OfferOutline;

            float iconX = (-CONTENT_WIDTH * 0.5f) + 26f + (OFFER_ICON_SIZE * 0.5f);
            _offerIcon = HudChrome.BuildGlyph(
                _offerRect, "Icon", null, new Vector2(OFFER_ICON_SIZE, OFFER_ICON_SIZE), new Vector2(iconX, 0f));
            _offerIcon.preserveAspect = true;
            _offerIcon.color = Color.clear;

            float textWidth = CONTENT_WIDTH - OFFER_ICON_SIZE - 80f;
            _offerText = CreateText(_offerRect, "Body", _bodyFontSize - 2, _bodyFont, OfferInk);
            _offerText.alignment = TextAnchor.MiddleLeft;
            _offerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _offerText.rectTransform.sizeDelta = new Vector2(textWidth, OFFER_BOX_HEIGHT - 20f);
            _offerText.rectTransform.anchoredPosition = new Vector2(iconX + (OFFER_ICON_SIZE * 0.5f) + 24f + (textWidth * 0.5f), 0f);
        }

        private void BuildAdButton()
        {
            var size = new Vector2(CONTENT_WIDTH, BUTTON_HEIGHT);
            _adButtonRect = HudChrome.CreateRect(_cardRect, "WatchAdButton", size, Vector2.zero);
            _adButtonPlate = BuildSlicedPlate(_adButtonRect, "Plate", _buttonSprite, size, BUTTON_SLICE_SCALE);
            _adButtonPlate.color = AdBlue;

            _adButtonText = CreateText(_adButtonRect, "Caption", _buttonFontSize, _displayFont, Color.white);
            _adButtonText.rectTransform.sizeDelta = new Vector2(CONTENT_WIDTH - 40f, BUTTON_HEIGHT);
            _adButtonText.rectTransform.anchoredPosition = new Vector2(0f, BUTTON_LABEL_RISE);
            _adButtonText.resizeTextForBestFit = true;
            _adButtonText.resizeTextMinSize = 28;
            _adButtonText.resizeTextMaxSize = _buttonFontSize;
            var shadow = _adButtonText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = TextShadowColour;
            shadow.effectDistance = TextShadowOffset;
        }

        /// <summary>A white glossy sprite, 9-sliced to <paramref name="size"/>; falls back to a rounded plate.</summary>
        private static Image BuildSlicedPlate(
            RectTransform parent, string objectName, Sprite sprite, Vector2 size, float sliceScale)
        {
            if (sprite == null)
            {
                return HudChrome.BuildRounded(parent, objectName, size, Vector2.zero, size.y * 0.25f);
            }

            var plateObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(parent, false);
            HudChrome.Centre(plateRect, size);

            var plate = plateObject.GetComponent<Image>();
            plate.sprite = sprite;
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = sliceScale;
            plate.raycastTarget = false;
            return plate;
        }

        private static Text CreateText(RectTransform parent, string objectName, int fontSize, Font font, Color colour)
            => UiTextFactory.Create(parent, objectName, fontSize, FontStyle.Normal, colour, font);
    }
}
