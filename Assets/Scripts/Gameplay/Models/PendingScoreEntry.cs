namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// One run's score waiting to reach the leaderboard backend. Carries the mode alongside the score
    /// because the boards a score belongs on are decided by the mode it was played in, and by the time
    /// the queue drains — possibly a launch later — the live mode is no longer that mode.
    /// <para>
    /// A readonly struct: an entry is a value that is never edited in place, only added and removed.
    /// </para>
    /// </summary>
    public readonly struct PendingScoreEntry
    {
        public PendingScoreEntry(GameMode mode, int score)
        {
            Mode = mode;
            Score = score;
        }

        public GameMode Mode { get; }

        public int Score { get; }
    }
}
