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
    /// Covers AC6(a): a level's authored coin-cell count reaches that level's board.
    /// <para>
    /// The timing is the substance of these tests. A run opens on an empty board and a coin cell has to
    /// sit on a block, so the count is armed at run start and paid at the first placement that leaves a
    /// block standing — see <see cref="LevelCoinCellSeedSystem"/>. Each test below states one edge of
    /// that: nothing appears too early, nothing appears twice, and nothing is dropped when the first
    /// placement happens to empty the board again.
    /// </para>
    /// </summary>
    public class LevelCoinCellSeedSystemTests
    {
        private BoardModel _boardModel;
        private LevelProgressionModel _progressionModel;
        private PathRunModel _pathRunModel;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private LevelCatalog _catalog;
        private LevelCoinCellSeedSystem _system;

        [SetUp]
        public void CreateBrokers()
        {
            _boardModel = new BoardModel();
            _progressionModel = new LevelProgressionModel();
            _pathRunModel = new PathRunModel();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
        }

        [TearDown]
        public void DisposeSystem()
        {
            if (_system != null)
            {
                _system.Dispose();
            }

            if (_catalog != null)
            {
                Object.DestroyImmediate(_catalog);
            }
        }

        /// <summary>The whole point of the two-step: a run start has nothing to paint, and painting
        /// nothing is the correct — not a broken — outcome at that moment.</summary>
        [Test]
        public void OnRunStarted_AloneOnAnEmptyBoard_SeedsNothing()
        {
            CreateSystem(coinCellCount: 2);

            _runStartedBroker.Publish(new RunStartedMessage());

            Assert.AreEqual(0, CountCoins());
        }

        /// <summary>AC6(a): the authored count appears once there are blocks to carry it.</summary>
        [Test]
        public void OnTheFirstPlacement_SeedsTheAuthoredCount()
        {
            CreateSystem(coinCellCount: 3);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);

            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(3, CountCoins());
        }

        /// <summary>Seeded onto blocks, in place: a level's dressing must not add occupancy the player
        /// never placed.</summary>
        [Test]
        public void OnTheFirstPlacement_SeedsOnlyOntoOccupiedCells()
        {
            CreateSystem(coinCellCount: 2);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);

            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(Board.SIZE, _boardModel.Board.OccupiedCellCount());
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_boardModel.GetSpecialKind(position) == SpecialCellKind.Coin)
                    {
                        Assert.IsTrue(_boardModel.Board.IsOccupied(position), $"{position} holds no block.");
                    }
                }
            }
        }

        /// <summary>The debt is paid once. A level authoring two coins does not hand out two more on
        /// every subsequent placement for the rest of the run.</summary>
        [Test]
        public void OnLaterPlacements_SeedsNothingFurther()
        {
            CreateSystem(coinCellCount: 2);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);

            _piecePlacedBroker.Publish(APlacement());
            _piecePlacedBroker.Publish(APlacement());
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(2, CountCoins());
        }

        /// <summary>Nothing is dropped when the first placement clears the line it completed: the debt
        /// simply waits for a board with something on it.</summary>
        [Test]
        public void WhenTheFirstPlacementLeavesAnEmptyBoard_TheDebtIsPaidByTheNextOne()
        {
            CreateSystem(coinCellCount: 1);
            _runStartedBroker.Publish(new RunStartedMessage());

            _piecePlacedBroker.Publish(APlacement());
            Assert.AreEqual(0, CountCoins(), "Nothing to paint onto yet.");

            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(1, CountCoins());
        }

        /// <summary>A level authoring none seeds none, which is every level authored before the field
        /// existed.</summary>
        [Test]
        public void OnTheFirstPlacement_ForALevelAuthoringNoCoins_SeedsNothing()
        {
            CreateSystem(coinCellCount: 0);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);

            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(0, CountCoins());
        }

        /// <summary>A level the catalog does not author seeds nothing rather than throwing: a broken or
        /// shrunken catalog must not take the run down.</summary>
        [Test]
        public void OnTheFirstPlacement_ForAnUnauthoredLevel_SeedsNothing()
        {
            CreateSystem(coinCellCount: 3);
            _progressionModel.CurrentLevelNumber.Value = 99;
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);

            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(0, CountCoins());
        }

        /// <summary>In Path mode the level being played is the active one, not the frontier — the same
        /// distinction the progression system keeps.</summary>
        [Test]
        public void OnTheFirstPlacement_InPathMode_SeedsTheActiveLevelsCount()
        {
            _catalog = ACatalogOf(Level(1, coinCellCount: 0), Level(2, coinCellCount: 4));
            _system = new LevelCoinCellSeedSystem(
                _boardModel, _catalog, _progressionModel, _pathRunModel, _runStartedBroker,
                _piecePlacedBroker, seed: 1);

            _progressionModel.CurrentLevelNumber.Value = 1;
            _pathRunModel.ActiveLevelNumber.Value = 2;

            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(4, CountCoins());
        }

        /// <summary>A debt belongs to the run that armed it: a run that ended before its coins were ever
        /// painted must not hand them to the next one on top of its own.</summary>
        [Test]
        public void OnASecondRunStarted_ReplacesTheUnpaidDebtRatherThanAddingToIt()
        {
            CreateSystem(coinCellCount: 2);

            _runStartedBroker.Publish(new RunStartedMessage());
            _runStartedBroker.Publish(new RunStartedMessage());

            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(2, CountCoins());
        }

        // --- Issue #167: coin cells bought at the level-start screen ---

        /// <summary>AC3: the purchased quantity reaches the board on top of the level's own authored
        /// count, not instead of it. Both numbers are non-zero and different, so a sum and either source
        /// alone are three distinguishable answers.</summary>
        [Test]
        public void QueueExtraCoinCells_AddsToTheAuthoredCountRatherThanReplacingIt()
        {
            CreateSystem(coinCellCount: 2);

            _system.QueueExtraCoinCells(3);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(5, CountCoins());
        }

        /// <summary>A purchase dresses the level it was bought for and no other: the queue is cleared by
        /// the run that takes it up, so the next run gets the authored count alone.</summary>
        [Test]
        public void QueueExtraCoinCells_IsConsumedByExactlyOneRun()
        {
            CreateSystem(coinCellCount: 1);

            _system.QueueExtraCoinCells(2);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());
            Assert.AreEqual(3, CountCoins(), "The bought cells belong to this run.");

            _boardModel.ClearAll();
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(1, CountCoins(), "The second run gets the authored count and nothing bought.");
        }

        /// <summary>AC4: declining sows nothing, and a level authoring none with nothing bought is a
        /// completely bare board.</summary>
        [Test]
        public void QueueExtraCoinCells_WithZero_SeedsOnlyTheAuthoredCount()
        {
            CreateSystem(coinCellCount: 0);

            _system.QueueExtraCoinCells(0);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(0, CountCoins());
        }

        /// <summary>A negative quantity cannot reach here through the picker, and if it did it must not
        /// be able to subtract from the cells the level itself asked for.</summary>
        [Test]
        public void QueueExtraCoinCells_WithANegativeQuantity_LeavesTheAuthoredCountIntact()
        {
            CreateSystem(coinCellCount: 2);

            _system.QueueExtraCoinCells(-5);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(2, CountCoins());
        }

        /// <summary>Re-committing at the level-start screen before any run has started offers the new
        /// quantity, not the sum of both attempts.</summary>
        [Test]
        public void QueueExtraCoinCells_CalledTwiceBeforeARunStarts_ReplacesTheEarlierQuantity()
        {
            CreateSystem(coinCellCount: 0);

            _system.QueueExtraCoinCells(4);
            _system.QueueExtraCoinCells(1);
            _runStartedBroker.Publish(new RunStartedMessage());
            OccupyRow(4);
            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(1, CountCoins());
        }

        /// <summary>
        /// AC6: asking for more cells than the board has blocks to carry can neither overflow the board
        /// nor stack two coins onto one cell and call it two. The selector only ever picking a plain block
        /// (issue #441) is what makes this true, and a purchase is the one thing that can ask for more cells than a level ever
        /// would.
        /// </summary>
        [Test]
        public void QueueExtraCoinCells_MoreThanTheBoardCanCarry_NeverOverflowsOrDoublesUp()
        {
            CreateSystem(coinCellCount: 0);

            _system.QueueExtraCoinCells(20);
            _runStartedBroker.Publish(new RunStartedMessage());

            // Three blocks on the board, twenty coin cells owed: at most three cells can be coins.
            for (int x = 0; x < 3; x++)
            {
                _boardModel.Occupy(new GridPosition(x, 4), 1);
            }

            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(3, CountCoins(), "Every block carries exactly one coin, none doubled up.");
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_boardModel.GetSpecialKind(position) == SpecialCellKind.Coin)
                    {
                        Assert.IsTrue(_boardModel.Board.IsOccupied(position), $"{position} holds no block.");
                    }
                }
            }
        }

        [Test]
        public void AfterDispose_SeedsNothing()
        {
            CreateSystem(coinCellCount: 2);
            _runStartedBroker.Publish(new RunStartedMessage());
            _system.Dispose();
            OccupyRow(4);

            _piecePlacedBroker.Publish(APlacement());

            Assert.AreEqual(0, CountCoins());
        }

        private void CreateSystem(int coinCellCount)
        {
            _catalog = ACatalogOf(Level(1, coinCellCount));
            _progressionModel.CurrentLevelNumber.Value = 1;
            _system = new LevelCoinCellSeedSystem(
                _boardModel, _catalog, _progressionModel, _pathRunModel, _runStartedBroker,
                _piecePlacedBroker, seed: 1);
        }

        /// <summary>A placement that cleared nothing. None of the figures matter here — the seeder reads
        /// the message only as "something was placed, so the board may have blocks on it now".</summary>
        private static PiecePlacedMessage APlacement()
        {
            return new PiecePlacedMessage(
                "test_single", new GridPosition(0, 0), PieceFamily.Single, 1, 1, 0, 0, 0, 0,
                boardEmptyAfterPlacement: false, occupiedCellCountBeforeClear: 1,
                anyCornerCleared: false, centerCoreEmptyAfterPlacement: false,
                hasIsolatedHolesAfterPlacement: false);
        }

        private void OccupyRow(int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                _boardModel.Occupy(new GridPosition(x, y), 1);
            }
        }

        private int CountCoins()
        {
            int count = 0;
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    if (_boardModel.GetSpecialKind(new GridPosition(x, y)) == SpecialCellKind.Coin)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>Builds a catalog from authored rows through <see cref="JsonUtility"/>, exactly as
        /// <c>LevelProgressionSystemPathModeTests</c> does: the serialized field names are the asset's
        /// own contract, so a row written this way is what the Inspector would have produced.</summary>
        private static LevelCatalog ACatalogOf(params string[] levels)
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            JsonUtility.FromJsonOverwrite($"{{\"_levels\":[{string.Join(",", levels)}]}}", catalog);
            return catalog;
        }

        private static string Level(int levelNumber, int coinCellCount)
        {
            return "{"
                + $"\"_levelNumber\":{levelNumber},"
                + "\"_targetValue\":1,"
                + "\"_requiredLineCount\":1,"
                + $"\"_coinCellCount\":{coinCellCount}"
                + "}";
        }
    }
}
