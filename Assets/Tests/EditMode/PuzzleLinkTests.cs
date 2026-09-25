using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #483: a puzzle-link group goes only if every member is hit in the same resolution — then the
    /// whole group is destroyed and pays a bonus — and a partial hit removes none of it.
    /// </summary>
    public sealed class PuzzleLinkTests
    {
        private const int COLOUR = 4;
        private const int GROUP = 1;

        private static readonly Piece VerticalDomino =
            new Piece("test_vdomino", new[] { new GridPosition(0, 0), new GridPosition(0, 1) });

        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        [Test]
        public void ARowThroughOnePieceOfAVerticalPair_ClearsAroundIt_AndLeavesBothPieces()
        {
            var board = new Board();
            var bottom = new GridPosition(3, 3);
            var top = new GridPosition(3, 4);
            board.OccupyPuzzleLink(bottom, COLOUR, GROUP);
            board.OccupyPuzzleLink(top, COLOUR, GROUP);
            FillRow(board, 3);

            LineClearResult result = LineClearResolver.ResolveClears(board);
            int removed = board.ResolvePuzzleLinks(new List<GridPosition>());

            Assert.AreEqual(1, result.LineCount);
            Assert.AreEqual(Board.SIZE - 1, result.ClearedCellCount, "The link is no destroyed cell.");
            Assert.AreEqual(0, removed);
            Assert.AreEqual(SpecialCellKind.PuzzleLink, board.GetSpecialKind(bottom));
            Assert.AreEqual(SpecialCellKind.PuzzleLink, board.GetSpecialKind(top));
            Assert.IsFalse(board.IsOccupied(new GridPosition(0, 3)));
        }

        [Test]
        public void TwoRowsAtOnceThroughBothPieces_RemoveTheWholeGroup()
        {
            var board = new Board();
            var bottom = new GridPosition(3, 3);
            var top = new GridPosition(3, 4);
            board.OccupyPuzzleLink(bottom, COLOUR, GROUP);
            board.OccupyPuzzleLink(top, COLOUR, GROUP);
            FillRow(board, 3);
            FillRow(board, 4);

            LineClearResolver.ResolveClears(board);
            var removedCells = new List<GridPosition>();
            int removed = board.ResolvePuzzleLinks(removedCells);

            Assert.AreEqual(2, removed);
            CollectionAssert.AreEquivalent(new[] { bottom, top }, removedCells);
            Assert.IsFalse(board.IsOccupied(bottom));
            Assert.IsFalse(board.IsOccupied(top));
            Assert.AreEqual(0, board.GetPuzzleGroupId(bottom));
        }

        [Test]
        public void HitsInTwoSeparateResolutions_NeverAddUpToAGroup()
        {
            var board = new Board();
            var bottom = new GridPosition(3, 3);
            var top = new GridPosition(3, 4);
            board.OccupyPuzzleLink(bottom, COLOUR, GROUP);
            board.OccupyPuzzleLink(top, COLOUR, GROUP);

            board.TryDamage(bottom);
            Assert.AreEqual(0, board.ResolvePuzzleLinks(null));
            board.TryDamage(top);
            Assert.AreEqual(0, board.ResolvePuzzleLinks(null));

            Assert.IsTrue(board.IsOccupied(bottom));
            Assert.IsTrue(board.IsOccupied(top));
        }

        [Test]
        public void CloneAndCopyFrom_CarryTheGroups_ButNeverAPendingHit()
        {
            var board = new Board();
            var bottom = new GridPosition(3, 3);
            var top = new GridPosition(3, 4);
            board.OccupyPuzzleLink(bottom, COLOUR, GROUP);
            board.OccupyPuzzleLink(top, COLOUR, GROUP);
            Board snapshot = board.Clone();
            Assert.AreEqual(GROUP, snapshot.GetPuzzleGroupId(top));

            board.TryDamage(bottom);
            board.CopyFrom(snapshot);
            board.TryDamage(top);

            Assert.AreEqual(0, board.ResolvePuzzleLinks(null), "The hit made before the restore is forgotten.");
            Assert.AreEqual(GROUP, board.GetPuzzleGroupId(bottom));
        }

        /// <summary>End to end: one placement that completes two rows through both pieces takes the pair
        /// and announces it for the bonus; a placement through only one piece announces nothing.</summary>
        [Test]
        public void TryPlacePiece_ThroughBothPiecesAtOnce_RemovesThePairAndPublishesTheBonus()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PuzzleLinksClearedMessage> clearedBroker);
            var bottom = new GridPosition(3, 3);
            var top = new GridPosition(3, 4);
            boardModel.OccupyPuzzleLink(bottom, COLOUR, GROUP);
            boardModel.OccupyPuzzleLink(top, COLOUR, GROUP);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                if (x != bottom.X)
                {
                    boardModel.Occupy(new GridPosition(x, 3), COLOUR);
                    boardModel.Occupy(new GridPosition(x, 4), COLOUR);
                }
            }

            trayModel.SetSlot(0, VerticalDomino, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(bottom));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(top));
            Assert.AreEqual(1, clearedBroker.Published.Count);
            Assert.AreEqual(2, clearedBroker.Published[0].CellCount);
        }

        [Test]
        public void TryPlacePiece_ThroughOnePieceOnly_LeavesThePairAndPaysNothing()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PuzzleLinksClearedMessage> clearedBroker);
            var bottom = new GridPosition(3, 3);
            var top = new GridPosition(3, 4);
            boardModel.OccupyPuzzleLink(bottom, COLOUR, GROUP);
            boardModel.OccupyPuzzleLink(top, COLOUR, GROUP);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                if (x != bottom.X)
                {
                    boardModel.Occupy(new GridPosition(x, 3), COLOUR);
                }
            }

            trayModel.SetSlot(0, Single, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.AreEqual(SpecialCellKind.PuzzleLink, boardModel.GetSpecialKind(bottom));
            Assert.AreEqual(SpecialCellKind.PuzzleLink, boardModel.GetSpecialKind(top));
            Assert.AreEqual(0, clearedBroker.Published.Count);
        }

        [Test]
        public void IsValid_WithAWellFormedPairAndTrio_Passes()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_puzzleLinkGroups\":["
                + "{\"_cells\":[{\"x\":1,\"y\":1},{\"x\":1,\"y\":2}]},"
                + "{\"_cells\":[{\"x\":4,\"y\":4},{\"x\":5,\"y\":4},{\"x\":5,\"y\":5}]}]}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        [TestCase("[{\"x\":1,\"y\":1}]")]
        [TestCase("[{\"x\":1,\"y\":1},{\"x\":1,\"y\":2},{\"x\":1,\"y\":3},{\"x\":1,\"y\":4}]")]
        [TestCase("[{\"x\":1,\"y\":1},{\"x\":2,\"y\":2}]")]
        [TestCase("[{\"x\":7,\"y\":1},{\"x\":8,\"y\":1}]")]
        public void IsValid_WithABadGroup_Fails(string cells)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_puzzleLinkGroups\":["
                + "{\"_cells\":" + cells + "}]}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValid_WithAGroupOnAHole_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_boardHoles\":[{\"_x\":2,\"_y\":2}],"
                + "\"_puzzleLinkGroups\":[{\"_cells\":[{\"x\":2,\"y\":2},{\"x\":2,\"y\":3}]}]}");

            Assert.IsFalse(config.IsValid(out string error));
            StringAssert.Contains("hole", error);
        }

        private static void FillRow(Board board, int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (!board.IsOccupied(position))
                {
                    board.Occupy(position, COLOUR);
                }
            }
        }

        private static LevelObjectiveConfig ARow(string json) => JsonUtility.FromJson<LevelObjectiveConfig>(json);

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<PuzzleLinksClearedMessage> clearedBroker)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            clearedBroker = new TestMessageBroker<PuzzleLinksClearedMessage>();
            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
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
                seed: 1,
                puzzleLinksClearedPublisher: clearedBroker);
        }
    }
}
