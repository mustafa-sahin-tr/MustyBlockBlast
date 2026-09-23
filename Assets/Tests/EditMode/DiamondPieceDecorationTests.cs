using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Cover for the diamond mechanic's Gameplay slice (issue #394): per-slot decoration on
    /// <see cref="TrayModel"/>, the draw-time <see cref="DiamondPieceDecorator"/> and its gate, and
    /// <see cref="BoardSystem"/> carrying a decorated piece's gems onto the board at placement.
    /// <para>
    /// The decorator is driven with a fixed seed throughout, so every "over N draws" assertion is a
    /// deterministic replay rather than a probabilistic one.
    /// </para>
    /// </summary>
    public class DiamondPieceDecorationTests
    {
        private const int BLOCK_COLOUR = 3;
        private const int RED = 1;
        private const int BLUE = 2;
        private const int SEED = 7;
        private const int MANY_DRAWS = 400;

        private static readonly Piece Single = PieceCatalog.SingleCell;
        private static readonly Piece Domino = new Piece("test_domino", new[] { new GridPosition(0, 0), new GridPosition(1, 0) });
        private static readonly Piece Square = new Piece(
            "test_square",
            new[] { new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(0, 1), new GridPosition(1, 1) });

        // --- TrayModel storage (AC1) ---

        [Test]
        public void SetSlot_WithADecoration_StoresItPerOffsetAndZeroesTheRest()
        {
            var tray = new TrayModel();

            tray.SetSlot(0, Square, BLOCK_COLOUR, SpecialPieceKind.None, new[] { 0, RED, 0, BLUE });

            Assert.IsTrue(tray.HasDiamonds(0));
            Assert.AreEqual(TrayModel.NO_DIAMOND, tray.GetDiamondColourId(0, 0));
            Assert.AreEqual(RED, tray.GetDiamondColourId(0, 1));
            Assert.AreEqual(TrayModel.NO_DIAMOND, tray.GetDiamondColourId(0, 2));
            Assert.AreEqual(BLUE, tray.GetDiamondColourId(0, 3));
            Assert.AreEqual(TrayModel.NO_DIAMOND, tray.GetDiamondColourId(0, 4), "Past the piece is never a gem.");
            Assert.IsFalse(tray.HasDiamonds(1), "Other slots are untouched.");
        }

        [Test]
        public void SetSlot_CopiesTheDecoration_SoTheCallersBufferCanBeReused()
        {
            var tray = new TrayModel();
            int[] buffer = { RED, 0 };

            tray.SetSlot(0, Domino, BLOCK_COLOUR, SpecialPieceKind.None, buffer);
            buffer[0] = BLUE;

            Assert.AreEqual(RED, tray.GetDiamondColourId(0, 0));
        }

        [Test]
        public void SetSlot_WithoutADecoration_AfterADecoratedPiece_LeavesNoStaleGem()
        {
            var tray = new TrayModel();
            tray.SetSlot(0, Square, BLOCK_COLOUR, SpecialPieceKind.None, new[] { RED, RED, RED, 0 });

            tray.SetSlot(0, Domino, BLOCK_COLOUR);

            Assert.IsFalse(tray.HasDiamonds(0));
            Assert.AreEqual(TrayModel.NO_DIAMOND, tray.GetDiamondColourId(0, 0));
            Assert.AreEqual(TrayModel.NO_DIAMOND, tray.GetDiamondColourId(0, 2), "A shorter piece hides nothing behind it.");
        }

        [Test]
        public void ConsumeSlot_DropsTheDecorationWithThePiece()
        {
            var tray = new TrayModel();
            tray.SetSlot(1, Domino, BLOCK_COLOUR, SpecialPieceKind.None, new[] { RED, 0 });

            tray.ConsumeSlot(1);

            Assert.IsFalse(tray.HasDiamonds(1));
            Assert.AreEqual(TrayModel.NO_DIAMOND, tray.GetDiamondColourId(1, 0));
        }

        [Test]
        public void SetHeld_CarriesTheDecorationIntoThePocket()
        {
            var tray = new TrayModel();

            tray.SetHeld(Domino, BLOCK_COLOUR, SpecialPieceKind.None, new[] { 0, BLUE });

            Assert.IsTrue(tray.HasHeldDiamonds);
            Assert.AreEqual(BLUE, tray.GetHeldDiamondColourId(1));
            Assert.AreEqual(TrayModel.NO_DIAMOND, tray.GetHeldDiamondColourId(0));

            tray.ClearHold();
            Assert.IsFalse(tray.HasHeldDiamonds);
        }

        // --- The gate (AC4) ---

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void TryDecorate_OutsidePathMode_NeverDecoratesEvenWithADiamondObjective(GameMode mode)
        {
            DiamondPieceDecorator decorator = ADecorator(mode, DiamondObjective(RED));

            Assert.IsFalse(decorator.IsActive);
            Assert.AreEqual(0, CountDecoratedDraws(decorator, Square, MANY_DRAWS));
        }

        [Test]
        public void TryDecorate_InPathModeWithoutADiamondObjective_NeverDecorates()
        {
            var colourObjective = new ObjectiveProgress(new ObjectiveDefinition(
                "colour", ObjectiveType.ColourCleared, ObjectiveScope.PerRun, 5, requiredColourId: RED));
            DiamondPieceDecorator decorator = ADecorator(GameMode.Path, colourObjective);

            Assert.IsFalse(decorator.IsActive);
            Assert.AreEqual(0, CountDecoratedDraws(decorator, Square, MANY_DRAWS));
        }

        [Test]
        public void TryDecorate_InPathModeWithNoObjectiveAtAll_NeverDecorates()
        {
            DiamondPieceDecorator decorator = ADecorator(GameMode.Path);

            Assert.AreEqual(0, CountDecoratedDraws(decorator, Square, MANY_DRAWS));
        }

        [Test]
        public void TryDecorate_InPathModeWithADiamondObjective_DecoratesSomeDraws()
        {
            DiamondPieceDecorator decorator = ADecorator(GameMode.Path, DiamondObjective(RED));

            Assert.IsTrue(decorator.IsActive);
            Assert.Greater(CountDecoratedDraws(decorator, Square, MANY_DRAWS), 0);
        }

        /// <summary>The "~20%" of #390, pinned loosely: a fixed seed makes this a replay, and the band
        /// is wide enough that only a broken roll (always/never/inverted) could fall outside it.</summary>
        [Test]
        public void TryDecorate_DecoratesRoughlyOneDrawInFive()
        {
            DiamondPieceDecorator decorator = ADecorator(GameMode.Path, DiamondObjective(RED));

            const int draws = 2000;
            double share = CountDecoratedDraws(decorator, Square, draws) / (double)draws;

            Assert.That(share, Is.InRange(0.12, 0.28), $"Decorated share was {share:P1}.");
        }

        /// <summary>The gate is read live: the same decorator turns off when the objective set changes
        /// under it, which is what happens when the player moves from a diamond level to another.</summary>
        [Test]
        public void TryDecorate_ReadsTheObjectiveSetLive()
        {
            var objectiveModel = new ObjectiveModel();
            objectiveModel.SetCurrentObjective(DiamondObjective(RED));
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Path;
            var decorator = new DiamondPieceDecorator(objectiveModel, gameModeModel, SEED);
            Assert.Greater(CountDecoratedDraws(decorator, Square, MANY_DRAWS), 0, "Sanity.");

            objectiveModel.SetCurrentObjective(null);

            Assert.AreEqual(0, CountDecoratedDraws(decorator, Square, MANY_DRAWS));
        }

        // --- The roll (AC2) ---

        [Test]
        public void TryDecorate_NeverDecoratesAOneCellPiece()
        {
            DiamondPieceDecorator decorator = ADecorator(GameMode.Path, DiamondObjective(RED));

            Assert.AreEqual(0, CountDecoratedDraws(decorator, Single, MANY_DRAWS));
        }

        [Test]
        public void TryDecorate_OnEveryCatalogPiece_KeepsTheCountBetweenOneAndCellCountMinusOne()
        {
            DiamondPieceDecorator decorator = ADecorator(GameMode.Path, DiamondObjective(RED));
            var buffer = new int[16];
            IReadOnlyList<Piece> catalog = PieceCatalog.AllPieces;

            for (int pieceIndex = 0; pieceIndex < catalog.Count; pieceIndex++)
            {
                Piece piece = catalog[pieceIndex];
                int decoratedDraws = 0;
                for (int draw = 0; draw < 100; draw++)
                {
                    if (!decorator.TryDecorate(piece, buffer))
                    {
                        AssertAllZero(buffer, piece);
                        continue;
                    }

                    decoratedDraws++;
                    int count = CountGems(buffer, piece.CellCount);
                    Assert.That(count, Is.InRange(1, piece.CellCount - 1), $"{piece.Id} had {count} gems.");
                    for (int offsetIndex = piece.CellCount; offsetIndex < buffer.Length; offsetIndex++)
                    {
                        Assert.AreEqual(0, buffer[offsetIndex], $"{piece.Id}: gem past the piece.");
                    }
                }

                if (piece.CellCount > 1)
                {
                    Assert.Greater(decoratedDraws, 0, $"{piece.Id} was never decorated in 100 draws.");
                }
                else
                {
                    Assert.AreEqual(0, decoratedDraws, $"{piece.Id} is a 1x1 and must never be decorated.");
                }
            }
        }

        [Test]
        public void TryDecorate_UsesOnlyTheColoursTheDiamondObjectivesName()
        {
            var objectiveModel = new ObjectiveModel();
            objectiveModel.SetObjectives(new[] { DiamondObjective(RED), DiamondObjective(BLUE) });
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Path;
            var decorator = new DiamondPieceDecorator(objectiveModel, gameModeModel, SEED);

            var buffer = new int[Square.CellCount];
            bool sawRed = false;
            bool sawBlue = false;
            for (int draw = 0; draw < MANY_DRAWS; draw++)
            {
                if (!decorator.TryDecorate(Square, buffer))
                {
                    continue;
                }

                for (int offsetIndex = 0; offsetIndex < buffer.Length; offsetIndex++)
                {
                    int colour = buffer[offsetIndex];
                    if (colour == TrayModel.NO_DIAMOND)
                    {
                        continue;
                    }

                    Assert.That(colour, Is.EqualTo(RED).Or.EqualTo(BLUE), "A gem colour no objective asked for.");
                    sawRed |= colour == RED;
                    sawBlue |= colour == BLUE;
                }
            }

            Assert.IsTrue(sawRed && sawBlue, "Both named colours should come up over many draws.");
        }

        [Test]
        public void TryDecorate_WithABufferSmallerThanThePiece_IsRefused()
        {
            DiamondPieceDecorator decorator = ADecorator(GameMode.Path, DiamondObjective(RED));

            Assert.Throws<System.ArgumentException>(() => decorator.TryDecorate(Square, new int[2]));
        }

        // --- Placement (AC5) and the reinforced/timer rule (AC3) ---

        [Test]
        public void TryPlacePiece_WithADecoratedPiece_WritesDiamondsOnlyOntoTheMarkedCells()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel);
            trayModel.SetSlot(0, Square, BLOCK_COLOUR, SpecialPieceKind.None, new[] { 0, RED, BLUE, 0 });
            var anchor = new GridPosition(2, 2);

            Assert.IsTrue(system.TryPlacePiece(0, anchor));

            Board board = boardModel.Board;
            for (int offsetIndex = 0; offsetIndex < Square.CellCount; offsetIndex++)
            {
                GridPosition position = anchor + Square.Offsets[offsetIndex];
                Assert.AreEqual(BLOCK_COLOUR, board[position], "Every cell is a block of the piece's colour.");
            }

            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(anchor + Square.Offsets[0]));
            Assert.AreEqual(SpecialCellKind.Diamond, board.GetSpecialKind(anchor + Square.Offsets[1]));
            Assert.AreEqual(RED, board.GetDiamondColourId(anchor + Square.Offsets[1]));
            Assert.AreEqual(SpecialCellKind.Diamond, board.GetSpecialKind(anchor + Square.Offsets[2]));
            Assert.AreEqual(BLUE, board.GetDiamondColourId(anchor + Square.Offsets[2]));
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(anchor + Square.Offsets[3]));
            Assert.AreEqual(0, board.GetDiamondColourId(anchor + Square.Offsets[3]));
            Assert.IsFalse(trayModel.HasDiamonds(0), "The consumed slot keeps nothing.");
        }

        [Test]
        public void TryPlacePiece_WithAnUndecoratedPiece_PlacesOrdinaryCellsExactlyAsBefore()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel);
            trayModel.SetSlot(0, Square, BLOCK_COLOUR);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            for (int offsetIndex = 0; offsetIndex < Square.CellCount; offsetIndex++)
            {
                GridPosition position = Square.Offsets[offsetIndex];
                Assert.AreEqual(BLOCK_COLOUR, boardModel.Board[position]);
                Assert.AreEqual(SpecialCellKind.None, boardModel.Board.GetSpecialKind(position));
                Assert.AreEqual(0, boardModel.Board.GetDiamondColourId(position));
            }
        }

        [Test]
        public void TryPlacePiece_WithADecoratedPiece_ClearingALine_CountsTheGemsOnTheMessage()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel, out TestMessageBroker<PiecePlacedMessage> placed);
            for (int x = 0; x < Board.SIZE - 2; x++)
            {
                boardModel.Occupy(new GridPosition(x, 0), BLOCK_COLOUR);
            }

            trayModel.SetSlot(0, Domino, BLOCK_COLOUR, SpecialPieceKind.None, new[] { RED, RED });

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 2, 0)));

            Assert.AreEqual(2, placed.Published[0].DestroyedDiamondCountByColour[RED]);
        }

        [Test]
        public void TryPlacePiece_WithADecoratedPiece_LeavesNeighbouringReinforcedAndTimerCellsUntouched()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel);
            var reinforced = new GridPosition(2, 0);
            var timer = new GridPosition(0, 1);
            boardModel.OccupyReinforced(reinforced, BLOCK_COLOUR, hitCount: 2, skin: 0);
            boardModel.OccupyTimer(timer, BLOCK_COLOUR, startingCountdown: 5);
            trayModel.SetSlot(0, Domino, BLOCK_COLOUR, SpecialPieceKind.None, new[] { RED, RED });

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            Board board = boardModel.Board;
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(reinforced));
            Assert.AreEqual(2, board.GetHitCount(reinforced));
            Assert.AreEqual(0, board.GetDiamondColourId(reinforced));
            Assert.AreEqual(SpecialCellKind.Timer, board.GetSpecialKind(timer));
            Assert.AreEqual(4, board.GetTimerCountdown(timer), "Only the per-placement tick touched it.");
            Assert.AreEqual(0, board.GetDiamondColourId(timer));
            Assert.AreEqual(SpecialCellKind.Diamond, board.GetSpecialKind(new GridPosition(0, 0)));
            Assert.AreEqual(SpecialCellKind.Diamond, board.GetSpecialKind(new GridPosition(1, 0)));
        }

        [Test]
        public void CanCarryDiamond_IsFalseOnReinforcedAndTimerCells_AndTrueOnEmptyOnes()
        {
            var board = new Board();
            var reinforced = new GridPosition(1, 1);
            var timer = new GridPosition(2, 2);
            var empty = new GridPosition(3, 3);
            board.OccupyReinforced(reinforced, BLOCK_COLOUR, hitCount: 2, skin: 0);
            board.OccupyTimer(timer, BLOCK_COLOUR, startingCountdown: 3);

            Assert.IsFalse(DiamondCellRules.CanCarryDiamond(board, reinforced));
            Assert.IsFalse(DiamondCellRules.CanCarryDiamond(board, timer));
            Assert.IsTrue(DiamondCellRules.CanCarryDiamond(board, empty));
        }

        [Test]
        public void CanCarryDiamond_IsFalseOnEveryOtherSpecialKindToo()
        {
            var board = new Board();
            var position = new GridPosition(4, 4);
            board.Occupy(position, BLOCK_COLOUR);
            board.SetSpecialKind(position, SpecialCellKind.ScoreGem);

            Assert.IsFalse(DiamondCellRules.CanCarryDiamond(board, position));
        }

        // --- The System end to end: the deal (AC2/AC4 through BoardSystem) ---

        [Test]
        public void StartNewRun_InPathModeWithADiamondObjective_DealsDecoratedPiecesEventually()
        {
            BoardSystem system = CreateSystem(
                out _, out TrayModel trayModel, out _,
                ADecorator(GameMode.Path, DiamondObjective(RED)));

            Assert.IsTrue(AnySlotDecoratedOverRuns(system, trayModel, runs: 60));
        }

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void StartNewRun_OutsidePathMode_NeverDealsADecoratedPiece(GameMode mode)
        {
            BoardSystem system = CreateSystem(
                out _, out TrayModel trayModel, out _,
                ADecorator(mode, DiamondObjective(RED)));

            Assert.IsFalse(AnySlotDecoratedOverRuns(system, trayModel, runs: 60));
        }

        [Test]
        public void StartNewRun_WithoutADecorator_NeverDealsADecoratedPiece()
        {
            BoardSystem system = CreateSystem(out _, out TrayModel trayModel);

            Assert.IsFalse(AnySlotDecoratedOverRuns(system, trayModel, runs: 20));
        }

        [Test]
        public void StartNewRun_WithADecorator_NeverDecoratesAOneCellPieceOrEveryCellOfAPiece()
        {
            BoardSystem system = CreateSystem(
                out _, out TrayModel trayModel, out _,
                ADecorator(GameMode.Path, DiamondObjective(RED)));

            for (int run = 0; run < 60; run++)
            {
                system.StartNewRun();
                for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
                {
                    Piece piece = trayModel.GetPiece(slotIndex);
                    int gems = 0;
                    for (int offsetIndex = 0; offsetIndex < piece.CellCount; offsetIndex++)
                    {
                        if (trayModel.GetDiamondColourId(slotIndex, offsetIndex) != TrayModel.NO_DIAMOND)
                        {
                            gems++;
                        }
                    }

                    if (piece.CellCount == 1)
                    {
                        Assert.AreEqual(0, gems);
                    }
                    else
                    {
                        Assert.Less(gems, piece.CellCount, $"{piece.Id} had every cell decorated.");
                    }
                }
            }
        }

        // --- Carrying the decoration: Hold and Rotate ---

        [Test]
        public void TryParkPiece_CarriesTheDecorationIntoThePocketAndBackOut()
        {
            BoardSystem system = CreateSystem(out _, out TrayModel trayModel);
            trayModel.SetSlot(0, Square, BLOCK_COLOUR, SpecialPieceKind.None, new[] { 0, RED, 0, BLUE });
            trayModel.SetSlot(1, Domino, BLOCK_COLOUR);

            Assert.IsTrue(system.TryParkPiece(0));
            Assert.IsTrue(trayModel.HasHeldDiamonds);
            Assert.AreEqual(RED, trayModel.GetHeldDiamondColourId(1));
            Assert.AreEqual(BLUE, trayModel.GetHeldDiamondColourId(3));
            Assert.IsFalse(trayModel.HasDiamonds(0), "The vacated slot is empty and plain.");

            // Swap the plain domino in: the decorated square comes back to the dock intact.
            Assert.IsTrue(system.TryParkPiece(1));
            Assert.AreSame(Square, trayModel.GetPiece(1));
            Assert.IsTrue(trayModel.HasDiamonds(1));
            Assert.AreEqual(RED, trayModel.GetDiamondColourId(1, 1));
            Assert.AreEqual(BLUE, trayModel.GetDiamondColourId(1, 3));
            Assert.IsFalse(trayModel.HasHeldDiamonds, "The plain domino carried nothing into the pocket.");
        }

        [Test]
        public void MapCellValuesClockwise_KeepsEachGemOnTheCellItWasOn()
        {
            Piece source = FindCatalogPiece("line_h2");
            Assert.IsTrue(PieceRotator.TryRotateClockwise(source, out Piece rotated));
            int sourceGemIndex = IndexOf(source.Offsets, new GridPosition(1, 0));
            var sourceValues = new int[source.CellCount];
            sourceValues[sourceGemIndex] = RED;
            var rotatedValues = new int[rotated.CellCount];

            PieceRotator.MapCellValuesClockwise(source, rotated, sourceValues, rotatedValues);

            // (1,0) turned clockwise about a 2x1 line lands on (0,0) of the vertical 1x2.
            int expectedIndex = IndexOf(rotated.Offsets, new GridPosition(0, 0));
            Assert.AreEqual(RED, rotatedValues[expectedIndex]);
            Assert.AreEqual(1, CountGems(rotatedValues, rotated.CellCount));
        }

        [Test]
        public void MapCellValuesClockwise_FourTurns_ReturnsTheDecorationToWhereItStarted()
        {
            Piece source = FindCatalogPiece("corner3_bl");
            var values = new int[source.CellCount];
            values[0] = RED;
            values[2] = BLUE;
            int[] original = (int[])values.Clone();
            Piece current = source;
            var next = new int[source.CellCount];

            for (int turn = 0; turn < 4; turn++)
            {
                Assert.IsTrue(PieceRotator.TryRotateClockwise(current, out Piece rotated));
                PieceRotator.MapCellValuesClockwise(current, rotated, values, next);
                System.Array.Copy(next, values, values.Length);
                current = rotated;
            }

            Assert.AreSame(source, current);
            CollectionAssert.AreEqual(original, values);
        }

        // --- Helpers ---

        private static ObjectiveProgress DiamondObjective(int colourId)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                $"diamonds_{colourId}", ObjectiveType.DiamondsCleared, ObjectiveScope.PerRun, 10,
                requiredColourId: colourId));
        }

        private static DiamondPieceDecorator ADecorator(GameMode mode, ObjectiveProgress objective = null)
        {
            var objectiveModel = new ObjectiveModel();
            objectiveModel.SetCurrentObjective(objective);
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = mode;
            return new DiamondPieceDecorator(objectiveModel, gameModeModel, SEED);
        }

        private static int CountDecoratedDraws(DiamondPieceDecorator decorator, Piece piece, int draws)
        {
            var buffer = new int[piece.CellCount];
            int decorated = 0;
            for (int draw = 0; draw < draws; draw++)
            {
                if (decorator.TryDecorate(piece, buffer))
                {
                    decorated++;
                    int count = CountGems(buffer, piece.CellCount);
                    Assert.That(count, Is.InRange(1, piece.CellCount - 1));
                }
                else
                {
                    AssertAllZero(buffer, piece);
                }
            }

            return decorated;
        }

        private static bool AnySlotDecoratedOverRuns(BoardSystem system, TrayModel trayModel, int runs)
        {
            for (int run = 0; run < runs; run++)
            {
                system.StartNewRun();
                for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
                {
                    if (trayModel.HasDiamonds(slotIndex))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int CountGems(int[] buffer, int cellCount)
        {
            int count = 0;
            for (int offsetIndex = 0; offsetIndex < cellCount; offsetIndex++)
            {
                if (buffer[offsetIndex] != TrayModel.NO_DIAMOND)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertAllZero(int[] buffer, Piece piece)
        {
            for (int offsetIndex = 0; offsetIndex < buffer.Length; offsetIndex++)
            {
                Assert.AreEqual(0, buffer[offsetIndex], $"{piece.Id}: a plain draw left a gem behind.");
            }
        }

        private static Piece FindCatalogPiece(string id)
        {
            IReadOnlyList<Piece> catalog = PieceCatalog.AllPieces;
            for (int pieceIndex = 0; pieceIndex < catalog.Count; pieceIndex++)
            {
                if (catalog[pieceIndex].Id == id)
                {
                    return catalog[pieceIndex];
                }
            }

            Assert.Fail($"No catalog piece '{id}'.");
            return null;
        }

        private static int IndexOf(IReadOnlyList<GridPosition> offsets, GridPosition offset)
        {
            for (int offsetIndex = 0; offsetIndex < offsets.Count; offsetIndex++)
            {
                if (offsets[offsetIndex].Equals(offset))
                {
                    return offsetIndex;
                }
            }

            return -1;
        }

        private static BoardSystem CreateSystem(out BoardModel boardModel, out TrayModel trayModel)
            => CreateSystem(out boardModel, out trayModel, out _);

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<PiecePlacedMessage> placedBroker,
            DiamondPieceDecorator decorator = null)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            placedBroker = new TestMessageBroker<PiecePlacedMessage>();

            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: SEED),
                new TestMessageBroker<RunStartedMessage>(),
                placedBroker,
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: SEED,
                diamondPieceDecorator: decorator);
        }
    }
}
