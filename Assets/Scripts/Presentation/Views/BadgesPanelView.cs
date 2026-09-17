using System.Collections.Generic;
using System.Text;
using MustyBlockBlast.Core;
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
    /// The badges overlay: the whole lifetime-achievement wall as a grid of tiles, each locked or
    /// unlocked, each with its authored icon (see <see cref="BadgeConfig.Icon"/>), its name and how
    /// close the player is. Read-only — a tile is a status light, not a button, so tapping one
    /// deliberately does nothing.
    /// <para>
    /// Unpaged, unlike <see cref="LevelPathPanelView"/>: the authored set is small enough to fit the
    /// card in one 2x5 grid, and a wall you have to page through stops reading as a wall. Should the
    /// catalog outgrow the grid the surplus is simply not drawn, and the header counter still reports
    /// the true totals — a wrong-looking count would be worse than a short list.
    /// </para>
    /// <para>
    /// Built once in <see cref="Start"/> and toggled with SetActive, exactly as
    /// <see cref="SettingsPanelView"/> and <see cref="LevelPathPanelView"/> are. It repaints on
    /// <see cref="Open"/> rather than subscribing to unlocks: a badge that falls while the card is
    /// closed simply shows as unlocked the next time it is opened, which is the whole of the feedback
    /// this slice promises.
    /// </para>
    /// <para>
    /// Modal like the other two cards: while open it holds the timed countdown through
    /// <see cref="TimerRunSystem.SetMenuPaused"/>, and the gate chain in <see cref="BoardInputView"/>
    /// guarantees no two of the three panels can be open at once — which is what keeps that single
    /// shared pause flag from having two owners.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BadgesPanelView : MonoBehaviour
    {
        private const int GRID_COLUMN_COUNT = 2;
        private const int GRID_ROW_COUNT = 5;

        /// <summary>Tiles the card can draw. The authored catalog is expected to fit inside this.</summary>
        private const int TILE_COUNT = GRID_COLUMN_COUNT * GRID_ROW_COUNT;

        // Layout, in canvas reference pixels, matching the settings and level path cards so all three
        // overlays read as one family.
        private const float HEADER_INSET = 92f;
        private const float SIDE_INSET = 60f;
        private const float ICON_BUTTON_SIZE = 92f;
        private const float GRID_TOP_INSET = 194f;

        private const float TILE_WIDTH = 376f;
        private const float TILE_HEIGHT = 150f;
        private const float TILE_SPACING_X = 396f;
        private const float TILE_SPACING_Y = 172f;
        private const float TILE_TEXT_INSET = 26f;
        private const float TILE_DOT_SIZE = 28f;

        /// <summary>The badge's icon disc on the tile's left, and the glyph drawn inside it. Sized so the
        /// disc nearly fills the tile's height, as a medal would, and the name and counter sit to its
        /// right.</summary>
        private const float TILE_ICON_DISC_SIZE = 104f;
        private const float TILE_ICON_GLYPH_SIZE = 64f;
        private const float TILE_ICON_TEXT_GAP = 18f;

        /// <summary>How far a locked tile's icon disc is tinted from the card towards the ink, so it
        /// still separates from the (already tinted) locked plate behind it.</summary>
        private const float LOCKED_DISC_TINT = 0.06f;

        /// <summary>
        /// Alpha applied to a locked tile. Deliberately the same dim <see cref="LevelPathPanelView"/>
        /// uses for a locked node and <see cref="PowerUpInventoryView"/> for an empty slot: "you
        /// cannot have this yet" is one visual idea and should look identical wherever it appears.
        /// </summary>
        private const float LOCKED_TILE_ALPHA = 0.35f;

        /// <summary>Separator in the header and per-tile counters. A symbol, not a word — nothing here
        /// for a translator to translate, so it stays out of the String Table.</summary>
        private const string COUNTER_SEPARATOR = " / ";

        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);
        private readonly BadgeTile[] _tiles = new BadgeTile[TILE_COUNT];

        /// <summary>Repaint bucket: every Image that follows the theme's ink (the close cross), so a
        /// theme switch is one tight loop instead of a hierarchy walk.</summary>
        private readonly List<Image> _inkImages = new List<Image>(4);

        [Header("Layout")]
        // Shorter than the level path card: this grid is unpaged and has no pager row under it, so a
        // full-height card would be mostly empty space below the last tile.
        [SerializeField] private Vector2 _cardSize = new Vector2(880f, 1130f);
        [SerializeField] private int _headerFontSize = 64;
        [SerializeField] private int _titleFontSize = 34;
        [SerializeField] private int _progressFontSize = 28;

        [Header("Palette")]
        [SerializeField] private Color _scrimColour = new Color(0.17f, 0.15f, 0.20f, 0.55f);

        private BadgeModel _badgeModel;
        private BadgeCatalog _badgeCatalog;
        private SettingsModel _settingsModel;
        private TimerRunSystem _timerRunSystem;

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _cardRect;
        private Image _cardImage;
        private Image _cardShadowImage;
        private RectTransform _closeButtonRect;
        private Text _headerText;

        private ThemeDefinition _currentTheme;

        /// <summary>One built tile widget. Rebuilt never, repainted on every open.</summary>
        private sealed class BadgeTile
        {
            internal BadgeTile(
                RectTransform root, Image plateImage, Image shadowImage, Image iconDisc, Image iconGlyph,
                Image doneDot, Text titleText, Text progressText)
            {
                Root = root;
                PlateImage = plateImage;
                ShadowImage = shadowImage;
                IconDisc = iconDisc;
                IconGlyph = iconGlyph;
                DoneDot = doneDot;
                TitleText = titleText;
                ProgressText = progressText;
            }

            internal RectTransform Root { get; }

            internal Image PlateImage { get; }

            internal Image ShadowImage { get; }

            internal Image IconDisc { get; }

            internal Image IconGlyph { get; }

            internal Image DoneDot { get; }

            internal Text TitleText { get; }

            internal Text ProgressText { get; }
        }

        [Inject]
        public void Construct(
            BadgeModel badgeModel,
            BadgeCatalog badgeCatalog,
            SettingsModel settingsModel,
            TimerRunSystem timerRunSystem)
        {
            _badgeModel = badgeModel;
            _badgeCatalog = badgeCatalog;
            _settingsModel = settingsModel;
            _timerRunSystem = timerRunSystem;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            if (_badgeModel == null || _badgeCatalog == null || _settingsModel == null || _timerRunSystem == null)
            {
                Debug.LogError(
                    $"{nameof(BadgesPanelView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            BuildPanel();
            _panel.SetActive(false);

            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);
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

        /// <summary>Shows the panel, repainted from the model. Re-opening never double-pauses the
        /// clock: an already-open panel returns immediately.</summary>
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
        }

        /// <summary>
        /// Routes a tap while the panel is open. The close cross wins; the card then swallows anything
        /// else — a tile is read-only, so landing on one is a deliberate no-op rather than a dismissal.
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

        /// <summary>Repaints the whole card from the model: header counter and every tile.</summary>
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

            IReadOnlyList<BadgeProgress> badges = _badgeModel.Badges;

            int unlockedCount = 0;
            for (int badgeIndex = 0; badgeIndex < badges.Count; badgeIndex++)
            {
                if (badges[badgeIndex].IsUnlocked)
                {
                    unlockedCount++;
                }
            }

            // Counts the whole catalog, not just what fits the grid, so the header stays truthful even
            // if the content outgrows the card.
            _headerText.color = _currentTheme.Ink;
            _headerText.text = FormatCounter(unlockedCount, badges.Count);

            for (int tileIndex = 0; tileIndex < _tiles.Length; tileIndex++)
            {
                RefreshTile(tileIndex, tileIndex < badges.Count ? badges[tileIndex] : null);
            }
        }

        private void RefreshTile(int tileIndex, BadgeProgress progress)
        {
            BadgeTile tile = _tiles[tileIndex];

            // A catalog shorter than the grid leaves surplus widgets hidden rather than drawn empty.
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
            float alpha = isUnlocked ? 1f : LOCKED_TILE_ALPHA;

            // Unlocked inverts onto the accent plate, the same way LevelPathPanelView marks the node
            // the player is on and PowerUpInventoryView marks the armed slot.
            Color plateColour = isUnlocked
                ? _currentTheme.Accent
                : Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, 0.16f);

            tile.PlateImage.color = WithAlpha(plateColour, alpha);
            tile.ShadowImage.color = WithAlpha(_currentTheme.CardShadow, alpha);
            tile.DoneDot.color = isUnlocked ? _currentTheme.CardBackground : Color.clear;

            // The disc is always the card's own colour so the glyph has a calm ground on both plates;
            // the glyph takes the accent once earned and the ink while it is still a goal.
            BadgeConfig config = _badgeCatalog.Find(progress.Definition.Id);
            Sprite icon = config != null ? config.Icon : null;
            tile.IconDisc.color = WithAlpha(
                isUnlocked
                    ? _currentTheme.CardBackground
                    : Color.Lerp(_currentTheme.CardBackground, _currentTheme.Ink, LOCKED_DISC_TINT),
                alpha);
            tile.IconGlyph.sprite = icon;
            tile.IconGlyph.color = icon == null
                ? Color.clear
                : WithAlpha(isUnlocked ? _currentTheme.Accent : _currentTheme.Ink, alpha);

            tile.TitleText.color = WithAlpha(
                isUnlocked ? _currentTheme.CardBackground : _currentTheme.Ink, alpha);
            tile.TitleText.text = config != null && !string.IsNullOrEmpty(config.DisplayName)
                ? config.DisplayName
                : progress.Definition.Id;

            tile.ProgressText.color = WithAlpha(
                isUnlocked ? _currentTheme.CardBackground : _currentTheme.SoftInk, alpha);
            tile.ProgressText.text = FormatCounter(progress.CurrentValue, progress.Definition.Threshold);
        }

        private string FormatCounter(long value, long total)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(value);
            _stringBuilder.Append(COUNTER_SEPARATOR);
            _stringBuilder.Append(total);
            return _stringBuilder.ToString();
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

            var panelObject = new GameObject("BadgesPanel", typeof(RectTransform), typeof(Image));
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
                panelRect, "BadgesCard", _cardSize, out _cardImage, out _cardShadowImage);

            float cardHalfHeight = _cardSize.y * 0.5f;
            float cardHalfWidth = _cardSize.x * 0.5f;
            float headerY = cardHalfHeight - HEADER_INSET;

            _headerText = CreateLabel(
                _cardRect, "Header", _headerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(-cardHalfWidth + SIDE_INSET, headerY));

            BuildCloseButton(
                _cardRect, new Vector2(cardHalfWidth - SIDE_INSET - (ICON_BUTTON_SIZE * 0.5f), headerY));

            BuildGrid(cardHalfHeight);

            _panel = panelObject;
        }

        private void BuildGrid(float cardHalfHeight)
        {
            float gridHeight = ((GRID_ROW_COUNT - 1) * TILE_SPACING_Y) + TILE_HEIGHT;
            float gridCentreY = cardHalfHeight - GRID_TOP_INSET - (gridHeight * 0.5f);

            for (int tileIndex = 0; tileIndex < TILE_COUNT; tileIndex++)
            {
                int column = tileIndex % GRID_COLUMN_COUNT;
                int row = tileIndex / GRID_COLUMN_COUNT;

                float x = (column - ((GRID_COLUMN_COUNT - 1) * 0.5f)) * TILE_SPACING_X;
                float y = gridCentreY + ((((GRID_ROW_COUNT - 1) * 0.5f) - row) * TILE_SPACING_Y);

                _tiles[tileIndex] = BuildTile(tileIndex, new Vector2(x, y));
            }
        }

        private BadgeTile BuildTile(int tileIndex, Vector2 anchoredPosition)
        {
            var tileSize = new Vector2(TILE_WIDTH, TILE_HEIGHT);

            var tileObject = new GameObject($"BadgeTile_{tileIndex}", typeof(RectTransform));
            var tileRect = (RectTransform)tileObject.transform;
            tileRect.SetParent(_cardRect, false);
            Centre(tileRect, tileSize);
            tileRect.anchoredPosition = anchoredPosition;

            var shadowObject = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            var shadowRect = (RectTransform)shadowObject.transform;
            shadowRect.SetParent(tileRect, false);
            Centre(shadowRect, tileSize + new Vector2(10f, 10f));
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            var shadowImage = shadowObject.GetComponent<Image>();
            ConfigureRounded(shadowImage);

            var plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var plateRect = (RectTransform)plateObject.transform;
            plateRect.SetParent(tileRect, false);
            Centre(plateRect, tileSize);
            var plateImage = plateObject.GetComponent<Image>();
            ConfigureRounded(plateImage);

            float discX = (-TILE_WIDTH * 0.5f) + TILE_TEXT_INSET + (TILE_ICON_DISC_SIZE * 0.5f);

            var discObject = new GameObject("IconDisc", typeof(RectTransform), typeof(Image));
            var discRect = (RectTransform)discObject.transform;
            discRect.SetParent(tileRect, false);
            Centre(discRect, new Vector2(TILE_ICON_DISC_SIZE, TILE_ICON_DISC_SIZE));
            discRect.anchoredPosition = new Vector2(discX, 0f);
            var discImage = discObject.GetComponent<Image>();
            ConfigureCircle(discImage);

            var glyphObject = new GameObject("IconGlyph", typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(discRect, false);
            Centre(glyphRect, new Vector2(TILE_ICON_GLYPH_SIZE, TILE_ICON_GLYPH_SIZE));
            var glyphImage = glyphObject.GetComponent<Image>();
            glyphImage.type = Image.Type.Simple;
            glyphImage.preserveAspect = true;
            glyphImage.color = Color.clear;
            glyphImage.raycastTarget = false;

            float textX = discX + (TILE_ICON_DISC_SIZE * 0.5f) + TILE_ICON_TEXT_GAP;

            Text titleText = CreateLabel(
                tileRect, "Title", _titleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(textX, 26f));

            Text progressText = CreateLabel(
                tileRect, "Progress", _progressFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(textX, -26f));

            // Same filled dot the objective HUD and the level path use for "done", tucked into the
            // top-right corner so even the longest name has the full text row to itself.
            var dotObject = new GameObject("DoneDot", typeof(RectTransform), typeof(Image));
            var dotRect = (RectTransform)dotObject.transform;
            dotRect.SetParent(tileRect, false);
            Centre(dotRect, new Vector2(TILE_DOT_SIZE, TILE_DOT_SIZE));
            dotRect.anchoredPosition = new Vector2((TILE_WIDTH * 0.5f) - 24f, (TILE_HEIGHT * 0.5f) - 24f);
            var dotImage = dotObject.GetComponent<Image>();
            ConfigureCircle(dotImage);

            return new BadgeTile(
                tileRect, plateImage, shadowImage, discImage, glyphImage, dotImage, titleText, progressText);
        }

        /// <summary>Two bars crossed at right angles — the close glyph, as on the other two cards.</summary>
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

        /// <summary>Builds a wordless label. Every caller fills it in from <see cref="Refresh"/>,
        /// because every label on this card carries a value rather than a plain lookup.</summary>
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

        // Raycasts stay off everywhere: taps arrive through BoardInputView's pointer action, not
        // through an EventSystem, and this scene has none.
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
