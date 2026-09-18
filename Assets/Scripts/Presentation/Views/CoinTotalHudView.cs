using System.Text;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The running coin total as the top bar's left-hand pill (issue #265): a card-coloured pill
    /// ringed in the accent, the authored gold coin on its left, the balance in the display face,
    /// and a green "+" disc on its right. Binds to <see cref="ProfileModel.CoinBalance"/>.
    /// <para>
    /// Reactive, never polled — which is the whole point of it. Coins arrive from three unrelated
    /// places (a score conversion, a rewarded ad, and a destroyed
    /// <see cref="MustyBlockBlast.Core.SpecialCellKind.Coin"/> cell mid-placement), and the last of
    /// those happens with no screen open and nothing else to repaint. Subscribing to the property means
    /// this label cannot be stale for a frame, whichever of the three moved it.
    /// </para>
    /// <para>
    /// Deliberately always visible, unlike <see cref="DoubleMultiplierHudView"/>: a wallet is a standing
    /// fact rather than a temporary state, and a total that vanished at zero would read as "coins are
    /// not a thing in this game" to exactly the player who has not earned one yet. The "+" disc is
    /// the mockup's affordance only — the pill routes no tap of its own today.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoinTotalHudView : MonoBehaviour
    {
        /// <summary>Fallback disc colour when no coin sprite is assigned; the same hue
        /// <c>BoardView.CoinIconTint</c> paints a coin cell with.</summary>
        private static readonly Color CoinTint = new Color(1f, 0.82f, 0.25f, 1f);

        /// <summary>Which theme kind's fill the "+" disc takes: the fifth kind — every season's green.</summary>
        private const int PLUS_KIND = 5;

        /// <summary>How far the accent is pulled toward black for the balance's ink (the mockup's #91700F).</summary>
        private const float VALUE_SHADE = 0.35f;

        /// <summary>Thickness of the "+" strokes as a fraction of the disc.</summary>
        private const float PLUS_STROKE = 0.16f;
        private const float PLUS_LENGTH = 0.5f;

        [Header("Layout")]
        [Tooltip("Offset of the pill's top-left corner from the safe area's top-left corner, in reference pixels.")]
        [SerializeField] private Vector2 _cornerOffset = new Vector2(24f, -32f);

        [SerializeField] private float _pillHeight = 92f;

        [Tooltip("Thickness of the accent ring around the pill.")]
        [SerializeField] private float _ringThickness = 8f;

        [SerializeField] private float _coinSize = 70f;
        [SerializeField] private float _plusDiscSize = 56f;
        [SerializeField] private float _paddingLeft = 12f;
        [SerializeField] private float _paddingRight = 16f;
        [SerializeField] private float _gap = 14f;
        [SerializeField] private int _fontSize = 47;

        [Header("Art")]
        [Tooltip("Full-colour coin face. Drawn untinted. Leave empty to fall back to a plain gold disc.")]
        [SerializeField] private Sprite _coinSprite;

        [Tooltip("The chunky display face for the balance. Falls back to the builtin font when unassigned.")]
        [SerializeField] private Font _displayFont;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        private ProfileModel _profileModel;
        private SettingsModel _settingsModel;

        private RectTransform _rect;
        private Image _shadowImage;
        private Image _ringImage;
        private Image _plateImage;
        private RectTransform _coinRect;
        private Image _coinImage;
        private Text _totalText;
        private RectTransform _totalRect;
        private RectTransform _plusRect;
        private Image _plusDiscImage;
        private Image _plusDiscLipImage;
        private Image _plusBarHorizontal;
        private Image _plusBarVertical;

        [Inject]
        public void Construct(ProfileModel profileModel, SettingsModel settingsModel)
        {
            _profileModel = profileModel;
            _settingsModel = settingsModel;
        }

        private void Awake()
        {
            BuildPill();

            // Painted by the subscription in Start rather than left blank: the balance is loaded before
            // any View starts, so there is no "unknown" state to render.
            RenderTotal(0);
        }

        private void Start()
        {
            if (_profileModel == null || _settingsModel == null)
            {
                Debug.LogError(
                    $"{nameof(CoinTotalHudView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // Fires immediately with the current balance, so the label is correct from the first frame
            // and no separate initial read is needed.
            _profileModel.CoinBalance.Subscribe(RenderTotal).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _shadowImage.color = theme.CardShadow;
            _ringImage.color = theme.Accent;
            _plateImage.color = theme.CardBackground;
            _totalText.color = HudChrome.Darken(theme.Accent, VALUE_SHADE);

            Color green = theme.GetFill(PLUS_KIND);
            _plusDiscImage.color = green;
            _plusDiscLipImage.color = theme.GetShade(PLUS_KIND);
            _plusBarHorizontal.color = Color.white;
            _plusBarVertical.color = Color.white;

            // The coin is a full-colour authored sprite, so it is drawn white and never themed; the
            // fallback disc is the fixed gold a coin cell wears.
            _coinImage.color = _coinSprite != null ? Color.white : CoinTint;
        }

        /// <summary>One integer through the shared builder, so a change allocates the one string it
        /// hands to the label rather than the several a concatenation would. The pill is re-measured
        /// around the number: "1240" is wider than "0".</summary>
        private void RenderTotal(int balance)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(balance);
            _totalText.text = _stringBuilder.ToString();
            LayOut();
        }

        private void LayOut()
        {
            float textWidth = _totalText.preferredWidth;
            float width = _paddingLeft + _coinSize + _gap + textWidth + _gap + _plusDiscSize + _paddingRight;
            var size = new Vector2(width, _pillHeight);

            _rect.sizeDelta = size;
            _shadowImage.rectTransform.sizeDelta = size;
            _ringImage.rectTransform.sizeDelta = size;
            _plateImage.rectTransform.sizeDelta = size - (Vector2.one * (_ringThickness * 2f));

            float x = (-width * 0.5f) + _paddingLeft;
            _coinRect.anchoredPosition = new Vector2(x + (_coinSize * 0.5f), 0f);
            x += _coinSize + _gap;
            _totalRect.anchoredPosition = new Vector2(x, 0f);
            x += textWidth + _gap;
            _plusRect.anchoredPosition = new Vector2(x + (_plusDiscSize * 0.5f), 0f);
        }

        /// <summary>Built before the theme is known; the theme subscription in Start paints it.</summary>
        private void BuildPill()
        {
            _rect = (RectTransform)transform;
            _rect.anchorMin = new Vector2(0f, 1f);
            _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            _rect.sizeDelta = new Vector2(_pillHeight * 3f, _pillHeight);
            _rect.anchoredPosition = _cornerOffset;

            // Every size here is in canvas reference units, so the pill owns its own scale rather than
            // inheriting whatever the scene object happened to be created with.
            _rect.localScale = Vector3.one;

            float radius = _pillHeight * 0.5f;
            _shadowImage = HudChrome.BuildRounded(
                _rect, "Shadow", _rect.sizeDelta, new Vector2(0f, -HudChrome.PILL_SHADOW_DROP), radius);
            _ringImage = HudChrome.BuildRounded(_rect, "Ring", _rect.sizeDelta, Vector2.zero, radius);
            _plateImage = HudChrome.BuildRounded(_rect, "Plate", _rect.sizeDelta, Vector2.zero, radius - _ringThickness);

            _coinImage = HudChrome.BuildGlyph(
                _rect, "CoinFace", _coinSprite != null ? _coinSprite : UiSpriteFactory.Circle,
                new Vector2(_coinSize, _coinSize), Vector2.zero);
            _coinRect = (RectTransform)_coinImage.transform;

            _totalText = HudChrome.CreateLabel(
                _rect, "CoinTotalValue", _fontSize, FontStyle.Normal, TextAnchor.MiddleLeft, Vector2.zero, _displayFont);
            _totalRect = (RectTransform)_totalText.transform;

            // The "+" disc: a shade-coloured lip under a green disc, with two white bars over it.
            _plusRect = HudChrome.CreateRect(_rect, "PlusDisc", new Vector2(_plusDiscSize, _plusDiscSize), Vector2.zero);
            _plusDiscLipImage = HudChrome.BuildCircle(_plusRect, "Lip", _plusDiscSize, new Vector2(0f, -4f));
            _plusDiscImage = HudChrome.BuildCircle(_plusRect, "Disc", _plusDiscSize, Vector2.zero);

            float stroke = _plusDiscSize * PLUS_STROKE;
            float length = _plusDiscSize * PLUS_LENGTH;
            _plusBarHorizontal = HudChrome.BuildRounded(_plusRect, "PlusH", new Vector2(length, stroke), Vector2.zero, stroke * 0.5f);
            _plusBarVertical = HudChrome.BuildRounded(_plusRect, "PlusV", new Vector2(stroke, length), Vector2.zero, stroke * 0.5f);
        }
    }
}
