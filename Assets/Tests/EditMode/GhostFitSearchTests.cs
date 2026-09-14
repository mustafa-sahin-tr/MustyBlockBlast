using MustyBlockBlast.Core;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;

// Unity's allocation constraint lives on its own Is, and the extension that backs it needs the
// namespace above imported — which would otherwise make the name ambiguous with NUnit's Is.
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the exact search behind the Ghost Fit power-up: that it really does find the optimum over
    /// every dock piece and every anchor, that the three ranking criteria apply in the stated order, and
    /// that a dock with nowhere to go is reported rather than thrown.
    /// <para>
    /// Everything here talks to <see cref="GhostFitSearch"/> directly with hand-built boards and pieces.
    /// The search is pure Core, so nothing about the power-up's inventory, its input gesture or its
    /// silhouette has to be stood up to pin down which move it picks.
    /// </para>
    /// </summary>
    public class GhostFitSearchTests
    {
        [Test]
        public void TryFindBestMove_EmptyBoardAndEmptyDock_FindsNothing()
        {
            var search = new GhostFitSearch();
            Piece[] dock = { null, null, null };

            Assert.IsFalse(search.TryFindBestMove(new Board(), dock, isStreakActive: false, out GhostFitMove move));
            Assert.AreEqual(default(GhostFitMove), move);
        }

        /// <summary>The acceptance criterion's negative case: nothing fits anywhere, so the search says
        /// so instead of crashing or spinning.</summary>
        [Test]
        public void TryFindBestMove_NoPieceFitsAnywhere_FindsNothing()
        {
            var board = new Board();
            FillEntireBoard(board);

            var search = new GhostFitSearch();
            Piece[] dock = { Single(), Domino(), Single() };

            Assert.IsFalse(search.TryFindBestMove(board, dock, isStreakActive: false, out GhostFitMove move));
            Assert.AreEqual(default(GhostFitMove), move);
        }

        [Test]
        public void TryFindBestMove_SkipsEmptyDockSlots()
        {
            var search = new GhostFitSearch();
            Piece[] dock = { null, Single(), null };

            Assert.IsTrue(search.TryFindBestMove(new Board(), dock, isStreakActive: false, out GhostFitMove move));
            Assert.AreEqual(1, move.SlotIndex);
        }

        /// <summary>
        /// Acceptance criterion 1. Row 0 is one cell short of full and only the single-cell piece can
        /// complete it, so exactly one placement in the whole search space clears a line — and it is in
        /// the last dock slot, so finding it means every slot really was searched.
        /// </summary>
        [Test]
        public void TryFindBestMove_OnePlacementClearsTheMostLines_PicksExactlyThatPieceAndAnchor()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            var search = new GhostFitSearch();

            // The domino cannot complete row 0: that needs two adjacent empty cells in it, and there is
            // only one empty cell left.
            Piece[] dock = { Domino(), null, Single() };

            Assert.IsTrue(search.TryFindBestMove(board, dock, isStreakActive: false, out GhostFitMove move));

            Assert.AreEqual(2, move.SlotIndex);
            Assert.AreEqual(new GridPosition(Board.SIZE - 1, 0), move.Anchor);
            Assert.AreEqual(1, move.LineCount);
        }

        /// <summary>
        /// Criterion 1 outranks everything below it: a placement that clears two lines wins over one
        /// that clears one, which in turn wins over the 60-odd that clear none.
        /// </summary>
        [Test]
        public void TryFindBestMove_PrefersTheDoubleClearOverTheSingleClear()
        {
            Board board = BuildDoubleAndSingleClearBoard();
            var search = new GhostFitSearch();
            Piece[] dock = { Single(), null, null };

            Assert.IsTrue(search.TryFindBestMove(board, dock, isStreakActive: false, out GhostFitMove move));

            // (7,0) is the shared gap of the near-full row 0 and the near-full column 7.
            Assert.AreEqual(new GridPosition(Board.SIZE - 1, 0), move.Anchor);
            Assert.AreEqual(2, move.LineCount);
        }

        /// <summary>
        /// Acceptance criterion 2, as literally as it can be staged: the streak is running, one placement
        /// clears a line and every other clears none, and the clearing one is chosen.
        /// <para>
        /// It passes because of criterion 1, not criterion 2 — see
        /// <see cref="TryFindBestMove_WithNoStreak_MakesTheSameChoice"/>, which pins the same answer with
        /// the streak switched off. The criterion describes a preference applied "given a tie on
        /// lines-cleared", and two placements tied on lines cleared have the <em>same</em> count, so
        /// "clears >= 1" is true of both or neither and the preference can never break the tie. The
        /// combo-preservation term exists in the ranking regardless, ordered directly under criterion 1,
        /// so the behaviour described here is guaranteed by construction rather than incidental.
        /// </para>
        /// </summary>
        [Test]
        public void TryFindBestMove_WithStreakActive_PrefersThePlacementThatClearsALine()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            var search = new GhostFitSearch();
            Piece[] dock = { Single(), null, null };

            Assert.IsTrue(search.TryFindBestMove(board, dock, isStreakActive: true, out GhostFitMove move));

            Assert.AreEqual(new GridPosition(Board.SIZE - 1, 0), move.Anchor);
            Assert.AreEqual(1, move.LineCount);
        }

        [Test]
        public void TryFindBestMove_WithNoStreak_MakesTheSameChoice()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            var search = new GhostFitSearch();
            Piece[] dock = { Single(), null, null };

            Assert.IsTrue(search.TryFindBestMove(board, dock, isStreakActive: false, out GhostFitMove move));

            Assert.AreEqual(new GridPosition(Board.SIZE - 1, 0), move.Anchor);
            Assert.AreEqual(1, move.LineCount);
        }

        /// <summary>
        /// Criterion 3 on its own: no placement anywhere on this board clears a line, so the whole
        /// ranking comes down to how much contiguous room is left afterwards.
        /// <para>
        /// The fixture walls the board into a 30-cell region, a 22-cell one and two one-cell pockets.
        /// Spending the piece on a pocket leaves the 30-cell region whole; spending it inside that region
        /// eats into it and leaves 29. The best answer is therefore 30, and the first anchor reaching it
        /// in the search's documented scan order — slot, then row, then column — is the bottom pocket.
        /// </para>
        /// </summary>
        [Test]
        public void TryFindBestMove_AllPlacementsClearNothing_PicksTheOneLeavingTheMostOpenSpace()
        {
            Board board = BuildWalledBoard();
            var search = new GhostFitSearch();
            Piece[] dock = { Single(), null, null };

            Assert.IsTrue(search.TryFindBestMove(board, dock, isStreakActive: false, out GhostFitMove move));

            Assert.AreEqual(0, move.LineCount, "The fixture must not let any placement clear a line.");
            Assert.AreEqual(new GridPosition(4, 0), move.Anchor);
            Assert.AreEqual(30, move.LargestOpenRegion);
        }

        /// <summary>A placement inside the big region is measurably worse on criterion 3 than the one the
        /// search picked — the other half of the assertion above, stated without going through the
        /// search.</summary>
        [Test]
        public void WalledBoard_PlacingInsideTheLargestRegion_LeavesLessOpenSpace()
        {
            Board board = BuildWalledBoard();
            board.Occupy(new GridPosition(0, 0), 1);

            Assert.AreEqual(
                29, board.LargestEmptyRegionSize(new bool[Board.SIZE * Board.SIZE], new int[Board.SIZE * Board.SIZE]));
        }

        /// <summary>
        /// The player can ask for a suggestion whenever they like, so the whole 192-candidate sweep must
        /// stay off the heap — the same guarantee the drag preview's would-clear query already carries,
        /// and the reason the search owns its scratch buffers instead of allocating per call.
        /// <para>
        /// Staged on the widest candidate space there is: a board empty but for one near-full row, and
        /// all three dock slots filled. Every slot is legal nearly everywhere, so the sweep runs at close
        /// to its 192-candidate ceiling — and, because that row can be completed, the projection's
        /// row/column clearing path is measured too rather than only the never-clears path a walled
        /// fixture would exercise.
        /// </para>
        /// </summary>
        [Test]
        public void TryFindBestMove_OverTheWidestCandidateSpace_AllocatesNothing()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            var search = new GhostFitSearch();
            Piece[] dock = { Single(), Domino(), Square(2) };

            // Jitted before it is measured, so the first-call compilation cost is not mistaken for an
            // allocation by the test. Also proves the clearing path really is reached below.
            Assert.IsTrue(search.TryFindBestMove(board, dock, isStreakActive: false, out GhostFitMove warmUp));
            Assert.AreEqual(1, warmUp.LineCount, "The fixture must make the projection resolve a clear.");

            // A statement body, not an expression one: the constraint takes a void TestDelegate.
            Assert.That(
                () => { search.TryFindBestMove(board, dock, isStreakActive: false, out GhostFitMove _); },
                Is.Not.AllocatingGCMemory());
        }

        /// <summary>
        /// The suggestion sits on screen rather than flashing past, so the same board and dock must always
        /// produce the same move: a search that picked differently between two identical calls would make
        /// the silhouette flicker between equally good anchors. Ties are broken by scan order — dock slot
        /// ascending, then row, then column — which an empty board and three identical pieces puts under
        /// maximum pressure, every one of the 192 candidates being equally good.
        /// </summary>
        [Test]
        public void TryFindBestMove_WithEverythingTied_IsDeterministicAndTakesTheFirstInScanOrder()
        {
            var search = new GhostFitSearch();
            Piece[] dock = { Single(), Single(), Single() };

            Assert.IsTrue(search.TryFindBestMove(new Board(), dock, isStreakActive: false, out GhostFitMove first));
            Assert.IsTrue(search.TryFindBestMove(new Board(), dock, isStreakActive: false, out GhostFitMove second));

            Assert.AreEqual(first, second, "The same board and dock must always yield the same suggestion.");
            Assert.AreEqual(0, first.SlotIndex, "Slot order is the outermost tie-break.");
            Assert.AreEqual(new GridPosition(0, 0), first.Anchor, "Then row, then column.");
        }

        /// <summary>
        /// Row 0 and column 7 are each one cell short of full and share that gap at (7,0), so filling it
        /// clears both. Row 4 is separately one short at (6,4), which clears one. Nothing else on the
        /// board can clear anything.
        /// </summary>
        private static Board BuildDoubleAndSingleClearBoard()
        {
            var board = new Board();

            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            for (int y = 1; y < Board.SIZE; y++)
            {
                board.Occupy(new GridPosition(Board.SIZE - 1, y), 1);
            }

            for (int x = 0; x <= 5; x++)
            {
                board.Occupy(new GridPosition(x, 4), 1);
            }

            return board;
        }

        /// <summary>
        /// A board split into four empty regions by a wall down column 4 with a sealed pocket at each
        /// end: 30 cells to the left, 22 to the right, and (4,0) and (4,7) on their own. Every row and
        /// every column keeps at least two empty cells, so no single-cell placement on it can complete a
        /// line — which is what makes it a clean test of criterion 3 alone.
        /// </summary>
        private static Board BuildWalledBoard()
        {
            var board = new Board();

            for (int y = 1; y <= Board.SIZE - 2; y++)
            {
                board.Occupy(new GridPosition(4, y), 1);
            }

            board.Occupy(new GridPosition(3, 0), 1);
            board.Occupy(new GridPosition(5, 0), 1);
            board.Occupy(new GridPosition(3, Board.SIZE - 1), 1);
            board.Occupy(new GridPosition(5, Board.SIZE - 1), 1);

            return board;
        }

        private static void FillEntireBoard(Board board)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }
        }

        private static Piece Single() => new Piece("single", new[] { new GridPosition(0, 0) });

        private static Piece Domino()
            => new Piece("domino", new[] { new GridPosition(0, 0), new GridPosition(1, 0) });

        private static Piece Square(int side)
        {
            var offsets = new GridPosition[side * side];
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    offsets[(y * side) + x] = new GridPosition(x, y);
                }
            }

            return new Piece($"square_{side}x{side}", offsets);
        }
    }
}
