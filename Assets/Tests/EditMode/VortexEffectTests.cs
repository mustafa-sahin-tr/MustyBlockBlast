using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the fill itself (issue #349): which cells count as an island (via
    /// <see cref="Board.CollectEnclosedEmptyIslands"/>, including the border-row/column edge case that
    /// method exists to catch), multiple islands filled by one trigger, the hand-off when no island
    /// exists (and its own no-op when no eligible cell exists either), that a Reinforced cell is never
    /// overwritten, and the chain into a line a fill itself completes.
    /// </summary>
    public class VortexEffectTests
    {
        private VortexEffect _effect;

        [SetUp]
        public void CreateEffect()
        {
            _effect = new VortexEffect(new Random(1));
            _effect.BeginResolution();
        }

        [Test]
        public void Constructor_WithNoRandom_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new VortexEffect(null));
        }

        /// <summary>AC1: a single interior island present at trigger time is filled in the same
        /// resolution.</summary>
        [Test]
        public void Apply_WithASingleInteriorIsland_FillsEveryCellOfIt()
        {
            Board board = new Board();
            OccupyRectangle(board, 2, 2, 4, 4, 1);
            GridPosition island = new GridPosition(3, 3);
            board.Clear(island);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Vortex));

            Assert.IsTrue(board.IsOccupied(island));
            Assert.AreEqual(1, _effect.FilledCells.Count);
            Assert.AreEqual(island, _effect.FilledCells[0]);
            Assert.AreEqual(0, _effect.HandOffTargets.Count, "A fill and a hand-off never happen together.");
        }

        /// <summary>AC2: an island whose cells sit on the board's last row and column, boxed in on every
        /// in-board side, is filled exactly as an interior one is — the case
        /// <see cref="Board.HasIsolatedEmptyCells"/> misses.</summary>
        [Test]
        public void Apply_WithAnIslandOnTheLastRowAndColumn_FillsIt()
        {
            Board board = new Board();
            FillEntireBoard(board);
            OccupyRectangle(board, 2, 2, 4, 4, 1);
            // Un-occupy the "main" open area so it exists as somewhere for the pocket not to be.
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    board.Clear(new GridPosition(x, y));
                }
            }

            var pocket = new[]
            {
                new GridPosition(5, 7), new GridPosition(6, 7), new GridPosition(7, 7),
            };
            for (int i = 0; i < pocket.Length; i++)
            {
                board.Clear(pocket[i]);
            }

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Vortex));

            for (int i = 0; i < pocket.Length; i++)
            {
                Assert.IsTrue(board.IsOccupied(pocket[i]), $"{pocket[i]} should have been reclaimed.");
            }

            Assert.AreEqual(pocket.Length, _effect.FilledCells.Count);
        }

        /// <summary>Multiple islands present at once are all filled by the one trigger, not just the
        /// first found.</summary>
        [Test]
        public void Apply_WithTwoSeparateIslands_FillsBothInOneCall()
        {
            Board board = new Board();
            FillEntireBoard(board);
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    board.Clear(new GridPosition(x, y));
                }
            }

            GridPosition firstIsland = new GridPosition(0, 0);
            GridPosition secondIsland = new GridPosition(0, 7);
            board.Clear(firstIsland);
            board.Clear(secondIsland);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(2, 2), SpecialCellKind.Vortex));

            Assert.IsTrue(board.IsOccupied(firstIsland));
            Assert.IsTrue(board.IsOccupied(secondIsland));
            Assert.AreEqual(2, _effect.FilledCells.Count);
        }

        /// <summary>A filled cell's colour is always a real playable colour, never
        /// <see cref="Board.EMPTY"/> and never out of range.</summary>
        [Test]
        public void Apply_FillsWithAValidColourId()
        {
            Board board = new Board();
            OccupyRectangle(board, 2, 2, 4, 4, 1);
            GridPosition island = new GridPosition(3, 3);
            board.Clear(island);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Vortex));

            int colourId = board[island];
            Assert.GreaterOrEqual(colourId, 1);
            Assert.LessOrEqual(colourId, Board.COLOUR_COUNT);
        }

        /// <summary>AC5: a Reinforced cell is never a fill target — the scan only ever finds empty cells,
        /// and a reinforced cell is (by definition) occupied.</summary>
        [Test]
        public void Apply_NeverOverwritesAReinforcedCell()
        {
            Board board = new Board();
            OccupyRectangle(board, 2, 2, 4, 4, 1);
            GridPosition island = new GridPosition(3, 3);
            board.Clear(island);

            GridPosition reinforced = new GridPosition(0, 0);
            board.OccupyReinforced(reinforced, 2, hitCount: 3);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 1), SpecialCellKind.Vortex));

            Assert.AreEqual(2, board[reinforced], "Untouched colour.");
            Assert.AreEqual(3, board.GetHitCount(reinforced), "Untouched hit count.");
        }

        /// <summary>AC3/AC6: with no island, the tag hands off to a uniformly random occupied cell that
        /// carries no special kind of its own.</summary>
        [Test]
        public void Apply_WithNoIsland_HandsOffToAnEligibleOccupiedCell()
        {
            Board board = new Board();
            GridPosition eligible = new GridPosition(4, 4);
            board.Occupy(eligible, 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Vortex));

            Assert.AreEqual(0, _effect.FilledCells.Count, "No island existed to fill.");
            Assert.AreEqual(1, _effect.HandOffTargets.Count);
            Assert.AreEqual(eligible, _effect.HandOffTargets[0]);
            Assert.AreEqual(SpecialCellKind.Vortex, board.GetSpecialKind(eligible));
        }

        /// <summary>A cell that already carries a kind of its own is not an eligible hand-off target.</summary>
        [Test]
        public void Apply_WithNoIsland_NeverHandsOffToACellThatAlreadyHasAKind()
        {
            Board board = new Board();
            GridPosition alreadySpecial = new GridPosition(4, 4);
            board.Occupy(alreadySpecial, 1);
            board.SetSpecialKind(alreadySpecial, SpecialCellKind.Laser);

            GridPosition eligible = new GridPosition(0, 0);
            board.Occupy(eligible, 2);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(7, 7), SpecialCellKind.Vortex));

            Assert.AreEqual(1, _effect.HandOffTargets.Count);
            Assert.AreEqual(eligible, _effect.HandOffTargets[0]);
            Assert.AreEqual(SpecialCellKind.Laser, board.GetSpecialKind(alreadySpecial), "Left alone.");
        }

        /// <summary>AC6 (negative): with no island and no eligible cell either — nothing occupied at all
        /// — the hand-off is a no-op.</summary>
        [Test]
        public void Apply_WithNoIslandAndNoEligibleCell_IsANoOp()
        {
            Board board = new Board();

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Vortex));

            Assert.AreEqual(0, _effect.FilledCells.Count);
            Assert.AreEqual(0, _effect.HandOffTargets.Count);
            Assert.IsTrue(board.IsEmpty());
        }

        /// <summary>The same no-op when every occupied cell already carries its own kind.</summary>
        [Test]
        public void Apply_WithNoIslandAndEveryOccupiedCellAlreadySpecial_IsANoOp()
        {
            Board board = new Board();
            GridPosition onlyOccupied = new GridPosition(4, 4);
            board.Occupy(onlyOccupied, 1);
            board.SetSpecialKind(onlyOccupied, SpecialCellKind.Coin);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Vortex));

            Assert.AreEqual(0, _effect.FilledCells.Count);
            Assert.AreEqual(0, _effect.HandOffTargets.Count);
            Assert.AreEqual(SpecialCellKind.Coin, board.GetSpecialKind(onlyOccupied), "Left alone.");
        }

        /// <summary>A kind that is not a vortex is ignored, exactly as every other effect ignores one.</summary>
        [Test]
        public void Apply_WithAnotherKind_DoesNothing()
        {
            Board board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Laser));

            Assert.AreEqual(0, _effect.FilledCells.Count);
            Assert.AreEqual(0, _effect.HandOffTargets.Count);
        }

        [Test]
        public void BeginResolution_ForgetsThePreviousResolutionsWork()
        {
            Board board = new Board();
            OccupyRectangle(board, 2, 2, 4, 4, 1);
            board.Clear(new GridPosition(3, 3));
            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Vortex));
            Assert.AreEqual(1, _effect.FilledCells.Count);

            _effect.BeginResolution();

            Assert.AreEqual(0, _effect.FilledCells.Count);
            Assert.AreEqual(0, _effect.HandOffTargets.Count);
        }

        /// <summary>AC7: a fill that completes a line is not cleared by the effect itself — the cascade
        /// loop's next iteration is what catches it, exactly as every other effect's fill/move/destroy
        /// leaves a completed line standing for it.</summary>
        [Test]
        public void ResolveCascade_WhenAFillCompletesALine_ChainsIntoASecondPhase()
        {
            Board board = new Board();

            // Row 0 is full, so it clears first. Its cell (7, 0) carries the vortex.
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 0), 1);
            }

            board.SetSpecialKind(new GridPosition(7, 0), SpecialCellKind.Vortex);

            // Row 7 is one cell short of full, and that one cell — (7, 7) — is a one-cell island: boxed
            // in by (7, 6) above and the rest of row 7 to its left, with the board's own corner closing
            // it off on the other two sides. Placed on the bottom edge, deliberately, so removing it
            // leaves everything above as a single connected block rather than splitting the board in two.
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 7), 1);
            }

            board.Occupy(new GridPosition(7, 6), 1);

            CascadeClearResult result = CascadeClearResolver.ResolveCascade(board, _effect);

            Assert.AreEqual(1, result.Primary.LineCount, "Phase 0 is the row the placement completed.");
            Assert.AreEqual(1, result.CascadePhaseCount, "The fill completed row 7, which is phase 1.");
            Assert.AreEqual(2, result.TotalLineCount);
            Assert.IsFalse(board.IsOccupied(new GridPosition(7, 7)), "Filled, then cleared by the cascade.");
        }

        /// <summary>The fill never clears a line itself, even a completed one — that is the cascade
        /// loop's job, and doing it here would double-report it.</summary>
        [Test]
        public void Apply_NeverClearsAnything()
        {
            Board board = new Board();
            OccupyRectangle(board, 2, 2, 4, 4, 1);
            board.Clear(new GridPosition(3, 3));
            int occupiedBefore = board.OccupiedCellCount();

            _effect.Apply(board, new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Vortex));

            Assert.AreEqual(occupiedBefore + 1, board.OccupiedCellCount(), "A fill only ever adds blocks.");
        }

        private static void OccupyRectangle(Board board, int minX, int minY, int maxX, int maxY, int colourId)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    board.Occupy(new GridPosition(x, y), colourId);
                }
            }
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
    }
}
