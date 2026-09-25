using System;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One authored rule-based power-up reward (issue #464): what the player has to do, how many times
    /// in a row, and what it pays. Inspector-editable rather than hardcoded so rules are retuned or
    /// added as an asset edit — see <see cref="RewardRuleCatalog"/>, which owns the list of these.
    /// <para>
    /// Every rule is <b>repeatable</b>: it pays each time its progress reaches a multiple of
    /// <see cref="Threshold"/> (3, 6, 9, … for a threshold of 3). Because the streak moves one step at
    /// a time and a rule is checked only on the step that moved it, a rule can never pay twice for the
    /// same streak value.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class RewardRuleConfig
    {
        [Tooltip("What this rule counts.")]
        [SerializeField] private RewardRuleCondition _condition = RewardRuleCondition.ConsecutiveFirstTryClears;

        [Tooltip("How far the count must get to pay. Pays again at every multiple (3, 6, 9, …). Must be "
            + "greater than zero.")]
        [SerializeField] private int _threshold = 3;

        [Tooltip("Draw Unlocked: a kind drawn from those unlocked, preferring one the level did not already "
            + "pay. Fixed: always Reward Kind (drawn instead while that kind is still locked).")]
        [SerializeField] private RewardKindSelection _kindSelection = RewardKindSelection.DrawUnlocked;

        [Tooltip("The kind paid when Kind Selection is Fixed. Ignored for Draw Unlocked.")]
        [SerializeField] private PowerUpKind _rewardKind = PowerUpKind.Bomb;

        [Tooltip("How many power-ups one payout grants. Must be greater than zero.")]
        [SerializeField] private int _rewardCount = 1;

        public RewardRuleCondition Condition => _condition;

        public int Threshold => _threshold;

        public RewardKindSelection KindSelection => _kindSelection;

        public PowerUpKind RewardKind => _rewardKind;

        public int RewardCount => _rewardCount;

        /// <summary>
        /// Whether this row can pay anything. A zero threshold would pay on every step (or divide by
        /// zero), and a zero count would announce a payout of nothing, so both are refused rather than
        /// guessed at.
        /// </summary>
        public bool IsValid(out string error)
        {
            if (_threshold <= 0)
            {
                error = $"Threshold must be greater than zero (was {_threshold}).";
                return false;
            }

            if (_rewardCount <= 0)
            {
                error = $"Reward Count must be greater than zero (was {_rewardCount}).";
                return false;
            }

            error = null;
            return true;
        }
    }
}
