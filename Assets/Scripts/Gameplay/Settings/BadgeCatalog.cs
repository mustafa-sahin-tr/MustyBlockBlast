using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The game's badge content: the list of authored <see cref="BadgeConfig"/> rows. This is the
    /// asset the developer opens to add, edit or retune lifetime achievements before shipping — badge
    /// content is deliberately not in C#, exactly as level content is not.
    /// <para>
    /// Indexing contract: a badge is identified by its <see cref="BadgeConfig.Id"/>, not by its
    /// position in the list. <see cref="Find"/> scans for the id, so reordering rows in the Inspector
    /// never changes which badge is which — which matters here more than it does for levels, because
    /// the id is what the unlocked-badge save list stores.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Badge Catalog", fileName = "BadgeCatalog")]
    public sealed class BadgeCatalog : ScriptableObject
    {
        private static readonly BadgeConfig[] EmptyBadges = new BadgeConfig[0];

        [Tooltip("Authored badges. Identified by their Id field — list order is display order only.")]
        [SerializeField] private List<BadgeConfig> _badges = new List<BadgeConfig>();

        /// <summary>Every authored badge, in list order. Never null.</summary>
        public IReadOnlyList<BadgeConfig> Badges => _badges ?? (IReadOnlyList<BadgeConfig>)EmptyBadges;

        /// <summary>The badge with this id, or null when none is authored. First match wins.</summary>
        public BadgeConfig Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            IReadOnlyList<BadgeConfig> badges = Badges;
            for (int badgeIndex = 0; badgeIndex < badges.Count; badgeIndex++)
            {
                BadgeConfig badge = badges[badgeIndex];
                if (badge != null && badge.Id == id)
                {
                    return badge;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-time feedback for the developer authoring badges: clamps the numeric field and logs
        /// any entry that would throw when its <c>BadgeDefinition</c> is built, plus any duplicate id —
        /// two badges sharing an id would share one save entry and so unlock each other.
        /// </summary>
        private void OnValidate()
        {
            if (_badges == null)
            {
                return;
            }

            for (int badgeIndex = 0; badgeIndex < _badges.Count; badgeIndex++)
            {
                BadgeConfig badge = _badges[badgeIndex];
                if (badge == null)
                {
                    continue;
                }

                badge.ValidateInEditor();

                if (!badge.IsValid(out string error))
                {
                    Debug.LogWarning($"{nameof(BadgeCatalog)} entry {badgeIndex} ('{badge.Id}') is invalid: {error}", this);
                    continue;
                }

                for (int otherIndex = 0; otherIndex < badgeIndex; otherIndex++)
                {
                    BadgeConfig other = _badges[otherIndex];
                    if (other != null && other.Id == badge.Id)
                    {
                        Debug.LogWarning(
                            $"{nameof(BadgeCatalog)} entry {badgeIndex} repeats the id '{badge.Id}' already used by "
                            + $"entry {otherIndex}. Ids must be unique — they key the unlocked-badge save data.",
                            this);
                        break;
                    }
                }
            }
        }
#endif
    }
}
