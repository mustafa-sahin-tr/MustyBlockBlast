namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// A Path level's move budget ran out short of its target score and the one "+moves" offer is now
    /// up (issue #465). Published by <c>MoveBudgetSystem</c>; the out-of-moves sheet opens on it. The
    /// run is held, not over: the player's answer — watch the ad, or decline — decides what comes next.
    /// </summary>
    public readonly struct MovesRanOutMessage
    {
    }
}
