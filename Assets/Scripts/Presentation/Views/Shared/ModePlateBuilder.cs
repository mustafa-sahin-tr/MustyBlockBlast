using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views.Shared
{
    /// <summary>
    /// Builds and repaints the mode plates — one per entry in <see cref="SelectableModes"/>, each a
    /// kind-coloured tile with the mode's glyph, its name over a one-line description and the PLAYING
    /// tag the active mode wears — and, on the Timed plate, the two rows of duration chips under its
    /// header (issues #260, #270, #355). Extracted from <see cref="SettingsPanelView"/> so the
    /// mode-select screen (issue #379) draws the very same plates from the very same code rather than a
    /// copy of it.
    /// <para>
    /// Not a MonoBehaviour and not a View: it owns no input and decides nothing. A host View creates
    /// one, asks it to build plates into a rect, hit-tests the returned <see cref="ModeOptions"/> and
    /// <see cref="DurationOptions"/> itself, and forwards its theme and locale changes to
    /// <see cref="Repaint"/> and <see cref="Relocalize"/>. Selection state is painted on request through
    /// <see cref="RefreshModeSelection"/> and <see cref="RefreshDurationSelection"/>, with the host
    /// deciding which plate rings and in what colour — the settings card rings the pending mode while a
    /// switch awaits confirmation, the mode-select screen simply rings the mode being played.
    /// </para>
    /// <para>
    /// Every Image and Text built here lands in one of the builder's own repaint buckets, so the host
    /// never has to know which parts are ink, which are plate and which are a kind's fill: one
    /// <see cref="Repaint"/> call recolours all of it.
    /// </para>
    /// </summary>
    internal sealed class ModePlateBuilder
    {
        // Layout, in canvas reference pixels, on the 880-wide card family every picker shares. Kept in
        // lock-step with SettingsPanelView's row constants so the mode plates sit in its well exactly as
        // its other rows do.
        internal const float ROW_HEIGHT = 148f;
        internal const float ROW_GAP = 22f;
        private const float ROW_PADDING_X = 28f;

        /// <summary>The icon tile: a rounded square in the row's kind fill over a slightly taller one
        /// in its shade, so the shade shows as a lip along the bottom.</summary>
        internal const float TILE_SIZE = 96f;
        private const float TILE_LIP = 7f;
        private const float TILE_CORNER_RADIUS = 28f;

        /// <summary>Left edge of a row's text column: after the padding, the tile and a gap.</summary>
        private const float ROW_TEXT_INSET = ROW_PADDING_X + TILE_SIZE + 28f;

        /// <summary>Rendered corner radius of a plate, in reference pixels. The shared rounded sprite
        /// bakes its radius at <see cref="UiSpriteFactory.ROUNDED_RADIUS"/>, so the slice multiplier is
        /// derived from the two.</summary>
        private const float PLATE_CORNER_RADIUS = 16f;

        /// <summary>A plate's drop shadow: a copy of the plate, this far lower, in Ink at low alpha.</summary>
        private const float PLATE_SHADOW_DROP = 4f;

        /// <summary>The selection ring around a chosen plate: an accent ring this far outside the
        /// plate, with a card-coloured gap between the two so the ring reads as a ring.</summary>
        private const float RING_OUTSET = 14f;
        private const float RING_GAP_OUTSET = 7f;

        private const float MODE_NAME_RISE = 26f;
        private const float MODE_DESCRIPTION_DROP = 24f;

        /// <summary>Horizontal room reserved at a mode plate's right edge for the PLAYING tag, so a
        /// description that wraps to a second line (issue #355 follow-up — some locales' copy, e.g.
        /// Spanish's "Infinito" description, does not fit one line) never runs under it.</summary>
        private const float MODE_DESCRIPTION_PLAYING_RESERVE = 150f;

        /// <summary>How many timed (non-"Sınırsız") lengths share the bottom chip row — 3/5/10 dk
        /// today. The endless entry gets its own full-width row above this one instead of a fourth
        /// column, so "3 dk"/"5 dk"/"10 dk" can stay one line each in every locale (issue #355
        /// follow-up) rather than needing the number and unit split onto separate lines.</summary>
        private const int DURATION_COLUMN_COUNT = 3;
        private const float OPTION_GAP = 24f;

        /// <summary>
        /// The Timed plate carries two duration chip rows under its header — the "Sınırsız" entry
        /// full-width on top, the timed lengths in a grid below it (issue #355 follow-up) — so it is
        /// taller than the other mode plates (issue #270). The header fills the top
        /// <see cref="ROW_HEIGHT"/> of the plate exactly as every other mode plate does, and the two
        /// chip rows fill the rest, with one <see cref="ROW_GAP"/> between each pair.
        /// </summary>
        private const float TIMED_CHIP_ROW_HEIGHT = 110f;
        internal const float TIMED_PLATE_HEIGHT =
            ROW_HEIGHT + (ROW_GAP * 2f) + (TIMED_CHIP_ROW_HEIGHT * 2f);

        // The mode-select screen's stand-alone layout (issue #379 redesign): no card, no well. The
        // Klasik box is the Timed plate's header and chip rows in their own tinted box with a padded
        // bottom edge, and Şölen/Macera are two upright buttons side by side — tile on top, name under
        // it, the PLAYING tag along the bottom.
        internal const float DURATION_BOX_HEIGHT = TIMED_PLATE_HEIGHT + ROW_GAP;
        internal const float STANDALONE_BUTTON_HEIGHT = 236f;
        private const float STANDALONE_TILE_RISE = 44f;
        private const float STANDALONE_NAME_DROP = 40f;
        private const float STANDALONE_PLAYING_DROP = 84f;

        private const int LABEL_FONT_SIZE = 24;
        private const int MODE_NAME_FONT_SIZE = 40;
        private const int DESCRIPTION_FONT_SIZE = 24;
        private const int OPTION_FONT_SIZE = 44;

        /// <summary>A plate's shadow: Ink at this alpha, as the profile card's plates have.</summary>
        private const float PLATE_SHADOW_ALPHA = 0.12f;

        // Which theme kind's bevel triplet each mode's tile takes. Chosen by role, so a season swap
        // recolours them together, and shared with the settings card's own rows: the mode row wears
        // MODE_KIND, the language row LANGUAGE_KIND.
        private const int MODE_KIND = 5;
        private const int LANGUAGE_KIND = 4;
        private const int DURATION_KIND = 1;

        /// <summary>
        /// Every mode the picker offers, in display order. The single source of "which modes exist to
        /// choose from": adding one here (with its tile kind, glyph and description key) is all any
        /// picker needs.
        /// </summary>
        internal static readonly GameMode[] SelectableModes =
        {
            GameMode.Timed,
            GameMode.Endless,
            GameMode.Path,
        };

        private readonly LocalizationSystem _localizationSystem;
        private readonly LocalizationModel _localizationModel;
        private readonly TimedModeSystem _timedModeSystem;
        private readonly Font _displayFont;

        private readonly List<ModeOption> _modeOptions = new List<ModeOption>(SelectableModes.Length);
        private readonly List<DurationOption> _durationOptions = new List<DurationOption>(6);
        private readonly StringBuilder _stringBuilder = new StringBuilder(8);

        // Repaint buckets: every Image and Text built here belongs to exactly one of them, so a theme
        // switch is a handful of tight loops instead of a hierarchy walk.
        private readonly List<Image> _platePlates = new List<Image>(12);
        private readonly List<Image> _plateShadows = new List<Image>(12);

        /// <summary>Plates and chips painted in a mode's own kind fill rather than the neutral card
        /// colour — the mode-select screen's stand-alone buttons and Klasik's duration chips, each in
        /// their mode's colour so Klasik/Şölen/Macera read as three differently-coloured buttons.</summary>
        private readonly List<KindImage> _kindPlates = new List<KindImage>(6);

        /// <summary>The Klasik box's own background: a mode's kind shade (the darker half of its fill/
        /// shade pair) rather than its fill, so the fill-coloured chips sitting on it read as raised
        /// buttons instead of blending into a same-coloured box.</summary>
        private readonly List<KindImage> _kindShadePlates = new List<KindImage>(1);

        private readonly List<KindImage> _kindFills = new List<KindImage>(16);
        private readonly List<KindImage> _kindShades = new List<KindImage>(8);
        private readonly List<Text> _inkTexts = new List<Text>(12);
        private readonly List<Text> _softInkTexts = new List<Text>(4);

        /// <summary>Every label whose wording is a plain String Table lookup, paired with its key, so a
        /// language switch is one tight loop. The duration chips are not in here — their wording has a
        /// number substituted in, so <see cref="Relocalize"/> re-renders them separately.</summary>
        private readonly List<LocalizedLabel> _localizedLabels = new List<LocalizedLabel>(12);

        /// <param name="displayFont">The chunky display face for names and chips. Null falls back to
        /// the built-in runtime font.</param>
        internal ModePlateBuilder(
            LocalizationSystem localizationSystem,
            LocalizationModel localizationModel,
            TimedModeSystem timedModeSystem,
            Font displayFont)
        {
            _localizationSystem = localizationSystem;
            _localizationModel = localizationModel;
            _timedModeSystem = timedModeSystem;
            _displayFont = displayFont;
        }

        /// <summary>The mode plates built so far, in build order. Hit-tested by the host.</summary>
        internal IReadOnlyList<ModeOption> ModeOptions => _modeOptions;

        /// <summary>The duration chips built so far, in config order. Hit-tested by the host — before
        /// the plates, since the chips sit inside the Timed one.</summary>
        internal IReadOnlyList<DurationOption> DurationOptions => _durationOptions;

        /// <summary>A built label together with the String Table key it renders.</summary>
        private readonly struct LocalizedLabel
        {
            internal LocalizedLabel(Text label, string key, bool uppercase)
            {
                Label = label;
                Key = key;
                Uppercase = uppercase;
            }

            internal Text Label { get; }

            internal string Key { get; }

            internal bool Uppercase { get; }
        }

        /// <summary>An Image painted in one theme kind's fill or shade.</summary>
        private readonly struct KindImage
        {
            internal KindImage(Image image, int kind)
            {
                Image = image;
                Kind = kind;
            }

            internal Image Image { get; }

            internal int Kind { get; }
        }

        // ---------------------------------------------------------------------------- mode facts

        internal static string ModeNameKey(GameMode mode)
        {
            return mode switch
            {
                GameMode.Timed => LocalizationKeys.MODE_TIMED,
                GameMode.Path => LocalizationKeys.MODE_PATH,
                _ => LocalizationKeys.MODE_ENDLESS,
            };
        }

        private static string ModeDescriptionKey(GameMode mode)
        {
            return mode switch
            {
                GameMode.Timed => LocalizationKeys.MODE_TIMED_DESCRIPTION,
                GameMode.Path => LocalizationKeys.MODE_PATH_DESCRIPTION,
                _ => LocalizationKeys.MODE_ENDLESS_DESCRIPTION,
            };
        }

        /// <summary>Which kind's bevel triplet a mode's tile takes, on the settings row and in the pickers.
        /// Internal so <see cref="LoadingCurtainView"/> can build the very same tile for the chosen mode.</summary>
        internal static int ModeKind(GameMode mode)
        {
            return mode switch
            {
                GameMode.Timed => DURATION_KIND,
                GameMode.Path => LANGUAGE_KIND,
                _ => MODE_KIND,
            };
        }

        /// <summary>How tall a mode's plate is: the Timed plate carries its chips, so it is taller.</summary>
        internal static float PlateHeight(GameMode mode) => mode == GameMode.Timed ? TIMED_PLATE_HEIGHT : ROW_HEIGHT;

        // ---------------------------------------------------------------------------- building

        /// <summary>
        /// Builds one mode plate into <paramref name="root"/>, centred at <paramref name="anchoredPosition"/>
        /// and <paramref name="contentWidth"/> wide: the mode's tile and glyph, its name over a one-line
        /// description, and the PLAYING tag on the right that only the active mode shows. The Timed
        /// plate also gets its duration chips (issue #270). Height is <see cref="PlateHeight"/>.
        /// </summary>
        internal ModeOption BuildModeOption(RectTransform root, GameMode mode, float contentWidth, Vector2 anchoredPosition)
        {
            float plateHeight = PlateHeight(mode);
            RectTransform plateRect = BuildSelectablePlate(
                root, $"ModeOption_{mode}", new Vector2(contentWidth, plateHeight), anchoredPosition, out PlateSelection selection);

            // The header content sits where it would in a plain ROW_HEIGHT plate; on the taller Timed
            // plate that means lifted to its top.
            float headerCentreY = (plateHeight * 0.5f) - (ROW_HEIGHT * 0.5f);

            int kind = ModeKind(mode);
            RectTransform tileRect = BuildKindTile(
                plateRect, kind, new Vector2((-contentWidth * 0.5f) + ROW_PADDING_X + (TILE_SIZE * 0.5f), headerCentreY));
            BuildModeGlyph(tileRect, mode, kind);

            float textX = (-contentWidth * 0.5f) + ROW_TEXT_INSET;
            Text nameText = CreateLabel(
                plateRect, "Name", MODE_NAME_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(textX, headerCentreY + MODE_NAME_RISE), _displayFont);
            _inkTexts.Add(nameText);
            RegisterLocalized(nameText, ModeNameKey(mode));

            BuildModeDescriptionLabel(plateRect, mode, contentWidth, textX, headerCentreY - MODE_DESCRIPTION_DROP);

            // Painted by RefreshModeSelection rather than the ink bucket: it is accent or nothing.
            Text playingText = CreateLabel(
                plateRect, "Playing", LABEL_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2((contentWidth * 0.5f) - ROW_PADDING_X, headerCentreY));
            RegisterLocalized(playingText, LocalizationKeys.SETTINGS_MODE_PLAYING, uppercase: true);

            if (mode == GameMode.Timed)
            {
                BuildDurationChips(plateRect, contentWidth, headerCentreY);
            }

            var option = new ModeOption(mode, plateRect, selection, playingText);
            _modeOptions.Add(option);
            return option;
        }

        /// <summary>
        /// Builds one stand-alone mode button for the mode-select screen (issue #379 redesign): a
        /// single rounded plate, <paramref name="size"/> big and centred at
        /// <paramref name="anchoredPosition"/>, with the mode's tile and glyph on top, its name under
        /// them and the PLAYING tag along the bottom. Same ring and tag treatment as
        /// <see cref="BuildModeOption"/>, and lands in <see cref="ModeOptions"/> the same way; no
        /// description, since the button is half the width of a plate.
        /// </summary>
        internal ModeOption BuildStandaloneModeButton(RectTransform root, GameMode mode, Vector2 size, Vector2 anchoredPosition)
        {
            int kind = ModeKind(mode);
            RectTransform plateRect = BuildSelectablePlate(
                root, $"ModeButton_{mode}", size, anchoredPosition, out PlateSelection selection, kind);

            RectTransform tileRect = BuildKindTile(plateRect, kind, new Vector2(0f, STANDALONE_TILE_RISE));
            BuildModeGlyph(tileRect, mode, kind);

            Text nameText = CreateLabel(
                plateRect, "Name", MODE_NAME_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0f, -STANDALONE_NAME_DROP), _displayFont);
            nameText.color = Color.white;
            RegisterLocalized(nameText, ModeNameKey(mode));

            Text playingText = CreateLabel(
                plateRect, "Playing", LABEL_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0f, -STANDALONE_PLAYING_DROP));
            RegisterLocalized(playingText, LocalizationKeys.SETTINGS_MODE_PLAYING, uppercase: true);

            var option = new ModeOption(mode, plateRect, selection, playingText);
            _modeOptions.Add(option);
            return option;
        }

        /// <summary>
        /// Builds the Klasik box for the mode-select screen (issue #379 redesign): a tinted rounded
        /// box, <paramref name="boxWidth"/> wide and <see cref="DURATION_BOX_HEIGHT"/> tall, centred at
        /// <paramref name="anchoredPosition"/>, with the Timed plate's header (tile, name, description,
        /// PLAYING tag) across its top and the duration chips — "Sınırsız" full width, then the timed
        /// lengths in a grid — under it, inset by the row padding on both sides. The box itself is the
        /// Timed <see cref="ModeOption"/>, ring and all, so a tap outside every chip still starts a
        /// Klasik run; the chips land in <see cref="DurationOptions"/> as usual.
        /// </summary>
        internal ModeOption BuildDurationBox(RectTransform root, float boxWidth, Vector2 anchoredPosition)
        {
            const GameMode mode = GameMode.Timed;
            var size = new Vector2(boxWidth, DURATION_BOX_HEIGHT);

            var boxObject = new GameObject($"DurationBox_{mode}", typeof(RectTransform));
            var boxRect = (RectTransform)boxObject.transform;
            boxRect.SetParent(root, false);
            Centre(boxRect, size);
            boxRect.anchoredPosition = anchoredPosition;

            int kind = ModeKind(mode);

            Image ring = BuildRounded(
                boxRect, "Ring", size + (Vector2.one * (RING_OUTSET * 2f)), Vector2.zero, PLATE_CORNER_RADIUS + RING_OUTSET);
            Image gap = BuildRounded(
                boxRect, "RingGap", size + (Vector2.one * (RING_GAP_OUTSET * 2f)), Vector2.zero, PLATE_CORNER_RADIUS + RING_GAP_OUTSET);
            _plateShadows.Add(BuildRounded(boxRect, "Shadow", size, new Vector2(0f, -PLATE_SHADOW_DROP), PLATE_CORNER_RADIUS));
            _kindShadePlates.Add(new KindImage(BuildRounded(boxRect, "Plate", size, Vector2.zero, PLATE_CORNER_RADIUS), kind));

            // The header sits in the box's top ROW_HEIGHT, exactly as on the Timed plate.
            float headerCentreY = (size.y * 0.5f) - (ROW_HEIGHT * 0.5f);

            RectTransform tileRect = BuildKindTile(
                boxRect, kind, new Vector2((-boxWidth * 0.5f) + ROW_PADDING_X + (TILE_SIZE * 0.5f), headerCentreY));
            BuildModeGlyph(tileRect, mode, kind);

            float textX = (-boxWidth * 0.5f) + ROW_TEXT_INSET;
            Text nameText = CreateLabel(
                boxRect, "Name", MODE_NAME_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(textX, headerCentreY + MODE_NAME_RISE), _displayFont);
            nameText.color = Color.white;
            RegisterLocalized(nameText, ModeNameKey(mode));

            BuildModeDescriptionLabel(boxRect, mode, boxWidth, textX, headerCentreY - MODE_DESCRIPTION_DROP, white: true);

            Text playingText = CreateLabel(
                boxRect, "Playing", LABEL_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2((boxWidth * 0.5f) - ROW_PADDING_X, headerCentreY));
            RegisterLocalized(playingText, LocalizationKeys.SETTINGS_MODE_PLAYING, uppercase: true);

            BuildDurationChips(boxRect, boxWidth - (ROW_PADDING_X * 2f), headerCentreY, kind);

            var option = new ModeOption(mode, boxRect, new PlateSelection(ring, gap), playingText);
            _modeOptions.Add(option);
            return option;
        }

        /// <summary>A kind-coloured tile: a rounded square in the kind's fill over a taller one in its
        /// shade, so the shade shows as a lip along the bottom. Returns the tile's rect for the glyph.
        /// Public to the host because the settings card's own rows wear the same tile.</summary>
        internal RectTransform BuildKindTile(RectTransform parent, int kind, Vector2 anchoredPosition)
        {
            var tileObject = new GameObject("Tile", typeof(RectTransform));
            var tileRect = (RectTransform)tileObject.transform;
            tileRect.SetParent(parent, false);
            Centre(tileRect, new Vector2(TILE_SIZE, TILE_SIZE));
            tileRect.anchoredPosition = anchoredPosition;

            _kindShades.Add(new KindImage(
                BuildRounded(tileRect, "Shade", new Vector2(TILE_SIZE, TILE_SIZE), Vector2.zero, TILE_CORNER_RADIUS), kind));
            _kindFills.Add(new KindImage(
                BuildRounded(tileRect, "Fill", new Vector2(TILE_SIZE, TILE_SIZE - TILE_LIP), new Vector2(0f, TILE_LIP * 0.5f), TILE_CORNER_RADIUS), kind));
            return tileRect;
        }

        /// <summary>
        /// The glyph a mode's tile wears, white on the kind's fill: two overlapping rings for endless (a
        /// loose nod to the infinity mark), a clock for timed, and two stops joined by a rise for path.
        /// The tile's fill kind is needed for the fake cut-outs that turn discs into rings. Public to
        /// the host because the settings card's mode row wears the active mode's glyph too.
        /// </summary>
        internal GameObject BuildModeGlyph(RectTransform tileRect, GameMode mode, int tileKind)
        {
            var glyphObject = new GameObject($"Glyph_{mode}", typeof(RectTransform));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(tileRect, false);
            Centre(glyphRect, new Vector2(TILE_SIZE, TILE_SIZE));

            switch (mode)
            {
                case GameMode.Timed:
                    BuildClockGlyph(glyphRect, tileKind);
                    break;
                case GameMode.Path:
                    BuildTrailGlyph(glyphRect);
                    break;
                default:
                    BuildInfinityGlyph(glyphRect, tileKind);
                    break;
            }

            return glyphObject;
        }

        /// <summary>
        /// One mode plate's description line. Wrapping (rather than <see cref="CreateLabel"/>'s usual
        /// overflow) is deliberate — some locales' copy does not fit one line at
        /// <see cref="MODE_DESCRIPTION_PLAYING_RESERVE"/>'s width budget (issue #355 follow-up, e.g.
        /// Spanish's "Infinito" description), and letting it wrap to a second line beats letting it run
        /// under the PLAYING tag or off the plate.
        /// </summary>
        private void BuildModeDescriptionLabel(
            RectTransform plateRect, GameMode mode, float contentWidth, float textX, float y, bool white = false)
        {
            Text descriptionText = CreateLabel(
                plateRect, "Description", DESCRIPTION_FONT_SIZE, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(textX, y));
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Overflow;

            var rect = (RectTransform)descriptionText.transform;
            rect.sizeDelta = new Vector2(
                contentWidth - ROW_TEXT_INSET - ROW_PADDING_X - MODE_DESCRIPTION_PLAYING_RESERVE,
                DESCRIPTION_FONT_SIZE * 1.6f * 2f);

            if (white)
            {
                // On a coloured plate rather than the neutral card: a plain, slightly soft white reads
                // as a subtitle without needing a theme-driven repaint.
                descriptionText.color = new Color(1f, 1f, 1f, 0.85f);
            }
            else
            {
                _softInkTexts.Add(descriptionText);
            }

            RegisterLocalized(descriptionText, ModeDescriptionKey(mode));
        }

        /// <summary>
        /// The Timed plate's duration chips, in two rows (issue #355 follow-up): "Sınırsız" alone,
        /// full width, on top — it is a different kind of choice ("play forever") than a length, not a
        /// fourth one to squeeze in beside them — and the timed lengths in a
        /// <see cref="DURATION_COLUMN_COUNT"/>-column grid below it, seated under the Timed plate's
        /// header instead of under a sub-header (issue #270).
        /// </summary>
        private void BuildDurationChips(RectTransform plateRect, float contentWidth, float headerCentreY, int? chipKind = null)
        {
            IReadOnlyList<float> durations = _timedModeSystem.AvailableDurations;

            float endlessRowCentreY = headerCentreY - (ROW_HEIGHT * 0.5f) - ROW_GAP - (TIMED_CHIP_ROW_HEIGHT * 0.5f);
            float durationsRowCentreY = endlessRowCentreY - TIMED_CHIP_ROW_HEIGHT - ROW_GAP;

            float tileWidth = (contentWidth - (OPTION_GAP * (DURATION_COLUMN_COUNT - 1))) / DURATION_COLUMN_COUNT;
            var tileSize = new Vector2(tileWidth, TIMED_CHIP_ROW_HEIGHT);

            // Counts only the timed lengths seen so far, independent of each entry's index in the
            // config — so the endless entry, wherever the config lists it, never opens a gap in the
            // 3-column grid.
            int timedIndex = 0;

            for (int durationIndex = 0; durationIndex < durations.Count; durationIndex++)
            {
                float seconds = durations[durationIndex];

                if (TimedModeConfig.IsEndlessDuration(seconds))
                {
                    var endlessSize = new Vector2(contentWidth, TIMED_CHIP_ROW_HEIGHT);
                    _durationOptions.Add(BuildDurationOption(plateRect, seconds, endlessSize, new Vector2(0f, endlessRowCentreY), chipKind));
                    continue;
                }

                float x = (timedIndex - ((DURATION_COLUMN_COUNT - 1) * 0.5f)) * (tileWidth + OPTION_GAP);
                _durationOptions.Add(BuildDurationOption(plateRect, seconds, tileSize, new Vector2(x, durationsRowCentreY), chipKind));
                timedIndex++;
            }
        }

        /// <summary>One duration chip: built for the Timed mode plate's inline picker (issue #270), the
        /// same look the old stand-alone duration screen used. <paramref name="chipKind"/> colours the
        /// chip in a mode's kind fill instead of the neutral card colour, for the mode-select screen's
        /// Klasik box (issue #379 redesign) — <see langword="null"/> keeps the settings card's look.</summary>
        private DurationOption BuildDurationOption(
            RectTransform root, float seconds, Vector2 tileSize, Vector2 anchoredPosition, int? chipKind = null)
        {
            RectTransform plateRect = BuildSelectablePlate(
                root, $"DurationOption_{Mathf.RoundToInt(seconds)}", tileSize, anchoredPosition, out PlateSelection selection, chipKind);

            Text nameText = CreateLabel(
                plateRect, "Name", OPTION_FONT_SIZE, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, _displayFont);
            if (chipKind.HasValue)
            {
                nameText.color = Color.white;
            }
            else
            {
                _inkTexts.Add(nameText);
            }

            nameText.text = FormatDuration(seconds);

            return new DurationOption(seconds, plateRect, selection, nameText);
        }

        /// <summary>
        /// A plate that can be chosen: a rounded rect over a copy of itself dropped a few pixels in Ink
        /// at low alpha, with an accent ring and a card-coloured gap behind it, both clear until
        /// <see cref="PaintSelection"/> shows them. The returned rect is the plate's own; children
        /// position relative to it, and it is the tap target.
        /// <para>
        /// The plate itself is painted the neutral card colour, unless <paramref name="plateKind"/> is
        /// given — then it is painted that kind's fill instead, for the mode-select screen's
        /// stand-alone buttons and duration chips, each in their mode's own colour.
        /// </para>
        /// </summary>
        private RectTransform BuildSelectablePlate(
            RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition, out PlateSelection selection,
            int? plateKind = null)
        {
            var rootObject = new GameObject(objectName, typeof(RectTransform));
            var rootRect = (RectTransform)rootObject.transform;
            rootRect.SetParent(parent, false);
            Centre(rootRect, size);
            rootRect.anchoredPosition = anchoredPosition;

            Image ring = BuildRounded(
                rootRect, "Ring", size + (Vector2.one * (RING_OUTSET * 2f)), Vector2.zero, PLATE_CORNER_RADIUS + RING_OUTSET);
            Image gap = BuildRounded(
                rootRect, "RingGap", size + (Vector2.one * (RING_GAP_OUTSET * 2f)), Vector2.zero, PLATE_CORNER_RADIUS + RING_GAP_OUTSET);
            _plateShadows.Add(BuildRounded(rootRect, "Shadow", size, new Vector2(0f, -PLATE_SHADOW_DROP), PLATE_CORNER_RADIUS));

            Image plate = BuildRounded(rootRect, "Plate", size, Vector2.zero, PLATE_CORNER_RADIUS);
            if (plateKind.HasValue)
            {
                _kindPlates.Add(new KindImage(plate, plateKind.Value));
            }
            else
            {
                _platePlates.Add(plate);
            }

            selection = new PlateSelection(ring, gap);
            return rootRect;
        }

        // ---------------------------------------------------------------------------- glyphs

        /// <summary>Two overlapping rings — enough to read as "endless" at tile size.</summary>
        private void BuildInfinityGlyph(RectTransform parent, int tileKind)
        {
            const float RING_DIAMETER = 40f;
            const float RING_OVERLAP = 13f;
            const float RING_THICKNESS = 7f;

            for (int discIndex = 0; discIndex < 2; discIndex++)
            {
                BuildCircle(parent, $"InfinityDisc_{discIndex}", RING_DIAMETER, new Vector2(RingOffsetX(discIndex), 0f)).color = Color.white;
            }

            // Fake cut-out: the tile underneath is one opaque colour, so a smaller circle in that
            // colour turns each disc into a ring. Both holes are drawn after both discs so neither disc
            // fills the other's hole.
            for (int holeIndex = 0; holeIndex < 2; holeIndex++)
            {
                _kindFills.Add(new KindImage(
                    BuildCircle(parent, $"InfinityHole_{holeIndex}", RING_DIAMETER - (RING_THICKNESS * 2f), new Vector2(RingOffsetX(holeIndex), 0f)),
                    tileKind));
            }

            float RingOffsetX(int index)
                => (index == 0 ? -1f : 1f) * ((RING_DIAMETER * 0.5f) - (RING_OVERLAP * 0.5f));
        }

        /// <summary>Ring plus a single off-vertical hand — enough to read as a clock at tile size.</summary>
        private void BuildClockGlyph(RectTransform parent, int tileKind)
        {
            const float DIAL_DIAMETER = 54f;
            const float FACE_DIAMETER = 40f;
            const float HAND_LENGTH = 17f;
            const float HAND_ANGLE = -35f;

            BuildCircle(parent, "ClockDial", DIAL_DIAMETER, Vector2.zero).color = Color.white;
            _kindFills.Add(new KindImage(BuildCircle(parent, "ClockFace", FACE_DIAMETER, Vector2.zero), tileKind));

            Image hand = BuildRounded(parent, "ClockHand", new Vector2(6f, HAND_LENGTH), Vector2.zero, 3f);
            hand.rectTransform.localRotation = Quaternion.Euler(0f, 0f, HAND_ANGLE);

            // Pushed half its own length along its rotated axis so the base sits on the centre.
            hand.rectTransform.anchoredPosition =
                (Vector2)(Quaternion.Euler(0f, 0f, HAND_ANGLE) * new Vector3(0f, HAND_LENGTH * 0.5f, 0f));
            hand.color = Color.white;
        }

        /// <summary>Two stops joined by a rise and a fall — the trail, as the level path draws it.</summary>
        private static void BuildTrailGlyph(RectTransform parent)
        {
            const float STOP_DIAMETER = 18f;
            const float STOP_X = 26f;
            const float STOP_Y = -14f;
            const float LEG_LENGTH = 40f;
            const float LEG_THICKNESS = 7f;
            const float LEG_ANGLE = 52f;

            for (int legIndex = 0; legIndex < 2; legIndex++)
            {
                float sign = legIndex == 0 ? -1f : 1f;
                Image leg = BuildRounded(
                    parent, $"TrailLeg_{legIndex}", new Vector2(LEG_LENGTH, LEG_THICKNESS),
                    new Vector2(sign * 13f, 2f), LEG_THICKNESS * 0.5f);
                leg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -sign * LEG_ANGLE);
                leg.color = Color.white;
            }

            for (int stopIndex = 0; stopIndex < 2; stopIndex++)
            {
                float sign = stopIndex == 0 ? -1f : 1f;
                BuildCircle(parent, $"TrailStop_{stopIndex}", STOP_DIAMETER, new Vector2(sign * STOP_X, STOP_Y)).color = Color.white;
            }
        }

        // ---------------------------------------------------------------------------- repainting

        /// <summary>Recolours every plate, tile, glyph cut-out and label built so far from the theme.
        /// Selection rings are not touched here: the host repaints those through the two Refresh calls,
        /// since which plate rings is its decision.</summary>
        internal void Repaint(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            Color shadowColour = WithAlpha(theme.Ink, PLATE_SHADOW_ALPHA);

            for (int imageIndex = 0; imageIndex < _platePlates.Count; imageIndex++)
            {
                _platePlates[imageIndex].color = theme.CardBackground;
            }

            for (int imageIndex = 0; imageIndex < _plateShadows.Count; imageIndex++)
            {
                _plateShadows[imageIndex].color = shadowColour;
            }

            for (int imageIndex = 0; imageIndex < _kindPlates.Count; imageIndex++)
            {
                KindImage kindPlate = _kindPlates[imageIndex];
                kindPlate.Image.color = theme.GetFill(kindPlate.Kind);
            }

            for (int imageIndex = 0; imageIndex < _kindShadePlates.Count; imageIndex++)
            {
                KindImage kindPlate = _kindShadePlates[imageIndex];
                kindPlate.Image.color = theme.GetShade(kindPlate.Kind);
            }

            for (int imageIndex = 0; imageIndex < _kindFills.Count; imageIndex++)
            {
                KindImage kindImage = _kindFills[imageIndex];
                kindImage.Image.color = theme.GetFill(kindImage.Kind);
            }

            for (int imageIndex = 0; imageIndex < _kindShades.Count; imageIndex++)
            {
                KindImage kindImage = _kindShades[imageIndex];
                kindImage.Image.color = theme.GetShade(kindImage.Kind);
            }

            for (int textIndex = 0; textIndex < _inkTexts.Count; textIndex++)
            {
                _inkTexts[textIndex].color = theme.Ink;
            }

            for (int textIndex = 0; textIndex < _softInkTexts.Count; textIndex++)
            {
                _softInkTexts[textIndex].color = theme.SoftInk;
            }
        }

        /// <summary>Re-words every label built so far in the current language — the plain-key labels
        /// and the duration chips, whose wording is a number in the minutes format.</summary>
        internal void Relocalize()
        {
            for (int labelIndex = 0; labelIndex < _localizedLabels.Count; labelIndex++)
            {
                LocalizedLabel localizedLabel = _localizedLabels[labelIndex];
                string wording = _localizationSystem.Translate(localizedLabel.Key);
                localizedLabel.Label.text = localizedLabel.Uppercase ? Uppercase(wording) : wording;
            }

            for (int optionIndex = 0; optionIndex < _durationOptions.Count; optionIndex++)
            {
                DurationOption option = _durationOptions[optionIndex];
                option.NameText.text = FormatDuration(option.Seconds);
            }
        }

        /// <summary>
        /// Repaints the mode plates: <paramref name="playingMode"/> wears the PLAYING tag, and
        /// <paramref name="ringedMode"/> wears the ring in <paramref name="ringColour"/>. The two are
        /// usually the same mode; the settings card splits them while a switch awaits confirmation, so
        /// the eye goes from the newly-tapped plate to the RESTART button of the same colour and the
        /// player never sees two plates highlighted at once.
        /// </summary>
        internal void RefreshModeSelection(
            ThemeDefinition theme, GameMode playingMode, GameMode ringedMode, Color ringColour, Color? playingColour = null)
        {
            if (theme == null)
            {
                return;
            }

            Color resolvedPlayingColour = playingColour ?? theme.Accent;
            for (int optionIndex = 0; optionIndex < _modeOptions.Count; optionIndex++)
            {
                ModeOption option = _modeOptions[optionIndex];
                PaintSelection(option.Selection, option.Mode == ringedMode, ringColour, theme);
                option.PlayingText.color = option.Mode == playingMode ? resolvedPlayingColour : Color.clear;
            }
        }

        /// <summary>Repaints the duration chips' rings: the chip for <paramref name="selectedSeconds"/> rings in accent.</summary>
        internal void RefreshDurationSelection(ThemeDefinition theme, float selectedSeconds)
        {
            if (theme == null)
            {
                return;
            }

            for (int optionIndex = 0; optionIndex < _durationOptions.Count; optionIndex++)
            {
                DurationOption option = _durationOptions[optionIndex];
                PaintSelection(option.Selection, Mathf.Approximately(option.Seconds, selectedSeconds), theme.Accent, theme);
            }
        }

        /// <summary>Shows or clears a plate's ring. The mode and duration plates carry no check disc.</summary>
        private static void PaintSelection(PlateSelection selection, bool isSelected, Color ringColour, ThemeDefinition theme)
        {
            selection.Ring.color = isSelected ? ringColour : Color.clear;
            selection.Gap.color = isSelected ? theme.CardBackground : Color.clear;
        }

        // ---------------------------------------------------------------------------- wording

        /// <summary>
        /// Records <paramref name="label"/> as rendering <paramref name="key"/> and paints it once, so
        /// a label is correct from the moment it is built rather than only after the first switch.
        /// </summary>
        private void RegisterLocalized(Text label, string key, bool uppercase = false)
        {
            _localizedLabels.Add(new LocalizedLabel(label, key, uppercase));
            string wording = _localizationSystem.Translate(key);
            label.text = uppercase ? Uppercase(wording) : wording;
        }

        /// <summary>
        /// Renders a round length through the shared minutes format, the same one the best-score suffix
        /// and the game-over card use, so a length is spelled identically everywhere it is named. Round
        /// lengths are whole minutes, so the countdown's own mm:ss clock is not reused here.
        /// </summary>
        private string FormatDuration(float seconds)
        {
            // The "Sınırsız" (endless/no countdown) entry (issue #355) is a duration outside the
            // minutes format's domain — "0 dk" would misname it — so it gets its own key instead.
            if (TimedModeConfig.IsEndlessDuration(seconds))
            {
                return _localizationSystem.Translate(LocalizationKeys.DURATION_ENDLESS);
            }

            _stringBuilder.Clear();
            _stringBuilder.Append(Mathf.RoundToInt(seconds / 60f));
            return _localizationSystem.Format(LocalizationKeys.FORMAT_MINUTES, _stringBuilder.ToString());
        }

        /// <summary>
        /// Capitalises in the current language rather than invariantly: Turkish has a dotted capital İ,
        /// and the invariant rules would turn "Dil" into "DIL". Falls back to invariant for a locale
        /// code the runtime does not know.
        /// </summary>
        private string Uppercase(string wording)
        {
            if (string.IsNullOrEmpty(wording))
            {
                return wording;
            }

            LocaleDefinition locale = _localizationModel.CurrentLocale.Value;
            if (locale == null || string.IsNullOrEmpty(locale.Code))
            {
                return wording.ToUpperInvariant();
            }

            try
            {
                return wording.ToUpper(CultureInfo.GetCultureInfo(locale.Code));
            }
            catch (CultureNotFoundException)
            {
                return wording.ToUpperInvariant();
            }
        }

        // ---------------------------------------------------------------------------- primitives

        /// <summary>Builds a wordless label; callers word it. Pivoted on the aligned edge so the
        /// anchored position is that edge, whatever the string ends up measuring.</summary>
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
            float pivotX = alignment == TextAnchor.MiddleRight ? 1f : (alignment == TextAnchor.MiddleLeft ? 0f : 0.5f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.sizeDelta = new Vector2(0f, fontSize * 1.6f);
            rect.anchoredPosition = anchoredPosition;
            return text;
        }

        // Raycasts stay off everywhere: taps arrive through a pointer action, not through an
        // EventSystem. Every rounded Image shares the one rounded-square sprite, sliced to its own
        // radius, so the plates batch with the rest of the UI.
        private static Image BuildRounded(RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition, float radius)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var imageRect = (RectTransform)imageObject.transform;
            imageRect.SetParent(parent, false);
            Centre(imageRect, size);
            imageRect.anchoredPosition = anchoredPosition;

            var image = imageObject.GetComponent<Image>();
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UiSpriteFactory.ROUNDED_RADIUS / radius;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A non-interactive disc: aspect kept, no raycast, painted later. The circle sprite
        /// has no border, so it must never be sliced.</summary>
        private static Image BuildCircle(RectTransform parent, string objectName, float diameter, Vector2 anchoredPosition)
        {
            var glyphObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(parent, false);
            Centre(glyphRect, new Vector2(diameter, diameter));
            glyphRect.anchoredPosition = anchoredPosition;

            var image = glyphObject.GetComponent<Image>();
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        private static Color WithAlpha(Color colour, float alphaScale)
            => new Color(colour.r, colour.g, colour.b, colour.a * alphaScale);
    }
}
