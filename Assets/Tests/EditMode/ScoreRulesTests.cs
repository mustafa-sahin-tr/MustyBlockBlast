using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    public class ScoreRulesTests
    {
        [TestCase(1, 1)]
        [TestCase(4, 4)]
        [TestCase(9, 9)]
        public void PlacementScore_IsOnePointPerCell(int cellCount, int expected)
        {
            Assert.AreEqual(expected, ScoreRules.PlacementScore(cellCount));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 3)]
        [TestCase(3, 6)]
        [TestCase(4, 10)]
        [TestCase(5, 10)]
        public void ComboMultiplier_MatchesDesignTable(int lines, double expected)
        {
            Assert.AreEqual(expected, ScoreRules.ComboMultiplier(lines));
        }

        // Still the multi-clear-streak rule's own formula (ScoreRules.StreakBonus) — see
        // MultiClearStreakScoreRuleTests. The ordinary any-clear streak below no longer shares it.
        [TestCase(0, 0)]
        [TestCase(1, 0.5)]
        [TestCase(2, 1.0)]
        [TestCase(6, 3.0)]
        [TestCase(10, 3.0)]
        public void StreakBonus_IsHalfPerStepCappedAtThree(int streak, double expected)
        {
            Assert.AreEqual(expected, ScoreRules.StreakBonus(streak), 0.0001);
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(6, 5)]
        [TestCase(100, 99)]
        public void ComboStreakBonus_IsOnePerStepUncapped(int streak, double expected)
        {
            Assert.AreEqual(expected, ScoreRules.ComboStreakBonus(streak), 0.0001);
        }

        [Test]
        public void ClearScore_NoLinesCleared_IsZero()
        {
            Assert.AreEqual(0, ScoreRules.ClearScore(lines: 0, streak: 5));
        }

        [Test]
        public void ClearScore_SingleLineNoStreak_MatchesFormula()
        {
            // 10 * 1 * (1 + 0) = 10
            Assert.AreEqual(10, ScoreRules.ClearScore(lines: 1, streak: 0));
        }

        [Test]
        public void ClearScore_DoubleLineWithStreak_MatchesFormula()
        {
            // 10 * 2 * (3 + comboStreakBonus(1)=0) = 60 — a streak of 1 is the first successful clear,
            // not yet a second consecutive one, so it carries no bonus of its own.
            Assert.AreEqual(60, ScoreRules.ClearScore(lines: 2, streak: 1));
        }

        [Test]
        public void ClearScore_FourPlusLinesAtHighStreak_MatchesFormula()
        {
            // 10 * 4 * (10 + comboStreakBonus(10)=9) = 760 — uncapped, unlike the old streak bonus.
            Assert.AreEqual(760, ScoreRules.ClearScore(lines: 4, streak: 10));
        }

        [Test]
        public void ClearScore_MoreLinesAlwaysScoresMoreThanFewerAtSameStreak()
        {
            int oneLine = ScoreRules.ClearScore(lines: 1, streak: 2);
            int twoLines = ScoreRules.ClearScore(lines: 2, streak: 2);
            int threeLines = ScoreRules.ClearScore(lines: 3, streak: 2);

            Assert.Less(oneLine, twoLines);
            Assert.Less(twoLines, threeLines);
        }
    }
}
