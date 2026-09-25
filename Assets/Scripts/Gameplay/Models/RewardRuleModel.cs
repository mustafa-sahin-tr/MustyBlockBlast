using System.Collections.Generic;
using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// State of the rule-based power-up rewards (issue #464): the first-try streak the rules are
    /// measured against, and what the most recent payout granted. Owned and written by
    /// <see cref="Systems.RewardRuleSystem"/>; the Level Path hint and the result card only read it.
    /// </summary>
    public sealed class RewardRuleModel
    {
        private readonly List<PowerUpKind> _lastPayoutKinds = new List<PowerUpKind>(4);

        /// <summary>
        /// How many Path levels in a row the player has cleared on the first attempt, as of the last
        /// first clear or failure. Persisted by the system, so it survives a restart.
        /// </summary>
        public ReactiveProperty<int> FirstTryStreak { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// The level whose first clear paid a rule bonus during the current run, or 0 when none has.
        /// Reset at every run start, so a result card only ever sees its own run's payout.
        /// </summary>
        public int LastPayoutLevelNumber { get; private set; }

        /// <summary>Every kind that payout granted, in grant order. Empty when
        /// <see cref="LastPayoutLevelNumber"/> is 0.</summary>
        public IReadOnlyList<PowerUpKind> LastPayoutKinds => _lastPayoutKinds;

        internal void RecordPayout(int levelNumber, List<PowerUpKind> kinds)
        {
            LastPayoutLevelNumber = levelNumber;
            _lastPayoutKinds.Clear();
            _lastPayoutKinds.AddRange(kinds);
        }

        internal void ClearPayout()
        {
            LastPayoutLevelNumber = 0;
            _lastPayoutKinds.Clear();
        }
    }
}
