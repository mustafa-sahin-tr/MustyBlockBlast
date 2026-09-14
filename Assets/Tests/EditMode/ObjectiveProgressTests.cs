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
            string pieceId = null,
            int currentRunScore = 0,
            bool boardEmptyAfterPlacement = false,
            int currentStreak = 0,
            int occupiedCellCountBeforeClear = 0,
            bool anyCornerCleared = false,
            bool centerCoreEmptyAfterPlacement = false,
            bool hasIsolatedHolesAfterPlacement = false,
            float elapsedRunSeconds = 0f)
        {
            return new ObjectivePlacementContext(
                linesCleared, rowsCleared, columnsCleared, pieceFamily, pieceId, currentRunScore,
                boardEmptyAfterPlacement, currentStreak, occupiedCellCountBeforeClear,
                anyCornerCleared, centerCoreEmptyAfterPlacement, hasIsolatedHolesAfterPlacement,
                elapsedRunSeconds);
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

        private static ObjectiveProgress ClutchObjective(int occupancyThreshold, int targetValue)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "clutch", ObjectiveType.ClutchRecoveryClear, ObjectiveScope.PerRun, targetValue,
                requiredOccupancyThreshold: occupancyThreshold));
        }

        [Test]
        public void ClutchRecoveryClear_ClearingALineAtOrAboveTheThreshold_Qualifies()
        {
            ObjectiveProgress objective = ClutchObjective(occupancyThreshold: 52, targetValue: 1);

            Assert.IsTrue(objective.ApplyPlacement(Placement(
                linesCleared: 1, occupiedCellCountBeforeClear: 52)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        // 51 is the adjacent-below case that pins the comparison to ">=" against exactly the threshold:
        // paired with the at-threshold test above, an off-by-one in either direction fails one of them.
        [TestCase(20)]
        [TestCase(51)]
        public void ClutchRecoveryClear_ClearingALineBelowTheThreshold_DoesNotQualify(int occupiedCellCount)
        {
            ObjectiveProgress objective = ClutchObjective(occupancyThreshold: 52, targetValue: 1);

            Assert.IsFalse(objective.ApplyPlacement(Placement(
                linesCleared: 1, occupiedCellCountBeforeClear: occupiedCellCount)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void ClutchRecoveryClear_HighOccupancyButNoLineCleared_DoesNotQualify()
        {
            // A packed board that clears nothing isn't a "clutch recovery" — it's just a packed board.
            ObjectiveProgress objective = ClutchObjective(occupancyThreshold: 52, targetValue: 1);

            Assert.IsFalse(objective.ApplyPlacement(Placement(
                linesCleared: 0, occupiedCellCountBeforeClear: 63)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        private static ObjectiveProgress AtLeastLineObjective(int requiredLineCount, int targetValue)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "mega", ObjectiveType.AtLeastLineClear, ObjectiveScope.PerRun, targetValue,
                requiredLineCount: requiredLineCount));
        }

        [Test]
        public void AtLeastLineClear_ClearingExactlyTheRequiredCount_Qualifies()
        {
            ObjectiveProgress objective = AtLeastLineObjective(requiredLineCount: 4, targetValue: 1);

            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 4)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void AtLeastLineClear_ClearingMoreThanTheRequiredCount_AlsoQualifies()
        {
            // The entire reason this is a separate type from the exact-match one: more is fine here.
            ObjectiveProgress objective = AtLeastLineObjective(requiredLineCount: 4, targetValue: 1);

            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 5)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void AtLeastLineClear_ClearingOneFewerThanRequired_DoesNotQualify()
        {
            ObjectiveProgress objective = AtLeastLineObjective(requiredLineCount: 4, targetValue: 1);

            Assert.IsFalse(objective.ApplyPlacement(Placement(linesCleared: 3)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void SimultaneousLineClear_StaysExactMatchOnly_UnaffectedByTheNewType()
        {
            // Regression guard: adding AtLeastLineClear must not have loosened the existing type's
            // exact-match rule (e.g. by an accidental shared branch or a >= creeping into its case).
            ObjectiveProgress objective = LineClearObjective(requiredLineCount: 2, targetValue: 1);

            Assert.IsFalse(objective.ApplyPlacement(Placement(linesCleared: 3)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        private static ObjectiveProgress PieceIdCountObjective(string requiredPieceId, int targetValue)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "piece_id", ObjectiveType.PieceIdCount, ObjectiveScope.Cumulative, targetValue,
                requiredPieceId: requiredPieceId));
        }

        private static ObjectiveProgress PieceIdLineClearObjective(string requiredPieceId, int targetValue)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "piece_id_clear", ObjectiveType.PieceIdLineClear, ObjectiveScope.PerRun, targetValue,
                requiredPieceId: requiredPieceId));
        }

        [Test]
        public void PieceIdCount_MatchingPieceId_Increments()
        {
            ObjectiveProgress objective = PieceIdCountObjective("square_3x3", targetValue: 12);

            Assert.IsTrue(objective.ApplyPlacement(Placement(pieceId: "square_3x3")));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void PieceIdCount_DifferentSizeOfTheSameFamily_DoesNotIncrement()
        {
            // The whole reason this needs the exact id, not PieceFamilyCount: 2x2 and 3x3 are both
            // Square family, but only one of them is the target here.
            ObjectiveProgress objective = PieceIdCountObjective("square_3x3", targetValue: 12);

            Assert.IsFalse(objective.ApplyPlacement(Placement(pieceId: "square_2x2")));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void PieceIdLineClear_MatchingPieceIdThatClearsALine_Qualifies()
        {
            ObjectiveProgress objective = PieceIdLineClearObjective("line_h5", targetValue: 1);

            Assert.IsTrue(objective.ApplyPlacement(Placement(pieceId: "line_h5", linesCleared: 1)));
            Assert.AreEqual(1, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void PieceIdLineClear_MatchingPieceIdThatClearsNothing_DoesNotQualify()
        {
            ObjectiveProgress objective = PieceIdLineClearObjective("line_h5", targetValue: 1);

            Assert.IsFalse(objective.ApplyPlacement(Placement(pieceId: "line_h5", linesCleared: 0)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void PieceIdLineClear_ClearsALineWithTheWrongPiece_DoesNotQualify()
        {
            ObjectiveProgress objective = PieceIdLineClearObjective("line_h5", targetValue: 1);

            Assert.IsFalse(objective.ApplyPlacement(Placement(pieceId: "line_v5", linesCleared: 1)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void FourCornersCleared_ACornerClear_Increments()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "corners", ObjectiveType.FourCornersCleared, ObjectiveScope.PerRun, targetValue: 8));

            Assert.IsTrue(objective.ApplyPlacement(Placement(anyCornerCleared: true)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void FourCornersCleared_AClearThatMissesEveryCorner_DoesNotIncrement()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "corners", ObjectiveType.FourCornersCleared, ObjectiveScope.PerRun, targetValue: 8));

            Assert.IsFalse(objective.ApplyPlacement(Placement(linesCleared: 1, anyCornerCleared: false)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void CenterCoreEvacuated_CenterLeftEmpty_Increments()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "core", ObjectiveType.CenterCoreEvacuated, ObjectiveScope.PerRun, targetValue: 1));

            Assert.IsTrue(objective.ApplyPlacement(Placement(centerCoreEmptyAfterPlacement: true)));
            Assert.AreEqual(1, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void CenterCoreEvacuated_CenterStillOccupied_DoesNotIncrement()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "core", ObjectiveType.CenterCoreEvacuated, ObjectiveScope.PerRun, targetValue: 1));

            Assert.IsFalse(objective.ApplyPlacement(Placement(centerCoreEmptyAfterPlacement: false)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void NoIsolatedHolesStreak_ConsecutiveCleanPlacements_Accumulates()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "no_holes", ObjectiveType.NoIsolatedHolesStreak, ObjectiveScope.PerRun, targetValue: 15));

            Assert.IsTrue(objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false)));
            Assert.AreEqual(1, objective.CurrentValue);

            Assert.IsTrue(objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false)));
            Assert.AreEqual(2, objective.CurrentValue);
        }

        [Test]
        public void NoIsolatedHolesStreak_APlacementThatCreatesAHole_ResetsTheLiveStreak_ButKeepsThePeak()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "no_holes", ObjectiveType.NoIsolatedHolesStreak, ObjectiveScope.PerRun, targetValue: 15));

            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            Assert.AreEqual(3, objective.CurrentValue);

            // A hole breaks the live streak, but the best-ever peak (3) must survive it — same
            // high-water-mark discipline StreakThreshold uses for the combo streak.
            Assert.IsFalse(objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: true)));
            Assert.AreEqual(3, objective.CurrentValue);

            // The live streak resumed from 0, not 3 — a fresh streak of 2 does not exceed the peak.
            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            Assert.AreEqual(3, objective.CurrentValue);
        }

        [Test]
        public void NoIsolatedHolesStreak_EveryPlacementCounts_NotJustClearingOnes()
        {
            // Deliberately different from every other counting objective: this is a hygiene streak,
            // not a clear-event counter, so a placement that clears nothing still extends it as long
            // as it leaves no isolated holes.
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "no_holes", ObjectiveType.NoIsolatedHolesStreak, ObjectiveScope.PerRun, targetValue: 15));

            Assert.IsTrue(objective.ApplyPlacement(
                Placement(linesCleared: 0, hasIsolatedHolesAfterPlacement: false)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void NoIsolatedHolesStreak_ResetForNewRun_ClearsTheLiveStreakForAPerRunObjective()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "no_holes", ObjectiveType.NoIsolatedHolesStreak, ObjectiveScope.PerRun, targetValue: 5));

            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            Assert.AreEqual(3, objective.CurrentValue);

            objective.ResetForNewRun();

            // Not just CurrentValue — the internal live-streak counter must also reset, or the next
            // run's first clean placement would silently jump to 4 instead of starting at 1.
            Assert.IsTrue(objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false)));
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void NoIsolatedHolesStreak_ResetForNewRun_LeavesTheLiveStreakUntouchedForACumulativeObjective()
        {
            // The exact opposite of the PerRun case above: a Cumulative "no isolated holes" streak is
            // a lifetime best, so a run boundary must not touch the live streak counter at all — the
            // next run's first clean placement should extend it (4), not restart it at 1.
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "no_holes_lifetime", ObjectiveType.NoIsolatedHolesStreak, ObjectiveScope.Cumulative, targetValue: 5));

            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false));
            Assert.AreEqual(3, objective.CurrentValue);

            objective.ResetForNewRun();

            Assert.IsTrue(objective.ApplyPlacement(Placement(hasIsolatedHolesAfterPlacement: false)));
            Assert.AreEqual(4, objective.CurrentValue);
        }

        private static ObjectiveProgress RollingLineClearWindowObjective(int targetValue, float windowSeconds)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "burst", ObjectiveType.RollingLineClearWindow, ObjectiveScope.PerRun, targetValue,
                windowSeconds: windowSeconds));
        }

        private static ObjectiveProgress EarlyScoreRushObjective(int targetValue, float windowSeconds)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "rush", ObjectiveType.EarlyScoreRush, ObjectiveScope.PerRun, targetValue,
                windowSeconds: windowSeconds));
        }

        [Test]
        public void RollingLineClearWindow_ClearsWithinTheWindow_Accumulates()
        {
            ObjectiveProgress objective = RollingLineClearWindowObjective(targetValue: 6, windowSeconds: 12f);

            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 2, elapsedRunSeconds: 1f)));
            Assert.AreEqual(2, objective.CurrentValue);

            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 3, elapsedRunSeconds: 5f)));
            Assert.AreEqual(5, objective.CurrentValue);
        }

        [Test]
        public void RollingLineClearWindow_APlacementThatClearsNothing_DoesNotCountButDoesNotBreakTheWindow()
        {
            ObjectiveProgress objective = RollingLineClearWindowObjective(targetValue: 6, windowSeconds: 12f);

            objective.ApplyPlacement(Placement(linesCleared: 2, elapsedRunSeconds: 1f));
            Assert.IsFalse(objective.ApplyPlacement(Placement(linesCleared: 0, elapsedRunSeconds: 2f)));
            Assert.AreEqual(2, objective.CurrentValue);

            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 4, elapsedRunSeconds: 3f)));
            Assert.AreEqual(6, objective.CurrentValue);
        }

        [Test]
        public void RollingLineClearWindow_AnEventThatAgesOutOfTheWindow_IsDroppedFromTheRunningTotal()
        {
            ObjectiveProgress objective = RollingLineClearWindowObjective(targetValue: 10, windowSeconds: 10f);

            objective.ApplyPlacement(Placement(linesCleared: 3, elapsedRunSeconds: 0f));
            Assert.AreEqual(3, objective.CurrentValue);

            // At t=11, the window is [1, 11] (the purge drops entries strictly older than windowStart,
            // so an entry exactly on the boundary survives); the t=0 event is outside it and must be purged, so
            // the running total reflects only the new event, not 3+2.
            objective.ApplyPlacement(Placement(linesCleared: 2, elapsedRunSeconds: 11f));
            Assert.AreEqual(2, objective.CurrentValue);
        }

        [Test]
        public void RollingLineClearWindow_OnceComplete_APlacementThatAgesOutOfTheWindow_DoesNotUncomplete()
        {
            ObjectiveProgress objective = RollingLineClearWindowObjective(targetValue: 3, windowSeconds: 10f);

            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 3, elapsedRunSeconds: 0f)));
            Assert.IsTrue(objective.IsComplete);

            // IsComplete latches in ApplyPlacement's shared early-return, so a later placement that
            // would otherwise cause the running total to age back down never even runs the switch.
            Assert.IsFalse(objective.ApplyPlacement(Placement(linesCleared: 0, elapsedRunSeconds: 20f)));
            Assert.IsTrue(objective.IsComplete);
            Assert.AreEqual(3, objective.CurrentValue);
        }

        [Test]
        public void RollingLineClearWindow_ResetForNewRun_ClearsTheEventQueue()
        {
            ObjectiveProgress objective = RollingLineClearWindowObjective(targetValue: 10, windowSeconds: 10f);

            objective.ApplyPlacement(Placement(linesCleared: 3, elapsedRunSeconds: 0f));
            Assert.AreEqual(3, objective.CurrentValue);

            objective.ResetForNewRun();

            // Not just CurrentValue — the internal event queue must also clear, or a new run's first
            // placement at elapsedRunSeconds=0 would spuriously sum with the stale t=0 entry.
            Assert.IsTrue(objective.ApplyPlacement(Placement(linesCleared: 2, elapsedRunSeconds: 0f)));
            Assert.AreEqual(2, objective.CurrentValue);
        }

        [Test]
        public void EarlyScoreRush_WithinTheDeadline_MirrorsTheRunScore()
        {
            ObjectiveProgress objective = EarlyScoreRushObjective(targetValue: 2500, windowSeconds: 60f);

            Assert.IsTrue(objective.ApplyPlacement(
                Placement(currentRunScore: 1000, elapsedRunSeconds: 30f)));
            Assert.AreEqual(1000, objective.CurrentValue);
        }

        [Test]
        public void EarlyScoreRush_PastTheDeadline_FreezesInsteadOfCompletingLate()
        {
            ObjectiveProgress objective = EarlyScoreRushObjective(targetValue: 2500, windowSeconds: 60f);

            objective.ApplyPlacement(Placement(currentRunScore: 1000, elapsedRunSeconds: 30f));
            Assert.AreEqual(1000, objective.CurrentValue);

            // A later placement past the deadline must not update CurrentValue even though the run
            // score kept climbing — it freezes at the last in-window reading.
            Assert.IsFalse(objective.ApplyPlacement(
                Placement(currentRunScore: 3000, elapsedRunSeconds: 90f)));
            Assert.AreEqual(1000, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void EarlyScoreRush_ResetForNewRun_ClearsProgress()
        {
            ObjectiveProgress objective = EarlyScoreRushObjective(targetValue: 2500, windowSeconds: 60f);

            objective.ApplyPlacement(Placement(currentRunScore: 1000, elapsedRunSeconds: 30f));
            objective.ResetForNewRun();

            Assert.AreEqual(0, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void RollingLineClearWindowDefinition_CumulativeScope_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ObjectiveDefinition(
                "burst", ObjectiveType.RollingLineClearWindow, ObjectiveScope.Cumulative, targetValue: 6,
                windowSeconds: 12f));
        }

        [Test]
        public void EarlyScoreRushDefinition_CumulativeScope_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ObjectiveDefinition(
                "rush", ObjectiveType.EarlyScoreRush, ObjectiveScope.Cumulative, targetValue: 2500,
                windowSeconds: 60f));
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

        [Test]
        public void RerollSave_ApplyPowerUpRerollSave_Increments()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "reroll_save", ObjectiveType.RerollSave, ObjectiveScope.PerRun, targetValue: 2));

            Assert.IsTrue(objective.ApplyPowerUpRerollSave());
            Assert.AreEqual(1, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);

            Assert.IsTrue(objective.ApplyPowerUpRerollSave());
            Assert.AreEqual(2, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void RerollSave_AlreadyComplete_StopsTracking()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "reroll_save", ObjectiveType.RerollSave, ObjectiveScope.PerRun, targetValue: 1));

            Assert.IsTrue(objective.ApplyPowerUpRerollSave());
            Assert.IsFalse(objective.ApplyPowerUpRerollSave());
            Assert.AreEqual(1, objective.CurrentValue);
        }

        [Test]
        public void RerollSave_IsNeverAdvancedByAPlacement()
        {
            ObjectiveProgress objective = new ObjectiveProgress(new ObjectiveDefinition(
                "reroll_save", ObjectiveType.RerollSave, ObjectiveScope.PerRun, targetValue: 1));

            Assert.IsFalse(objective.ApplyPlacement(Placement(
                linesCleared: 4, pieceFamily: PieceFamily.Square, currentRunScore: 9999,
                boardEmptyAfterPlacement: true, currentStreak: 99)));
            Assert.AreEqual(0, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void OtherObjectiveTypes_AreNeverAdvancedByApplyPowerUpRerollSave()
        {
            ObjectiveProgress lineClear = LineClearObjective(requiredLineCount: 2, targetValue: 3);
            ObjectiveProgress family = FamilyObjective(PieceFamily.Square, targetValue: 3);
            ObjectiveProgress streak = StreakObjective(targetValue: 3);

            Assert.IsFalse(lineClear.ApplyPowerUpRerollSave());
            Assert.IsFalse(family.ApplyPowerUpRerollSave());
            Assert.IsFalse(streak.ApplyPowerUpRerollSave());

            Assert.AreEqual(0, lineClear.CurrentValue);
            Assert.AreEqual(0, family.CurrentValue);
            Assert.AreEqual(0, streak.CurrentValue);
        }
    }
}
