using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MustyBlockBlast.Gameplay;
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
    /// The leaderboard overlay: where the player stands against everyone else, on the mode they are
    /// playing. Two tabs — all-time and weekly — over that mode's own two boards.
    /// <para>
    /// Read-only, like <see cref="BadgesPanelView"/> and unlike <see cref="ProfilePanelView"/>. A name
    /// and an avatar are set on the profile card and nowhere else; this one only renders the name the
    /// backend returns with an entry and the avatar that entry's metadata carries. A row is a standing,
    /// not a button, so tapping one is deliberately a no-op.
    /// </para>
    /// <para>
    /// The mode is never chosen here either. The boards shown are always the current
    /// <see cref="GameModeSystem.CurrentMode"/>'s, resolved by
    /// <see cref="LeaderboardQuerySystem"/> — so switching mode (from the settings card, which cannot be
    /// open at the same time as this one) shows that mode's own boards on the next open, rather than a
    /// shared board that would rank a timed run against an endless one.
    /// </para>
    /// <para>
    /// Unpaged and unscrollable, exactly as the badge wall is: the card draws a fixed number of rows and
    /// any surplus the backend returned is simply not drawn. This scene has no EventSystem — every tap
    /// is hit-tested in <see cref="HandleTap"/> — so there is no scroll infrastructure to lean on, and
    /// the top of a board is what a leaderboard is for.
    /// </para>
    /// <para>
    /// Anonymous players are first-class here: nothing gates on a linked account, and an entry whose
    /// player never set a name is drawn with a placeholder rather than skipped.
    /// </para>
    /// <para>
    /// Built once in <see cref="Start"/> and toggled with SetActive like the other four cards, modal
    /// like them (it holds the timed countdown through <see cref="TimerRunSystem.SetMenuPaused"/>), and
    /// kept mutually exclusive with them by the gate chain in <see cref="BoardInputView"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeaderboardPanelView : MonoBehaviour
    {
        /// <summary>
        /// Rows the card can draw. The service is asked for a few more than this so the card is always
        /// full where the board allows it; the surplus is not drawn — see the class remarks.
        /// </summary>
        private const int ROW_COUNT = 8;

        // Layout, in canvas reference pixels, matching the other four cards so all five read as one
        // family.
        private const float HEADER_INSET = 92f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;

        private const float MODE_LABEL_Y = -164f;

        private const float TAB_ROW_Y = -252f;
        private const float TAB_HEIGHT = 92f;
        private const float TAB_GAP = 20f;

        private const float ROW_TOP_Y = -348f;
        private const float ROW_HEIGHT = 84f;
        private const float ROW_SPACING = 94f;
        private const float ROW_TEXT_INSET = 26f;
        private const float AVATAR_DIAMETER = 56f;

        /// <summary>Where the "nothing to show, and here is why" line sits — the top of the row block,
        /// so a message replaces the standings rather than floating under them.</summary>
        private const float MESSAGE_Y = -400f;

        /// <summary>Dim applied to the tab that is not selected. The same "not this one" dim the avatar
        /// picker, the badge wall and the level path use, so the idea looks identical everywhere.</summary>
        private const float UNSELECTED_TAB_ALPHA = 0.42f;

        // Plain strings, not String Table keys: LocalizationKeys has no leaderboard section yet, and
        // adding keys with no translations behind them would render the keys themselves. Tracked for a
        // follow-up — see the class remarks in LocalizationKeys.
        private const string HEADER_TEXT = "LEADERBOARD";
        private const string ALL_TIME_TAB_TEXT = "ALL-TIME";
        private const string WEEKLY_TAB_TEXT = "WEEKLY";
        // Renamed for display only (issue #355): the GameMode enum members and their leaderboard board
        // ids (see LeaderboardBoardIds) are unchanged, so a Classic-mode (GameMode.Timed) run still
        // ranks on the same board a Timed run always has.
        private const string ENDLESS_MODE_TEXT = "Şölen Modu";
        private const string TIMED_MODE_TEXT = "Klasik Mod";
        private const string PATH_MODE_TEXT = "Macera Modu";
        private const string LOADING_TEXT = "Loading standings...";
        private const string EMPTY_TEXT = "No scores on this board yet.";
        private const string NOT_RANKED_TEXT = "Level path runs aren't ranked.";
        private const string UNAVAILABLE_TEXT = "Standings need a connection.";
        private const string FAILED_TEXT = "Couldn't load the board. Try again.";

        /// <summary>Stand-in for a player who never set a name. Anonymous is an ordinary state — see the
        /// class remarks — so the row still draws, just without a name on it.</summary>
        private const string UNNAMED_PLAYER_TEXT = "Player";

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Reused for the rank prefix so repainting eight rows allocates one string per row
        /// instead of several.</summary>
        private readonly StringBuilder _stringBuilder = new StringBuilder(16);

        /// <summary>Repaint bucket: every Image that follows the theme's ink (the close cross).</summary>
        private readonly List<Image> _inkImages = new List<Image>(4);

        private readonly EntryRow[] _rows = new EntryRow[ROW_COUNT];

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1130f);
        [SerializeField] private int _headerFontSize = 64;
        [SerializeField] private int _sectionFontSize = 30;
        [SerializeField] private int _rowFontSize = 32;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private LeaderboardModel _leaderboardModel;
        private LeaderboardQuerySystem _leaderboardQuerySystem;
        private GameModeSystem _gameModeSystem;
        private SettingsModel _settingsModel;
        private TimerRunSystem _timerRunSystem;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private RectTransform _closeButtonRect;

        private Text _headerText;
        private Text _modeLabelText;
        private Text _messageText;

        private RectTransform _allTimeTabRect;
        private Image _allTimeTabPlate;
        private Text _allTimeTabText;

        private RectTransform _weeklyTabRect;
        private Image _weeklyTabPlate;
        private Text _weeklyTabText;

        private ThemeDefinition _currentTheme;

        /// <summary>One built row widget. Rebuilt never, repainted on every change.</summary>
        private sealed class EntryRow
        {
            internal EntryRow(
                RectTransform root, Image plateImage, Image avatarImage, Text rankText, Text nameText, Text scoreText)
            {
                Root = root;
                PlateImage = plateImage;
                AvatarImage = avatarImage;
                RankText = rankText;
                NameText = nameText;
                ScoreText = scoreText;
            }

            internal RectTransform Root { get; }

            internal Image PlateImage { get; }

            internal Image AvatarImage { get; }

            internal Text RankText { get; }

            internal Text NameText { get; }

            internal Text ScoreText { get; }
        }

        [Inject]
        public void Construct(
            LeaderboardModel leaderboardModel,
            LeaderboardQuerySystem leaderboardQuerySystem,
            GameModeSystem gameModeSystem,
            SettingsModel settingsModel,
            TimerRunSystem timerRunSystem)
        {
            _leaderboardModel = leaderboardModel;
            _leaderboardQuerySystem = leaderboardQuerySystem;
            _gameModeSystem = gameModeSystem;
            _settingsModel = settingsModel;
            _timerRunSystem = timerRunSystem;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_leaderboardModel == null || _leaderboardQuerySystem == null || _gameModeSystem == null
                || _settingsModel == null || _timerRunSystem == null)
            {
                Debug.LogError(
                    $"{nameof(LeaderboardPanelView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            BuildPanel();
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // A fetch lands after the card is already up, so the card cannot repaint on open alone the
            // way the badge wall does: these three are what turn an answer into rows.
            _leaderboardModel.State.Subscribe(OnStateChanged).AddTo(_disposables);
            _leaderboardModel.Entries.Subscribe(OnEntriesChanged).AddTo(_disposables);
            _leaderboardModel.ActiveTab.Subscribe(OnActiveTabChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>The card's own rect, current size included. Read by <see cref="HubPanelView"/> to
        /// sit its tab bar flush against whichever card is open, rather than at a fixed offset that
        /// would gap open against a shorter card.</summary>
        internal RectTransform CardRect => _cardRect;

        /// <summary>This card's own close cross. Hidden by <see cref="HubPanelView"/> once opened
        /// there, since the hub's own header now carries the one close button for whichever tab is
        /// open.</summary>
        internal RectTransform CloseButtonRect => _closeButtonRect;

        /// <summary>This card's own title, which just repeats the tab it belongs to. Hidden by
        /// <see cref="HubPanelView"/> once opened there, since the hub's own header now says the same
        /// thing.</summary>
        internal Text HeaderTitleText => _headerText;

        /// <summary>
        /// Shows the panel and asks for fresh standings. Re-opening never double-pauses the clock: an
        /// already-open panel returns immediately.
        /// <para>
        /// The fetch is started here rather than kept warm in the background, so a closed card costs no
        /// network at all — and so a card opened right after a game over shows the run that was just
        /// filed rather than a cached board from before it.
        /// </para>
        /// </summary>
        internal void Open()
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            Refresh();
            _panel.SetActive(true);
            transform.SetAsLastSibling();
            _timerRunSystem.SetMenuPaused(true);

            _leaderboardQuerySystem.Refresh();
        }

        /// <summary>
        /// Routes a tap while the panel is open. The close cross wins, then the two tabs; the card then
        /// swallows anything else, so a tap on a standing is a deliberate no-op rather than a dismissal.
        /// Only the scrim outside the card closes.
        /// </summary>
        internal void HandleTap(Vector2 screenPosition)
        {
            if (!IsOpen)
            {
                return;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            if (RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, screenPosition, eventCamera))
            {
                Close();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_allTimeTabRect, screenPosition, eventCamera))
            {
                _leaderboardQuerySystem.SelectTab(LeaderboardTab.AllTime);
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_weeklyTabRect, screenPosition, eventCamera))
            {
                _leaderboardQuerySystem.SelectTab(LeaderboardTab.Weekly);
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        /// <summary>
        /// Shuts the card and releases the menu pause. Reachable by <c>HubPanelView</c>, which shuts the
        /// outgoing card when the player switches tabs; every other caller is this class's own dismiss
        /// paths.
        /// </summary>
        internal void Close()
        {
            _panel.SetActive(false);
            _timerRunSystem.SetMenuPaused(false);
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

        private void OnStateChanged(LeaderboardLoadState state) => Refresh();

        private void OnEntriesChanged(IReadOnlyList<LeaderboardEntryData> entries) => Refresh();

        private void OnActiveTabChanged(LeaderboardTab tab) => Refresh();

        /// <summary>Repaints the whole card from the model: header, mode, tabs, rows and message.</summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            _cardImage.color = _currentTheme.CardBackground;
            _cardShadowImage.color = _currentTheme.CardShadow;

            for (int inkIndex = 0; inkIndex < _inkImages.Count; inkIndex++)
            {
                _inkImages[inkIndex].color = _currentTheme.Ink;
            }

            _headerText.color = _currentTheme.Ink;
            _headerText.text = HEADER_TEXT;

            _modeLabelText.color = _currentTheme.SoftInk;
            _modeLabelText.text = ModeLabelFor(_gameModeSystem.CurrentMode.Value);

            RefreshTabs();
            RefreshRows();
        }

        private void RefreshTabs()
        {
            LeaderboardTab activeTab = _leaderboardModel.ActiveTab.Value;

            PaintTab(_allTimeTabPlate, _allTimeTabText, ALL_TIME_TAB_TEXT, activeTab == LeaderboardTab.AllTime);
            PaintTab(_weeklyTabPlate, _weeklyTabText, WEEKLY_TAB_TEXT, activeTab == LeaderboardTab.Weekly);
        }

        /// <summary>The selected tab inverts onto the accent plate, the same way the level path marks the
        /// node the player is on and the badge wall marks an unlocked tile.</summary>
        private void PaintTab(Image plate, Text label, string text, bool isSelected)
        {
            Color plateColour = isSelected
                ? _currentTheme.Accent
                : Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.14f);

            plate.color = isSelected ? plateColour : WithAlpha(plateColour, UNSELECTED_TAB_ALPHA);

            label.color = isSelected ? _currentTheme.CardBackground : _currentTheme.SoftInk;
            label.text = text;
        }

        private void RefreshRows()
        {
            LeaderboardLoadState state = _leaderboardModel.State.Value;
            IReadOnlyList<LeaderboardEntryData> entries = _leaderboardModel.Entries.Value;

            // Rows only mean anything once a page has landed. Every other state has no standings to
            // show and says why instead — an empty card with no explanation reads as a broken card.
            bool hasRows = state == LeaderboardLoadState.Ready && entries != null && entries.Count > 0;

            for (int rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                // A board shorter than the card leaves surplus widgets hidden rather than drawn empty —
                // and a board longer than it has its tail silently dropped, which is the trade an
                // unscrollable card makes.
                bool exists = hasRows && rowIndex < entries.Count;
                EntryRow row = _rows[rowIndex];

                if (row.Root.gameObject.activeSelf != exists)
                {
                    row.Root.gameObject.SetActive(exists);
                }

                if (exists)
                {
                    PaintRow(row, entries[rowIndex]);
                }
            }

            _messageText.text = hasRows ? string.Empty : MessageFor(state);
            _messageText.color = state == LeaderboardLoadState.Failed
                ? _currentTheme.InvalidPreview
                : _currentTheme.SoftInk;
        }

        private void PaintRow(EntryRow row, LeaderboardEntryData entry)
        {
            row.PlateImage.color = Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.10f);
            row.AvatarImage.color = PlaceholderAvatarColour(entry.AvatarId);

            row.RankText.color = _currentTheme.SoftInk;

            // The service ranks from zero; a standing reads from one.
            row.RankText.text = FormatRank(entry.Rank + 1);

            row.NameText.color = _currentTheme.Ink;
            row.NameText.text = string.IsNullOrEmpty(entry.PlayerName) ? UNNAMED_PLAYER_TEXT : entry.PlayerName;

            row.ScoreText.color = _currentTheme.Ink;

            // Invariant culture so a device locale cannot change how another player's score reads,
            // matching how every other total in the game is drawn.
            row.ScoreText.text = entry.Score.ToString(CultureInfo.InvariantCulture);
        }

        private string FormatRank(int displayRank)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(displayRank);
            _stringBuilder.Append('.');
            return _stringBuilder.ToString();
        }

        private static string MessageFor(LeaderboardLoadState state)
        {
            switch (state)
            {
                case LeaderboardLoadState.Loading:
                    return LOADING_TEXT;
                case LeaderboardLoadState.NotRanked:
                    return NOT_RANKED_TEXT;
                case LeaderboardLoadState.Unavailable:
                    return UNAVAILABLE_TEXT;
                case LeaderboardLoadState.Failed:
                    return FAILED_TEXT;
                default:
                    // Ready with nothing in it is a board nobody has played, not a failure — and Idle
                    // only lasts until the first fetch this card itself starts on open.
                    return EMPTY_TEXT;
            }
        }

        private static string ModeLabelFor(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Timed:
                    return TIMED_MODE_TEXT;
                case GameMode.Path:
                    return PATH_MODE_TEXT;
                default:
                    return ENDLESS_MODE_TEXT;
            }
        }

        /// <summary>
        /// PLACEHOLDER ART, mirroring the scheme <see cref="ProfilePanelView"/> picks avatars with: a
        /// flat swatch generated from the id, because the project has no avatar sprites yet. Duplicated
        /// rather than shared, since it is one line and sharing it would mean a type that exists only to
        /// hold it. Both go away together once the atlas exists.
        /// </summary>
        private static Color PlaceholderAvatarColour(int avatarId)
        {
            float hue = (float)avatarId / ProfileModel.AVATAR_COUNT;
            return Color.HSVToRGB(hue, 0.55f, 0.92f);
        }

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("LeaderboardPanel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var scrim = panelObject.GetComponent<Image>();
            scrim.color = _scrimColour;
            scrim.raycastTarget = false;

            _cardRect = CellFactory.CreateCard(
                panelRect, "LeaderboardCard", _cardSize, out _cardImage, out _cardShadowImage);

            float cardHalfHeight = _cardSize.y * 0.5f;
            float cardHalfWidth = _cardSize.x * 0.5f;
            float leftX = -cardHalfWidth + SIDE_INSET;
            float rightX = cardHalfWidth - SIDE_INSET;
            float headerY = cardHalfHeight - HEADER_INSET;

            _headerText = CreateLabel(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX, headerY));

            BuildCloseButton(_cardRect, new Vector2(rightX - (ICON_BUTTON_SIZE * 0.5f), headerY));

            _modeLabelText = CreateLabel(
                _cardRect, "ModeLabel", _sectionFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(leftX, cardHalfHeight + MODE_LABEL_Y));

            BuildTabs(cardHalfHeight);
            BuildRows(cardHalfHeight);

            _messageText = CreateLabel(
                _cardRect, "Message", _sectionFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, cardHalfHeight + MESSAGE_Y));

            _panel = panelObject;
        }

        private void BuildTabs(float cardHalfHeight)
        {
            float rowWidth = _cardSize.x - (SIDE_INSET * 2f);
            float tabWidth = (rowWidth - TAB_GAP) * 0.5f;
            float tabOffsetX = (tabWidth + TAB_GAP) * 0.5f;
            float tabY = cardHalfHeight + TAB_ROW_Y;

            _allTimeTabRect = BuildTab(
                "AllTimeTab", new Vector2(-tabOffsetX, tabY), tabWidth,
                out _allTimeTabPlate, out _allTimeTabText);

            _weeklyTabRect = BuildTab(
                "WeeklyTab", new Vector2(tabOffsetX, tabY), tabWidth,
                out _weeklyTabPlate, out _weeklyTabText);
        }

        private RectTransform BuildTab(
            string objectName, Vector2 anchoredPosition, float tabWidth, out Image plate, out Text label)
        {
            var tabObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var tabRect = (RectTransform)tabObject.transform;
            tabRect.SetParent(_cardRect, false);
            Centre(tabRect, new Vector2(tabWidth, TAB_HEIGHT));
            tabRect.anchoredPosition = anchoredPosition;

            plate = tabObject.GetComponent<Image>();
            ConfigureRounded(plate);

            label = CreateLabel(
                tabRect, "Label", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero);

            return tabRect;
        }

        private void BuildRows(float cardHalfHeight)
        {
            float rowWidth = _cardSize.x - (SIDE_INSET * 2f);

            for (int rowIndex = 0; rowIndex < ROW_COUNT; rowIndex++)
            {
                float y = cardHalfHeight + ROW_TOP_Y - (rowIndex * ROW_SPACING) - (ROW_HEIGHT * 0.5f);
                _rows[rowIndex] = BuildRow(rowIndex, rowWidth, new Vector2(0f, y));
            }
        }

        private EntryRow BuildRow(int rowIndex, float rowWidth, Vector2 anchoredPosition)
        {
            var rowObject = new GameObject($"EntryRow_{rowIndex}", typeof(RectTransform));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(_cardRect, false);
            Centre(rowRect, new Vector2(rowWidth, ROW_HEIGHT));
            rowRect.anchoredPosition = anchoredPosition;

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(rowRect, false);
            Centre(plateRect, new Vector2(rowWidth, ROW_HEIGHT));
            var plateImage = plateObject.GetComponent<Image>();
            ConfigureRounded(plateImage);

            float rowLeftX = -rowWidth * 0.5f;

            Text rankText = CreateLabel(
                rowRect, "Rank", _rowFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(rowLeftX + ROW_TEXT_INSET, 0f));

            var avatarObject = new GameObject("Avatar", typeof(RectTransform), typeof(Image));
            var avatarRect = (RectTransform)avatarObject.transform;
            avatarRect.SetParent(rowRect, false);
            Centre(avatarRect, new Vector2(AVATAR_DIAMETER, AVATAR_DIAMETER));
            avatarRect.anchoredPosition = new Vector2(rowLeftX + 118f, 0f);
            var avatarImage = avatarObject.GetComponent<Image>();
            ConfigureCircle(avatarImage);

            Text nameText = CreateLabel(
                rowRect, "Name", _rowFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(rowLeftX + 168f, 0f));

            Text scoreText = CreateLabel(
                rowRect, "Score", _rowFontSize, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2((rowWidth * 0.5f) - ROW_TEXT_INSET, 0f));

            return new EntryRow(rowRect, plateImage, avatarImage, rankText, nameText, scoreText);
        }

        /// <summary>Two bars crossed at right angles — the close glyph, as on the other four cards.</summary>
        private void BuildCloseButton(RectTransform root, Vector2 anchoredPosition)
        {
            const float CROSS_LENGTH = 46f;
            const float CROSS_THICKNESS = 8f;

            var closeObject = new GameObject("CloseButton", typeof(RectTransform));
            _closeButtonRect = (RectTransform)closeObject.transform;
            _closeButtonRect.SetParent(root, false);
            Centre(_closeButtonRect, new Vector2(ICON_BUTTON_SIZE, ICON_BUTTON_SIZE));
            _closeButtonRect.anchoredPosition = anchoredPosition;

            for (int barIndex = 0; barIndex < 2; barIndex++)
            {
                var barObject = new GameObject($"CloseBar_{barIndex}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform)barObject.transform;
                barRect.SetParent(_closeButtonRect, false);
                Centre(barRect, new Vector2(CROSS_LENGTH, CROSS_THICKNESS));
                barRect.localRotation = Quaternion.Euler(0f, 0f, barIndex == 0 ? 45f : -45f);

                var barImage = barObject.GetComponent<Image>();
                ConfigureRounded(barImage);
                _inkImages.Add(barImage);
            }
        }

        /// <summary>Builds a wordless label. Callers fill it in from <see cref="Refresh"/>.</summary>
        private static Text CreateLabel(
            RectTransform parent,
            string objectName,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 anchoredPosition)
        {
            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, Color.clear);
            text.alignment = alignment;

            var rect = (RectTransform)text.transform;

            // Pivot on the aligned edge so the anchored position is that edge, whatever the string ends
            // up measuring — labels overflow their rect by design (see UiTextFactory).
            float pivotX = alignment == TextAnchor.MiddleRight ? 1f : (alignment == TextAnchor.MiddleLeft ? 0f : 0.5f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.sizeDelta = new Vector2(0f, fontSize * 1.6f);
            rect.anchoredPosition = anchoredPosition;
            return text;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not through
        // an EventSystem, and this scene has none.
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
    }
}
