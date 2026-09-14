using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// The project's first structured save state: everything else persists as flat PlayerPrefs keys,
    /// but level progression needs several related fields written atomically, so it is stored as one
    /// JSON blob under a single key (see <see cref="LevelProgressionSystem"/>).
    /// <para>
    /// Shaped for <see cref="UnityEngine.JsonUtility"/>: public fields only, no properties, no
    /// dictionary (a list of entries stands in for one). <see cref="schemaVersion"/> is here so a
    /// later shape change can be migrated rather than discarded — cheap now, painful to retrofit.
    /// </para>
    /// </summary>
    [Serializable]
    internal sealed class LevelProgressSaveData
    {
        /// <summary>Bumped whenever the persisted shape changes in a way a loader must know about.</summary>
        public const int CURRENT_SCHEMA_VERSION = 1;

        public int schemaVersion = CURRENT_SCHEMA_VERSION;

        /// <summary>1-based level the player is currently attempting.</summary>
        public int currentLevelNumber = 1;

        /// <summary>
        /// Cumulative-scope objective progress, keyed by objective id. Per-run progress is absent by
        /// design: it resets every run, so persisting it would hand the player back progress the rules
        /// say they lost.
        /// </summary>
        public List<CumulativeProgressEntry> cumulativeProgress = new List<CumulativeProgressEntry>();

        /// <summary>One saved objective counter. JsonUtility cannot serialize a dictionary, so the
        /// id travels alongside the value.</summary>
        [Serializable]
        internal sealed class CumulativeProgressEntry
        {
            public string objectiveId;
            public int currentValue;
        }
    }
}
