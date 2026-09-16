namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// What the leaderboard card has to say for itself right now. Every member other than
    /// <see cref="Ready"/> is a state where there are no rows to draw and the card must say why, because
    /// an empty card with no explanation reads as a broken card.
    /// </summary>
    public enum LeaderboardLoadState
    {
        /// <summary>Nothing has been asked for yet. The state a freshly built card sits in until it is
        /// first opened.</summary>
        Idle,

        Loading,

        /// <summary>A page came back. The row list is authoritative — including when it is empty, which
        /// is an unplayed board rather than a failure.</summary>
        Ready,

        /// <summary>The current mode has no boards at all, so there is nothing to rank against. An
        /// ordinary state for <see cref="GameMode.Path"/>, not an error — see <c>LeaderboardBoardIds</c>
        /// on why a level-bound run is not ranked.</summary>
        NotRanked,

        /// <summary>The board could not be read because the device is offline or has no identity yet.
        /// Separate from <see cref="Failed"/> because the answer for the player is different: wait for a
        /// connection rather than try again.</summary>
        Unavailable,

        /// <summary>The backend was reached and refused, or broke. Retrying is the only move.</summary>
        Failed,
    }
}
