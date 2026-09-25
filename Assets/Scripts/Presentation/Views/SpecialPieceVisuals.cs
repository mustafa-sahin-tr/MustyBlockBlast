using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// How a dock plate is painted for the <see cref="SpecialPieceKind"/> its piece carries. One place
    /// rather than two, because the same piece can sit in the tray or in the pocket and must look the
    /// same in both — <see cref="PieceTrayView"/> and <see cref="HoldSlotView"/> both paint through
    /// here.
    /// <para>
    /// Pure presentation: it maps a kind to colours and a glyph and decides nothing about the game.
    /// Golden is shown as a fill, the other two as a mark on an otherwise ordinary plate, which follows
    /// what the kinds mean — a golden 1x1 <em>is</em> a different block, a rocket or a hammer is an
    /// ordinary-looking plate that does something extra when it is used.
    /// </para>
    /// </summary>
    internal static class SpecialPieceVisuals
    {
        /// <summary>
        /// Gold emboss triple for <see cref="SpecialPieceKind.Golden"/>. Fixed rather than themed, for
        /// the reason <c>BoardView</c>'s icon tint is: "this one is golden" is a readability mark, and a
        /// theme that had to author its own gold could author one its own block fills swallow. The three
        /// tones follow the same lit/face/shaded relationship every theme's block colours do, so the
        /// plate still reads as the same embossed block as its neighbours.
        /// </summary>
        private static readonly Color GoldFill = new Color(1f, 0.78f, 0.20f, 1f);
        private static readonly Color GoldHighlight = new Color(1f, 0.92f, 0.56f, 1f);
        private static readonly Color GoldShade = new Color(0.76f, 0.50f, 0.06f, 1f);

        /// <summary>Colour a dock glyph is drawn in. Deliberately the warm near-white
        /// <c>BoardView</c> draws a special <em>cell</em>'s icon in: the two marks mean related things
        /// and should not read as two unrelated systems.</summary>
        private static readonly Color IconTint = new Color(1f, 0.95f, 0.72f, 1f);

        /// <summary>
        /// Each kind's identity hue — the colour its info-card hero icon is drawn in, and the colour an
        /// info demo (issue #451) paints its effects in. Golden is <see cref="GoldFill"/> itself, so "this
        /// one is golden" reads as one hue everywhere it appears; the rocket is fire, not gold; the hammer
        /// a cool steel, deliberately the furthest from gold of the three (issue #283: Golden and
        /// Demolition Hammer must never again share one undistinguished placeholder).
        /// </summary>
        private static readonly Color PiercingRocketIdentity = new Color(1f, 0.47f, 0.24f, 1f);
        private static readonly Color DemolitionHammerIdentity = new Color(0.72f, 0.76f, 0.84f, 1f);

        /// <summary>
        /// Paints one cell of a dock plate. <paramref name="kind"/> decides the fill and whether a glyph
        /// is drawn on top; <paramref name="colourId"/> is the piece's ordinary colour, used for every
        /// kind that does not override it.
        /// <para>
        /// Always clears the glyph when the kind does not want one, so a cell reused for an ordinary
        /// piece can never keep a previous special piece's mark.
        /// </para>
        /// </summary>
        internal static void Apply(CellView cell, SpecialPieceKind kind, ThemeDefinition theme, int colourId)
        {
            if (cell == null)
            {
                return;
            }

            if (TryGetFill(kind, out Color fill, out Color highlight, out Color shade))
            {
                cell.SetEmbossedColours(fill, highlight, shade);
            }
            else if (theme != null)
            {
                cell.SetEmbossedColours(
                    theme.GetFill(colourId), theme.GetHighlight(colourId), theme.GetShade(colourId));
            }

            ApplyGlyph(cell, kind);
        }

        /// <summary>
        /// The emboss triple <paramref name="kind"/> overrides a plate's ordinary colour with — only
        /// <see cref="SpecialPieceKind.Golden"/> has one. False for every other kind, whose plate keeps
        /// the piece's own theme colour. Shared with the info demos (issue #451), which paint a golden
        /// piece exactly as the dock does.
        /// </summary>
        internal static bool TryGetFill(SpecialPieceKind kind, out Color fill, out Color highlight, out Color shade)
        {
            if (kind == SpecialPieceKind.Golden)
            {
                fill = GoldFill;
                highlight = GoldHighlight;
                shade = GoldShade;
                return true;
            }

            fill = Color.clear;
            highlight = Color.clear;
            shade = Color.clear;
            return false;
        }

        /// <summary>
        /// Draws <paramref name="kind"/>'s dock glyph on <paramref name="cell"/>, or clears any glyph when
        /// the kind wears none — so a cell reused for an ordinary piece never keeps a previous special
        /// piece's mark. Allocates nothing. Shared with the info demos (issue #451), whose tray pieces
        /// wear the dock's own marks.
        /// </summary>
        internal static void ApplyGlyph(CellView cell, SpecialPieceKind kind)
        {
            if (cell == null)
            {
                return;
            }

            Sprite glyph = GlyphFor(kind);
            if (glyph == null)
            {
                cell.ClearSpecialIcon();
                return;
            }

            // A dock glyph is a mark on the plate, never full-bleed — reset it in case this cell last
            // wore a diamond (issue #480).
            cell.SetSpecialIconFullBleed(false);
            cell.SetSpecialIcon(IconTint, glyph);
        }

        /// <summary>
        /// <paramref name="kind"/>'s identity hue (see <see cref="PiercingRocketIdentity"/>): the info
        /// card's hero-icon tint, and the colour an info demo draws the kind's effects in. White for
        /// <see cref="SpecialPieceKind.None"/>.
        /// </summary>
        internal static Color IdentityColour(SpecialPieceKind kind)
        {
            switch (kind)
            {
                case SpecialPieceKind.Golden:
                    return GoldFill;
                case SpecialPieceKind.PiercingRocket:
                    return PiercingRocketIdentity;
                case SpecialPieceKind.DemolitionHammer:
                    return DemolitionHammerIdentity;
                default:
                    return Color.white;
            }
        }

        /// <summary>The mark <paramref name="kind"/> wears, or null for a kind that wears none —
        /// an ordinary piece, and Golden, which is told apart by its fill instead.</summary>
        private static Sprite GlyphFor(SpecialPieceKind kind)
        {
            if (kind == SpecialPieceKind.PiercingRocket)
            {
                return UiSpriteFactory.RocketIcon;
            }

            if (kind == SpecialPieceKind.DemolitionHammer)
            {
                return UiSpriteFactory.HammerIcon;
            }

            return null;
        }
    }
}
