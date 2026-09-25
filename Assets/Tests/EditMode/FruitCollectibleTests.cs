using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #484: fruit goals ride the diamond path — a fruit is a collectible id stored per cell like a
    /// gem colour, dealt on pieces by the same decorator (Path only), and counted by the same tally on
    /// every destruction path; a FruitsCollected objective advances by its fruit's count.
    /// </summary>
    public sealed class FruitCollectibleTests
    {
        private const int SEED = 7;
        private const int DRAWS = 400;

        private static readonly int Pomegranate = Collectibles.FruitId(FruitKind.Pomegranate);
        private static readonly int Avocado = Collectibles.FruitId(FruitKind.Avocado);

        private static readonly Piece Square = new Piece(
            "test_square",
            new[] { new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(0, 1), new GridPosition(1, 1) });

        [Test]
        public void FruitIds_AreClearOfTheDiamondColours_AndRoundTripTheirKind()
        {
            for (int fruitIndex = 0; fruitIndex < Collectibles.FRUIT_COUNT; fruitIndex++)
            {
                int id = Collectibles.FruitId((FruitKind)fruitIndex);
                Assert.IsTrue(Collectibles.IsFruit(id));
                Assert.IsFalse(Collectibles.IsDiamond(id));
                Assert.AreEqual((FruitKind)fruitIndex, Collectibles.FruitOf(id));
                Assert.Less(id, Collectibles.TALLY_LENGTH);
            }

            Assert.IsFalse(Collectibles.IsFruit(Board.COLOUR_COUNT));
        }

        [Test]
        public void DiamondClearEffect_CountsADestroyedFruitByItsId()
        {
            var triggers = new List<SpecialCellTrigger>
            {
                new SpecialCellTrigger(new GridPosition(0, 0), SpecialCellKind.Diamond, ClearAxis.Row, 0, Pomegranate),
                new SpecialCellTrigger(new GridPosition(1, 0), SpecialCellKind.Diamond, ClearAxis.Row, 0, Pomegranate),
                new SpecialCellTrigger(new GridPosition(2, 0), SpecialCellKind.Diamond, ClearAxis.Row, 0, 2),
            };

            int[] tally = DiamondClearEffect.CountDestroyedByColour(triggers);

            Assert.AreEqual(2, tally[Pomegranate]);
            Assert.AreEqual(1, tally[2], "Diamonds are still counted by their colour.");
        }

        [Test]
        public void FruitsCollected_AdvancesByItsOwnFruitOnly()
        {
            var progress = new ObjectiveProgress(new ObjectiveDefinition(
                "fruit", ObjectiveType.FruitsCollected, ObjectiveScope.PerRun, 10, requiredColourId: Pomegranate));
            var tally = new int[Collectibles.TALLY_LENGTH];
            tally[Pomegranate] = 3;
            tally[Avocado] = 5;

            Assert.IsTrue(progress.ApplyPowerUpDiamondsCleared(tally));
            Assert.AreEqual(3, progress.CurrentValue);
        }

        [Test]
        public void FruitsCollected_WithANonFruitId_IsRefused()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ObjectiveDefinition(
                "fruit", ObjectiveType.FruitsCollected, ObjectiveScope.PerRun, 10, requiredColourId: 2));
        }

        [Test]
        public void Board_StoresAFruitOnACell_AndACloneCarriesIt()
        {
            var board = new Board();
            var position = new GridPosition(2, 2);
            board.Occupy(position, 1);
            board.SetSpecialKind(position, SpecialCellKind.Diamond);
            board.SetDiamondColourId(position, Avocado);

            Assert.AreEqual(Avocado, board.Clone().GetDiamondColourId(position));
        }

        [Test]
        public void Decorator_InPathMode_DealsTheGoalsFruitOnPieces()
        {
            DiamondPieceDecorator decorator = ADecorator(GameMode.Path);
            var buffer = new int[Square.CellCount];
            bool sawFruit = false;
            for (int draw = 0; draw < DRAWS && !sawFruit; draw++)
            {
                if (!decorator.TryDecorate(Square, buffer))
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < Square.CellCount; cellIndex++)
                {
                    Assert.IsTrue(buffer[cellIndex] == 0 || buffer[cellIndex] == Pomegranate);
                    sawFruit |= buffer[cellIndex] == Pomegranate;
                }
            }

            Assert.IsTrue(sawFruit);
        }

        /// <summary>AC8: outside Path mode no fruit is ever dealt.</summary>
        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void Decorator_OutsidePathMode_DealsNoFruit(GameMode mode)
        {
            DiamondPieceDecorator decorator = ADecorator(mode);
            var buffer = new int[Square.CellCount];
            for (int draw = 0; draw < DRAWS; draw++)
            {
                Assert.IsFalse(decorator.TryDecorate(Square, buffer));
            }
        }

        private static DiamondPieceDecorator ADecorator(GameMode mode)
        {
            var objectiveModel = new ObjectiveModel();
            objectiveModel.SetCurrentObjective(new ObjectiveProgress(new ObjectiveDefinition(
                "fruit", ObjectiveType.FruitsCollected, ObjectiveScope.PerRun, 10, requiredColourId: Pomegranate)));
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = mode;
            return new DiamondPieceDecorator(objectiveModel, gameModeModel, SEED);
        }
    }
}
