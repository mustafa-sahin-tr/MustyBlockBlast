using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="PowerUpClearResolver"/> reporting the special cells its clears destroy, and
    /// doing so through the same <see cref="SpecialCellDetection"/> pass a placement's line clear uses
    /// — a special block must be destroyed identically whether a completed line or a Bomb took it out.
    /// Every kind is <see cref="SpecialCellKind.None"/> today, so a stub kind is the only way to
    /// exercise this.
    /// </summary>
    public class PowerUpClearResolverSpecialCellTests
    {
        /// <summary>Stands in for the first real special kind, which a later sub-issue names.</summary>
        private const SpecialCellKind StubKind = (SpecialCellKind)1;

        [Test]
        public void ResolveBombClear_OverASpecialCell_ReportsItAsTriggered()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);
            var specialPosition = new GridPosition(5, 5);
            board.SetSpecialKind(specialPosition, StubKind);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(4, 4));

            Assert.AreEqual(1, result.TriggeredSpecials.Count);
            Assert.AreEqual(specialPosition, result.TriggeredSpecials[0].Position);
            Assert.AreEqual(StubKind, result.TriggeredSpecials[0].Kind);
        }

        [Test]
        public void ResolveBombClear_SpecialCellOutsideTheBlast_IsNotTriggered()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);
            board.SetSpecialKind(new GridPosition(7, 7), StubKind);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(1, 1));

            Assert.AreEqual(0, result.TriggeredSpecials.Count);
            Assert.AreEqual(StubKind, board.GetSpecialKind(new GridPosition(7, 7)), "Untouched cells keep their kind.");
        }

        /// <summary>A power-up clear empties the cell, so the kind goes with it — the same reset a line
        /// clear performs.</summary>
        [Test]
        public void ResolveBombClear_ClearingASpecialCell_AlsoResetsItsKind()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);
            var specialPosition = new GridPosition(3, 3);
            board.SetSpecialKind(specialPosition, StubKind);

            PowerUpClearResolver.ResolveBombClear(board, specialPosition);

            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(specialPosition));
        }

        [Test]
        public void ResolveRowClear_WithNoSpecialCells_ReportsNoTriggers()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveRowClear(board, 2);

            Assert.IsTrue(result.AnyCleared);
            Assert.AreEqual(0, result.TriggeredSpecials.Count);
        }

        [Test]
        public void ResolveColumnClear_OverASpecialCell_ReportsItAsTriggered()
        {
            var board = new Board();
            FillBoard(board, colourId: 1);
            board.SetSpecialKind(new GridPosition(6, 2), StubKind);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColumnClear(board, 6);

            Assert.AreEqual(1, result.TriggeredSpecials.Count);
            Assert.AreEqual(new GridPosition(6, 2), result.TriggeredSpecials[0].Position);
        }

        [Test]
        public void ResolveColorCleanser_OverSpecialCellsOfTheClearedColour_ReportsEachOfThem()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), 1);
            board.Occupy(new GridPosition(1, 0), 1);
            board.Occupy(new GridPosition(2, 0), 2);
            board.SetSpecialKind(new GridPosition(1, 0), StubKind);
            board.SetSpecialKind(new GridPosition(2, 0), StubKind);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(board, new GridPosition(0, 0));

            Assert.AreEqual(1, result.TriggeredSpecials.Count, "Only the cleared colour's special cell is destroyed.");
            Assert.AreEqual(new GridPosition(1, 0), result.TriggeredSpecials[0].Position);
        }

        /// <summary>A rejected cleanser reads and writes nothing, so it cannot have triggered anything.</summary>
        [Test]
        public void ResolveColorCleanser_OnAnEmptyTarget_ReportsNoTriggers()
        {
            var board = new Board();
            board.SetSpecialKind(new GridPosition(4, 4), StubKind);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(board, new GridPosition(4, 4));

            Assert.IsFalse(result.AnyCleared);
            Assert.AreEqual(0, result.TriggeredSpecials.Count);
            Assert.AreEqual(StubKind, board.GetSpecialKind(new GridPosition(4, 4)));
        }

        /// <summary>
        /// The shared-detection claim, stated directly: the same special cell destroyed by a completed
        /// line and by a Bomb produces the same trigger, because both resolvers route through
        /// <see cref="SpecialCellDetection"/> rather than each deciding for itself what counts.
        /// </summary>
        [Test]
        public void ADestroyedSpecialCell_ReportsIdenticallyWhetherALineClearOrAPowerUpTookItOut()
        {
            var specialPosition = new GridPosition(3, 4);

            var lineClearBoard = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                lineClearBoard.Occupy(new GridPosition(x, 4), 1);
            }

            lineClearBoard.SetSpecialKind(specialPosition, StubKind);

            var recorder = new RecordingEffect();
            CascadeClearResolver.ResolveCascade(lineClearBoard, recorder);
            List<SpecialCellTrigger> lineClearTriggers = recorder.Triggers;

            var powerUpBoard = new Board();
            powerUpBoard.Occupy(specialPosition, 1);
            powerUpBoard.SetSpecialKind(specialPosition, StubKind);

            PowerUpClearResult powerUpResult = PowerUpClearResolver.ResolveBombClear(powerUpBoard, specialPosition);

            Assert.AreEqual(1, lineClearTriggers.Count);
            Assert.AreEqual(1, powerUpResult.TriggeredSpecials.Count);
            Assert.AreEqual(lineClearTriggers[0].Position, powerUpResult.TriggeredSpecials[0].Position);
            Assert.AreEqual(lineClearTriggers[0].Kind, powerUpResult.TriggeredSpecials[0].Kind);
        }

        /// <summary>Observes what the cascade loop detected without touching the board.</summary>
        private sealed class RecordingEffect : ISpecialCellEffect
        {
            public List<SpecialCellTrigger> Triggers { get; } = new List<SpecialCellTrigger>();

            public void Apply(Board board, SpecialCellTrigger trigger) => Triggers.Add(trigger);
        }

        private static void FillBoard(Board board, int colourId)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    board.Occupy(new GridPosition(x, y), colourId);
                }
            }
        }
    }
}
