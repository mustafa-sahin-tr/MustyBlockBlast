namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// The keys a leaderboard entry's metadata may carry. One place rather than a literal at each end,
    /// because the two ends are in different assemblies: the submitting systems write them, and
    /// <c>UnityLeaderboardsService</c> reads them back out of a fetched entry. A typo in either half
    /// would not fail to compile — it would silently cost every player their avatar.
    /// </summary>
    public static class LeaderboardMetadataKeys
    {
        /// <summary>
        /// Index into the preset avatar set, written as its decimal digits. A string rather than a
        /// number because the whole metadata payload is a flat string map: one shape for every key
        /// means no key needs its own type handling on the way back in.
        /// </summary>
        public const string AVATAR_ID = "avatarId";
    }
}
