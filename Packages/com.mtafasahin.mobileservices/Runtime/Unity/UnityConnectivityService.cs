using UnityEngine;

namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// Unity implementation of <see cref="IConnectivityService"/>. The only type in the project that
    /// reads <see cref="Application.internetReachability"/>, so a test — or a later switch to a real
    /// reachability probe — is one binding in the LifetimeScope, exactly like
    /// <see cref="UnityLeaderboardsService"/>.
    /// </summary>
    public sealed class UnityConnectivityService : IConnectivityService
    {
        /// <summary>
        /// Read on demand rather than cached: reachability changes while the app runs, and the property
        /// is a cheap platform query, so there is nothing to gain from holding a value that can go stale.
        /// </summary>
        public bool IsOffline => Application.internetReachability == NetworkReachability.NotReachable;
    }
}
