using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// How a cell carrying a <see cref="SpecialCellKind.Diamond"/> gem is decorated (issue #395). One
    /// place rather than four, because the same decorated piece is drawn in the tray, in the pocket,
    /// in the air as the drag ghost and finally on the board, and it must look the same in all of
    /// them: the diamond glyph, tinted in the gem's own theme colour, sitting on top of the block's
    /// ordinary colour fill — never replacing it — with the same soft glow halo every other special
    /// cell wears.
    /// <para>
    /// Pure presentation: it is told a gem colour and paints it, and decides nothing about which
    /// pieces are decorated (that is <c>DiamondPieceDecorator</c>'s) or which cells land as diamonds
    /// (that is <c>BoardSystem</c>'s). Painted through <see cref="CellView.SetSpecialIcon(Color, Sprite)"/>,
    /// the very layer the board's Timer/Coin/Laser icons use, so a diamond's footprint on a cell is
    /// identical to every other special-cell badge at any cell size.
    /// </para>
    /// </summary>
    internal static class DiamondVisuals
    {
        /// <summary>
        /// Paints the diamond of colour <paramref name="diamondColourId"/> on <paramref name="cell"/>,
        /// over whatever fill the cell already shows. A <see cref="TrayModel.NO_DIAMOND"/> id paints
        /// nothing and touches nothing — the caller's ordinary look (and any special-piece glyph it
        /// drew) stands. Allocates nothing, so it is safe on any repaint path.
        /// </summary>
        internal static void Apply(CellView cell, int diamondColourId, ThemeDefinition theme, Sprite glyph)
        {
            if (cell == null || theme == null || diamondColourId == TrayModel.NO_DIAMOND)
            {
                return;
            }

            // The crystal is a whole block of its own (issue #480): drawn full-bleed, with no block colour
            // behind it, on the board and on every piece cell alike, so a gem looks the same in the air
            // as once it lands.
            Color tint = Tint(theme, diamondColourId);
            cell.SetSpecialIconFullBleed(true);
            cell.SetSpecialIcon(tint, glyph);
            cell.SetSpecialGlow(BoardView.GlowTintFrom(tint));
        }

        /// <summary>
        /// The colour a diamond of <paramref name="diamondColourId"/> is drawn in: the active theme's
        /// fill for that colour id — the same swatch the objective's description and GOAL chip use for
        /// it, so "collect the red ones" and the red gems on the tray are visibly the one colour. Read
        /// from the theme handed in, never cached, so a theme switch repaints the gem with the rest of
        /// the cell. The gem's white rim-light and glow (see <see cref="CellView.SetSpecialIcon(Color, Sprite)"/>)
        /// are what keep it legible when it happens to ride on a block of its own colour.
        /// </summary>
        internal static Color Tint(ThemeDefinition theme, int diamondColourId)
        {
            // A fruit (issue #484) is realistic art carrying its own colours: never tinted.
            if (Collectibles.IsFruit(diamondColourId))
            {
                return Color.white;
            }

            return theme != null ? theme.GetFill(diamondColourId) : Color.white;
        }
    }
}
