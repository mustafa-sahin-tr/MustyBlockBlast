using System.Collections.Generic;
using MustyBlockBlast.Core;
using Mtafasahin.Reactive;

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
    /// <para>
    /// Alongside the unlock latch each badge carries a claim latch: whether the coin reward it pays on
    /// unlocking has been collected yet. The two are separate because they move at different times —
    /// a badge unlocks mid-run when a counter crosses its threshold, and is claimed later by a tap on
    /// its tile — and a badge can sit unlocked-but-unclaimed across any number of sessions.
    /// </para>
    /// </summary>
    public sealed class BadgeModel
    {
        private readonly List<BadgeProgress> _badges = new List<BadgeProgress>();
        private readonly HashSet<string> _claimedBadgeIds = new HashSet<string>();
        private readonly List<string> _unlockedThisRun = new List<string>();

        /// <summary>Every tracked badge, in catalog order. Never null.</summary>
        public IReadOnlyList<BadgeProgress> Badges => _badges;

        /// <summary>
        /// Bumped by the owning System on every unlock and every claim, so a View can subscribe once and
        /// repaint the whole wall rather than watch each badge separately. The value itself means
        /// nothing; only the change does.
        /// </summary>
        public ReactiveProperty<int> Revision { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// Ids of the badges that unlocked during the current run, in unlock order. Cleared by the owning
        /// System when the next run starts, so the end-of-run result screen lists exactly this run's
        /// unlocks and never a previous run's. Never null.
        /// </summary>
        public IReadOnlyList<string> UnlockedThisRun => _unlockedThisRun;

        /// <summary>Whether the coin reward of <paramref name="badgeId"/> has already been paid out.</summary>
        public bool IsClaimed(string badgeId) => badgeId != null && _claimedBadgeIds.Contains(badgeId);

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

        /// <summary>Appends an unlock to this run's buffer.</summary>
        internal void AddUnlockedThisRun(string badgeId)
        {
            if (!string.IsNullOrEmpty(badgeId))
            {
                _unlockedThisRun.Add(badgeId);
            }
        }

        /// <summary>Empties this run's buffer. Called at run start by the owning System.</summary>
        internal void ClearUnlockedThisRun() => _unlockedThisRun.Clear();

        /// <summary>Latches <paramref name="badgeId"/> as claimed. One-way, like the unlock latch.</summary>
        internal void MarkClaimed(string badgeId)
        {
            if (!string.IsNullOrEmpty(badgeId))
            {
                _claimedBadgeIds.Add(badgeId);
            }
        }
    }
}
