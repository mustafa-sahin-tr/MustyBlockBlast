using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the one rule that has no other test anywhere: a "Bomb-induced line clear" objective
    /// must advance ONLY off a Bomb clear that actually emptied a line — Row Clear and Column Clear
    /// trivially empty their own target line every time they're used, so the <c>PowerUpKind</c>
    /// filter in <see cref="ObjectiveSystem.OnPowerUpApplied"/> is the sole thing standing between a
    /// routine Row Clear and a wrongly-credited "Bomb synergy" objective.
    /// </summary>
    public class ObjectiveSystemTests
    {
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<ScoreChangedMessage> _scoreChangedBroker;
        private TestMessageBroker<PowerUpAppliedMessage> _powerUpAppliedBroker;
        private TestMessageBroker<ObjectiveProgressChangedMessage> _progressChangedBroker;
        private TestMessageBroker<ObjectiveCompletedMessage> _completedBroker;
        private ObjectiveModel _objectiveModel;
        private RunPauseModel _runPauseModel;
        private ObjectiveSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _scoreChangedBroker = new TestMessageBroker<ScoreChangedMessage>();
            _powerUpAppliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _progressChangedBroker = new TestMessageBroker<ObjectiveProgressChangedMessage>();
            _completedBroker = new TestMessageBroker<ObjectiveCompletedMessage>();
            _objectiveModel = new ObjectiveModel();
            _runPauseModel = new RunPauseModel();
            _system = new ObjectiveSystem(
                _objectiveModel, _runPauseModel, _piecePlacedBroker, _runStartedBroker, _scoreChangedBroker,
                _powerUpAppliedBroker, _progressChangedBroker, _completedBroker);
        }

        /// <summary>A placement that cleared one row, with only the reinforced-cell report varying —
        /// everything else is the same ordinary clear, so the only thing under test is whether the
        /// count threads through <see cref="ObjectiveSystem.OnPiecePlaced"/>'s context.</summary>
        private static PiecePlacedMessage APlacement(int reinforcedCellsFullyClearedCount)
        {
            return new PiecePlacedMessage(
                pieceId: "line_h4", anchor: new GridPosition(0, 0), pieceFamily: PieceFamily.Line,
                cellCount: 4, colourId: 1, linesCleared: 1, rowsCleared: 1, columnsCleared: 0,
                monochromeLineCount: 0, boardEmptyAfterPlacement: false,
                occupiedCellCountBeforeClear: 0, anyCornerCleared: false,
                centerCoreEmptyAfterPlacement: false, hasIsolatedHolesAfterPlacement: false,
                destroyedScoreGemCount: 0,
                reinforcedCellsFullyClearedCount: reinforcedCellsFullyClearedCount);
        }

        private static ObjectiveProgress BombLineObjective(int targetValue = 1)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "bomb_line", ObjectiveType.BombInducedLineClear, ObjectiveScope.PerRun, targetValue));
        }

        [Test]
        public void OnPowerUpApplied_Bomb_EmptiedALine_AdvancesTheObjective()
        {
            _objectiveModel.SetCurrentObjective(BombLineObjective());

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Bomb, clearedCellCount: 3, clearedLineCount: 0, emptiedLineCount: 1));

            Assert.AreEqual(1, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsTrue(_objectiveModel.CurrentObjective.IsComplete);
            Assert.AreEqual(1, _completedBroker.Published.Count);
        }

        [TestCase(PowerUpKind.RowClear)]
        [TestCase(PowerUpKind.ColumnClear)]
        [TestCase(PowerUpKind.Joker)]
        public void OnPowerUpApplied_NonBombKind_NeverAdvancesTheObjective_EvenWithLinesEmptied(PowerUpKind kind)
        {
            // Row Clear/Column Clear trivially report EmptiedLineCount >= 1 every time they're used
            // (PowerUpClearResolver's own documented behaviour) — this is exactly the case the Kind
            // filter exists to reject. If this test ever fails, every routine Row/Column Clear would
            // silently start completing "Bomb synergy" objectives.
            _objectiveModel.SetCurrentObjective(BombLineObjective());

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                kind, clearedCellCount: 8, clearedLineCount: 0, emptiedLineCount: 1));

            Assert.AreEqual(0, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsFalse(_objectiveModel.CurrentObjective.IsComplete);
            Assert.AreEqual(0, _completedBroker.Published.Count);
        }

        private static ObjectiveProgress RerollSaveObjective(int targetValue = 1)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "reroll_save", ObjectiveType.RerollSave, ObjectiveScope.PerRun, targetValue));
        }

        [Test]
        public void OnPowerUpApplied_Reroll_WasClutchSave_AdvancesTheObjective()
        {
            _objectiveModel.SetCurrentObjective(RerollSaveObjective());

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Reroll, clearedCellCount: 0, clearedLineCount: 0, emptiedLineCount: 0,
                wasClutchSave: true));

            Assert.AreEqual(1, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsTrue(_objectiveModel.CurrentObjective.IsComplete);
            Assert.AreEqual(1, _completedBroker.Published.Count);
        }

        [Test]
        public void OnPowerUpApplied_Reroll_NotAClutchSave_NeverAdvancesTheObjective()
        {
            // A reroll used while moves already existed — the negative case AC2 in issue #95 guards.
            _objectiveModel.SetCurrentObjective(RerollSaveObjective());

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Reroll, clearedCellCount: 0, clearedLineCount: 0, emptiedLineCount: 0,
                wasClutchSave: false));

            Assert.AreEqual(0, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsFalse(_objectiveModel.CurrentObjective.IsComplete);
            Assert.AreEqual(0, _completedBroker.Published.Count);
        }

        [Test]
        public void OnPowerUpApplied_Bomb_NeverAdvancesARerollSaveObjective_EvenWithLinesEmptied()
        {
            // Converse of the Bomb-kind filter test above: a Bomb event must not leak into a
            // RerollSave objective just because both are power-up-sourced events.
            _objectiveModel.SetCurrentObjective(RerollSaveObjective());

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Bomb, clearedCellCount: 3, clearedLineCount: 0, emptiedLineCount: 1));

            Assert.AreEqual(0, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsFalse(_objectiveModel.CurrentObjective.IsComplete);
            Assert.AreEqual(0, _completedBroker.Published.Count);
        }

        [Test]
        public void OnPowerUpApplied_Bomb_EmptiedNoLine_DoesNotAdvance()
        {
            _objectiveModel.SetCurrentObjective(BombLineObjective());

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Bomb, clearedCellCount: 3, clearedLineCount: 0, emptiedLineCount: 0));

            Assert.AreEqual(0, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.AreEqual(0, _progressChangedBroker.Published.Count);
        }

        [Test]
        public void OnPiecePlaced_NeverAdvancesABombInducedLineClearObjective()
        {
            _objectiveModel.SetCurrentObjective(BombLineObjective());

            _piecePlacedBroker.Publish(new PiecePlacedMessage(
                pieceId: "line_h4", anchor: new GridPosition(0, 0), pieceFamily: PieceFamily.Line,
                cellCount: 4, colourId: 1, linesCleared: 4, rowsCleared: 4, columnsCleared: 0,
                monochromeLineCount: 0, boardEmptyAfterPlacement: true, occupiedCellCountBeforeClear: 0, anyCornerCleared: false, centerCoreEmptyAfterPlacement: false, hasIsolatedHolesAfterPlacement: false));

            Assert.AreEqual(0, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.AreEqual(0, _completedBroker.Published.Count);
        }

        [Test]
        public void OnPiecePlaced_StillAdvancesAnUnrelatedObjectiveType_RegressionGuardOnTheRefactor()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "wipe", ObjectiveType.BoardWipeCount, ObjectiveScope.PerRun, targetValue: 1));
            _objectiveModel.SetCurrentObjective(objective);

            _piecePlacedBroker.Publish(new PiecePlacedMessage(
                pieceId: "single_1x1", anchor: new GridPosition(0, 0), pieceFamily: PieceFamily.Single,
                cellCount: 1, colourId: 1, linesCleared: 1, rowsCleared: 1, columnsCleared: 0,
                monochromeLineCount: 0, boardEmptyAfterPlacement: true, occupiedCellCountBeforeClear: 0, anyCornerCleared: false, centerCoreEmptyAfterPlacement: false, hasIsolatedHolesAfterPlacement: false));

            Assert.AreEqual(1, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
            Assert.AreEqual(1, _completedBroker.Published.Count);
        }

        [Test]
        public void OnPiecePlaced_ThreadsRowsAndColumnsClearedThroughToACrossClearObjective()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "cross", ObjectiveType.RowAndColumnCrossClear, ObjectiveScope.PerRun, targetValue: 1));
            _objectiveModel.SetCurrentObjective(objective);

            _piecePlacedBroker.Publish(new PiecePlacedMessage(
                pieceId: "square_2x2", anchor: new GridPosition(0, 0), pieceFamily: PieceFamily.Square,
                cellCount: 4, colourId: 1, linesCleared: 2, rowsCleared: 1, columnsCleared: 1,
                monochromeLineCount: 0, boardEmptyAfterPlacement: false, occupiedCellCountBeforeClear: 0, anyCornerCleared: false, centerCoreEmptyAfterPlacement: false, hasIsolatedHolesAfterPlacement: false));

            Assert.AreEqual(1, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void OnPiecePlaced_ThreadsOccupiedCellCountThroughToAClutchRecoveryObjective()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "clutch", ObjectiveType.ClutchRecoveryClear, ObjectiveScope.PerRun, targetValue: 1,
                requiredOccupancyThreshold: 52));
            _objectiveModel.SetCurrentObjective(objective);

            _piecePlacedBroker.Publish(new PiecePlacedMessage(
                pieceId: "line_h4", anchor: new GridPosition(0, 0), pieceFamily: PieceFamily.Line,
                cellCount: 4, colourId: 1, linesCleared: 1, rowsCleared: 1, columnsCleared: 0,
                monochromeLineCount: 0, boardEmptyAfterPlacement: false, occupiedCellCountBeforeClear: 60, anyCornerCleared: false, centerCoreEmptyAfterPlacement: false, hasIsolatedHolesAfterPlacement: false));

            Assert.AreEqual(1, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        private static ObjectiveProgress ReinforcedCellsObjective(int targetValue = 1)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "reinforced", ObjectiveType.ReinforcedCellsCleared, ObjectiveScope.PerRun, targetValue));
        }

        [Test]
        public void OnPiecePlaced_FinishingOffAReinforcedCell_AdvancesTheObjective()
        {
            _objectiveModel.SetCurrentObjective(ReinforcedCellsObjective());

            _piecePlacedBroker.Publish(APlacement(reinforcedCellsFullyClearedCount: 1));

            Assert.AreEqual(1, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsTrue(_objectiveModel.CurrentObjective.IsComplete);
            Assert.AreEqual(1, _completedBroker.Published.Count);
        }

        /// <summary>AC5 end to end: a placement whose clear only decremented a reinforced cell's hit
        /// count reports zero fully cleared, and the objective must not budge.</summary>
        [Test]
        public void OnPiecePlaced_OnlyDamagingAReinforcedCell_DoesNotAdvanceTheObjective()
        {
            _objectiveModel.SetCurrentObjective(ReinforcedCellsObjective());

            _piecePlacedBroker.Publish(APlacement(reinforcedCellsFullyClearedCount: 0));

            Assert.AreEqual(0, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsFalse(_objectiveModel.CurrentObjective.IsComplete);
            Assert.AreEqual(0, _progressChangedBroker.Published.Count);
            Assert.AreEqual(0, _completedBroker.Published.Count);
        }

        [Test]
        public void OnPiecePlaced_FinishingOffTwoReinforcedCellsAtOnce_AdvancesByTwo()
        {
            _objectiveModel.SetCurrentObjective(ReinforcedCellsObjective(targetValue: 3));

            _piecePlacedBroker.Publish(APlacement(reinforcedCellsFullyClearedCount: 2));

            Assert.AreEqual(2, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsFalse(_objectiveModel.CurrentObjective.IsComplete);
        }

        /// <summary>The second event source: a spent power-up that finished a reinforced cell off is the
        /// same destruction, arriving on the other message.</summary>
        [Test]
        public void OnPowerUpApplied_FinishingOffAReinforcedCell_AdvancesTheObjective()
        {
            _objectiveModel.SetCurrentObjective(ReinforcedCellsObjective());

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Bomb, clearedCellCount: 5, clearedLineCount: 0, emptiedLineCount: 0,
                wasClutchSave: false, destroyedScoreGemCount: 0, reinforcedCellsFullyClearedCount: 1));

            Assert.AreEqual(1, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.IsTrue(_objectiveModel.CurrentObjective.IsComplete);
            Assert.AreEqual(1, _completedBroker.Published.Count);
        }

        [Test]
        public void OnPowerUpApplied_ThatOnlyDamagedAReinforcedCell_DoesNotAdvanceTheObjective()
        {
            _objectiveModel.SetCurrentObjective(ReinforcedCellsObjective());

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.RowClear, clearedCellCount: 7, clearedLineCount: 0, emptiedLineCount: 0,
                wasClutchSave: false, destroyedScoreGemCount: 0, reinforcedCellsFullyClearedCount: 0));

            Assert.AreEqual(0, _objectiveModel.CurrentObjective.CurrentValue);
            Assert.AreEqual(0, _progressChangedBroker.Published.Count);
        }

        /// <summary>Why the reinforced branch is an independent <c>if</c> rather than chained onto the
        /// Bomb branch that early-returns: one Bomb application really can do both, and a reinforced-cell
        /// objective tracked alongside a Bomb one must still be credited.</summary>
        [Test]
        public void OnPowerUpApplied_ABombThatBothEmptiedALineAndFinishedAReinforcedCell_CreditsBoth()
        {
            ObjectiveProgress reinforced = ReinforcedCellsObjective();
            ObjectiveProgress bombLine = BombLineObjective();
            _objectiveModel.SetObjectives(new[] { reinforced, bombLine });

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Bomb, clearedCellCount: 5, clearedLineCount: 0, emptiedLineCount: 1,
                wasClutchSave: false, destroyedScoreGemCount: 0, reinforcedCellsFullyClearedCount: 1));

            Assert.AreEqual(1, reinforced.CurrentValue);
            Assert.AreEqual(1, bombLine.CurrentValue);
        }

        [Test]
        public void Dispose_ThenAPowerUpApplication_DoesNotAdvanceTheObjective()
        {
            _objectiveModel.SetCurrentObjective(BombLineObjective());
            _system.Dispose();

            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Bomb, clearedCellCount: 3, clearedLineCount: 0, emptiedLineCount: 1));

            Assert.AreEqual(0, _objectiveModel.CurrentObjective.CurrentValue);
        }
    }
}
