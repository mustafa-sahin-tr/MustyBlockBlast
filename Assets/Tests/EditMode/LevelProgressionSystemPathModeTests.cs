using System.Collections.Generic;
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
    /// Covers <see cref="GameMode.Path"/>'s contract, and — just as importantly — that Endless and
    /// Timed did not acquire any of it.
    /// <para>
    /// The regression guard is the point of this file. Path mode reuses the very method that Endless
    /// and Timed advance through, so "completing a level ends the run" and "completing a level does
    /// not end the run" are one branch apart; the Endless/Timed cases here pin the second so a change
    /// to the first cannot quietly take it with it.
    /// </para>
    /// </summary>
    public class LevelProgressionSystemPathModeTests
    {
        /// <summary>The PlayerPrefs blob <see cref="LevelProgressionSystem"/> reads its frontier from.
        /// Cleared around every test: a level number left behind by one test would silently decide
        /// which levels the next one thinks are unlocked.</summary>
        private const string LEVEL_SAVE_KEY = "Levels.Progress";

        private const string HIGH_SCORE_KEY = "Score.HighScore";

        private TestMessageBroker<ObjectiveCompletedMessage> _objectiveCompletedBroker;
        private TestMessageBroker<ObjectiveProgressChangedMessage> _objectiveProgressBroker;
        private TestMessageBroker<LevelAdvancedMessage> _levelAdvancedBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private TestMessageBroker<ScoreChangedMessage> _scoreChangedBroker;

        /// <summary>Every power-up that enters the inventory announces itself here — the only external
        /// evidence that a level's reward was (or was not) paid.</summary>
        private TestMessageBroker<PowerUpGrantedMessage> _powerUpGrantedBroker;

        private LevelProgressionModel _progressionModel;
        private PathRunModel _pathRunModel;
        private ObjectiveModel _objectiveModel;
        private ScoreModel _scoreModel;
        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private BoardSystem _boardSystem;
        private GameModeSystem _gameModeSystem;
        private ScoreSystem _scoreSystem;

        [SetUp]
        public void ClearPersistedProgress()
        {
            PlayerPrefs.DeleteKey(LEVEL_SAVE_KEY);
            PlayerPrefs.DeleteKey(HIGH_SCORE_KEY);
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.RowClear));

            _objectiveCompletedBroker = new TestMessageBroker<ObjectiveCompletedMessage>();
            _objectiveProgressBroker = new TestMessageBroker<ObjectiveProgressChangedMessage>();
            _levelAdvancedBroker = new TestMessageBroker<LevelAdvancedMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _scoreChangedBroker = new TestMessageBroker<ScoreChangedMessage>();
            _powerUpGrantedBroker = new TestMessageBroker<PowerUpGrantedMessage>();
        }

        [TearDown]
        public void ClearPersistedProgressAfterwards()
        {
            PlayerPrefs.DeleteKey(LEVEL_SAVE_KEY);
            PlayerPrefs.DeleteKey(HIGH_SCORE_KEY);

            // A level-up reward is persisted the moment it is granted, so a test that earns one would
            // otherwise hand the next fixture a stocked inventory.
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.RowClear));
        }

        // --- AC1: the mode exists and is selectable ---

        [Test]
        public void SelectMode_Path_BecomesTheActiveMode()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2)));
            Assert.IsNotNull(system);

            _gameModeSystem.SelectMode(GameMode.Path);

            Assert.AreEqual(GameMode.Path, _gameModeSystem.CurrentMode.Value);
        }

        [Test]
        public void EnteringPathMode_StartsOnTheUnlockedFrontier()
        {
            PersistFrontier(3);
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2), Level(3), Level(4)));
            Assert.IsNotNull(system);

            _gameModeSystem.SelectMode(GameMode.Path);

            Assert.AreEqual(3, _pathRunModel.ActiveLevelNumber.Value);
            Assert.AreEqual("level_3", _objectiveModel.CurrentObjective.Definition.Id);
        }

        // --- AC2 / AC3: completing the objective ends the run, with the bonus in its score ---

        [Test]
        public void ObjectiveCompleted_InPathMode_EndsTheRunWithLevelCompleted()
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1, completionScoreBonus: 250), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);
            int gameOversBefore = _gameOverBroker.Published.Count;

            CompleteCurrentObjective();

            Assert.IsTrue(_boardSystem.IsGameOver);
            Assert.AreEqual(gameOversBefore + 1, _gameOverBroker.Published.Count);
            Assert.AreEqual(
                GameOverReason.LevelCompleted,
                _gameOverBroker.Published[_gameOverBroker.Published.Count - 1].Reason);
        }

        [Test]
        public void ObjectiveCompleted_InPathMode_AddsTheLevelsBonusToTheRunsScore()
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1, completionScoreBonus: 250), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);

            // Points the run had already earned, so the assertion is about the bonus being added to
            // the run's score rather than replacing it.
            _scoreSystem.AddLevelCompletionBonus(40);

            CompleteCurrentObjective();

            Assert.AreEqual(290, _scoreModel.Score.Value);
        }

        [Test]
        public void ObjectiveCompleted_InPathMode_OnALevelWithNoAuthoredBonus_AddsNothing()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);
            _scoreSystem.AddLevelCompletionBonus(40);

            CompleteCurrentObjective();

            Assert.AreEqual(40, _scoreModel.Score.Value);
        }

        [Test]
        public void ObjectiveCompleted_InPathMode_RefusesFurtherPlacements()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);

            CompleteCurrentObjective();

            // The board is empty and the tray was just refilled, so every slot would fit if the run
            // were still live — a refusal here can only be the run being over.
            Assert.IsFalse(_boardSystem.TryPlacePiece(0, new GridPosition(0, 0)));
        }

        // --- AC4: the cumulative path total ---

        [Test]
        public void PathTotal_SumsEachCompletedLevelsFinalScore()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(
                Level(1, completionScoreBonus: 100),
                Level(2, completionScoreBonus: 300),
                Level(3)));
            _gameModeSystem.SelectMode(GameMode.Path);

            // Level 1: 20 played points plus its 100 bonus.
            _scoreSystem.AddLevelCompletionBonus(20);
            CompleteCurrentObjective();

            Assert.AreEqual(120, _pathRunModel.PathTotalScore.Value);

            // Clearing level 1 moved the frontier, so level 2 is now startable.
            Assert.IsTrue(system.TryStartPathLevel(2));

            // Each level starts fresh: the run score reset with RunStartedMessage even though the
            // path total did not.
            Assert.AreEqual(0, _scoreModel.Score.Value);

            _scoreSystem.AddLevelCompletionBonus(50);
            CompleteCurrentObjective();

            Assert.AreEqual(350, _scoreModel.Score.Value);
            Assert.AreEqual(470, _pathRunModel.PathTotalScore.Value);
        }

        [Test]
        public void PathTotal_IsDiscardedOnLeavingPathMode()
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1, completionScoreBonus: 100), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);
            CompleteCurrentObjective();
            Assert.AreEqual(100, _pathRunModel.PathTotalScore.Value);

            _gameModeSystem.SelectMode(GameMode.Endless);

            Assert.AreEqual(0, _pathRunModel.PathTotalScore.Value);
            Assert.AreEqual(PathRunModel.NO_ACTIVE_LEVEL, _pathRunModel.ActiveLevelNumber.Value);
        }

        [Test]
        public void PathTotal_StartsOverWhenTheWalkRestartsFromTheFirstLevel()
        {
            PersistFrontier(3);
            LevelProgressionSystem system = CreateSystem(ACatalogOf(
                Level(1), Level(2), Level(3, completionScoreBonus: 500), Level(4)));
            _gameModeSystem.SelectMode(GameMode.Path);
            CompleteCurrentObjective();
            Assert.AreEqual(500, _pathRunModel.PathTotalScore.Value);

            Assert.IsTrue(system.TryStartPathLevel(1));

            Assert.AreEqual(0, _pathRunModel.PathTotalScore.Value);
        }

        // --- AC5: node taps are mode-aware ---

        [Test]
        public void TryStartPathLevel_InPathMode_OnAnUnlockedLevel_StartsARunThere()
        {
            PersistFrontier(4);
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1), Level(2), Level(3), Level(4)));
            _gameModeSystem.SelectMode(GameMode.Path);
            int runsBefore = _runStartedBroker.Published.Count;

            bool started = system.TryStartPathLevel(2);

            Assert.IsTrue(started);
            Assert.AreEqual(2, _pathRunModel.ActiveLevelNumber.Value);
            Assert.AreEqual("level_2", _objectiveModel.CurrentObjective.Definition.Id);
            Assert.AreEqual(runsBefore + 1, _runStartedBroker.Published.Count);
        }

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void TryStartPathLevel_OutsidePathMode_IsACompleteNoOp(GameMode mode)
        {
            PersistFrontier(4);
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1), Level(2), Level(3), Level(4)));
            _gameModeSystem.SelectMode(mode);
            int runsBefore = _runStartedBroker.Published.Count;

            bool started = system.TryStartPathLevel(2);

            Assert.IsFalse(started);
            Assert.AreEqual(PathRunModel.NO_ACTIVE_LEVEL, _pathRunModel.ActiveLevelNumber.Value);

            // The tracked objective is still the frontier's, and no run was thrown away.
            Assert.AreEqual("level_4", _objectiveModel.CurrentObjective.Definition.Id);
            Assert.AreEqual(runsBefore, _runStartedBroker.Published.Count);
        }

        [Test]
        public void TryStartPathLevel_OnALockedLevel_IsRefused()
        {
            PersistFrontier(2);
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1), Level(2), Level(3), Level(4)));
            _gameModeSystem.SelectMode(GameMode.Path);
            int runsBefore = _runStartedBroker.Published.Count;

            Assert.IsFalse(system.TryStartPathLevel(3));
            Assert.AreEqual(2, _pathRunModel.ActiveLevelNumber.Value);
            Assert.AreEqual(runsBefore, _runStartedBroker.Published.Count);
        }

        [Test]
        public void TryStartPathLevel_OnALevelTheCatalogDoesNotAuthor_IsRefused()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2)));
            _gameModeSystem.SelectMode(GameMode.Path);

            Assert.IsFalse(system.TryStartPathLevel(0));
            Assert.IsFalse(system.TryStartPathLevel(9));
        }

        /// <summary>
        /// The corruption this issue's trickiest interaction could cause: replaying a level the player
        /// has long since cleared must not drag the persisted frontier back to just after it, and must
        /// not re-pay that level's power-up — which would turn any cleared rewarding level into an
        /// unlimited reward farm. Level 2 therefore authors a reward here specifically so that the
        /// second half of that has something to fail on.
        /// </summary>
        [Test]
        public void ReplayingAClearedLevel_LeavesTheFrontierWhereItWas()
        {
            PersistFrontier(5);
            LevelProgressionSystem system = CreateSystem(ACatalogOf(
                Level(1),
                Level(2, completionScoreBonus: 70, grantsLevelUpReward: true),
                Level(3), Level(4), Level(5), Level(6)));
            _gameModeSystem.SelectMode(GameMode.Path);
            Assert.IsTrue(system.TryStartPathLevel(2));
            int advancesBefore = _levelAdvancedBroker.Published.Count;
            int grantsBefore = _powerUpGrantedBroker.Published.Count;

            CompleteCurrentObjective();

            Assert.AreEqual(5, _progressionModel.CurrentLevelNumber.Value);
            Assert.AreEqual(advancesBefore, _levelAdvancedBroker.Published.Count);

            // Not one power-up: the reward is payment for a first clear, and this was not one.
            Assert.AreEqual(grantsBefore, _powerUpGrantedBroker.Published.Count);

            // The run still ended as a success and still paid the level's bonus — only the frontier
            // is protected.
            Assert.AreEqual(
                GameOverReason.LevelCompleted,
                _gameOverBroker.Published[_gameOverBroker.Published.Count - 1].Reason);
            Assert.AreEqual(70, _scoreModel.Score.Value);
        }

        /// <summary>
        /// The positive counterpart to <see cref="ReplayingAClearedLevel_LeavesTheFrontierWhereItWas"/>.
        /// Level 1 authors the same reward that test proves is withheld on a replay, so "no power-up was
        /// granted" there is a real guard rather than a rewardless catalog passing by default.
        /// </summary>
        [Test]
        public void ClearingTheFrontierLevel_InPathMode_MovesAndPersistsTheFrontier()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(
                Level(1, grantsLevelUpReward: true), Level(2), Level(3)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);

            CompleteCurrentObjective();

            Assert.AreEqual(2, _progressionModel.CurrentLevelNumber.Value);
            Assert.AreEqual(1, _levelAdvancedBroker.Published.Count);
            Assert.AreEqual(2, _levelAdvancedBroker.Published[0].CurrentLevelNumber);
            Assert.IsTrue(PlayerPrefs.GetString(LEVEL_SAVE_KEY, string.Empty).Contains("\"currentLevelNumber\":2"));

            // A first clear does pay the level's reward.
            Assert.AreEqual(1, _powerUpGrantedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.RowClear, _powerUpGrantedBroker.Published[0].Kind);
        }

        // --- AC6: failing a Path level ends the run distinguishably ---

        [Test]
        public void NoMovesLeft_InPathMode_EndsTheRunWithNoMovesLeft()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);

            // An empty dock rather than a full board: with nothing left to place there is no move,
            // whatever the board looks like. Stated this way since issue #129, because a board filled to
            // the brim now earns a one-off demolition hammer instead of ending the run — and this test
            // is about what Path mode does with a NoMovesLeft verdict, not about how one is reached.
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _trayModel.SetSlot(slotIndex, null, Board.EMPTY);
            }

            _boardSystem.RecheckGameOver();

            Assert.IsTrue(_boardSystem.IsGameOver);
            Assert.AreEqual(
                GameOverReason.NoMovesLeft,
                _gameOverBroker.Published[_gameOverBroker.Published.Count - 1].Reason);

            // The objective was never met, so nothing was paid and the frontier did not move.
            Assert.AreEqual(0, _pathRunModel.PathTotalScore.Value);
            Assert.AreEqual(1, _progressionModel.CurrentLevelNumber.Value);
        }

        [Test]
        public void ALevelCompletedOnADeadBoard_PublishesOnlyTheSuccess()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);
            int gameOversBefore = _gameOverBroker.Published.Count;

            // The board is dead, but the level completes first. Only one verdict may be published,
            // and it must be the success — a second NoMovesLeft on top would contradict the card the
            // player is being shown.
            FillBoard();
            CompleteCurrentObjective();
            _boardSystem.RecheckGameOver();

            Assert.AreEqual(gameOversBefore + 1, _gameOverBroker.Published.Count);
            Assert.AreEqual(
                GameOverReason.LevelCompleted,
                _gameOverBroker.Published[gameOversBefore].Reason);
        }

        // --- AC7: the regression guard. Endless and Timed must be untouched ---

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void ObjectiveCompleted_OutsidePathMode_AdvancesTheLevelWithoutEndingTheRun(GameMode mode)
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1, completionScoreBonus: 999), Level(2), Level(3)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(mode);
            int gameOversBefore = _gameOverBroker.Published.Count;

            CompleteCurrentObjective();

            // The run continues, exactly as it did before Path mode existed.
            Assert.IsFalse(_boardSystem.IsGameOver);
            Assert.AreEqual(gameOversBefore, _gameOverBroker.Published.Count);

            // And the level rolled straight onto the next objective.
            Assert.AreEqual(2, _progressionModel.CurrentLevelNumber.Value);
            Assert.AreEqual("level_2", _objectiveModel.CurrentObjective.Definition.Id);
            Assert.AreEqual(1, _levelAdvancedBroker.Published.Count);
            Assert.AreEqual(2, _levelAdvancedBroker.Published[0].CurrentLevelNumber);
        }

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void ObjectiveCompleted_OutsidePathMode_PaysNoCompletionScoreBonus(GameMode mode)
        {
            // The authored bonus is deliberately enormous: if any of it leaked into a non-Path run it
            // could not be mistaken for ordinary scoring.
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1, completionScoreBonus: 999), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(mode);

            CompleteCurrentObjective();

            Assert.AreEqual(0, _scoreModel.Score.Value);
            Assert.AreEqual(0, _pathRunModel.PathTotalScore.Value);
        }

        [Test]
        public void SwitchingBetweenEndlessAndTimed_LeavesTheTrackedObjectiveAlone()
        {
            PersistFrontier(2);
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2), Level(3)));
            Assert.IsNotNull(system);
            ObjectiveProgress before = _objectiveModel.CurrentObjective;

            _gameModeSystem.SelectMode(GameMode.Timed);

            // The very same instance, not merely an equal one: a mode switch between these two has
            // never rebuilt the objective and must not start now.
            Assert.AreSame(before, _objectiveModel.CurrentObjective);
        }

        [Test]
        public void LeavingPathMode_RestoresTheFrontiersObjective()
        {
            PersistFrontier(3);
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2), Level(3), Level(4)));
            _gameModeSystem.SelectMode(GameMode.Path);
            Assert.IsTrue(system.TryStartPathLevel(1));
            Assert.AreEqual("level_1", _objectiveModel.CurrentObjective.Definition.Id);

            _gameModeSystem.SelectMode(GameMode.Endless);

            Assert.AreEqual("level_3", _objectiveModel.CurrentObjective.Definition.Id);
        }

        // --- Subscriber-order integration guard ---

        /// <summary>
        /// Drives a REAL placement through the REAL <see cref="ObjectiveSystem"/> and
        /// <see cref="ScoreSystem"/> (not the <see cref="CompleteCurrentObjective"/> stand-in every
        /// other test in this file uses), because this is the one behaviour that depends on which of
        /// the two resolves first in <c>GameLifetimeScope</c> — a resolve-order fact no test that
        /// bypasses <see cref="ObjectiveSystem"/> could ever exercise.
        /// <para>
        /// <see cref="ObjectiveSystem"/> publishes <see cref="ObjectiveCompletedMessage"/> synchronously
        /// from inside its own <see cref="PiecePlacedMessage"/> handler, so
        /// <see cref="LevelProgressionSystem.CompletePathLevel"/>'s <c>AddLevelCompletionBonus</c> call
        /// runs nested inside the SAME <c>PiecePlacedMessage</c> publish loop that credits the placement
        /// its own score. If <see cref="ObjectiveSystem"/> were subscribed to <c>PiecePlacedMessage</c>
        /// before <see cref="ScoreSystem"/>, the bonus would be added to a score that does not yet
        /// include this placement's own points — <c>ScoreModel.Score</c> would still settle on the
        /// right total once <see cref="ScoreSystem"/>'s handler ran (addition is commutative), but the
        /// snapshot <c>CompletePathLevel</c> takes for <see cref="PathRunModel.RecordLevelCompletion"/>
        /// would be captured too early and undercount by exactly the placement's own score — visible
        /// only in the path total, not in the run's own score. This test constructs
        /// <see cref="ScoreSystem"/> then <see cref="ObjectiveSystem"/>, matching
        /// <c>GameLifetimeScope</c>'s real registration order, and would fail if that order were ever
        /// reversed.
        /// </para>
        /// </summary>
        [Test]
        public void ARealPlacementThatCompletesTheLevel_BanksTheFullScoreIncludingThePlacementsOwnPoints()
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1, completionScoreBonus: 250), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);

            // ScoreSystem is already subscribed (built inside CreateSystem). Constructing
            // ObjectiveSystem here subscribes it to the same _piecePlacedBroker strictly afterwards —
            // the same order GameLifetimeScope resolves them in.
            var objectiveSystem = new ObjectiveSystem(
                _objectiveModel,
                new RunPauseModel(),
                _piecePlacedBroker,
                _runStartedBroker,
                _scoreChangedBroker,
                new TestMessageBroker<PowerUpAppliedMessage>(),
                _objectiveProgressBroker,
                _objectiveCompletedBroker);
            Assert.IsNotNull(objectiveSystem);

            // Fill row 0 everywhere except (0,0); a 1x1 piece dropped there completes the row, clearing
            // exactly one line — level 1's SimultaneousLineClear{count:1} objective.
            for (int x = 1; x < Board.SIZE; x++)
            {
                _boardModel.Occupy(new GridPosition(x, 0), 1);
            }

            _trayModel.SetSlot(0, FindPiece("single_1x1"), colourId: 1);

            bool placed = _boardSystem.TryPlacePiece(0, new GridPosition(0, 0));

            Assert.IsTrue(placed);
            Assert.IsTrue(_boardSystem.IsGameOver, "The placement should have completed the level.");

            // The placement's own line-clear score plus the level's 250 bonus, all landed in the one
            // run's score — provably the full amount, not a bonus-only total that would result from the
            // bonus being added before the placement's own credit.
            Assert.Greater(_scoreModel.Score.Value, 250, "The placement's own score must be included.");
            Assert.AreEqual(_scoreModel.Score.Value, _pathRunModel.PathTotalScore.Value);
        }

        // --- Cumulative-scope replay: the bug the "start fresh" decision fixes ---

        /// <summary>
        /// The bug this fixes: <see cref="ObjectiveProgress.RestoreProgress"/> sets <c>IsComplete</c>
        /// straight from the saved value, and <see cref="ObjectiveSystem"/> only ever publishes
        /// <see cref="ObjectiveCompletedMessage"/> on the false-to-true edge — so restoring an
        /// already-complete Cumulative objective at the start of a Path run left it permanently unable
        /// to complete again. A real <see cref="ObjectiveSystem"/> is required here: the
        /// <see cref="CompleteCurrentObjective"/> stand-in every other test uses publishes the
        /// completion directly and would never have caught this — it does not go through the
        /// false-to-true check at all.
        /// </summary>
        [Test]
        public void ReplayingAClearedCumulativeLevel_CanBeCompletedAgain()
        {
            PersistFrontierWithCumulativeProgress(5, "level_2", alreadyAtValue: 1);
            LevelProgressionSystem system = CreateSystem(ACatalogOf(
                Level(1), Level(2, scope: ObjectiveScope.Cumulative), Level(3), Level(4), Level(5)));
            var objectiveSystem = new ObjectiveSystem(
                _objectiveModel,
                new RunPauseModel(),
                _piecePlacedBroker,
                _runStartedBroker,
                _scoreChangedBroker,
                new TestMessageBroker<PowerUpAppliedMessage>(),
                _objectiveProgressBroker,
                _objectiveCompletedBroker);
            Assert.IsNotNull(objectiveSystem);
            _gameModeSystem.SelectMode(GameMode.Path);

            Assert.IsTrue(system.TryStartPathLevel(2));
            Assert.IsFalse(
                _objectiveModel.CurrentObjective.IsComplete,
                "A Path run must start the objective fresh, not restored-and-already-done.");

            for (int x = 1; x < Board.SIZE; x++)
            {
                _boardModel.Occupy(new GridPosition(x, 0), 1);
            }

            _trayModel.SetSlot(0, FindPiece("single_1x1"), colourId: 1);
            bool placed = _boardSystem.TryPlacePiece(0, new GridPosition(0, 0));

            Assert.IsTrue(placed);
            Assert.IsTrue(_boardSystem.IsGameOver, "The replay must be completable, not stuck at NoMovesLeft forever.");
            Assert.AreEqual(
                GameOverReason.LevelCompleted,
                _gameOverBroker.Published[_gameOverBroker.Published.Count - 1].Reason);
        }

        /// <summary>
        /// The corollary this session's product decision explicitly accepted: a Path run of a
        /// Cumulative-scope level never writes that level's persisted save entry, so it can neither
        /// resurrect a stale value nor regress a value earned through Endless/Timed play. Only
        /// completion is skipped by starting fresh — the save entry itself is untouched either way.
        /// </summary>
        [Test]
        public void PlayingACumulativeLevelInPathMode_NeverWritesItsSavedProgress()
        {
            PersistFrontierWithCumulativeProgress(5, "level_2", alreadyAtValue: 1);
            LevelProgressionSystem system = CreateSystem(ACatalogOf(
                Level(1), Level(2, scope: ObjectiveScope.Cumulative), Level(3), Level(4), Level(5)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);
            Assert.IsTrue(system.TryStartPathLevel(2));

            // Progress changes on the fresh, in-progress attempt — none of this may reach the save file.
            _objectiveProgressBroker.Publish(
                new ObjectiveProgressChangedMessage("level_2", currentValue: 0, targetValue: 1));

            string saveBlob = PlayerPrefs.GetString(LEVEL_SAVE_KEY, string.Empty);
            Assert.IsTrue(
                saveBlob.Contains("\"objectiveId\":\"level_2\",\"currentValue\":1"),
                "The Endless/Timed cumulative value for level_2 must survive a Path attempt untouched.");
        }

        // --- Path total on a replay: each level counts once, at its best ---

        /// <summary>
        /// Deliberately replays level 2, never level 1. Restarting the walk from the FIRST level is
        /// itself a <c>ResetWalk</c> (see <see cref="LevelProgressionSystem.TryStartPathLevel"/>), which
        /// empties the best-score table — so a version of this test that replayed level 1 would clear
        /// the very bookkeeping it means to exercise and would pass just as happily against a naive
        /// "add every completion to the total" implementation. Replaying a mid-walk level is the only
        /// way to reach <see cref="PathRunModel.RecordLevelCompletion"/> with a non-zero previous best.
        /// </summary>
        [Test]
        public void PathTotal_OnARepeatedCompletionOfTheSameLevel_OnlyCountsTheImprovement()
        {
            PersistFrontier(2);
            LevelProgressionSystem system = CreateSystem(ACatalogOf(
                Level(1), Level(2, completionScoreBonus: 100), Level(3)));
            Assert.IsNotNull(system);

            // Entering Path mode starts on the frontier, which is level 2.
            _gameModeSystem.SelectMode(GameMode.Path);
            Assert.AreEqual(2, _pathRunModel.ActiveLevelNumber.Value);

            CompleteCurrentObjective();
            Assert.AreEqual(100, _pathRunModel.PathTotalScore.Value);

            // The same level again, an identical result. The walk is NOT reset (level 2 is not the
            // first level), so this is a genuine repeat: the total must not move.
            Assert.IsTrue(system.TryStartPathLevel(2));
            CompleteCurrentObjective();
            Assert.AreEqual(
                100,
                _pathRunModel.PathTotalScore.Value,
                "A repeat that beats nothing must not be counted a second time.");

            // A third attempt, 50 points better: the total rises by exactly the improvement (50), not
            // by the replay's full 150.
            Assert.IsTrue(system.TryStartPathLevel(2));
            _scoreSystem.AddLevelCompletionBonus(50);
            CompleteCurrentObjective();
            Assert.AreEqual(150, _pathRunModel.PathTotalScore.Value);

            // A fourth attempt, better than the FIRST (100) but worse than the best so far (150). The
            // total must stay at the best, not creep up by the difference against some earlier figure.
            Assert.IsTrue(system.TryStartPathLevel(2));
            _scoreSystem.AddLevelCompletionBonus(20);
            CompleteCurrentObjective();
            Assert.AreEqual(
                150,
                _pathRunModel.PathTotalScore.Value,
                "Only the best score per level counts — a mid-range replay changes nothing.");
        }

        // --- Multi-objective levels: a level may be authored as several rows sharing one level number ---

        [Test]
        public void ALevelAuthoredWithTwoObjectives_TracksBothOfThem()
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1), Level(1, objectiveType: ObjectiveType.BoardWipeCount), Level(2)));
            Assert.IsNotNull(system);

            Assert.AreEqual(2, _objectiveModel.TrackedObjectives.Count);
        }

        [Test]
        public void ALevelsFirstObjective_KeepsTheUnsuffixedId_AndLaterOnesAreSuffixed()
        {
            // The id is the save key for cumulative progress, so the first row's id must be byte
            // identical to what a single-objective level has always produced — anything else orphans
            // every shipped player's saved progress for that level.
            LevelProgressionSystem system = CreateSystem(ACatalogOf(
                Level(1),
                Level(1, objectiveType: ObjectiveType.BoardWipeCount),
                Level(1, objectiveType: ObjectiveType.RowAndColumnCrossClear),
                Level(2)));
            Assert.IsNotNull(system);

            IReadOnlyList<ObjectiveProgress> tracked = _objectiveModel.TrackedObjectives;
            Assert.AreEqual(3, tracked.Count);
            Assert.AreEqual("level_1", tracked[0].Definition.Id);
            Assert.AreEqual("level_1_1", tracked[1].Definition.Id);
            Assert.AreEqual("level_1_2", tracked[2].Definition.Id);
        }

        [Test]
        public void ASingleObjectiveLevel_StillProducesTheBareId()
        {
            LevelProgressionSystem system = CreateSystem(ACatalogOf(Level(1), Level(2)));
            Assert.IsNotNull(system);

            Assert.AreEqual("level_1", _objectiveModel.CurrentObjective.Definition.Id);
        }

        [Test]
        public void CompletingOnlyOneOfTwoObjectives_DoesNotAdvanceTheLevel()
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1), Level(1, objectiveType: ObjectiveType.BoardWipeCount), Level(2)));
            Assert.IsNotNull(system);

            CompleteObjectiveAt(0);

            Assert.AreEqual(
                1,
                _progressionModel.CurrentLevelNumber.Value,
                "A level asking for two objectives is cleared by the last of them, not the first.");
            Assert.AreEqual(0, _levelAdvancedBroker.Published.Count);
        }

        [Test]
        public void CompletingBothObjectives_AdvancesTheLevelOnce()
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1), Level(1, objectiveType: ObjectiveType.BoardWipeCount), Level(2)));
            Assert.IsNotNull(system);

            CompleteObjectiveAt(0);
            CompleteObjectiveAt(1);

            Assert.AreEqual(2, _progressionModel.CurrentLevelNumber.Value);
            Assert.AreEqual(1, _levelAdvancedBroker.Published.Count);
        }

        [Test]
        public void CompletingBothObjectives_InPathMode_EndsTheRunOnlyOnTheSecond()
        {
            LevelProgressionSystem system = CreateSystem(
                ACatalogOf(Level(1), Level(1, objectiveType: ObjectiveType.BoardWipeCount), Level(2)));
            Assert.IsNotNull(system);
            _gameModeSystem.SelectMode(GameMode.Path);

            CompleteObjectiveAt(0);
            Assert.IsFalse(_boardSystem.IsGameOver, "One of two objectives is not a cleared level.");

            CompleteObjectiveAt(1);
            Assert.IsTrue(_boardSystem.IsGameOver);
            Assert.AreEqual(
                GameOverReason.LevelCompleted,
                _gameOverBroker.Published[_gameOverBroker.Published.Count - 1].Reason);
        }

        [Test]
        public void SetObjectives_ReplacesTheTrackedList_AndIsNullAndEmptySafe()
        {
            var model = new ObjectiveModel();
            ObjectiveProgress first = AnObjective("a");
            ObjectiveProgress second = AnObjective("b");

            model.SetObjectives(new List<ObjectiveProgress> { first, second });
            Assert.AreEqual(2, model.TrackedObjectives.Count);
            Assert.AreSame(first, model.CurrentObjective);

            // Replaces wholesale rather than appending — otherwise the previous level's objectives
            // would go on collecting placements.
            model.SetObjectives(new List<ObjectiveProgress> { second });
            Assert.AreEqual(1, model.TrackedObjectives.Count);
            Assert.AreSame(second, model.TrackedObjectives[0]);

            model.SetObjectives(new List<ObjectiveProgress>());
            Assert.AreEqual(0, model.TrackedObjectives.Count);
            Assert.IsNull(model.CurrentObjective);

            model.SetObjectives(new List<ObjectiveProgress> { first });
            model.SetObjectives(null);
            Assert.AreEqual(0, model.TrackedObjectives.Count);

            // A null entry is dropped rather than stored: everything downstream dereferences Definition.
            model.SetObjectives(new List<ObjectiveProgress> { null, first, null });
            Assert.AreEqual(1, model.TrackedObjectives.Count);
            Assert.AreSame(first, model.TrackedObjectives[0]);
        }

        // --- Fixture helpers ---

        private static ObjectiveProgress AnObjective(string id)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                id, ObjectiveType.BoardWipeCount, ObjectiveScope.PerRun, targetValue: 1));
        }

        /// <summary>Announces the completion of the tracked objective at <paramref name="objectiveIndex"/>,
        /// the multi-objective form of <see cref="CompleteCurrentObjective"/>.</summary>
        private void CompleteObjectiveAt(int objectiveIndex)
        {
            IReadOnlyList<ObjectiveProgress> tracked = _objectiveModel.TrackedObjectives;
            Assert.Greater(tracked.Count, objectiveIndex, "Fewer objectives are tracked than expected.");

            _objectiveCompletedBroker.Publish(
                new ObjectiveCompletedMessage(tracked[objectiveIndex].Definition.Id));
        }

        /// <summary>
        /// Drives the objective the system is tracking to completion and announces it, standing in for
        /// the placement that would have done it in a real run. <see cref="ObjectiveSystem"/> is not
        /// in the loop on purpose: what is under test is what
        /// <see cref="LevelProgressionSystem"/> does with the completion, not what produces it.
        /// </summary>
        private void CompleteCurrentObjective()
        {
            ObjectiveProgress objective = _objectiveModel.CurrentObjective;
            Assert.IsNotNull(objective, "No objective is being tracked — the catalog or the mode is wrong.");

            _objectiveCompletedBroker.Publish(new ObjectiveCompletedMessage(objective.Definition.Id));
        }

        private void FillBoard()
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    _boardModel.Occupy(new GridPosition(x, y), 1);
                }
            }
        }

        /// <summary>Writes the save blob the system reads at construction, so a test can start the
        /// player part-way up the ladder without having to clear their way there.</summary>
        private static void PersistFrontier(int levelNumber)
        {
            PlayerPrefs.SetString(
                LEVEL_SAVE_KEY,
                $"{{\"schemaVersion\":1,\"currentLevelNumber\":{levelNumber},\"cumulativeProgress\":[]}}");
        }

        /// <summary>Like <see cref="PersistFrontier"/>, but also seeds one Cumulative objective's saved
        /// progress — simulating a level that was cleared (or partly progressed) before this test run.</summary>
        private static void PersistFrontierWithCumulativeProgress(
            int levelNumber, string objectiveId, int alreadyAtValue)
        {
            PlayerPrefs.SetString(
                LEVEL_SAVE_KEY,
                "{\"schemaVersion\":1,"
                + $"\"currentLevelNumber\":{levelNumber},"
                + $"\"cumulativeProgress\":[{{\"objectiveId\":\"{objectiveId}\",\"currentValue\":{alreadyAtValue}}}]}}");
        }

        private LevelProgressionSystem CreateSystem(LevelCatalog catalog)
        {
            _progressionModel = new LevelProgressionModel();
            _pathRunModel = new PathRunModel();
            _objectiveModel = new ObjectiveModel();
            _scoreModel = new ScoreModel();
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();

            _boardSystem = new BoardSystem(
                _boardModel,
                _trayModel,
                new PerfectRoundModel(),
                new WeightedPieceDraw(),
                _runStartedBroker,
                _piecePlacedBroker,
                new TestMessageBroker<LinesClearedMessage>(),
                _gameOverBroker,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>());

            _gameModeSystem = new GameModeSystem(new GameModeModel(), _boardSystem);

            _scoreSystem = new ScoreSystem(
                _scoreModel,
                _gameModeSystem,
                new DoubleMultiplierModel(),
                // A real rule, not an empty list: the two tests that drive an actual TryPlacePiece
                // (the subscriber-order guard and the cumulative-replay guard) need the placement itself
                // to score something nonzero, or they could not tell "the placement's points landed"
                // apart from "they didn't". Every other test in this file bypasses placement entirely
                // via CompleteCurrentObjective, so this is inert for them.
                new List<IScoreRule> { new PlacementScoreRule() },
                _piecePlacedBroker,
                _runStartedBroker,
                _scoreChangedBroker,
                new TestMessageBroker<NewRecordMessage>(),
                new TestMessageBroker<BonusScoredMessage>());

            var system = new LevelProgressionSystem(
                _progressionModel,
                _pathRunModel,
                _objectiveModel,
                catalog,
                CreatePowerUpSystem(),
                _gameModeSystem,
                _boardSystem,
                _scoreSystem,
                _objectiveCompletedBroker,
                _objectiveProgressBroker,
                _levelAdvancedBroker);

            // Starts the first run, as BoardSystem's IStartable entry point does in the scene, so the
            // tray holds pieces and IsGameOver is a real answer rather than its default.
            _boardSystem.StartNewRun();
            return system;
        }

        /// <summary>
        /// A real <see cref="PowerUpSystem"/>, because the level-up reward path runs through it. None
        /// of these tests authors a rewarding level, so it is only ever constructed, never spent from.
        /// </summary>
        private PowerUpSystem CreatePowerUpSystem()
        {
            var powerUpAppliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            var doubleMultiplierModel = new DoubleMultiplierModel();
            var doubleMultiplierSystem = new DoubleMultiplierSystem(
                doubleMultiplierModel,
                new RunPauseModel(),
                _runStartedBroker,
                _gameOverBroker);

            var ghostFitModel = new GhostFitModel();
            var ghostFitSystem = new GhostFitSystem(
                ghostFitModel,
                _boardModel,
                _trayModel,
                _scoreModel,
                _runStartedBroker,
                _gameOverBroker,
                _piecePlacedBroker,
                powerUpAppliedBroker);

            var timerRunSystem = new TimerRunSystem(
                new TimerModel(),
                new RunPauseModel(),
                _gameModeSystem,
                new TimedModeSystem(new TimedModeModel(), ScriptableObject.CreateInstance<TimedModeConfig>()),
                _boardSystem,
                new TestMessageBroker<TrayRefilledMessage>(),
                _gameOverBroker);

            return new PowerUpSystem(
                new PowerUpModel(),
                _progressionModel,
                _boardModel,
                _trayModel,
                _boardSystem,
                timerRunSystem,
                doubleMultiplierSystem,
                ghostFitSystem,
                ghostFitModel,
                new StubRewardSource(),
                powerUpAppliedBroker,
                _powerUpGrantedBroker,
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                _runStartedBroker,
                _gameOverBroker);
        }

        /// <summary>
        /// Builds a catalog from authored rows. Goes through <see cref="JsonUtility"/> rather than
        /// reflection because the serialized field names are the asset's own contract: a row written
        /// this way is exactly what the Inspector would have produced.
        /// </summary>
        private static LevelCatalog ACatalogOf(params string[] levels)
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            JsonUtility.FromJsonOverwrite($"{{\"_levels\":[{string.Join(",", levels)}]}}", catalog);
            return catalog;
        }

        /// <summary>One authored objective row, clearable by a single one-line clear by default. Two
        /// rows sharing a level number is how a level asks for two objectives at once.</summary>
        private static string Level(
            int levelNumber,
            int completionScoreBonus = 0,
            bool grantsLevelUpReward = false,
            ObjectiveScope scope = ObjectiveScope.PerRun,
            ObjectiveType objectiveType = ObjectiveType.SimultaneousLineClear)
        {
            return "{"
                + $"\"_levelNumber\":{levelNumber},"
                + $"\"_objectiveType\":{(int)objectiveType},"
                + $"\"_scope\":{(int)scope},"
                + "\"_targetValue\":1,"
                + "\"_requiredLineCount\":1,"
                + $"\"_grantsLevelUpReward\":{(grantsLevelUpReward ? "true" : "false")},"
                + $"\"_levelUpReward\":{(int)PowerUpKind.RowClear},"
                + $"\"_completionScoreBonus\":{completionScoreBonus}"
                + "}";
        }

        private static Piece FindPiece(string id)
        {
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                if (piece.Id == id)
                {
                    return piece;
                }
            }

            Assert.Fail($"Piece '{id}' not found in catalog.");
            return null;
        }

        /// <summary>Never asked for anything: a level-up reward goes through <c>GrantDirect</c>, which
        /// bypasses the rewarded-ad gate entirely.</summary>
        private sealed class StubRewardSource : IRewardSource
        {
            public UniTask<RewardResult> RequestRewardAsync(
                PowerUpKind kind, CancellationToken cancellationToken)
                => UniTask.FromResult(new RewardResult(kind, granted: false));
        }
    }
}
