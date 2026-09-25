using System;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// The persisted half of <see cref="RewardRuleSystem"/> (issue #464): one JSON blob under a single
    /// PlayerPrefs key, shaped for <see cref="UnityEngine.JsonUtility"/> (public fields only), the same
    /// way <see cref="LevelProgressSaveData"/> and <see cref="BadgeSaveData"/> are.
    /// <para>
    /// Nothing about payouts is stored: a rule pays on the step that moves the streak onto a multiple
    /// of its threshold, and the streak is written in the same breath as the grant, so there is no
    /// "already paid" latch to keep.
    /// </para>
    /// </summary>
    [Serializable]
    internal sealed class RewardRuleSaveData
    {
        /// <summary>Bumped whenever the persisted shape changes in a way a loader must know about.</summary>
        public const int CURRENT_SCHEMA_VERSION = 1;

        public int schemaVersion = CURRENT_SCHEMA_VERSION;

        /// <summary>Consecutive Path levels cleared on the first attempt.</summary>
        public int firstTryStreak;

        /// <summary>
        /// The frontier level the player has failed at least once since last clearing one, or 0. Its
        /// eventual clear is not a first-try clear, so it leaves the streak at zero rather than
        /// starting a new one at one.
        /// </summary>
        public int failedLevelNumber;
    }
}
