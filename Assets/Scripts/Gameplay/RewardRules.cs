using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Settings;

namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The pure half of the rule-based rewards (issue #464): which rules a streak value pays, and how
    /// close the player is to the next payout. Shared by <see cref="Systems.RewardRuleSystem"/>, which
    /// pays, and the Level Path hint, which previews — so the preview can never disagree with the payout.
    /// </summary>
    public static class RewardRules
    {
        /// <summary>
        /// Whether <paramref name="rule"/> pays when the <paramref name="condition"/> count has just
        /// moved to <paramref name="count"/>. Repeatable: every positive multiple of the threshold pays.
        /// </summary>
        public static bool PaysAt(RewardRuleConfig rule, RewardRuleCondition condition, int count)
            => rule != null
                && rule.Condition == condition
                && rule.IsValid(out _)
                && count > 0
                && count % rule.Threshold == 0;

        /// <summary>
        /// Appends to <paramref name="payout"/> every kind the first-try streak rules pay when a first
        /// clear of <paramref name="completedLevelNumber"/> moves the streak to
        /// <paramref name="streakAfterClear"/>, in catalog order. Empty when no rule lands. This is the
        /// payout itself — <see cref="Systems.RewardRuleSystem"/> grants exactly this list — and the
        /// level-start card's preview, so the two can never disagree.
        /// </summary>
        public static void AppendFirstTryPayout(
            IReadOnlyList<RewardRuleConfig> rules, int streakAfterClear, int completedLevelNumber, List<PowerUpKind> payout)
        {
            int frontierAfterClear = completedLevelNumber + 1;
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                RewardRuleConfig rule = rules[ruleIndex];
                if (!PaysAt(rule, RewardRuleCondition.ConsecutiveFirstTryClears, streakAfterClear))
                {
                    continue;
                }

                for (int rewardIndex = 0; rewardIndex < rule.RewardCount; rewardIndex++)
                {
                    payout.Add(KindFor(rule, completedLevelNumber, frontierAfterClear, payout.Count));
                }
            }
        }

        /// <summary>A rule's authored kind when it is fixed and already unlocked; otherwise a drawn one.</summary>
        private static PowerUpKind KindFor(RewardRuleConfig rule, int completedLevelNumber, int frontierAfterClear, int drawIndex)
        {
            if (rule.KindSelection == RewardKindSelection.Fixed
                && PowerUpUnlockLevels.IsUnlockedAt(rule.RewardKind, frontierAfterClear))
            {
                return rule.RewardKind;
            }

            return LevelCompletionRewards.DrawBonusKind(completedLevelNumber, drawIndex);
        }

        /// <summary>
        /// The valid rule for <paramref name="condition"/> the player is closest to being paid by at
        /// <paramref name="count"/>, and how far along its current cycle they are. Ties go to the rule
        /// listed first. False when no valid rule watches the condition.
        /// </summary>
        /// <param name="progress">Steps taken towards the next payout, 0 to threshold − 1.</param>
        public static bool TryGetNextPayout(
            IReadOnlyList<RewardRuleConfig> rules, RewardRuleCondition condition, int count,
            out RewardRuleConfig nextRule, out int progress)
        {
            nextRule = null;
            progress = 0;
            int bestRemaining = int.MaxValue;
            int safeCount = count < 0 ? 0 : count;

            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                RewardRuleConfig rule = rules[ruleIndex];
                if (rule == null || rule.Condition != condition || !rule.IsValid(out _))
                {
                    continue;
                }

                int ruleProgress = safeCount % rule.Threshold;
                int remaining = rule.Threshold - ruleProgress;
                if (remaining < bestRemaining)
                {
                    bestRemaining = remaining;
                    nextRule = rule;
                    progress = ruleProgress;
                }
            }

            return nextRule != null;
        }
    }
}
