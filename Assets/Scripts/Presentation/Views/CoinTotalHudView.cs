using System.Text;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The running coin total, pinned to the top-right corner of the HUD: a coin disc and the number
    /// beside it. Binds to <see cref="ProfileModel.CoinBalance"/>.
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
    /// The icon is drawn procedurally from the shared sprite atlas-free UI sprite set
    /// (<see cref="UiSpriteFactory.Circle"/>) and tinted, exactly as a special cell's board icon is
    /// (<c>BoardView.IconTint</c>): one shared sprite plus a colour, so the icon costs no new texture
    /// and no import step.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoinTotalHudView : MonoBehaviour
    {
        /// <summary>
        /// Colour the coin disc is drawn in. Fixed and unthemed for the reason a board icon's tint is:
        /// it is a readability mark rather than decoration, and gold is what makes a disc read as a coin
        /// without any theme having to author (and keep legible) a colour for it. The same hue
        /// <c>BoardView.CoinIconTint</c> paints a coin cell with, so the thing on the board and the
        /// thing it pays into are recognisably the same currency.
        /// </summary>
        private static readonly Color CoinTint = new Color(1f, 0.82f, 0.25f, 1f);

        [Header("Layout")]
        // X clears the right-hand button column, whose buttons sit at x -60 and are 112 wide (see
        // SettingsButtonView and its siblings), so -200 keeps the number off them at any aspect ratio.
        // Y lines the total up with the topmost of those buttons.
        [Tooltip("Offset from the top-right corner of the parent canvas. X is measured leftwards.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(-200f, -52f);

        [SerializeField] private float _iconSize = 56f;

        [SerializeField] private int _fontSize = 56;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        private ProfileModel _profileModel;
        private Image _coinIcon;
        private Text _totalText;

        [Inject]
        public void Construct(ProfileModel profileModel)
        {
            _profileModel = profileModel;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            PinToTopRight(rect, _cornerOffset);
            rect.sizeDelta = new Vector2(280f, Mathf.Max(_iconSize, _fontSize));

            // The number first, hugging the corner, with the disc to its left: the total is what grows,
            // so anchoring it to the corner keeps a four-digit balance from pushing the icon off screen.
            _totalText = UiTextFactory.Create(rect, "CoinTotalValue", _fontSize, FontStyle.Bold, CoinTint);
            var textRect = (RectTransform)_totalText.transform;
            PinToTopRight(textRect, Vector2.zero);
            _totalText.alignment = TextAnchor.UpperRight;

            var iconObject = new GameObject("CoinIcon", typeof(RectTransform), typeof(Image));
            _coinIcon = iconObject.GetComponent<Image>();
            _coinIcon.sprite = UiSpriteFactory.Circle;
            _coinIcon.type = Image.Type.Simple;
            _coinIcon.color = CoinTint;
            _coinIcon.raycastTarget = false;

            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(rect, false);
            PinToTopRight(iconRect, new Vector2(-(_iconSize + 96f), 0f));
            iconRect.sizeDelta = new Vector2(_iconSize, _iconSize);

            // Painted by the subscription in Start rather than left blank: the balance is loaded before
            // any View starts, so there is no "unknown" state to render.
            RenderTotal(0);
        }

        private void Start()
        {
            if (_profileModel == null)
            {
                Debug.LogError(
                    $"{nameof(CoinTotalHudView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            // Fires immediately with the current balance, so the label is correct from the first frame
            // and no separate initial read is needed.
            _profileModel.CoinBalance.Subscribe(RenderTotal).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>Anchors <paramref name="rect"/> to its parent's top-right corner. Stated once
        /// because the root, the label and the icon all share it, and a corner-pinned HUD element that
        /// disagreed with its own children about the corner would drift at other aspect ratios.</summary>
        private static void PinToTopRight(RectTransform rect, Vector2 offset)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = offset;
        }

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
