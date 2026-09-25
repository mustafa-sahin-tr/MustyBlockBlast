using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// The part of the Google Mobile Ads SDK every ad format shares: the User Messaging Platform
    /// consent flow, SDK initialisation, and the single full-screen slot. Split out of
    /// <see cref="AdMobRewardSource"/> when <see cref="AdMobInterstitialSource"/> arrived (issue #501) so
    /// the two formats run the one consent-then-initialise sequence instead of two copies of it that
    /// could drift apart — consent in particular must happen exactly once, before any ad of any format
    /// is requested.
    /// <para>
    /// The readiness result is cached for the session on success and cleared on failure, so an SDK that
    /// could not come up now is asked again on the next request rather than pinned as broken — the same
    /// policy <see cref="UnityCoinPurchaseService.EnsureReadyAsync"/> follows.
    /// </para>
    /// <para>
    /// <b>One full-screen ad at a time, across formats.</b> A rewarded ad and an interstitial are both
    /// full-screen and modal, so a second one requested while the first is up has nowhere to go. The
    /// slot lives here rather than in each source so a rewarded ad can never land on top of an
    /// interstitial or the reverse; a request that finds it taken is refused, not queued.
    /// </para>
    /// </summary>
    public sealed class AdMobSdk
    {
        /// <summary>
        /// How long consent lookup, SDK initialisation and an ad load are each allowed before they count
        /// as a no-fill. Real time rather than game time because the game may well be paused behind the
        /// request. The consent form and the ad itself are deliberately not on a clock: both are paced by
        /// the player, and a form left open for a minute is not a failure.
        /// </summary>
        public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Completion of the one-time consent-and-initialise, cached so every later request awaits the
        /// same result instead of re-running the flow. Null until the first request asks for it, and
        /// cleared again on failure so the next request retries.
        /// </summary>
        private UniTaskCompletionSource<bool> _readySource;

        private bool _isFullScreenAdShowing;

        /// <summary>
        /// Claims the full-screen slot. False when another ad already holds it; every true must be paired
        /// with <see cref="ReleaseFullScreen"/> in a <c>finally</c>, or the slot stays taken for the rest
        /// of the session and refuses every later ad.
        /// </summary>
        public bool TryAcquireFullScreen()
        {
            if (_isFullScreenAdShowing)
            {
                return false;
            }

            _isFullScreenAdShowing = true;
            return true;
        }

        public void ReleaseFullScreen()
        {
            _isFullScreenAdShowing = false;
        }

        /// <summary>
        /// Runs the consent flow and initialises the SDK, once. Every caller after the first awaits the
        /// same cached result. A failed attempt clears the cache rather than caching the failure, so an
        /// SDK that could not come up now is asked again on the next request.
        /// </summary>
        public async UniTask<bool> EnsureReadyAsync(CancellationToken cancellationToken)
        {
            if (_readySource != null)
            {
                return await _readySource.Task.AttachExternalCancellation(cancellationToken);
            }

            var readySource = new UniTaskCompletionSource<bool>();
            _readySource = readySource;

            bool isReady = false;
            try
            {
                isReady = await InitializeAsync(cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                Debug.LogError($"{nameof(AdMobSdk)}: initialising the ads SDK failed: {exception.Message}");
            }
            finally
            {
                readySource.TrySetResult(isReady);
                if (!isReady && ReferenceEquals(_readySource, readySource))
                {
                    _readySource = null;
                }
            }

            return isReady;
        }

        /// <summary>
        /// Consent first, then the SDK — in that order, because the SDK must not be initialised (and
        /// no ad requested) until the User Messaging Platform says ads may be requested. UMP's own
        /// answer is what decides that: a returning player who consented last session passes straight
        /// through, a player in a consent region sees Google's default form once.
        /// </summary>
        private static async UniTask<bool> InitializeAsync(CancellationToken cancellationToken)
        {
            await GatherConsentAsync(cancellationToken);

            if (!ConsentInformation.CanRequestAds())
            {
                Debug.LogWarning($"{nameof(AdMobSdk)}: consent does not allow ad requests; no ad will be shown.");
                return false;
            }

            // Play Console declares this app's target audience as 5+ (Everyone) with no neutral age
            // screen, so every ad request must be tagged child-directed per Google's policy.
            var requestConfiguration = new RequestConfiguration
            {
                TagForChildDirectedTreatment = TagForChildDirectedTreatment.True,
                TagForUnderAgeOfConsent = TagForUnderAgeOfConsent.True,
                MaxAdContentRating = MaxAdContentRating.G
            };
            MobileAds.SetRequestConfiguration(requestConfiguration);

            var initializeSource = new UniTaskCompletionSource<bool>();
            MobileAds.Initialize(status => initializeSource.TrySetResult(status != null));

            // AttachExternalCancellation rather than a cancellable await: the SDK call takes no token,
            // so cancelling abandons the await and leaves initialisation to finish on its own.
            bool isInitialized = await initializeSource.Task
                .AttachExternalCancellation(cancellationToken)
                .Timeout(StepTimeout, DelayType.Realtime);

            // SDK callbacks are not guaranteed to arrive on Unity's main thread, and everything after
            // an await here goes on to touch the SDK again or hand a result back to a View.
            await UniTask.SwitchToMainThread(cancellationToken);
            return isInitialized;
        }

        /// <summary>
        /// The UMP flow as Google documents it: refresh consent information, then load and show the
        /// consent form if — and only if — it is required. Errors are logged and swallowed because
        /// the caller's next question, <see cref="ConsentInformation.CanRequestAds"/>, is the real
        /// verdict either way: a consent lookup that fails offline still permits ads for a player who
        /// consented last session, and still refuses them for one who has not.
        /// </summary>
        private static async UniTask GatherConsentAsync(CancellationToken cancellationToken)
        {
            var updateSource = new UniTaskCompletionSource<FormError>();
            ConsentInformation.Update(new ConsentRequestParameters(), error => updateSource.TrySetResult(error));

            FormError updateError = await updateSource.Task
                .AttachExternalCancellation(cancellationToken)
                .Timeout(StepTimeout, DelayType.Realtime);
            await UniTask.SwitchToMainThread(cancellationToken);

            if (updateError != null)
            {
                Debug.LogWarning($"{nameof(AdMobSdk)}: consent information update failed: {updateError.Message}");
                return;
            }

            // Not on the clock: the form is paced by the player.
            var formSource = new UniTaskCompletionSource<FormError>();
            ConsentForm.LoadAndShowConsentFormIfRequired(error => formSource.TrySetResult(error));

            FormError formError = await formSource.Task.AttachExternalCancellation(cancellationToken);
            await UniTask.SwitchToMainThread(cancellationToken);

            if (formError != null)
            {
                Debug.LogWarning($"{nameof(AdMobSdk)}: consent form failed: {formError.Message}");
            }
        }
    }
}
