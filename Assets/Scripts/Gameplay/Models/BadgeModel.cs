using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Every badge the game authors, all tracked at once. Deliberately unlike
    /// <see cref="ObjectiveModel"/>, which holds a single current objective: a level is a queue the
    /// player walks, whereas badges are a wall the player fills in — all of them are live, all of them
    /// are shown together, and none of them is "next".
    /// <para>
    /// Seeding is left to the caller; in the running game that is <c>BadgeSystem</c>, which builds the
    /// list from <c>BadgeCatalog</c> at construction.
    /// </para>
    /// </summary>
    public sealed class BadgeModel
    {
        private readonly List<BadgeProgress> _badges = new List<BadgeProgress>();

        /// <summary>Every tracked badge, in catalog order. Never null.</summary>
        public IReadOnlyList<BadgeProgress> Badges => _badges;

        /// <summary>Replaces the tracked set. Called once, at boot, by the owning System.</summary>
        internal void SetBadges(IReadOnlyList<BadgeProgress> badges)
        {
            _badges.Clear();

            if (badges == null)
            {
                return;
            }

            for (int badgeIndex = 0; badgeIndex < badges.Count; badgeIndex++)
            {
                _badges.Add(badges[badgeIndex]);
            }
        }
    }
}
