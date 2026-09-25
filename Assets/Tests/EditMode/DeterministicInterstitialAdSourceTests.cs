using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Pins the Editor/test interstitial stub (issue #501) to the <see cref="IInterstitialAdSource"/>
    /// contract the placement logic will be built against: an ad is always "shown" — except for a player
    /// who bought "remove ads", who never gets one, stub or not.
    /// </summary>
    public class DeterministicInterstitialAdSourceTests
    {
        private const string ADS_REMOVED_KEY = "Profile.AdsRemoved";

        [SetUp]
        public void ClearPersistedFlag()
        {
            PlayerPrefs.DeleteKey(ADS_REMOVED_KEY);
        }

        [TearDown]
        public void ClearPersistedFlagAfterwards()
        {
            PlayerPrefs.DeleteKey(ADS_REMOVED_KEY);
        }

        [Test]
        public void TryShowAsync_WithAdsNotRemoved_ReportsAnAdShown()
        {
            DeterministicInterstitialAdSource source = CreateSource();

            bool shown = source.TryShowAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(shown);
        }

        [Test]
        public void TryShowAsync_WithAdsRemoved_NeverShowsAnAd()
        {
            PlayerPrefs.SetInt(ADS_REMOVED_KEY, 1);
            DeterministicInterstitialAdSource source = CreateSource();

            bool shown = source.TryShowAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(shown);
        }

        [Test]
        public void TryShowAsync_WithACancelledToken_Throws()
        {
            DeterministicInterstitialAdSource source = CreateSource();
            var cancelled = new CancellationToken(canceled: true);

            Assert.Throws<System.OperationCanceledException>(
                () => source.TryShowAsync(cancelled).GetAwaiter().GetResult());
        }

        [Test]
        public void PreloadAsync_CompletesImmediately()
        {
            DeterministicInterstitialAdSource source = CreateSource();

            UniTask preload = source.PreloadAsync(CancellationToken.None);

            Assert.AreEqual(UniTaskStatus.Succeeded, preload.Status);
        }

        /// <summary>The stub only reads the flag, so the purchase-side collaborators are left null.</summary>
        private static DeterministicInterstitialAdSource CreateSource()
        {
            var adRemovalSystem = new AdRemovalSystem(
                new ProfileModel(), productConfig: null, purchaseService: null, receiptValidator: null);
            return new DeterministicInterstitialAdSource(adRemovalSystem);
        }
    }
}
