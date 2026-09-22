using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    public class PieceCatalogTests
    {
        [Test]
        public void AllPieces_IdsAreUnique()
        {
            var seen = new HashSet<string>();
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                Assert.IsTrue(seen.Add(piece.Id), $"Duplicate piece id '{piece.Id}'.");
            }
        }

        [Test]
        public void AllPieces_HaveNoDuplicateOffsetsWithinAPiece()
        {
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                var offsets = new HashSet<GridPosition>();
                foreach (GridPosition offset in piece.Offsets)
                {
                    Assert.IsTrue(offsets.Add(offset), $"Piece '{piece.Id}' has a repeated offset {offset}.");
                }
            }
        }

        [Test]
        public void AllPieces_ContainsSingleCellPiece()
        {
            Assert.IsTrue(Contains("single_1x1"));
        }

        [TestCase("line_h2", 2)]
        [TestCase("line_h3", 3)]
        [TestCase("line_h4", 4)]
        [TestCase("line_h5", 5)]
        [TestCase("line_v2", 2)]
        [TestCase("line_v3", 3)]
        [TestCase("line_v4", 4)]
        [TestCase("line_v5", 5)]
        public void AllPieces_ContainsExpectedLines(string id, int expectedCellCount)
        {
            Piece piece = Find(id);
            Assert.AreEqual(expectedCellCount, piece.CellCount);
        }

        [Test]
        public void AllPieces_ContainsSquares()
        {
            Assert.AreEqual(4, Find("square_2x2").CellCount);
            Assert.AreEqual(9, Find("square_3x3").CellCount);
        }

        [Test]
        public void AllPieces_ContainsFourDistinct2x2Corners()
        {
            string[] ids = { "corner2_missing_tr", "corner2_missing_tl", "corner2_missing_bl", "corner2_missing_br" };
            foreach (string id in ids)
            {
                Assert.AreEqual(3, Find(id).CellCount, $"{id} should be a 3-cell L-tromino.");
            }
        }

        [Test]
        public void AllPieces_ContainsFourDistinct3x3Corners()
        {
            string[] ids = { "corner3_bl", "corner3_br", "corner3_tl", "corner3_tr" };
            foreach (string id in ids)
            {
                Assert.AreEqual(5, Find(id).CellCount, $"{id} should be a 5-cell L-shape.");
            }
        }

        [Test]
        public void AllPieces_ContainsFourTOrientations()
        {
            string[] ids = { "t_up", "t_down", "t_left", "t_right" };
            foreach (string id in ids)
            {
                Assert.AreEqual(4, Find(id).CellCount);
            }
        }

        [Test]
        public void AllPieces_ContainsTwoSAndTwoZOrientations()
        {
            string[] ids = { "s_horizontal", "s_vertical", "z_horizontal", "z_vertical" };
            foreach (string id in ids)
            {
                Assert.AreEqual(4, Find(id).CellCount);
            }
        }

        [Test]
        public void AllPieces_ContainsEightDistinctLAndJOrientations()
        {
            string[] ids =
            {
                "l_up", "l_right", "l_down", "l_left",
                "j_up", "j_right", "j_down", "j_left",
            };
            foreach (string id in ids)
            {
                Assert.AreEqual(4, Find(id).CellCount, $"{id} should be a 4-cell L/J tetromino.");
            }
        }

        /// <summary>Pins the catalog's total size so an accidental removal (or an accidental duplicate
        /// swallowed by a different check) doesn't slip through unnoticed.</summary>
        [Test]
        public void AllPieces_TotalCountMatchesTheAuthoredSet()
        {
            Assert.AreEqual(35, PieceCatalog.AllPieces.Count);
        }

        /// <summary>Adding the L/J tetrominoes must never disturb the existing symmetric corners.</summary>
        [Test]
        public void AllPieces_ExistingCornerPiecesAreUnchanged()
        {
            AssertOffsets("corner2_missing_tr", (0, 0), (1, 0), (0, 1));
            AssertOffsets("corner2_missing_tl", (0, 0), (1, 0), (1, 1));
            AssertOffsets("corner2_missing_bl", (1, 0), (0, 1), (1, 1));
            AssertOffsets("corner2_missing_br", (0, 0), (0, 1), (1, 1));
            AssertOffsets("corner3_bl", (0, 0), (1, 0), (2, 0), (0, 1), (0, 2));
            AssertOffsets("corner3_br", (0, 0), (1, 0), (2, 0), (2, 1), (2, 2));
            AssertOffsets("corner3_tl", (0, 0), (0, 1), (0, 2), (1, 2), (2, 2));
            AssertOffsets("corner3_tr", (2, 0), (2, 1), (2, 2), (1, 2), (0, 2));
        }

        [Test]
        public void AllPieces_EveryPieceOffsetsAreConnectedAndNonNegative()
        {
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                foreach (GridPosition offset in piece.Offsets)
                {
                    Assert.GreaterOrEqual(offset.X, 0, $"{piece.Id} has a negative X offset.");
                    Assert.GreaterOrEqual(offset.Y, 0, $"{piece.Id} has a negative Y offset.");
                }
            }
        }

        private static void AssertOffsets(string id, params (int X, int Y)[] expected)
        {
            Piece piece = Find(id);
            var expectedOffsets = new HashSet<GridPosition>();
            foreach ((int x, int y) in expected)
            {
                expectedOffsets.Add(new GridPosition(x, y));
            }

            CollectionAssert.AreEquivalent(expectedOffsets, new HashSet<GridPosition>(piece.Offsets));
        }

        private static Piece Find(string id)
        {
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                if (piece.Id == id)
                {
                    return piece;
                }
            }

            Assert.Fail($"Piece '{id}' not found in catalog.");
            return null;
        }

        private static bool Contains(string id)
        {
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                if (piece.Id == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
