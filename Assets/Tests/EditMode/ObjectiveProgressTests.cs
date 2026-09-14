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
            PieceFamily pieceFamily = PieceFamily.Single,
            int currentRunScore = 0,
            bool boardEmptyAfterPlacement = false)
        {
            return new ObjectivePlacementContext(
                linesCleared, pieceFamily, currentRunScore, boardEmptyAfterPlacement);
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
    }
}
