using System.Text;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The running coin total, drawn as one authored gold coin with the number centred on its face,
    /// stacked under the settings and level-path icons in the right-hand HUD column. Binds to
    /// <see cref="ProfileModel.CoinBalance"/>.
    /// <para>
    /// Reactive, never polled — which is the whole point of it. Coins now arrive from three unrelated
    /// places (a score conversion, a rewarded ad, and a destroyed
    /// <see cref="MustyBlockBlast.Core.SpecialCellKind.Coin"/> cell mid-placement), and the last of
    /// those happens with no screen open and nothing else to repaint. Subscribing to the property means
    /// this label cannot be stale for a frame, whichever of the three moved it, and means no future
    /// faucet has to remember to tell the HUD about itself.
    /// </para>
    /// <para>
    /// Deliberately always visible, unlike <see cref="DoubleMultiplierHudView"/>: a wallet is a standing
    /// fact rather than a temporary state, and a total that vanished at zero would read as "coins are
    /// not a thing in this game" to exactly the player who has not earned one yet.
    /// </para>
    /// <para>
    /// The coin is a full-colour authored sprite rather than a tinted primitive, so it is drawn with
    /// <see cref="Color.white"/> and never themed; the number on it is a fixed dark ink for the same
    /// reason a board icon's tint is fixed — legibility on gold, not decoration. Without a sprite
    /// assigned it falls back to the tinted disc the HUD drew before any art existed.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoinTotalHudView : MonoBehaviour
    {
        /// <summary>Fallback disc colour when no coin sprite is assigned; the same hue
        /// <c>BoardView.CoinIconTint</c> paints a coin cell with.</summary>
        private static readonly Color CoinTint = new Color(1f, 0.82f, 0.25f, 1f);

        /// <summary>Ink for the number on the coin face. Dark and fixed: it has to read on gold in
        /// every theme.</summary>
        private static readonly Color NumberInk = new Color(0.36f, 0.22f, 0.05f, 1f);

        [Header("Layout")]
        // Sits on the band directly above the board card, flush with its right edge, with the
        // objective icons on the same band at the left — not stacked under the right-column buttons
        // any more: three 112px icons plus their gaps never fit between the board's top edge and the
        // safe area on a tall phone, and the coin was the one being pushed into the card (issue #229).
        [Tooltip("Right edge of the coin, in reference pixels from the canvas's right edge. Matches " +
            "SettingsButtonView and LevelPathButtonView's corner offset so the three read as one " +
            "right-aligned column.")]
        [SerializeField] private float _rightInset = 60f;

        [Tooltip("Gap between the board card's top edge and the bottom of the coin, in reference " +
            "pixels. The coin hangs from the board (see BoardView.StandardCardTopEdge), not from the " +
            "screen top, so it can never be pushed down into the card by a taller screen or a notch.")]
        [SerializeField] private float _gapAboveBoard = 12f;

        [Tooltip("Side of the coin, in reference pixels. Matches the icons above it and the power-up strip.")]
        [SerializeField] private float _coinSize = 104f;

        [SerializeField] private int _fontSize = 40;

        [Header("Art")]
        [Tooltip("Full-colour coin face. Drawn untinted; the balance is centred on it. Leave empty to " +
            "fall back to a plain gold disc.")]
        [SerializeField] private Sprite _coinSprite;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        private BoardView _boardView;
        private ProfileModel _profileModel;
        private RectTransform _rect;
        private Text _totalText;

        [Inject]
        public void Construct(BoardView boardView, ProfileModel profileModel)
        {
            _boardView = boardView;
            _profileModel = profileModel;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            _rect = rect;

            // Right edge and vertical centre, the board card's own vertical anchor. Bottom-right pivot
            // so the position set in Start is simply "this far in, this far above the board".
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(_coinSize, _coinSize);

            // Every size here is in canvas reference units, so the coin owns its own scale rather than
            // inheriting whatever the scene object happened to be created with.
            rect.localScale = Vector3.one;

            var coinObject = new GameObject("CoinFace", typeof(RectTransform), typeof(Image));
            var coinRect = (RectTransform)coinObject.transform;
            coinRect.SetParent(rect, false);
            coinRect.anchorMin = Vector2.zero;
            coinRect.anchorMax = Vector2.one;
            coinRect.offsetMin = Vector2.zero;
            coinRect.offsetMax = Vector2.zero;

            var coinImage = coinObject.GetComponent<Image>();
            coinImage.type = Image.Type.Simple;
            coinImage.preserveAspect = true;
            coinImage.raycastTarget = false;

            if (_coinSprite != null)
            {
                coinImage.sprite = _coinSprite;
                coinImage.color = Color.white;
            }
            else
            {
                // The circle sprite has no border, so it must never be sliced.
                coinImage.sprite = UiSpriteFactory.Circle;
                coinImage.color = CoinTint;
            }

            // Centred on the face, created after the coin so it draws over it.
            _totalText = UiTextFactory.Create(rect, "CoinTotalValue", _fontSize, FontStyle.Bold, NumberInk);
            var textRect = (RectTransform)_totalText.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            _totalText.alignment = TextAnchor.MiddleCenter;
            _totalText.resizeTextForBestFit = true;
            _totalText.resizeTextMaxSize = _fontSize;
            _totalText.resizeTextMinSize = Mathf.Max(12, _fontSize / 3);

            // Painted by the subscription in Start rather than left blank: the balance is loaded before
            // any View starts, so there is no "unknown" state to render.
            RenderTotal(0);
        }

        private void Start()
        {
            if (_boardView == null || _profileModel == null)
            {
                Debug.LogError(
                    $"{nameof(CoinTotalHudView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Positioned here rather than in Awake: the board's layout is read off an injected View,
            // and injection has only certainly happened by Start.
            _rect.anchoredPosition = new Vector2(-_rightInset, _boardView.StandardCardTopEdge + _gapAboveBoard);

            // Fires immediately with the current balance, so the label is correct from the first frame
            // and no separate initial read is needed.
            _profileModel.CoinBalance.Subscribe(RenderTotal).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>One integer through the shared builder, so a change allocates the one string it
        /// hands to the label rather than the several a concatenation would.</summary>
        private void RenderTotal(int balance)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(balance);
            _totalText.text = _stringBuilder.ToString();
        }
    }
}
