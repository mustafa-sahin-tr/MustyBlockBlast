using System.Collections.Generic;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Guards the rule abstraction: each built-in rule stays a thin delegate to <see cref="ScoreRules"/>,
    /// and adding a brand new rule changes the total without any edit to ScoreSystem.
    /// </summary>
    public class ScoreRuleCompositionTests
    {
        private const int FAKE_RULE_BONUS = 7;

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(9)]
        public void PlacementScoreRule_MatchesScoreRulesFormula(int cellCount)
        {
            PlacementScoreRule rule = new PlacementScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(cellCount, 0, 0, 0, 0, 0);

            Assert.AreEqual(ScoreRules.PlacementScore(cellCount), rule.ComputeBonus(context));
        }

        [TestCase(0, 5)]
        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(4, 10)]
        public void LineClearScoreRule_MatchesScoreRulesFormula(int linesCleared, int streak)
        {
            LineClearScoreRule rule = new LineClearScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(4, linesCleared, streak, 0, 0, 0);

            Assert.AreEqual(ScoreRules.ClearScore(linesCleared, streak), rule.ComputeBonus(context));
        }

        [Test]
        public void LineClearScoreRule_NoLinesCleared_IsZero()
        {
            LineClearScoreRule rule = new LineClearScoreRule();

            Assert.AreEqual(0, rule.ComputeBonus(new ScorePlacementContext(4, 0, 3, 0, 0, 0)));
        }

        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(2, 2)]
        [TestCase(4, 3)]
        public void MonochromeScoreRule_MatchesTheManualFormula(int linesCleared, int monochromeLineCount)
        {
            MonochromeScoreRule rule = new MonochromeScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(4, linesCleared, 0, monochromeLineCount, 0, 0);

            // 10 points per line x lines x the +0.5x-per-monochrome-line multiplier.
            int expected = (int)System.Math.Round(
                10 * linesCleared * ScoreRules.MonochromeMultiplierBonus(monochromeLineCount),
                System.MidpointRounding.AwayFromZero);

            Assert.AreEqual(expected, rule.ComputeBonus(context));
        }

        [Test]
        public void MonochromeScoreRule_MixedColourLines_IsZero()
        {
            MonochromeScoreRule rule = new MonochromeScoreRule();

            Assert.AreEqual(0, rule.ComputeBonus(new ScorePlacementContext(4, 2, 3, 0, 0, 0)));
        }

        [Test]
        public void MonochromeScoreRule_NoLinesCleared_IsZero()
        {
            MonochromeScoreRule rule = new MonochromeScoreRule();

            Assert.AreEqual(0, rule.ComputeBonus(new ScorePlacementContext(4, 0, 3, 2, 0, 0)));
        }

        [Test]
        public void MonochromeScoreRule_StacksOnTopOfTheLineClearRule()
        {
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 2, streakBeforePlacement: 1, monochromeLineCount: 2,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 0);
            List<IScoreRule> rules = new List<IScoreRule>
            {
                new PlacementScoreRule(),
                new LineClearScoreRule(),
                new MonochromeScoreRule()
            };

            int expected = ScoreRules.PlacementScore(4)
                + ScoreRules.ClearScore(2, 1)
                + new MonochromeScoreRule().ComputeBonus(context);

            Assert.AreEqual(expected, SumBonuses(rules, context));
            Assert.Greater(
                SumBonuses(rules, context),
                ScoreRules.PlacementScore(4) + ScoreRules.ClearScore(2, 1));
        }

        [Test]
        public void Rules_AreSummedIntoTheGainedTotal()
        {
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 2, streakBeforePlacement: 1, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 0);
            List<IScoreRule> rules = new List<IScoreRule>
            {
                new PlacementScoreRule(),
                new LineClearScoreRule()
            };

            int expected = ScoreRules.PlacementScore(4) + ScoreRules.ClearScore(2, 1);

            Assert.AreEqual(expected, SumBonuses(rules, context));
        }

        /// <summary>
        /// Acceptance criterion: a brand new rule only has to be added to the rule collection (one DI
        /// registration in production) to affect the total. No scoring-system control flow changes.
        /// </summary>
        [Test]
        public void AddingANewRule_ChangesTheTotal_WithoutTouchingScoreSystem()
        {
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 5, linesCleared: 0, streakBeforePlacement: 0, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 0);
            List<IScoreRule> rules = new List<IScoreRule> { new PlacementScoreRule() };

            int before = SumBonuses(rules, context);
            rules.Add(new FixedBonusRule(FAKE_RULE_BONUS));
            int after = SumBonuses(rules, context);

            Assert.AreEqual(ScoreRules.PlacementScore(5), before);
            Assert.AreEqual(ScoreRules.PlacementScore(5) + FAKE_RULE_BONUS, after);
        }

        private static int SumBonuses(IEnumerable<IScoreRule> rules, ScorePlacementContext context)
        {
            int total = 0;
            foreach (IScoreRule rule in rules)
            {
                total += rule.ComputeBonus(context);
            }

            return total;
        }

        /// <summary>Test-only stand-in for a future bonus (monochrome, milestone, ...).</summary>
        private sealed class FixedBonusRule : IScoreRule
        {
            private readonly int _bonus;

            public FixedBonusRule(int bonus)
            {
                _bonus = bonus;
            }

            public int ComputeBonus(ScorePlacementContext context) => _bonus;
        }
    }
}
