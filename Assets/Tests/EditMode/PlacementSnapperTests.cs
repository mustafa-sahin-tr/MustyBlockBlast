using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    public class PlacementSnapperTests
    {
        [Test]
        public void Resolve_WhenRawAnchorIsLegal_LocksToIt()
        {
            var board = new Board();
            var snapper = new PlacementSnapper();
            Piece piece = Line(2);

            SnapResult result = snapper.Resolve(board, piece, new GridPosition(3, 3), searchRadius: 2);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(new GridPosition(3, 3), result.Anchor);
        }

        [Test]
        public void Resolve_WhenRawAnchorMovesToAnotherLegalSpot_FollowsRawImmediately()
        {
            // On an empty board every raw anchor is directly legal, so the preview must track the
            // pointer 1:1 with no stickiness — stickiness only matters once the raw anchor is illegal.
            var board = new Board();
            var snapper = new PlacementSnapper();
            Piece piece = Line(4);

            snapper.Resolve(board, piece, new GridPosition(0, 0), searchRadius: 2);

            SnapResult result = snapper.Resolve(board, piece, new GridPosition(1, 0), searchRadius: 2);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(new GridPosition(1, 0), result.Anchor);
        }

        [Test]
        public void Resolve_WhenRawAnchorBecomesIllegalButOverlapWithLockStaysAboveThreshold_KeepsLockedAnchor()
        {
            var board = new Board();
            board.Occupy(new GridPosition(3, 0), 1);
            var snapper = new PlacementSnapper();
            Piece piece = Line(3);

            snapper.Resolve(board, piece, new GridPosition(0, 0), searchRadius: 2);

            // Raw anchor (1,0) covers {1,2,3}; (3,0) is occupied so it is illegal. It still overlaps
            // the locked anchor's {0,1,2} at {1,2} (67%), above the 20% threshold.
            SnapResult result = snapper.Resolve(board, piece, new GridPosition(1, 0), searchRadius: 2);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(new GridPosition(0, 0), result.Anchor);
        }

        [Test]
        public void Resolve_WhenRawAnchorIllegalAndOverlapWithLockDropsBelowThreshold_SearchesNearest()
        {
            var board = new Board();
            board.Occupy(new GridPosition(7, 0), 1);
            var snapper = new PlacementSnapper();
            Piece piece = Line(4);

            snapper.Resolve(board, piece, new GridPosition(0, 0), searchRadius: 2);

            // Raw anchor (4,0) would cover {4,5,6,7}; (7,0) is occupied so it is illegal, and it has
            // zero overlap with the locked {0,1,2,3}. The nearest legal anchor is (3,0) -> {3,4,5,6}.
            SnapResult result = snapper.Resolve(board, piece, new GridPosition(4, 0), searchRadius: 2);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(new GridPosition(3, 0), result.Anchor);
        }

        [Test]
        public void Resolve_WhenRawAnchorIllegalButLegalPlacementWithinRadius_SnapsToNearest()
        {
            var board = new Board();
            board.Occupy(new GridPosition(3, 0), 1);
            var snapper = new PlacementSnapper();
            Piece piece = Single();

            SnapResult result = snapper.Resolve(board, piece, new GridPosition(3, 0), searchRadius: 2);

            Assert.IsTrue(result.IsValid);
            Assert.AreNotEqual(new GridPosition(3, 0), result.Anchor);
        }

        [Test]
        public void Resolve_WhenNoLegalPlacementWithinRadius_ReturnsInvalidAtRawAnchor()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                for (int y = 0; y < Board.SIZE; y++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            var snapper = new PlacementSnapper();
            Piece piece = Single();

            SnapResult result = snapper.Resolve(board, piece, new GridPosition(3, 3), searchRadius: 2);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(new GridPosition(3, 3), result.Anchor);
        }

        [Test]
        public void Resolve_WhenTiedCandidatesIncludeCurrentLock_KeepsCurrentLock()
        {
            var board = new Board();
            board.Occupy(new GridPosition(3, 3), 1);
            var snapper = new PlacementSnapper();
            Piece piece = Single();

            // Lock onto (2,3): one cell left of the occupied raw anchor.
            snapper.Resolve(board, piece, new GridPosition(2, 3), searchRadius: 2);

            // Raw anchor (3,3) is occupied; (2,3), (4,3), (3,2) and (3,4) are equally near candidates.
            SnapResult result = snapper.Resolve(board, piece, new GridPosition(3, 3), searchRadius: 2);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(new GridPosition(2, 3), result.Anchor);
        }

        [Test]
        public void Reset_ClearsLock_SoTieBreakNoLongerFavoursThePreviousAnchor()
        {
            var board = new Board();
            board.Occupy(new GridPosition(3, 3), 1);
            var snapper = new PlacementSnapper();
            Piece piece = Single();

            snapper.Resolve(board, piece, new GridPosition(2, 3), searchRadius: 2);
            snapper.Reset();

            // Same tie as Resolve_WhenTiedCandidatesIncludeCurrentLock_KeepsCurrentLock, but the lock
            // memory is gone, so the tie-break no longer prefers (2,3) over the other nearest ties.
            SnapResult result = snapper.Resolve(board, piece, new GridPosition(3, 3), searchRadius: 2);

            Assert.IsTrue(result.IsValid);
            Assert.AreNotEqual(new GridPosition(2, 3), result.Anchor);
        }

        private static Piece Line(int length)
        {
            var offsets = new GridPosition[length];
            for (int i = 0; i < length; i++)
            {
                offsets[i] = new GridPosition(i, 0);
            }

            return new Piece($"test_line_{length}", offsets);
        }

        private static Piece Single() => new Piece("test_single", new[] { new GridPosition(0, 0) });
    }
}
