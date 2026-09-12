using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the consecutive-multi-clear bonus: only a placement that itself clears 2+ lines earns it, the
    /// escalation reuses <see cref="ScoreRules.StreakBonus"/>, and it stacks additively with the other rules.
    /// </summary>
    public class MultiClearStreakScoreRuleTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        public void NoLinesCleared_IsZero_RegardlessOfStreak(int multiClearStreak)
        {
            MultiClearStreakScoreRule rule = new MultiClearStreakScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 0, streakBeforePlacement: 3, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: multiClearStreak);

            Assert.AreEqual(0, rule.ComputeBonus(context));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        public void SingleLineClear_IsZero_RegardlessOfStreak(int multiClearStreak)
        {
            MultiClearStreakScoreRule rule = new MultiClearStreakScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 1, streakBeforePlacement: 3, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: multiClearStreak);

            Assert.AreEqual(0, rule.ComputeBonus(context));
        }

        [Test]
        public void FirstMultiClear_EarnsNothing_BecauseTheStreakBeforeItIsZero()
        {
            MultiClearStreakScoreRule rule = new MultiClearStreakScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 2, streakBeforePlacement: 1, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0);

            Assert.AreEqual(0, ScoreRules.StreakBonus(0));
            Assert.AreEqual(0, rule.ComputeBonus(context));
        }

        [TestCase(2, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 1)]
        [TestCase(4, 4)]
        public void MultiClear_MatchesTheManualStreakBonusFormula(int linesCleared, int multiClearStreak)
        {
            MultiClearStreakScoreRule rule = new MultiClearStreakScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: linesCleared, streakBeforePlacement: 0, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: multiClearStreak);

            // 10 points per line x lines x the +0.5x-per-consecutive-multi-clear multiplier.
            int expected = (int)System.Math.Round(
                10 * linesCleared * ScoreRules.StreakBonus(multiClearStreak),
                System.MidpointRounding.AwayFromZero);

            Assert.AreEqual(expected, rule.ComputeBonus(context));
        }

        [Test]
        public void LongChain_IsCappedAtTheStreakBonusCeiling()
        {
            MultiClearStreakScoreRule rule = new MultiClearStreakScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 3, streakBeforePlacement: 0, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 10);

            // StreakBonus caps at +3.0x, so 10 x 3 lines x 3.0 = 90 no matter how long the chain runs.
            Assert.AreEqual(3.0, ScoreRules.StreakBonus(10));
            Assert.AreEqual(90, rule.ComputeBonus(context));
        }

        [Test]
        public void StacksOnTopOfTheOtherRules()
        {
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 2, streakBeforePlacement: 1, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 2);
            List<IScoreRule> rules = new List<IScoreRule>
            {
                new PlacementScoreRule(),
                new LineClearScoreRule(),
                new MonochromeScoreRule(),
                new MultiClearStreakScoreRule()
            };

            int expected = ScoreRules.PlacementScore(4)
                + ScoreRules.ClearScore(2, 1)
                + new MultiClearStreakScoreRule().ComputeBonus(context);

            int total = 0;
            foreach (IScoreRule rule in rules)
            {
                total += rule.ComputeBonus(context);
            }

            Assert.AreEqual(expected, total);
            Assert.Greater(total, ScoreRules.PlacementScore(4) + ScoreRules.ClearScore(2, 1));
        }
    }
}
