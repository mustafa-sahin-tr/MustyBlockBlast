using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The hub screen: one tabbed surface over the five cards that used to each have their own
    /// persistent HUD icon. The settings cog is now the only way in, and it lands on
    /// <see cref="HubTab.Settings"/> — so every section is exactly two taps from the board.
    /// <para>
    /// This View owns no content of its own beyond a header (the striped awning across the top, the
    /// active tab's name and the one round close button that now shuts the hub, whichever tab is
    /// open) and the tab bar beneath it. Each tab
    /// *is* the existing panel View, opened and closed unchanged, which is what keeps this a
    /// navigation change rather than five screen rewrites — each card's own title and close button
    /// are simply hidden once it is opened here, since the header above now says both. It is the
    /// single modal owner of those five: <see cref="BoardInputView"/> has one gate for the hub where
    /// it used to have five, so the five can no longer stack on each other.
    /// </para>
    /// <para>
    /// Switching tabs closes the outgoing card and opens the incoming one, which flips
    /// <see cref="Gameplay.Systems.TimerRunSystem.SetMenuPaused"/> off and straight back on inside the
    /// one call. That is deliberate and provably free: nothing reads the pause flag between the two
    /// writes (the countdown and the 2x window both read it on their own tick, and
    /// <c>ObjectiveSystem</c> accumulates paused spans, so a span closed and reopened at the same
    /// timestamp adds exactly the seconds it would have anyway). Going through the panels' own
    /// open/close rather than reaching past them into their roots is what keeps each card's own
    /// on-open work — the leaderboard's fetch, the badge wall's repaint — running on every tab visit.
    /// </para>
    /// <para>
    /// Like the rest of the HUD this View never raycasts: <see cref="BoardInputView"/> owns the pointer
    /// and forwards taps to <see cref="HandleTap"/> while the hub is open. The tab bar is tested first,
    /// then the tap falls through to the open card — so a card never sees, and never dismisses on, a
    /// tap meant for the bar above it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HubPanelView : MonoBehaviour
    {
        /// <summary>Display order in the bar. Mirrors <see cref="HubTab"/>'s declaration order, and is a
        /// static array rather than <c>Enum.GetValues</c> so building the bar allocates nothing.</summary>
        private static readonly HubTab[] TabOrder =
        {
            HubTab.Settings,
            HubTab.PowerUpShop,
            HubTab.Leaderboard,
            HubTab.Profile,
            HubTab.Badges,
        };

        private const int TAB_COUNT = 5;

        /// <summary>Padding between the strip's edge and the outermost tab plate.</summary>
        private const float STRIP_PADDING = 12f;

        /// <summary>Rendered corner radius of a tab plate, in reference pixels — the design's 14px, used
        /// directly. The shared rounded sprite bakes its radius at <see cref="UiSpriteFactory.ROUNDED_RADIUS"/>,
        /// so the slice multiplier is derived from the two rather than tuned by eye: the header, strip
        /// and close cross keep the HUD-wide ~5px look, only the tabs round to this.</summary>
        private const float TAB_CORNER_RADIUS = 14f;

        /// <summary>Vertical offset of a tab's own shadow, so the plate reads as lifted off the strip.</summary>
        private const float TAB_SHADOW_OFFSET = 4f;

        /// <summary>How far the shadow outgrows the plate on every side.</summary>
        private const float TAB_SHADOW_SPREAD = 6f;

        /// <summary>How much of the theme's card shadow an active tab casts versus an inactive one. The
        /// selected plate is lifted, the rest sit nearly flat — the design's shadow-strength contrast,
        /// with the shape itself identical in both states.</summary>
        private const float ACTIVE_TAB_SHADOW_STRENGTH = 2.5f;
        private const float INACTIVE_TAB_SHADOW_STRENGTH = 0.8f;

        /// <summary>Overlap between the bar's bottom edge and the active card's top edge, and likewise
        /// between the header's bottom edge and the bar's top edge. Small and negative rather than
        /// zero, so a seam never opens a hairline gap under canvas-scaler rounding — each pair is meant
        /// to read as one surface, not as two plates touching at a knife edge.</summary>
        private const float BAR_CARD_OVERLAP = 2f;

        /// <summary>The striped awning across the very top of the hub (issue #235): the stall's canopy,
        /// drawn once here above the tab bar rather than inside any one card, so every tab sits under
        /// the same canopy. Its height is the sprite's aspect at the 880-wide card.</summary>
        private const float AWNING_HEIGHT = 108f;

        /// <summary>The awning outgrows the header plate a little on each side so its scalloped edge
        /// hangs over the plate's rounded corners instead of stopping short of them.</summary>
        private const float AWNING_OVERHANG = 6f;

        /// <summary>The title row under the awning: the active tab's name, centred.</summary>
        private const float TITLE_ROW_HEIGHT = 112f;

        private const float HEADER_HEIGHT = AWNING_HEIGHT + TITLE_ROW_HEIGHT;
        private const float HEADER_CLOSE_SIZE = 96f;

        /// <summary>How far the round close button's centre sits in from the header's top-right corner,
        /// so the disc overlaps the corner the way the reference's does.</summary>
        private const float HEADER_CLOSE_CORNER_INSET = 40f;
        private const int HEADER_FONT_SIZE = 60;

        /// <summary>How much shorter an inactive tab plate is than the active one. The active tab is
        /// the full <see cref="_tabSize"/>; its siblings drop this much from their top edge, so the
        /// selected tab reads as lifted above the row (issue #235).</summary>
        private const float TAB_INACTIVE_DROP = 16f;

        /// <summary>Height of the solid lip drawn under every tab plate — a shade of the plate's own
        /// colour, so the tab reads as a chunky 3D key rather than a flat swatch.</summary>
        private const float TAB_LIP_HEIGHT = 8f;

        /// <summary>How far the accent is pulled toward black for the active tab's lip and the title:
        /// the accent's own shade, derived rather than taken from a kind (the kinds' hues are ordered
        /// differently in every season's palette) and not blended with Ink (a blue ink would turn the
        /// gold olive).</summary>
        private const float ACCENT_SHADE = 0.35f;

        /// <summary>Mean of the five cards' own configured heights. Used only to pick a resting
        /// position for the header+bar that reads as vertically centred for a typical tab — actual
        /// per-tab (and, within Settings, per-sub-screen) card alignment beneath the bar is always
        /// exact and dynamic (see <see cref="AlignCardBelowBar"/>), never derived from this.</summary>
        private const float REFERENCE_CARD_HEIGHT = 1140f;

        /// <summary>Header title key per tab, in <see cref="HubTab"/> declaration order. Translated on
        /// every open and again on every locale change, so the header follows the language setting
        /// the Settings tab itself changes.</summary>
        private static readonly string[] TabTitleKeys =
        {
            LocalizationKeys.SETTINGS_TITLE,
            LocalizationKeys.HUB_TAB_POWER_UP_SHOP,
            LocalizationKeys.HUB_TAB_LEADERBOARD,
            LocalizationKeys.HUB_TAB_PROFILE,
            LocalizationKeys.HUB_TAB_BADGES,
        };

        [Header("Layout")]
        [Tooltip("Minimum gap between the top of the safe area and the top of the header, in reference "
            + "pixels — a floor under the computed centred position, not the position itself.")]
        [SerializeField] private float _topInset = 28f;
        [SerializeField] private Vector2 _tabSize = new Vector2(176f, 118f);
        [SerializeField] private float _tabGap = 8f;

        [Tooltip("Size of the glyph box inside a tab plate, in reference pixels.")]
        [SerializeField] private float _glyphSize = 68f;

        [Header("Art")]
        [Tooltip("The chunky display face for the header title. Falls back to the built-in runtime font "
            + "when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("The striped awning drawn across the top of the hub, above the tab bar. Shared with the "
            + "shop's stall art; drawn untinted.")]
        [SerializeField] private Sprite _awningSprite;

        [Header("Tab icons")]
        [Tooltip("White-on-transparent glyphs, one per HubTab in declaration order (Settings, Power-up Shop, "
            + "Leaderboard, Profile, Badges). Tinted at runtime from the theme, so keep them pure white.")]
        [SerializeField] private Sprite[] _tabIcons = new Sprite[TAB_COUNT];

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly TabEntry[] _tabs = new TabEntry[TAB_COUNT];

        private SettingsModel _settingsModel;
        private LocalizationModel _localizationModel;
        private LocalizationSystem _localizationSystem;
        private SettingsPanelView _settingsPanelView;
        private PowerUpShopView _powerUpShopView;
        private LeaderboardPanelView _leaderboardPanelView;
        private ProfilePanelView _profilePanelView;
        private BadgesPanelView _badgesPanelView;

        private Canvas _canvas;
        private GameObject _barRoot;
        private RectTransform _barRect;
        private float _stripHeight;
        private Image _stripImage;
        private Image _stripShadowImage;

        private GameObject _headerRoot;
        private RectTransform _headerRect;
        private Image _headerPlateImage;
        private Text _headerTitleText;
        private RectTransform _headerCloseRect;
        private Image _headerCloseDisc;
        private Image _headerCloseDiscShadow;
        private Image[] _headerCloseInk;
        private Image _awningImage;

        private ThemeDefinition _currentTheme;

        /// <summary>Reused every reposition so tracking the active card's edge allocates nothing.</summary>
        private readonly Vector3[] _cardCornerBuffer = new Vector3[4];

        private bool _isOpen;
        private HubTab _activeTab = HubTab.Settings;

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            LocalizationModel localizationModel,
            LocalizationSystem localizationSystem,
            SettingsPanelView settingsPanelView,
            PowerUpShopView powerUpShopView,
            LeaderboardPanelView leaderboardPanelView,
            ProfilePanelView profilePanelView,
            BadgesPanelView badgesPanelView)
        {
            _settingsModel = settingsModel;
            _localizationModel = localizationModel;
            _localizationSystem = localizationSystem;
            _settingsPanelView = settingsPanelView;
            _powerUpShopView = powerUpShopView;
            _leaderboardPanelView = leaderboardPanelView;
            _profilePanelView = profilePanelView;
            _badgesPanelView = badgesPanelView;
        }

        /// <summary>True while the hub is showing. Read by <see cref="BoardInputView"/>, whose one gate
        /// for this replaced the five it used to keep for the cards underneath.</summary>
        internal bool IsOpen => _isOpen;

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_settingsModel == null || _localizationModel == null || _localizationSystem == null
                || _settingsPanelView == null || _powerUpShopView == null
                || _leaderboardPanelView == null || _profilePanelView == null || _badgesPanelView == null)
            {
                Debug.LogError(
                    $"{nameof(HubPanelView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            BuildBar();
            _barRoot.SetActive(false);
            _headerRoot.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
            _localizationModel.CurrentLocale.Subscribe(OnLocaleChanged).AddTo(_disposables);
        }

        private void OnLocaleChanged(LocaleDefinition locale) => RefreshHeaderTitle();

        private void RefreshHeaderTitle()
        {
            if (_headerTitleText == null)
            {
                return;
            }

            // HubTab's declaration order is the display order (see its own doc comment), so the enum
            // value indexes the key table directly.
            _headerTitleText.text = _localizationSystem.Translate(TabTitleKeys[(int)_activeTab]);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>Opens the hub on its default section. The settings cog's whole behaviour.</summary>
        internal void Open() => Open(HubTab.Settings);

        /// <summary>
        /// Opens the hub on <paramref name="tab"/>. Re-opening an already-open hub switches to that tab
        /// rather than stacking a second open, so the menu pause can only ever be taken once.
        /// </summary>
        internal void Open(HubTab tab)
        {
            if (_barRoot == null)
            {
                return;
            }

            if (_isOpen)
            {
                SelectTab(tab);
                return;
            }

            _isOpen = true;
            _activeTab = tab;
            _barRoot.SetActive(true);
            _headerRoot.SetActive(true);
            OpenPanel(tab);
            RefreshTabs();
            RepositionBarAboveActiveCard();

            // After the card's own SetAsLastSibling, never before: the bar has to draw over the card it
            // is labelling, and the card raises itself as it opens.
            transform.SetAsLastSibling();
        }

        /// <summary>
        /// Routes a tap while the hub is open. The header's own close cross wins first — it is drawn
        /// above everything else in the hub, so it must be tested before anything it visually
        /// overlaps — then the bar, so a tab tap can never reach the card underneath and be read there
        /// as a dismissing scrim tap. Anything else is the card's.
        /// </summary>
        internal void HandleTap(Vector2 screenPosition)
        {
            if (!_isOpen)
            {
                return;
            }

            Camera eventCamera = EventCamera();

            if (RectTransformUtility.RectangleContainsScreenPoint(_headerCloseRect, screenPosition, eventCamera))
            {
                Close();
                return;
            }

            for (int tabIndex = 0; tabIndex < _tabs.Length; tabIndex++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(
                        _tabs[tabIndex].Rect, screenPosition, eventCamera))
                {
                    SelectTab(_tabs[tabIndex].Tab);
                    return;
                }
            }

            // The profile card's badge preview is a shortcut into the Badges tab (issue #235). Tested
            // here, not in the profile card, because switching tabs is the hub's move and the card
            // must not reach up into the hub that opened it.
            if (_activeTab == HubTab.Profile
                && _profilePanelView.BadgeWallRect != null
                && RectTransformUtility.RectangleContainsScreenPoint(
                    _profilePanelView.BadgeWallRect, screenPosition, eventCamera))
            {
                SelectTab(HubTab.Badges);
                return;
            }

            HandlePanelTap(_activeTab, screenPosition);

            // The card owns its own close cross and its own dismissing scrim, so it can shut itself
            // during that tap. The bar has to follow it down — and by closing itself the card has
            // already released the menu pause, so Close() here would only double the work.
            if (!IsPanelOpen(_activeTab))
            {
                _isOpen = false;
                _barRoot.SetActive(false);
                _headerRoot.SetActive(false);
                return;
            }

            // Still open, but the tap may have resized the card in place — the settings card does this
            // on every sub-screen it navigates to. Following that resize is what keeps the bar flush
            // against it rather than just against the screen it happened to be opened on.
            RepositionBarAboveActiveCard();
        }

        /// <summary>Shuts the hub and the card it is showing. A no-op when already closed, so every path
        /// that could end the hub may call it unconditionally.</summary>
        internal void Close()
        {
            if (!_isOpen)
            {
                return;
            }

            _isOpen = false;
            ClosePanel(_activeTab);
            _barRoot.SetActive(false);
            _headerRoot.SetActive(false);
        }

        private void SelectTab(HubTab tab)
        {
            if (tab == _activeTab && IsPanelOpen(tab))
            {
                return;
            }

            ClosePanel(_activeTab);
            _activeTab = tab;
            OpenPanel(tab);
            RefreshTabs();
            RepositionBarAboveActiveCard();
            transform.SetAsLastSibling();
        }

        /// <summary>
        /// Hides the parts of the freshly opened card that the header above the bar now duplicates.
        /// Every card's own close cross goes — the header's is the one close button for whichever tab
        /// is open. Every card's own title goes too, except the Badges tile-wall's: that one is not a
        /// title but a live "unlocked / total" counter, so it stays as content the header cannot say
        /// for it — <see cref="ActiveHeaderTitleText"/> returns null for that tab rather than exposing
        /// an accessor <see cref="BadgesPanelView"/> would have to fight to keep showing.
        /// <para>
        /// Done here rather than at build time: hiding either for good would also hide it the one time
        /// it still matters, a tap that lands in that same corner before this method has run for the
        /// freshly opened card.
        /// </para>
        /// </summary>
        private void HideActiveCardChrome()
        {
            RectTransform closeButton = ActiveCloseButtonRect(_activeTab);
            if (closeButton != null)
            {
                closeButton.gameObject.SetActive(false);
            }

            Text title = ActiveHeaderTitleText(_activeTab);
            if (title != null)
            {
                title.gameObject.SetActive(false);
            }
        }

        private RectTransform ActiveCloseButtonRect(HubTab tab)
        {
            switch (tab)
            {
                case HubTab.PowerUpShop:
                    return _powerUpShopView.CloseButtonRect;
                case HubTab.Leaderboard:
                    return _leaderboardPanelView.CloseButtonRect;
                case HubTab.Profile:
                    return _profilePanelView.CloseButtonRect;
                case HubTab.Badges:
                    return _badgesPanelView.CloseButtonRect;
                default:
                    return _settingsPanelView.CloseButtonRect;
            }
        }

        private Text ActiveHeaderTitleText(HubTab tab)
        {
            switch (tab)
            {
                case HubTab.PowerUpShop:
                    return _powerUpShopView.HeaderTitleText;
                case HubTab.Leaderboard:
                    return _leaderboardPanelView.HeaderTitleText;
                case HubTab.Profile:
                    return _profilePanelView.HeaderTitleText;
                case HubTab.Badges:
                    // The counter, not a title — left showing (see the doc comment above).
                    return null;
                default:
                    return _settingsPanelView.HeaderTitleText;
            }
        }

        /// <summary>Fits the bar to the active card: matches its width, and slides the card's own
        /// position so the card's top edge sits flush against the bar's fixed bottom edge. The bar
        /// itself never moves — only the card does — which is what keeps the bar rock-still while
        /// switching between tabs (or Settings sub-screens) whose cards are taller or shorter than one
        /// another; moving the bar to chase each card's height read as the whole hub jumping around.
        /// Measured fresh every time rather than assumed from a fixed size, since card size varies per
        /// tab and, within Settings, per sub-screen.</summary>
        private void RepositionBarAboveActiveCard()
        {
            RectTransform card = ActiveCardRect(_activeTab);
            if (card == null || _barRect == null)
            {
                return;
            }

            ResizeBarToWidth(card.rect.width);
            AlignCardBelowBar(card);
        }

        /// <summary>
        /// The bar's bottom edge, in its own local space, is always <c>(0, -_stripHeight)</c> since it
        /// is pivoted at its own top. This walks that point out to world space and back into the card's
        /// parent's local space — a sibling under the same canvas needs no scale correction for this,
        /// but the round trip is what keeps the two frames' different pivots from having to be reasoned
        /// about by hand. The card's own pivot is its centre, so its anchored Y is that target minus
        /// half its height, less a small overlap so canvas-scaler rounding can never open a seam.
        /// </summary>
        private void AlignCardBelowBar(RectTransform card)
        {
            Vector3 barBottomWorld = _barRect.TransformPoint(new Vector3(0f, -_stripHeight, 0f));

            Camera eventCamera = EventCamera();
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, barBottomWorld);

            var cardParent = (RectTransform)card.parent;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    cardParent, screenPoint, eventCamera, out Vector2 localPoint))
            {
                return;
            }

            card.anchoredPosition = new Vector2(
                card.anchoredPosition.x, localPoint.y - BAR_CARD_OVERLAP - (card.rect.height * 0.5f));
        }

        /// <summary>Resizes the strip and re-spaces its five tabs to fill <paramref name="cardWidth"/>
        /// exactly, keeping the same edge padding and inter-tab gap the bar was built with. Each tab's
        /// glyph is centred on its own plate via anchors rather than an absolute offset, so shrinking or
        /// growing the plate moves nothing inside it.</summary>
        private void ResizeBarToWidth(float cardWidth)
        {
            _barRect.sizeDelta = new Vector2(cardWidth, _barRect.sizeDelta.y);
            _stripImage.rectTransform.sizeDelta = new Vector2(cardWidth, _stripImage.rectTransform.sizeDelta.y);
            _stripShadowImage.rectTransform.sizeDelta =
                new Vector2(cardWidth + 10f, _stripShadowImage.rectTransform.sizeDelta.y);

            float tabWidth = (cardWidth - (STRIP_PADDING * 2f) - (_tabGap * (TAB_COUNT - 1))) / TAB_COUNT;
            float pitch = tabWidth + _tabGap;

            for (int tabIndex = 0; tabIndex < _tabs.Length; tabIndex++)
            {
                RectTransform tabRect = _tabs[tabIndex].Rect;
                tabRect.sizeDelta = new Vector2(tabWidth, tabRect.sizeDelta.y);
                tabRect.anchoredPosition = new Vector2((tabIndex - ((TAB_COUNT - 1) * 0.5f)) * pitch, 0f);
            }

            _headerRect.sizeDelta = new Vector2(cardWidth, _headerRect.sizeDelta.y);
            _headerPlateImage.rectTransform.sizeDelta = new Vector2(cardWidth, _headerPlateImage.rectTransform.sizeDelta.y);

            _awningImage.rectTransform.sizeDelta =
                new Vector2(cardWidth + (AWNING_OVERHANG * 2f), _awningImage.rectTransform.sizeDelta.y);

            float headerHalfWidth = cardWidth * 0.5f;
            _headerCloseRect.anchoredPosition = new Vector2(
                headerHalfWidth - HEADER_CLOSE_CORNER_INSET, (HEADER_HEIGHT * 0.5f) - HEADER_CLOSE_CORNER_INSET);
        }

        private RectTransform ActiveCardRect(HubTab tab)
        {
            switch (tab)
            {
                case HubTab.PowerUpShop:
                    return _powerUpShopView.CardRect;
                case HubTab.Leaderboard:
                    return _leaderboardPanelView.CardRect;
                case HubTab.Profile:
                    return _profilePanelView.CardRect;
                case HubTab.Badges:
                    return _badgesPanelView.CardRect;
                default:
                    return _settingsPanelView.CardRect;
            }
        }

        private void OpenPanel(HubTab tab)
        {
            switch (tab)
            {
                case HubTab.PowerUpShop:
                    _powerUpShopView.Open();
                    break;
                case HubTab.Leaderboard:
                    _leaderboardPanelView.Open();
                    break;
                case HubTab.Profile:
                    _profilePanelView.Open();
                    break;
                case HubTab.Badges:
                    _badgesPanelView.Open();
                    break;
                default:
                    _settingsPanelView.Open();
                    break;
            }

            HideActiveCardChrome();

            // HubTab's declaration order is the display order (see its own doc comment), so the enum
            // value is already the array index — no lookup needed.
            RefreshHeaderTitle();
        }

        /// <summary>Shuts a card only when it is actually showing, so the menu pause is never released
        /// by a close that had nothing to close.</summary>
        private void ClosePanel(HubTab tab)
        {
            if (!IsPanelOpen(tab))
            {
                return;
            }

            switch (tab)
            {
                case HubTab.PowerUpShop:
                    _powerUpShopView.Close();
                    break;
                case HubTab.Leaderboard:
                    _leaderboardPanelView.Close();
                    break;
                case HubTab.Profile:
                    _profilePanelView.Close();
                    break;
                case HubTab.Badges:
                    _badgesPanelView.Close();
                    break;
                default:
                    _settingsPanelView.Close();
                    break;
            }
        }

        private void HandlePanelTap(HubTab tab, Vector2 screenPosition)
        {
            switch (tab)
            {
                case HubTab.PowerUpShop:
                    _powerUpShopView.HandleTap(screenPosition);
                    break;
                case HubTab.Leaderboard:
                    _leaderboardPanelView.HandleTap(screenPosition);
                    break;
                case HubTab.Profile:
                    _profilePanelView.HandleTap(screenPosition);
                    break;
                case HubTab.Badges:
                    _badgesPanelView.HandleTap(screenPosition);
                    break;
                default:
                    _settingsPanelView.HandleTap(screenPosition);
                    break;
            }
        }

        private bool IsPanelOpen(HubTab tab)
        {
            switch (tab)
            {
                case HubTab.PowerUpShop:
                    return _powerUpShopView.IsOpen;
                case HubTab.Leaderboard:
                    return _leaderboardPanelView.IsOpen;
                case HubTab.Profile:
                    return _profilePanelView.IsOpen;
                case HubTab.Badges:
                    return _badgesPanelView.IsOpen;
                default:
                    return _settingsPanelView.IsOpen;
            }
        }

        private Camera EventCamera()
        {
            return _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
        }

        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            if (_stripImage == null)
            {
                return;
            }

            _stripImage.color = theme.CardBackground;
            _stripShadowImage.color = theme.CardShadow;

            _headerPlateImage.color = theme.CardBackground;
            _awningImage.color = _awningSprite != null ? Color.white : Color.clear;

            // The title takes the accent's shade: the warm two-tone fill of the mockup's title,
            // reduced to the one flat colour legacy Text can draw.
            _headerTitleText.color = AccentShade(theme);

            // The close disc is the card's own colour so it reads as a button laid over the awning, and
            // its cross takes the first kind's shade — the reference's red cross, in-palette.
            _headerCloseDisc.color = theme.CardBackground;
            _headerCloseDiscShadow.color = theme.CardShadow;
            for (int inkIndex = 0; inkIndex < _headerCloseInk.Length; inkIndex++)
            {
                _headerCloseInk[inkIndex].color = theme.GetShade(1);
            }

            RefreshTabs();
        }

        private void RefreshTabs()
        {
            if (_currentTheme == null)
            {
                return;
            }

            for (int tabIndex = 0; tabIndex < _tabs.Length; tabIndex++)
            {
                PaintTab(_tabs[tabIndex], _tabs[tabIndex].Tab == _activeTab);
            }
        }

        /// <summary>
        /// The selected tab inverts onto the accent plate, the same way the leaderboard marks its own
        /// two tabs and the level path marks the node the player is on, and casts the heavier of the
        /// two shadows. Only colour and shadow change between the states: the plate's shape, radius
        /// and glyph are the same object in both, which is what keeps a tab switch from looking like a
        /// tab reshaping.
        /// </summary>
        private void PaintTab(TabEntry entry, bool isSelected)
        {
            entry.Plate.color = isSelected ? _currentTheme.Accent : _currentTheme.EmptyCellFill;
            entry.Lip.color = isSelected ? AccentShade(_currentTheme) : _currentTheme.EmptyCellOutline;
            entry.Glyph.color = isSelected ? _currentTheme.CardBackground : _currentTheme.SoftInk;

            Color shadow = _currentTheme.CardShadow;
            float strength = isSelected ? ACTIVE_TAB_SHADOW_STRENGTH : INACTIVE_TAB_SHADOW_STRENGTH;
            entry.Shadow.color = new Color(shadow.r, shadow.g, shadow.b, Mathf.Clamp01(shadow.a * strength));

            // The selected plate stands the full tab height; its siblings drop from the top so the
            // active tab is the tallest thing in the row. Every layer is bottom-anchored within the
            // tab's fixed hit rect, so lifting changes only the drawn plate, never the tap target.
            float drop = isSelected ? 0f : TAB_INACTIVE_DROP;
            entry.Shadow.rectTransform.offsetMax = new Vector2(TAB_SHADOW_SPREAD, TAB_SHADOW_SPREAD - TAB_SHADOW_OFFSET - drop);
            entry.Lip.rectTransform.offsetMax = new Vector2(0f, -drop);
            entry.Plate.rectTransform.offsetMax = new Vector2(0f, -drop);
            entry.Glyph.rectTransform.anchoredPosition = new Vector2(0f, (TAB_LIP_HEIGHT * 0.5f) - (drop * 0.5f));
        }

        private void BuildBar()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            float stripWidth = (_tabSize.x * TAB_COUNT) + (_tabGap * (TAB_COUNT - 1)) + (STRIP_PADDING * 2f);
            float stripHeight = _tabSize.y + (STRIP_PADDING * 2f);

            _stripHeight = stripHeight;

            // A fixed offset from the top put the whole hub hard against the safe area, with all the
            // empty space landing below whatever card happened to be open. Centring against every
            // card's own height would pull the header/bar down and up again on every tab switch — the
            // exact jitter AlignCardBelowBar exists to avoid — so this centres once, at build time,
            // against the mean card height instead: close to balanced for a typical tab, and always the
            // same fixed position no matter which tab is open.
            float assemblyHeight = HEADER_HEIGHT + stripHeight + REFERENCE_CARD_HEIGHT;
            float centredInset = (rect.rect.height - assemblyHeight) * 0.5f;
            float topInset = Mathf.Max(_topInset, centredInset);

            var headerObject = new GameObject("HubHeader", typeof(RectTransform));
            _headerRect = (RectTransform)headerObject.transform;
            _headerRect.SetParent(rect, false);
            _headerRect.anchorMin = new Vector2(0.5f, 1f);
            _headerRect.anchorMax = new Vector2(0.5f, 1f);
            _headerRect.pivot = new Vector2(0.5f, 1f);
            _headerRect.sizeDelta = new Vector2(stripWidth, HEADER_HEIGHT);
            _headerRect.anchoredPosition = new Vector2(0f, -topInset);
            _headerRoot = headerObject;

            var headerPlateObject = new GameObject("HubHeaderPlate", typeof(RectTransform), typeof(Image));
            var headerPlateRect = (RectTransform)headerPlateObject.transform;
            headerPlateRect.SetParent(_headerRect, false);
            Centre(headerPlateRect, new Vector2(stripWidth, HEADER_HEIGHT));
            _headerPlateImage = headerPlateObject.GetComponent<Image>();
            ConfigureRounded(_headerPlateImage);

            // The awning hangs across the header's top edge, over the plate's rounded corners.
            var awningObject = new GameObject("HubAwning", typeof(RectTransform), typeof(Image));
            var awningRect = (RectTransform)awningObject.transform;
            awningRect.SetParent(_headerRect, false);
            awningRect.anchorMin = new Vector2(0.5f, 1f);
            awningRect.anchorMax = new Vector2(0.5f, 1f);
            awningRect.pivot = new Vector2(0.5f, 1f);
            awningRect.sizeDelta = new Vector2(stripWidth + (AWNING_OVERHANG * 2f), AWNING_HEIGHT);
            awningRect.anchoredPosition = Vector2.zero;
            _awningImage = awningObject.GetComponent<Image>();
            _awningImage.sprite = _awningSprite;
            _awningImage.type = Image.Type.Simple;
            _awningImage.preserveAspect = false;
            _awningImage.color = Color.clear;
            _awningImage.raycastTarget = false;

            // Centred in the title row under the awning, in the display face — the stall's signboard.
            // The string overflows its rect by design (see UiTextFactory); centred on a 880-wide
            // header even "POWER-UP SHOP" has room on both sides.
            _headerTitleText = UiTextFactory.Create(
                _headerRect, "HeaderTitle", HEADER_FONT_SIZE, FontStyle.Normal, Color.clear, _displayFont);
            _headerTitleText.alignment = TextAnchor.MiddleCenter;

            var headerTitleRect = (RectTransform)_headerTitleText.transform;
            headerTitleRect.anchoredPosition = new Vector2(0f, -(HEADER_HEIGHT * 0.5f) + (TITLE_ROW_HEIGHT * 0.5f));

            BuildHeaderCloseButton();

            var barObject = new GameObject("HubTabBar", typeof(RectTransform));
            var barRect = (RectTransform)barObject.transform;
            barRect.SetParent(rect, false);
            _barRect = barRect;

            // Pinned directly below the header rather than to a fixed offset from the centre: the cards
            // below are centred and the safe area's height is the thing that varies per device, so
            // anchoring to the top (through the header) is what keeps the whole assembly clear of a
            // notch on every one of them.
            barRect.anchorMin = new Vector2(0.5f, 1f);
            barRect.anchorMax = new Vector2(0.5f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(stripWidth, stripHeight);
            barRect.anchoredPosition = new Vector2(0f, -topInset - HEADER_HEIGHT + BAR_CARD_OVERLAP);

            var shadowObject = new GameObject("HubTabBarShadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(barRect, false);
            Centre(shadowRect, new Vector2(stripWidth + 10f, stripHeight + 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            _stripShadowImage = shadowObject.GetComponent<Image>();
            ConfigureRounded(_stripShadowImage);

            var stripObject = new GameObject("HubTabBarPlate", typeof(RectTransform), typeof(Image));
            var stripRect = (RectTransform)stripObject.transform;
            stripRect.SetParent(barRect, false);
            Centre(stripRect, new Vector2(stripWidth, stripHeight));
            _stripImage = stripObject.GetComponent<Image>();
            ConfigureRounded(_stripImage);

            float pitch = _tabSize.x + _tabGap;
            for (int tabIndex = 0; tabIndex < TabOrder.Length; tabIndex++)
            {
                float x = (tabIndex - ((TAB_COUNT - 1) * 0.5f)) * pitch;
                _tabs[tabIndex] = BuildTab(barRect, TabOrder[tabIndex], new Vector2(x, 0f));
            }

            _barRoot = barObject;
        }

        /// <summary>Two crossed rounded bars — the same cross every card's own close button already
        /// draws elsewhere in the HUD, so the hub's unified close reads as the same affordance rather
        /// than a different one.</summary>
        private void BuildHeaderCloseButton()
        {
            const float CROSS_LENGTH = 44f;
            const float CROSS_THICKNESS = 8f;

            var closeObject = new GameObject("HeaderClose", typeof(RectTransform));
            _headerCloseRect = (RectTransform)closeObject.transform;
            _headerCloseRect.SetParent(_headerRect, false);
            Centre(_headerCloseRect, new Vector2(HEADER_CLOSE_SIZE, HEADER_CLOSE_SIZE));
            _headerCloseRect.anchoredPosition = new Vector2(
                (_headerRect.sizeDelta.x * 0.5f) - HEADER_CLOSE_CORNER_INSET,
                (HEADER_HEIGHT * 0.5f) - HEADER_CLOSE_CORNER_INSET);

            // A round plate under the cross, so the button reads as a disc laid over the awning's
            // corner (issue #235) rather than a bare glyph floating on the stripes.
            var discShadowObject = new GameObject("CloseDiscShadow", typeof(RectTransform), typeof(Image));
            var discShadowRect = (RectTransform)discShadowObject.transform;
            discShadowRect.SetParent(_headerCloseRect, false);
            Centre(discShadowRect, new Vector2(HEADER_CLOSE_SIZE, HEADER_CLOSE_SIZE));
            discShadowRect.anchoredPosition = new Vector2(0f, -5f);
            var discShadow = discShadowObject.GetComponent<Image>();
            ConfigureCircle(discShadow);
            _headerCloseDiscShadow = discShadow;

            var discObject = new GameObject("CloseDisc", typeof(RectTransform), typeof(Image));
            var discRect = (RectTransform)discObject.transform;
            discRect.SetParent(_headerCloseRect, false);
            Centre(discRect, new Vector2(HEADER_CLOSE_SIZE, HEADER_CLOSE_SIZE));
            _headerCloseDisc = discObject.GetComponent<Image>();
            ConfigureCircle(_headerCloseDisc);

            _headerCloseInk = new Image[2];
            for (int barIndex = 0; barIndex < 2; barIndex++)
            {
                var barObject = new GameObject($"CloseBar_{barIndex}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform)barObject.transform;
                barRect.SetParent(_headerCloseRect, false);
                Centre(barRect, new Vector2(CROSS_LENGTH, CROSS_THICKNESS));
                barRect.localRotation = Quaternion.Euler(0f, 0f, barIndex == 0 ? 45f : -45f);

                var barImage = barObject.GetComponent<Image>();
                ConfigureRounded(barImage);
                _headerCloseInk[barIndex] = barImage;
            }
        }

        /// <summary>
        /// One tab: its own shadow, then the plate, then a single sprite glyph from
        /// <see cref="_tabIcons"/>, tinted at paint time. The old hand-assembled primitive glyphs (and
        /// their fake cut-outs) are gone with it: a real icon sprite has its own holes, so the glyph is
        /// one Image and one colour, and the tab no longer has to know which shape it is drawing.
        /// </summary>
        private TabEntry BuildTab(RectTransform barRect, HubTab tab, Vector2 anchoredPosition)
        {
            var tabObject = new GameObject($"HubTab_{tab}", typeof(RectTransform));
            var tabRect = (RectTransform)tabObject.transform;
            tabRect.SetParent(barRect, false);
            Centre(tabRect, _tabSize);
            tabRect.anchoredPosition = anchoredPosition;

            // Shadow and plate stretch to the tab's rect rather than being sized once: ResizeBarToWidth
            // resizes the tab to the active card's width, and both have to follow it.
            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(tabRect, false);
            Stretch(shadowRect, TAB_SHADOW_SPREAD);
            shadowRect.offsetMin = new Vector2(-TAB_SHADOW_SPREAD, -TAB_SHADOW_SPREAD - TAB_SHADOW_OFFSET);
            shadowRect.offsetMax = new Vector2(TAB_SHADOW_SPREAD, TAB_SHADOW_SPREAD - TAB_SHADOW_OFFSET);
            var shadow = shadowObject.GetComponent<Image>();
            ConfigureTabPlate(shadow);

            // The lip is the plate's own shape shifted down by its height and painted in the plate's
            // shade; the plate drawn over it leaves just that band showing along the bottom.
            var lipObject = new GameObject("Lip", typeof(RectTransform), typeof(Image));
            var lipRect = (RectTransform)lipObject.transform;
            lipRect.SetParent(tabRect, false);
            Stretch(lipRect, 0f);
            var lip = lipObject.GetComponent<Image>();
            ConfigureTabPlate(lip);

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(tabRect, false);
            Stretch(plateRect, 0f);
            plateRect.offsetMin = new Vector2(0f, TAB_LIP_HEIGHT);
            var plate = plateObject.GetComponent<Image>();
            ConfigureTabPlate(plate);

            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(tabRect, false);
            Centre(glyphRect, new Vector2(_glyphSize, _glyphSize));
            var glyph = glyphObject.GetComponent<Image>();
            glyph.sprite = IconFor(tab);
            glyph.type = Image.Type.Simple;
            glyph.preserveAspect = true;
            glyph.color = Color.clear;
            glyph.raycastTarget = false;

            return new TabEntry(tab, tabRect, shadow, lip, plate, glyph);
        }

        /// <summary>HubTab's declaration order is the display order (see its own doc comment), and the
        /// icon array is authored in that same order, so the enum value indexes it directly.</summary>
        private Sprite IconFor(HubTab tab)
        {
            int iconIndex = (int)tab;
            Sprite icon = _tabIcons != null && iconIndex < _tabIcons.Length ? _tabIcons[iconIndex] : null;
            if (icon == null)
            {
                Debug.LogError($"{nameof(HubPanelView)} has no icon sprite assigned for {tab}.", this);
            }

            return icon;
        }

        /// <summary>The tab plate and its shadow share the HUD's one rounded sprite, sliced to the
        /// design's radius rather than the HUD-wide ~5px, so the whole bar still batches together.</summary>
        private static void ConfigureTabPlate(Image image)
        {
            ConfigureRounded(image);
            image.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / TAB_CORNER_RADIUS;
        }

        private static Color AccentShade(ThemeDefinition theme)
            => new Color(
                theme.Accent.r * (1f - ACCENT_SHADE),
                theme.Accent.g * (1f - ACCENT_SHADE),
                theme.Accent.b * (1f - ACCENT_SHADE),
                theme.Accent.a);

        private static void ConfigureCircle(Image image)
        {
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            image.raycastTarget = false;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Fills the parent, outgrowing it by <paramref name="spread"/> on every side.</summary>
        private static void Stretch(RectTransform rect, float spread)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(spread * 2f, spread * 2f);
            rect.anchoredPosition = Vector2.zero;
        }

        // Every rounded Image here shares the one rounded-square sprite, so the whole bar batches into
        // the surrounding UI instead of adding draw calls of its own.
        private static void ConfigureRounded(Image image)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = Color.clear;
            image.raycastTarget = false;
        }

        /// <summary>One built tab: its hotspot and the four Images a repaint touches — shadow, lip,
        /// plate and the single tinted glyph.</summary>
        private readonly struct TabEntry
        {
            internal TabEntry(HubTab tab, RectTransform rect, Image shadow, Image lip, Image plate, Image glyph)
            {
                Tab = tab;
                Rect = rect;
                Shadow = shadow;
                Lip = lip;
                Plate = plate;
                Glyph = glyph;
            }

            internal HubTab Tab { get; }

            internal RectTransform Rect { get; }

            internal Image Shadow { get; }

            internal Image Lip { get; }

            internal Image Plate { get; }

            internal Image Glyph { get; }
        }
    }
}
