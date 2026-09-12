using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the cumulative multi-clear milestone bonus: only a placement that itself clears 2+ lines counts
    /// as an occurrence, the milestone fires on the placement that reaches it, and placements that land
    /// between milestones earn nothing.
    /// </summary>
    public class CumulativeMultiClearMilestoneScoreRuleTests
    {
        [TestCase(0)]
        [TestCase(4)]
        [TestCase(9)]
        public void NoLinesCleared_IsZero_RegardlessOfTheCumulativeCount(int cumulativeCount)
        {
            CumulativeMultiClearMilestoneScoreRule rule = new CumulativeMultiClearMilestoneScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 0, streakBeforePlacement: 3, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: cumulativeCount);

            Assert.AreEqual(0, rule.ComputeBonus(context));
        }

        /// <summary>A single-line clear is never an occurrence, so it can never complete a milestone.</summary>
        [TestCase(0)]
        [TestCase(4)]
        [TestCase(9)]
        public void SingleLineClear_IsZero_RegardlessOfTheCumulativeCount(int cumulativeCount)
        {
            CumulativeMultiClearMilestoneScoreRule rule = new CumulativeMultiClearMilestoneScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 1, streakBeforePlacement: 3, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: cumulativeCount);

            Assert.AreEqual(0, rule.ComputeBonus(context));
        }

        [Test]
        public void FifthOccurrence_EarnsTheFirstMilestone()
        {
            CumulativeMultiClearMilestoneScoreRule rule = new CumulativeMultiClearMilestoneScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 2, streakBeforePlacement: 1, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 4);

            Assert.AreEqual(50, rule.ComputeBonus(context));
        }

        [Test]
        public void TenthOccurrence_EarnsTheLargerSecondMilestone()
        {
            CumulativeMultiClearMilestoneScoreRule rule = new CumulativeMultiClearMilestoneScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 2, streakBeforePlacement: 1, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 9);

            Assert.AreEqual(100, rule.ComputeBonus(context));
        }

        [Test]
        public void OccurrenceBetweenMilestones_EarnsNothing()
        {
            CumulativeMultiClearMilestoneScoreRule rule = new CumulativeMultiClearMilestoneScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 3, streakBeforePlacement: 1, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 3);

            // 4th occurrence — not a multiple of 5.
            Assert.AreEqual(0, rule.ComputeBonus(context));
        }

        /// <summary>One placement is one occurrence no matter how many lines it cleared, so a 4-line clear
        /// reaching the 5th occurrence pays exactly the same milestone as a 2-line clear would.</summary>
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void MilestoneReward_DoesNotScaleWithTheLineCountOfThePlacement(int linesCleared)
        {
            CumulativeMultiClearMilestoneScoreRule rule = new CumulativeMultiClearMilestoneScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: linesCleared, streakBeforePlacement: 1, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 4);

            Assert.AreEqual(50, rule.ComputeBonus(context));
        }

        [Test]
        public void StacksOnTopOfTheOtherRules()
        {
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 2, streakBeforePlacement: 1, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 2, cumulativeMultiClearCountBeforePlacement: 4);
            List<IScoreRule> rules = new List<IScoreRule>
            {
                new PlacementScoreRule(),
                new LineClearScoreRule(),
                new MonochromeScoreRule(),
                new MultiClearStreakScoreRule(),
                new CumulativeMultiClearMilestoneScoreRule()
            };

            int expected = ScoreRules.PlacementScore(4)
                + ScoreRules.ClearScore(2, 1)
                + new MultiClearStreakScoreRule().ComputeBonus(context)
                + 50;

            int total = 0;
            foreach (IScoreRule rule in rules)
            {
                total += rule.ComputeBonus(context);
            }

            Assert.AreEqual(expected, total);
        }
    }
}
