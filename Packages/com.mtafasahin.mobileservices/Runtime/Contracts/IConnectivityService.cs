namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// Seam between the game and the platform's network reachability. Deliberately free of any
    /// UnityEngine type, exactly like <see cref="IAuthService"/> and <see cref="ILeaderboardsService"/>,
    /// so the rule that decides whether a score is submitted or banked is testable without a device.
    /// </summary>
    public interface IConnectivityService
    {
        /// <summary>
        /// True when the device reports no route to the network at all. Deliberately the pessimistic
        /// half of the question: reachability can claim a connection that turns out to be dead, so a
        /// false here is only a hint that an attempt is worth making — never a promise it will succeed.
        /// Callers must still handle a failed submission.
        /// </summary>
        bool IsOffline { get; }
    }
}
