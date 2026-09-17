using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Core;
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
    /// The profile card: who the player is. Drawn after the storefront reference (issue #235): a
    /// sunken content well under the hub's tab bar, and inside it an identity plate (ringed avatar,
    /// name, coin balance, one glossy EDIT button), the avatar swatches, a LEVEL row with a path bar,
    /// a three-up lifetime strip, a 3×2 preview of the badge wall and, at the bottom, the one call to
    /// action — save the account — or, once saved, the green strip that says so.
    /// <para>
    /// Editable, unlike <see cref="BadgesPanelView"/> — but it still holds no logic. Every change is
    /// handed to <see cref="ProfileSystem"/>, which is what validates, persists and publishes it; this
    /// card only draws what came back. Every colour on it is a <see cref="ThemeDefinition"/> field, so
    /// the card repaints itself with the season.
    /// </para>
    /// <para>
    /// Anonymous is a fully working state here, not a locked one: the name and avatar rows behave
    /// identically whether or not an account is linked, and linking is offered as a way to keep progress
    /// rather than demanded as a way to start.
    /// </para>
    /// <para>
    /// The badge preview is a shortcut, not a second badge wall: its tiles show nothing the Badges tab
    /// does not, and a tap on any of them is read by <see cref="HubPanelView"/> (through
    /// <see cref="BadgeWallRect"/>) as "open the Badges tab". Switching tabs is the hub's move, which is
    /// why this card exposes the rect rather than reaching up into the hub that opened it.
    /// </para>
    /// <para>
    /// Built once in <see cref="Start"/> and toggled with SetActive like the other cards, modal like
    /// them (it holds the timed countdown through <see cref="TimerRunSystem.SetMenuPaused"/>), and kept
    /// mutually exclusive with them by the gate chain in <see cref="BoardInputView"/>. Text entry goes
    /// through <see cref="TouchScreenKeyboard"/> rather than a UGUI input field: this scene has no
    /// EventSystem by design — every tap is hit-tested here — and an input field without one would
    /// accept nothing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProfilePanelView : MonoBehaviour
    {
        private const int AVATAR_COLUMN_COUNT = 6;
        private const int BADGE_PREVIEW_COLUMNS = 3;
        private const int BADGE_PREVIEW_ROWS = 2;
        private const int BADGE_PREVIEW_COUNT = BADGE_PREVIEW_COLUMNS * BADGE_PREVIEW_ROWS;
        private const int STAT_COLUMN_COUNT = 3;

        // Layout, in canvas reference pixels, on the 880 × 1140 card the hub's other cards share.
        // Vertical offsets are measured down from the card's top edge; TopY turns them into anchored
        // positions. The whole card is one column: nothing here is measured from the bottom, so a
        // taller card only ever adds empty space under the call to action.
        private const float HEADER_INSET = 84f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;

        /// <summary>The well: the slightly sunken plate the whole content sits in, inset from the card
        /// edge on every side, with its own padding inside that.</summary>
        private const float WELL_INSET = 16f;
        private const float WELL_PADDING = 16f;
        private const float WELL_CORNER_RADIUS = 22f;

        /// <summary>Rendered corner radius of the identity plate and the buttons, in reference pixels.
        /// The shared rounded sprite bakes its radius at <see cref="UiSpriteFactory.ROUNDED_RADIUS"/>,
        /// so the slice multiplier is derived from the two, as the hub's tabs do.</summary>
        private const float PLATE_CORNER_RADIUS = 18f;

        private const float IDENTITY_TOP = 32f;
        private const float IDENTITY_HEIGHT = 140f;
        private const float IDENTITY_PADDING = 16f;
        private const float AVATAR_DISC_SIZE = 108f;
        private const float AVATAR_RING_GAP = 4f;
        private const float AVATAR_RING_THICKNESS = 4f;
        private const float NAME_COLUMN_X = 166f;
        private const float NAME_LABEL_RISE = 42f;
        private const float NAME_VALUE_RISE = 2f;
        private const float COIN_ROW_DROP = 40f;
        private const float COIN_GLYPH_SIZE = 32f;
        private const float COIN_TEXT_GAP = 8f;
        private const float EDIT_BUTTON_WIDTH = 150f;
        private const float EDIT_BUTTON_HEIGHT = 64f;

        private const float MESSAGE_Y = 190f;

        private const float AVATAR_LABEL_Y = 222f;
        private const float AVATAR_ROWS_TOP = 244f;
        private const float AVATAR_CELL_SIZE = 80f;
        private const float AVATAR_PITCH_X = 124f;
        private const float AVATAR_PITCH_Y = 92f;
        private const float AVATAR_SELECTION_PADDING = 12f;

        private const float LEVEL_LABEL_Y = 446f;
        private const float LEVEL_NUMBER_X = -300f;
        private const float LEVEL_BAR_TOP = 468f;
        private const float LEVEL_BAR_HEIGHT = 16f;
        private const float LEVEL_BAR_LIP = 3f;
        private const float LEVEL_ENDS_Y = 500f;

        private const float STRIP_TOP = 522f;
        private const float STRIP_BOTTOM = 622f;
        private const float STRIP_VALUE_Y = 560f;
        private const float STRIP_LABEL_Y = 598f;
        private const float HAIRLINE_THICKNESS = 2f;
        private const float DIVIDER_INSET = 10f;

        private const float BADGES_LABEL_Y = 646f;
        private const float TILES_TOP = 668f;
        private const float TILE_BOX_HEIGHT = 104f;
        private const float TILE_BOX_CORNER_RADIUS = 16f;
        private const float TILE_GAP = 12f;
        private const float TILE_NAME_DROP = 122f;
        private const float TILE_ROW_PITCH = 148f;
        private const float TILE_WALL_HEIGHT = TILE_ROW_PITCH + TILE_BOX_HEIGHT + 36f;
        private const float MEDAL_SIZE = 78f;
        private const float MEDAL_LIP = 4f;
        private const float MEDAL_GLYPH_SIZE = 42f;

        private const float ACCOUNT_LABEL_Y = 984f;
        private const float ACTION_TOP = 1006f;
        private const float ACTION_HEIGHT = 72f;

        /// <summary>The glossy button sprite's face sits above a baked darker lip, so a label is lifted
        /// off the button's geometric centre to sit on the face — the same rise the shop uses.</summary>
        private const float BUTTON_LABEL_RISE = 4f;

        /// <summary>Slice scale for the glossy button sprite, matching <see cref="PowerUpShopView"/> so
        /// the two screens' buttons have the same lip and corner.</summary>
        private const float BUTTON_SLICE_SCALE = 2.5f;

        // Which theme kind's bevel triplet each coloured element takes. Chosen by role, so a season
        // swap recolours them together: the primary call to action, the secondary EDIT button, the
        // level bar, and the linked-account strip. Gold — medallions, the best score, the coin figure —
        // is the theme's Accent rather than a kind: the kinds' hues are ordered differently in every
        // season's palette, but Accent is the warm gold in all of them.
        private const int PRIMARY_KIND = 1;
        private const int SECONDARY_KIND = 2;
        private const int LEVEL_KIND = 5;
        private const int LINKED_KIND = 2;

        /// <summary>How far the accent is pulled toward black for the gold's shade — a medallion's lip,
        /// and the gold figures, which need more contrast on the card than the raw accent has.</summary>
        private const float GOLD_SHADE = 0.35f;

        /// <summary>Dim applied to an unpicked avatar swatch and a locked badge — the same "not yours
        /// yet" dim the badge wall and the level path use, so the idea looks identical wherever it
        /// appears.</summary>
        private const float UNPICKED_ALPHA = 0.42f;

        /// <summary>How far the well sinks below the card: Ink over CardBackground.</summary>
        private const float WELL_TINT = 0.06f;

        /// <summary>The level bar's empty track: Ink over CardBackground.</summary>
        private const float TRACK_TINT = 0.12f;

        /// <summary>A badge tile's ground: its kind's fill over CardBackground.</summary>
        private const float TILE_TINT = 0.25f;

        /// <summary>The linked strip's ground: the linked kind's highlight over CardBackground.</summary>
        private const float LINKED_TINT = 0.55f;

        /// <summary>How far a busy SAVE button fades toward SoftInk while the native prompt is up.</summary>
        private const float BUSY_FADE = 0.5f;

        // Plain strings, not String Table keys: LocalizationKeys has no profile section yet, and adding
        // keys with no translations behind them would render the keys themselves. Tracked for a
        // follow-up — see the class remarks in LocalizationKeys.
        private const string HEADER_TEXT = "PROFILE";
        private const string NAME_LABEL_TEXT = "NAME";
        private const string NAME_PLACEHOLDER_TEXT = "Tap to set a name";
        private const string NAME_REJECTED_TEXT = "That name can't be used. Try another.";
        private const string NAME_FAILED_TEXT = "Couldn't save that name. Try again.";
        private const string NAME_KEYBOARD_UNAVAILABLE_TEXT = "No keyboard available on this device.";
        private const string EDIT_TEXT = "EDIT";
        private const string AVATAR_LABEL_TEXT = "AVATAR";
        private const string LEVEL_LABEL_TEXT = "LEVEL";
        private const string LEVEL_END_PREFIX = "Level ";
        private const string PATH_PREFIX = "PATH ";
        private const string BEST_SCORE_LABEL_TEXT = "BEST SCORE";
        private const string RUNS_LABEL_TEXT = "RUNS";
        private const string LINES_LABEL_TEXT = "LINES";
        private const string BADGES_LABEL_TEXT = "BADGES";
        private const string ACCOUNT_ANONYMOUS_TEXT = "ACCOUNT · THIS DEVICE ONLY";
        private const string ACCOUNT_APPLE_TEXT = "ACCOUNT · SAVED WITH APPLE";
        private const string ACCOUNT_GOOGLE_TEXT = "ACCOUNT · SAVED WITH GOOGLE";
        private const string LINKED_APPLE_TEXT = "Saved with Apple · progress follows you";
        private const string LINKED_GOOGLE_TEXT = "Saved with Google · progress follows you";
        private const string SAVE_ACCOUNT_APPLE_TEXT = "SAVE WITH APPLE";
        private const string SAVE_ACCOUNT_GOOGLE_TEXT = "SAVE WITH GOOGLE";
        private const string SAVE_ACCOUNT_BUSY_TEXT = "SIGNING IN...";
        private const string LINK_ALREADY_USED_TEXT = "That account already belongs to another player.";
        private const string LINK_FAILED_TEXT = "Couldn't save your account. Try again.";
        private const string COUNTER_SEPARATOR = " / ";

        /// <summary>Keyboard prompt. Shown by the OS above the text field.</summary>
        private const string NAME_KEYBOARD_PROMPT = "Your display name";

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Repaint bucket: every Image that follows the theme's ink (the close cross).</summary>
        private readonly List<Image> _inkImages = new List<Image>(4);

        /// <summary>Repaint bucket: the strip's hairlines and dividers, Ink at low alpha.</summary>
        private readonly List<Image> _hairlineImages = new List<Image>(4);

        private readonly AvatarSwatch[] _avatarSwatches = new AvatarSwatch[ProfileModel.AVATAR_COUNT];
        private readonly Text[] _statValueTexts = new Text[STAT_COLUMN_COUNT];
        private readonly Text[] _statLabelTexts = new Text[STAT_COLUMN_COUNT];
        private readonly BadgeTile[] _badgeTiles = new BadgeTile[BADGE_PREVIEW_COUNT];

        [Header("Layout")]
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1140f);
        [SerializeField] private int _headerFontSize = 64;

        [Tooltip("Section labels (NAME, AVATAR, LEVEL, BADGES) and the strip's captions.")]
        [SerializeField] private int _sectionFontSize = 22;

        [Tooltip("The name and the level number, in the display face.")]
        [SerializeField] private int _valueFontSize = 40;

        [Tooltip("Messages, the level bar's end labels and badge names.")]
        [SerializeField] private int _bodyFontSize = 24;

        [Tooltip("The three lifetime figures and the coin balance, in the display face.")]
        [SerializeField] private int _statFontSize = 36;

        [Tooltip("Button labels, in the display face.")]
        [SerializeField] private int _buttonFontSize = 26;

        [Header("Art")]
        [Tooltip("The chunky display face for the name, the figures and the buttons. Falls back to the "
            + "built-in runtime font when unassigned.")]
        [SerializeField] private Font _displayFont;

        [Tooltip("White 9-sliced glossy button with a darker bottom lip, shared with the shop. Tinted at "
            + "runtime from the theme.")]
        [SerializeField] private Sprite _buttonSprite;

        [Tooltip("The coin drawn beside the balance, the same one the HUD and the shop use.")]
        [SerializeField] private Sprite _coinSprite;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private ProfileModel _profileModel;
        private ProfileSystem _profileSystem;
        private BadgeStatsModel _statsModel;
        private BadgeModel _badgeModel;
        private BadgeCatalog _badgeCatalog;
        private LevelProgressionModel _levelProgressionModel;
        private LevelCatalog _levelCatalog;
        private SettingsModel _settingsModel;
        private LocalizationSystem _localizationSystem;
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

        private Image _wellPlate;

        private RectTransform _identityRect;
        private Image _identityPlate;
        private Image _identityShadow;
        private Image _avatarRing;
        private Image _avatarRingGap;
        private Image _avatarDisc;
        private Text _avatarNumberText;
        private Text _nameLabelText;
        private Text _nameValueText;
        private Image _coinGlyph;
        private Text _coinText;
        private RectTransform _editButtonRect;
        private Image _editButtonPlate;
        private Text _editButtonText;

        private Text _messageText;

        private Text _avatarLabelText;
        private Text _avatarCounterText;

        private Text _levelLabelText;
        private Text _levelNumberText;
        private Text _pathPercentText;
        private Image _levelTrack;
        private Image _levelFillLip;
        private Image _levelFill;
        private RectTransform _levelFillLipRect;
        private RectTransform _levelFillRect;
        private Text _levelStartText;
        private Text _levelEndText;

        private Text _badgesLabelText;
        private Text _badgesCounterText;
        private RectTransform _badgeWallRect;

        private Text _accountStatusText;
        private RectTransform _saveAccountRect;
        private Image _saveAccountPlate;
        private Text _saveAccountText;
        private RectTransform _linkedStripRect;
        private Image _linkedStripPlate;
        private Image _linkedCheck;
        private Text _linkedText;

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
            internal AvatarSwatch(RectTransform root, Image selectionImage, Image gapImage, Image swatchImage, Text numberText)
            {
                Root = root;
                SelectionImage = selectionImage;
                GapImage = gapImage;
                SwatchImage = swatchImage;
                NumberText = numberText;
            }

            internal RectTransform Root { get; }

            internal Image SelectionImage { get; }

            internal Image GapImage { get; }

            internal Image SwatchImage { get; }

            internal Text NumberText { get; }
        }

        /// <summary>One built badge preview tile: the tinted box, the medallion and the name.</summary>
        private sealed class BadgeTile
        {
            internal BadgeTile(RectTransform root, Image box, Image medalLip, Image medal, Image glyph, Text nameText)
            {
                Root = root;
                Box = box;
                MedalLip = medalLip;
                Medal = medal;
                Glyph = glyph;
                NameText = nameText;
            }

            internal RectTransform Root { get; }

            internal Image Box { get; }

            internal Image MedalLip { get; }

            internal Image Medal { get; }

            internal Image Glyph { get; }

            internal Text NameText { get; }
        }

        [Inject]
        public void Construct(
            ProfileModel profileModel,
            ProfileSystem profileSystem,
            BadgeStatsModel statsModel,
            BadgeModel badgeModel,
            BadgeCatalog badgeCatalog,
            LevelProgressionModel levelProgressionModel,
            LevelCatalog levelCatalog,
            SettingsModel settingsModel,
            LocalizationSystem localizationSystem,
            TimerRunSystem timerRunSystem,
            AppleSignInProvider appleSignInProvider,
            GooglePlayGamesSignInProvider googlePlayGamesSignInProvider)
        {
            _profileModel = profileModel;
            _profileSystem = profileSystem;
            _statsModel = statsModel;
            _badgeModel = badgeModel;
            _badgeCatalog = badgeCatalog;
            _levelProgressionModel = levelProgressionModel;
            _levelCatalog = levelCatalog;
            _settingsModel = settingsModel;
            _localizationSystem = localizationSystem;
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
            if (_profileModel == null || _profileSystem == null || _statsModel == null || _badgeModel == null
                || _badgeCatalog == null || _levelProgressionModel == null || _levelCatalog == null
                || _settingsModel == null || _localizationSystem == null || _timerRunSystem == null)
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
            // drawn value and the model from ever disagreeing. The balance and the level are observed
            // for the same reason: both can move while the hub is open (a badge claim on the Badges tab
            // pays coins), and the card must show what the model holds when the player comes back.
            _profileModel.DisplayName.Subscribe(OnDisplayNameChanged).AddTo(_disposables);
            _profileModel.AvatarId.Subscribe(OnAvatarIdChanged).AddTo(_disposables);
            _profileModel.LinkStatus.Subscribe(OnLinkStatusChanged).AddTo(_disposables);
            _profileModel.CoinBalance.Subscribe(OnCoinBalanceChanged).AddTo(_disposables);
            _levelProgressionModel.CurrentLevelNumber.Subscribe(OnLevelChanged).AddTo(_disposables);
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

        /// <summary>The badge preview's whole area. Read by <see cref="HubPanelView"/>, which turns a
        /// tap inside it into a switch to the Badges tab — see the class remarks.</summary>
        internal RectTransform BadgeWallRect => _badgeWallRect;

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
        /// Routes a tap while the panel is open. The close cross wins while it is showing, then the
        /// editable rows in the order they are drawn; the card then swallows anything else, so a tap
        /// on a stat is a deliberate no-op rather than a dismissal. Only the scrim outside the card
        /// closes. The badge preview is not tested here: the hub reads <see cref="BadgeWallRect"/>
        /// before this method runs, since the answer to that tap is a tab switch it alone can make.
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

            // Only while showing: the hub hides this cross, and the EDIT button now sits where a hidden
            // cross would otherwise still swallow a tap.
            if (_closeButtonRect.gameObject.activeSelf
                && RectTransformUtility.RectangleContainsScreenPoint(_closeButtonRect, screenPosition, eventCamera))
            {
                Close();
                return;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(_identityRect, screenPosition, eventCamera))
            {
                // The whole identity plate edits the name — the EDIT button is the affordance, the plate
                // is the forgiving target.
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

        /// <summary>
        /// Shuts the card and releases the menu pause. Reachable by <c>HubPanelView</c>, which shuts the
        /// outgoing card when the player switches tabs; every other caller is this class's own dismiss
        /// paths.
        /// </summary>
        internal void Close()
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

            // Desktop and the Editor have no native keyboard. Asked up front rather than inferred from
            // Open's result: on Unity 6 the Editor hands back a non-null keyboard whose native side is
            // missing, and reading its status throws every frame. Said out loud rather than ignored: a
            // row that silently does nothing reads as a broken row.
            if (!TouchScreenKeyboard.isSupported)
            {
                SetMessage(NAME_KEYBOARD_UNAVAILABLE_TEXT);
                return;
            }

            _keyboard = TouchScreenKeyboard.Open(
                _profileModel.DisplayName.Value,
                TouchScreenKeyboardType.Default,
                false,
                false,
                false,
                false,
                NAME_KEYBOARD_PROMPT);

            // Belt and braces for a platform that claims support and still returns nothing.
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

        private void OnAvatarIdChanged(int avatarId)
        {
            RefreshAvatarSwatches();
            RefreshIdentityAvatar();
        }

        private void OnLinkStatusChanged(AccountLinkStatus linkStatus) => RefreshAccountRow();

        private void OnCoinBalanceChanged(int balance) => RefreshCoinRow();

        private void OnLevelChanged(int levelNumber) => RefreshLevelRow();

        /// <summary>Repaints the whole card from the models.</summary>
        private void Refresh()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            _cardImage.color = _currentTheme.CardBackground;
            _cardShadowImage.color = _currentTheme.CardShadow;
            _wellPlate.color = Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, WELL_TINT);

            for (int inkIndex = 0; inkIndex < _inkImages.Count; inkIndex++)
            {
                _inkImages[inkIndex].color = _currentTheme.Ink;
            }

            Color hairline = WithAlpha(_currentTheme.Ink, TRACK_TINT);
            for (int hairlineIndex = 0; hairlineIndex < _hairlineImages.Count; hairlineIndex++)
            {
                _hairlineImages[hairlineIndex].color = hairline;
            }

            _headerText.color = _currentTheme.Ink;
            _headerText.text = HEADER_TEXT;

            _identityPlate.color = _currentTheme.CardBackground;
            _identityShadow.color = _currentTheme.CardShadow;
            _nameLabelText.color = _currentTheme.SoftInk;
            _nameLabelText.text = NAME_LABEL_TEXT;
            _editButtonPlate.color = _currentTheme.GetFill(SECONDARY_KIND);
            _editButtonText.color = _currentTheme.CardBackground;
            _editButtonText.text = EDIT_TEXT;

            _avatarLabelText.color = _currentTheme.SoftInk;
            _avatarLabelText.text = AVATAR_LABEL_TEXT;
            _avatarCounterText.color = _currentTheme.SoftInk;

            _levelLabelText.color = _currentTheme.SoftInk;
            _levelLabelText.text = LEVEL_LABEL_TEXT;
            _levelTrack.color = WithAlpha(_currentTheme.Ink, TRACK_TINT);
            _levelFillLip.color = _currentTheme.GetShade(LEVEL_KIND);
            _levelFill.color = _currentTheme.GetFill(LEVEL_KIND);

            _badgesLabelText.color = _currentTheme.SoftInk;
            _badgesLabelText.text = BADGES_LABEL_TEXT;
            _badgesCounterText.color = _currentTheme.SoftInk;

            RefreshNameRow();
            RefreshCoinRow();
            RefreshIdentityAvatar();
            RefreshAvatarSwatches();
            RefreshLevelRow();
            RefreshStats();
            RefreshBadgeWall();
            RefreshAccountRow();
        }

        private void RefreshNameRow()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            string displayName = _profileModel.DisplayName.Value;
            bool hasName = !string.IsNullOrEmpty(displayName);

            _nameValueText.text = hasName ? displayName : NAME_PLACEHOLDER_TEXT;
            _nameValueText.color = hasName ? _currentTheme.Ink : _currentTheme.SoftInk;
        }

        private void RefreshCoinRow()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            _coinGlyph.color = _coinSprite != null ? Color.white : Color.clear;
            _coinText.color = GoldShade();
            _coinText.text = _profileModel.CoinBalance.Value.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>The big avatar in the identity plate: the picked swatch's colour on a disc, ringed
        /// in the accent so it reads as the one that is chosen even before the strip below is seen.</summary>
        private void RefreshIdentityAvatar()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            int avatarId = _profileModel.AvatarId.Value;
            _avatarRing.color = _currentTheme.Accent;
            _avatarRingGap.color = _currentTheme.CardBackground;
            _avatarDisc.color = AvatarColour(avatarId);
            _avatarNumberText.color = _currentTheme.CardBackground;
            _avatarNumberText.text = (avatarId + 1).ToString(CultureInfo.InvariantCulture);
        }

        private void RefreshAvatarSwatches()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            int selectedId = _profileModel.AvatarId.Value;

            _avatarCounterText.text = FormatCounter(selectedId + 1, ProfileModel.AVATAR_COUNT);

            for (int avatarIndex = 0; avatarIndex < _avatarSwatches.Length; avatarIndex++)
            {
                AvatarSwatch swatch = _avatarSwatches[avatarIndex];
                bool isSelected = avatarIndex == selectedId;

                Color swatchColour = AvatarColour(avatarIndex);
                swatch.SwatchImage.color = isSelected ? swatchColour : WithAlpha(swatchColour, UNPICKED_ALPHA);

                // The pick is marked by an accent ring with a card-coloured gap inside it — the same
                // ring the big avatar wears — rather than a plate behind the disc.
                swatch.SelectionImage.color = isSelected ? _currentTheme.Accent : Color.clear;
                swatch.GapImage.color = isSelected ? _currentTheme.CardBackground : Color.clear;

                swatch.NumberText.color = isSelected
                    ? _currentTheme.CardBackground
                    : WithAlpha(_currentTheme.CardBackground, UNPICKED_ALPHA);
            }
        }

        /// <summary>
        /// The level row. The game has no XP, so the bar is the level path itself: where the current
        /// level sits between the first authored level and the last. The number is the one truth; the
        /// bar only pictures it.
        /// </summary>
        private void RefreshLevelRow()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            int currentLevel = _levelProgressionModel.CurrentLevelNumber.Value;
            int maxLevel = Mathf.Max(_levelCatalog.MaxLevelNumber, currentLevel, 1);
            float fraction = maxLevel > 1 ? Mathf.Clamp01((currentLevel - 1f) / (maxLevel - 1f)) : 1f;

            _levelNumberText.color = _currentTheme.Ink;
            _levelNumberText.text = currentLevel.ToString(CultureInfo.InvariantCulture);

            _pathPercentText.color = _currentTheme.SoftInk;
            _pathPercentText.text = PATH_PREFIX + Mathf.RoundToInt(fraction * 100f).ToString(CultureInfo.InvariantCulture) + "%";

            float trackWidth = _levelTrack.rectTransform.sizeDelta.x;
            float fillWidth = Mathf.Max(fraction * trackWidth, fraction > 0f ? LEVEL_BAR_HEIGHT : 0f);
            _levelFillRect.sizeDelta = new Vector2(fillWidth, LEVEL_BAR_HEIGHT);
            _levelFillLipRect.sizeDelta = new Vector2(fillWidth, LEVEL_BAR_HEIGHT);

            _levelStartText.color = _currentTheme.SoftInk;
            _levelStartText.text = LEVEL_END_PREFIX + 1.ToString(CultureInfo.InvariantCulture);
            _levelEndText.color = _currentTheme.SoftInk;
            _levelEndText.text = LEVEL_END_PREFIX + maxLevel.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads the three lifetime figures one-shot, exactly as <see cref="BadgesPanelView"/> reads its
        /// badges: a total that moves while the card is open cannot happen, because the card is modal and
        /// the board is paused behind it. The other three counters stay on the Badges tab.
        /// </summary>
        private void RefreshStats()
        {
            WriteStat(0, BEST_SCORE_LABEL_TEXT, _statsModel.HighestScoreEver.Value, GoldShade());
            WriteStat(1, RUNS_LABEL_TEXT, _statsModel.TotalRunsPlayed.Value, _currentTheme.Ink);
            WriteStat(2, LINES_LABEL_TEXT, _statsModel.TotalLinesCleared.Value, _currentTheme.Ink);
        }

        private void WriteStat(int columnIndex, string label, long value, Color valueColour)
        {
            _statLabelTexts[columnIndex].color = _currentTheme.SoftInk;
            _statLabelTexts[columnIndex].text = label;

            _statValueTexts[columnIndex].color = valueColour;

            // Invariant culture so a device locale cannot change how a saved total reads back, matching
            // how BadgeStatsSystem persists them.
            _statValueTexts[columnIndex].text = value.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>The first six badges of the wall, as they are on the Badges tab: earned ones on a
        /// gold medallion, the rest greyed. A catalog shorter than six leaves the surplus tiles hidden.</summary>
        private void RefreshBadgeWall()
        {
            IReadOnlyList<BadgeProgress> badges = _badgeModel.Badges;

            int unlockedCount = 0;
            for (int badgeIndex = 0; badgeIndex < badges.Count; badgeIndex++)
            {
                if (badges[badgeIndex].IsUnlocked)
                {
                    unlockedCount++;
                }
            }

            _badgesCounterText.text = FormatCounter(unlockedCount, badges.Count);

            for (int tileIndex = 0; tileIndex < _badgeTiles.Length; tileIndex++)
            {
                RefreshBadgeTile(_badgeTiles[tileIndex], tileIndex, tileIndex < badges.Count ? badges[tileIndex] : null);
            }
        }

        private void RefreshBadgeTile(BadgeTile tile, int tileIndex, BadgeProgress progress)
        {
            bool exists = progress != null;
            if (tile.Root.gameObject.activeSelf != exists)
            {
                tile.Root.gameObject.SetActive(exists);
            }

            if (!exists)
            {
                return;
            }

            bool isUnlocked = progress.IsUnlocked;
            float alpha = isUnlocked ? 1f : UNPICKED_ALPHA;

            // Each tile's ground is a different kind's fill, faint, over the card — the reference's
            // coloured squares, in-palette — so the six read as a set of distinct things.
            int tileKind = (tileIndex % ThemeDefinition.KIND_COUNT) + 1;
            tile.Box.color = Color.Lerp(_currentTheme.CardBackground, _currentTheme.GetFill(tileKind), TILE_TINT);

            // Gold once earned; the soft ink while it is still a goal, so a locked medallion reads as
            // pewter rather than as a gold one that happens to be faint.
            tile.MedalLip.color = WithAlpha(isUnlocked ? GoldShade() : _currentTheme.SoftInk, alpha);
            tile.Medal.color = WithAlpha(isUnlocked ? _currentTheme.Accent : _currentTheme.EmptyCellOutline, alpha);

            BadgeConfig config = _badgeCatalog.Find(progress.Definition.Id);
            Sprite icon = config != null ? config.Icon : null;
            tile.Glyph.sprite = icon;
            tile.Glyph.color = icon == null
                ? Color.clear
                : WithAlpha(isUnlocked ? GoldShade() : _currentTheme.SoftInk, alpha);

            tile.NameText.color = isUnlocked ? _currentTheme.Ink : _currentTheme.SoftInk;
            tile.NameText.text = DisplayNameOf(config, progress.Definition.Id);
        }

        /// <summary>
        /// The badge's name in the player's language: the string-table entry when a key is authored,
        /// else the authored fallback name, else the id — the same resolution <see cref="BadgesPanelView"/>
        /// makes, so the preview and the wall never disagree on a name.
        /// </summary>
        private string DisplayNameOf(BadgeConfig config, string badgeId)
        {
            if (config == null)
            {
                return badgeId;
            }

            if (!string.IsNullOrEmpty(config.DisplayNameKey))
            {
                string translated = _localizationSystem.Translate(config.DisplayNameKey);
                if (!string.IsNullOrEmpty(translated) && translated != config.DisplayNameKey)
                {
                    return translated;
                }
            }

            return !string.IsNullOrEmpty(config.DisplayName) ? config.DisplayName : badgeId;
        }

        /// <summary>
        /// The bottom of the card: the one call to action while there is something to do, or the green
        /// strip once the account is saved. The two share a rect, so exactly one of them is ever showing.
        /// </summary>
        private void RefreshAccountRow()
        {
            if (_panel == null || _currentTheme == null)
            {
                return;
            }

            AccountLinkStatus linkStatus = _profileModel.LinkStatus.Value;
            bool isAnonymous = linkStatus == AccountLinkStatus.Anonymous;

            _accountStatusText.color = _currentTheme.SoftInk;
            _accountStatusText.text = AccountStatusTextFor(linkStatus);

            // The offer only exists while there is something to save. A linked account has nowhere left
            // to go from here, so the button is gone rather than disabled, and the strip takes its place.
            if (_saveAccountRect.gameObject.activeSelf != isAnonymous)
            {
                _saveAccountRect.gameObject.SetActive(isAnonymous);
            }

            if (_linkedStripRect.gameObject.activeSelf == isAnonymous)
            {
                _linkedStripRect.gameObject.SetActive(!isAnonymous);
            }

            if (isAnonymous)
            {
                Color buttonColour = _currentTheme.GetFill(PRIMARY_KIND);
                _saveAccountPlate.color = _isLinking
                    ? Color.Lerp(buttonColour, _currentTheme.SoftInk, BUSY_FADE)
                    : buttonColour;
                _saveAccountText.color = _currentTheme.CardBackground;
                _saveAccountText.text = SaveAccountLabel();
                return;
            }

            _linkedStripPlate.color = Color.Lerp(
                _currentTheme.CardBackground, _currentTheme.GetHighlight(LINKED_KIND), LINKED_TINT);
            _linkedCheck.color = _currentTheme.GetShade(LINKED_KIND);
            _linkedText.color = _currentTheme.GetShade(LINKED_KIND);
            _linkedText.text = linkStatus == AccountLinkStatus.LinkedApple ? LINKED_APPLE_TEXT : LINKED_GOOGLE_TEXT;
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

        private void SetMessage(string message)
        {
            if (_messageText == null)
            {
                return;
            }

            _messageText.text = message;
            // InvalidPreview is authored translucent for the board; a line of text needs it solid.
            Color messageColour = _currentTheme != null ? _currentTheme.InvalidPreview : Color.clear;
            _messageText.color = new Color(messageColour.r, messageColour.g, messageColour.b, _currentTheme != null ? 1f : 0f);
        }

        /// <summary>
        /// PLACEHOLDER ART. Each preset avatar is a flat swatch rather than a drawn character, because
        /// the project has no avatar sprites yet — but the swatch is now the theme's own kind fill for
        /// that id, cycling through the palette, so the twelve are told apart at a glance and still
        /// repaint with the season. Numbered so a player can name the one they picked. Replace with a
        /// sprite lookup once the atlas exists.
        /// </summary>
        private Color AvatarColour(int avatarId)
        {
            int kind = (avatarId % ThemeDefinition.KIND_COUNT) + 1;

            // Two laps of the five kinds would repeat exactly; the second lap takes the highlight so
            // ids 1–5 and 6–10 stay tellable apart, and 11–12 the shade.
            int lap = avatarId / ThemeDefinition.KIND_COUNT;
            switch (lap)
            {
                case 0:
                    return _currentTheme.GetFill(kind);
                case 1:
                    return _currentTheme.GetHighlight(kind);
                default:
                    return _currentTheme.GetShade(kind);
            }
        }

        private Color GoldShade() => Darken(_currentTheme.Accent, GOLD_SHADE);

        /// <summary>The same hue, pulled toward black — a shade of the colour itself, not a blend with the
        /// ink, so a blue-inked season still gets a gold shade rather than an olive one.</summary>
        private static Color Darken(Color colour, float amount)
            => new Color(colour.r * (1f - amount), colour.g * (1f - amount), colour.b * (1f - amount), colour.a);

        private static string FormatCounter(long value, long total)
            => value.ToString(CultureInfo.InvariantCulture) + COUNTER_SEPARATOR + total.ToString(CultureInfo.InvariantCulture);

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);

        // ---------------------------------------------------------------------------- building

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
            float headerY = cardHalfHeight - HEADER_INSET;

            // The card's own title and close cross, kept for a stand-alone open; the hub hides both.
            _headerText = CreateLabel(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-cardHalfWidth + SIDE_INSET, headerY));
            BuildCloseButton(_cardRect, new Vector2(cardHalfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f), headerY));

            BuildWell();

            float contentWidth = _cardSize.x - ((WELL_INSET + WELL_PADDING) * 2f);
            float leftX = -contentWidth * 0.5f;
            float rightX = contentWidth * 0.5f;

            BuildIdentityRow(contentWidth, leftX, rightX);
            BuildAvatarSection(leftX, rightX);
            BuildLevelSection(contentWidth, leftX, rightX);
            BuildStatsStrip(contentWidth);
            BuildBadgeWall(contentWidth, leftX, rightX);
            BuildAccountSection(contentWidth);

            _panel = panelObject;
        }

        /// <summary>The sunken plate under everything: one rounded Image, drawn first so every row sits
        /// on it.</summary>
        private void BuildWell()
        {
            var wellObject = new GameObject("Well", typeof(RectTransform), typeof(Image));
            var wellRect = (RectTransform)wellObject.transform;
            wellRect.SetParent(_cardRect, false);
            wellRect.anchorMin = Vector2.zero;
            wellRect.anchorMax = Vector2.one;
            wellRect.pivot = new Vector2(0.5f, 0.5f);
            wellRect.offsetMin = new Vector2(WELL_INSET, WELL_INSET);
            wellRect.offsetMax = new Vector2(-WELL_INSET, -WELL_INSET);
            _wellPlate = ConfigureRounded(wellObject.GetComponent<Image>(), WELL_CORNER_RADIUS);
        }

        /// <summary>The identity plate: ringed avatar on the left, name and coins in the middle, the EDIT
        /// button on the right. The plate itself is the name row's tap target.</summary>
        private void BuildIdentityRow(float contentWidth, float leftX, float rightX)
        {
            var rowObject = new GameObject("IdentityRow", typeof(RectTransform));
            _identityRect = (RectTransform)rowObject.transform;
            _identityRect.SetParent(_cardRect, false);
            Centre(_identityRect, new Vector2(contentWidth, IDENTITY_HEIGHT));
            _identityRect.anchoredPosition = new Vector2(0f, TopY(IDENTITY_TOP, IDENTITY_HEIGHT));

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(_identityRect, false);
            Centre(shadowRect, new Vector2(contentWidth, IDENTITY_HEIGHT));
            shadowRect.anchoredPosition = new Vector2(0f, -4f);
            _identityShadow = ConfigureRounded(shadowObject.GetComponent<Image>(), PLATE_CORNER_RADIUS);

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(_identityRect, false);
            Centre(plateRect, new Vector2(contentWidth, IDENTITY_HEIGHT));
            _identityPlate = ConfigureRounded(plateObject.GetComponent<Image>(), PLATE_CORNER_RADIUS);

            // Avatar: accent ring, card-coloured gap, then the swatch disc, all concentric discs.
            float ringSize = AVATAR_DISC_SIZE + ((AVATAR_RING_GAP + AVATAR_RING_THICKNESS) * 2f);
            float gapSize = AVATAR_DISC_SIZE + (AVATAR_RING_GAP * 2f);
            var avatarPosition = new Vector2((-contentWidth * 0.5f) + IDENTITY_PADDING + (ringSize * 0.5f), 0f);
            _avatarRing = BuildDisc(_identityRect, "AvatarRing", ringSize, avatarPosition);
            _avatarRingGap = BuildDisc(_identityRect, "AvatarRingGap", gapSize, avatarPosition);
            _avatarDisc = BuildDisc(_identityRect, "AvatarDisc", AVATAR_DISC_SIZE, avatarPosition);
            _avatarNumberText = CreateLabel(
                _identityRect, "AvatarNumber", _valueFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                avatarPosition, _displayFont);

            float nameX = (-contentWidth * 0.5f) + NAME_COLUMN_X;
            _nameLabelText = CreateLabel(
                _identityRect, "NameLabel", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(nameX, NAME_LABEL_RISE));
            _nameValueText = CreateLabel(
                _identityRect, "NameValue", _valueFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(nameX, NAME_VALUE_RISE), _displayFont);

            var coinObject = new GameObject("CoinGlyph", typeof(RectTransform), typeof(Image));
            var coinRect = (RectTransform)coinObject.transform;
            coinRect.SetParent(_identityRect, false);
            Centre(coinRect, new Vector2(COIN_GLYPH_SIZE, COIN_GLYPH_SIZE));
            coinRect.anchoredPosition = new Vector2(nameX + (COIN_GLYPH_SIZE * 0.5f), -COIN_ROW_DROP);
            _coinGlyph = ConfigureGlyph(coinObject.GetComponent<Image>(), _coinSprite);

            _coinText = CreateLabel(
                _identityRect, "CoinBalance", _buttonFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(nameX + COIN_GLYPH_SIZE + COIN_TEXT_GAP, -COIN_ROW_DROP), _displayFont);

            _editButtonRect = BuildGlossyButton(
                _identityRect, "EditButton", new Vector2(EDIT_BUTTON_WIDTH, EDIT_BUTTON_HEIGHT), out _editButtonPlate);
            _editButtonRect.anchoredPosition =
                new Vector2((contentWidth * 0.5f) - IDENTITY_PADDING - (EDIT_BUTTON_WIDTH * 0.5f), 0f);
            _editButtonText = CreateLabel(
                _editButtonRect, "EditLabel", _buttonFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, BUTTON_LABEL_RISE), _displayFont);

            _messageText = CreateLabel(
                _cardRect, "Message", _bodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX + 8f, TopY(MESSAGE_Y, 0f)));
        }

        private void BuildAvatarSection(float leftX, float rightX)
        {
            _avatarLabelText = CreateLabel(
                _cardRect, "AvatarLabel", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX + 8f, TopY(AVATAR_LABEL_Y, 0f)));
            _avatarCounterText = CreateLabel(
                _cardRect, "AvatarCounter", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(rightX - 8f, TopY(AVATAR_LABEL_Y, 0f)));

            for (int avatarIndex = 0; avatarIndex < ProfileModel.AVATAR_COUNT; avatarIndex++)
            {
                int column = avatarIndex % AVATAR_COLUMN_COUNT;
                int row = avatarIndex / AVATAR_COLUMN_COUNT;

                float x = (column - ((AVATAR_COLUMN_COUNT - 1) * 0.5f)) * AVATAR_PITCH_X;
                float y = TopY(AVATAR_ROWS_TOP + (row * AVATAR_PITCH_Y), AVATAR_CELL_SIZE);

                _avatarSwatches[avatarIndex] = BuildAvatarSwatch(avatarIndex, new Vector2(x, y));
            }
        }

        private AvatarSwatch BuildAvatarSwatch(int avatarIndex, Vector2 anchoredPosition)
        {
            var swatchObject = new GameObject($"AvatarSwatch_{avatarIndex}", typeof(RectTransform));
            var swatchRoot = (RectTransform)swatchObject.transform;
            swatchRoot.SetParent(_cardRect, false);
            Centre(swatchRoot, new Vector2(AVATAR_CELL_SIZE + AVATAR_SELECTION_PADDING, AVATAR_CELL_SIZE + AVATAR_SELECTION_PADDING));
            swatchRoot.anchoredPosition = anchoredPosition;

            Image selectionImage = BuildDisc(swatchRoot, "Selection", AVATAR_CELL_SIZE + AVATAR_SELECTION_PADDING, Vector2.zero);
            Image gapImage = BuildDisc(swatchRoot, "Gap", AVATAR_CELL_SIZE + (AVATAR_SELECTION_PADDING * 0.5f), Vector2.zero);
            Image fillImage = BuildDisc(swatchRoot, "Swatch", AVATAR_CELL_SIZE, Vector2.zero);

            Text numberText = CreateLabel(
                swatchRoot, "Number", _buttonFontSize, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, _displayFont);

            // The number is the whole of the identity right now — see AvatarColour.
            numberText.text = (avatarIndex + 1).ToString(CultureInfo.InvariantCulture);

            return new AvatarSwatch(swatchRoot, selectionImage, gapImage, fillImage, numberText);
        }

        private void BuildLevelSection(float contentWidth, float leftX, float rightX)
        {
            float labelY = TopY(LEVEL_LABEL_Y, 0f);
            _levelLabelText = CreateLabel(
                _cardRect, "LevelLabel", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX + 8f, labelY));
            _levelNumberText = CreateLabel(
                _cardRect, "LevelNumber", _valueFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(LEVEL_NUMBER_X, labelY), _displayFont);
            _pathPercentText = CreateLabel(
                _cardRect, "PathPercent", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(rightX - 8f, labelY));

            float barY = TopY(LEVEL_BAR_TOP, LEVEL_BAR_HEIGHT);

            var trackObject = new GameObject("LevelTrack", typeof(RectTransform), typeof(Image));
            var trackRect = (RectTransform)trackObject.transform;
            trackRect.SetParent(_cardRect, false);
            Centre(trackRect, new Vector2(contentWidth, LEVEL_BAR_HEIGHT));
            trackRect.anchoredPosition = new Vector2(0f, barY);
            _levelTrack = ConfigureRounded(trackObject.GetComponent<Image>(), LEVEL_BAR_HEIGHT * 0.5f);

            // The fill grows from the track's left edge, so both fill layers pivot there; RefreshLevelRow
            // only ever sets their width.
            _levelFillLip = BuildLeftAnchoredBar(trackRect, "LevelFillLip", new Vector2(0f, -LEVEL_BAR_LIP), out _levelFillLipRect);
            _levelFill = BuildLeftAnchoredBar(trackRect, "LevelFill", Vector2.zero, out _levelFillRect);

            float endsY = TopY(LEVEL_ENDS_Y, 0f);
            _levelStartText = CreateLabel(
                _cardRect, "LevelStart", _bodyFontSize - 4, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX + 8f, endsY));
            _levelEndText = CreateLabel(
                _cardRect, "LevelEnd", _bodyFontSize - 4, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(rightX - 8f, endsY));
        }

        private Image BuildLeftAnchoredBar(RectTransform track, string objectName, Vector2 offset, out RectTransform barRect)
        {
            var barObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            barRect = (RectTransform)barObject.transform;
            barRect.SetParent(track, false);
            barRect.anchorMin = new Vector2(0f, 0.5f);
            barRect.anchorMax = new Vector2(0f, 0.5f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(0f, LEVEL_BAR_HEIGHT);
            barRect.anchoredPosition = offset;
            return ConfigureRounded(barObject.GetComponent<Image>(), LEVEL_BAR_HEIGHT * 0.5f);
        }

        /// <summary>Three figures between two hairlines, with a hairline divider between each pair.</summary>
        private void BuildStatsStrip(float contentWidth)
        {
            BuildHairline("StripTop", new Vector2(contentWidth, HAIRLINE_THICKNESS), new Vector2(0f, TopY(STRIP_TOP, HAIRLINE_THICKNESS)));
            BuildHairline("StripBottom", new Vector2(contentWidth, HAIRLINE_THICKNESS), new Vector2(0f, TopY(STRIP_BOTTOM, HAIRLINE_THICKNESS)));

            float columnWidth = contentWidth / STAT_COLUMN_COUNT;
            float dividerHeight = STRIP_BOTTOM - STRIP_TOP - (DIVIDER_INSET * 2f);
            float dividerY = TopY(STRIP_TOP + DIVIDER_INSET, dividerHeight);

            for (int columnIndex = 0; columnIndex < STAT_COLUMN_COUNT; columnIndex++)
            {
                float x = (columnIndex - ((STAT_COLUMN_COUNT - 1) * 0.5f)) * columnWidth;

                if (columnIndex > 0)
                {
                    BuildHairline(
                        $"StripDivider_{columnIndex}", new Vector2(HAIRLINE_THICKNESS, dividerHeight),
                        new Vector2(x - (columnWidth * 0.5f), dividerY));
                }

                _statValueTexts[columnIndex] = CreateLabel(
                    _cardRect, $"StatValue_{columnIndex}", _statFontSize, FontStyle.Normal,
                    TextAnchor.MiddleCenter, new Vector2(x, TopY(STRIP_VALUE_Y, 0f)), _displayFont);
                _statLabelTexts[columnIndex] = CreateLabel(
                    _cardRect, $"StatLabel_{columnIndex}", _sectionFontSize - 2, FontStyle.Bold,
                    TextAnchor.MiddleCenter, new Vector2(x, TopY(STRIP_LABEL_Y, 0f)));
            }
        }

        private void BuildHairline(string objectName, Vector2 size, Vector2 anchoredPosition)
        {
            var lineObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var lineRect = (RectTransform)lineObject.transform;
            lineRect.SetParent(_cardRect, false);
            Centre(lineRect, size);
            lineRect.anchoredPosition = anchoredPosition;

            var lineImage = lineObject.GetComponent<Image>();
            lineImage.sprite = null;
            lineImage.color = Color.clear;
            lineImage.raycastTarget = false;
            _hairlineImages.Add(lineImage);
        }

        /// <summary>The 3×2 badge preview. One invisible rect spans all six tiles — that is what the hub
        /// hit-tests, so a tap in the gaps between tiles opens the wall too.</summary>
        private void BuildBadgeWall(float contentWidth, float leftX, float rightX)
        {
            float labelY = TopY(BADGES_LABEL_Y, 0f);
            _badgesLabelText = CreateLabel(
                _cardRect, "BadgesLabel", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(leftX + 8f, labelY));
            _badgesCounterText = CreateLabel(
                _cardRect, "BadgesCounter", _sectionFontSize, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(rightX - 8f, labelY));

            var wallObject = new GameObject("BadgeWall", typeof(RectTransform));
            _badgeWallRect = (RectTransform)wallObject.transform;
            _badgeWallRect.SetParent(_cardRect, false);
            Centre(_badgeWallRect, new Vector2(contentWidth, TILE_WALL_HEIGHT));
            _badgeWallRect.anchoredPosition = new Vector2(0f, TopY(TILES_TOP, TILE_WALL_HEIGHT));

            float boxWidth = (contentWidth - (TILE_GAP * (BADGE_PREVIEW_COLUMNS - 1))) / BADGE_PREVIEW_COLUMNS;
            float wallHalfHeight = TILE_WALL_HEIGHT * 0.5f;

            for (int tileIndex = 0; tileIndex < BADGE_PREVIEW_COUNT; tileIndex++)
            {
                int column = tileIndex % BADGE_PREVIEW_COLUMNS;
                int row = tileIndex / BADGE_PREVIEW_COLUMNS;

                float x = (column - ((BADGE_PREVIEW_COLUMNS - 1) * 0.5f)) * (boxWidth + TILE_GAP);
                float boxTop = wallHalfHeight - (row * TILE_ROW_PITCH);

                _badgeTiles[tileIndex] = BuildBadgeTile(tileIndex, new Vector2(boxWidth, TILE_BOX_HEIGHT), x, boxTop);
            }
        }

        private BadgeTile BuildBadgeTile(int tileIndex, Vector2 boxSize, float x, float boxTop)
        {
            var tileObject = new GameObject($"BadgeTile_{tileIndex}", typeof(RectTransform));
            var tileRoot = (RectTransform)tileObject.transform;
            tileRoot.SetParent(_badgeWallRect, false);
            Centre(tileRoot, new Vector2(boxSize.x, TILE_ROW_PITCH));
            tileRoot.anchoredPosition = new Vector2(x, boxTop - (TILE_ROW_PITCH * 0.5f));

            float boxY = (TILE_ROW_PITCH * 0.5f) - (boxSize.y * 0.5f);

            var boxObject = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var boxRect = (RectTransform)boxObject.transform;
            boxRect.SetParent(tileRoot, false);
            Centre(boxRect, boxSize);
            boxRect.anchoredPosition = new Vector2(0f, boxY);
            Image box = ConfigureRounded(boxObject.GetComponent<Image>(), TILE_BOX_CORNER_RADIUS);

            Image medalLip = BuildDisc(tileRoot, "MedalLip", MEDAL_SIZE, new Vector2(0f, boxY - MEDAL_LIP));
            Image medal = BuildDisc(tileRoot, "Medal", MEDAL_SIZE, new Vector2(0f, boxY));

            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(tileRoot, false);
            Centre(glyphRect, new Vector2(MEDAL_GLYPH_SIZE, MEDAL_GLYPH_SIZE));
            glyphRect.anchoredPosition = new Vector2(0f, boxY);
            Image glyph = ConfigureGlyph(glyphObject.GetComponent<Image>(), null);

            Text nameText = CreateLabel(
                tileRoot, "Name", _bodyFontSize - 4, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, (TILE_ROW_PITCH * 0.5f) - TILE_NAME_DROP));

            return new BadgeTile(tileRoot, box, medalLip, medal, glyph, nameText);
        }

        private void BuildAccountSection(float contentWidth)
        {
            _accountStatusText = CreateLabel(
                _cardRect, "AccountStatus", _sectionFontSize - 2, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, TopY(ACCOUNT_LABEL_Y, 0f)));

            var actionSize = new Vector2(contentWidth, ACTION_HEIGHT);
            float actionY = TopY(ACTION_TOP, ACTION_HEIGHT);

            _saveAccountRect = BuildGlossyButton(_cardRect, "SaveAccountButton", actionSize, out _saveAccountPlate);
            _saveAccountRect.anchoredPosition = new Vector2(0f, actionY);
            _saveAccountText = CreateLabel(
                _saveAccountRect, "SaveAccountLabel", _buttonFontSize + 4, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Vector2(0f, BUTTON_LABEL_RISE), _displayFont);

            // The linked strip shares the button's rect, so whichever of the two is showing sits in the
            // same place at the bottom of the card.
            var stripObject = new GameObject("LinkedStrip", typeof(RectTransform), typeof(Image));
            _linkedStripRect = (RectTransform)stripObject.transform;
            _linkedStripRect.SetParent(_cardRect, false);
            Centre(_linkedStripRect, actionSize);
            _linkedStripRect.anchoredPosition = new Vector2(0f, actionY);
            _linkedStripPlate = ConfigureRounded(stripObject.GetComponent<Image>(), PLATE_CORNER_RADIUS);

            var checkObject = new GameObject("Check", typeof(RectTransform), typeof(Image));
            var checkRect = (RectTransform)checkObject.transform;
            checkRect.SetParent(_linkedStripRect, false);
            Centre(checkRect, new Vector2(COIN_GLYPH_SIZE, COIN_GLYPH_SIZE));
            checkRect.anchoredPosition = new Vector2((-contentWidth * 0.5f) + IDENTITY_PADDING + (COIN_GLYPH_SIZE * 0.5f), 0f);
            _linkedCheck = ConfigureGlyph(checkObject.GetComponent<Image>(), UiSpriteFactory.CheckMark);

            _linkedText = CreateLabel(
                _linkedStripRect, "LinkedLabel", _bodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2((-contentWidth * 0.5f) + IDENTITY_PADDING + COIN_GLYPH_SIZE + (COIN_TEXT_GAP * 2f), 0f));
        }

        /// <summary>Two bars crossed at right angles — the close glyph, as on the other cards.</summary>
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

                Image barImage = ConfigureRounded(barObject.GetComponent<Image>(), CROSS_THICKNESS * 0.5f);
                _inkImages.Add(barImage);
            }
        }

        /// <summary>
        /// A glossy 3D button: the shop's white button sprite, sliced, tinted at paint time with a
        /// kind's fill — its baked highlight and lip supply the bevel. The returned rect is the hit
        /// area; the caller positions it and adds its label.
        /// </summary>
        private RectTransform BuildGlossyButton(RectTransform parent, string objectName, Vector2 size, out Image plate)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(parent, false);
            Centre(buttonRect, size);

            plate = buttonObject.GetComponent<Image>();
            if (_buttonSprite != null)
            {
                plate.sprite = _buttonSprite;
                plate.type = Image.Type.Sliced;
                plate.pixelsPerUnitMultiplier = BUTTON_SLICE_SCALE;
                plate.color = Color.clear;
                plate.raycastTarget = false;
            }
            else
            {
                ConfigureRounded(plate, PLATE_CORNER_RADIUS);
            }

            return buttonRect;
        }

        private static Image BuildDisc(RectTransform parent, string objectName, float diameter, Vector2 anchoredPosition)
        {
            var discObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var discRect = (RectTransform)discObject.transform;
            discRect.SetParent(parent, false);
            Centre(discRect, new Vector2(diameter, diameter));
            discRect.anchoredPosition = anchoredPosition;
            return ConfigureGlyph(discObject.GetComponent<Image>(), UiSpriteFactory.Circle);
        }

        /// <summary>Builds a wordless label. Callers fill it in from <see cref="Refresh"/>.</summary>
        private static Text CreateLabel(
            RectTransform parent,
            string objectName,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 anchoredPosition,
            Font font = null)
        {
            Text text = UiTextFactory.Create(parent, objectName, fontSize, fontStyle, Color.clear, font);
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

        /// <summary>Anchored Y of an element <paramref name="height"/> tall whose top edge sits
        /// <paramref name="offsetFromTop"/> below the card's top edge.</summary>
        private float TopY(float offsetFromTop, float height)
            => (_cardSize.y * 0.5f) - offsetFromTop - (height * 0.5f);

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not through
        // an EventSystem, and this scene has none. Every rounded Image shares the one rounded-square
        // sprite, sliced to its own radius, so the card batches with the rest of the HUD.
        private static Image ConfigureRounded(Image image, float radius)
        {
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / radius;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A non-interactive picture: aspect kept, no raycast, painted later.</summary>
        private static Image ConfigureGlyph(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }
    }
}
