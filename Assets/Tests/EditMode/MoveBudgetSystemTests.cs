using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    /// Issue #465 — "reach S points within N moves": the move budget a Path level's ScoreInMoves objective
    /// arms, what spends a move (only a landed placement), the one rewarded "+moves" offer when the budget
    /// runs out short of the target, the failure after it, and the leftover-move bonus.
    /// </summary>
    public sealed class MoveBudgetSystemTests
    {
        private const int MOVE_LIMIT = 3;
        private const int TARGET_SCORE = 1500;

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private GameModeModel _gameModeModel;
        private ObjectiveModel _objectiveModel;
        private PathRunModel _pathRunModel;
        private MoveBudgetModel _moveBudgetModel;
        private MoveBudgetConfig _moveBudgetConfig;
        private CurrencyConfig _currencyConfig;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<MovesRanOutMessage> _movesRanOutBroker;
        private BoardSystem _boardSystem;
        private MoveBudgetSystem _system;
        private StubExtraMovesRewardSource _rewardSource;

        [SetUp]
        public void CreateSystems()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _gameModeModel = new GameModeModel();
            _gameModeModel.CurrentMode.Value = GameMode.Path;
            _objectiveModel = new ObjectiveModel();
            _pathRunModel = new PathRunModel();
            _moveBudgetModel = new MoveBudgetModel();
            _moveBudgetConfig = ScriptableObject.CreateInstance<MoveBudgetConfig>();
            _currencyConfig = ScriptableObject.CreateInstance<CurrencyConfig>();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _movesRanOutBroker = new TestMessageBroker<MovesRanOutMessage>();
            _rewardSource = new StubExtraMovesRewardSource(granted: true);

            _objectiveModel.SetObjectives(new[]
            {
                new ObjectiveProgress(new ObjectiveDefinition(
                    "level_1", ObjectiveType.ScoreInMoves, ObjectiveScope.PerRun, TARGET_SCORE, moveLimit: MOVE_LIMIT)),
            });

            // Deliberately unstarted: the tests deal the dock by hand and announce the run themselves.
            _boardSystem = new BoardSystem(
                _boardModel,
                _trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                _runStartedBroker,
                _piecePlacedBroker,
                new TestMessageBroker<LinesClearedMessage>(),
                _gameOverBroker,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                _currencyConfig,
                seed: 1,
                gameModeModel: _gameModeModel,
                moveBudgetModel: _moveBudgetModel);

            _system = CreateMoveBudgetSystem(_rewardSource);
        }

        [TearDown]
        public void DestroyConfigs()
        {
            _system.Dispose();
            UnityEngine.Object.DestroyImmediate(_moveBudgetConfig);
            UnityEngine.Object.DestroyImmediate(_currencyConfig);
        }

        [Test]
        public void APathRunOnAMoveLimitedLevel_ArmsTheBudget()
        {
            StartRun();

            Assert.IsTrue(_moveBudgetModel.IsActive.Value);
            Assert.AreEqual(MOVE_LIMIT, _moveBudgetModel.MovesLeft.Value);
        }

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void OutsidePath_ThereIsNoBudget_AndPlacementsAreUnlimited(GameMode mode)
        {
            _gameModeModel.CurrentMode.Value = mode;
            StartRun();

            for (int placement = 0; placement < MOVE_LIMIT + 2; placement++)
            {
                Assert.IsTrue(Place(placement), $"Placement {placement} was refused.");
            }

            Assert.IsFalse(_moveBudgetModel.IsActive.Value);
            Assert.AreEqual(0, _movesRanOutBroker.Published.Count);
        }

        [Test]
        public void ALevelWithoutTheObjective_HasNoBudget()
        {
            _objectiveModel.SetObjectives(new[]
            {
                new ObjectiveProgress(new ObjectiveDefinition("level_2", ObjectiveType.ScoreInRun, ObjectiveScope.PerRun, TARGET_SCORE)),
            });

            StartRun();

            Assert.IsFalse(_moveBudgetModel.IsActive.Value);
        }

        [Test]
        public void EachLandedPlacement_SpendsExactlyOneMove_AndARefusedDropSpendsNone()
        {
            StartRun();
            Assert.IsTrue(Place(0));
            Assert.AreEqual(MOVE_LIMIT - 1, _moveBudgetModel.MovesLeft.Value);

            // The same cell again: refused, so the budget does not move.
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);
            Assert.IsFalse(_boardSystem.TryPlacePiece(0, new GridPosition(0, 0)));
            Assert.AreEqual(MOVE_LIMIT - 1, _moveBudgetModel.MovesLeft.Value);
        }

        [Test]
        public void SpendingTheLastMoveShortOfTheTarget_OpensTheOffer_AndHoldsTheRun()
        {
            StartRun();
            SpendWholeBudget();

            Assert.IsTrue(_moveBudgetModel.IsOfferOpen.Value);
            Assert.AreEqual(1, _movesRanOutBroker.Published.Count);
            Assert.IsFalse(_boardSystem.IsGameOver, "The run is held, not ended.");

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);
            Assert.IsFalse(_boardSystem.CanPlace(0, new GridPosition(5, 5)), "A spent budget takes no placement.");
        }

        [Test]
        public void AGrantedAd_AddsTheConfiguredMoves_AndClosesTheOffer()
        {
            StartRun();
            SpendWholeBudget();

            bool granted = RequestExtraMoves();

            Assert.IsTrue(granted);
            Assert.IsFalse(_moveBudgetModel.IsOfferOpen.Value);
            Assert.AreEqual(_moveBudgetConfig.ExtraMovesFromAd, _moveBudgetModel.MovesLeft.Value);
            Assert.AreEqual(1, _rewardSource.RequestCount);
            Assert.IsTrue(Place(MOVE_LIMIT), "The extra moves are playable.");
        }

        [Test]
        public void RunningOutASecondTime_FailsTheRunWithoutAnotherOffer()
        {
            StartRun();
            SpendWholeBudget();
            RequestExtraMoves();

            for (int placement = 0; placement < _moveBudgetConfig.ExtraMovesFromAd; placement++)
            {
                Assert.IsTrue(Place(MOVE_LIMIT + placement));
            }

            Assert.AreEqual(1, _movesRanOutBroker.Published.Count, "The offer comes once per attempt.");
            Assert.IsTrue(_boardSystem.IsGameOver);
            Assert.AreEqual(GameOverReason.ObjectiveMissed, _gameOverBroker.Published[0].Reason);
        }

        [Test]
        public void DecliningTheOffer_FailsTheRunAsObjectiveMissed()
        {
            StartRun();
            SpendWholeBudget();

            _system.DeclineExtraMoves();

            Assert.IsFalse(_moveBudgetModel.IsOfferOpen.Value);
            Assert.IsTrue(_boardSystem.IsGameOver);
            Assert.AreEqual(GameOverReason.ObjectiveMissed, _gameOverBroker.Published[0].Reason);
            Assert.IsFalse(_gameOverBroker.Published[0].IsRescueAvailable);
        }

        [Test]
        public void AnAdThatDoesNotPay_FailsTheRun()
        {
            _system.Dispose();
            _rewardSource = new StubExtraMovesRewardSource(granted: false);
            _system = CreateMoveBudgetSystem(_rewardSource);
            StartRun();
            SpendWholeBudget();

            bool granted = RequestExtraMoves();

            Assert.IsFalse(granted);
            Assert.IsTrue(_boardSystem.IsGameOver);
            Assert.AreEqual(GameOverReason.ObjectiveMissed, _gameOverBroker.Published[0].Reason);
        }

        [Test]
        public void TheLastMoveThatReachesTheTarget_OpensNoOffer()
        {
            StartRun();
            Place(0);
            Place(1);

            // The objective completing on this placement raises the flag before the budget looks.
            _pathRunModel.IsLevelCompleting = true;
            Place(2);

            Assert.AreEqual(0, _movesRanOutBroker.Published.Count);
            Assert.IsFalse(_moveBudgetModel.IsOfferOpen.Value);
            Assert.IsFalse(_boardSystem.IsGameOver);
        }

        [Test]
        public void ANewRun_ResetsTheBudgetAndTheOffer()
        {
            StartRun();
            SpendWholeBudget();
            RequestExtraMoves();

            StartRun();

            Assert.AreEqual(MOVE_LIMIT, _moveBudgetModel.MovesLeft.Value);
            Assert.IsFalse(_moveBudgetModel.IsOfferSpent);
            Assert.AreEqual(0, _moveBudgetModel.ExtraMoves);
        }

        [Test]
        public void TheLeftoverBonus_PaysEveryUnspentMove()
        {
            StartRun();
            Place(0);

            int bonus = _system.ClaimLeftoverBonus();

            Assert.AreEqual((MOVE_LIMIT - 1) * _moveBudgetConfig.BonusPerLeftoverMove, bonus);
            Assert.AreEqual(MOVE_LIMIT - 1, _moveBudgetModel.LeftoverMoves);
            Assert.AreEqual(bonus, _moveBudgetModel.LeftoverBonus);
        }

        [Test]
        public void TheLeftoverBonus_IsZeroWithoutABudget()
        {
            _gameModeModel.CurrentMode.Value = GameMode.Endless;
            StartRun();

            Assert.AreEqual(0, _system.ClaimLeftoverBonus());
        }

        [Test]
        public void ScoreInMoves_MirrorsTheRunScore_LikeScoreInRun()
        {
            var progress = new ObjectiveProgress(new ObjectiveDefinition(
                "moves", ObjectiveType.ScoreInMoves, ObjectiveScope.PerRun, 500, moveLimit: 10));

            progress.ApplyPlacement(ScoreContext(320));
            Assert.AreEqual(320, progress.CurrentValue);

            progress.ApplyPlacement(ScoreContext(900));
            Assert.AreEqual(500, progress.CurrentValue, "Clamped to the target.");
            Assert.IsTrue(progress.IsComplete);
        }

        [Test]
        public void ScoreInMoves_NeedsAMoveLimit_AndAPerRunScope()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectiveDefinition(
                "moves", ObjectiveType.ScoreInMoves, ObjectiveScope.PerRun, 500, moveLimit: 0));
            Assert.Throws<ArgumentException>(() => new ObjectiveDefinition(
                "moves", ObjectiveType.ScoreInMoves, ObjectiveScope.Cumulative, 500, moveLimit: 10));
            Assert.AreEqual(0, new ObjectiveDefinition(
                "score", ObjectiveType.ScoreInRun, ObjectiveScope.PerRun, 500, moveLimit: 10).MoveLimit,
                "Only ScoreInMoves carries a budget.");
        }

        // --- helpers ---

        private MoveBudgetSystem CreateMoveBudgetSystem(IExtraMovesRewardSource rewardSource)
            => new MoveBudgetSystem(
                _moveBudgetModel, _moveBudgetConfig, _objectiveModel, _gameModeModel, _pathRunModel, _boardSystem,
                rewardSource, _runStartedBroker, _piecePlacedBroker, _gameOverBroker, _movesRanOutBroker);

        private void StartRun() => _runStartedBroker.Publish(new RunStartedMessage());

        /// <summary>Drops a single cell on a distinct cell of the first row(s) — never completing a line.</summary>
        private bool Place(int placementIndex)
        {
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);
            int row = (placementIndex / 7) * 2;
            int column = placementIndex % 7;
            return _boardSystem.TryPlacePiece(0, new GridPosition(row, column));
        }

        private void SpendWholeBudget()
        {
            for (int placement = 0; placement < MOVE_LIMIT; placement++)
            {
                Assert.IsTrue(Place(placement), $"Placement {placement} was refused.");
            }
        }

        private bool RequestExtraMoves()
            => _system.TryRequestExtraMovesAsync(CancellationToken.None).GetAwaiter().GetResult();

        private static ObjectivePlacementContext ScoreContext(int runScore)
            => new ObjectivePlacementContext(
                0, 0, 0, PieceFamily.Single, "single", runScore, false, 0, 0, false, false, false, 0f, 0,
                null, 0, null, 0);

        private sealed class StubExtraMovesRewardSource : IExtraMovesRewardSource
        {
            private readonly bool _granted;

            internal StubExtraMovesRewardSource(bool granted)
            {
                _granted = granted;
            }

            internal int RequestCount { get; private set; }

            public UniTask<ExtraMovesRewardResult> RequestExtraMovesRewardAsync(CancellationToken cancellationToken)
            {
                RequestCount++;
                return UniTask.FromResult(new ExtraMovesRewardResult(_granted));
            }
        }
    }
}
