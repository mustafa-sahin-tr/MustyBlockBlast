using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.PieceFamilyCount"/> objective card's demo (issue #454, mockup artboard
    /// "Parça ailesi"), authored from the real rule (<see cref="ObjectiveProgress"/>,
    /// <see cref="PieceFamilyClassifier"/>): every placement of a piece whose family is
    /// <see cref="ObjectiveDefinition.RequiredPieceFamily"/> counts one, whatever its size or orientation — it
    /// needs no clear. The demo plays two <em>different</em> members of the objective's own family (a 4-cell L
    /// and a 3-cell corner for Corner, a 3x3 and a 2x2 for Square, ...), their shapes read from the real
    /// <see cref="PieceCatalog"/>, while a tray piece of another family stays put.
    /// <para>
    /// The chip shows the family's first member in miniature and the objective's own target: the two
    /// placements take it from <c>target - 2</c> to <c>target</c> (from 0 when that is under 2), e.g.
    /// "3/5 → 5/5". Timing: <see cref="InfoDemoCountedPlacements"/>.
    /// </para>
    /// <para>
    /// Board (rows top to bottom, '.' empty, letters are block colours); both members land in its empty top
    /// — the first with its footprint's top-left on (1,1), the second on (1,5) — where any member of any family
    /// (none is wider or taller than 3) fits without completing a line:
    /// <code>
    /// row0-2 ........
    /// row3 ....g...
    /// row4 ..uu....
    /// row5 .b...g..
    /// row6 g...bb..
    /// row7 gg...buu
    /// </code>
    /// The tray holds the first member (magenta), a piece of another family (blue) and the second member
    /// (magenta). Every family is supported, with any target from 1 to 999.
    /// </para>
    /// </summary>
    internal static class PieceFamilyCountInfoDemo
    {
        internal const float LOOP_DURATION = InfoDemoCountedPlacements.LOOP_DURATION;

        internal const int PLACEMENT_COUNT = InfoDemoCountedPlacements.PLACEMENT_COUNT;

        /// <summary>Each family member's footprint top-left (x = column, y = row), in play order.</summary>
        internal static readonly Vector2Int[] LandCells = { new Vector2Int(1, 1), new Vector2Int(5, 1) };

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "....g...",
            "..uu....",
            ".b...g..",
            "g...bb..",
            "gg...buu",
        };

        /// <summary>The two catalog members each family plays, indexed by <see cref="PieceFamily"/> value.</summary>
        private static readonly string[][] MemberIds =
        {
            new[] { "single_1x1", "single_1x1" },
            new[] { "line_h3", "line_v3" },
            new[] { "square_3x3", "square_2x2" },
            new[] { "j_right", "corner2_missing_tr" },
            new[] { "t_up", "t_left" },
            new[] { "s_horizontal", "s_vertical" },
            new[] { "z_horizontal", "z_vertical" },
        };

        /// <summary>The catalog piece of another family that stays in the tray: a 1x2 — or, for the Line family
        /// the 1x2 belongs to, a 2x2.</summary>
        private const string OTHER_ID = "line_h2";
        private const string OTHER_ID_FOR_LINE = "square_2x2";

        private const float LABEL_ROW = 4.6f;

        /// <summary>True when the demo can show <paramref name="family"/> with a chip counting to
        /// <paramref name="target"/>.</summary>
        internal static bool Supports(PieceFamily family, int target)
            => (int)family >= 0 && (int)family < MemberIds.Length && InfoDemoCountedPlacements.SupportsTarget(target);

        /// <summary>The catalog id of member <paramref name="placementIndex"/> of <paramref name="family"/>.</summary>
        internal static string MemberId(PieceFamily family, int placementIndex) => MemberIds[(int)family][placementIndex];

        /// <summary>The catalog id of the other-family piece left in the tray for <paramref name="family"/>.</summary>
        internal static string OtherId(PieceFamily family) => family == PieceFamily.Line ? OTHER_ID_FOR_LINE : OTHER_ID;

        internal static InfoDemoTimeline Build(PieceFamily family, int target) => Build(family, target, out _);

        /// <summary>As <see cref="Build(PieceFamily, int)"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(PieceFamily family, int target, out InfoDemoProgressChip chip)
        {
            Vector2Int[][] shapes = new Vector2Int[PLACEMENT_COUNT][];
            for (int placementIndex = 0; placementIndex < PLACEMENT_COUNT; placementIndex++)
            {
                shapes[placementIndex] = DemoShape(MemberId(family, placementIndex));
            }

            return InfoDemoCountedPlacements.Build(
                Rows, shapes[0], shapes, LandCells, DemoShape(OtherId(family)),
                LocalizationKeys.INFO_POPUP_DEMO_FAMILY_COUNTS, LABEL_ROW, target, out chip);
        }

        private static Vector2Int[] DemoShape(string pieceId)
            => PieceIdLineClearInfoDemo.DemoShape(PieceIdLineClearInfoDemo.FindPiece(pieceId));
    }
}
