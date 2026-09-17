using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Which badges the player has already unlocked, and which of those they have already claimed the
    /// coin reward for. Stored as one JSON blob under a single key, the same way
    /// <see cref="LevelProgressSaveData"/> is — the set has to be written whole, because "is this
    /// badge unlocked" is only answerable against the complete list.
    /// <para>
    /// Shaped for <see cref="UnityEngine.JsonUtility"/>: public fields only, no properties, no set
    /// type (a list of ids stands in for one). <see cref="schemaVersion"/> is here so a later shape
    /// change can be migrated rather than discarded — and it has been used once already, see below.
    /// </para>
    /// <para>
    /// Only the latches are persisted, never the counters: those live in their own PlayerPrefs keys
    /// under <see cref="BadgeStatsSystem"/> and are the single source of truth for progress. A badge's
    /// progress bar is therefore always re-derived from the real counter at boot, and can never drift
    /// away from it.
    /// </para>
    /// <para>
    /// Schema history. Version 1 held only <see cref="unlockedBadgeIds"/>, and an unlock paid a
    /// power-up on the spot. Version 2 added <see cref="claimedBadgeIds"/> when badges started paying
    /// coins the player claims by tapping (issue #214). A version-1 blob is migrated by treating every
    /// badge it lists as unlocked as already claimed — those players were paid in power-ups at the
    /// time, and the decision was no retroactive coins.
    /// </para>
    /// </summary>
    [Serializable]
    internal sealed class BadgeSaveData
    {
        /// <summary>Bumped whenever the persisted shape changes in a way a loader must know about.</summary>
        public const int CURRENT_SCHEMA_VERSION = 2;

        public int schemaVersion = CURRENT_SCHEMA_VERSION;

        /// <summary>Ids of badges already unlocked. Membership, not order, is the meaning; an id no
        /// longer in the catalog is simply never matched.</summary>
        public List<string> unlockedBadgeIds = new List<string>();

        /// <summary>Ids of unlocked badges whose coin reward has already been paid out. Always a subset
        /// of <see cref="unlockedBadgeIds"/>; a badge in the first list but not this one is claimable.</summary>
        public List<string> claimedBadgeIds = new List<string>();
    }
}
