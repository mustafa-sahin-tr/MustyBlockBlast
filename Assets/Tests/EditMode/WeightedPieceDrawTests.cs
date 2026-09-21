using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the two halves of the draw contract that must not drift into each other: the ordinary
    /// draw is unguaranteed and stays that way, while the Reroll-only set draw is guaranteed within a
    /// bound and degrades gracefully when even that bound cannot deliver. The line-clear-preferring
    /// variant is held to the same bound and the same fallback, plus its own preference.
    /// <para>
    /// Everything here is seeded, so "the ordinary draw can come up unplayable" is demonstrated by a
    /// specific reproducible draw rather than by a probabilistic sample.
    /// </para>
    /// </summary>
    public class WeightedPieceDrawTests
    {
        /// <summary>How many seeds the search below will try. Well past the point one is found — the
        /// search exists to avoid a magic seed in the assertion, not to be a real hunt.</summary>
        private const int SEED_SEARCH_LIMIT = 64;

        /// <summary>
        /// The regression guard on the ordinary draw: on a board with only a 2x2 corner open there are
        /// seeds whose three <see cref="WeightedPieceDraw.DrawPiece"/> results cannot be placed at all.
        /// A refill may be unplayable, and that stays a legitimate game over
        /// (docs/game-design.md, "Drawing pieces") — the Reroll guarantee must not have leaked into it.
        /// </summary>
        [Test]
        public void DrawPiece_IsNotSolvabilityGuaranteed_OnAConstrainedBoard()
        {
            Board board = BoardWithOpenCorner();

            Assert.IsTrue(
                TryFindUnplayableOrdinaryDrawSeed(board, out int seed),
                $"No seed below {SEED_SEARCH_LIMIT} produced an unplayable ordinary draw.");

            Assert.IsFalse(HasAnyMoveInOrdinaryDraw(board, seed));
        }

        /// <summary>
        /// The other half of the same comparison: the very seed that defeats the ordinary draw produces
        /// a playable set through the Reroll path, because that one retries. Same draw, same seed, same
        /// board — the only difference is the guarantee.
        /// </summary>
        [Test]
        public void TryDrawSolvableSet_OnTheSeedThatDefeatsTheOrdinaryDraw_StillFindsAPlayableSet()
        {
            Board board = BoardWithOpenCorner();
            Assert.IsTrue(TryFindUnplayableOrdinaryDrawSeed(board, out int seed));

            var pieces = new Piece[3];
            var colourIds = new int[3];

            Assert.IsTrue(new WeightedPieceDraw(seed).TryDrawSolvableSet(board, pieces, colourIds));
            Assert.IsTrue(MoveAvailability.HasAnyMove(board, pieces));
        }

        [Test]
        public void TryDrawSolvableSet_FillsEverySlotWithAPieceAndAColour()
        {
            var pieces = new Piece[3];
            var colourIds = new int[3];

            new WeightedPieceDraw(seed: 1).TryDrawSolvableSet(new Board(), pieces, colourIds);

            for (int slotIndex = 0; slotIndex < pieces.Length; slotIndex++)
            {
                Assert.IsNotNull(pieces[slotIndex]);
                Assert.GreaterOrEqual(colourIds[slotIndex], 1);
                Assert.LessOrEqual(colourIds[slotIndex], WeightedPieceDraw.COLOUR_COUNT);
            }
        }

        /// <summary>
        /// AC5, the adversarial case: a completely full board has no legal placement for any piece, so
        /// no set can ever satisfy the guarantee. The draw must still terminate inside its bound, throw
        /// nothing, and hand back a complete set — the documented fallback.
        /// </summary>
        [Test]
        public void TryDrawSolvableSet_OnAFullBoard_GivesUpWithinTheBoundAndStillReturnsASet()
        {
            Board board = FullBoard();
            var pieces = new Piece[3];
            var colourIds = new int[3];
            var draw = new WeightedPieceDraw(seed: 42);

            bool guaranteed = false;
            Assert.DoesNotThrow(() => guaranteed = draw.TryDrawSolvableSet(board, pieces, colourIds));

            Assert.IsFalse(guaranteed);
            for (int slotIndex = 0; slotIndex < pieces.Length; slotIndex++)
            {
                Assert.IsNotNull(pieces[slotIndex], "The fallback must still hand back a full set.");
            }
        }

        /// <summary>
        /// The bound is a real one. A full board can never satisfy the guarantee, so the only thing that
        /// can end the loop is the cap — and a cap that took a different number of attempts each run
        /// would not be one. Two identically seeded draws over the same hopeless board therefore agree
        /// exactly, which is only true if both stopped at the same fixed attempt count.
        /// </summary>
        [Test]
        public void TryDrawSolvableSet_OnAFullBoard_StopsAtAFixedAttemptCount()
        {
            Board board = FullBoard();

            var firstPieces = new Piece[3];
            var firstColours = new int[3];
            new WeightedPieceDraw(seed: 8).TryDrawSolvableSet(board, firstPieces, firstColours);

            var secondPieces = new Piece[3];
            var secondColours = new int[3];
            new WeightedPieceDraw(seed: 8).TryDrawSolvableSet(board, secondPieces, secondColours);

            CollectionAssert.AreEqual(firstPieces, secondPieces);
            CollectionAssert.AreEqual(firstColours, secondColours);
            Assert.Greater(WeightedPieceDraw.MAX_SOLVABLE_DRAW_ATTEMPTS, 0);
        }

        [Test]
        public void TryDrawSolvableSet_WithMismatchedBufferLengths_IsRejected()
        {
            var draw = new WeightedPieceDraw(seed: 2);

            Assert.Throws<System.ArgumentException>(
                () => draw.TryDrawSolvableSet(new Board(), new Piece[3], new int[2]));
        }

        /// <summary>
        /// The positive case: row 0 is one cell short, so any set with a piece that can drop into that
        /// gap clears a line. Small pieces dominate the catalog, so over a full budget the draw must find
        /// one — and report it, and leave it in the buffers.
        /// </summary>
        [Test]
        public void TryDrawLineClearingSet_WithAnObviousLineToClear_ReturnsASetContainingAClearingPiece()
        {
            Board board = BoardWithOneGapInRowZero();
            var pieces = new Piece[3];
            var colourIds = new int[3];

            bool found = new WeightedPieceDraw(seed: 3).TryDrawLineClearingSet(
                board, pieces, colourIds, out int maxLineCount);

            Assert.IsTrue(found);
            Assert.GreaterOrEqual(maxLineCount, 1);
            Assert.AreEqual(maxLineCount, MaxLinesAnyPieceClears(board, pieces));
            Assert.IsTrue(MoveAvailability.HasAnyMove(board, pieces));
        }

        /// <summary>
        /// AC4's fallback: an empty board is trivially solvable but no catalog piece spans eight cells,
        /// so no attempt can clear a line. The draw must then degrade to exactly what
        /// <see cref="WeightedPieceDraw.TryDrawSolvableSet"/> promises — a complete, placeable set — and
        /// say honestly that it found no clearing piece.
        /// </summary>
        [Test]
        public void TryDrawLineClearingSet_WhenNoClearIsReachable_FallsBackToASolvableSet()
        {
            var board = new Board();
            var pieces = new Piece[3];
            var colourIds = new int[3];

            bool found = new WeightedPieceDraw(seed: 5).TryDrawLineClearingSet(
                board, pieces, colourIds, out int maxLineCount);

            Assert.IsFalse(found);
            Assert.AreEqual(0, maxLineCount);
            Assert.IsTrue(MoveAvailability.HasAnyMove(board, pieces));
            for (int slotIndex = 0; slotIndex < pieces.Length; slotIndex++)
            {
                Assert.IsNotNull(pieces[slotIndex]);
                Assert.GreaterOrEqual(colourIds[slotIndex], 1);
                Assert.LessOrEqual(colourIds[slotIndex], WeightedPieceDraw.COLOUR_COUNT);
            }
        }

        /// <summary>
        /// The "never worse than the solvable draw" clause, on the board that defeats the ordinary draw:
        /// the same seed that produces an unplayable ordinary set still yields a playable one here,
        /// because a solvable attempt always outranks an unplaceable one.
        /// </summary>
        [Test]
        public void TryDrawLineClearingSet_OnTheSeedThatDefeatsTheOrdinaryDraw_StillFindsAPlayableSet()
        {
            Board board = BoardWithOpenCorner();
            Assert.IsTrue(TryFindUnplayableOrdinaryDrawSeed(board, out int seed));

            var pieces = new Piece[3];
            var colourIds = new int[3];
            new WeightedPieceDraw(seed).TryDrawLineClearingSet(board, pieces, colourIds, out _);

            Assert.IsTrue(MoveAvailability.HasAnyMove(board, pieces));
        }

        /// <summary>The full-board fallback, mirroring the solvable draw's: no attempt can succeed, the
        /// draw terminates within its bound, throws nothing, and hands back a complete set.</summary>
        [Test]
        public void TryDrawLineClearingSet_OnAFullBoard_GivesUpWithinTheBoundAndStillReturnsASet()
        {
            Board board = FullBoard();
            var pieces = new Piece[3];
            var colourIds = new int[3];
            var draw = new WeightedPieceDraw(seed: 42);

            bool found = true;
            int maxLineCount = -1;
            Assert.DoesNotThrow(
                () => found = draw.TryDrawLineClearingSet(board, pieces, colourIds, out maxLineCount));

            Assert.IsFalse(found);
            Assert.AreEqual(0, maxLineCount);
            for (int slotIndex = 0; slotIndex < pieces.Length; slotIndex++)
            {
                Assert.IsNotNull(pieces[slotIndex], "The fallback must still hand back a full set.");
            }
        }

        /// <summary>Same argument as the solvable draw's fixed-attempt test: on a hopeless board only the
        /// cap ends the loop, so two identically seeded draws agree exactly only if the cap is fixed.</summary>
        [Test]
        public void TryDrawLineClearingSet_OnAFullBoard_StopsAtAFixedAttemptCount()
        {
            Board board = FullBoard();

            var firstPieces = new Piece[3];
            var firstColours = new int[3];
            new WeightedPieceDraw(seed: 8).TryDrawLineClearingSet(board, firstPieces, firstColours, out _);

            var secondPieces = new Piece[3];
            var secondColours = new int[3];
            new WeightedPieceDraw(seed: 8).TryDrawLineClearingSet(board, secondPieces, secondColours, out _);

            CollectionAssert.AreEqual(firstPieces, secondPieces);
            CollectionAssert.AreEqual(firstColours, secondColours);
        }

        [Test]
        public void TryDrawLineClearingSet_WithMismatchedBufferLengths_IsRejected()
        {
            var draw = new WeightedPieceDraw(seed: 2);

            Assert.Throws<System.ArgumentException>(
                () => draw.TryDrawLineClearingSet(new Board(), new Piece[3], new int[2], out _));
        }

        private static int MaxLinesAnyPieceClears(Board board, Piece[] pieces)
        {
            var opportunity = new LineClearOpportunity();
            int best = 0;
            for (int slotIndex = 0; slotIndex < pieces.Length; slotIndex++)
            {
                int lineCount = opportunity.MaxLinesAnyPlacementClears(board, pieces[slotIndex]);
                if (lineCount > best)
                {
                    best = lineCount;
                }
            }

            return best;
        }

        /// <summary>Row 0 filled except its first cell; everything else open. Any piece that can cover
        /// (0,0) without spilling into the rest of row 0 clears that row.</summary>
        private static Board BoardWithOneGapInRowZero()
        {
            var board = new Board();
            for (int x = 1; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            return board;
        }

        /// <summary>The first seed whose three ordinary draws are all unplaceable on
        /// <paramref name="board"/>, searched rather than hard-coded so the test states the property it
        /// depends on instead of a magic number.</summary>
        private static bool TryFindUnplayableOrdinaryDrawSeed(Board board, out int seed)
        {
            for (int candidate = 0; candidate < SEED_SEARCH_LIMIT; candidate++)
            {
                if (!HasAnyMoveInOrdinaryDraw(board, candidate))
                {
                    seed = candidate;
                    return true;
                }
            }

            seed = -1;
            return false;
        }

        /// <summary>Three bare <see cref="WeightedPieceDraw.DrawPiece"/> calls — exactly what an
        /// ordinary tray refill does, with no retry and no solvability check.</summary>
        private static bool HasAnyMoveInOrdinaryDraw(Board board, int seed)
        {
            var draw = new WeightedPieceDraw(seed);
            var pieces = new List<Piece>(3);
            for (int slotIndex = 0; slotIndex < 3; slotIndex++)
            {
                pieces.Add(draw.DrawPiece());
            }

            return MoveAvailability.HasAnyMove(board, pieces);
        }

        /// <summary>Every cell occupied but a 2x2 block in the bottom-left corner: most of the catalog
        /// cannot be placed, but a meaningful minority still can.</summary>
        private static Board BoardWithOpenCorner()
        {
            var board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    if (x < 2 && y < 2)
                    {
                        continue;
                    }

                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            return board;
        }

        private static Board FullBoard()
        {
            var board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                }
            }

            return board;
        }
    }
}
