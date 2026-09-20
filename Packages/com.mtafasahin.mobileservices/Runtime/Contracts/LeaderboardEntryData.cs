namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// One row of a ranked board, as the game sees it. A backend-free projection of whatever the
    /// leaderboard service returns, for the same reason <see cref="ILeaderboardsService"/> itself is
    /// free of SDK types: the board view renders these, so it can be built and reasoned about without a
    /// live backend.
    /// <para>
    /// A struct rather than a class because a fetched page is a fixed set of small values that nothing
    /// mutates after it is read — and a top-fifteen page is then one array's worth of memory rather than
    /// fifteen objects for the collector to walk.
    /// </para>
    /// </summary>
    public readonly struct LeaderboardEntryData
    {
        public LeaderboardEntryData(int rank, string playerName, int score, int avatarId)
        {
            Rank = rank;
            PlayerName = playerName;
            Score = score;
            AvatarId = avatarId;
        }

        /// <summary>Position on the board, as the backend ranked it. Zero-based, exactly as the service
        /// reports it — the view is what adds one for display, so nothing here has to guess whether a
        /// rank has already been made human-readable.</summary>
        public int Rank { get; }

        /// <summary>
        /// The name the player set through their profile, or empty for a player who never set one.
        /// Empty is an ordinary state, not a fault: an anonymous player ranks exactly like anyone else
        /// (see <see cref="LeaderboardSystem"/>), and a name is optional, so the view substitutes a
        /// placeholder rather than drawing a blank row.
        /// </summary>
        public string PlayerName { get; }

        /// <summary>Narrowed from the backend's wider numeric type, mirroring how a score is submitted as
        /// an <see cref="int"/> — that is what a run's score actually is.</summary>
        public int Score { get; }

        /// <summary>
        /// Index into the preset avatar set, from the entry's own metadata. Defaults to the first preset
        /// for an entry filed before avatars were submitted, or one whose metadata is unreadable: a
        /// picture everyone has is worth more than a correct blank.
        /// </summary>
        public int AvatarId { get; }
    }
}
