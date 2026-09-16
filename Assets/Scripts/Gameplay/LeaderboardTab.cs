namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// Which of a mode's two boards is being looked at. Every mode that ranks has exactly one of each
    /// (see <c>LeaderboardBoardIds</c>), so this is a choice between boards rather than a filter over
    /// one: an all-time standing and a weekly one are separate boards on the backend, and the weekly one
    /// resets on its own schedule.
    /// </summary>
    public enum LeaderboardTab
    {
        /// <summary>Every score ever filed for the mode. The default, because it is the board a player
        /// who has just set a personal best is looking for.</summary>
        AllTime,

        Weekly,
    }
}
