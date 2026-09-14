using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the objective rule engine: each of the four types advances only on a placement that
    /// actually qualifies, progress is clamped to the target, completion latches, and only per-run
    /// objectives are cleared at run start.
    /// </summary>
    public class ObjectiveProgressTests
    {
        private static ObjectivePlacementContext Placement(
            int linesCleared = 0,
            int rowsCleared = 0,
            int columnsCleared = 0,
            PieceFamily pieceFamily = PieceFamily.Single,
            int currentRunScore = 0,
            bool boardEmptyAfterPlacement = false,
            int currentStreak = 0)
        {
            return new ObjectivePlacementContext(
                linesCleared, rowsCleared, columnsCleared, pieceFamily, currentRunScore,
                boardEmptyAfterPlacement, currentStreak);
        }

        private static ObjectiveProgress StreakObjective(int targetValue)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "streak", ObjectiveType.StreakThreshold, ObjectiveScope.PerRun, targetValue));
        }

        private static ObjectiveProgress LineClearObjective(int requiredLineCount, int targetValue)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "lines", ObjectiveType.SimultaneousLineClear, ObjectiveScope.PerRun, targetValue,
                requiredLineCount: requiredLineCount));
        }

        private static ObjectiveProgress FamilyObjective(PieceFamily family, int targetValue)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "family", ObjectiveType.PieceFamilyCount, ObjectiveScope.PerRun, targetValue,
                requiredPieceFamily: family));
        }

        [Test]
        public void SimultaneousLineClear_ExactMatch_Increments()
        {
            ObjectiveProgress objective = LineClearObjective(requiredLineCount: 2, targetValue: 3);

            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 2)));
            Assert.AreEqual(1, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void SimultaneousLineClear_WrongLineCount_DoesNotIncrement(int linesCleared)
        {
            ObjectiveProgress objective = LineClearObjective(requiredLineCount: 2, targetValue: 3);

            Assert.IsFalse(objective.ApplyPlacement(Placement(linesCleared: linesCleared)));
            Assert.AreEqual(0, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void PieceFamilyCount_MatchingFamily_Increments()
        {
            ObjectiveProgress objective = FamilyObjective(PieceFamily.TShape, targetValue: 2);

            Assert.IsTrue(objective.ApplyPlacement(Placement(pieceFamily: PieceFamily.TShape)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [TestCase(PieceFamily.Single)]
        [TestCase(PieceFamily.Line)]
        [TestCase(PieceFamily.SShape)]
        public void PieceFamilyCount_WrongFamily_DoesNotIncrement(PieceFamily placedFamily)
        {
            ObjectiveProgress objective = FamilyObjective(PieceFamily.TShape, targetValue: 2);

            Assert.IsFalse(objective.ApplyPlacement(Placement(pieceFamily: placedFamily)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void BoardWipeCount_EmptyBoard_Increments_OtherwiseDoesNot()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "wipes", ObjectiveType.BoardWipeCount, ObjectiveScope.PerRun, targetValue: 2));

            Assert.IsFalse(objective.ApplyPlacement(Placement(boardEmptyAfterPlacement: false)));
            Assert.AreEqual(0, objective.CurrentValue);

            Assert.IsTrue(objective.ApplyPlacement(Placement(boardEmptyAfterPlacement: true)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void ScoreInRun_MirrorsTheRunScore_AndReportsNoChangeWhenTheScoreStands()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "score", ObjectiveType.ScoreInRun, ObjectiveScope.PerRun, targetValue: 500));

            Assert.IsTrue(objective.ApplyPlacement(Placement(currentRunScore: 120)));
            Assert.AreEqual(120, objective.CurrentValue);

            // A placement that scored nothing leaves the value untouched, so nothing is republished.
            Assert.IsFalse(objective.ApplyPlacement(Placement(currentRunScore: 120)));
            Assert.AreEqual(120, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void ScoreInRun_ClampsToTarget_AndCompletes()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "score", ObjectiveType.ScoreInRun, ObjectiveScope.PerRun, targetValue: 500));

            Assert.IsTrue(objective.ApplyPlacement(Placement(currentRunScore: 9999)));
            Assert.AreEqual(500, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void StreakThreshold_ReachingTheTarget_Completes()
        {
            ObjectiveProgress objective = StreakObjective(targetValue: 7);

            Assert.IsTrue(objective.ApplyPlacement(Placement(currentStreak: 7)));
            Assert.AreEqual(7, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void StreakThreshold_TracksTheHighWaterMark_NotTheLiveValue()
        {
            ObjectiveProgress objective = StreakObjective(targetValue: 7);

            // Streak climbs to 4, then resets to 0 (a non-clearing placement).
            Assert.IsTrue(objective.ApplyPlacement(Placement(currentStreak: 4)));
            Assert.AreEqual(4, objective.CurrentValue);

            // A drop in the live streak must not erase the peak already recorded.
            Assert.IsFalse(objective.ApplyPlacement(Placement(currentStreak: 0)));
            Assert.AreEqual(4, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void StreakThreshold_TwoSeparateStreaks_DoNotSumTowardTheTarget()
        {
            ObjectiveProgress objective = StreakObjective(targetValue: 7);

            // An earlier streak of 4, a reset, then a later streak of 6: best-ever is 6, never 4+6=10.
            objective.ApplyPlacement(Placement(currentStreak: 4));
            objective.ApplyPlacement(Placement(currentStreak: 0));

            Assert.IsTrue(objective.ApplyPlacement(Placement(currentStreak: 6)));
            Assert.AreEqual(6, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void StreakThreshold_ClampsToTarget()
        {
            ObjectiveProgress objective = StreakObjective(targetValue: 7);

            Assert.IsTrue(objective.ApplyPlacement(Placement(currentStreak: 40)));
            Assert.AreEqual(7, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void CurrentValue_NeverExceedsTarget_AndACompletedObjectiveStopsTracking()
        {
            ObjectiveProgress objective = LineClearObjective(requiredLineCount: 1, targetValue: 2);

            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 1)));
            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 1)));
            Assert.AreEqual(2, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);

            // Already complete: further qualifying placements are no-ops, which is what keeps
            // ObjectiveCompletedMessage firing exactly once.
            Assert.IsFalse(objective.ApplyPlacement(Placement(linesCleared: 1)));
            Assert.AreEqual(2, objective.CurrentValue);
        }

        [Test]
        public void ResetForNewRun_ClearsAPerRunObjective()
        {
            ObjectiveProgress objective = LineClearObjective(requiredLineCount: 1, targetValue: 2);
            objective.ApplyPlacement(Placement(linesCleared: 1));
            objective.ApplyPlacement(Placement(linesCleared: 1));
            Assert.IsTrue(objective.IsComplete);

            objective.ResetForNewRun();

            Assert.AreEqual(0, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void ResetForNewRun_LeavesACumulativeObjectiveUntouched()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "family_cumulative", ObjectiveType.PieceFamilyCount, ObjectiveScope.Cumulative,
                targetValue: 5, requiredPieceFamily: PieceFamily.Square));
            objective.ApplyPlacement(Placement(pieceFamily: PieceFamily.Square));
            objective.ApplyPlacement(Placement(pieceFamily: PieceFamily.Square));

            objective.ResetForNewRun();

            Assert.AreEqual(2, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);

            // And it keeps accumulating into the next run.
            Assert.IsTrue(objective.ApplyPlacement(Placement(pieceFamily: PieceFamily.Square)));
            Assert.AreEqual(3, objective.CurrentValue);
        }

        [Test]
        public void RowAndColumnCrossClear_OneRowAndOneColumn_Qualifies()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "cross", ObjectiveType.RowAndColumnCrossClear, ObjectiveScope.PerRun, targetValue: 3));

            Assert.IsTrue(objective.ApplyPlacement(Placement(
                linesCleared: 2, rowsCleared: 1, columnsCleared: 1)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void RowAndColumnCrossClear_TwoRowsAndNoColumns_DoesNotQualify()
        {
            // The entire reason this needs its own fields: same LinesCleared total (2) as the
            // qualifying case above, but it's two rows, not a row-and-column cross.
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "cross", ObjectiveType.RowAndColumnCrossClear, ObjectiveScope.PerRun, targetValue: 3));

            Assert.IsFalse(objective.ApplyPlacement(Placement(
                linesCleared: 2, rowsCleared: 2, columnsCleared: 0)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void RowAndColumnCrossClear_TwoColumnsAndNoRows_DoesNotQualify()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "cross", ObjectiveType.RowAndColumnCrossClear, ObjectiveScope.PerRun, targetValue: 3));

            Assert.IsFalse(objective.ApplyPlacement(Placement(
                linesCleared: 2, rowsCleared: 0, columnsCleared: 2)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void RowAndColumnCrossClear_NothingCleared_DoesNotQualify()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "cross", ObjectiveType.RowAndColumnCrossClear, ObjectiveScope.PerRun, targetValue: 3));

            Assert.IsFalse(objective.ApplyPlacement(Placement(linesCleared: 0)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Construction_RejectsANonPositiveTarget_BecauseItCanNeverComplete(int targetValue)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectiveDefinition(
                "bad_target", ObjectiveType.BoardWipeCount, ObjectiveScope.PerRun, targetValue));
        }

        [Test]
        public void Construction_RejectsScoreInRunPairedWithCumulative_BecauseItWouldGoBackwardsEveryRun()
        {
            Assert.Throws<ArgumentException>(() => new ObjectiveDefinition(
                "bad_scope", ObjectiveType.ScoreInRun, ObjectiveScope.Cumulative, targetValue: 500));
        }

        [Test]
        public void BombInducedLineClear_ApplyPowerUpLineEmptied_Increments()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "bomb_line", ObjectiveType.BombInducedLineClear, ObjectiveScope.PerRun, targetValue: 2));

            Assert.IsTrue(objective.ApplyPowerUpLineEmptied());
            Assert.AreEqual(1, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);

            Assert.IsTrue(objective.ApplyPowerUpLineEmptied());
            Assert.AreEqual(2, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void BombInducedLineClear_AlreadyComplete_StopsTracking()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "bomb_line", ObjectiveType.BombInducedLineClear, ObjectiveScope.PerRun, targetValue: 1));

            Assert.IsTrue(objective.ApplyPowerUpLineEmptied());
            Assert.IsFalse(objective.ApplyPowerUpLineEmptied());
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void BombInducedLineClear_IsNeverAdvancedByAPlacement()
        {
            // The whole reason this type has its own method: a placement — however it clears lines,
            // whatever piece family, whatever the score — must never be mistaken for a Bomb event.
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "bomb_line", ObjectiveType.BombInducedLineClear, ObjectiveScope.PerRun, targetValue: 1));

            Assert.IsFalse(objective.ApplyPlacement(Placement(
                linesCleared: 4, pieceFamily: PieceFamily.Square, currentRunScore: 9999,
                boardEmptyAfterPlacement: true, currentStreak: 99)));
            Assert.AreEqual(0, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void OtherObjectiveTypes_AreNeverAdvancedByApplyPowerUpLineEmptied()
        {
            // The converse guard: a Bomb event must not leak into an unrelated objective type just
            // because it happens to be tracked in the same run.
            ObjectiveProgress lineClear = LineClearObjective(requiredLineCount: 2, targetValue: 3);
            ObjectiveProgress family = FamilyObjective(PieceFamily.Square, targetValue: 3);
            ObjectiveProgress streak = StreakObjective(targetValue: 3);

            Assert.IsFalse(lineClear.ApplyPowerUpLineEmptied());
            Assert.IsFalse(family.ApplyPowerUpLineEmptied());
            Assert.IsFalse(streak.ApplyPowerUpLineEmptied());

            Assert.AreEqual(0, lineClear.CurrentValue);
            Assert.AreEqual(0, family.CurrentValue);
            Assert.AreEqual(0, streak.CurrentValue);
        }
    }
}
