using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Every id prefix the <see cref="PieceCatalog"/> emits must land on the right
    /// <see cref="PieceFamily"/> — objectives are written against families, so a mis-classified
    /// prefix silently makes an objective untrackable.
    /// </summary>
    public class PieceFamilyClassifierTests
    {
        [TestCase("single_1x1", PieceFamily.Single)]
        [TestCase("line_h2", PieceFamily.Line)]
        [TestCase("line_v5", PieceFamily.Line)]
        [TestCase("square_2x2", PieceFamily.Square)]
        [TestCase("square_3x3", PieceFamily.Square)]
        [TestCase("corner2_missing_tr", PieceFamily.Corner)]
        [TestCase("corner3_bl", PieceFamily.Corner)]
        [TestCase("l_up", PieceFamily.Corner)]
        [TestCase("l_left", PieceFamily.Corner)]
        [TestCase("j_down", PieceFamily.Corner)]
        [TestCase("j_right", PieceFamily.Corner)]
        [TestCase("t_up", PieceFamily.TShape)]
        [TestCase("t_left", PieceFamily.TShape)]
        [TestCase("s_horizontal", PieceFamily.SShape)]
        [TestCase("s_vertical", PieceFamily.SShape)]
        [TestCase("z_horizontal", PieceFamily.ZShape)]
        [TestCase("z_vertical", PieceFamily.ZShape)]
        public void Classify_MapsCatalogIdToItsFamily(string pieceId, PieceFamily expected)
        {
            Assert.AreEqual(expected, PieceFamilyClassifier.Classify(pieceId));
        }

        [Test]
        public void EveryCatalogPiece_ClassifiesWithoutFallingBackByAccident()
        {
            // A "single" family result is only legitimate for the one 1x1 piece; anything else reaching
            // it means a prefix was missed and the fallback swallowed it.
            for (int pieceIndex = 0; pieceIndex < PieceCatalog.AllPieces.Count; pieceIndex++)
            {
                Piece piece = PieceCatalog.AllPieces[pieceIndex];
                PieceFamily family = PieceFamilyClassifier.Classify(piece.Id);

                if (family == PieceFamily.Single)
                {
                    Assert.AreEqual("single_1x1", piece.Id);
                }
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("unknown_shape")]
        public void UnknownId_FallsBackToSingle_RatherThanThrowing(string pieceId)
        {
            Assert.AreEqual(PieceFamily.Single, PieceFamilyClassifier.Classify(pieceId));
        }
    }
}
