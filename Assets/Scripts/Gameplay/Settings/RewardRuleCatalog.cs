using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The game's rule-based power-up rewards (issue #464): the list of authored
    /// <see cref="RewardRuleConfig"/> rows <see cref="Systems.RewardRuleSystem"/> evaluates. Content, not
    /// code — a new rule for an existing condition is an asset edit.
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Reward Rule Catalog", fileName = "RewardRuleCatalog")]
    public sealed class RewardRuleCatalog : ScriptableObject
    {
        private static readonly RewardRuleConfig[] EmptyRules = new RewardRuleConfig[0];

        [Tooltip("Authored rules. Every valid row is evaluated independently; list order is only the order "
            + "their payouts are granted in.")]
        [SerializeField] private List<RewardRuleConfig> _rules = new List<RewardRuleConfig>();

        /// <summary>Every authored rule, in list order. Never null; may contain invalid rows, which
        /// readers skip via <see cref="RewardRuleConfig.IsValid"/>.</summary>
        public IReadOnlyList<RewardRuleConfig> Rules => _rules ?? (IReadOnlyList<RewardRuleConfig>)EmptyRules;

#if UNITY_EDITOR
        /// <summary>Editor-time feedback for the developer authoring rules: logs every row the system
        /// would skip at runtime.</summary>
        private void OnValidate()
        {
            if (_rules == null)
            {
                return;
            }

            for (int ruleIndex = 0; ruleIndex < _rules.Count; ruleIndex++)
            {
                RewardRuleConfig rule = _rules[ruleIndex];
                if (rule != null && !rule.IsValid(out string error))
                {
                    Debug.LogWarning($"{nameof(RewardRuleCatalog)} entry {ruleIndex} is invalid and will be skipped: {error}", this);
                }
            }
        }
#endif
    }
}
