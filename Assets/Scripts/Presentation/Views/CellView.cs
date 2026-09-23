using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// One rounded board/tray square, able to render in two looks that are both built once and
    /// toggled (never rebuilt), because cells are reused across redraws, previews and fades:
    /// <list type="bullet">
    /// <item>Flat — two stacked Images (an outer ring silhouette, an evenly inset inner face). Used
    /// for empty board cells and for the flat preview tint: the mockup's <c>.empty</c> cell, a fill
    /// inside a one-pixel outline.</item>
    /// <item>Block — the storefront <c>.blk</c> (issue #265): a rounded base in the kind's shade, the
    /// kind's fill sat on it and lifted off the bottom edge so the shade shows as a bevel under it,
    /// and a soft glossy ellipse across the top in the kind's highlight. The same volume language the
    /// shop tiles are drawn in.</item>
    /// </list>
    /// On top of both sit two independent ring layers — the would-clear outline
    /// (<see cref="SetHighlight"/>) and the Ghost Fit ring (<see cref="SetGhostRing"/>) — each toggled
    /// on its own, so a suggestion and a clear preview can share a cell without one taking the other
    /// down. Pure visual — it is told a colour, it never decides one.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class CellView : MonoBehaviour
    {
        /// <summary>Thickness of the would-clear outline, in reference pixels: the mockup's 3px.</summary>
        private const float HIGHLIGHT_THICKNESS = 8f;

        /// <summary>Thickness of the Ghost Fit ring: the mockup's 2px.</summary>
        private const float GHOST_RING_THICKNESS = 6f;

        /// <summary>The gloss ellipse's box, as fractions of the cell: the mockup's
        /// <c>left 12%, top 8%, width 76%, height 32%</c>.</summary>
        private const float GLOSS_LEFT = 0.12f;
        private const float GLOSS_RIGHT = 0.88f;
        private const float GLOSS_TOP = 0.92f;
        private const float GLOSS_BOTTOM = 0.55f;

        /// <summary>Alpha the gloss ellipse is drawn at: the mockup's white at 0.5 fading to nothing,
        /// which the soft glow sprite supplies as a falloff. Stored so <see cref="SetAlpha"/> can fade
        /// it in proportion rather than snapping it to full strength.</summary>
        private const float GLOSS_ALPHA = 0.7f;

        /// <summary>How many bevel thicknesses the special-cell icon is inset by, on top of the cell's
        /// own inset. Half keeps the icon just clear of the bottom bevel on every side, so it sits on
        /// the block's flat face at any cell size, while still reading clearly as the mark it is — the
        /// old 2x made the distinct per-kind glyphs (see BoardView's icon sprite fields) too small to
        /// tell apart at a glance.</summary>
        private const float SPECIAL_ICON_BEVEL_INSET_MULTIPLIER = 0.5f;

        /// <summary>How far the special-cell glow halo (issue #365) bleeds past the cell's own bounds,
        /// in reference pixels, so it reads as a soft radiating backing like the approved mockup rather
        /// than a shape confined to the icon's own inset. Kept under half the board's cell spacing (8px,
        /// see BoardView's <c>_cellSpacing</c>) so the halo's already-soft falloff never visibly reaches
        /// the neighbouring cell on a full 8x8 board.</summary>
        private const float SPECIAL_GLOW_OUTSET = 6f;

        /// <summary>How much larger than the icon itself the fake rim-light copy behind it is drawn, so
        /// a sliver of it shows past the tinted icon's edge as a bright outline (the mockup's rim-light)
        /// without a custom shader.</summary>
        private const float SPECIAL_ICON_RIM_SCALE = 1.16f;

        /// <summary>Alpha the rim-light copy is drawn at — bright but not fully opaque, so it reads as a
        /// highlight rather than a second solid icon.</summary>
        private const float SPECIAL_ICON_RIM_ALPHA = 0.85f;

        /// <summary>Colour of the icon rim-light: near-white for every kind, since the per-kind hue
        /// identity (issue #365 AC5) is carried by the tinted icon and glow on top of it, not by the
        /// outline.</summary>
        private static readonly Color SpecialIconRimColour = new Color(1f, 1f, 1f, SPECIAL_ICON_RIM_ALPHA);

        /// <summary>How far <see cref="SetIconShine"/> blends the icon towards white at the brightest
        /// point of the pulse (issue #421) — subtle enough that the icon's own hue (and the vivid‑tint
        /// retune in <c>BoardView.IconTint</c>) still reads as that kind's identity, not a flash of
        /// white.</summary>
        private const float ICON_SHINE_MAX = 0.22f;

        /// <summary>Reference-pixel font size for the <see cref="MustyBlockBlast.Core.SpecialCellKind.Timer"/>
        /// countdown number (issue #307 AC6a) — legible at a glance without competing with the cell's
        /// own special-icon glyph.</summary>
        private const int TIMER_COUNTDOWN_FONT_SIZE = 34;

        /// <summary>Reference-pixel font size for the level-completion empty-cell bonus number (issue
        /// #424) — the same size as the timer countdown, so the two number layers read as one family.</summary>
        private const int BONUS_NUMBER_FONT_SIZE = 34;

        /// <summary>Fallback ink for the bonus number before <see cref="SetBonusNumberStyle"/> has ever
        /// been called — dark enough to stay legible on the theme's light grey empty-cell fill
        /// (<see cref="MustyBlockBlast.Gameplay.Settings.ThemeDefinition.EmptyCellFill"/>) even if a
        /// theme paint is somehow missed. In practice <see cref="BoardView"/> always styles this to the
        /// score card's own ink the moment the theme is known (issue #424), so the count reads as the
        /// same digits the score counter is about to gain, not as an unrelated on-board number.</summary>
        private static readonly Color BonusNumberColour = new Color(0.12f, 0.1f, 0.16f);

        /// <summary>The flat frost tint an ice socket's overlay is drawn in (issue #433): a pale ice
        /// blue, deliberately colour-independent — it tints whatever the cell shows underneath (an
        /// empty face or any block colour) the same way — and deliberately NOT
        /// <c>BoardView.ReinforcedDamageTint</c>'s slate grey, so a socket never reads as a damaged
        /// block. The alpha is supplied per call by <see cref="SetIceOverlay"/>.</summary>
        private static readonly Color IceOverlayTint = new Color(0.80f, 0.92f, 0.96f, 1f);

        /// <summary>Overlay alpha at the highest ice level: thick enough to read as ice at a glance,
        /// thin enough that the block colour (and the empty-cell face) underneath still shows through.
        /// Lower levels scale linearly down from it, so the fade is a simple uniform ramp — not the
        /// organic melts-from-a-corner pattern the reinforced-cell damage tint uses.</summary>
        private const float ICE_OVERLAY_MAX_ALPHA = 0.72f;

        private Image _outerImage;
        private Image _flatFaceImage;
        private GameObject _blockRoot;
        private Image _blockShadeImage;
        private Image _blockFaceImage;
        private Image _blockGlossImage;
        private Image _specialGlowImage;
        private Image _iceOverlayImage;
        private Image _specialIconRimImage;
        private Image _specialIconImage;
        private Image _highlightImage;
        private Image _ghostRingImage;
        private Text _timerCountdownText;
        private Text _bonusNumberText;

        /// <summary>The resting alpha the glow halo was last shown at (issue #365) — the target
        /// <see cref="SetGlowPulse"/> multiplies against, since the halo's own colour alpha is
        /// overwritten every frame by the pulse rather than by <see cref="SetSpecialGlow"/>.</summary>
        private float _glowBaseAlpha;

        /// <summary>The icon's own resting tint, last set by <see cref="SetSpecialIcon(Color, Sprite)"/> —
        /// what <see cref="SetIconShine"/> blends towards white from every frame, since the icon's colour
        /// is overwritten each pulse tick the same way the glow halo's alpha is (issue #421).</summary>
        private Color _iconBaseColour;

        private void Awake() => CacheOuter();

        /// <summary>Creates both layer sets. Called by the builder right after AddComponent.
        /// <paramref name="cornerRadius"/> is in reference pixels; every rounded layer shares it.</summary>
        internal void Build(Sprite roundedSprite, float inset, float bevelThickness, float cornerRadius)
        {
            CacheOuter();
            HudChrome.ConfigureRounded(_outerImage, cornerRadius);

            _flatFaceImage = CreateStretchedImage(transform, "Face");
            HudChrome.ConfigureRounded(_flatFaceImage, Mathf.Max(1f, cornerRadius - inset));
            SetStretchInsets((RectTransform)_flatFaceImage.transform, inset, inset, inset, inset);

            var blockObject = new GameObject("Block", typeof(RectTransform));
            _blockRoot = blockObject;
            var blockRect = (RectTransform)blockObject.transform;
            blockRect.SetParent(transform, false);
            StretchToParent(blockRect);

            _blockShadeImage = CreateStretchedImage(blockRect, "Shade");
            HudChrome.ConfigureRounded(_blockShadeImage, cornerRadius);

            // Lifted off the bottom edge by the bevel, so the shade underneath reads as the block's
            // shaded base — the mockup's "inset 0 -4px 0" — and nowhere else.
            _blockFaceImage = CreateStretchedImage(blockRect, "Face");
            HudChrome.ConfigureRounded(_blockFaceImage, cornerRadius);
            SetStretchInsets((RectTransform)_blockFaceImage.transform, 0f, bevelThickness, 0f, 0f);

            // The soft glow stretched into a wide ellipse across the top of the face: its falloff is
            // what makes the highlight read as a sheen rather than a sticker.
            _blockGlossImage = CreateStretchedImage(blockRect, "Gloss");
            _blockGlossImage.sprite = UiSpriteFactory.RadialGlow;
            _blockGlossImage.type = Image.Type.Simple;
            _blockGlossImage.raycastTarget = false;
            var glossRect = (RectTransform)_blockGlossImage.transform;
            glossRect.anchorMin = new Vector2(GLOSS_LEFT, GLOSS_BOTTOM);
            glossRect.anchorMax = new Vector2(GLOSS_RIGHT, GLOSS_TOP);
            glossRect.offsetMin = Vector2.zero;
            glossRect.offsetMax = Vector2.zero;

            _blockRoot.SetActive(false);

            // The ice-socket overlay (issue #433): one flat, rounded, semi-transparent plate over the
            // whole cell. Built right after both looks so it draws over whichever is showing (an ice
            // socket is empty most of the time and occupied the rest), and before the glow/icon/rings
            // so a special block that lands on a socket still shows its mark on top of the frost.
            // Parented to the cell rather than the block root because the ice belongs to the position
            // and must stay put when the block's look is toggled off. Same rounded outline as the cell
            // itself, so it reads as the cell frosted over rather than a sticker on it.
            _iceOverlayImage = CreateStretchedImage(transform, "IceOverlay");
            HudChrome.ConfigureRounded(_iceOverlayImage, cornerRadius);
            _iceOverlayImage.raycastTarget = false;
            _iceOverlayImage.color = Color.clear;
            _iceOverlayImage.gameObject.SetActive(false);

            float iconInset = inset + (bevelThickness * SPECIAL_ICON_BEVEL_INSET_MULTIPLIER);

            // The special-cell glow halo (issue #365): a soft, bright backing behind the icon so a
            // special cell reads as special even on a theme fill it happens to be close in luminance
            // to. Built before the icon (and before the rim below) so it is their junior sibling and
            // therefore draws behind both, while still drawing over the block/flat face layers built
            // above it. Sized past the cell's own bounds (SPECIAL_GLOW_OUTSET) so it bleeds outward
            // like the mockup's starburst rather than sitting flush inside the bevel.
            _specialGlowImage = CreateStretchedImage(transform, "SpecialGlow");
            _specialGlowImage.sprite = UiSpriteFactory.RadialGlow;
            _specialGlowImage.type = Image.Type.Simple;
            _specialGlowImage.raycastTarget = false;
            _specialGlowImage.color = Color.clear;
            SetStretchInsets(
                (RectTransform)_specialGlowImage.transform,
                -SPECIAL_GLOW_OUTSET, -SPECIAL_GLOW_OUTSET, -SPECIAL_GLOW_OUTSET, -SPECIAL_GLOW_OUTSET);
            _specialGlowImage.gameObject.SetActive(false);

            // The icon's rim-light (issue #365): a near-white, slightly larger copy of the same sprite,
            // drawn directly behind the tinted icon so a sliver of it shows past the tinted icon's own
            // edge as a bright outline — the mockup's rim-light, without a custom shader. Its sprite and
            // scale are kept in step with the icon's own by SetSpecialIcon below.
            _specialIconRimImage = CreateStretchedImage(transform, "SpecialIconRim");
            _specialIconRimImage.sprite = UiSpriteFactory.Starburst;
            _specialIconRimImage.type = Image.Type.Simple;
            _specialIconRimImage.raycastTarget = false;
            _specialIconRimImage.color = Color.clear;
            SetStretchInsets(
                (RectTransform)_specialIconRimImage.transform, iconInset, iconInset, iconInset, iconInset);
            _specialIconRimImage.gameObject.SetActive(false);

            // Built after both looks so it draws on top of whichever is active. In practice only an
            // occupied (block) cell ever wears one — a special kind belongs to the block standing on
            // the cell — but it is parented to the cell rather than to the block root so toggling
            // looks can never take the icon down with it. Inset well inside the bevel so it reads as a
            // mark on the block's face rather than as a second silhouette.
            _specialIconImage = CreateStretchedImage(transform, "SpecialIcon");
            _specialIconImage.sprite = UiSpriteFactory.Starburst;
            _specialIconImage.type = Image.Type.Simple;
            _specialIconImage.raycastTarget = false;
            _specialIconImage.color = Color.clear;
            SetStretchInsets((RectTransform)_specialIconImage.transform, iconInset, iconInset, iconInset, iconInset);
            _specialIconImage.gameObject.SetActive(false);

            // The two rings are built last so they are the cell's last siblings and therefore draw
            // over whichever look is active. Both use the hollow outline sprite family, so the wall is
            // a constant thickness whatever the corner radius, and they cost no extra draw call
            // between them.
            _ghostRingImage = CreateStretchedImage(transform, "GhostRing");
            HudChrome.ConfigureOutline(_ghostRingImage, cornerRadius, GHOST_RING_THICKNESS);
            _ghostRingImage.gameObject.SetActive(false);

            _highlightImage = CreateStretchedImage(transform, "Highlight");
            HudChrome.ConfigureOutline(_highlightImage, cornerRadius, HIGHLIGHT_THICKNESS);
            _highlightImage.gameObject.SetActive(false);

            // Built last of all, and parented to the cell rather than the special-icon image, so it
            // draws over both looks and over the icon alike — a timer cell's own countdown must always
            // read clearly, whatever else is drawn on that cell (issue #307 AC6a). Deliberately no
            // background chip: "a number on the cell is sufficient" (AC6, out-of-scope: rings/pulsing).
            var countdownRect = (RectTransform)transform;
            _timerCountdownText = UiTextFactory.Create(
                countdownRect, "TimerCountdown", TIMER_COUNTDOWN_FONT_SIZE, FontStyle.Bold, Color.white);
            _timerCountdownText.gameObject.SetActive(false);

            // Issue #424: the level-completion empty-cell count, written over the cell. Its own layer
            // rather than a reuse of the timer countdown so the two can never fight over one Text.
            _bonusNumberText = UiTextFactory.Create(
                countdownRect, "BonusNumber", BONUS_NUMBER_FONT_SIZE, FontStyle.Bold, BonusNumberColour);
            _bonusNumberText.gameObject.SetActive(false);
        }

        /// <summary>Shows the outline frame in <paramref name="colour"/>. Independent of both looks:
        /// it neither reads nor writes the face/fill/shade layers, so a cell can be tinted, embossed
        /// or empty underneath an outline.</summary>
        internal void SetHighlight(Color colour) => ShowLayer(_highlightImage, colour);

        /// <summary>Hides the outline frame. Safe to call on a cell that never had one.</summary>
        internal void ClearHighlight() => HideLayer(_highlightImage);

        /// <summary>Shows the Ghost Fit ring in <paramref name="colour"/>. Its own layer, separate from
        /// the would-clear outline, so a drag that would clear the suggested line keeps both.</summary>
        internal void SetGhostRing(Color colour) => ShowLayer(_ghostRingImage, colour);

        /// <summary>Hides the Ghost Fit ring. Safe to call on a cell that never had one.</summary>
        internal void ClearGhostRing() => HideLayer(_ghostRingImage);

        /// <summary>Shows the special-cell icon in <paramref name="colour"/>. Independent of both looks
        /// and of the outline, exactly as <see cref="SetHighlight"/> is: it neither reads nor writes any
        /// other layer, and allocates nothing, so it is safe on any repaint path.</summary>
        internal void SetSpecialIcon(Color colour) => SetSpecialIcon(colour, UiSpriteFactory.Starburst);

        /// <summary>
        /// As <see cref="SetSpecialIcon(Color)"/>, but drawing <paramref name="sprite"/> instead of the
        /// default starburst — the dock plates mark a
        /// <see cref="MustyBlockBlast.Core.SpecialPieceKind"/> with its own glyph rather than with a
        /// tint, because a piece that is tapped rather than dragged has to be told apart by shape.
        /// Allocates nothing, so it is safe on any repaint path.
        /// <para>
        /// Always snaps the icon's own scale and rotation back to identity (issue #330 AC2): every
        /// ordinary repaint path (a theme switch, a redraw, a cancelled fade) must show the icon at its
        /// resting size and orientation, and the one path that wants anything else —
        /// <c>BoardView.OnSpecialCellSpawned</c>'s spin-and-shrink pop-in — pulls the scale and rotation
        /// back down itself, synchronously, immediately after this call returns and before the frame this
        /// drew ever renders.
        /// </para>
        /// </summary>
        internal void SetSpecialIcon(Color colour, Sprite sprite)
        {
            if (_specialIconImage == null)
            {
                return;
            }

            if (sprite != null)
            {
                _specialIconImage.sprite = sprite;
            }

            _iconBaseColour = colour;
            _specialIconImage.transform.localScale = Vector3.one;
            _specialIconImage.transform.localRotation = Quaternion.identity;
            ShowLayer(_specialIconImage, colour);

            // The rim-light copy (issue #365) always mirrors the icon's own sprite and resets its own
            // scale on every call for the same reason the icon's does: an ordinary repaint must show it
            // at rest, whatever the sprite or scale a previous call — or a spawn-in pop — left it at.
            if (_specialIconRimImage != null)
            {
                if (sprite != null)
                {
                    _specialIconRimImage.sprite = sprite;
                }

                _specialIconRimImage.transform.localScale = Vector3.one * SPECIAL_ICON_RIM_SCALE;
                ShowLayer(_specialIconRimImage, SpecialIconRimColour);
            }
        }

        /// <summary>
        /// Shows the ice-socket overlay (issue #433) at an opacity proportional to
        /// <paramref name="iceLevel"/> over <paramref name="maxIceLevel"/> — a linear ramp from
        /// <see cref="ICE_OVERLAY_MAX_ALPHA"/> at the top level down towards clear — or hides it when
        /// <paramref name="iceLevel"/> is 0, at which point the cell is indistinguishable from one that
        /// was never icy. Independent of every other layer: it neither reads nor writes the face, block
        /// or icon layers, and deliberately is NOT touched by <see cref="SetAlpha"/>, because the ice
        /// belongs to the position and must stay on screen while the block above it fades out.
        /// Allocates nothing, so it is safe on any repaint path.
        /// </summary>
        internal void SetIceOverlay(int iceLevel, int maxIceLevel)
        {
            if (_iceOverlayImage == null)
            {
                return;
            }

            if (iceLevel <= 0)
            {
                HideLayer(_iceOverlayImage);
                return;
            }

            float fraction = Mathf.Clamp01(iceLevel / (float)Mathf.Max(1, maxIceLevel));
            ShowLayer(
                _iceOverlayImage,
                new Color(IceOverlayTint.r, IceOverlayTint.g, IceOverlayTint.b, ICE_OVERLAY_MAX_ALPHA * fraction));
        }

        /// <summary>Hides the special-cell icon and its rim-light. Safe to call on a cell that never had
        /// one.</summary>
        internal void ClearSpecialIcon()
        {
            HideLayer(_specialIconImage);
            HideLayer(_specialIconRimImage);
        }

        /// <summary>Shows the special-cell glow halo (issue #365) behind the icon in
        /// <paramref name="colour"/> — its alpha is the halo's resting brightness, which
        /// <see cref="SetGlowPulse"/> then modulates every frame. Independent of every other layer and
        /// allocates nothing, so it is safe on any repaint path.</summary>
        internal void SetSpecialGlow(Color colour)
        {
            if (_specialGlowImage == null)
            {
                return;
            }

            _glowBaseAlpha = colour.a;
            ShowLayer(_specialGlowImage, colour);
        }

        /// <summary>Hides the glow halo. Safe to call on a cell that never had one — including every
        /// <see cref="MustyBlockBlast.Core.SpecialCellKind.None"/> cell, which never calls
        /// <see cref="SetSpecialGlow"/> in the first place.</summary>
        internal void ClearSpecialGlow()
        {
            HideLayer(_specialGlowImage);
            _glowBaseAlpha = 0f;
        }

        /// <summary>Scales the glow halo's current alpha by <paramref name="alphaMultiplier"/> against
        /// its resting brightness from the last <see cref="SetSpecialGlow"/> call — the one per-frame
        /// touch point <c>BoardView.Update</c> needs for the halo's gentle pulse (issue #365 AC4).
        /// Touches only the halo's own alpha, never its colour or any other layer, and allocates
        /// nothing, so a board's worth of active glows can be driven from one field every frame. A no-op
        /// on a cell whose glow is not currently showing, so a caller need not guard the call itself.</summary>
        internal void SetGlowPulse(float alphaMultiplier)
        {
            if (_specialGlowImage == null || !_specialGlowImage.gameObject.activeSelf)
            {
                return;
            }

            Color colour = _specialGlowImage.color;
            colour.a = _glowBaseAlpha * alphaMultiplier;
            _specialGlowImage.color = colour;
        }

        /// <summary>
        /// Drives the special-cell icon's shine (issue #421) on the same wave <see cref="SetGlowPulse"/>
        /// rides — <paramref name="wave"/> is 0 at the pulse's dimmest instant and 1 at its brightest, so
        /// the icon breathes in lockstep with its own glow halo rather than on a second, independent
        /// timer. Blends the icon's resting tint (<see cref="_iconBaseColour"/>) towards white by up to
        /// <see cref="ICON_SHINE_MAX"/> — a highlight, not a colour change, so the vivid‑tint retune
        /// (<c>BoardView.IconTint</c>) still reads as that kind's identity at every point in the cycle.
        /// Writes one <see cref="Color"/> struct; allocates nothing.
        /// </summary>
        internal void SetIconShine(float wave)
        {
            if (_specialIconImage == null || !_specialIconImage.gameObject.activeSelf)
            {
                return;
            }

            _specialIconImage.color = Color.Lerp(_iconBaseColour, Color.white, wave * ICON_SHINE_MAX);
        }

        /// <summary>The special-cell icon's own transform, exposed only for
        /// <c>BoardView.OnSpecialCellSpawned</c>'s spawn-in pop animation (issue #330 AC2) to animate its
        /// <c>localScale</c> without touching the cell's own rect (which the fade/stagger effects in
        /// <c>BoardView.PlayClearAsync</c> already own). Null before <see cref="Build"/> runs.</summary>
        internal RectTransform SpecialIconTransform
            => _specialIconImage != null ? (RectTransform)_specialIconImage.transform : null;

        /// <summary>The block layer's own transform, exposed only for
        /// <c>BoardView.OnVortexIslandFilled</c>'s fill-in pop animation (issue #349 AC4) to animate its
        /// <c>localScale</c> without touching the cell's own rect (which the fade/stagger effects in
        /// <c>BoardView.PlayClearAsync</c> already own) — the same reasoning
        /// <see cref="SpecialIconTransform"/> is exposed for, one layer down. Null before
        /// <see cref="Build"/> runs.</summary>
        internal RectTransform BlockTransform
            => _blockRoot != null ? (RectTransform)_blockRoot.transform : null;

        /// <summary>Shows <paramref name="countdown"/> as the cell's placements-remaining number (issue
        /// #307 AC6a). Independent of every other layer, exactly as <see cref="SetSpecialIcon(Color)"/>
        /// is: allocates nothing beyond the string conversion, so it is safe on any repaint path.</summary>
        internal void SetTimerCountdown(int countdown)
        {
            if (_timerCountdownText == null)
            {
                return;
            }

            _timerCountdownText.text = countdown.ToString();

            if (!_timerCountdownText.gameObject.activeSelf)
            {
                _timerCountdownText.gameObject.SetActive(true);
            }
        }

        /// <summary>Hides the countdown number — a cell converts to ordinary or is cleared and stops
        /// counting down (issue #307 AC6a). Safe to call on a cell that never showed one.</summary>
        internal void ClearTimerCountdown()
        {
            if (_timerCountdownText == null || !_timerCountdownText.gameObject.activeSelf)
            {
                return;
            }

            _timerCountdownText.gameObject.SetActive(false);
        }

        /// <summary>Shows <paramref name="number"/> as this empty cell's place in the level-completion
        /// bonus count (issue #424). Independent of every other layer, exactly as
        /// <see cref="SetTimerCountdown"/> is: allocates nothing beyond the string conversion.</summary>
        internal void SetBonusNumber(int number)
        {
            if (_bonusNumberText == null)
            {
                return;
            }

            _bonusNumberText.text = number.ToString();

            if (!_bonusNumberText.gameObject.activeSelf)
            {
                _bonusNumberText.gameObject.SetActive(true);
            }
        }

        /// <summary>Hides the bonus count number once the count-up has been shown (issue #424). Safe to
        /// call on a cell that never showed one.</summary>
        internal void ClearBonusNumber()
        {
            if (_bonusNumberText == null || !_bonusNumberText.gameObject.activeSelf)
            {
                return;
            }

            _bonusNumberText.gameObject.SetActive(false);
        }

        /// <summary>Repaints the bonus number to match the score card's own look (issue #424) — the same
        /// chunky display face <see cref="ScoreView"/> draws its figures in, and the same
        /// theme-accent-darkened ink <see cref="ScoreView"/> uses for the best/record value, so the count
        /// on the board reads as the same digits the score counter is about to gain rather than as an
        /// unrelated number painted onto the cell. Called by <see cref="BoardView"/> whenever the theme
        /// is (re)painted; <paramref name="font"/> may be null (kept at whatever it already is) so a
        /// theme repaint before the font is known never blanks it back to the builtin face.</summary>
        internal void SetBonusNumberStyle(Font font, Color colour)
        {
            if (_bonusNumberText == null)
            {
                return;
            }

            if (font != null)
            {
                _bonusNumberText.font = font;
            }

            _bonusNumberText.color = colour;
        }

        /// <summary>Flat two-layer look: empty cells and the drag preview tint. <paramref name="shade"/>
        /// is the outline ring, <paramref name="face"/> the fill inside it.</summary>
        internal void SetColours(Color face, Color shade)
        {
            CacheOuter();
            _outerImage.enabled = true;
            _outerImage.color = shade;

            if (_flatFaceImage != null)
            {
                _flatFaceImage.gameObject.SetActive(true);
                _flatFaceImage.color = face;
            }

            if (_blockRoot != null)
            {
                _blockRoot.SetActive(false);
            }
        }

        /// <summary>Block look: filled piece cells on the board, tray, pocket and ghost. The three
        /// tones are the theme's fill, highlight and shade for the kind.</summary>
        internal void SetEmbossedColours(Color fill, Color highlight, Color shade)
        {
            CacheOuter();
            _outerImage.enabled = false;

            if (_flatFaceImage != null)
            {
                _flatFaceImage.gameObject.SetActive(false);
            }

            if (_blockRoot == null)
            {
                return;
            }

            _blockRoot.SetActive(true);
            _blockShadeImage.color = shade;
            _blockFaceImage.color = fill;
            _blockGlossImage.color = new Color(highlight.r, highlight.g, highlight.b, highlight.a * GLOSS_ALPHA);
        }

        /// <summary>Applies one alpha to every layer of both looks, so whichever is showing fades
        /// uniformly and the other cannot come back at a stale opacity.</summary>
        internal void SetAlpha(float alpha)
        {
            CacheOuter();
            ApplyAlpha(_outerImage, alpha);
            ApplyAlpha(_flatFaceImage, alpha);
            ApplyAlpha(_blockShadeImage, alpha);
            ApplyAlpha(_blockFaceImage, alpha);

            // The gloss is translucent by design, so it fades in proportion to its resting alpha
            // rather than being snapped to the cell's.
            ApplyAlpha(_blockGlossImage, alpha * GLOSS_ALPHA);

            // The rings fade with the rest so a highlighted cell cannot stay solid mid-fade. Their own
            // alpha is restored in full by the next SetHighlight / SetGhostRing call.
            ApplyAlpha(_highlightImage, alpha);
            ApplyAlpha(_ghostRingImage, alpha);

            // The icon likewise: a destroyed special cell fades out as one block, never as a fading
            // block with a solid mark left floating over it. Restored by the next SetSpecialIcon call.
            ApplyAlpha(_specialIconImage, alpha);

            // And its rim-light and glow halo (issue #365), each fading in proportion to its own resting
            // alpha exactly as the gloss ellipse does above, so neither is left at full brightness — or
            // floating over a fading cell — mid-fade. Restored by the next SetSpecialIcon /
            // SetSpecialGlow call.
            ApplyAlpha(_specialIconRimImage, alpha * SPECIAL_ICON_RIM_ALPHA);
            ApplyAlpha(_specialGlowImage, alpha * _glowBaseAlpha);

            // And the countdown number, for the same reason: a timer cell that is fading out (cleared
            // in time) must not leave its number floating over an emptying cell.
            if (_timerCountdownText != null)
            {
                Color colour = _timerCountdownText.color;
                colour.a = alpha;
                _timerCountdownText.color = colour;
            }
        }

        private static void ShowLayer(Image image, Color colour)
        {
            if (image == null)
            {
                return;
            }

            image.color = colour;

            if (!image.gameObject.activeSelf)
            {
                image.gameObject.SetActive(true);
            }
        }

        private static void HideLayer(Image image)
        {
            if (image == null || !image.gameObject.activeSelf)
            {
                return;
            }

            image.gameObject.SetActive(false);
        }

        private static void ApplyAlpha(Image image, float alpha)
        {
            if (image == null)
            {
                return;
            }

            Color colour = image.color;
            colour.a = alpha;
            image.color = colour;
        }

        private static Image CreateStretchedImage(Transform parent, string objectName)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)imageObject.transform;
            rect.SetParent(parent, false);
            StretchToParent(rect);
            return imageObject.GetComponent<Image>();
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetStretchInsets(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void CacheOuter()
        {
            if (_outerImage == null)
            {
                _outerImage = GetComponent<Image>();
            }
        }
    }
}
