using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Systems;
using VContainer.Unity;
using Debug = UnityEngine.Debug;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Runs the ad SDK's consent flow and initialisation as soon as a scene boots, then gets the first
    /// interstitial loaded (issue #501), on the same footing as <see cref="AuthBootSystem"/> — a
    /// VContainer entry point, not a MonoBehaviour, side by side with everything else
    /// <see cref="IAsyncStartable"/> rather than blocking any of it.
    /// <para>
    /// Registered only on the platforms <see cref="AdMobSdk"/> itself is bound on (see
    /// <c>GameLifetimeScope</c>'s <c>#if UNITY_EDITOR || (!UNITY_ANDROID &amp;&amp; !UNITY_IOS)</c> split)
    /// — nowhere else does this type even exist, so there is no stub or no-op version of it to reason about.
    /// </para>
    /// <para>
    /// Purely a latency win, never a correctness dependency: <see cref="AdMobRewardSource"/> and
    /// <see cref="AdMobInterstitialSource"/> both run the same readiness check themselves before they
    /// ever request an ad. A player who reaches for a reward before this finishes (or while it is still
    /// failing and being retried) simply pays that cost inline instead. The interstitial preload
    /// honours "remove ads" on its own.
    /// </para>
    /// </summary>
    public sealed class AdWarmUpSystem : IAsyncStartable
    {
        private readonly AdMobSdk _adMobSdk;
        private readonly IInterstitialAdSource _interstitialAdSource;

        public AdWarmUpSystem(AdMobSdk adMobSdk, IInterstitialAdSource interstitialAdSource)
        {
            _adMobSdk = adMobSdk;
            _interstitialAdSource = interstitialAdSource;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            try
            {
                bool isReady = await _adMobSdk.EnsureReadyAsync(cancellation);
                if (isReady)
                {
                    await _interstitialAdSource.PreloadAsync(cancellation);
                }
            }
            catch (OperationCanceledException)
            {
                // Scene torn down mid-warm-up; nothing to do.
            }
            catch (Exception exception)
            {
                // Not a boot failure: the same request retries inline the first time an ad is actually
                // wanted, per EnsureReadyAsync's cache-on-success/clear-on-failure policy.
                Debug.LogError($"Ad SDK warm-up failed: {exception.Message}");
            }
        }
    }
}
