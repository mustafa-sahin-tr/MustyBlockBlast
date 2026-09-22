using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Google AdMob implementation of the three rewarded-ad seams — <see cref="IRewardSource"/>,
    /// <see cref="ICoinRewardSource"/> and <see cref="IRescueRewardSource"/> (issue #380). The only type
    /// in the project that touches the Google Mobile Ads SDK, on the same footing as
    /// <see cref="UnityCoinPurchaseService"/> is for Unity IAP: swapping ad networks, or stubbing one out
    /// for the Editor and tests, is one binding in <see cref="GameLifetimeScope"/>.
    /// <para>
    /// One class for three seams because the three are the same mechanic underneath — load a rewarded
    /// ad, show it, and find out whether the player watched enough of it to be paid — and only the
    /// bookkeeping around the answer differs. Each interface method is a thin wrapper over
    /// <see cref="ShowRewardedAdAsync"/> that turns a bool into its own result type; the seams stay
    /// separate at the interface level for the reasons written on them, and this class simply happens
    /// to satisfy all three.
    /// </para>
    /// <para>
    /// The SDK's API is callback-driven (consent, initialisation, load, show and reward each complete on
    /// a callback) while every seam above is a single awaitable call, so the body of this class is that
    /// translation: one completion source per outstanding step, completed from the SDK's handlers,
    /// exactly as <see cref="UnityCoinPurchaseService"/> does for the store. Nothing else lives here —
    /// no decision about what a reward is worth, no inventory, no coin arithmetic. Those stay in
    /// <see cref="PowerUpSystem"/>, <see cref="CurrencySystem"/> and <see cref="BoardSystem"/>.
    /// </para>
    /// <para>
    /// <b>Granted is true only on the SDK's reward-earned callback.</b> An ad that was closed early, an ad
    /// that failed to load or show, a consent flow that refuses ads, a timeout, or any exception the SDK
    /// throws all resolve to a false grant — never to an exception in the caller — because every seam
    /// documents a false grant as "the player did not earn it", not as an error. The one thing that does
    /// propagate is the caller's own cancellation, which the deterministic stubs propagate too.
    /// </para>
    /// <para>
    /// One ad at a time. A rewarded ad is full-screen and modal, so a second overlapping request has
    /// nowhere to go; it is refused with a false grant rather than queued behind the first, and the
    /// single in-flight slot is also what makes matching the SDK's events back to their awaiter
    /// unambiguous.
    /// </para>
    /// <para>
    /// The SDK is initialised lazily on the first request, and the User Messaging Platform consent flow
    /// runs before that initialisation, so no ad is ever requested before the player has been asked
    /// (issue #380, AC6). The readiness result is cached for the session on success and cleared on
    /// failure, so an SDK that could not come up now is asked again on the next tap rather than pinned
    /// as broken — the same policy <see cref="UnityCoinPurchaseService.EnsureReadyAsync"/> follows.
    /// </para>
    /// </summary>
    public sealed class AdMobRewardSource : IRewardSource, ICoinRewardSource, IRescueRewardSource
    {
        // ------------------------------------------------------------------------------------------
        // Google's public test rewarded unit — always fills, every ad is stamped "Test Ad" and earns
        // nothing. Kept only as a fallback for the Editor/EditMode-adjacent build configs this class
        // never actually runs under (see GameLifetimeScope's #if) and as a quick manual revert if the
        // real unit below ever needs pulling. Test ids: https://developers.google.com/admob/unity/test-ads
        // ------------------------------------------------------------------------------------------
        private const string TEST_ANDROID_REWARDED_AD_UNIT_ID = "ca-app-pub-3940256099942544/5224354917";

        // ------------------------------------------------------------------------------------------
        // Real rewarded unit from the developer's own AdMob account (app "Blockio Blast: Time Rush").
        // Matches the AdMob App ID entered in Assets > Google Mobile Ads > Settings... (Android field:
        // ca-app-pub-8909172296809126~5406503750) — the two must always be swapped together, since a
        // real unit id paired with a test App ID (or the reverse) is a policy violation on Google's side.
        // ------------------------------------------------------------------------------------------
        private const string LIVE_ANDROID_REWARDED_AD_UNIT_ID = "ca-app-pub-8909172296809126/7402111707";

        /// <summary>The unit every request loads.</summary>
        private const string REWARDED_AD_UNIT_ID = LIVE_ANDROID_REWARDED_AD_UNIT_ID;

        /// <summary>
        /// How long consent lookup, SDK initialisation and an ad load are each allowed before they count
        /// as a no-fill. Real time rather than game time because the game may well be paused behind the
        /// request. The consent form and the ad itself are deliberately not on a clock: both are paced by
        /// the player, and a form left open for a minute is not a failure.
        /// </summary>
        private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Completion of the one-time consent-and-initialise, cached so every later request awaits the
        /// same result instead of re-running the flow. Null until the first request asks for it, and
        /// cleared again on failure so the next request retries.
        /// </summary>
        private UniTaskCompletionSource<bool> _readySource;

        private bool _isRequestInFlight;

        /// <summary>
        /// Runs consent and SDK initialisation now rather than waiting for the first reward request —
        /// Google's own guidance for the reward-earned latency this buys back. Called once at boot by
        /// <see cref="AdWarmUpSystem"/>; every one of the three seam methods still calls
        /// <see cref="EnsureReadyAsync"/> itself and finds the cached result already sitting there, so
        /// nothing here is load-bearing for correctness — a boot that never reaches this still ends up
        /// consented and initialised on the player's first ad, exactly as before this existed.
        /// </summary>
        public async UniTask WarmUpAsync(CancellationToken cancellationToken)
        {
            await EnsureReadyAsync(cancellationToken);
        }

        public async UniTask<RewardResult> RequestRewardAsync(
            PowerUpKind kind, CancellationToken cancellationToken)
        {
            bool earned = await ShowRewardedAdAsync(cancellationToken);
            return new RewardResult(kind, earned);
        }

        public async UniTask<CoinRewardResult> RequestCoinRewardAsync(
            int amount, CancellationToken cancellationToken)
        {
            bool earned = await ShowRewardedAdAsync(cancellationToken);

            // The amount is the caller's ask echoed back on success and zero otherwise: the seam makes
            // the source the authority on the amount, and this source pays in full or not at all.
            return new CoinRewardResult(earned ? amount : 0, earned);
        }

        public async UniTask<RescueRewardResult> RequestRescueRewardAsync(CancellationToken cancellationToken)
        {
            bool earned = await ShowRewardedAdAsync(cancellationToken);
            return new RescueRewardResult(earned);
        }

        /// <summary>
        /// The whole mechanic, shared by all three seams: make sure the SDK is up, load one rewarded ad,
        /// show it, and report whether the reward-earned callback fired. Every failure path is a false
        /// return; only the caller's cancellation escapes.
        /// </summary>
        private async UniTask<bool> ShowRewardedAdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_isRequestInFlight)
            {
                return false;
            }

            _isRequestInFlight = true;
            try
            {
                bool isReady = await EnsureReadyAsync(cancellationToken);
                if (!isReady)
                {
                    return false;
                }

                RewardedAd rewardedAd = await LoadAsync(cancellationToken);
                if (rewardedAd == null)
                {
                    return false;
                }

                try
                {
                    return await ShowAsync(rewardedAd, cancellationToken);
                }
                finally
                {
                    // A RewardedAd is single-use and holds a native handle. Released whether the show
                    // succeeded, failed or was abandoned — an abandoned one keeps playing natively
                    // until the player closes it, but nothing here is awaiting it any more.
                    rewardedAd.Destroy();
                }
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                // The SDK throws synchronously on some platform misconfigurations, and a step that
                // outlives StepTimeout surfaces as a TimeoutException. Both are "no ad this time", not
                // a crash — the seams promise as much.
                Debug.LogError($"{nameof(AdMobRewardSource)}: rewarded ad request failed: {exception.Message}");
                return false;
            }
            finally
            {
                // Cleared even on cancellation, or the single in-flight slot would stay occupied for
                // the rest of the session and refuse every later request.
                _isRequestInFlight = false;
            }
        }

        /// <summary>
        /// Runs the consent flow and initialises the SDK, once. Every caller after the first awaits the
        /// same cached result. A failed attempt clears the cache rather than caching the failure, so an
        /// SDK that could not come up now is asked again on the next request.
        /// </summary>
        private async UniTask<bool> EnsureReadyAsync(CancellationToken cancellationToken)
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
                Debug.LogError($"{nameof(AdMobRewardSource)}: initialising the ads SDK failed: {exception.Message}");
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
        private async UniTask<bool> InitializeAsync(CancellationToken cancellationToken)
        {
            await GatherConsentAsync(cancellationToken);

            if (!ConsentInformation.CanRequestAds())
            {
                Debug.LogWarning($"{nameof(AdMobRewardSource)}: consent does not allow ad requests; no ad will be shown.");
                return false;
            }

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
                Debug.LogWarning($"{nameof(AdMobRewardSource)}: consent information update failed: {updateError.Message}");
                return;
            }

            // Not on the clock: the form is paced by the player.
            var formSource = new UniTaskCompletionSource<FormError>();
            ConsentForm.LoadAndShowConsentFormIfRequired(error => formSource.TrySetResult(error));

            FormError formError = await formSource.Task.AttachExternalCancellation(cancellationToken);
            await UniTask.SwitchToMainThread(cancellationToken);

            if (formError != null)
            {
                Debug.LogWarning($"{nameof(AdMobRewardSource)}: consent form failed: {formError.Message}");
            }
        }

        /// <summary>
        /// Loads one rewarded ad, or null on no-fill, error or timeout. An ad that arrives after the
        /// awaiter gave up is destroyed in the callback rather than leaked.
        /// </summary>
        private static async UniTask<RewardedAd> LoadAsync(CancellationToken cancellationToken)
        {
            var loadSource = new UniTaskCompletionSource<RewardedAd>();
            RewardedAd.Load(REWARDED_AD_UNIT_ID, new AdRequest(), (loadedAd, loadError) =>
            {
                if (loadError != null || loadedAd == null)
                {
                    string reason = loadError == null ? "no ad returned" : loadError.GetMessage();
                    Debug.LogWarning($"{nameof(AdMobRewardSource)}: rewarded ad failed to load: {reason}");
                    loadSource.TrySetResult(null);
                    return;
                }

                if (!loadSource.TrySetResult(loadedAd))
                {
                    loadedAd.Destroy();
                }
            });

            RewardedAd rewardedAd = await loadSource.Task
                .AttachExternalCancellation(cancellationToken)
                .Timeout(StepTimeout, DelayType.Realtime);
            await UniTask.SwitchToMainThread(cancellationToken);
            return rewardedAd;
        }

        /// <summary>
        /// Shows a loaded ad and reports whether the reward was earned. The reward-earned callback is
        /// the one and only thing that flips the answer to true; the full-screen-closed event is what
        /// ends the wait, carrying whatever the answer was by then. A show failure ends it with false.
        /// </summary>
        private static async UniTask<bool> ShowAsync(RewardedAd rewardedAd, CancellationToken cancellationToken)
        {
            if (!rewardedAd.CanShowAd())
            {
                Debug.LogWarning($"{nameof(AdMobRewardSource)}: the loaded rewarded ad reports it cannot be shown.");
                return false;
            }

            bool rewardEarned = false;
            var closedSource = new UniTaskCompletionSource<bool>();

            rewardedAd.OnAdFullScreenContentClosed += () => closedSource.TrySetResult(rewardEarned);
            rewardedAd.OnAdFullScreenContentFailed += adError =>
            {
                Debug.LogWarning($"{nameof(AdMobRewardSource)}: rewarded ad failed to show: {adError.GetMessage()}");
                closedSource.TrySetResult(false);
            };

            // Not on the clock: the ad is paced by the player, and closing it is what completes this.
            rewardedAd.Show(reward => rewardEarned = true);

            bool earned = await closedSource.Task.AttachExternalCancellation(cancellationToken);
            await UniTask.SwitchToMainThread(cancellationToken);
            return earned;
        }
    }
}
