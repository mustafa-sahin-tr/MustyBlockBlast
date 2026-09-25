using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GoogleMobileAds.Api;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Google AdMob implementation of <see cref="IInterstitialAdSource"/> (issue #501) — the project's
    /// first forced-ad format, alongside <see cref="AdMobRewardSource"/>'s opt-in rewarded ones. Consent,
    /// SDK initialisation and the one-full-screen-ad-at-a-time slot are <see cref="AdMobSdk"/>'s, shared
    /// with the rewarded source; what lives here is only the interstitial's own load/show cycle.
    /// <para>
    /// <b>Load ahead, show only what is ready.</b> The seam promises never to make the player wait on a
    /// load, so this keeps at most one loaded ad cached. <see cref="TryShowAsync"/> shows that ad if it
    /// is there and still fresh; otherwise it returns false straight away and starts a background load
    /// for the next opportunity. After every show — successful or not — the next ad is loaded the same way.
    /// </para>
    /// <para>
    /// <b>"Remove ads" stops loads as well as shows.</b> With <see cref="AdRemovalSystem.AdsRemoved"/> set,
    /// no ad is requested at all, and an ad loaded before the purchase is discarded rather than shown.
    /// </para>
    /// <para>
    /// Every failure — no consent, no-fill, timeout, a show error, an SDK exception — resolves to
    /// "no ad this time", never to an exception in the caller; only the caller's cancellation escapes.
    /// </para>
    /// </summary>
    public sealed class AdMobInterstitialSource : IInterstitialAdSource, IDisposable
    {
        // ------------------------------------------------------------------------------------------
        // Google's public test interstitial units — always fill, every ad is stamped "Test Ad" and
        // earns nothing. Used by every development build (see InterstitialAdUnitId). Test ids: https://developers.google.com/admob/unity/test-ads
        // ------------------------------------------------------------------------------------------
        private const string TEST_ANDROID_INTERSTITIAL_AD_UNIT_ID = "ca-app-pub-3940256099942544/1033173712";
        private const string TEST_IOS_INTERSTITIAL_AD_UNIT_ID = "ca-app-pub-3940256099942544/4411468910";

        // ------------------------------------------------------------------------------------------
        // Real interstitial units from the developer's own AdMob account (app "Blockio Blast: Time
        // Rush"). Each must match the AdMob App ID entered for its platform in Assets > Google Mobile
        // Ads > Settings... (Android: ca-app-pub-8909172296809126~5406503750, iOS:
        // ca-app-pub-8909172296809126~5705870749) — see AdMobRewardSource for why they swap together.
        // ------------------------------------------------------------------------------------------
        private const string LIVE_ANDROID_INTERSTITIAL_AD_UNIT_ID = "ca-app-pub-8909172296809126/7334802717";
        private const string LIVE_IOS_INTERSTITIAL_AD_UNIT_ID = "ca-app-pub-8909172296809126/5543298189";

        /// <summary>
        /// The unit every load requests: Google's test unit in a development build, the real one in a
        /// release build. Development builds (anything built with "Development Build", including every
        /// run from Xcode or Build And Run) therefore always get fill-guaranteed "Test Ad" creatives on any
        /// device with no test-device registration, and a developer tapping one can never register as
        /// invalid traffic on the live account. Only release builds ever touch the live unit.
        /// </summary>
#if UNITY_IOS
        private static string InterstitialAdUnitId =>
            Debug.isDebugBuild ? TEST_IOS_INTERSTITIAL_AD_UNIT_ID : LIVE_IOS_INTERSTITIAL_AD_UNIT_ID;
#else
        private static string InterstitialAdUnitId =>
            Debug.isDebugBuild ? TEST_ANDROID_INTERSTITIAL_AD_UNIT_ID : LIVE_ANDROID_INTERSTITIAL_AD_UNIT_ID;
#endif

        /// <summary>
        /// A loaded AdMob ad expires an hour after it was fetched; one older than this is thrown away and
        /// reloaded rather than shown, with a margin so it cannot expire between the check and the show.
        /// </summary>
        private const float MAX_LOADED_AD_AGE_SECONDS = 55f * 60f;

        private readonly AdMobSdk _sdk;
        private readonly AdRemovalSystem _adRemovalSystem;

        /// <summary>Cancels the background loads this source starts on its own; cancelled on dispose.</summary>
        private readonly CancellationTokenSource _lifetimeCts = new();

        private InterstitialAd _loadedAd;
        private float _loadedAtRealtime;
        private bool _isLoading;

        public AdMobInterstitialSource(AdMobSdk sdk, AdRemovalSystem adRemovalSystem)
        {
            _sdk = sdk;
            _adRemovalSystem = adRemovalSystem;
        }

        public async UniTask PreloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_adRemovalSystem.AdsRemoved || _isLoading || HasFreshAd())
            {
                return;
            }

            DiscardLoadedAd();
            _isLoading = true;
            try
            {
                bool isReady = await _sdk.EnsureReadyAsync(cancellationToken);

                // Re-checked after the await: the purchase can land while consent/init is running.
                if (!isReady || _adRemovalSystem.AdsRemoved)
                {
                    return;
                }

                InterstitialAd loadedAd = await LoadAsync(cancellationToken);
                if (loadedAd == null)
                {
                    return;
                }

                _loadedAd = loadedAd;
                _loadedAtRealtime = Time.realtimeSinceStartup;
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                // A step that outlives StepTimeout surfaces as a TimeoutException, and the SDK throws
                // synchronously on some platform misconfigurations — both are simply "no ad loaded".
                Debug.LogWarning($"{nameof(AdMobInterstitialSource)}: interstitial preload failed: {exception.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        public async UniTask<bool> TryShowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_adRemovalSystem.AdsRemoved)
            {
                DiscardLoadedAd();
                return false;
            }

            if (!HasFreshAd() || !_loadedAd.CanShowAd())
            {
                // Nothing ready: never make the player wait — load for next time and move on.
                DiscardLoadedAd();
                PreloadInBackground();
                return false;
            }

            if (!_sdk.TryAcquireFullScreen())
            {
                // Another ad is up; keep this one cached for the next opportunity.
                return false;
            }

            // Taken out of the cache before showing: an interstitial is single-use.
            InterstitialAd interstitialAd = _loadedAd;
            _loadedAd = null;

            try
            {
                return await ShowAsync(interstitialAd, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                Debug.LogError($"{nameof(AdMobInterstitialSource)}: interstitial show failed: {exception.Message}");
                return false;
            }
            finally
            {
                // Released even on cancellation, or the shared slot would refuse every later ad.
                interstitialAd.Destroy();
                _sdk.ReleaseFullScreen();
                PreloadInBackground();
            }
        }

        public void Dispose()
        {
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
            DiscardLoadedAd();
        }

        private bool HasFreshAd()
        {
            return _loadedAd != null && Time.realtimeSinceStartup - _loadedAtRealtime < MAX_LOADED_AD_AGE_SECONDS;
        }

        private void DiscardLoadedAd()
        {
            if (_loadedAd == null)
            {
                return;
            }

            _loadedAd.Destroy();
            _loadedAd = null;
        }

        private void PreloadInBackground()
        {
            if (_lifetimeCts.IsCancellationRequested)
            {
                return;
            }

            PreloadAsync(_lifetimeCts.Token).SuppressCancellationThrow().Forget();
        }

        /// <summary>
        /// Loads one interstitial, or null on no-fill, error or timeout. An ad that arrives after the
        /// awaiter gave up is destroyed in the callback rather than leaked.
        /// </summary>
        private static async UniTask<InterstitialAd> LoadAsync(CancellationToken cancellationToken)
        {
            var loadSource = new UniTaskCompletionSource<InterstitialAd>();
            InterstitialAd.Load(InterstitialAdUnitId, new AdRequest(), (loadedAd, loadError) =>
            {
                if (loadError != null || loadedAd == null)
                {
                    string reason = loadError == null ? "no ad returned" : loadError.GetMessage();
                    Debug.LogWarning($"{nameof(AdMobInterstitialSource)}: interstitial failed to load: {reason}");
                    loadSource.TrySetResult(null);
                    return;
                }

                if (!loadSource.TrySetResult(loadedAd))
                {
                    loadedAd.Destroy();
                }
            });

            InterstitialAd interstitialAd = await loadSource.Task
                .AttachExternalCancellation(cancellationToken)
                .Timeout(AdMobSdk.StepTimeout, DelayType.Realtime);
            await UniTask.SwitchToMainThread(cancellationToken);
            return interstitialAd;
        }

        /// <summary>
        /// Shows a loaded interstitial and completes when the player closes it (true) or it fails to
        /// show (false). Not on the clock: the ad is paced by the player.
        /// </summary>
        private static async UniTask<bool> ShowAsync(InterstitialAd interstitialAd, CancellationToken cancellationToken)
        {
            var closedSource = new UniTaskCompletionSource<bool>();

            interstitialAd.OnAdFullScreenContentClosed += () => closedSource.TrySetResult(true);
            interstitialAd.OnAdFullScreenContentFailed += adError =>
            {
                Debug.LogWarning($"{nameof(AdMobInterstitialSource)}: interstitial failed to show: {adError.GetMessage()}");
                closedSource.TrySetResult(false);
            };

            interstitialAd.Show();

            bool shown = await closedSource.Task.AttachExternalCancellation(cancellationToken);
            await UniTask.SwitchToMainThread(cancellationToken);
            return shown;
        }
    }
}
