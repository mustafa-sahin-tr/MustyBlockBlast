using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;

// Unity's allocation constraint lives on its own Is, and the extension that backs it needs the
// namespace above imported — which would otherwise make the name ambiguous with NUnit's Is.
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Pins down the rotation math and, more importantly, the property the Rotate power-up is built on:
    /// the catalog is closed under 90-degree rotation, so rotating never has to invent a piece.
    /// </summary>
    public class PieceRotatorTests
    {
        [TestCase("t_up", "t_right")]
        [TestCase("t_right", "t_down")]
        [TestCase("t_down", "t_left")]
        [TestCase("t_left", "t_up")]
        [TestCase("line_h5", "line_v5")]
        [TestCase("line_v2", "line_h2")]
        [TestCase("corner2_missing_tr", "corner2_missing_br")]
        [TestCase("corner3_bl", "corner3_tl")]
        [TestCase("l_up", "l_right")]
        [TestCase("l_right", "l_down")]
        [TestCase("l_down", "l_left")]
        [TestCase("l_left", "l_up")]
        [TestCase("j_up", "j_right")]
        [TestCase("j_right", "j_down")]
        [TestCase("j_down", "j_left")]
        [TestCase("j_left", "j_up")]
        public void TryRotateClockwise_TurnsThePieceOntoTheExpectedCatalogEntry(string id, string expectedId)
        {
            Piece piece = Find(id);

            bool rotated = PieceRotator.TryRotateClockwise(piece, out Piece result);

            Assert.IsTrue(rotated, $"'{id}' should have a distinct rotation.");
            Assert.AreEqual(expectedId, result.Id);
        }

        /// <summary>
        /// The catalog-closure property the whole design rests on: every piece's rotation is itself a
        /// catalog piece, so a rotation is always a swap to a real entry with a truthful id — never a
        /// synthesised piece whose id would stop describing its shape.
        /// </summary>
        [Test]
        public void TryRotateClockwise_EveryCatalogPieceRotatesOntoACatalogPiece()
        {
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                bool rotated = PieceRotator.TryRotateClockwise(piece, out Piece result);

                if (PieceRotator.IsFullySymmetrical(piece))
                {
                    Assert.IsFalse(rotated, $"'{piece.Id}' is symmetrical and must refuse to rotate.");
                    continue;
                }

                Assert.IsTrue(rotated, $"'{piece.Id}' has no catalog entry for its rotation.");
                Assert.AreEqual(piece.CellCount, result.CellCount);
            }
        }

        [TestCase("t_up")]
        [TestCase("t_left")]
        [TestCase("corner3_bl")]
        [TestCase("corner2_missing_tl")]
        [TestCase("line_h4")]
        [TestCase("s_horizontal")]
        [TestCase("z_vertical")]
        [TestCase("l_up")]
        [TestCase("j_left")]
        public void TryRotateClockwise_FourTimes_ReturnsToTheOriginalPiece(string id)
        {
            Piece piece = Find(id);
            Piece current = piece;

            for (int turn = 0; turn < 4; turn++)
            {
                Assert.IsTrue(
                    PieceRotator.TryRotateClockwise(current, out current),
                    $"'{id}' stopped rotating after {turn} turns.");
            }

            Assert.AreEqual(piece.Id, current.Id);
        }

        [TestCase("single_1x1")]
        [TestCase("square_2x2")]
        [TestCase("square_3x3")]
        public void IsFullySymmetrical_ForASymmetricalPiece_IsTrueAndRotationIsRefused(string id)
        {
            Piece piece = Find(id);

            Assert.IsTrue(PieceRotator.IsFullySymmetrical(piece));
            Assert.IsFalse(PieceRotator.TryRotateClockwise(piece, out Piece result));
            Assert.IsNull(result);
        }

        /// <summary>The symmetrical set is derived from the offsets, not from a list of ids — so this
        /// asserts the catalog contains exactly the three the design names, and nothing else.</summary>
        [Test]
        public void IsFullySymmetrical_IsTrueForExactlyTheThreeSquarePieces()
        {
            var symmetrical = new List<string>();
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                if (PieceRotator.IsFullySymmetrical(piece))
                {
                    symmetrical.Add(piece.Id);
                }
            }

            CollectionAssert.AreEquivalent(
                new[] { "single_1x1", "square_2x2", "square_3x3" }, symmetrical);
        }

        /// <summary>The aim preview asks this on every frame of a drag, so it must not allocate.</summary>
        [Test]
        public void IsFullySymmetrical_AllocatesNothing()
        {
            Piece piece = Find("t_up");

            // Jitted before it is measured, so the first-call compilation cost is not mistaken for an
            // allocation by the test.
            PieceRotator.IsFullySymmetrical(piece);

            // A statement body, not an expression one: the constraint takes a void TestDelegate.
            Assert.That(
                () => { PieceRotator.IsFullySymmetrical(piece); },
                Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void TryRotateClockwise_AllocatesNothing()
        {
            Piece piece = Find("corner3_bl");
            PieceRotator.TryRotateClockwise(piece, out Piece _);

            Assert.That(
                () => { PieceRotator.TryRotateClockwise(piece, out Piece _); },
                Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void TryRotateClockwise_WithNoPiece_IsRefused()
        {
            Assert.IsFalse(PieceRotator.TryRotateClockwise(null, out Piece result));
            Assert.IsNull(result);
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
    }
}
