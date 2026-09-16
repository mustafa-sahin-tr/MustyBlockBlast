namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// How durable the player's identity is. Anonymous is the default and is deliberately a first-class
    /// state rather than an error: every identity feature — display name, avatar, leaderboard entry —
    /// works without ever linking, and linking only buys the player survival across reinstalls and
    /// devices.
    /// <para>
    /// Public because <c>ProfilePanelView</c> in the Presentation assembly renders it. One value per
    /// provider rather than a bool plus a provider string: the panel branches on exactly these three
    /// cases, so a compile-time set is what keeps a fourth provider from silently rendering blank.
    /// </para>
    /// </summary>
    public enum AccountLinkStatus
    {
        Anonymous = 0,
        LinkedApple = 1,
        LinkedGoogle = 2,
    }
}
