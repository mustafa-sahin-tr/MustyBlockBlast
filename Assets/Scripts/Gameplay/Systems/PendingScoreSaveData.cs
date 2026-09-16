using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Scores earned offline and not yet accepted by the leaderboard backend. Stored as one JSON blob
    /// under a single key, the same way <see cref="BadgeSaveData"/> is — the queue only means anything
    /// as a whole, since draining it rewrites what is left.
    /// <para>
    /// Shaped for <see cref="UnityEngine.JsonUtility"/>: public fields only, no properties.
    /// <see cref="schemaVersion"/> is here so a later shape change can be migrated rather than
    /// discarded.
    /// </para>
    /// <para>
    /// The blob is obfuscated before it reaches PlayerPrefs — see
    /// <see cref="PendingScoreQueueSystem"/>, which owns the read and write.
    /// </para>
    /// </summary>
    [Serializable]
    internal sealed class PendingScoreSaveData
    {
        /// <summary>Bumped whenever the persisted shape changes in a way a loader must know about.</summary>
        public const int CURRENT_SCHEMA_VERSION = 1;

        public int schemaVersion = CURRENT_SCHEMA_VERSION;

        /// <summary>Queued scores, oldest first, so the backlog is retried in the order it was earned.</summary>
        public List<PendingScoreSaveEntry> entries = new List<PendingScoreSaveEntry>();
    }
}
