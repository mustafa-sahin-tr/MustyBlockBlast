using System.Collections.Generic;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Settings;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// The pure half of the rule-based rewards (issue #464): when a rule pays, what the Level Path
    /// hint previews, and which kind a drawn bonus is.
    /// </summary>
    public class RewardRulesTests
    {
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        [TestCase(6, true)]
        [TestCase(9, true)]
        public void PaysAt_EveryPositiveMultipleOfTheThreshold(int streak, bool expected)
        {
            RewardRuleConfig rule = RulesOf(Rule(3))[0];

            Assert.AreEqual(expected, RewardRules.PaysAt(rule, RewardRuleCondition.ConsecutiveFirstTryClears, streak));
        }

        [Test]
        public void PaysAt_AnInvalidRule_NeverPays()
        {
            RewardRuleConfig rule = RulesOf(Rule(0))[0];

            Assert.IsFalse(RewardRules.PaysAt(rule, RewardRuleCondition.ConsecutiveFirstTryClears, 3));
        }

        [TestCase(0, 0)]
        [TestCase(2, 2)]
        [TestCase(3, 0)]
        [TestCase(7, 1)]
        public void TryGetNextPayout_ReportsProgressIntoTheCurrentCycle(int streak, int expectedProgress)
        {
            IReadOnlyList<RewardRuleConfig> rules = RulesOf(Rule(3));

            Assert.IsTrue(RewardRules.TryGetNextPayout(
                rules, RewardRuleCondition.ConsecutiveFirstTryClears, streak, out RewardRuleConfig next, out int progress));
            Assert.AreSame(rules[0], next);
            Assert.AreEqual(expectedProgress, progress);
        }

        /// <summary>With two rules, the hint shows whichever pays soonest.</summary>
        [Test]
        public void TryGetNextPayout_PicksTheRuleClosestToPaying()
        {
            IReadOnlyList<RewardRuleConfig> rules = RulesOf(Rule(3), Rule(5));

            RewardRules.TryGetNextPayout(rules, RewardRuleCondition.ConsecutiveFirstTryClears, 4, out RewardRuleConfig next, out int progress);

            Assert.AreSame(rules[1], next, "4 → 5 is one step away; 4 → 6 is two.");
            Assert.AreEqual(4, progress);
        }

        [Test]
        public void TryGetNextPayout_NoValidRule_ReturnsFalse()
        {
            Assert.IsFalse(RewardRules.TryGetNextPayout(
                RulesOf(Rule(0)), RewardRuleCondition.ConsecutiveFirstTryClears, 2, out _, out _));
        }

        /// <summary>The payout the card previews and the system grants: the rule's count of drawn kinds on
        /// a multiple, nothing off one.</summary>
        [TestCase(3, 1)]
        [TestCase(6, 1)]
        [TestCase(4, 0)]
        public void AppendFirstTryPayout_PaysOnlyWhenTheStreakLandsOnAMultiple(int streakAfterClear, int expectedCount)
        {
            var payout = new List<PowerUpKind>();

            RewardRules.AppendFirstTryPayout(RulesOf(Rule(3)), streakAfterClear, 12, payout);

            Assert.AreEqual(expectedCount, payout.Count);
            if (expectedCount > 0)
            {
                Assert.AreEqual(LevelCompletionRewards.DrawBonusKind(12, 0), payout[0]);
            }
        }

        /// <summary>A fixed kind that is still locked is drawn instead, so a rule never pays into a slot
        /// the strip does not show yet.</summary>
        [Test]
        public void AppendFirstTryPayout_ALockedFixedKind_IsDrawnInstead()
        {
            string lockedFixed = "{\"_condition\":0,\"_threshold\":3,\"_kindSelection\":1,\"_rewardKind\":"
                + (int)PowerUpKind.PaintCross + ",\"_rewardCount\":1}";
            var payout = new List<PowerUpKind>();

            RewardRules.AppendFirstTryPayout(RulesOf(lockedFixed), 3, 2, payout);

            Assert.AreEqual(1, payout.Count);
            Assert.AreEqual(LevelCompletionRewards.DrawBonusKind(2, 0), payout[0]);
        }

        /// <summary>The bonus prefers a kind the level's own reward did not pay, is unlocked at the
        /// frontier the clear reaches, and is deterministic.</summary>
        [Test]
        public void DrawBonusKind_IsUnlocked_DiffersFromTheLevelsOwnReward_AndIsStable()
        {
            for (int levelNumber = 1; levelNumber <= 60; levelNumber++)
            {
                PowerUpKind bonus = LevelCompletionRewards.DrawBonusKind(levelNumber, 0);

                Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(bonus, levelNumber + 1), $"Level {levelNumber}");
                CollectionAssert.DoesNotContain(LevelCompletionRewards.For(levelNumber), bonus, $"Level {levelNumber}");
                Assert.AreEqual(bonus, LevelCompletionRewards.DrawBonusKind(levelNumber, 0));
            }
        }

        /// <summary>When the level already paid every unlocked kind, the bonus repeats one rather than
        /// failing.</summary>
        [Test]
        public void DrawBonusKind_WhenEveryKindWasPaid_FallsBackToTheWholePool()
        {
            var roster = new[] { PowerUpKind.Bomb, PowerUpKind.RowClear, PowerUpKind.ColumnClear };

            PowerUpKind bonus = LevelCompletionRewards.DrawBonusKind(5, 0, roster, _ => 0);

            CollectionAssert.Contains(roster, bonus);
        }

        private static string Rule(int threshold)
            => $"{{\"_condition\":0,\"_threshold\":{threshold},\"_kindSelection\":0,\"_rewardKind\":0,\"_rewardCount\":1}}";

        private static IReadOnlyList<RewardRuleConfig> RulesOf(params string[] rules)
        {
            var catalog = ScriptableObject.CreateInstance<RewardRuleCatalog>();
            JsonUtility.FromJsonOverwrite($"{{\"_rules\":[{string.Join(",", rules)}]}}", catalog);
            return catalog.Rules;
        }
    }
}
