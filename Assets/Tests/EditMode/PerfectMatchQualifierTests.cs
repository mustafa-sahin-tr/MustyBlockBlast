using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the "Perfect Match" predicate in isolation, decoupled from where the reward ends up
    /// (issue #352): which piece families qualify, and that every one of the piece's own cells — not
    /// merely some of them — has to fall inside the placement's own clear.
    /// </summary>
    public class PerfectMatchQualifierTests
    {
        /// <summary>The real catalog id, rebuilt here rather than looked up: the family check is defined
        /// on the id prefix, so a test that used a made-up one would prove nothing about the rule.</summary>
        private static readonly Piece Square2X2 = new Piece("square_2x2", Rectangle(2, 2));
        private static readonly Piece Corner3Bl = new Piece("corner3_bl", new[]
        {
            new GridPosition(0, 0), new GridPosition(0, 1), new GridPosition(1, 1),
        });
        private static readonly Piece Single = new Piece("single_1x1", new[] { new GridPosition(0, 0) });
        private static readonly Piece LineH3 = new Piece("line_h3", HorizontalRun(3));

        /// <summary>AC1: a shaped piece consumed whole by its own placement's clear qualifies.</summary>
        [Test]
        public void Qualifies_WithAShapedPieceFullyClearedByItsOwnRows_ReturnsTrue()
        {
            var anchor = new GridPosition(3, 3);
            var clearResult = new LineClearResult(new[] { 3, 4 }, new int[0], 0, 0);

            Assert.IsTrue(PerfectMatchQualifier.Qualifies(Square2X2, anchor, clearResult));
        }

        /// <summary>A cell can be covered by either axis — a mix of "its row cleared" and "its column
        /// cleared" is still every cell accounted for.</summary>
        [Test]
        public void Qualifies_WithCellsCoveredByAMixOfRowsAndColumns_ReturnsTrue()
        {
            // Corner3Bl occupies (0,0), (0,1), (1,1) relative to its anchor.
            var anchor = new GridPosition(5, 5);
            var clearResult = new LineClearResult(new[] { 6 }, new[] { 5 }, 0, 0);

            Assert.IsTrue(PerfectMatchQualifier.Qualifies(Corner3Bl, anchor, clearResult));
        }

        /// <summary>AC6/negative: a 1x1 piece is excluded by family, even when its one cell is fully
        /// covered by the clear.</summary>
        [Test]
        public void Qualifies_WithASingleCellPieceFullyCleared_ReturnsFalse()
        {
            var anchor = new GridPosition(5, 5);
            var clearResult = new LineClearResult(new[] { 5 }, new[] { 5 }, 0, 0);

            Assert.IsFalse(PerfectMatchQualifier.Qualifies(Single, anchor, clearResult));
        }

        /// <summary>AC7/negative: a straight line is excluded by family regardless of length, even when
        /// fully covered by the clear.</summary>
        [Test]
        public void Qualifies_WithAStraightLineFullyCleared_ReturnsFalse()
        {
            var anchor = new GridPosition(0, 5);
            var clearResult = new LineClearResult(new[] { 5 }, new int[0], 0, 0);

            Assert.IsFalse(PerfectMatchQualifier.Qualifies(LineH3, anchor, clearResult));
        }

        /// <summary>AC8/negative: a qualifying-family piece with only some of its own cells covered by
        /// the clear does not qualify.</summary>
        [Test]
        public void Qualifies_WithOnlySomeOfItsCellsCleared_ReturnsFalse()
        {
            var anchor = new GridPosition(3, 3);

            // Only row 3 cleared: (3,3) and (4,3) are covered, (3,4) and (4,4) are not.
            var clearResult = new LineClearResult(new[] { 3 }, new int[0], 0, 0);

            Assert.IsFalse(PerfectMatchQualifier.Qualifies(Square2X2, anchor, clearResult));
        }

        /// <summary>AC9/negative: a qualifying-family piece that clears no line at all does not qualify.</summary>
        [Test]
        public void Qualifies_WithNothingCleared_ReturnsFalse()
        {
            var anchor = new GridPosition(3, 3);
            var clearResult = new LineClearResult(new int[0], new int[0], 0, 0);

            Assert.IsFalse(PerfectMatchQualifier.Qualifies(Square2X2, anchor, clearResult));
        }

        /// <summary>AC10: a piercing rocket's wipe reaches this only through the ordinary clear-result
        /// plumbing — a shaped piece whose placement wipe cleared no actual line qualifies for nothing,
        /// exactly as the "nothing cleared" case above.</summary>
        [Test]
        public void Qualifies_WithAQualifyingShapeAndDefaultClearResult_ReturnsFalse()
        {
            var anchor = new GridPosition(0, 0);

            Assert.IsFalse(PerfectMatchQualifier.Qualifies(Square2X2, anchor, default));
        }

        [Test]
        public void Qualifies_WithANullPiece_ReturnsFalse()
        {
            var clearResult = new LineClearResult(new[] { 0 }, new[] { 0 }, 0, 0);

            Assert.IsFalse(PerfectMatchQualifier.Qualifies(null, new GridPosition(0, 0), clearResult));
        }

        private static GridPosition[] Rectangle(int width, int height)
        {
            var offsets = new GridPosition[width * height];
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    offsets[index] = new GridPosition(x, y);
                    index++;
                }
            }

            return offsets;
        }

        private static GridPosition[] HorizontalRun(int length)
        {
            var offsets = new GridPosition[length];
            for (int x = 0; x < length; x++)
            {
                offsets[x] = new GridPosition(x, 0);
            }

            return offsets;
        }
    }
}
