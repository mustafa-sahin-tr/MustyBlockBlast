using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the ad-removal purchase contract (issue #161): a validated purchase records the product as
    /// owned and that record survives a relaunch; a dismissal, a store failure or an unvouched receipt
    /// records nothing at all; and the flag is idempotent, which is why — unlike the coin credit next
    /// door — this path keeps no transaction-id ledger.
    /// <para>
    /// Reuses <see cref="StubCoinPurchaseService"/> and <see cref="StubPurchaseReceiptValidator"/>
    /// unchanged rather than growing a third pair of doubles: this path asks the same two seams the same
    /// two questions <see cref="CurrencySystemPurchaseTests"/> does, and a second copy of a stub is a
    /// second thing that can drift away from the interface.
    /// </para>
    /// <para>
    /// Nothing here exercises the real <c>UnityCoinPurchaseService</c>, for the reason
    /// <see cref="CurrencySystemPurchaseTests"/> states: EditMode has no store to connect to. What is
    /// testable is everything above that seam, plus — at the bottom of this fixture — the shipped
    /// validator's widened SKU recognition, which is the one part of the store-side generalization that
    /// is a pure function of config.
    /// </para>
    /// </summary>
    public class AdRemovalSystemTests
    {
        private const string ADS_REMOVED_KEY = "Profile.AdsRemoved";

        /// <summary>The SKU a freshly created <see cref="RemoveAdsProductConfig"/> ships with, so the
        /// lookups these tests make go through the same value the game boots on.</summary>
        private const string REMOVE_ADS_SKU = "remove_ads";

        /// <summary>A SKU from the shipped coin line-up, used only to prove the two products are told
        /// apart — a bundle receipt must not be readable as an ad-removal one.</summary>
        private const string COIN_SKU = "coins_medium";

        private const string TRANSACTION_ID = "txn-ads-0001";

        private RemoveAdsProductConfig _productConfig;
        private CoinBundleConfig _bundleConfig;

        /// <summary>
        /// The system loads the flag in its constructor, so a value left behind by a previous test would
        /// silently decide whether the next one's purchase had anything to do.
        /// </summary>
        [SetUp]
        public void ClearPersistedFlag()
        {
            PlayerPrefs.DeleteKey(ADS_REMOVED_KEY);
            _productConfig = ScriptableObject.CreateInstance<RemoveAdsProductConfig>();
            _bundleConfig = ScriptableObject.CreateInstance<CoinBundleConfig>();
        }

        [TearDown]
        public void ClearPersistedFlagAfterwards()
        {
            PlayerPrefs.DeleteKey(ADS_REMOVED_KEY);
            DestroyIfPresent(_productConfig);
            DestroyIfPresent(_bundleConfig);
        }

        // --- The shipped placeholder product ---

        /// <summary>
        /// Pins that a freshly created config names a sellable product, so a scene booting on the
        /// LifetimeScope's fallback still has something to offer. Not a claim about the SKU itself — it
        /// is a placeholder expected to be set to whatever the store consoles are given.
        /// </summary>
        [Test]
        public void RemoveAdsProductConfig_OnAFreshInstance_NamesASellableProduct()
        {
            Assert.IsTrue(_productConfig.IsValid);
            Assert.AreEqual(REMOVE_ADS_SKU, _productConfig.Sku);
            Assert.IsTrue(_productConfig.Matches(REMOVE_ADS_SKU));
        }

        /// <summary>A blank or foreign SKU is not this product. Matters because the same method is both
        /// the store guard and the validator's recognition test.</summary>
        [TestCase("")]
        [TestCase(null)]
        [TestCase(COIN_SKU)]
        public void RemoveAdsProductConfig_ForAnotherSku_DoesNotMatch(string sku)
        {
            Assert.IsFalse(_productConfig.Matches(sku));
        }

        // --- AC1, AC2: a validated purchase records the product as owned ---

        /// <summary>
        /// AC2's recording half: the flag is set, it reaches the disk, the store is asked for the
        /// configured SKU and is acknowledged afterwards.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_WithAValidatedReceipt_RecordsTheProductAsOwned()
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, REMOVE_ADS_SKU));
            AdRemovalSystem system = CreateSystem(
                profileModel, store, new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));

            bool purchased = Purchase(system);

            Assert.IsTrue(purchased);
            Assert.IsTrue(profileModel.AdsRemoved.Value);
            Assert.IsTrue(system.AdsRemoved);
            Assert.AreEqual(1, PlayerPrefs.GetInt(ADS_REMOVED_KEY, 0));
            Assert.AreEqual(REMOVE_ADS_SKU, store.LastSku);
            Assert.AreEqual(1, store.ConfirmedTransactionIds.Count);
        }

        /// <summary>
        /// A verdict worth no coins is the *only* verdict this product can ever produce, so the flag must
        /// not read the amount. The one test that would fail if this path were ever written to reuse
        /// <see cref="CurrencySystem.PurchaseCoinBundleAsync"/>'s "positive amount" gate, which would
        /// refuse every valid ad-removal receipt there is.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_IgnoresTheValidatorsCoinAmount()
        {
            var profileModel = new ProfileModel();
            AdRemovalSystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, REMOVE_ADS_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));

            Assert.IsTrue(Purchase(system));
            Assert.IsTrue(profileModel.AdsRemoved.Value);
        }

        /// <summary>
        /// The purchase touches nothing else on the profile. Removing ads is not an economy event: it
        /// mints no coin and takes nothing out of the convertible pool, which is exactly why it is not a
        /// faucet on <see cref="CurrencySystem"/>.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_LeavesTheCurrencyFieldsAlone()
        {
            var profileModel = new ProfileModel();
            AdRemovalSystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, REMOVE_ADS_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));

            Assert.IsTrue(Purchase(system));

            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, profileModel.TotalScoreEarned.Value);
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
        }

        /// <summary>
        /// Repeating the purchase is a no-op rather than a bug, which is the whole reason this path needs
        /// no transaction-id ledger: setting an already-set flag changes nothing, so a store replaying
        /// the same receipt after a crash or a reinstall lands on exactly the right state.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_ReplayingTheSameTransaction_IsIdempotent()
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, REMOVE_ADS_SKU));
            AdRemovalSystem system = CreateSystem(
                profileModel, store, new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));

            Assert.IsTrue(Purchase(system));
            Assert.IsTrue(Purchase(system));

            Assert.IsTrue(profileModel.AdsRemoved.Value);
            Assert.AreEqual(1, PlayerPrefs.GetInt(ADS_REMOVED_KEY, 0));
        }

        // --- AC3: a refusal records nothing ---

        /// <summary>AC3 for a dismissal: the player backed out of the store prompt, so nothing is
        /// recorded — and the validator is never consulted, because there is no receipt to judge.</summary>
        [Test]
        public void PurchaseRemoveAdsAsync_WhenTheStoreIsCancelled_RecordsNothing()
        {
            var profileModel = new ProfileModel();
            var validator = new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true);
            AdRemovalSystem system = CreateSystem(
                profileModel, new StubCoinPurchaseService(CoinPurchaseOutcome.Cancelled), validator);

            Assert.IsFalse(Purchase(system));
            Assert.IsFalse(profileModel.AdsRemoved.Value);
            Assert.IsFalse(PlayerPrefs.HasKey(ADS_REMOVED_KEY));
            Assert.AreEqual(0, validator.ValidateCount);
        }

        /// <summary>AC3 for a store failure. Same outcome as a dismissal, which is why the two are
        /// distinguished on the result rather than by what they record.</summary>
        [Test]
        public void PurchaseRemoveAdsAsync_WhenTheStoreFails_RecordsNothing()
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CoinPurchaseOutcome.Failed);
            AdRemovalSystem system = CreateSystem(
                profileModel, store, new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));

            Assert.IsFalse(Purchase(system));
            Assert.IsFalse(profileModel.AdsRemoved.Value);
            Assert.IsFalse(PlayerPrefs.HasKey(ADS_REMOVED_KEY));
            Assert.AreEqual(0, store.ConfirmedTransactionIds.Count);
        }

        /// <summary>
        /// AC3's validation half: the store took the money and handed back a receipt, and the product is
        /// still not owned because the validator would not vouch for it. The one test that would fail if
        /// validation were ever reduced to a formality this path ignores.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_WithAnInvalidReceipt_RecordsNothing()
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, REMOVE_ADS_SKU));
            AdRemovalSystem system = CreateSystem(
                profileModel, store, StubPurchaseReceiptValidator.Rejecting());

            Assert.IsFalse(Purchase(system));
            Assert.IsFalse(profileModel.AdsRemoved.Value);
            Assert.IsFalse(PlayerPrefs.HasKey(ADS_REMOVED_KEY));

            // Nothing acknowledged either: telling the store the goods were handed over would discard a
            // replay the player may still be owed.
            Assert.AreEqual(0, store.ConfirmedTransactionIds.Count);
        }

        /// <summary>
        /// No product to sell is refused without the store being asked at all: asking for a blank SKU
        /// could only fail less clearly, and it is logged as an error because an unconfigured store
        /// product is a misconfigured build rather than an ordinary state.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_WithNoConfiguredProduct_NeverReachesTheStore()
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, REMOVE_ADS_SKU));
            var system = new AdRemovalSystem(
                profileModel,
                productConfig: null,
                store,
                new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));

            LogAssert.Expect(LogType.Error, new Regex("has no product to buy"));

            Assert.IsFalse(Purchase(system));
            Assert.AreEqual(0, store.PurchaseCount);
            Assert.IsFalse(profileModel.AdsRemoved.Value);
            Assert.IsFalse(PlayerPrefs.HasKey(ADS_REMOVED_KEY));
        }

        // --- Persistence across a relaunch ---

        /// <summary>
        /// The flag survives the app being killed: a fresh model-and-system pair over the same
        /// PlayerPrefs boots owning the product. Without this the player would pay once per launch.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_ThenANewSystem_StillOwnsTheProduct()
        {
            var profileModel = new ProfileModel();
            AdRemovalSystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, REMOVE_ADS_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));
            Assert.IsTrue(Purchase(system));

            // A store and a validator that would both refuse, so the reloaded state can only have come
            // from the disk.
            var reloadedModel = new ProfileModel();
            AdRemovalSystem reloaded = CreateSystem(
                reloadedModel,
                new StubCoinPurchaseService(CoinPurchaseOutcome.Failed),
                StubPurchaseReceiptValidator.Rejecting());

            Assert.IsTrue(reloadedModel.AdsRemoved.Value, "The purchase must survive a relaunch.");
            Assert.IsTrue(reloaded.AdsRemoved);
        }

        /// <summary>
        /// And the other half: a relaunch after a *refused* purchase boots not owning it. The pair of
        /// these two is what proves the flag is read back from the disk rather than defaulted either way.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_WhenRefused_ThenANewSystem_StillDoesNotOwnTheProduct()
        {
            var profileModel = new ProfileModel();
            AdRemovalSystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CoinPurchaseOutcome.Cancelled),
                new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));
            Assert.IsFalse(Purchase(system));

            var reloadedModel = new ProfileModel();
            AdRemovalSystem reloaded = CreateSystem(
                reloadedModel,
                new StubCoinPurchaseService(CoinPurchaseOutcome.Cancelled),
                new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));

            Assert.IsFalse(reloadedModel.AdsRemoved.Value);
            Assert.IsFalse(reloaded.AdsRemoved);
        }

        // --- The shipped validator's widened SKU recognition ---

        /// <summary>
        /// The validator now recognises two kinds of product, and this is the ad-removal one: approved,
        /// for no coins. Zero is "not applicable" here rather than "worth nothing" — see
        /// <see cref="ValidatedPurchase.CoinAmount"/> — and the consumer reads only
        /// <see cref="ValidatedPurchase.IsValid"/>.
        /// </summary>
        [Test]
        public void DeterministicPurchaseReceiptValidator_ForTheRemoveAdsSku_ApprovesForNoCoins()
        {
            var validator = new DeterministicPurchaseReceiptValidator(_bundleConfig, _productConfig);

            ValidatedPurchase verdict = Validate(validator, REMOVE_ADS_SKU);

            Assert.IsTrue(verdict.IsValid);
            Assert.AreEqual(0, verdict.CoinAmount);
        }

        /// <summary>
        /// The no-regression half, and the reason the widening is safe: a coin bundle is still priced off
        /// <see cref="CoinBundleConfig"/> exactly as it was before the ad-removal product existed. The
        /// bundle branch is reached with the new config present and non-matching, which is the case every
        /// shipped build is in on every coin purchase.
        /// </summary>
        [Test]
        public void DeterministicPurchaseReceiptValidator_ForACoinSku_StillApprovesTheConfiguredAmount()
        {
            var validator = new DeterministicPurchaseReceiptValidator(_bundleConfig, _productConfig);
            _bundleConfig.TryGetBundle(COIN_SKU, out CoinBundle bundle);

            ValidatedPurchase verdict = Validate(validator, COIN_SKU);

            Assert.IsTrue(verdict.IsValid);
            Assert.AreEqual(bundle.CoinAmount, verdict.CoinAmount);
            Assert.Greater(verdict.CoinAmount, 0);
        }

        /// <summary>A SKU neither config knows is still rejected. Widening the recognised line-up must
        /// not widen it to everything.</summary>
        [Test]
        public void DeterministicPurchaseReceiptValidator_ForAnUnknownSku_StillRejects()
        {
            var validator = new DeterministicPurchaseReceiptValidator(_bundleConfig, _productConfig);

            ValidatedPurchase verdict = Validate(validator, "no_such_product");

            Assert.IsFalse(verdict.IsValid);
            Assert.AreEqual(0, verdict.CoinAmount);
        }

        /// <summary>
        /// An ad-removal receipt that somehow reached the coin credit path buys no coins. It is approved
        /// by the validator — it is a genuine purchase — and refused by
        /// <see cref="CurrencySystem.PurchaseCoinBundleAsync"/>'s existing positive-amount gate, which is
        /// the behaviour that let the two products share one verdict type without a discriminator.
        /// </summary>
        [Test]
        public void DeterministicPurchaseReceiptValidator_TheRemoveAdsVerdict_BuysNoCoins()
        {
            var validator = new DeterministicPurchaseReceiptValidator(_bundleConfig, _productConfig);

            ValidatedPurchase verdict = Validate(validator, REMOVE_ADS_SKU);

            Assert.IsTrue(
                verdict.IsValid && verdict.CoinAmount <= 0,
                "CurrencySystem's credit gate must see this as nothing to credit.");
        }

        /// <summary>
        /// End to end through the shipped validator rather than a stub: the real recognition path records
        /// the product as owned. The one test that would fail if the validator and the config disagreed
        /// about which SKU this product is.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_ThroughTheShippedValidator_RecordsTheProductAsOwned()
        {
            var profileModel = new ProfileModel();
            AdRemovalSystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, REMOVE_ADS_SKU)),
                new DeterministicPurchaseReceiptValidator(_bundleConfig, _productConfig));

            Assert.IsTrue(Purchase(system));
            Assert.IsTrue(profileModel.AdsRemoved.Value);
        }

        /// <summary>
        /// And the refusal end to end: a store that handed back a receipt for some other product is
        /// refused, because the shipped validator will not recognise it as this one. What keeps a cheap
        /// coin bundle's receipt from buying the ad-removal product.
        /// </summary>
        [Test]
        public void PurchaseRemoveAdsAsync_WhenTheStoreReturnsAnotherProductsReceipt_RecordsNothing()
        {
            var profileModel = new ProfileModel();
            AdRemovalSystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, COIN_SKU)),
                new RemoveAdsOnlyValidator(_productConfig));

            Assert.IsFalse(Purchase(system));
            Assert.IsFalse(profileModel.AdsRemoved.Value);
        }

        private AdRemovalSystem CreateSystem(
            ProfileModel profileModel,
            ICoinPurchaseService purchaseService,
            IPurchaseReceiptValidator receiptValidator)
            => new AdRemovalSystem(profileModel, _productConfig, purchaseService, receiptValidator);

        private static bool Purchase(AdRemovalSystem system)
            => system.PurchaseRemoveAdsAsync(CancellationToken.None).GetAwaiter().GetResult();

        private static ValidatedPurchase Validate(IPurchaseReceiptValidator validator, string sku)
            => validator
                .ValidateAsync(CreateReceipt(TRANSACTION_ID, sku), CancellationToken.None)
                .GetAwaiter().GetResult();

        private static PurchaseReceipt CreateReceipt(string transactionId, string sku)
            => new PurchaseReceipt(transactionId, sku, "{\"stub\":true}");

        private static void DestroyIfPresent(ScriptableObject asset)
        {
            if (asset != null)
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// A validator that honours only the ad-removal SKU and rejects everything else. Distinct from
        /// <see cref="StubPurchaseReceiptValidator"/>, which answers the same way whatever it is handed:
        /// the test above needs a verdict that actually depends on the receipt's SKU, so that a store
        /// returning the wrong product's receipt is refused rather than waved through.
        /// </summary>
        private sealed class RemoveAdsOnlyValidator : IPurchaseReceiptValidator
        {
            private readonly RemoveAdsProductConfig _config;

            internal RemoveAdsOnlyValidator(RemoveAdsProductConfig config)
            {
                _config = config;
            }

            public UniTask<ValidatedPurchase> ValidateAsync(
                PurchaseReceipt receipt, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(
                    _config.Matches(receipt.Sku)
                        ? new ValidatedPurchase(coinAmount: 0, isValid: true)
                        : ValidatedPurchase.Rejected);
            }
        }
    }
}
