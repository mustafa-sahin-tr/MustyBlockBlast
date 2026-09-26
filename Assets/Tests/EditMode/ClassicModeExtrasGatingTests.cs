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
    /// Issue #355: Classic mode (<see cref="GameMode.Timed"/>, displayed to the player as "Klasik
    /// Mod") plays with no special cells and no power-ups. Covers the single source of truth
    /// (<see cref="GameModeModel.ExtrasEnabled"/>) and every spawn/apply path gated on it that is not
    /// already covered by <c>PowerUpSystemTests</c>' own Classic-mode cases, plus the "Sınırsız"
    /// (endless) duration sentinel's timer and high-score behaviour.
    /// </summary>
    public sealed class ClassicModeExtrasGatingTests
    {
        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        // --- GameModeModel.ExtrasEnabled: the one source of truth every gate below reads ---

        [Test]
        public void ExtrasEnabled_InEndlessMode_IsTrue()
        {
            var model = new GameModeModel();
            model.CurrentMode.Value = GameMode.Endless;

            Assert.IsTrue(model.ExtrasEnabled);
        }

        [Test]
        public void ExtrasEnabled_InPathMode_IsTrue()
        {
            var model = new GameModeModel();
            model.CurrentMode.Value = GameMode.Path;

            Assert.IsTrue(model.ExtrasEnabled);
        }

        [Test]
        public void ExtrasEnabled_InClassicMode_IsFalse()
        {
            var model = new GameModeModel();
            model.CurrentMode.Value = GameMode.Timed;

            Assert.IsFalse(model.ExtrasEnabled);
        }

        // --- BoardSystem: no special-cell spawns at all in Classic mode ---

        [Test]
        public void TryPlacePiece_InClassicMode_ClosingARowAndAColumn_SpawnsNoExplosiveCore()
        {
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, Single, 1);
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Timed;
            BoardSystem system = CreateBoardSystem(boardModel, trayModel, gameModeModel);

            var gap = new GridPosition(3, 5);
            FillRowExcept(boardModel, y: 5, gap);
            FillColumnExcept(boardModel, x: 3, gap);

            system.TryPlacePiece(0, gap);

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    Assert.AreEqual(
                        SpecialCellKind.None, boardModel.GetSpecialKind(new GridPosition(x, y)),
                        $"({x}, {y}) should carry no special kind in Classic mode.");
                }
            }
        }

        // --- Special dock pieces: Classic mode deals neither a piercing rocket nor a golden single ---

        [Test]
        public void TryPlacePiece_InClassicMode_ClearingThreeLines_DealsNoPiercingRocket()
        {
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            var domino = new Piece("test_domino_v", new[] { new GridPosition(0, 0), new GridPosition(0, 1) });
            trayModel.SetSlot(0, domino, 1);
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Timed;
            BoardSystem system = CreateBoardSystem(boardModel, trayModel, gameModeModel);

            // The domino lands on (3, 4) and (3, 5), completing rows 4 and 5 and column 3 at once.
            FillRowExcept(boardModel, y: 4, new GridPosition(3, 4));
            FillRowExcept(boardModel, y: 5, new GridPosition(3, 5));
            for (int y = 0; y < Board.SIZE; y++)
            {
                if (y != 4 && y != 5)
                {
                    boardModel.Occupy(new GridPosition(3, y), 1);
                }
            }

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(3, 4)));

            AssertNoSpecialPieceInTheDock(trayModel);
        }

        [Test]
        public void RequestGoldenPieceInjection_InClassicMode_ThenARefill_DealsNoGoldenPiece()
        {
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, Single, 1);
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Timed;
            BoardSystem system = CreateBoardSystem(new BoardModel(), trayModel, gameModeModel);

            system.RequestGoldenPieceInjection();
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            AssertNoSpecialPieceInTheDock(trayModel);
        }

        [Test]
        public void RequestGoldenPieceInjection_InEndlessMode_ThenARefill_StillDealsTheGoldenPiece()
        {
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, Single, 1);
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Endless;
            BoardSystem system = CreateBoardSystem(new BoardModel(), trayModel, gameModeModel);

            system.RequestGoldenPieceInjection();
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            Assert.AreEqual(SpecialPieceKind.Golden, trayModel.GetSpecialKind(0));
        }

        // --- LaserSpawnSystem / CoinStreakEscalationSystem: no streak-earned special cells either ---

        [Test]
        public void LaserSpawnSystem_InClassicMode_ReachingTheSpawnStreak_SpawnsNoLaser()
        {
            var scoreModel = new ScoreModel();
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Timed;

            using (var system = new LaserSpawnSystem(scoreModel, boardModel, seed: 1, gameModeModel: gameModeModel))
            {
                AdvanceStreakTo(scoreModel, 4);

                Assert.AreEqual(SpecialCellKind.None, boardModel.GetSpecialKind(new GridPosition(0, 0)));
            }
        }

        /// <summary>Issue #401 AC7: none of the five paying streak levels drops a coin in Classic mode.</summary>
        [Test]
        public void CoinStreakEscalationSystem_InClassicMode_ClimbingThroughEveryPayingStreak_SpawnsNoCoin()
        {
            var scoreModel = new ScoreModel();
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Timed;

            using (var system = new CoinStreakEscalationSystem(scoreModel, boardModel, seed: 1, gameModeModel: gameModeModel))
            {
                AdvanceStreakTo(scoreModel, 7);

                Assert.AreEqual(SpecialCellKind.None, boardModel.GetSpecialKind(new GridPosition(0, 0)));
                Assert.AreEqual(0, boardModel.GetCoinValue(new GridPosition(0, 0)));
            }
        }

        // --- TimerRunSystem: the "Sınırsız" (endless) duration never starts a clock ---

        [Test]
        public void OnRunStarted_InClassicModeWithTheEndlessDuration_NeverStartsTheClock()
        {
            var timerModel = new TimerModel();
            BoardSystem boardSystem = CreateBoardSystem();
            var gameModeSystem = new GameModeSystem(new GameModeModel(), boardSystem);
            gameModeSystem.SelectMode(GameMode.Timed);

            var timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            var timedModeModel = new TimedModeModel();
            var timedModeSystem = new TimedModeSystem(timedModeModel, timedModeConfig);

            // Written to the Model after the System is built, not through SelectDuration: that would
            // persist the choice to the editor's real PlayerPrefs, which the constructor of every later
            // TimedModeSystem reads back — leaking this test's duration into the next one.
            timedModeModel.SelectedDurationSeconds.Value = TimedModeConfig.ENDLESS_DURATION_SECONDS;

            var runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            var timerRunSystem = new TimerRunSystem(
                timerModel,
                new RunPauseModel(),
                gameModeSystem,
                timedModeSystem,
                boardSystem,
                runStartedBroker,
                new TestMessageBroker<GameOverMessage>());

            runStartedBroker.Publish(new RunStartedMessage());

            Assert.IsFalse(timerModel.IsRunning.Value);
            Assert.AreEqual(0f, timerModel.RemainingSeconds.Value);
        }

        [Test]
        public void OnRunStarted_InClassicModeWithARealDuration_StartsTheClock()
        {
            var timerModel = new TimerModel();
            BoardSystem boardSystem = CreateBoardSystem();
            var gameModeSystem = new GameModeSystem(new GameModeModel(), boardSystem);
            gameModeSystem.SelectMode(GameMode.Timed);

            var timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            var timedModeModel = new TimedModeModel();
            var timedModeSystem = new TimedModeSystem(timedModeModel, timedModeConfig);

            // After the System is built: its constructor seeds the Model from PlayerPrefs, which would
            // overwrite a value written before it.
            timedModeModel.SelectedDurationSeconds.Value = 180f;

            var runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            var timerRunSystem = new TimerRunSystem(
                timerModel,
                new RunPauseModel(),
                gameModeSystem,
                timedModeSystem,
                boardSystem,
                runStartedBroker,
                new TestMessageBroker<GameOverMessage>());

            runStartedBroker.Publish(new RunStartedMessage());

            Assert.IsTrue(timerModel.IsRunning.Value);
            Assert.AreEqual(180f, timerModel.RemainingSeconds.Value);
        }

        // --- TimedHighScoreKey: Classic's "Sınırsız" duration gets its own bucket ---

        [Test]
        public void TimedHighScoreKey_ForTheEndlessDuration_IsDistinctFromARealDurationsKey()
        {
            string endlessKey = TimedHighScoreKey.For(TimedModeConfig.ENDLESS_DURATION_SECONDS);
            string realDurationKey = TimedHighScoreKey.For(180f);

            Assert.AreNotEqual(endlessKey, realDurationKey);
        }

        [Test]
        public void TimedHighScoreKey_ForTheEndlessDuration_IsStableAcrossCalls()
        {
            string first = TimedHighScoreKey.For(TimedModeConfig.ENDLESS_DURATION_SECONDS);
            string second = TimedHighScoreKey.For(TimedModeConfig.ENDLESS_DURATION_SECONDS);

            Assert.AreEqual(first, second);
        }

        private static void AssertNoSpecialPieceInTheDock(TrayModel trayModel)
        {
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.AreEqual(
                    SpecialPieceKind.None, trayModel.GetSpecialKind(slotIndex),
                    $"Slot {slotIndex} should hold no special piece in Classic mode.");
            }
        }

        private static void AdvanceStreakTo(ScoreModel scoreModel, int streak)
        {
            for (int value = 1; value <= streak; value++)
            {
                scoreModel.Streak.Value = value;
            }
        }

        private static void FillRowExcept(BoardModel boardModel, int y, GridPosition gap)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (position.X == gap.X && position.Y == gap.Y)
                {
                    continue;
                }

                boardModel.Occupy(position, 1);
            }
        }

        private static void FillColumnExcept(BoardModel boardModel, int x, GridPosition gap)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                var position = new GridPosition(x, y);
                if (position.X == gap.X && position.Y == gap.Y)
                {
                    continue;
                }

                boardModel.Occupy(position, 1);
            }
        }

        private static BoardSystem CreateBoardSystem()
        {
            return CreateBoardSystem(new BoardModel(), new TrayModel(), gameModeModel: null);
        }

        private static BoardSystem CreateBoardSystem(
            BoardModel boardModel, TrayModel trayModel, GameModeModel gameModeModel)
        {
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
                gameModeModel: gameModeModel);
        }
    }
}
