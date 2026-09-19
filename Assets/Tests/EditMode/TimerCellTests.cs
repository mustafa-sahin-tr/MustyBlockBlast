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
    /// Cover for the timer cell (issue #307): a pre-filled block that carries its own
    /// placements-remaining countdown and converts to an ordinary cell — or, in Path mode, ends the run
    /// — when it reaches 0.
    /// <para>
    /// Shaped after <see cref="ReinforcedCellTests"/>: first the <see cref="Board"/> primitives, then
    /// <see cref="TimerCellTick"/>, then each destruction path AC11 requires NOT to repeat the
    /// reinforced-cell cascade-reporting gap, then the real System end to end, then level authoring.
    /// </para>
    /// <para>
    /// <b>Undo is covered the same way <see cref="ReinforcedCellTests"/> covers it</b> — through
    /// <see cref="Board.Clone"/>/<see cref="Board.CopyFrom"/> directly, because one-step undo does not
    /// exist anywhere in this project yet; those two methods are what an undo built on a full-board
    /// snapshot will restore through.
    /// </para>
    /// </summary>
    public class TimerCellTests
    {
        private const int COLOUR = 3;
        private const int OTHER_COLOUR = 5;
        private const int TIMER_CELLS_MELTED_IN_TIME = (int)ObjectiveType.TimerCellsMeltedInTime;

        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        // --- Board primitives ---

        [Test]
        public void OccupyTimer_SetsColourKindAndCountdown()
        {
            var board = new Board();
            var position = new GridPosition(6, 2);

            board.OccupyTimer(position, COLOUR, 3);

            Assert.AreEqual(COLOUR, board[position]);
            Assert.AreEqual(SpecialCellKind.Timer, board.GetSpecialKind(position));
            Assert.AreEqual(3, board.GetTimerCountdown(position));
        }

        [Test]
        public void Clear_OnATimerCell_WipesItOutrightCountdownIncluded()
        {
            var board = new Board();
            var position = new GridPosition(3, 3);
            board.OccupyTimer(position, COLOUR, 4);

            board.Clear(position);

            Assert.IsFalse(board.IsOccupied(position));
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
            Assert.AreEqual(0, board.GetTimerCountdown(position));
        }

        [Test]
        public void Clone_CopiesTimerCountdowns()
        {
            var board = new Board();
            var position = new GridPosition(5, 1);
            board.OccupyTimer(position, COLOUR, 3);

            Board copy = board.Clone();

            Assert.AreEqual(3, copy.GetTimerCountdown(position));

            // And it is a copy, not a view: ticking one must not tick the other.
            copy.SetTimerCountdown(position, 1);
            Assert.AreEqual(3, board.GetTimerCountdown(position));
            Assert.AreEqual(1, copy.GetTimerCountdown(position));
        }

        [Test]
        public void CopyFrom_CopiesTimerCountdowns()
        {
            var source = new Board();
            var position = new GridPosition(5, 1);
            source.OccupyTimer(position, COLOUR, 2);

            var destination = new Board();
            destination.Occupy(position, OTHER_COLOUR);
            destination.CopyFrom(source);

            Assert.AreEqual(2, destination.GetTimerCountdown(position));
            Assert.AreEqual(SpecialCellKind.Timer, destination.GetSpecialKind(position));
            Assert.AreEqual(COLOUR, destination[position]);
        }

        [Test]
        public void CopyFrom_ABoardWithNoTimerCells_ClearsStaleCountdowns()
        {
            var source = new Board();
            var position = new GridPosition(5, 1);
            source.Occupy(position, COLOUR);

            var destination = new Board();
            destination.OccupyTimer(position, COLOUR, 4);
            destination.CopyFrom(source);

            Assert.AreEqual(0, destination.GetTimerCountdown(position));
            Assert.AreEqual(SpecialCellKind.None, destination.GetSpecialKind(position));
        }

        /// <summary>The "Undo" proxy: a full-snapshot restore (what an undo built on Clone/CopyFrom will
        /// do) rewinds a timer cell's countdown to exactly its pre-placement value, even after a tick
        /// changed it.</summary>
        [Test]
        public void CopyFrom_AfterATick_RestoresThePrePlacementCountdown()
        {
            var board = new Board();
            var position = new GridPosition(2, 2);
            board.OccupyTimer(position, COLOUR, 3);

            Board snapshot = board.Clone();

            var expired = new List<GridPosition>();
            TimerCellTick.Tick(board, expired);
            Assert.AreEqual(2, board.GetTimerCountdown(position), "Sanity: the tick really moved it.");

            board.CopyFrom(snapshot);

            Assert.AreEqual(3, board.GetTimerCountdown(position), "Restored to its pre-tick value.");
            Assert.AreEqual(SpecialCellKind.Timer, board.GetSpecialKind(position));
        }

        // --- TimerCellTick ---

        [Test]
        public void Tick_DecrementsEveryTimerCellByOne_RegardlessOfPosition()
        {
            var board = new Board();
            var untouched = new GridPosition(0, 0);
            var elsewhere = new GridPosition(7, 7);
            board.OccupyTimer(untouched, COLOUR, 3);
            board.OccupyTimer(elsewhere, COLOUR, 4);

            var expired = new List<GridPosition>();
            TimerCellTick.Tick(board, expired);

            Assert.AreEqual(2, board.GetTimerCountdown(untouched));
            Assert.AreEqual(3, board.GetTimerCountdown(elsewhere));
            Assert.AreEqual(0, expired.Count);
        }

        [Test]
        public void Tick_SkipsCellsThatAreNotTimerCells()
        {
            var board = new Board();
            var ordinary = new GridPosition(1, 1);
            board.Occupy(ordinary, COLOUR);

            var expired = new List<GridPosition>();
            TimerCellTick.Tick(board, expired);

            Assert.AreEqual(0, board.GetTimerCountdown(ordinary));
            Assert.AreEqual(0, expired.Count);
        }

        [Test]
        public void Tick_WhenCountdownReachesZero_ConvertsToOrdinaryCellWithNoBoardDamage()
        {
            var board = new Board();
            var position = new GridPosition(4, 4);
            board.OccupyTimer(position, COLOUR, 1);

            var expired = new List<GridPosition>();
            TimerCellTick.Tick(board, expired);

            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual(position, expired[0]);
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
            Assert.AreEqual(0, board.GetTimerCountdown(position));

            // No lock, no board damage (AC4): the block is still standing, in its own colour, ready to
            // be cleared like any ordinary cell.
            Assert.IsTrue(board.IsOccupied(position));
            Assert.AreEqual(COLOUR, board[position]);
        }

        [Test]
        public void Tick_RepeatedOnAThreeCountCell_ExpiresOnTheThirdTick()
        {
            var board = new Board();
            var position = new GridPosition(1, 1);
            board.OccupyTimer(position, COLOUR, 3);
            var expired = new List<GridPosition>();

            TimerCellTick.Tick(board, expired);
            Assert.AreEqual(0, expired.Count);
            TimerCellTick.Tick(board, expired);
            Assert.AreEqual(0, expired.Count);
            TimerCellTick.Tick(board, expired);

            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
        }

        // --- AC11: cleared-in-time reporting through every destruction path ---

        [Test]
        public void ResolveClears_ThroughATimerCellStillCounting_TriggersItAsClearedInTime()
        {
            var board = new Board();
            var timer = new GridPosition(2, 3);
            FillRowWithTimerCellAt(board, 3, timer, startingCountdown: 3);

            // CascadeClearResolver is what the real System uses to resolve a placement's clears; feeding
            // it a TimerCellClearEffect directly is exactly how BoardSystem's own composite effect
            // reaches this class's Apply for a phase-based clear (primary or cascaded alike).
            var timerCellClearEffect = new TimerCellClearEffect();
            timerCellClearEffect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, timerCellClearEffect);

            Assert.AreEqual(1, timerCellClearEffect.DestroyedCount);
            Assert.IsFalse(board.IsOccupied(timer));
        }

        [Test]
        public void ResolveBombClear_OverATimerCellStillCounting_TriggersItAsClearedInTime()
        {
            var board = new Board();
            var timer = new GridPosition(3, 3);
            board.OccupyTimer(timer, COLOUR, 2);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, timer);

            Assert.AreEqual(1, TimerCellClearEffect.CountDestroyed(result.TriggeredSpecials));
            Assert.IsFalse(board.IsOccupied(timer));
        }

        /// <summary>The exact shape of the gap AC11 requires NOT to repeat: a timer cell destroyed by
        /// another special cell's own blast, mid-cascade rather than through a clear phase.</summary>
        [Test]
        public void ExplosiveCoreEffect_BlastingATimerCellStillCounting_ReportsItAsDestroyed()
        {
            var board = new Board();
            var timer = new GridPosition(3, 4);
            board.OccupyTimer(timer, COLOUR, 3);

            var core = new ExplosiveCoreEffect();
            core.BeginResolution();
            core.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.ExplosiveCore));

            Assert.IsFalse(board.IsOccupied(timer));
            Assert.AreEqual(1, core.TimerCellsDestroyedCount);
        }

        [Test]
        public void LaserEffect_WipingATimerCellStillCounting_ReportsItAsDestroyed()
        {
            var board = new Board();
            var timer = new GridPosition(5, 6);
            board.OccupyTimer(timer, COLOUR, 2);

            var laser = new LaserEffect();
            laser.BeginResolution();
            laser.Apply(
                board,
                new SpecialCellTrigger(new GridPosition(0, 6), SpecialCellKind.Laser, ClearAxis.Column));

            Assert.IsFalse(board.IsOccupied(timer));
            Assert.AreEqual(1, laser.TimerCellsDestroyedCount);
        }

        [Test]
        public void ChainLightningEffect_StrikingTheOnlyOccupiedCell_ReportsATimerCellAsDestroyed()
        {
            var board = new Board();
            var timer = new GridPosition(2, 2);
            board.OccupyTimer(timer, COLOUR, 2);

            // Only one occupied cell on the board: the strike's sample is bounded by what is actually
            // occupied, so this timer cell is guaranteed to be picked whatever the random stream does.
            var chain = new ChainLightningEffect(new System.Random(1));
            chain.BeginResolution();
            chain.Apply(
                board, new SpecialCellTrigger(new GridPosition(7, 7), SpecialCellKind.ChainLightning));

            Assert.IsFalse(board.IsOccupied(timer));
            Assert.AreEqual(1, chain.TimerCellsDestroyedCount);
        }

        /// <summary>The negative half of AC5/AC11: a cell that already expired (lost its Timer kind) and
        /// is cleared afterward must NOT be reported as cleared in time — it can no longer produce a
        /// SpecialCellTrigger of kind Timer at all.</summary>
        [Test]
        public void ResolveClears_ThroughAnAlreadyExpiredTimerCell_DoesNotCountAsClearedInTime()
        {
            var board = new Board();
            var expired = new GridPosition(2, 3);
            FillRowWithTimerCellAt(board, 3, expired, startingCountdown: 1);

            var tickBuffer = new List<GridPosition>();
            TimerCellTick.Tick(board, tickBuffer);
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(expired), "Sanity: it expired.");

            var timerCellClearEffect = new TimerCellClearEffect();
            timerCellClearEffect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, timerCellClearEffect);

            Assert.AreEqual(0, timerCellClearEffect.DestroyedCount);
            Assert.IsFalse(board.IsOccupied(expired), "It still clears — just as an ordinary cell.");
        }

        // --- The real System, end to end ---

        [Test]
        public void TryPlacePiece_NotTouchingATimerCellAtAll_StillDecrementsItsCountdown()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);
            trayModel.SetSlot(0, Single, COLOUR);

            var timer = new GridPosition(7, 7);
            boardModel.Board.OccupyTimer(timer, COLOUR, 3);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            Assert.AreEqual(2, boardModel.GetTimerCountdown(timer));
            Assert.AreEqual(SpecialCellKind.Timer, boardModel.GetSpecialKind(timer));
        }

        [Test]
        public void TryPlacePiece_ClearingATimerCellInTime_ReportsItOnThePlacementMessage()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);
            trayModel.SetSlot(0, Single, COLOUR);

            var timer = new GridPosition(2, 3);
            boardModel.Board.OccupyTimer(timer, COLOUR, 3);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                var position = new GridPosition(x, 3);
                if (!position.Equals(timer))
                {
                    boardModel.Occupy(position, COLOUR);
                }
            }

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.AreEqual(1, placedBroker.Published.Count);
            Assert.AreEqual(1, placedBroker.Published[0].TimerCellsClearedInTimeCount);
        }

        [Test]
        public void TryPlacePiece_InEndlessModeWithATimerCellExpiring_DoesNotEndTheRun()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<GameOverMessage> gameOverBroker,
                gameMode: GameMode.Endless);
            trayModel.SetSlot(0, Single, COLOUR);

            var timer = new GridPosition(7, 7);
            boardModel.Board.OccupyTimer(timer, COLOUR, 1);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            Assert.AreEqual(SpecialCellKind.None, boardModel.GetSpecialKind(timer), "It expired.");
            Assert.IsFalse(system.IsGameOver);
            Assert.AreEqual(
                0, gameOverBroker.Published.Count, "ForceGameOver must never fire for Endless/Timed.");
        }

        [Test]
        public void TryPlacePiece_InPathModeWithATimerCellExpiring_EndsTheRunAsObjectiveMissed()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<GameOverMessage> gameOverBroker,
                gameMode: GameMode.Path);
            trayModel.SetSlot(0, Single, COLOUR);

            var timer = new GridPosition(7, 7);
            boardModel.Board.OccupyTimer(timer, COLOUR, 1);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            Assert.IsTrue(system.IsGameOver);
            Assert.AreEqual(1, gameOverBroker.Published.Count);
            Assert.AreEqual(GameOverReason.ObjectiveMissed, gameOverBroker.Published[0].Reason);
        }

        // --- Level authoring ---

        [Test]
        public void TimerCells_OnARowAuthoredBeforeTheFieldExisted_IsEmpty()
        {
            LevelObjectiveConfig config = ARow("{\"_levelNumber\":1,\"_targetValue\":1}");

            Assert.AreEqual(0, config.TimerCells.Count);
        }

        [Test]
        public void TimerCells_OnARowAuthoringTwo_ReadsBothPositionsAndCounts()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_timerCells\":["
                + "{\"_x\":1,\"_y\":2,\"_startingCountdown\":2},{\"_x\":5,\"_y\":6,\"_startingCountdown\":4}]}");

            Assert.AreEqual(2, config.TimerCells.Count);
            Assert.AreEqual(2, config.TimerCells[0].StartingCountdown);
            Assert.AreEqual(4, config.TimerCells[1].StartingCountdown);
        }

        [TestCase(0, 2)]
        [TestCase(1, 2)]
        [TestCase(2, 2)]
        [TestCase(4, 4)]
        [TestCase(9, 4)]
        [TestCase(-3, 2)]
        public void ValidateInEditor_ClampsTheStartingCountdownIntoRange(int authored, int expected)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_timerCells\":["
                + $"{{\"_x\":1,\"_y\":1,\"_startingCountdown\":{authored}}}]}}");

            config.ValidateInEditor();

            Assert.AreEqual(expected, config.TimerCells[0].StartingCountdown);
        }

        [TestCase(8, 1)]
        [TestCase(1, 8)]
        [TestCase(-1, 1)]
        public void IsValid_WithATimerCellOutsideTheBoard_Fails(int x, int y)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_timerCells\":["
                + $"{{\"_x\":{x},\"_y\":{y},\"_startingCountdown\":2}}]}}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [TestCase(1)]
        [TestCase(5)]
        public void IsValid_WithAStartingCountdownOutsideTwoToFour_Fails(int startingCountdown)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_timerCells\":["
                + $"{{\"_x\":1,\"_y\":1,\"_startingCountdown\":{startingCountdown}}}]}}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValid_WithATimerCellOnAHole_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_boardHoles\":[{\"_x\":2,\"_y\":2}],"
                + "\"_timerCells\":[{\"_x\":2,\"_y\":2,\"_startingCountdown\":2}]}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValid_WithATimerCellAlsoAuthoredAsReinforced_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_reinforcedCells\":[{\"_x\":2,\"_y\":2,\"_hitCount\":2}],"
                + "\"_timerCells\":[{\"_x\":2,\"_y\":2,\"_startingCountdown\":2}]}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValid_WithAWellFormedTimerCell_Passes()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_timerCells\":["
                + "{\"_x\":2,\"_y\":2,\"_startingCountdown\":3}]}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        [Test]
        public void StartNewRun_OnALevelAuthoringTimerCells_OccupiesThemWithTheAuthoredCountdowns()
        {
            var catalog = ACatalogOf(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_timerCells\":["
                + "{\"_x\":1,\"_y\":1,\"_startingCountdown\":2},{\"_x\":6,\"_y\":7,\"_startingCountdown\":4}]}");
            var progressionModel = new LevelProgressionModel();
            progressionModel.CurrentLevelNumber.Value = 1;

            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out _, out _,
                timerCellSeeder: new LevelTimerCellSeeder(
                    catalog, progressionModel, new PathRunModel(), new WeightedPieceDraw(seed: 1)));

            system.StartNewRun();

            Assert.IsTrue(boardModel.Board.IsOccupied(new GridPosition(1, 1)));
            Assert.AreEqual(2, boardModel.GetTimerCountdown(new GridPosition(1, 1)));
            Assert.AreEqual(SpecialCellKind.Timer, boardModel.GetSpecialKind(new GridPosition(1, 1)));
            Assert.IsTrue(boardModel.Board.IsOccupied(new GridPosition(6, 7)));
            Assert.AreEqual(4, boardModel.GetTimerCountdown(new GridPosition(6, 7)));

            Object.DestroyImmediate(catalog);
        }

        // --- ObjectiveProgress ---

        [Test]
        public void ApplyPlacement_ForATimerCellsMeltedInTimeObjective_AdvancesByTheClearedCount()
        {
            var definition = new ObjectiveDefinition(
                "test", ObjectiveType.TimerCellsMeltedInTime, ObjectiveScope.PerRun, targetValue: 3);
            var progress = new ObjectiveProgress(definition);

            bool changed = progress.ApplyPlacement(APlacementContext(timerCellsClearedInTime: 2));

            Assert.IsTrue(changed);
            Assert.AreEqual(2, progress.CurrentValue);
            Assert.IsFalse(progress.IsComplete);
        }

        [Test]
        public void ApplyPowerUpTimerCellsClearedInTime_ForTheRightType_Advances()
        {
            var definition = new ObjectiveDefinition(
                "test", ObjectiveType.TimerCellsMeltedInTime, ObjectiveScope.PerRun, targetValue: 2);
            var progress = new ObjectiveProgress(definition);

            bool changed = progress.ApplyPowerUpTimerCellsClearedInTime(2);

            Assert.IsTrue(changed);
            Assert.AreEqual(2, progress.CurrentValue);
            Assert.IsTrue(progress.IsComplete);
        }

        [Test]
        public void ApplyPowerUpTimerCellsClearedInTime_ForAnUnrelatedType_DoesNothing()
        {
            var definition = new ObjectiveDefinition(
                "test", ObjectiveType.ReinforcedCellsCleared, ObjectiveScope.PerRun, targetValue: 2);
            var progress = new ObjectiveProgress(definition);

            bool changed = progress.ApplyPowerUpTimerCellsClearedInTime(2);

            Assert.IsFalse(changed);
            Assert.AreEqual(0, progress.CurrentValue);
        }

        // --- Helpers ---

        private static void FillRowWithTimerCellAt(Board board, int y, GridPosition timer, int startingCountdown)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (position.Equals(timer))
                {
                    board.OccupyTimer(position, COLOUR, startingCountdown);
                    continue;
                }

                board.Occupy(position, COLOUR);
            }
        }

        private static ObjectivePlacementContext APlacementContext(int timerCellsClearedInTime)
        {
            return new ObjectivePlacementContext(
                linesCleared: 1, rowsCleared: 1, columnsCleared: 0, pieceFamily: PieceFamily.Single,
                pieceId: Single.Id, currentRunScore: 0, boardEmptyAfterPlacement: false, currentStreak: 0,
                occupiedCellCountBeforeClear: 0, anyCornerCleared: false,
                centerCoreEmptyAfterPlacement: false, hasIsolatedHolesAfterPlacement: false,
                elapsedRunSeconds: 0f, reinforcedCellsFullyCleared: 0, destroyedCellCountByColour: null,
                timerCellsClearedInTime: timerCellsClearedInTime);
        }

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<PiecePlacedMessage> placedBroker,
            LevelTimerCellSeeder timerCellSeeder = null)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            placedBroker = new TestMessageBroker<PiecePlacedMessage>();

            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                placedBroker,
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1,
                timerCellSeeder: timerCellSeeder);
        }

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<GameOverMessage> gameOverBroker,
            GameMode gameMode)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            gameOverBroker = new TestMessageBroker<GameOverMessage>();

            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = gameMode;

            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                gameOverBroker,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1,
                gameModeModel: gameModeModel);
        }

        private static LevelObjectiveConfig ARow(string json)
        {
            return JsonUtility.FromJson<LevelObjectiveConfig>(json);
        }

        private static LevelCatalog ACatalogOf(params string[] levels)
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            JsonUtility.FromJsonOverwrite($"{{\"_levels\":[{string.Join(",", levels)}]}}", catalog);
            return catalog;
        }
    }
}
