using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
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
    /// This View owns no content of its own beyond a header (the active tab's name and the one close
    /// button that now shuts the hub, whichever tab is open) and the tab bar beneath it. Each tab
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

        private const int COG_TOOTH_COUNT = 8;
        private const int PODIUM_BAR_COUNT = 3;

        /// <summary>Overlap between the bar's bottom edge and the active card's top edge, and likewise
        /// between the header's bottom edge and the bar's top edge. Small and negative rather than
        /// zero, so a seam never opens a hairline gap under canvas-scaler rounding — each pair is meant
        /// to read as one surface, not as two plates touching at a knife edge.</summary>
        private const float BAR_CARD_OVERLAP = 2f;

        private const float HEADER_HEIGHT = 120f;
        private const float HEADER_SIDE_INSET = 44f;
        private const float HEADER_CLOSE_SIZE = 88f;
        private const int HEADER_FONT_SIZE = 56;

        /// <summary>Mean of the five cards' own configured heights. Used only to pick a resting
        /// position for the header+bar that reads as vertically centred for a typical tab — actual
        /// per-tab (and, within Settings, per-sub-screen) card alignment beneath the bar is always
        /// exact and dynamic (see <see cref="AlignCardBelowBar"/>), never derived from this.</summary>
        private const float REFERENCE_CARD_HEIGHT = 1140f;

        private static readonly string[] TabTitles =
        {
            "SETTINGS",
            "POWER-UP SHOP",
            "LEADERBOARD",
            "PROFILE",
            "BADGES",
        };

        [Header("Layout")]
        [Tooltip("Minimum gap between the top of the safe area and the top of the header, in reference "
            + "pixels — a floor under the computed centred position, not the position itself.")]
        [SerializeField] private float _topInset = 28f;
        [SerializeField] private Vector2 _tabSize = new Vector2(176f, 118f);
        [SerializeField] private float _tabGap = 10f;

        [Tooltip("Size of the glyph box inside a tab plate, in reference pixels.")]
        [SerializeField] private float _glyphSize = 68f;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly TabEntry[] _tabs = new TabEntry[TAB_COUNT];

        private SettingsModel _settingsModel;
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
        private Image[] _headerCloseInk;

        private ThemeDefinition _currentTheme;

        /// <summary>Reused every reposition so tracking the active card's edge allocates nothing.</summary>
        private readonly Vector3[] _cardCornerBuffer = new Vector3[4];

        private bool _isOpen;
        private HubTab _activeTab = HubTab.Settings;

        [Inject]
        public void Construct(
            SettingsModel settingsModel,
            SettingsPanelView settingsPanelView,
            PowerUpShopView powerUpShopView,
            LeaderboardPanelView leaderboardPanelView,
            ProfilePanelView profilePanelView,
            BadgesPanelView badgesPanelView)
        {
            _settingsModel = settingsModel;
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
            if (_settingsModel == null || _settingsPanelView == null || _powerUpShopView == null
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

            float headerHalfWidth = cardWidth * 0.5f;
            ((RectTransform)_headerTitleText.transform).anchoredPosition =
                new Vector2(-headerHalfWidth + HEADER_SIDE_INSET, 0f);
            _headerCloseRect.anchoredPosition = new Vector2(headerHalfWidth - HEADER_SIDE_INSET, 0f);
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
            _headerTitleText.text = TabTitles[(int)tab];
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
            _headerTitleText.color = theme.Ink;
            for (int inkIndex = 0; inkIndex < _headerCloseInk.Length; inkIndex++)
            {
                _headerCloseInk[inkIndex].color = theme.SoftInk;
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
        /// two tabs and the level path marks the node the player is on. Both plate colours are fully
        /// opaque: the glyphs' holes are fake cut-outs painted in the plate's own colour, which only
        /// reads as a hole over something solid.
        /// </summary>
        private void PaintTab(TabEntry entry, bool isSelected)
        {
            Color plateColour = isSelected
                ? _currentTheme.Accent
                : Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.12f);

            entry.Plate.color = plateColour;

            Color inkColour = isSelected ? _currentTheme.CardBackground : _currentTheme.SoftInk;
            for (int inkIndex = 0; inkIndex < entry.Ink.Length; inkIndex++)
            {
                entry.Ink[inkIndex].color = inkColour;
            }

            for (int holeIndex = 0; holeIndex < entry.Holes.Length; holeIndex++)
            {
                entry.Holes[holeIndex].color = plateColour;
            }
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

            _headerTitleText = UiTextFactory.Create(
                _headerRect, "HeaderTitle", HEADER_FONT_SIZE, FontStyle.Bold, Color.clear);
            _headerTitleText.alignment = TextAnchor.MiddleLeft;

            var headerTitleRect = (RectTransform)_headerTitleText.transform;

            // Pivot on the left edge, matching every other header label in the HUD: the string
            // overflows its rect by design (see UiTextFactory), so a centred pivot would grow the text
            // off both sides and, for a title this long ("POWER-UP SHOP"), clip past the header itself.
            headerTitleRect.pivot = new Vector2(0f, 0.5f);
            headerTitleRect.anchoredPosition = new Vector2(-(stripWidth * 0.5f) + HEADER_SIDE_INSET, 0f);

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
            _headerCloseRect.anchoredPosition = new Vector2((_headerRect.sizeDelta.x * 0.5f) - HEADER_SIDE_INSET, 0f);

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

        private TabEntry BuildTab(RectTransform barRect, HubTab tab, Vector2 anchoredPosition)
        {
            var tabObject = new GameObject($"HubTab_{tab}", typeof(RectTransform), typeof(Image));
            var tabRect = (RectTransform)tabObject.transform;
            tabRect.SetParent(barRect, false);
            Centre(tabRect, _tabSize);
            tabRect.anchoredPosition = anchoredPosition;

            var plate = tabObject.GetComponent<Image>();
            ConfigureRounded(plate);

            var glyphObject = new GameObject("Glyph", typeof(RectTransform));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(tabRect, false);
            Centre(glyphRect, new Vector2(_glyphSize, _glyphSize));

            Image[] ink;
            Image[] holes;
            switch (tab)
            {
                case HubTab.PowerUpShop:
                    BuildCoinGlyph(glyphRect, out ink, out holes);
                    break;
                case HubTab.Leaderboard:
                    BuildPodiumGlyph(glyphRect, out ink, out holes);
                    break;
                case HubTab.Profile:
                    BuildAvatarGlyph(glyphRect, out ink, out holes);
                    break;
                case HubTab.Badges:
                    BuildMedalGlyph(glyphRect, out ink, out holes);
                    break;
                default:
                    BuildCogGlyph(glyphRect, out ink, out holes);
                    break;
            }

            return new TabEntry(tab, tabRect, plate, ink, holes);
        }

        /// <summary>Teeth first, then the hub disc over their inner ends, then the bore punched through
        /// both. The same cog the settings icon draws, at tab scale.</summary>
        private void BuildCogGlyph(RectTransform root, out Image[] ink, out Image[] holes)
        {
            ink = new Image[COG_TOOTH_COUNT + 1];

            float toothWidth = _glyphSize * 0.17f;
            float toothLength = _glyphSize * 0.26f;
            float toothDistance = _glyphSize * 0.32f;

            for (int toothIndex = 0; toothIndex < COG_TOOTH_COUNT; toothIndex++)
            {
                float angle = (360f / COG_TOOTH_COUNT) * toothIndex;
                var toothRect = CreateRect(root, $"Tooth_{toothIndex}", new Vector2(toothWidth, toothLength));

                // The tooth points away from the centre, so it is both rotated by the angle and pushed
                // out along that same rotated axis.
                toothRect.localRotation = Quaternion.Euler(0f, 0f, angle);
                toothRect.anchoredPosition =
                    (Vector2)(Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, toothDistance, 0f));

                var toothImage = toothRect.GetComponent<Image>();
                ConfigureRounded(toothImage);
                ink[toothIndex] = toothImage;
            }

            var ringRect = CreateRect(root, "CogRing", new Vector2(_glyphSize * 0.60f, _glyphSize * 0.60f));
            var ringImage = ringRect.GetComponent<Image>();
            ConfigureCircle(ringImage);
            ink[COG_TOOTH_COUNT] = ringImage;

            var boreRect = CreateRect(root, "CogBore", new Vector2(_glyphSize * 0.26f, _glyphSize * 0.26f));
            var boreImage = boreRect.GetComponent<Image>();
            ConfigureCircle(boreImage);
            holes = new[] { boreImage };
        }

        private void BuildCoinGlyph(RectTransform root, out Image[] ink, out Image[] holes)
        {
            float diameter = _glyphSize * 0.82f;

            var discRect = CreateRect(root, "CoinDisc", new Vector2(diameter, diameter));
            var discImage = discRect.GetComponent<Image>();
            ConfigureCircle(discImage);
            ink = new[] { discImage };

            var coreRect = CreateRect(root, "CoinCore", new Vector2(diameter * 0.42f, diameter * 0.42f));
            var coreImage = coreRect.GetComponent<Image>();
            ConfigureCircle(coreImage);
            holes = new[] { coreImage };
        }

        /// <summary>Second place to the left of the winner, third to the right — which is how a podium is
        /// ordered, and what stops three bars reading as a bar chart.</summary>
        private void BuildPodiumGlyph(RectTransform root, out Image[] ink, out Image[] holes)
        {
            ink = new Image[PODIUM_BAR_COUNT];
            holes = System.Array.Empty<Image>();

            float barWidth = _glyphSize * 0.22f;
            float barPitch = _glyphSize * 0.28f;
            float baselineY = -_glyphSize * 0.36f;

            for (int barIndex = 0; barIndex < PODIUM_BAR_COUNT; barIndex++)
            {
                float heightScale = barIndex == 1 ? 0.72f : (barIndex == 0 ? 0.46f : 0.58f);
                float barHeight = _glyphSize * heightScale;

                var barRect = CreateRect(root, $"PodiumBar_{barIndex}", new Vector2(barWidth, barHeight));
                barRect.anchoredPosition = new Vector2(
                    (barIndex - ((PODIUM_BAR_COUNT - 1) * 0.5f)) * barPitch,
                    baselineY + (barHeight * 0.5f));

                var barImage = barRect.GetComponent<Image>();
                ConfigureRounded(barImage);
                ink[barIndex] = barImage;
            }
        }

        private void BuildAvatarGlyph(RectTransform root, out Image[] ink, out Image[] holes)
        {
            holes = System.Array.Empty<Image>();

            float headDiameter = _glyphSize * 0.40f;

            var headRect = CreateRect(root, "AvatarHead", new Vector2(headDiameter, headDiameter));
            headRect.anchoredPosition = new Vector2(0f, _glyphSize * 0.22f);
            var headImage = headRect.GetComponent<Image>();
            ConfigureCircle(headImage);

            var shouldersRect = CreateRect(
                root, "AvatarShoulders", new Vector2(_glyphSize * 0.70f, _glyphSize * 0.46f));
            shouldersRect.anchoredPosition = new Vector2(0f, -_glyphSize * 0.24f);
            var shouldersImage = shouldersRect.GetComponent<Image>();
            ConfigureRounded(shouldersImage);

            ink = new[] { headImage, shouldersImage };
        }

        /// <summary>Ribbons first, then the disc over their ends, then the disc's core: the same medal the
        /// badges icon drew, at tab scale.</summary>
        private void BuildMedalGlyph(RectTransform root, out Image[] ink, out Image[] holes)
        {
            ink = new Image[3];

            float ribbonLength = _glyphSize * 0.34f;
            float ribbonThickness = _glyphSize * 0.13f;
            float discDiameter = _glyphSize * 0.62f;
            float discCentreY = -_glyphSize * 0.15f;

            for (int ribbonIndex = 0; ribbonIndex < 2; ribbonIndex++)
            {
                float sign = ribbonIndex == 0 ? 1f : -1f;

                var ribbonRect = CreateRect(
                    root, $"MedalRibbon_{ribbonIndex}", new Vector2(ribbonThickness, ribbonLength));
                ribbonRect.anchoredPosition = new Vector2(sign * _glyphSize * 0.12f, _glyphSize * 0.23f);
                ribbonRect.localRotation = Quaternion.Euler(0f, 0f, sign * 16f);

                var ribbonImage = ribbonRect.GetComponent<Image>();
                ConfigureRounded(ribbonImage);
                ink[ribbonIndex] = ribbonImage;
            }

            var discRect = CreateRect(root, "MedalDisc", new Vector2(discDiameter, discDiameter));
            discRect.anchoredPosition = new Vector2(0f, discCentreY);
            var discImage = discRect.GetComponent<Image>();
            ConfigureCircle(discImage);
            ink[2] = discImage;

            var coreRect = CreateRect(
                root, "MedalCore", new Vector2(discDiameter * 0.46f, discDiameter * 0.46f));
            coreRect.anchoredPosition = new Vector2(0f, discCentreY);
            var coreImage = coreRect.GetComponent<Image>();
            ConfigureCircle(coreImage);
            holes = new[] { coreImage };
        }

        private static RectTransform CreateRect(RectTransform parent, string objectName, Vector2 size)
        {
            var partObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var partRect = (RectTransform)partObject.transform;
            partRect.SetParent(parent, false);
            Centre(partRect, size);
            return partRect;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
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

        // The circle sprite has no border, so it must never be sliced.
        private static void ConfigureCircle(Image image)
        {
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            image.raycastTarget = false;
        }

        /// <summary>One built tab: its hotspot, its plate, and the two repaint buckets its glyph splits
        /// into — inked parts, and fake cut-outs that have to track the plate colour exactly.</summary>
        private readonly struct TabEntry
        {
            internal TabEntry(HubTab tab, RectTransform rect, Image plate, Image[] ink, Image[] holes)
            {
                Tab = tab;
                Rect = rect;
                Plate = plate;
                Ink = ink;
                Holes = holes;
            }

            internal HubTab Tab { get; }

            internal RectTransform Rect { get; }

            internal Image Plate { get; }

            internal Image[] Ink { get; }

            internal Image[] Holes { get; }
        }
    }
}
