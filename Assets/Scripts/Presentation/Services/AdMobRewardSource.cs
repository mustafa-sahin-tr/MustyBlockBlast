using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GoogleMobileAds.Api;
using MessagePipe;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Google AdMob implementation of the four rewarded-ad seams — <see cref="IRewardSource"/>,
    /// <see cref="ICoinRewardSource"/>, <see cref="IRescueRewardSource"/> (issue #380) and
    /// <see cref="ILivesRewardSource"/> (issue #478). The only type
    /// in the project that shows a rewarded ad through the Google Mobile Ads SDK, on the same footing as
    /// <see cref="UnityCoinPurchaseService"/> is for Unity IAP: swapping ad networks, or stubbing one out
    /// for the Editor and tests, is one binding in <see cref="GameLifetimeScope"/>.
    /// <para>
    /// One class for four seams because the four are the same mechanic underneath — load a rewarded
    /// ad, show it, and find out whether the player watched enough of it to be paid — and only the
    /// bookkeeping around the answer differs. Each interface method is a thin wrapper over
    /// <see cref="ShowRewardedAdAsync"/> that turns a bool into its own result type; the seams stay
    /// separate at the interface level for the reasons written on them, and this class simply happens
    /// to satisfy all four.
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
    /// One ad at a time. A rewarded ad is full-screen and modal, so a request made while any ad —
    /// rewarded or interstitial — holds <see cref="AdMobSdk"/>'s full-screen slot is refused with a false
    /// grant rather than queued behind it, and that single slot is also what makes matching the SDK's
    /// events back to their awaiter unambiguous.
    /// </para>
    /// <para>
    /// Consent and SDK initialisation belong to <see cref="AdMobSdk"/> (split out in issue #501 so the
    /// interstitial format shares them): it runs before the first request of any format, so no ad is
    /// ever requested before the player has been asked (issue #380, AC6).
    /// </para>
    /// </summary>
    public sealed class AdMobRewardSource
        : IRewardSource, ICoinRewardSource, IRescueRewardSource, ILivesRewardSource, IExtraMovesRewardSource
    {
        // ------------------------------------------------------------------------------------------
        // Google's public test rewarded units — always fill, every ad is stamped "Test Ad" and earns
        // nothing. Used by every development build (see RewardedAdUnitId). Test ids:
        // https://developers.google.com/admob/unity/test-ads
        // ------------------------------------------------------------------------------------------
        private const string TEST_ANDROID_REWARDED_AD_UNIT_ID = "ca-app-pub-3940256099942544/5224354917";
        private const string TEST_IOS_REWARDED_AD_UNIT_ID = "ca-app-pub-3940256099942544/1712485313";

        // ------------------------------------------------------------------------------------------
        // Real rewarded units from the developer's own AdMob account (app "Blockio Blast: Time Rush").
        // Each must match the AdMob App ID entered for its platform in Assets > Google Mobile Ads >
        // Settings... (Android: ca-app-pub-8909172296809126~5406503750, iOS:
        // ca-app-pub-8909172296809126~5705870749) — App ID and unit id must always be swapped together
        // per platform, since a real unit id paired with a test App ID (or the reverse) is a policy
        // violation on Google's side.
        // ------------------------------------------------------------------------------------------
        private const string LIVE_ANDROID_REWARDED_AD_UNIT_ID = "ca-app-pub-8909172296809126/7402111707";
        private const string LIVE_IOS_REWARDED_AD_UNIT_ID = "ca-app-pub-8909172296809126/2022610757";

        /// <summary>
        /// The unit every load requests: Google's test unit in a development build, the real one in a
        /// release build. Development builds (anything built with "Development Build", including every
        /// run from Xcode or Build And Run) therefore always get fill-guaranteed "Test Ad" creatives on any
        /// device with no test-device registration, and a developer tapping one can never register as
        /// invalid traffic on the live account. Only release builds ever touch the live unit.
        /// </summary>
#if UNITY_IOS
        private static string RewardedAdUnitId =>
            Debug.isDebugBuild ? TEST_IOS_REWARDED_AD_UNIT_ID : LIVE_IOS_REWARDED_AD_UNIT_ID;
#else
        private static string RewardedAdUnitId =>
            Debug.isDebugBuild ? TEST_ANDROID_REWARDED_AD_UNIT_ID : LIVE_ANDROID_REWARDED_AD_UNIT_ID;
#endif

        private readonly AdMobSdk _sdk;
        private readonly IPublisher<RewardedAdShownMessage> _rewardedAdShownPublisher;

        public AdMobRewardSource(AdMobSdk sdk, IPublisher<RewardedAdShownMessage> rewardedAdShownPublisher)
        {
            _sdk = sdk;
            _rewardedAdShownPublisher = rewardedAdShownPublisher;
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

        public async UniTask<LivesRewardResult> RequestLivesRewardAsync(
            int amount, CancellationToken cancellationToken)
        {
            bool earned = await ShowRewardedAdAsync(cancellationToken);

            // Paid in full or not at all, as the coin seam is; LivesSystem clamps what it banks.
            return new LivesRewardResult(earned ? amount : 0, earned);
        }

        public async UniTask<ExtraMovesRewardResult> RequestExtraMovesRewardAsync(CancellationToken cancellationToken)
        {
            bool earned = await ShowRewardedAdAsync(cancellationToken);
            return new ExtraMovesRewardResult(earned);
        }

        /// <summary>
        /// The whole mechanic, shared by all four seams: make sure the SDK is up, load one rewarded ad,
        /// show it, and report whether the reward-earned callback fired. Every failure path is a false
        /// return; only the caller's cancellation escapes.
        /// </summary>
        private async UniTask<bool> ShowRewardedAdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_sdk.TryAcquireFullScreen())
            {
                return false;
            }

            try
            {
                bool isReady = await _sdk.EnsureReadyAsync(cancellationToken);
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
                    bool earned = await ShowAsync(rewardedAd, cancellationToken);

                    // Earned or not, the player just sat through an ad (issue #502): no interstitial
                    // should follow it straight away.
                    _rewardedAdShownPublisher.Publish(new RewardedAdShownMessage());
                    return earned;
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
                // Released even on cancellation, or the shared full-screen slot would stay occupied for
                // the rest of the session and refuse every later ad of either format.
                _sdk.ReleaseFullScreen();
            }
        }

        /// <summary>
        /// Loads one rewarded ad, or null on no-fill, error or timeout. An ad that arrives after the
        /// awaiter gave up is destroyed in the callback rather than leaked.
        /// </summary>
        private static async UniTask<RewardedAd> LoadAsync(CancellationToken cancellationToken)
        {
            var loadSource = new UniTaskCompletionSource<RewardedAd>();
            RewardedAd.Load(RewardedAdUnitId, new AdRequest(), (loadedAd, loadError) =>
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
                .Timeout(AdMobSdk.StepTimeout, DelayType.Realtime);
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
