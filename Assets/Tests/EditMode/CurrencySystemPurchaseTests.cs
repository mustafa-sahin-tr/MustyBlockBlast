using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the real-money purchase contract (issue #164): a validated purchase credits the exact
    /// amount the validator vouched for and announces it; a dismissal or a store failure credits
    /// nothing; an unvouched receipt credits nothing; and — the one that matters most — the same
    /// transaction id credits exactly once, however many times it comes back, across a restart, and even
    /// against a validator that would happily approve it again.
    /// <para>
    /// Its own fixture rather than more of <see cref="CurrencySystemTests"/>, which is already past a
    /// thousand lines covering four other faucets and a sink. These tests also need something none of
    /// those do — a bundle line-up, a scripted store and a scripted validator — and a shared
    /// <c>SetUp</c> that built all three for tests that never touch them would make the older file
    /// harder to read for no gain.
    /// </para>
    /// <para>
    /// Nothing here exercises the real <c>UnityCoinPurchaseService</c>: EditMode has no store to connect
    /// to, so it could only ever fail. What is under test is everything above that seam, which is where
    /// all of the crediting, the validation gate and the duplicate protection live.
    /// </para>
    /// </summary>
    public class CurrencySystemPurchaseTests
    {
        private const string COIN_BALANCE_KEY = "Profile.CoinBalance";
        private const string TOTAL_SCORE_EARNED_KEY = "Profile.TotalScoreEarned";
        private const string SCORE_CONVERTED_KEY = "Profile.ScoreConverted";
        private const string CONSUMED_TRANSACTION_IDS_KEY = "Profile.ConsumedTransactionIds";

        /// <summary>A SKU from the shipped placeholder line-up, so the config lookups these tests make
        /// go through the same rows the game boots with.</summary>
        private const string KNOWN_SKU = "coins_medium";

        private const string TRANSACTION_ID = "txn-0001";

        /// <summary>A frontier past the last power-up gate, matching <see cref="CurrencySystemTests"/>:
        /// nothing here is about gating, and a purchase test blocked by one would be noise.</summary>
        private const int ALL_KINDS_UNLOCKED_LEVEL = 99;

        private TestMessageBroker<ScoreConvertedToCoinsMessage> _convertedBroker;
        private TestMessageBroker<CoinsGrantedFromAdMessage> _adGrantBroker;
        private TestMessageBroker<CoinsGrantedFromPurchaseMessage> _purchaseGrantBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<CoinCellsClearedMessage> _coinCellsBroker;
        private CurrencyConfig _config;
        private PowerUpPriceConfig _priceConfig;
        private CoinBundleConfig _bundleConfig;
        private LevelProgressionModel _levelProgressionModel;

        /// <summary>
        /// CurrencySystem loads the balance and the consumed-transaction set in its constructor, so a
        /// transaction left behind by a previous test would silently decide whether the next one's
        /// purchase is a duplicate.
        /// </summary>
        [SetUp]
        public void ClearPersistedCurrency()
        {
            DeleteCurrencyKeys();
            _convertedBroker = new TestMessageBroker<ScoreConvertedToCoinsMessage>();
            _adGrantBroker = new TestMessageBroker<CoinsGrantedFromAdMessage>();
            _purchaseGrantBroker = new TestMessageBroker<CoinsGrantedFromPurchaseMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _coinCellsBroker = new TestMessageBroker<CoinCellsClearedMessage>();
            _config = ScriptableObject.CreateInstance<CurrencyConfig>();
            _priceConfig = ScriptableObject.CreateInstance<PowerUpPriceConfig>();
            _bundleConfig = ScriptableObject.CreateInstance<CoinBundleConfig>();
            _levelProgressionModel = new LevelProgressionModel();
            _levelProgressionModel.CurrentLevelNumber.Value = ALL_KINDS_UNLOCKED_LEVEL;
        }

        [TearDown]
        public void ClearPersistedCurrencyAfterwards()
        {
            DeleteCurrencyKeys();
            DestroyIfPresent(_config);
            DestroyIfPresent(_priceConfig);
            DestroyIfPresent(_bundleConfig);
        }

        // --- The shipped placeholder line-up ---

        /// <summary>
        /// Pins the placeholder bundles the rest of these tests buy. Not a claim about the economy — the
        /// SKUs and amounts are expected to be retuned in the asset — only that a freshly created config
        /// describes a sellable line-up, so a scene booting on the fallback still has a storefront, and
        /// that a larger bundle never pays fewer coins than a smaller one.
        /// </summary>
        [Test]
        public void CoinBundleConfig_OnAFreshInstance_DescribesASellableLineUp()
        {
            Assert.GreaterOrEqual(_bundleConfig.BundleCount, 3, "The line-up should offer 3-4 bundles.");
            Assert.LessOrEqual(_bundleConfig.BundleCount, 4);

            int previousAmount = 0;
            for (int bundleIndex = 0; bundleIndex < _bundleConfig.BundleCount; bundleIndex++)
            {
                CoinBundle bundle = _bundleConfig.BundleAt(bundleIndex);
                Assert.IsTrue(bundle.IsValid, $"Bundle {bundleIndex} is not sellable.");
                Assert.IsNotEmpty(bundle.DisplayName);
                Assert.Greater(bundle.CoinAmount, previousAmount, "Bundles should climb in coin value.");
                previousAmount = bundle.CoinAmount;
            }
        }

        [Test]
        public void CoinBundleConfig_ForAnUnknownSku_ReportsAMiss()
        {
            Assert.IsFalse(_bundleConfig.TryGetBundle("coins_nonexistent", out CoinBundle bundle));
            Assert.IsFalse(bundle.IsValid);
            Assert.IsFalse(_bundleConfig.TryGetBundle(string.Empty, out CoinBundle _));
        }

        /// <summary>An out-of-range index yields an unsellable bundle rather than throwing: the
        /// storefront already has to handle "there is nothing here", and one path for it beats two.</summary>
        [Test]
        public void CoinBundleConfig_ForAnOutOfRangeIndex_YieldsAnUnsellableBundle()
        {
            Assert.IsFalse(_bundleConfig.BundleAt(-1).IsValid);
            Assert.IsFalse(_bundleConfig.BundleAt(_bundleConfig.BundleCount).IsValid);
        }

        // --- AC1, AC3: a validated purchase credits exactly what it is worth ---

        /// <summary>
        /// AC3's crediting half: the validator's figure lands in the balance, reaches the disk, and is
        /// announced with the SKU that earned it. The amount is deliberately not any bundle's authored
        /// value, so a credit path that priced the purchase itself — from the client's claimed SKU,
        /// which is the very thing validation exists to distrust — would fail here rather than pass by
        /// coincidence.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_WithAValidatedReceipt_CreditsTheValidatorsAmount()
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU));
            CurrencySystem system = CreateSystem(
                profileModel, store, new StubPurchaseReceiptValidator(coinAmount: 1234));

            bool credited = Purchase(system, KNOWN_SKU);

            Assert.IsTrue(credited);
            Assert.AreEqual(1234, profileModel.CoinBalance.Value);
            Assert.AreEqual(1234, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            Assert.AreEqual(KNOWN_SKU, store.LastSku);

            Assert.AreEqual(1, _purchaseGrantBroker.Published.Count);
            CoinsGrantedFromPurchaseMessage message = _purchaseGrantBroker.Published[0];
            Assert.AreEqual(KNOWN_SKU, message.Sku);
            Assert.AreEqual(1234, message.Amount);
            Assert.AreEqual(1234, message.NewCoinBalance);
        }

        /// <summary>
        /// A purchase is not a conversion and not an ad: it takes nothing out of the convertible pool,
        /// leaves both counters behind it alone, and announces itself on neither of the other channels.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_LeavesTheConvertiblePoolAndTheOtherChannelsAlone()
        {
            var profileModel = new ProfileModel();
            var scoreModel = new ScoreModel();
            scoreModel.Score.Value = 400;
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 600),
                scoreModel);
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));

            Assert.IsTrue(Purchase(system, KNOWN_SKU));

            Assert.AreEqual(600, profileModel.CoinBalance.Value);
            Assert.AreEqual(400, system.AvailableToConvert);
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
            Assert.AreEqual(0, _convertedBroker.Published.Count);
            Assert.AreEqual(0, _adGrantBroker.Published.Count);
        }

        /// <summary>The coins a purchase banks are coins: they buy power-ups exactly as converted,
        /// ad-granted or coin-cell ones do, which is the whole point of paying into the same
        /// balance.</summary>
        [Test]
        public void PurchaseCoinBundleAsync_TheCreditedCoinsCanBuyAPowerUp()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 500));

            Assert.IsTrue(Purchase(system, KNOWN_SKU));

            Assert.AreEqual(
                PowerUpPurchaseResult.Success, system.TryPurchasePowerUp(PowerUpKind.Bomb, 1));
        }

        // --- AC4: a refusal credits nothing ---

        /// <summary>
        /// AC4 for a dismissal: the player backed out of the store prompt, so the wallet is untouched,
        /// nothing is announced, and — importantly — the validator is never even consulted. There is no
        /// receipt to validate, and asking would be a round trip for a transaction that does not exist.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_WhenTheStoreIsCancelled_ChangesNothing()
        {
            var profileModel = new ProfileModel();
            var validator = new StubPurchaseReceiptValidator(coinAmount: 500);
            CurrencySystem system = CreateSystem(
                profileModel, new StubCoinPurchaseService(CoinPurchaseOutcome.Cancelled), validator);

            bool credited = Purchase(system, KNOWN_SKU);

            Assert.IsFalse(credited);
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, _purchaseGrantBroker.Published.Count);
            Assert.AreEqual(0, validator.ValidateCount);
        }

        /// <summary>AC4 for a store failure. Same outcome as a dismissal from the wallet's point of
        /// view, which is exactly why the two are distinguished on the result rather than by what they
        /// do to the balance — see <see cref="CoinPurchaseOutcome"/>.</summary>
        [Test]
        public void PurchaseCoinBundleAsync_WhenTheStoreFails_ChangesNothing()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CoinPurchaseOutcome.Failed),
                new StubPurchaseReceiptValidator(coinAmount: 500));

            Assert.IsFalse(Purchase(system, KNOWN_SKU));
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, _purchaseGrantBroker.Published.Count);
        }

        /// <summary>An empty SKU cannot come from a storefront built out of the config, so it is refused
        /// without the store being asked at all.</summary>
        [TestCase("")]
        [TestCase(null)]
        public void PurchaseCoinBundleAsync_WithNoSku_NeverReachesTheStore(string sku)
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU));
            CurrencySystem system = CreateSystem(
                profileModel, store, new StubPurchaseReceiptValidator(coinAmount: 500));

            Assert.IsFalse(Purchase(system, sku));
            Assert.AreEqual(0, store.PurchaseCount);
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
        }

        // --- AC2: nothing is credited without validation ---

        /// <summary>
        /// AC2 in full: the store took the money and handed back a receipt, and it still buys nothing
        /// because the validator would not vouch for it. The one test that would fail if validation were
        /// ever reduced to a formality the credit path ignores.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_WithAnInvalidReceipt_CreditsNothing()
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU));
            CurrencySystem system = CreateSystem(
                profileModel, store, StubPurchaseReceiptValidator.Rejecting());

            bool credited = Purchase(system, KNOWN_SKU);

            Assert.IsFalse(credited);
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            Assert.AreEqual(0, _purchaseGrantBroker.Published.Count);
        }

        /// <summary>
        /// A receipt the validator approves for nothing is refused too. A zero-coin credit is not a
        /// purchase, and letting one through would mark a real transaction id as consumed while paying
        /// out nothing — burning it for good.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_WithAValidReceiptWorthNothing_CreditsNothingAndBurnsNoTransaction()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 0, isValid: true));

            Assert.IsFalse(Purchase(system, KNOWN_SKU));
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(string.Empty, PlayerPrefs.GetString(CONSUMED_TRANSACTION_IDS_KEY, string.Empty));
        }

        /// <summary>
        /// A receipt with no transaction id cannot be deduped, and a credit that cannot be deduped is one
        /// a replay would pay twice. Refused rather than credited on trust — and the validator is never
        /// consulted, because no answer it could give would make the receipt safe to bank.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_WithNoTransactionId_CreditsNothing()
        {
            var profileModel = new ProfileModel();
            var validator = new StubPurchaseReceiptValidator(coinAmount: 500);
            CurrencySystem system = CreateSystem(
                profileModel, new StubCoinPurchaseService(CreateReceipt(string.Empty, KNOWN_SKU)),
                validator);

            Assert.IsFalse(Purchase(system, KNOWN_SKU));
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, validator.ValidateCount);
        }

        // --- AC3, AC5: exactly once, ever ---

        /// <summary>
        /// AC5 in full: the very same transaction id, offered twice by a store that succeeds both times
        /// and a validator that approves both times, is credited exactly once. The second attempt is
        /// refused *before* validation — the validator's count proves it — because an already-honoured
        /// receipt has no question left to ask.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_ReplayingTheSameTransaction_CreditsOnlyOnce()
        {
            var profileModel = new ProfileModel();
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU));
            var validator = new StubPurchaseReceiptValidator(coinAmount: 800);
            CurrencySystem system = CreateSystem(profileModel, store, validator);

            Assert.IsTrue(Purchase(system, KNOWN_SKU));
            Assert.IsFalse(Purchase(system, KNOWN_SKU), "A replayed receipt must not credit again.");

            Assert.AreEqual(800, profileModel.CoinBalance.Value);
            Assert.AreEqual(800, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            Assert.AreEqual(1, _purchaseGrantBroker.Published.Count);
            Assert.AreEqual(1, validator.ValidateCount, "The duplicate check must precede validation.");
        }

        /// <summary>
        /// The store is acknowledged for the replay too. Confirming an already-honoured transaction is
        /// what makes it stop coming back — dropping it silently would leave the store replaying a
        /// purchase this install will never credit again, forever.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_AcknowledgesTheStoreOnBothTheCreditAndTheReplay()
        {
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU));
            CurrencySystem system = CreateSystem(
                new ProfileModel(), store, new StubPurchaseReceiptValidator(coinAmount: 800));

            Purchase(system, KNOWN_SKU);
            Purchase(system, KNOWN_SKU);

            Assert.AreEqual(2, store.ConfirmedTransactionIds.Count);
            Assert.AreEqual(TRANSACTION_ID, store.ConfirmedTransactionIds[0]);
            Assert.AreEqual(TRANSACTION_ID, store.ConfirmedTransactionIds[1]);
        }

        /// <summary>A refused purchase acknowledges nothing: there is no transaction to confirm, and
        /// telling the store otherwise would discard a replay the player may still be owed.</summary>
        [Test]
        public void PurchaseCoinBundleAsync_OnARefusal_AcknowledgesNothing()
        {
            var store = new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU));
            CurrencySystem system = CreateSystem(
                new ProfileModel(), store, StubPurchaseReceiptValidator.Rejecting());

            Purchase(system, KNOWN_SKU);

            Assert.AreEqual(0, store.ConfirmedTransactionIds.Count);
        }

        /// <summary>A different transaction for the same SKU is a different purchase and pays again:
        /// the dedupe is per transaction, not per product. A player who buys the same bundle twice gets
        /// paid twice.</summary>
        [Test]
        public void PurchaseCoinBundleAsync_WithADifferentTransactionForTheSameSku_CreditsAgain()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt("txn-first", KNOWN_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 300));

            Assert.IsTrue(Purchase(system, KNOWN_SKU));

            // A second store, a second transaction id, against the same already-loaded system.
            CurrencySystem second = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt("txn-second", KNOWN_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 300));

            Assert.IsTrue(Purchase(second, KNOWN_SKU));
            Assert.AreEqual(600, profileModel.CoinBalance.Value);
        }

        /// <summary>
        /// AC3's "even if the app is killed" half: the consumed-transaction set is persisted, so a fresh
        /// model-and-system pair — i.e. a relaunch — still refuses the receipt it already paid for. The
        /// second pair's validator would approve it, which is what makes this a test of the persisted
        /// set rather than of the in-memory one.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_ThenANewSystem_StillRefusesTheSameTransaction()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU)),
                new StubPurchaseReceiptValidator(coinAmount: 950));
            Assert.IsTrue(Purchase(system, KNOWN_SKU));

            var reloadedModel = new ProfileModel();
            var reloadedValidator = new StubPurchaseReceiptValidator(coinAmount: 950);
            CurrencySystem reloaded = CreateSystem(
                reloadedModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU)),
                reloadedValidator);

            Assert.AreEqual(950, reloadedModel.CoinBalance.Value, "The credited balance must survive.");
            Assert.IsFalse(Purchase(reloaded, KNOWN_SKU));
            Assert.AreEqual(950, reloadedModel.CoinBalance.Value);
            Assert.AreEqual(0, reloadedValidator.ValidateCount);
        }

        /// <summary>
        /// Several transactions survive together, and a later relaunch refuses all of them. The
        /// persisted set is a set, not a "last transaction" field — which a delimited string is easy to
        /// get wrong in exactly this way.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_SeveralTransactions_AllSurviveARestart()
        {
            var profileModel = new ProfileModel();
            CreditThrough(profileModel, "txn-a", 100);
            CreditThrough(profileModel, "txn-b", 200);
            CreditThrough(profileModel, "txn-c", 300);
            Assert.AreEqual(600, profileModel.CoinBalance.Value);

            var reloadedModel = new ProfileModel();
            Assert.AreEqual(600, ReplayAllOf(reloadedModel, "txn-a", "txn-b", "txn-c"));
        }

        // --- The shipped placeholder validator ---

        /// <summary>
        /// The stand-in validator prices a known SKU off <see cref="CoinBundleConfig"/>, so the whole
        /// purchase flow is playable before a validation backend exists — the purchase counterpart of
        /// DeterministicCoinRewardSource's contract.
        /// </summary>
        [Test]
        public void DeterministicPurchaseReceiptValidator_ForAKnownSku_ApprovesTheConfiguredAmount()
        {
            var validator = new DeterministicPurchaseReceiptValidator(_bundleConfig);
            _bundleConfig.TryGetBundle(KNOWN_SKU, out CoinBundle bundle);

            ValidatedPurchase verdict = validator
                .ValidateAsync(CreateReceipt(TRANSACTION_ID, KNOWN_SKU), CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(verdict.IsValid);
            Assert.AreEqual(bundle.CoinAmount, verdict.CoinAmount);
            Assert.Greater(verdict.CoinAmount, 0);
        }

        /// <summary>
        /// An unknown SKU is rejected rather than approved for zero coins. The two are not the same
        /// upstream: a zero-coin approval would let the credit path mark a real transaction as consumed
        /// while paying nothing, and a rejection leaves it unconsumed and therefore recoverable.
        /// </summary>
        [Test]
        public void DeterministicPurchaseReceiptValidator_ForAnUnknownSku_Rejects()
        {
            var validator = new DeterministicPurchaseReceiptValidator(_bundleConfig);

            ValidatedPurchase verdict = validator
                .ValidateAsync(CreateReceipt(TRANSACTION_ID, "coins_nonexistent"), CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(verdict.IsValid);
            Assert.AreEqual(0, verdict.CoinAmount);
        }

        /// <summary>
        /// End to end through the shipped placeholder validator rather than a stub: a purchase of a real
        /// configured SKU credits that SKU's authored coin amount. The one test that would fail if the
        /// validator and the config disagreed about what a bundle is worth.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_ThroughTheShippedValidator_CreditsTheConfiguredAmount()
        {
            var profileModel = new ProfileModel();
            _bundleConfig.TryGetBundle(KNOWN_SKU, out CoinBundle bundle);
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, KNOWN_SKU)),
                new DeterministicPurchaseReceiptValidator(_bundleConfig));

            Assert.IsTrue(Purchase(system, KNOWN_SKU));

            Assert.AreEqual(bundle.CoinAmount, profileModel.CoinBalance.Value);
            Assert.AreEqual(bundle.CoinAmount, _purchaseGrantBroker.Published[0].Amount);
        }

        /// <summary>
        /// And the refusal end to end: an unknown SKU that somehow reached a store willing to sell it is
        /// still refused, because the shipped validator will not price it.
        /// </summary>
        [Test]
        public void PurchaseCoinBundleAsync_ThroughTheShippedValidatorForAnUnknownSku_CreditsNothing()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(TRANSACTION_ID, "coins_nonexistent")),
                new DeterministicPurchaseReceiptValidator(_bundleConfig));

            Assert.IsFalse(Purchase(system, "coins_nonexistent"));
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
        }

        /// <summary>Banks <paramref name="coinAmount"/> through a real purchase of
        /// <paramref name="transactionId"/>, asserted rather than assumed so a later failure cannot be a
        /// setup that quietly credited nothing.</summary>
        private void CreditThrough(ProfileModel profileModel, string transactionId, int coinAmount)
        {
            CurrencySystem system = CreateSystem(
                profileModel,
                new StubCoinPurchaseService(CreateReceipt(transactionId, KNOWN_SKU)),
                new StubPurchaseReceiptValidator(coinAmount));

            Assert.IsTrue(Purchase(system, KNOWN_SKU), $"Test setup failed to bank {transactionId}.");
        }

        /// <summary>
        /// Loads a fresh system over the same PlayerPrefs and replays every named transaction against
        /// it, each through a validator that would approve. Returns the balance left behind, which must
        /// be the one the restart loaded.
        /// </summary>
        private int ReplayAllOf(ProfileModel reloadedModel, params string[] transactionIds)
        {
            for (int idIndex = 0; idIndex < transactionIds.Length; idIndex++)
            {
                CurrencySystem reloaded = CreateSystem(
                    reloadedModel,
                    new StubCoinPurchaseService(CreateReceipt(transactionIds[idIndex], KNOWN_SKU)),
                    new StubPurchaseReceiptValidator(coinAmount: 5000));

                Assert.IsFalse(
                    Purchase(reloaded, KNOWN_SKU),
                    $"{transactionIds[idIndex]} was credited a second time after a restart.");
            }

            return reloadedModel.CoinBalance.Value;
        }

        private static PurchaseReceipt CreateReceipt(string transactionId, string sku)
            => new PurchaseReceipt(transactionId, sku, "{\"stub\":true}");

        private static bool Purchase(CurrencySystem system, string sku)
            => system.PurchaseCoinBundleAsync(sku, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// A system wired to a scripted store and validator. The <see cref="PowerUpSystem"/> it takes is
        /// real but otherwise untouched by these tests — the purchase path never grants a power-up —
        /// and the score model defaults to an empty run, since a purchase has nothing to do with one.
        /// </summary>
        private CurrencySystem CreateSystem(
            ProfileModel profileModel,
            ICoinPurchaseService coinPurchaseService,
            IPurchaseReceiptValidator receiptValidator,
            ScoreModel scoreModel = null)
        {
            return new CurrencySystem(
                profileModel,
                scoreModel ?? new ScoreModel(),
                _levelProgressionModel,
                _config,
                _priceConfig,
                // No campaign: these tests are about the real-money path, which has no promotional
                // price of its own — a coin bundle's price lives in the store, not in this codebase.
                ScriptableObject.CreateInstance<PromotionConfig>(),
                CreatePowerUpSystem(),
                new StubCoinRewardSource(),
                coinPurchaseService,
                receiptValidator,
                _convertedBroker,
                _adGrantBroker,
                _purchaseGrantBroker,
                _gameOverBroker,
                _coinCellsBroker);
        }

        /// <summary>
        /// A real <see cref="PowerUpSystem"/> over fresh, unstarted board, tray and clock models. The
        /// purchase path reads nothing from any of them, so a stub of each would only be a second
        /// description of "does nothing" — but the one test that spends the purchased coins on a power-up
        /// needs the real grant behind it.
        /// </summary>
        private PowerUpSystem CreatePowerUpSystem()
        {
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            BoardSystem boardSystem = CreateBoardSystem(boardModel, trayModel);
            var ghostFitModel = new GhostFitModel();
            var appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();

            return new PowerUpSystem(
                new PowerUpModel(),
                _levelProgressionModel,
                boardModel,
                trayModel,
                boardSystem,
                CreateTimerRunSystem(boardSystem),
                CreateDoubleMultiplierSystem(),
                new GhostFitSystem(
                    ghostFitModel,
                    boardModel,
                    trayModel,
                    new ScoreModel(),
                    new TestMessageBroker<RunStartedMessage>(),
                    new TestMessageBroker<GameOverMessage>(),
                    new TestMessageBroker<PiecePlacedMessage>(),
                    appliedBroker),
                ghostFitModel,
                new StubRewardSource(),
                appliedBroker,
                new TestMessageBroker<PowerUpGrantedMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                _config,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        private static BoardSystem CreateBoardSystem(BoardModel boardModel, TrayModel trayModel)
        {
            return new BoardSystem(
                boardModel,
                trayModel,
                new PerfectRoundModel(),
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>());
        }

        private static TimerRunSystem CreateTimerRunSystem(BoardSystem boardSystem)
        {
            TimedModeConfig timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            return new TimerRunSystem(
                new TimerModel(),
                new RunPauseModel(),
                new GameModeSystem(new GameModeModel(), boardSystem),
                new TimedModeSystem(new TimedModeModel(), timedModeConfig),
                boardSystem,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        private static DoubleMultiplierSystem CreateDoubleMultiplierSystem()
        {
            return new DoubleMultiplierSystem(
                new DoubleMultiplierModel(),
                new RunPauseModel(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        private static void DestroyIfPresent(ScriptableObject asset)
        {
            if (asset != null)
            {
                Object.DestroyImmediate(asset);
            }
        }

        private static void DeleteCurrencyKeys()
        {
            PlayerPrefs.DeleteKey(COIN_BALANCE_KEY);
            PlayerPrefs.DeleteKey(TOTAL_SCORE_EARNED_KEY);
            PlayerPrefs.DeleteKey(SCORE_CONVERTED_KEY);
            PlayerPrefs.DeleteKey(CONSUMED_TRANSACTION_IDS_KEY);
        }

        /// <summary>Ad seam stand-in. Always grants, and never reached: no test here watches an
        /// ad.</summary>
        private sealed class StubCoinRewardSource : ICoinRewardSource
        {
            public UniTask<CoinRewardResult> RequestCoinRewardAsync(
                int amount, CancellationToken cancellationToken)
                => UniTask.FromResult(new CoinRewardResult(amount, granted: true));
        }

        /// <summary>The power-up ad seam's stand-in, for the <see cref="PowerUpSystem"/> above. Never
        /// reached either — the purchase path grants no power-ups.</summary>
        private sealed class StubRewardSource : IRewardSource
        {
            public UniTask<RewardResult> RequestRewardAsync(
                PowerUpKind kind, CancellationToken cancellationToken)
                => UniTask.FromResult(new RewardResult(kind, granted: true));
        }
    }
}
