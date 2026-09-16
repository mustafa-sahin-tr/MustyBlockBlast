using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Services;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The profile overlay: who the player is. Display name, avatar, how durable the account is, and the
    /// lifetime totals behind every badge.
    /// <para>
    /// Editable, unlike <see cref="BadgesPanelView"/> — but it still holds no logic. Every change is
    /// handed to <see cref="ProfileSystem"/>, which is what validates, persists and publishes it; this
    /// card only draws what came back.
    /// </para>
    /// <para>
    /// Anonymous is a fully working state here, not a locked one: the name and avatar rows behave
    /// identically whether or not an account is linked, and linking is offered as a way to keep progress
    /// rather than demanded as a way to start.
    /// </para>
    /// <para>
    /// Built once in <see cref="Start"/> and toggled with SetActive like the other three cards, modal
    /// like them (it holds the timed countdown through <see cref="TimerRunSystem.SetMenuPaused"/>), and
    /// kept mutually exclusive with them by the gate chain in <see cref="BoardInputView"/>.
    /// </para>
    /// <para>
    /// Text entry goes through <see cref="TouchScreenKeyboard"/> rather than a UGUI input field: this
    /// scene has no EventSystem by design — every tap is hit-tested here — and an input field without
    /// one would accept nothing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProfilePanelView : MonoBehaviour
    {
        private const int AVATAR_COLUMN_COUNT = 4;

        // Layout, in canvas reference pixels, matching the other three cards so all four read as one
        // family.
        private const float HEADER_INSET = 84f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;

        private const float NAME_ROW_Y = -178f;
        private const float NAME_ROW_HEIGHT = 110f;
        private const float MESSAGE_ROW_Y = -252f;

        private const float AVATAR_GRID_TOP_Y = -310f;
        private const float AVATAR_CELL_SIZE = 132f;
        private const float AVATAR_SPACING_X = 176f;
        private const float AVATAR_SPACING_Y = 156f;
        private const float AVATAR_SELECTION_PADDING = 14f;

        private const float ACCOUNT_LABEL_Y = -852f;
        private const float ACCOUNT_BUTTON_Y = -938f;
        private const float ACCOUNT_BUTTON_HEIGHT = 96f;

        private const float STATS_TOP_Y = -1048f;
        private const float STATS_ROW_SPACING = 54f;

        private const int STAT_ROW_COUNT = 6;

        /// <summary>Dim applied to an unpicked avatar swatch — the same "not yours yet" dim the badge
        /// wall and the level path use, so the idea looks identical wherever it appears.</summary>
        private const float UNPICKED_AVATAR_ALPHA = 0.42f;

        // Plain strings, not String Table keys: LocalizationKeys has no profile section yet, and adding
        // keys with no translations behind them would render the keys themselves. Tracked for a
        // follow-up — see the class remarks in LocalizationKeys.
        private const string HEADER_TEXT = "PROFILE";
        private const string NAME_LABEL_TEXT = "NAME";
        private const string NAME_PLACEHOLDER_TEXT = "Tap to set a name";
        private const string NAME_REJECTED_TEXT = "That name can't be used. Try another.";
        private const string NAME_FAILED_TEXT = "Couldn't save that name. Try again.";
        private const string NAME_KEYBOARD_UNAVAILABLE_TEXT = "No keyboard available on this device.";
        private const string AVATAR_LABEL_TEXT = "AVATAR";
        private const string ACCOUNT_ANONYMOUS_TEXT = "Account: Anonymous (this device only)";
        private const string ACCOUNT_APPLE_TEXT = "Account: Linked via Apple";
        private const string ACCOUNT_GOOGLE_TEXT = "Account: Linked via Google Play Games";
        private const string SAVE_ACCOUNT_APPLE_TEXT = "SAVE ACCOUNT WITH APPLE";
        private const string SAVE_ACCOUNT_GOOGLE_TEXT = "SAVE ACCOUNT WITH GOOGLE";
        private const string SAVE_ACCOUNT_BUSY_TEXT = "SIGNING IN...";
        private const string LINK_ALREADY_USED_TEXT = "That account already belongs to another player.";
        private const string LINK_FAILED_TEXT = "Couldn't save your account. Try again.";
        private const string STATS_LABEL_TEXT = "LIFETIME";

        /// <summary>Keyboard prompt. Shown by the OS above the text field.</summary>
        private const string NAME_KEYBOARD_PROMPT = "Your display name";

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Repaint bucket: every Image that follows the theme's ink (the close cross).</summary>
        private readonly List<Image> _inkImages = new List<Image>(4);

        private readonly AvatarSwatch[] _avatarSwatches = new AvatarSwatch[ProfileModel.AVATAR_COUNT];
        private readonly Text[] _statValueTexts = new Text[STAT_ROW_COUNT];
        private readonly Text[] _statLabelTexts = new Text[STAT_ROW_COUNT];

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1420f);
        [SerializeField] private int _headerFontSize = 64;
        [SerializeField] private int _sectionFontSize = 30;
        [SerializeField] private int _valueFontSize = 40;
        [SerializeField] private int _bodyFontSize = 28;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private ProfileModel _profileModel;
        private ProfileSystem _profileSystem;
        private BadgeStatsModel _statsModel;
        private SettingsModel _settingsModel;
        private TimerRunSystem _timerRunSystem;
        private AppleSignInProvider _appleSignInProvider;
        private GooglePlayGamesSignInProvider _googlePlayGamesSignInProvider;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private RectTransform _closeButtonRect;

        private Text _headerText;
        private Text _nameLabelText;
        private Text _nameValueText;
        private Text _messageText;
        private Text _avatarLabelText;
        private Text _accountStatusText;
        private Text _statsLabelText;
        private Text _saveAccountText;

        private RectTransform _nameRowRect;
        private Image _nameRowPlate;
        private RectTransform _saveAccountRect;
        private Image _saveAccountPlate;

        private ThemeDefinition _currentTheme;

        /// <summary>
        /// Live native keyboard, or null when none is open. Non-null is also the flag that makes
        /// <see cref="Update"/> do any work at all, so a closed card costs nothing per frame.
        /// Deliberately not a Unity object, so plain null checks are correct here.
        /// </summary>
        private TouchScreenKeyboard _keyboard;

        /// <summary>Guards the link flow against a second tap while a native prompt is already up.</summary>
        private bool _isLinking;

        /// <summary>One built avatar swatch. Rebuilt never, repainted on every change.</summary>
        private sealed class AvatarSwatch
        {
            internal AvatarSwatch(RectTransform root, Image selectionImage, Image swatchImage, Text numberText)
            {
                Root = root;
                SelectionImage = selectionImage;
                SwatchImage = swatchImage;
                NumberText = numberText;
            }

            internal RectTransform Root { get; }

            internal Image SelectionImage { get; }

            internal Image SwatchImage { get; }

            internal Text NumberText { get; }
        }

        [Inject]
        public void Construct(
            ProfileModel profileModel,
            ProfileSystem profileSystem,
            BadgeStatsModel statsModel,
            SettingsModel settingsModel,
            TimerRunSystem timerRunSystem,
            AppleSignInProvider appleSignInProvider,
            GooglePlayGamesSignInProvider googlePlayGamesSignInProvider)
        {
            _profileModel = profileModel;
            _profileSystem = profileSystem;
            _statsModel = statsModel;
            _settingsModel = settingsModel;
            _timerRunSystem = timerRunSystem;
            _appleSignInProvider = appleSignInProvider;
            _googlePlayGamesSignInProvider = googlePlayGamesSignInProvider;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_profileModel == null || _profileSystem == null || _statsModel == null
                || _settingsModel == null || _timerRunSystem == null)
            {
                Debug.LogError(
                    $"{nameof(ProfilePanelView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            BuildPanel();
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            // The name and avatar can change from here, but the card must also survive them changing
            // while it is open — which a rejected-then-accepted name does. Subscribing is what keeps the
            // drawn value and the model from ever disagreeing.
            _profileModel.DisplayName.Subscribe(OnDisplayNameChanged).AddTo(_disposables);
            _profileModel.AvatarId.Subscribe(OnAvatarIdChanged).AddTo(_disposables);
            _profileModel.LinkStatus.Subscribe(OnLinkStatusChanged).AddTo(_disposables);
        }

        /// <summary>
        /// Pumps the native keyboard. Returns on the first line whenever no keyboard is open, which is
        /// every frame the card is closed — and allocates nothing on the frames it does run.
        /// </summary>
        private void Update()
        {
            if (_keyboard == null)
            {
                return;
            }

            TouchScreenKeyboard.Status status = _keyboard.status;
            if (status == TouchScreenKeyboard.Status.Visible)
            {
                return;
            }

            string entered = _keyboard.text;
            _keyboard = null;

            if (status != TouchScreenKeyboard.Status.Done)
            {
                // Cancelled or lost: the player backed out, which is not an error and gets no message.
                return;
            }

            SubmitDisplayName(entered).Forget();
        }

        private void OnDestroy() => _disposables.Dispose();

        /// <summary>True while the panel is showing. Read by <see cref="BoardInputView"/>.</summary>
        internal bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>Shows the panel, repainted from the models. Re-opening never double-pauses the
        /// clock: an already-open panel returns immediately.</summary>
        internal void Open()
        {
            if (_panel == null || IsOpen)
            {
                return;
            }

            SetMessage(string.Empty);
            Refresh();
            _panel.SetActive(true);
            transform.SetAsLastSibling();
            _timerRunSystem.SetMenuPaused(true);
        }

        /// <summary>
        /// Routes a tap while the panel is open. The close cross wins, then the editable rows in the
        /// order they are drawn; the card then swallows anything else, so a tap on a stat row is a
        /// deliberate no-op rather than a dismissal. Only the scrim outside the card closes.
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

            if (RectTransformUtility.RectangleContainsScreenPoint(_nameRowRect, screenPosition, eventCamera))
            {
                OpenNameKeyboard();
                return;
            }

            for (int avatarIndex = 0; avatarIndex < _avatarSwatches.Length; avatarIndex++)
            {
                RectTransform swatchRect = _avatarSwatches[avatarIndex].Root;
                if (RectTransformUtility.RectangleContainsScreenPoint(swatchRect, screenPosition, eventCamera))
                {
                    _profileSystem.SetAvatarId(avatarIndex);
                    return;
                }
            }

            if (_saveAccountRect.gameObject.activeSelf
                && RectTransformUtility.RectangleContainsScreenPoint(_saveAccountRect, screenPosition, eventCamera))
            {
                BeginLink().Forget();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_cardRect, screenPosition, eventCamera))
            {
                return;
            }

            Close();
        }

        private void Close()
        {
            // A keyboard left up over a closed card would keep typing into nothing, and its result would
            // arrive with no card to report it on.
            if (_keyboard != null)
            {
                _keyboard.active = false;
                _keyboard = null;
            }

            _panel.SetActive(false);
            _timerRunSystem.SetMenuPaused(false);
        }

        private void OpenNameKeyboard()
        {
            if (_keyboard != null)
            {
                return;
            }

            SetMessage(string.Empty);

            _keyboard = TouchScreenKeyboard.Open(
                _profileModel.DisplayName.Value,
                TouchScreenKeyboardType.Default,
                false,
                false,
                false,
                false,
                NAME_KEYBOARD_PROMPT);

            // Desktop and the Editor have no native keyboard, so Open returns null there. Said out loud
            // rather than ignored: a row that silently does nothing reads as a broken row.
            if (_keyboard == null)
            {
                SetMessage(NAME_KEYBOARD_UNAVAILABLE_TEXT);
            }
        }

        private async UniTaskVoid SubmitDisplayName(string enteredName)
        {
            try
            {
                bool accepted = await _profileSystem.UpdateDisplayNameAsync(
                    enteredName, this.GetCancellationTokenOnDestroy());

                SetMessage(accepted ? string.Empty : NAME_REJECTED_TEXT);
            }
            catch (OperationCanceledException)
            {
                // The card went away mid-request. Nothing left to tell.
            }
            catch (Exception)
            {
                // Everything the backend can fail with means the same thing to the player here: the name
                // they chose is not saved and trying again is the only move.
                SetMessage(NAME_FAILED_TEXT);
            }
        }

        private async UniTaskVoid BeginLink()
        {
            if (_isLinking)
            {
                return;
            }

            _isLinking = true;
            SetMessage(string.Empty);
            RefreshAccountRow();

            try
            {
                CancellationToken cancellationToken = this.GetCancellationTokenOnDestroy();

                // Branching on the live platform rather than on a compile-time define: one build of this
                // card serves both stores, and the provider behind each branch is the piece that is
                // platform-compiled.
                if (Application.platform == RuntimePlatform.IPhonePlayer)
                {
                    string identityToken = await _appleSignInProvider.GetIdentityTokenAsync(cancellationToken);
                    await _profileSystem.LinkAppleAsync(identityToken, cancellationToken);
                }
                else
                {
                    string authCode = await _googlePlayGamesSignInProvider.GetAuthCodeAsync(cancellationToken);
                    await _profileSystem.LinkGooglePlayGamesAsync(authCode, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // The card went away mid-flow.
            }
            catch (AccountAlreadyLinkedException)
            {
                // The one link failure with a real answer for the player, which is why IAuthService
                // gives it its own type.
                SetMessage(LINK_ALREADY_USED_TEXT);
            }
            catch (Exception)
            {
                SetMessage(LINK_FAILED_TEXT);
            }
            finally
            {
                _isLinking = false;
                RefreshAccountRow();
            }
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

        private void OnDisplayNameChanged(string displayName) => RefreshNameRow();

        private void OnAvatarIdChanged(int avatarId) => RefreshAvatarGrid();

        private void OnLinkStatusChanged(AccountLinkStatus linkStatus) => RefreshAccountRow();

        /// <summary>Repaints the whole card from the models.</summary>
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

            _nameLabelText.color = _currentTheme.SoftInk;
            _nameLabelText.text = NAME_LABEL_TEXT;

            _avatarLabelText.color = _currentTheme.SoftInk;
            _avatarLabelText.text = AVATAR_LABEL_TEXT;

            _statsLabelText.color = _currentTheme.SoftInk;
            _statsLabelText.text = STATS_LABEL_TEXT;

            RefreshNameRow();
            RefreshAvatarGrid();
            RefreshAccountRow();
            RefreshStats();
        }

        private void RefreshNameRow()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            _nameRowPlate.color = Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.12f);

            string displayName = _profileModel.DisplayName.Value;
            bool hasName = !string.IsNullOrEmpty(displayName);

            _nameValueText.text = hasName ? displayName : NAME_PLACEHOLDER_TEXT;
            _nameValueText.color = hasName ? _currentTheme.Ink : _currentTheme.SoftInk;
        }

        private void RefreshAvatarGrid()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            int selectedId = _profileModel.AvatarId.Value;

            for (int avatarIndex = 0; avatarIndex < _avatarSwatches.Length; avatarIndex++)
            {
                AvatarSwatch swatch = _avatarSwatches[avatarIndex];
                bool isSelected = avatarIndex == selectedId;

                Color swatchColour = PlaceholderAvatarColour(avatarIndex);
                swatch.SwatchImage.color = isSelected
                    ? swatchColour
                    : new Color(swatchColour.r, swatchColour.g, swatchColour.b, UNPICKED_AVATAR_ALPHA);

                // The pick is marked by the accent plate behind the swatch, the same way the level path
                // marks the node the player is on.
                swatch.SelectionImage.color = isSelected ? _currentTheme.Accent : Color.clear;

                swatch.NumberText.color = isSelected ? _currentTheme.CardBackground : _currentTheme.SoftInk;
            }
        }

        private void RefreshAccountRow()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            AccountLinkStatus linkStatus = _profileModel.LinkStatus.Value;

            _accountStatusText.color = _currentTheme.Ink;
            _accountStatusText.text = AccountStatusTextFor(linkStatus);

            // The offer only exists while there is something to save. A linked account has nowhere left
            // to go from here, so the button is gone rather than disabled.
            bool showSaveButton = linkStatus == AccountLinkStatus.Anonymous;
            if (_saveAccountRect.gameObject.activeSelf != showSaveButton)
            {
                _saveAccountRect.gameObject.SetActive(showSaveButton);
            }

            if (!showSaveButton)
            {
                return;
            }

            _saveAccountPlate.color = _currentTheme.Accent;
            _saveAccountText.color = _currentTheme.CardBackground;
            _saveAccountText.text = SaveAccountLabel();
        }

        private string SaveAccountLabel()
        {
            if (_isLinking)
            {
                return SAVE_ACCOUNT_BUSY_TEXT;
            }

            return Application.platform == RuntimePlatform.IPhonePlayer
                ? SAVE_ACCOUNT_APPLE_TEXT
                : SAVE_ACCOUNT_GOOGLE_TEXT;
        }

        private static string AccountStatusTextFor(AccountLinkStatus linkStatus)
        {
            switch (linkStatus)
            {
                case AccountLinkStatus.LinkedApple:
                    return ACCOUNT_APPLE_TEXT;
                case AccountLinkStatus.LinkedGoogle:
                    return ACCOUNT_GOOGLE_TEXT;
                default:
                    return ACCOUNT_ANONYMOUS_TEXT;
            }
        }

        /// <summary>
        /// Reads the six lifetime counters one-shot, exactly as <see cref="BadgesPanelView"/> reads its
        /// badges: a total that moves while the card is open cannot happen, because the card is modal and
        /// the board is paused behind it.
        /// </summary>
        private void RefreshStats()
        {
            WriteStatRow(0, "Pieces placed", _statsModel.TotalPiecesPlaced.Value);
            WriteStatRow(1, "Lines cleared", _statsModel.TotalLinesCleared.Value);
            WriteStatRow(2, "Board wipes", _statsModel.TotalBoardWipes.Value);
            WriteStatRow(3, "Best score", _statsModel.HighestScoreEver.Value);
            WriteStatRow(4, "Runs played", _statsModel.TotalRunsPlayed.Value);
            WriteStatRow(5, "Power-ups used", _statsModel.TotalPowerUpsApplied.Value);
        }

        private void WriteStatRow(int rowIndex, string label, long value)
        {
            _statLabelTexts[rowIndex].color = _currentTheme.SoftInk;
            _statLabelTexts[rowIndex].text = label;

            _statValueTexts[rowIndex].color = _currentTheme.Ink;

            // Invariant culture so a device locale cannot change how a saved total reads back, matching
            // how BadgeStatsSystem persists them.
            _statValueTexts[rowIndex].text = value.ToString(CultureInfo.InvariantCulture);
        }

        private void SetMessage(string message)
        {
            if (_messageText == null)
            {
                return;
            }

            _messageText.text = message;
            _messageText.color = _currentTheme != null ? _currentTheme.InvalidPreview : Color.clear;
        }

        /// <summary>
        /// PLACEHOLDER ART. Each preset avatar is a flat swatch generated from its own id rather than a
        /// drawn character, because the project has no avatar sprites yet. Spread around the hue wheel so
        /// the twelve are told apart at a glance, and numbered so a player can name the one they picked.
        /// Replace with a sprite lookup once the atlas exists — see the class docs on the art hand-off.
        /// </summary>
        private static Color PlaceholderAvatarColour(int avatarId)
        {
            float hue = (float)avatarId / ProfileModel.AVATAR_COUNT;
            return Color.HSVToRGB(hue, 0.55f, 0.92f);
        }

        private void BuildPanel()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var panelObject = new GameObject("ProfilePanel", typeof(RectTransform), typeof(Image));
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
                panelRect, "ProfileCard", _cardSize, out _cardImage, out _cardShadowImage);

            float cardHalfHeight = _cardSize.y * 0.5f;
            float cardHalfWidth = _cardSize.x * 0.5f;
            float leftX = -cardHalfWidth + SIDE_INSET;
            float rightX = cardHalfWidth - SIDE_INSET;
            float headerY = cardHalfHeight - HEADER_INSET;

            _headerText = CreateLabel(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX, headerY));

            BuildCloseButton(_cardRect, new Vector2(rightX - (ICON_BUTTON_SIZE * 0.5f), headerY));

            BuildNameRow(cardHalfHeight, leftX, rightX);
            BuildAvatarSection(cardHalfHeight, leftX);
            BuildAccountSection(cardHalfHeight, leftX);
            BuildStatsSection(cardHalfHeight, leftX, rightX);

            _panel = panelObject;
        }

        private void BuildNameRow(float cardHalfHeight, float leftX, float rightX)
        {
            _nameLabelText = CreateLabel(
                _cardRect, "NameLabel", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX, cardHalfHeight + NAME_ROW_Y + (NAME_ROW_HEIGHT * 0.5f) + 26f));

            float rowWidth = rightX - leftX;

            var rowObject = new GameObject("NameRow", typeof(RectTransform), typeof(Image));
            _nameRowRect = (RectTransform)rowObject.transform;
            _nameRowRect.SetParent(_cardRect, false);
            Centre(_nameRowRect, new Vector2(rowWidth, NAME_ROW_HEIGHT));
            _nameRowRect.anchoredPosition = new Vector2(0f, cardHalfHeight + NAME_ROW_Y);
            _nameRowPlate = rowObject.GetComponent<Image>();
            ConfigureRounded(_nameRowPlate);

            _nameValueText = CreateLabel(
                _nameRowRect, "NameValue", _valueFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2((-rowWidth * 0.5f) + 28f, 0f));

            _messageText = CreateLabel(
                _cardRect, "Message", _bodyFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(leftX, cardHalfHeight + MESSAGE_ROW_Y));
        }

        private void BuildAvatarSection(float cardHalfHeight, float leftX)
        {
            _avatarLabelText = CreateLabel(
                _cardRect, "AvatarLabel", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX, cardHalfHeight + AVATAR_GRID_TOP_Y));

            float gridTopY = cardHalfHeight + AVATAR_GRID_TOP_Y - 70f;

            for (int avatarIndex = 0; avatarIndex < ProfileModel.AVATAR_COUNT; avatarIndex++)
            {
                int column = avatarIndex % AVATAR_COLUMN_COUNT;
                int row = avatarIndex / AVATAR_COLUMN_COUNT;

                float x = (column - ((AVATAR_COLUMN_COUNT - 1) * 0.5f)) * AVATAR_SPACING_X;
                float y = gridTopY - (row * AVATAR_SPACING_Y) - (AVATAR_CELL_SIZE * 0.5f);

                _avatarSwatches[avatarIndex] = BuildAvatarSwatch(avatarIndex, new Vector2(x, y));
            }
        }

        private AvatarSwatch BuildAvatarSwatch(int avatarIndex, Vector2 anchoredPosition)
        {
            var cellSize = new Vector2(AVATAR_CELL_SIZE, AVATAR_CELL_SIZE);

            var swatchObject = new GameObject($"AvatarSwatch_{avatarIndex}", typeof(RectTransform));
            var swatchRoot = (RectTransform)swatchObject.transform;
            swatchRoot.SetParent(_cardRect, false);
            Centre(swatchRoot, cellSize);
            swatchRoot.anchoredPosition = anchoredPosition;

            var selectionObject = new GameObject("Selection", typeof(RectTransform), typeof(Image));
            var selectionRect = (RectTransform)selectionObject.transform;
            selectionRect.SetParent(swatchRoot, false);
            Centre(selectionRect, cellSize + new Vector2(AVATAR_SELECTION_PADDING, AVATAR_SELECTION_PADDING));
            var selectionImage = selectionObject.GetComponent<Image>();
            ConfigureRounded(selectionImage);

            var fillObject = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
            var fillRect = (RectTransform)fillObject.transform;
            fillRect.SetParent(swatchRoot, false);
            Centre(fillRect, cellSize);
            var fillImage = fillObject.GetComponent<Image>();
            ConfigureRounded(fillImage);

            Text numberText = CreateLabel(
                swatchRoot, "Number", _bodyFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero);

            // The number is the whole of the identity right now — see PlaceholderAvatarColour.
            numberText.text = (avatarIndex + 1).ToString(CultureInfo.InvariantCulture);

            return new AvatarSwatch(swatchRoot, selectionImage, fillImage, numberText);
        }

        private void BuildAccountSection(float cardHalfHeight, float leftX)
        {
            _accountStatusText = CreateLabel(
                _cardRect, "AccountStatus", _bodyFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(leftX, cardHalfHeight + ACCOUNT_LABEL_Y));

            var buttonObject = new GameObject("SaveAccountButton", typeof(RectTransform), typeof(Image));
            _saveAccountRect = (RectTransform)buttonObject.transform;
            _saveAccountRect.SetParent(_cardRect, false);
            Centre(_saveAccountRect, new Vector2(_cardSize.x - (SIDE_INSET * 2f), ACCOUNT_BUTTON_HEIGHT));
            _saveAccountRect.anchoredPosition = new Vector2(0f, cardHalfHeight + ACCOUNT_BUTTON_Y);
            _saveAccountPlate = buttonObject.GetComponent<Image>();
            ConfigureRounded(_saveAccountPlate);

            _saveAccountText = CreateLabel(
                _saveAccountRect, "SaveAccountLabel", _sectionFontSize, FontStyle.Bold,
                TextAnchor.MiddleCenter, Vector2.zero);
        }

        private void BuildStatsSection(float cardHalfHeight, float leftX, float rightX)
        {
            _statsLabelText = CreateLabel(
                _cardRect, "StatsLabel", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX, cardHalfHeight + STATS_TOP_Y));

            for (int rowIndex = 0; rowIndex < STAT_ROW_COUNT; rowIndex++)
            {
                float y = cardHalfHeight + STATS_TOP_Y - 56f - (rowIndex * STATS_ROW_SPACING);

                _statLabelTexts[rowIndex] = CreateLabel(
                    _cardRect, $"StatLabel_{rowIndex}", _bodyFontSize, FontStyle.Normal,
                    TextAnchor.MiddleLeft, new Vector2(leftX, y));

                _statValueTexts[rowIndex] = CreateLabel(
                    _cardRect, $"StatValue_{rowIndex}", _bodyFontSize, FontStyle.Bold,
                    TextAnchor.MiddleRight, new Vector2(rightX, y));
            }
        }

        /// <summary>Two bars crossed at right angles — the close glyph, as on the other three cards.</summary>
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
    }
}
