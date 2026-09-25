using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.PieceIdCount"/> objective card's demo (issue #454, mockup artboard "Belirli
    /// parça"), authored from the real rule (<see cref="ObjectiveProgress"/>): every placement of the exact
    /// catalog piece <see cref="ObjectiveDefinition.RequiredPieceId"/> counts one — no clear needed, and no
    /// other piece of its family counts. The demo is built for the objective's own piece, its shape read from
    /// the real <see cref="PieceCatalog"/>: two copies of it land while a different piece stays in the tray.
    /// <para>
    /// The chip shows the piece in miniature and the objective's own target, counting from <c>target - 2</c>
    /// (from 0 when that is under 2) to the target. Timing: <see cref="InfoDemoCountedPlacements"/>.
    /// </para>
    /// <para>
    /// Board (rows top to bottom, '.' empty, letters are block colours); the first copy lands with its
    /// footprint's top-left on (0,0), the second on (0,4) — or on (2,0) for a piece four or more wide, which
    /// beside the first would complete row 0:
    /// <code>
    /// row0-3 ........
    /// row4 ......g.
    /// row5 .g....g.
    /// row6 gg...bb.
    /// row7 ggu..bbu
    /// </code>
    /// The tray holds the piece (magenta), a different piece (blue: a single, or a 1x2 when the piece is the
    /// single) and a second copy (magenta). Every catalog id is supported, with any target from 1 to 999; an
    /// id that is not in the catalog gets no demo and the card keeps its glyph.
    /// </para>
    /// </summary>
    internal static class PieceIdCountInfoDemo
    {
        internal const float LOOP_DURATION = InfoDemoCountedPlacements.LOOP_DURATION;

        internal const int PLACEMENT_COUNT = InfoDemoCountedPlacements.PLACEMENT_COUNT;

        /// <summary>A piece at least this wide lands its second copy under the first rather than beside it.</summary>
        internal const int WIDE_PIECE_COLUMNS = 4;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "........",
            "......g.",
            ".g....g.",
            "gg...bb.",
            "ggu..bbu",
        };

        private const string OTHER_ID = "single_1x1";
        private const string OTHER_ID_FOR_SINGLE = "line_h2";

        private const float LABEL_ROW = 4.2f;

        /// <summary>True when the demo can show <paramref name="pieceId"/> with a chip counting to
        /// <paramref name="target"/>.</summary>
        internal static bool Supports(string pieceId, int target)
            => PieceIdLineClearInfoDemo.Supports(pieceId) && InfoDemoCountedPlacements.SupportsTarget(target);

        /// <summary>The catalog id of the piece that stays in the tray for <paramref name="pieceId"/>.</summary>
        internal static string OtherId(string pieceId) => pieceId == OTHER_ID ? OTHER_ID_FOR_SINGLE : OTHER_ID;

        /// <summary>Where each copy of a piece of <paramref name="shape"/> lands (footprint top-left, x = column,
        /// y = row), in play order.</summary>
        internal static Vector2Int[] LandCells(Vector2Int[] shape)
        {
            InfoDemoLayout.ShapeBounds(shape, out Vector2Int min, out Vector2Int max);
            bool isWide = max.x - min.x + 1 >= WIDE_PIECE_COLUMNS;
            return new[] { new Vector2Int(0, 0), isWide ? new Vector2Int(0, 2) : new Vector2Int(4, 0) };
        }

        internal static InfoDemoTimeline Build(string pieceId, int target) => Build(pieceId, target, out _);

        /// <summary>As <see cref="Build(string, int)"/>, handing back the chip so a test can read it. Null when
        /// <paramref name="pieceId"/> names no catalog piece.</summary>
        internal static InfoDemoTimeline Build(string pieceId, int target, out InfoDemoProgressChip chip)
        {
            Piece piece = PieceIdLineClearInfoDemo.FindPiece(pieceId);
            if (piece == null)
            {
                chip = null;
                return null;
            }

            Vector2Int[] shape = PieceIdLineClearInfoDemo.DemoShape(piece);
            Vector2Int[] otherShape = PieceIdLineClearInfoDemo.DemoShape(PieceIdLineClearInfoDemo.FindPiece(OtherId(pieceId)));

            return InfoDemoCountedPlacements.Build(
                Rows, shape, new[] { shape, shape }, LandCells(shape), otherShape,
                LocalizationKeys.INFO_POPUP_DEMO_THIS_PIECE, LABEL_ROW, target, out chip);
        }
    }
}
