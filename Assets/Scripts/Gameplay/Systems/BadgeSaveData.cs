using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Which badges the player has already unlocked. Stored as one JSON blob under a single key, the
    /// same way <see cref="LevelProgressSaveData"/> is — the set has to be written whole, because "is
    /// this badge unlocked" is only answerable against the complete list.
    /// <para>
    /// Shaped for <see cref="UnityEngine.JsonUtility"/>: public fields only, no properties, no set
    /// type (a list of ids stands in for one). <see cref="schemaVersion"/> is here so a later shape
    /// change can be migrated rather than discarded.
    /// </para>
    /// <para>
    /// Only the unlock latch is persisted, never the counters: those live in their own PlayerPrefs
    /// keys under <see cref="BadgeStatsSystem"/> and are the single source of truth for progress. A
    /// badge's progress bar is therefore always re-derived from the real counter at boot, and can
    /// never drift away from it.
    /// </para>
    /// </summary>
    [Serializable]
    internal sealed class BadgeSaveData
    {
        /// <summary>Bumped whenever the persisted shape changes in a way a loader must know about.</summary>
        public const int CURRENT_SCHEMA_VERSION = 1;

        public int schemaVersion = CURRENT_SCHEMA_VERSION;

        /// <summary>Ids of badges already unlocked and already paid out. Membership, not order, is the
        /// meaning; an id no longer in the catalog is simply never matched.</summary>
        public List<string> unlockedBadgeIds = new List<string>();
    }
}
