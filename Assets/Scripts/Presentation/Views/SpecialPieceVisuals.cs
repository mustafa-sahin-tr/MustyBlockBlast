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
        /// Paints one cell of a dock plate. <paramref name="kind"/> decides the fill and whether a glyph
        /// is drawn on top; <paramref name="colourId"/> is the piece's ordinary colour, used for every
        /// kind that does not override it.
        /// <para>
        /// Always clears the glyph when the kind does not want one, so a cell reused for an ordinary
        /// piece can never keep a previous special piece's mark.
        /// </para>
        /// </summary>
        internal static void Apply(CellView cell, SpecialPieceKind kind, ThemeDefinition theme, int colourId)
            => Apply(cell, kind, theme, colourId, null);

        /// <summary>
        /// As the four-argument overload, but also painting the dock plate's decorative cell-skin
        /// overlay (issue #324) — <paramref name="skinOverlay"/> is the already-resolved sprite (or
        /// null for none), resolved by the caller via <c>CellSkinIconCatalog.Find</c> so this stays a
        /// pure paint function with no catalog dependency of its own.
        /// </summary>
        internal static void Apply(
            CellView cell, SpecialPieceKind kind, ThemeDefinition theme, int colourId, Sprite skinOverlay)
        {
            if (cell == null)
            {
                return;
            }

            if (kind == SpecialPieceKind.Golden)
            {
                cell.SetEmbossedColours(GoldFill, GoldHighlight, GoldShade);
            }
            else if (theme != null)
            {
                cell.SetEmbossedColours(
                    theme.GetFill(colourId), theme.GetHighlight(colourId), theme.GetShade(colourId));
            }

            cell.SetSkinOverlay(skinOverlay);

            Sprite glyph = GlyphFor(kind);
            if (glyph == null)
            {
                cell.ClearSpecialIcon();
                return;
            }

            cell.SetSpecialIcon(IconTint, glyph);
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
