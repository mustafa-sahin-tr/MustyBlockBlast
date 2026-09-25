using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Seam between the forced-ad placements (issue #388) and whatever actually shows an interstitial.
    /// Free of any ad-SDK type for the same reason <see cref="IRewardSource"/> is: the decision of
    /// <i>when</i> to show one stays testable without an SDK.
    /// <para>
    /// <b>Never makes the player wait for a load.</b> An interstitial sits between the player and their
    /// next run, so <see cref="TryShowAsync"/> only shows an ad that is already loaded; with none ready
    /// it returns false at once and starts loading one for next time. <see cref="PreloadAsync"/> is how
    /// a caller gets one ready ahead of the moment it is wanted.
    /// </para>
    /// <para>
    /// A player who bought "remove ads" (<see cref="AdRemovalSystem.AdsRemoved"/>) is never shown one and
    /// never has one loaded on their behalf — the source itself honours that, so no caller can forget to.
    /// </para>
    /// </summary>
    public interface IInterstitialAdSource
    {
        /// <summary>
        /// Loads an interstitial if none is loaded or loading. Completes when the load settles either way;
        /// a no-fill is not an error. Only the caller's own cancellation propagates.
        /// </summary>
        UniTask PreloadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Shows the loaded interstitial and completes once the player closes it. False — immediately —
        /// when no ad is ready, ads are removed, another full-screen ad is up, or the ad fails to show;
        /// true only when an ad was actually shown. Only the caller's own cancellation propagates.
        /// </summary>
        UniTask<bool> TryShowAsync(CancellationToken cancellationToken);
    }
}
