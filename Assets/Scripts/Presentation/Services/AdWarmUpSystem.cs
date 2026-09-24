using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer.Unity;
using Debug = UnityEngine.Debug;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Runs the ad SDK's consent flow and initialisation as soon as a scene boots, on the same footing
    /// as <see cref="AuthBootSystem"/> — a VContainer entry point, not a MonoBehaviour, side by side
    /// with everything else <see cref="IAsyncStartable"/> rather than blocking any of it.
    /// <para>
    /// Registered only on the platforms <see cref="AdMobRewardSource"/> itself is bound on (see
    /// <c>GameLifetimeScope</c>'s <c>#if UNITY_EDITOR || (!UNITY_ANDROID &amp;&amp; !UNITY_IOS)</c> split)
    /// — nowhere else does this type even exist, so there is no stub or no-op version of it to reason about.
    /// </para>
    /// <para>
    /// Purely a latency win, never a correctness dependency: every one of <see cref="AdMobRewardSource"/>'s
    /// three seam methods calls the same readiness check itself before it ever requests an ad. A player
    /// who reaches for a reward before this finishes (or while it is still failing and being retried)
    /// simply pays that cost inline instead, exactly as before this system existed.
    /// </para>
    /// </summary>
    public sealed class AdWarmUpSystem : IAsyncStartable
    {
        private readonly AdMobRewardSource _adMobRewardSource;

        public AdWarmUpSystem(AdMobRewardSource adMobRewardSource)
        {
            _adMobRewardSource = adMobRewardSource;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            try
            {
                await _adMobRewardSource.WarmUpAsync(cancellation).SuppressCancellationThrow();
            }
            catch (Exception exception)
            {
                // Not a boot failure: the same request retries inline the first time a player actually
                // reaches for a reward, per EnsureReadyAsync's cache-on-success/clear-on-failure policy.
                Debug.LogError($"Ad SDK warm-up failed: {exception.Message}");
            }
        }
    }
}
