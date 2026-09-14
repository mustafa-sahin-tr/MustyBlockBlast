using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Builds the persistence key for one lifetime counter. One key per stat, exactly as
    /// <see cref="PowerUpInventoryKey"/> does per power-up kind, so adding a stat never migrates or
    /// invalidates the others' saved totals.
    /// </summary>
    internal static class BadgeStatsKey
    {
        private const string KEY_PREFIX = "Badges.Stats.";

        /// <summary>Stable storage key for the given stat, e.g. <c>Badges.Stats.TotalPiecesPlaced</c>.</summary>
        internal static string For(BadgeStatType statType) => KEY_PREFIX + statType;
    }
}
