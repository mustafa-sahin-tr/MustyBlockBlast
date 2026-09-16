using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the strike itself: how many cells it takes on either side of the
    /// <see cref="ChainLightningEffect.MAX_TARGETS_PER_STRIKE"/> boundary, that it never takes one twice
    /// or takes an empty one, that it draws from the stream it was handed (so a seeded run replays), and
    /// the chain into a second tile it caught — including that the chain terminates.
    /// </summary>
    public class ChainLightningEffectTests
    {
        private static readonly GridPosition Origin = new GridPosition(0, 0);

        /// <summary>AC2: a board with more blocks than the strike takes loses exactly the strike's worth.</summary>
        [Test]
        public void Apply_WithMoreOccupiedCellsThanTargets_VaporizesExactlyTheTargetCount()
        {
            var board = new Board();
            OccupyCells(board, 20);
            ChainLightningEffect effect = CreateEffect(seed: 1);

            effect.Apply(board, Trigger(Origin));

            Assert.AreEqual(ChainLightningEffect.MAX_TARGETS_PER_STRIKE, effect.VaporizedCells.Count);
            Assert.AreEqual(20 - ChainLightningEffect.MAX_TARGETS_PER_STRIKE, board.OccupiedCellCount());
        }

        /// <summary>The other branch of Min: a board holding fewer blocks than the strike wants loses all
        /// of them, and no attempt is made to pick a cell that is not there.</summary>
        [Test]
        public void Apply_WithFewerOccupiedCellsThanTargets_VaporizesAllOfThem()
        {
            var board = new Board();
            OccupyCells(board, 3);
            ChainLightningEffect effect = CreateEffect(seed: 1);

            effect.Apply(board, Trigger(Origin));

            Assert.AreEqual(3, effect.VaporizedCells.Count);
            Assert.AreEqual(0, board.OccupiedCellCount());
        }

        [Test]
        public void Apply_OnAnEmptyBoard_VaporizesNothingAndDoesNotThrow()
        {
            var board = new Board();
            ChainLightningEffect effect = CreateEffect(seed: 1);

            effect.Apply(board, Trigger(Origin));

            Assert.AreEqual(0, effect.VaporizedCells.Count);
        }

        /// <summary>No cell is taken twice: the partial shuffle is what guarantees it, and a repeat would
        /// quietly cost the strike one of its five targets.</summary>
        [Test]
        public void Apply_NeverVaporizesTheSameCellTwice()
        {
            var board = new Board();
            OccupyCells(board, 30);
            ChainLightningEffect effect = CreateEffect(seed: 7);

            effect.Apply(board, Trigger(Origin));

            var seen = new HashSet<GridPosition>();
            for (int i = 0; i < effect.VaporizedCells.Count; i++)
            {
                Assert.IsTrue(seen.Add(effect.VaporizedCells[i]), $"{effect.VaporizedCells[i]} taken twice.");
            }
        }

        /// <summary>Only occupied cells are candidates, so every reported cell held a block — an empty one
        /// is not "destroyed" and must not be scored or repainted as if it were.</summary>
        [Test]
        public void Apply_OnlyVaporizesCellsThatHeldABlock()
        {
            var board = new Board();
            OccupyCells(board, 8);
            ChainLightningEffect effect = CreateEffect(seed: 3);
            var occupiedBefore = new HashSet<GridPosition>();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (board.IsOccupied(position))
                    {
                        occupiedBefore.Add(position);
                    }
                }
            }

            effect.Apply(board, Trigger(Origin));

            for (int i = 0; i < effect.VaporizedCells.Count; i++)
            {
                Assert.IsTrue(
                    occupiedBefore.Contains(effect.VaporizedCells[i]),
                    $"{effect.VaporizedCells[i]} was never occupied.");
            }
        }

        /// <summary>The targets come from the stream the effect was handed, so two effects on identical
        /// boards with identically seeded streams take identical cells — which is what makes a seeded run
        /// replay.</summary>
        [Test]
        public void Apply_WithTheSameSeed_TakesTheSameCells()
        {
            var first = new Board();
            var second = new Board();
            OccupyCells(first, 30);
            OccupyCells(second, 30);

            ChainLightningEffect firstEffect = CreateEffect(seed: 42);
            ChainLightningEffect secondEffect = CreateEffect(seed: 42);

            firstEffect.Apply(first, Trigger(Origin));
            secondEffect.Apply(second, Trigger(Origin));

            CollectionAssert.AreEqual(firstEffect.VaporizedCells, secondEffect.VaporizedCells);
        }

        /// <summary>A trigger of another kind is not this effect's business; the composite hands every
        /// trigger to every effect, so ignoring the ones that are not its own is the whole dispatch.</summary>
        [Test]
        public void Apply_WithAnotherKindsTrigger_DoesNothing()
        {
            var board = new Board();
            OccupyCells(board, 20);
            ChainLightningEffect effect = CreateEffect(seed: 1);

            effect.Apply(board, new SpecialCellTrigger(Origin, SpecialCellKind.Vortex));

            Assert.AreEqual(0, effect.VaporizedCells.Count);
            Assert.AreEqual(20, board.OccupiedCellCount());
        }

        /// <summary>A tile caught in a strike fires in turn: with the whole board tagged, one trigger
        /// empties it entirely, five cells at a time, rather than stopping after the first five.</summary>
        [Test]
        public void Apply_WithChainLightningAmongTheTargets_FiresItInTurn()
        {
            var board = new Board();
            OccupyCells(board, 12);
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (board.IsOccupied(position))
                    {
                        board.SetSpecialKind(position, SpecialCellKind.ChainLightning);
                    }
                }
            }

            ChainLightningEffect effect = CreateEffect(seed: 5);

            effect.Apply(board, Trigger(Origin));

            Assert.AreEqual(0, board.OccupiedCellCount(), "Every tile caught fires, so the chain runs out of board.");
            Assert.AreEqual(12, effect.VaporizedCells.Count, "Each cell is reported exactly once.");
            Assert.IsFalse(effect.StoppedAtChainCap, "Twelve cells is three strikes — nowhere near the cap.");
        }

        /// <summary>The chain terminates on a board where every cell is a tile: the "never the same centre
        /// twice" guard bounds it at the cell count, and the cap bounds it again. Either way this returns
        /// rather than spinning.</summary>
        [Test]
        public void Apply_WithAFullBoardOfChainLightning_TerminatesAndLeavesTheBoardConsistent()
        {
            var board = new Board();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    board.Occupy(position, 1);
                    board.SetSpecialKind(position, SpecialCellKind.ChainLightning);
                }
            }

            ChainLightningEffect effect = CreateEffect(seed: 11);

            effect.Apply(board, Trigger(Origin));

            // 64 cells at five a strike is thirteen strikes, under the cap of twenty — so the board
            // empties. The point of the test is that it returns at all.
            Assert.AreEqual(0, board.OccupiedCellCount());
            Assert.AreEqual(Board.SIZE * Board.SIZE, effect.VaporizedCells.Count);
        }

        /// <summary>Each chained strike samples against the occupancy it finds, not against the board the
        /// chain started on: a board of six loses five and then the one that is left, never five twice.</summary>
        [Test]
        public void Apply_WhenChaining_SamplesAgainstTheShrinkingBoard()
        {
            var board = new Board();
            OccupyCells(board, 6);
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (board.IsOccupied(position))
                    {
                        board.SetSpecialKind(position, SpecialCellKind.ChainLightning);
                    }
                }
            }

            ChainLightningEffect effect = CreateEffect(seed: 2);

            effect.Apply(board, Trigger(Origin));

            Assert.AreEqual(6, effect.VaporizedCells.Count);
            Assert.AreEqual(0, board.OccupiedCellCount());
        }

        /// <summary>A destroyed tile's kind is gone with it: <c>Board.Clear</c> resets it, so a cell the
        /// strike emptied cannot fire a second time from a later trigger.</summary>
        [Test]
        public void Apply_ClearsTheSpecialKindOfEveryCellItTakes()
        {
            var board = new Board();
            OccupyCells(board, 5);
            ChainLightningEffect effect = CreateEffect(seed: 1);

            effect.Apply(board, Trigger(Origin));

            for (int i = 0; i < effect.VaporizedCells.Count; i++)
            {
                Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(effect.VaporizedCells[i]));
            }
        }

        /// <summary>The buffer means "this resolution", not "every resolution since the effect was
        /// built" — without the reset, Presentation would sweep cells that vanished a move ago.</summary>
        [Test]
        public void BeginResolution_ForgetsThePreviousResolutionsCells()
        {
            var board = new Board();
            OccupyCells(board, 20);
            ChainLightningEffect effect = CreateEffect(seed: 1);

            effect.Apply(board, Trigger(Origin));
            Assert.AreEqual(ChainLightningEffect.MAX_TARGETS_PER_STRIKE, effect.VaporizedCells.Count);

            effect.BeginResolution();

            Assert.AreEqual(0, effect.VaporizedCells.Count);
        }

        [Test]
        public void Constructor_WithNoRandomStream_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ChainLightningEffect(null));
        }

        private static ChainLightningEffect CreateEffect(int seed)
        {
            var effect = new ChainLightningEffect(new Random(seed));
            effect.BeginResolution();
            return effect;
        }

        private static SpecialCellTrigger Trigger(GridPosition position)
            => new SpecialCellTrigger(position, SpecialCellKind.ChainLightning);

        /// <summary>Fills the first <paramref name="count"/> cells of the board in scan order. The
        /// trigger's own cell may be among them, which is harmless: the effect never treats the trigger
        /// position as anything but a chain centre it has already fired, so an occupied one is simply one
        /// more candidate rather than a second strike.</summary>
        private static void OccupyCells(Board board, int count)
        {
            int filled = 0;
            for (int y = 0; y < Board.SIZE && filled < count; y++)
            {
                for (int x = 0; x < Board.SIZE && filled < count; x++)
                {
                    board.Occupy(new GridPosition(x, y), 1);
                    filled++;
                }
            }
        }
    }
}
