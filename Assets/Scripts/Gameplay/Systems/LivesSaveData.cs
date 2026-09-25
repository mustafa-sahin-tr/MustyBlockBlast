using System;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// The persisted half of <see cref="LivesSystem"/> (issue #477): one JSON blob under a single
    /// PlayerPrefs key, shaped for <see cref="UnityEngine.JsonUtility"/> (public fields only), the same
    /// way <see cref="RewardRuleSaveData"/> is.
    /// </summary>
    [Serializable]
    internal sealed class LivesSaveData
    {
        /// <summary>Bumped whenever the persisted shape changes in a way a loader must know about.</summary>
        public const int CURRENT_SCHEMA_VERSION = 1;

        public int schemaVersion = CURRENT_SCHEMA_VERSION;

        /// <summary>Lives held as of the last write.</summary>
        public int lives;

        /// <summary>
        /// The local wall-clock hour the refill was last checked in, as whole hours since
        /// <see cref="DateTime.MinValue"/> (<c>Ticks / TicksPerHour</c>). Every hour stamp between this
        /// one and the current one is an xx:00 boundary crossed since, and each pays one refill.
        /// </summary>
        public long lastRefillHourStamp;
    }
}
