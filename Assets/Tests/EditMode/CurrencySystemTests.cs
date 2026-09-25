using System;
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
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the currency contract: a run's score is banked but never minted, conversion is partial and
    /// leaves the remainder alone, an empty pool is a complete no-op, the ad faucet is independent of the
    /// pool entirely, a coin purchase is all-or-nothing and can never buy past a level gate, and every
    /// change survives a restart.
    /// </summary>
    public class CurrencySystemTests
    {
        private const string COIN_BALANCE_KEY = "Profile.CoinBalance";
        private const string TOTAL_SCORE_EARNED_KEY = "Profile.TotalScoreEarned";
        private const string SCORE_CONVERTED_KEY = "Profile.ScoreConverted";
        private const string DAILY_AD_GRANTS_REMAINING_KEY = "Profile.DailyAdGrantsRemaining";
        private const string DAILY_AD_GRANT_DAY_MARKER_KEY = "Profile.DailyAdGrantDayMarker";

        /// <summary>The lives save blob (issue #479's pack grants into it). Backed up and restored rather
        /// than deleted, so running the suite never costs the device's real lives.</summary>
        private const string LIVES_SAVE_KEY = "Lives.State";

        /// <summary>The lives pack tests' local clock: fixed, so no hour boundary can land mid-test and
        /// blur a pack's +10 with a refill's +5.</summary>
        private static readonly DateTime LivesNow = new DateTime(2026, 9, 25, 10, 40, 0);

        /// <summary>A frontier past the last gate in <see cref="PowerUpUnlockLevels"/>, so no kind is
        /// withheld from the purchase tests that are not about the gate.</summary>
        private const int ALL_KINDS_UNLOCKED_LEVEL = 99;

        /// <summary>
        /// The instant every test starts at. A fixed one rather than the machine's clock, so a
        /// campaign's window can be authored relative to something that does not move: the tests below
        /// place windows around this date, and "before the start" and "after the end" mean the same thing
        /// whenever the suite is run.
        /// </summary>
        private static readonly DateTime DefaultNowUtc =
            new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        private TestMessageBroker<ScoreConvertedToCoinsMessage> _convertedBroker;
        private TestMessageBroker<CoinsGrantedFromAdMessage> _adGrantBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<CoinCellsClearedMessage> _coinCellsBroker;
        private TestMessageBroker<PowerUpAppliedMessage> _appliedBroker;
        private TestMessageBroker<PowerUpGrantedMessage> _grantedBroker;
        private CurrencyConfig _config;
        private PowerUpPriceConfig _priceConfig;

        /// <summary>
        /// The promotion table every system in this fixture quotes through. Empty by default, so every
        /// test written before campaigns existed still asks for a standard price; the promotion tests
        /// fill it themselves through <c>AddCampaignForTests</c>.
        /// </summary>
        private PromotionConfig _promotionConfig;

        /// <summary>
        /// The clock the promotion windows are compared against, injected rather than read off the
        /// machine: a date-driven price tested against <see cref="DateTime.UtcNow"/> would pass today and
        /// fail on the day a window happened to close. Held on a field so a test can move "now" either
        /// side of a campaign it authored.
        /// </summary>
        private DateTime _utcNow;

        /// <summary>
        /// The progression frontier the purchase path's level gate reads. Held on a field and opened at
        /// <see cref="ALL_KINDS_UNLOCKED_LEVEL"/> by default, so every test that is not about the gate
        /// can buy anything; the gating tests lower it themselves.
        /// </summary>
        private LevelProgressionModel _levelProgressionModel;

        /// <summary>
        /// The inventory the purchase path grants into, and the system that owns it. Both are rebuilt by
        /// every <c>CreateSystem</c> call and held here so a test can read the granted count without
        /// reaching back through the system under test — and so a "restart" test's second pair reads the
        /// counts back out of PlayerPrefs exactly as a relaunch would.
        /// </summary>
        private PowerUpModel _powerUpModel;
        private PowerUpSystem _powerUpSystem;

        /// <summary>The lives pack's config and the model its grant lands in (issue #479). Only the pack
        /// tests build a <see cref="LivesSystem"/> over them; every other test's system has none.</summary>
        private LivesConfig _livesConfig;
        private LivesModel _livesModel;
        private bool _hadLivesSave;
        private string _livesSave;

        /// <summary>CurrencySystem loads the three counters in its constructor, and PowerUpSystem loads
        /// the inventory in its own, so anything left behind by a previous test would silently decide
        /// what the next one can convert or buy.</summary>
        [SetUp]
        public void ClearPersistedCurrency()
        {
            DeleteCurrencyKeys();
            DeleteInventoryKeys();
            _convertedBroker = new TestMessageBroker<ScoreConvertedToCoinsMessage>();
            _adGrantBroker = new TestMessageBroker<CoinsGrantedFromAdMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _coinCellsBroker = new TestMessageBroker<CoinCellsClearedMessage>();
            _appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _grantedBroker = new TestMessageBroker<PowerUpGrantedMessage>();
            _config = ScriptableObject.CreateInstance<CurrencyConfig>();
            _priceConfig = ScriptableObject.CreateInstance<PowerUpPriceConfig>();
            _promotionConfig = ScriptableObject.CreateInstance<PromotionConfig>();
            _utcNow = DefaultNowUtc;
            _levelProgressionModel = new LevelProgressionModel();
            _levelProgressionModel.CurrentLevelNumber.Value = ALL_KINDS_UNLOCKED_LEVEL;

            _hadLivesSave = PlayerPrefs.HasKey(LIVES_SAVE_KEY);
            _livesSave = _hadLivesSave ? PlayerPrefs.GetString(LIVES_SAVE_KEY) : null;
            PlayerPrefs.DeleteKey(LIVES_SAVE_KEY);
            _livesConfig = ScriptableObject.CreateInstance<LivesConfig>();
            _livesModel = new LivesModel();
        }

        [TearDown]
        public void ClearPersistedCurrencyAfterwards()
        {
            DeleteCurrencyKeys();
            DeleteInventoryKeys();
            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
            }

            if (_priceConfig != null)
            {
                UnityEngine.Object.DestroyImmediate(_priceConfig);
            }

            if (_promotionConfig != null)
            {
                UnityEngine.Object.DestroyImmediate(_promotionConfig);
            }

            if (_livesConfig != null)
            {
                UnityEngine.Object.DestroyImmediate(_livesConfig);
            }

            if (_hadLivesSave)
            {
                PlayerPrefs.SetString(LIVES_SAVE_KEY, _livesSave);
            }
            else
            {
                PlayerPrefs.DeleteKey(LIVES_SAVE_KEY);
            }
        }

        /// <summary>
        /// Pins the placeholder rate the rest of these tests quote against. Not a claim about the
        /// economy — the number is expected to be retuned in the asset — only that the shipped default
        /// is a sane, non-zero one, so a fresh scene converts something rather than nothing.
        /// </summary>
        [Test]
        public void ScoreToCoinRate_OnAFreshConfig_IsTheDocumentedPlaceholder()
        {
            Assert.AreEqual(0.1f, _config.ScoreToCoinRate, 0.0001f);
            Assert.Greater(_config.AdRewardCoins, 0);
        }

        // --- AC2, AC9: the run's score is banked, never minted ---

        /// <summary>
        /// The pool grows by exactly the run's score and the balance does not move at all. This is the
        /// whole of AC9 at the one moment it could plausibly be broken: a game over is the obvious place
        /// to quietly pay the player, and it must not.
        /// </summary>
        [Test]
        public void OnGameOver_BanksTheRunsScoreIntoThePool_AndMintsNoCoins()
        {
            var profileModel = new ProfileModel();
            var scoreModel = new ScoreModel();
            scoreModel.Score.Value = 250;
            CurrencySystem system = CreateSystem(profileModel, scoreModel);

            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));

            Assert.AreEqual(250, profileModel.TotalScoreEarned.Value);
            Assert.AreEqual(250, system.AvailableToConvert);
            Assert.AreEqual(0, profileModel.CoinBalance.Value, "A game over must never mint a coin.");
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
            Assert.AreEqual(0, _convertedBroker.Published.Count);
            Assert.AreEqual(0, _adGrantBroker.Published.Count);
        }

        /// <summary>The run's own score is left exactly as it was: the end-of-run card and the
        /// leaderboard still see the full figure, because banking is an addition elsewhere.</summary>
        [Test]
        public void OnGameOver_LeavesTheRunsScoreUntouched()
        {
            var scoreModel = new ScoreModel();
            scoreModel.Score.Value = 400;
            CurrencySystem unused = CreateSystem(new ProfileModel(), scoreModel);

            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.TimeUp));

            Assert.AreEqual(400, scoreModel.Score.Value);
        }

        /// <summary>Two runs add up: the pool is a lifetime figure, not the last run's.</summary>
        [Test]
        public void OnGameOver_TwiceOver_AccumulatesBothRuns()
        {
            var profileModel = new ProfileModel();
            var scoreModel = new ScoreModel();
            CurrencySystem system = CreateSystem(profileModel, scoreModel);

            scoreModel.Score.Value = 120;
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));

            scoreModel.Score.Value = 80;
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));

            Assert.AreEqual(200, profileModel.TotalScoreEarned.Value);
            Assert.AreEqual(200, system.AvailableToConvert);
        }

        /// <summary>
        /// Issue #370: a run can end twice — a rescue-available ending taken back, then a real one —
        /// and the score at the first ending must not be banked again at the second. Only what the run
        /// scored in between is added, so a rescue is paid for with an ad and never with coins.
        /// </summary>
        [Test]
        public void OnGameOver_AfterARescuedEnding_BanksOnlyWhatTheRunScoredSince()
        {
            var profileModel = new ProfileModel();
            var scoreModel = new ScoreModel();
            var runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            CurrencySystem system = CreateSystem(profileModel, scoreModel, runStartedBroker: runStartedBroker);

            runStartedBroker.Publish(new RunStartedMessage());
            scoreModel.Score.Value = 300;
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: true));
            scoreModel.Score.Value = 450;
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: true));

            Assert.AreEqual(450, profileModel.TotalScoreEarned.Value, "300 at the first ending, then the 150 since.");
            Assert.AreEqual(450, system.AvailableToConvert);
        }

        /// <summary>The per-run counter starts over on a new run, even one that happens to end on a
        /// score no higher than the last ending's.</summary>
        [Test]
        public void OnGameOver_InTheNextRun_BanksFromZeroAgain()
        {
            var profileModel = new ProfileModel();
            var scoreModel = new ScoreModel();
            var runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            CurrencySystem unused = CreateSystem(profileModel, scoreModel, runStartedBroker: runStartedBroker);

            runStartedBroker.Publish(new RunStartedMessage());
            scoreModel.Score.Value = 100;
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: true));

            runStartedBroker.Publish(new RunStartedMessage());
            scoreModel.Score.Value = 0;
            scoreModel.Score.Value = 100;
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));

            Assert.AreEqual(200, profileModel.TotalScoreEarned.Value);
        }

        // --- AC4, AC5: partial conversion ---

        /// <summary>
        /// AC4 in full: converting 60 of 100 credits the 60's worth of coins and leaves 40 convertible —
        /// still there, still the player's, still convertible later.
        /// </summary>
        [Test]
        public void ConvertScoreToCoins_WithAPartialAmount_CreditsThatMuchAndLeavesTheRemainder()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 100);

            system.ConvertScoreToCoins(60);

            Assert.AreEqual(Mathf.FloorToInt(60 * _config.ScoreToCoinRate), profileModel.CoinBalance.Value);
            Assert.AreEqual(60, profileModel.ScoreConverted.Value);
            Assert.AreEqual(40, system.AvailableToConvert);

            // The pool itself never shrinks — only the converted counter grows against it.
            Assert.AreEqual(100, profileModel.TotalScoreEarned.Value);
        }

        /// <summary>The remainder is convertible for real, not just reported as such.</summary>
        [Test]
        public void ConvertScoreToCoins_TwiceOver_DrainsThePoolInSteps()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 100);

            system.ConvertScoreToCoins(60);
            system.ConvertScoreToCoins(40);

            Assert.AreEqual(Mathf.FloorToInt(100 * _config.ScoreToCoinRate), profileModel.CoinBalance.Value);
            Assert.AreEqual(100, profileModel.ScoreConverted.Value);
            Assert.AreEqual(0, system.AvailableToConvert);
            Assert.AreEqual(2, _convertedBroker.Published.Count);
        }

        /// <summary>AC5: the message carries both sides of the trade and both totals it left behind, so
        /// a consumer needs no second read.</summary>
        [Test]
        public void ConvertScoreToCoins_PublishesTheWholeOutcome()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 500);

            system.ConvertScoreToCoins(300);

            Assert.AreEqual(1, _convertedBroker.Published.Count);
            ScoreConvertedToCoinsMessage message = _convertedBroker.Published[0];
            Assert.AreEqual(300, message.AmountConverted);
            Assert.AreEqual(Mathf.FloorToInt(300 * _config.ScoreToCoinRate), message.CoinsGranted);
            Assert.AreEqual(profileModel.CoinBalance.Value, message.NewCoinBalance);
            Assert.AreEqual(200, message.NewAvailableToConvert);
            Assert.AreEqual(system.AvailableToConvert, message.NewAvailableToConvert);
        }

        /// <summary>
        /// Rounds down: a rate that does not divide the amount evenly must not mint a coin the score did
        /// not pay for. The points that bought no coin are still deducted, which is what stops a player
        /// converting nine at a time to beat the rate.
        /// </summary>
        [Test]
        public void ConvertScoreToCoins_WithAnAmountTheRateDoesNotDivide_RoundsTheCoinsDown()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 100);

            // 9 points at 0.1 coins per point is 0.9 of a coin — which is none of one.
            system.ConvertScoreToCoins(9);

            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(9, profileModel.ScoreConverted.Value);
            Assert.AreEqual(91, system.AvailableToConvert);
            Assert.AreEqual(1, _convertedBroker.Published.Count);
            Assert.AreEqual(0, _convertedBroker.Published[0].CoinsGranted);
        }

        // --- AC8 and the other refusals ---

        /// <summary>AC8: nothing available means nothing happens — no coins, no counter, no message.</summary>
        [Test]
        public void ConvertScoreToCoins_WithAnEmptyPool_ChangesNothingAndPublishesNothing()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());

            system.ConvertScoreToCoins(50);

            Assert.AreEqual(0, system.AvailableToConvert);
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
            Assert.AreEqual(0, _convertedBroker.Published.Count);
        }

        /// <summary>The same refusal after the pool has been fully converted: an exhausted pool is as
        /// empty as one that was never filled.</summary>
        [Test]
        public void ConvertScoreToCoins_AfterTheWholePoolIsConverted_ChangesNothing()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 100);
            system.ConvertScoreToCoins(100);
            int balanceAfterFirst = profileModel.CoinBalance.Value;

            system.ConvertScoreToCoins(100);

            Assert.AreEqual(balanceAfterFirst, profileModel.CoinBalance.Value);
            Assert.AreEqual(100, profileModel.ScoreConverted.Value);
            Assert.AreEqual(1, _convertedBroker.Published.Count);
        }

        /// <summary>Asking for more than there is is rejected outright rather than silently clamped: the
        /// screen is built from AvailableToConvert and can never legitimately offer more, so an over-ask
        /// is a bug that must not be papered over.</summary>
        [Test]
        public void ConvertScoreToCoins_AskingForMoreThanIsAvailable_IsRefusedRatherThanClamped()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 100);

            system.ConvertScoreToCoins(101);

            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
            Assert.AreEqual(100, system.AvailableToConvert);
            Assert.AreEqual(0, _convertedBroker.Published.Count);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(-500)]
        public void ConvertScoreToCoins_WithANonPositiveAmount_ChangesNothing(int amount)
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 100);

            system.ConvertScoreToCoins(amount);

            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
            Assert.AreEqual(100, system.AvailableToConvert);
            Assert.AreEqual(0, _convertedBroker.Published.Count);
        }

        // --- AC6: the ad faucet, independent of the pool ---

        /// <summary>
        /// AC6: an ad grant credits coins, announces itself on its own channel, and takes nothing out of
        /// the convertible pool. The player sold no score here — they watched an ad.
        /// </summary>
        [Test]
        public void GrantCoinsFromAdAsync_WhenTheSourceGrants_CreditsCoinsAndLeavesThePoolAlone()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(
                profileModel, pool: 100, coinRewardSource: new StubCoinRewardSource(granted: true));

            bool granted = system.GrantCoinsFromAdAsync(25, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(granted);
            Assert.AreEqual(25, profileModel.CoinBalance.Value);
            Assert.AreEqual(25, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));

            Assert.AreEqual(1, _adGrantBroker.Published.Count);
            Assert.AreEqual(25, _adGrantBroker.Published[0].Amount);
            Assert.AreEqual(25, _adGrantBroker.Published[0].NewCoinBalance);

            // The conversion flow is untouched: same pool, same converted counter, no conversion message.
            Assert.AreEqual(100, system.AvailableToConvert);
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
            Assert.AreEqual(100, profileModel.TotalScoreEarned.Value);
            Assert.AreEqual(0, _convertedBroker.Published.Count);
        }

        [Test]
        public void GrantCoinsFromAdAsync_WhenTheSourceRefuses_ChangesNothing()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(
                profileModel, pool: 100, coinRewardSource: new StubCoinRewardSource(granted: false));

            bool granted = system.GrantCoinsFromAdAsync(25, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(granted);
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, _adGrantBroker.Published.Count);
        }

        [TestCase(0)]
        [TestCase(-10)]
        public void GrantCoinsFromAdAsync_WithANonPositiveAmount_NeverReachesTheSource(int amount)
        {
            var profileModel = new ProfileModel();
            var source = new StubCoinRewardSource(granted: true);
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 100, coinRewardSource: source);

            bool granted = system.GrantCoinsFromAdAsync(amount, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(granted);
            Assert.AreEqual(0, source.RequestCount);
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, _adGrantBroker.Published.Count);
        }

        /// <summary>The shipped stub grants in full, so the whole coin flow is playable before an ad SDK
        /// exists — the coin counterpart of DeterministicRewardSource's contract.</summary>
        [Test]
        public void DeterministicCoinRewardSource_GrantsTheFullAmountImmediately()
        {
            var source = new DeterministicCoinRewardSource();

            CoinRewardResult result = source.RequestCoinRewardAsync(40, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(result.Granted);
            Assert.AreEqual(40, result.Amount);
        }

        // --- Issue #257: the daily coin-ad cap ---

        /// <summary>Pins the placeholder cap the rest of these tests quote against, exactly as
        /// <see cref="ScoreToCoinRate_OnAFreshConfig_IsTheDocumentedPlaceholder"/> pins the rate.</summary>
        [Test]
        public void DailyAdRewardCap_OnAFreshConfig_IsTheDocumentedPlaceholder()
        {
            Assert.AreEqual(3, _config.DailyAdRewardCap);
        }

        /// <summary>A freshly built system has spent nothing today, so the full cap is available before
        /// any grant has happened.</summary>
        [Test]
        public void RemainingAdGrantsToday_OnAFreshSystem_IsTheFullCap()
        {
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(_config.DailyAdRewardCap, system.RemainingAdGrantsToday.Value);
        }

        /// <summary>AC5's counting half: each successful grant spends exactly one of today's
        /// allowance.</summary>
        [Test]
        public void GrantCoinsFromAdAsync_OnASuccessfulGrant_DecrementsRemainingByOne()
        {
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            bool granted = system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(granted);
            Assert.AreEqual(_config.DailyAdRewardCap - 1, system.RemainingAdGrantsToday.Value);
        }

        /// <summary>
        /// AC6 and AC9 together: the (cap + 1)-th request of the day is refused outright — no coins
        /// credited, no balance change, no message published, and (AC6 in full) the ad source itself is
        /// never asked, not asked-and-ignored. <see cref="StubCoinRewardSource.RequestCount"/> is the
        /// proof of the second half; a granted-then-discarded request would still have moved it.
        /// </summary>
        [Test]
        public void GrantCoinsFromAdAsync_PastTheDailyCap_RefusesWithoutReachingTheSourceOrTheBalance()
        {
            var profileModel = new ProfileModel();
            var source = new StubCoinRewardSource(granted: true);
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel(), source);

            for (int grantIndex = 0; grantIndex < _config.DailyAdRewardCap; grantIndex++)
            {
                Assert.IsTrue(
                    system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult());
            }

            int balanceAfterCap = profileModel.CoinBalance.Value;
            int requestsAfterCap = source.RequestCount;
            int messagesAfterCap = _adGrantBroker.Published.Count;

            bool grantedPastCap = system.GrantCoinsFromAdAsync(10, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(grantedPastCap);
            Assert.AreEqual(0, system.RemainingAdGrantsToday.Value);
            Assert.AreEqual(
                balanceAfterCap, profileModel.CoinBalance.Value, "The (N+1)th request must credit nothing.");
            Assert.AreEqual(
                requestsAfterCap, source.RequestCount,
                "The (N+1)th request must never reach ICoinRewardSource at all.");
            Assert.AreEqual(
                messagesAfterCap, _adGrantBroker.Published.Count,
                "The (N+1)th request must publish no CoinsGrantedFromAdMessage.");
        }

        /// <summary>
        /// AC4 without a restart: the stored day marker is left behind by the clock moving on, and the
        /// very next read — not a fresh system, not a relaunch — reports the full cap again. This is the
        /// "reopen the tab after midnight" scenario the acceptance criterion names explicitly.
        /// </summary>
        [Test]
        public void RemainingAdGrantsToday_WhenTheStoredDayHasPassed_ResetsToTheFullCapOnTheNextRead()
        {
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());
            for (int grantIndex = 0; grantIndex < _config.DailyAdRewardCap; grantIndex++)
            {
                system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult();
            }

            Assert.AreEqual(0, system.RemainingAdGrantsToday.Value, "Test setup: today's cap must be spent.");

            // The day turns over while the system stays exactly as it is — no new CurrencySystem, no
            // reload from PlayerPrefs. Only the clock this fixture controls moves.
            _utcNow = _utcNow.AddDays(1);

            Assert.AreEqual(
                _config.DailyAdRewardCap, system.RemainingAdGrantsToday.Value,
                "A day past the stored marker must read as a fresh cap without an app restart.");
        }

        /// <summary>The rollover a read triggers is not a one-off: a grant made on the new day is counted
        /// against the fresh cap, not against whatever was left of the old one.</summary>
        [Test]
        public void GrantCoinsFromAdAsync_OnTheDayAfterTheCapWasSpent_GrantsAgainstTheFreshCap()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());
            for (int grantIndex = 0; grantIndex < _config.DailyAdRewardCap; grantIndex++)
            {
                system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult();
            }

            _utcNow = _utcNow.AddDays(1);

            bool granted = system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(granted, "A grant on the new day must not still be refused by yesterday's cap.");
            Assert.AreEqual(_config.DailyAdRewardCap - 1, system.RemainingAdGrantsToday.Value);
        }

        /// <summary>
        /// AC11's persistence scenario: the counter and the day marker both survive a restart, read back
        /// by a fresh <see cref="ProfileModel"/>-and-<see cref="CurrencySystem"/> pair from the very
        /// PlayerPrefs keys the first system wrote — the daily-cap counterpart of
        /// <see cref="ConvertScoreToCoins_ThenANewSystem_LoadsTheBalanceAndTheConvertedCounter"/>.
        /// </summary>
        [Test]
        public void GrantCoinsFromAdAsync_ThenANewSystem_LoadsTheRemainingCountAndTheDayMarker()
        {
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());
            system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult();
            system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult();
            int expectedRemaining = system.RemainingAdGrantsToday.Value;
            Assert.AreEqual(_config.DailyAdRewardCap - 2, expectedRemaining, "Test setup: two grants spent.");

            string expectedDayMarker = _utcNow.ToString("yyyyMMdd");
            Assert.AreEqual(expectedRemaining, PlayerPrefs.GetInt(DAILY_AD_GRANTS_REMAINING_KEY, -1));
            Assert.AreEqual(expectedDayMarker, PlayerPrefs.GetString(DAILY_AD_GRANT_DAY_MARKER_KEY, string.Empty));

            // A fresh Model and a fresh System, reading the same keys — i.e. a relaunch on the same day.
            CurrencySystem reloaded = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(
                expectedRemaining, reloaded.RemainingAdGrantsToday.Value,
                "A relaunch on the same day must keep the spent allowance rather than granting a fresh cap.");
        }

        /// <summary>
        /// AC5 in full: the counter and the day marker reach the disk in the very same
        /// <see cref="PlayerPrefs.Save"/> call as the coin credit, not a second one — proven the same way
        /// the conversion and purchase paths already are elsewhere in this fixture, by reading every key
        /// back off PlayerPrefs directly rather than off the in-memory model.
        /// </summary>
        [Test]
        public void GrantCoinsFromAdAsync_PersistsTheCounterAndTheBalanceTogether()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());

            system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(10, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            Assert.AreEqual(_config.DailyAdRewardCap - 1, PlayerPrefs.GetInt(DAILY_AD_GRANTS_REMAINING_KEY, -1));
            Assert.AreEqual(
                _utcNow.ToString("yyyyMMdd"), PlayerPrefs.GetString(DAILY_AD_GRANT_DAY_MARKER_KEY, string.Empty));
        }

        /// <summary>
        /// AC10, the negative case the issue calls out by name: exhausting the coin-ad cap must not touch
        /// <see cref="PowerUpSystem"/>'s own earn-by-ad grants — a structurally separate faucet behind
        /// <see cref="IRewardSource"/>, gated by nothing this class owns.
        /// </summary>
        [Test]
        public void GrantCoinsFromAdAsync_AfterTheDailyCapIsExhausted_LeavesPowerUpEarnByAdUntouched()
        {
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());
            for (int grantIndex = 0; grantIndex < _config.DailyAdRewardCap; grantIndex++)
            {
                system.GrantCoinsFromAdAsync(10, CancellationToken.None).GetAwaiter().GetResult();
            }

            Assert.AreEqual(0, system.RemainingAdGrantsToday.Value, "Test setup: the coin-ad cap must be spent.");

            bool granted = _powerUpSystem.GrantRewardAsync(PowerUpKind.Bomb, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(granted, "The power-up earn-by-ad path must be independent of the coin-ad cap.");
            Assert.AreEqual(1, CountOf(PowerUpKind.Bomb));
        }

        // --- AC7: persistence ---

        /// <summary>AC7: a converted balance and the counter behind it are both loaded by the next
        /// launch. Same prefs, fresh objects — i.e. a restart.</summary>
        [Test]
        public void ConvertScoreToCoins_ThenANewSystem_LoadsTheBalanceAndTheConvertedCounter()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 500);
            system.ConvertScoreToCoins(300);
            int expectedBalance = profileModel.CoinBalance.Value;

            var reloadedModel = new ProfileModel();
            CurrencySystem reloaded = CreateSystem(reloadedModel, new ScoreModel());

            Assert.AreEqual(expectedBalance, reloadedModel.CoinBalance.Value);
            Assert.AreEqual(300, reloadedModel.ScoreConverted.Value);
            Assert.AreEqual(500, reloadedModel.TotalScoreEarned.Value);
            Assert.AreEqual(200, reloaded.AvailableToConvert, "The remainder must still be convertible.");
        }

        /// <summary>The other half of AC7: a banked run survives a restart too, so a player who closes
        /// the app at the game-over screen still finds the score waiting to be converted.</summary>
        [Test]
        public void OnGameOver_ThenANewSystem_LoadsTheBankedPool()
        {
            var scoreModel = new ScoreModel();
            scoreModel.Score.Value = 175;
            CurrencySystem unused = CreateSystem(new ProfileModel(), scoreModel);
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));

            var reloadedModel = new ProfileModel();
            CurrencySystem reloaded = CreateSystem(reloadedModel, new ScoreModel());

            Assert.AreEqual(175, reloadedModel.TotalScoreEarned.Value);
            Assert.AreEqual(175, reloaded.AvailableToConvert);
        }

        /// <summary>Ad-granted coins go through the same bookkeeping as converted ones, so they survive
        /// a restart the same way.</summary>
        [Test]
        public void GrantCoinsFromAdAsync_ThenANewSystem_LoadsTheGrantedBalance()
        {
            CurrencySystem system = CreateSystem(
                new ProfileModel(), new ScoreModel(), new StubCoinRewardSource(granted: true));
            system.GrantCoinsFromAdAsync(30, CancellationToken.None).GetAwaiter().GetResult();

            var reloadedModel = new ProfileModel();
            CurrencySystem unused = CreateSystem(reloadedModel, new ScoreModel());

            Assert.AreEqual(30, reloadedModel.CoinBalance.Value);
        }

        /// <summary>A hand-edited or partially restored save must boot into a poor player, never an
        /// indebted one — which is also what keeps AvailableToConvert from reading as a debt.</summary>
        [Test]
        public void Load_WithNegativePersistedCounters_FloorsThemAtZero()
        {
            PlayerPrefs.SetInt(COIN_BALANCE_KEY, -50);
            PlayerPrefs.SetInt(TOTAL_SCORE_EARNED_KEY, 100);
            PlayerPrefs.SetInt(SCORE_CONVERTED_KEY, -10);

            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());

            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
            Assert.AreEqual(100, system.AvailableToConvert);
        }

        /// <summary>A pool smaller than what has already been converted cannot happen through this
        /// system, but a broken save can present one — and it must read as "nothing left", not a debt.</summary>
        [Test]
        public void AvailableToConvert_WithMoreConvertedThanEarned_IsZeroRatherThanNegative()
        {
            PlayerPrefs.SetInt(TOTAL_SCORE_EARNED_KEY, 100);
            PlayerPrefs.SetInt(SCORE_CONVERTED_KEY, 400);

            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(0, system.AvailableToConvert);
        }

        // --- Quoting ---

        /// <summary>The screen quotes through the System rather than doing the arithmetic itself, so the
        /// figure offered and the figure granted can never be two different roundings.</summary>
        [Test]
        public void QuoteCoinsFor_MatchesWhatAConversionActuallyGrants()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 1000);
            int quoted = system.QuoteCoinsFor(777);

            system.ConvertScoreToCoins(777);

            Assert.AreEqual(quoted, profileModel.CoinBalance.Value);
        }

        [TestCase(0)]
        [TestCase(-100)]
        public void QuoteCoinsFor_WithANonPositiveAmount_IsZero(int amount)
        {
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(0, system.QuoteCoinsFor(amount));
        }

        // --- Issue #163: buying power-ups with coins ---

        /// <summary>
        /// The structural half of the price contract: a freshly created config prices every kind with a
        /// positive, buyable figure, so the shop is never a wall of unbuyable rows. The exact figures
        /// are pinned in <c>PowerUpPriceConfigTests</c> (issue #402); this test deliberately does not
        /// order the kinds against each other, because that pass priced by value rather than by gate.
        /// </summary>
        [Test]
        public void PowerUpPriceConfig_OnAFreshInstance_PricesEveryKindAbovePlaceholderZero()
        {
            PowerUpKind[] allKinds = (PowerUpKind[])System.Enum.GetValues(typeof(PowerUpKind));
            for (int kindIndex = 0; kindIndex < allKinds.Length; kindIndex++)
            {
                // Hold is deliberately unpriced: it is earned through rewarded ads only and appears in
                // no shop (issue #202); putting it on sale is a coin-economy decision for #160, not a
                // row this test may demand into existence. Coin Sower joined it in issue #404: its
                // charges are banked two per rewarded ad and spent at the level-start picker, never
                // bought.
                if (allKinds[kindIndex] == PowerUpKind.Hold || allKinds[kindIndex] == PowerUpKind.CoinSower)
                {
                    continue;
                }

                int price = _priceConfig.GetPrice(allKinds[kindIndex]);
                Assert.Greater(price, 0, $"{allKinds[kindIndex]} has no usable price.");
                Assert.Less(price, int.MaxValue, $"{allKinds[kindIndex]} is priced as unbuyable.");
            }
        }

        /// <summary>
        /// AC2 in full: the exact total price leaves the balance, the exact quantity arrives in the
        /// inventory, and the result says so. The quantity is deliberately more than one, so a purchase
        /// that granted a flat "+1" would fail here rather than pass by coincidence.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_WithEnoughCoins_DebitsTheTotalAndGrantsTheQuantity()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 500);
            long expectedPrice = system.QuotePriceFor(PowerUpKind.Bomb, 3);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Bomb, 3);

            Assert.AreEqual(PowerUpPurchaseResult.Success, result);
            Assert.AreEqual(500 - expectedPrice, profileModel.CoinBalance.Value);
            Assert.AreEqual(3, CountOf(PowerUpKind.Bomb));
        }

        /// <summary>The quote the shop shows and the figure actually charged are the same arithmetic, not
        /// two copies of it.</summary>
        [Test]
        public void QuotePriceFor_MatchesWhatAPurchaseActuallyCharges()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 1000);
            long quoted = system.QuotePriceFor(PowerUpKind.Joker, 2);

            system.TryPurchasePowerUp(PowerUpKind.Joker, 2);

            Assert.AreEqual(1000 - quoted, profileModel.CoinBalance.Value);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void QuotePriceFor_WithANonPositiveQuantity_IsZero(int quantity)
        {
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(0L, system.QuotePriceFor(PowerUpKind.Bomb, quantity));
        }

        /// <summary>
        /// AC3: too few coins is a refusal, not a partial purchase and not a silent no-op. The ask is one
        /// coin past the balance, so nothing but the comparison itself can explain the refusal.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_WithTooFewCoins_ChangesNothingAndSaysWhy()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());
            int price = (int)system.QuotePriceFor(PowerUpKind.Bomb, 1);
            GrantCoins(system, profileModel, price - 1);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Bomb, 1);

            Assert.AreEqual(PowerUpPurchaseResult.InsufficientCoins, result);
            Assert.AreEqual(price - 1, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, CountOf(PowerUpKind.Bomb));
            Assert.AreEqual(0, _grantedBroker.Published.Count);
        }

        /// <summary>The over-ask is refused rather than clamped down to the two the player could afford:
        /// they named a quantity, and buying a different one would be the worse surprise.</summary>
        [Test]
        public void TryPurchasePowerUp_AskingForMoreThanTheBalanceCovers_BuysNoneOfThem()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());
            int unitPrice = (int)system.QuotePriceFor(PowerUpKind.Bomb, 1);
            GrantCoins(system, profileModel, unitPrice * 2);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Bomb, 3);

            Assert.AreEqual(PowerUpPurchaseResult.InsufficientCoins, result);
            Assert.AreEqual(unitPrice * 2, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, CountOf(PowerUpKind.Bomb));
        }

        /// <summary>
        /// AC4, the one that matters most: the level gate is never openable with currency. The player is
        /// given far more coins than the kind costs and is still refused — and refused as
        /// <see cref="PowerUpPurchaseResult.Locked"/>, not as unaffordable, because "level up" and "earn
        /// coins" are two different things to tell them.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_ForALockedKind_IsRefusedEvenWithAmpleCoins()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 100000);

            // One level below the gate, so nothing but the gate itself can explain the refusal.
            _levelProgressionModel.CurrentLevelNumber.Value =
                PowerUpUnlockLevels.LevelFor(PowerUpKind.GhostFit) - 1;

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.GhostFit, 1);

            Assert.AreEqual(PowerUpPurchaseResult.Locked, result);
            Assert.AreEqual(100000, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, CountOf(PowerUpKind.GhostFit));
            Assert.AreEqual(0, _grantedBroker.Published.Count);
        }

        /// <summary>The gate is a gate, not a discount: reaching the level with the same coins in hand
        /// lets the same purchase through, so the refusal above was the gate and nothing else.</summary>
        [Test]
        public void TryPurchasePowerUp_OnceTheGateIsReached_LetsTheSamePurchaseThrough()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 100000);
            _levelProgressionModel.CurrentLevelNumber.Value =
                PowerUpUnlockLevels.LevelFor(PowerUpKind.GhostFit);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.GhostFit, 1);

            Assert.AreEqual(PowerUpPurchaseResult.Success, result);
            Assert.AreEqual(1, CountOf(PowerUpKind.GhostFit));
        }

        /// <summary>A locked kind is refused before the price is even consulted, so a player who cannot
        /// afford it either is still told the true reason.</summary>
        [Test]
        public void TryPurchasePowerUp_ForALockedKindWithNoCoins_ReportsLockedRatherThanUnaffordable()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());
            _levelProgressionModel.CurrentLevelNumber.Value = 1;

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Reroll, 1);

            Assert.AreEqual(PowerUpPurchaseResult.Locked, result);
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
        }

        /// <summary>AC5: an empty or negative ask changes nothing and is reported as the nonsense it is,
        /// rather than being read as "buy none" and passing quietly.</summary>
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(-500)]
        public void TryPurchasePowerUp_WithANonPositiveQuantity_ChangesNothing(int quantity)
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 500);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Bomb, quantity);

            Assert.AreEqual(PowerUpPurchaseResult.InvalidQuantity, result);
            Assert.AreEqual(500, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, CountOf(PowerUpKind.Bomb));
            Assert.AreEqual(0, _grantedBroker.Published.Count);
        }

        /// <summary>A purchase grants through the same <c>Grant</c> the ad and badge paths use, so it
        /// announces itself on the same channel with the running count — once per unit bought.</summary>
        [Test]
        public void TryPurchasePowerUp_PublishesOneGrantPerUnitBought()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 500);

            system.TryPurchasePowerUp(PowerUpKind.RowClear, 2);

            Assert.AreEqual(2, _grantedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.RowClear, _grantedBroker.Published[0].Kind);
            Assert.AreEqual(1, _grantedBroker.Published[0].NewInventoryCount);
            Assert.AreEqual(2, _grantedBroker.Published[1].NewInventoryCount);
        }

        /// <summary>AC6 as a regression check: the rewarded-ad earning path is untouched by the purchase
        /// path sharing <c>Grant</c> with it. Covered in full by PowerUpSystemTests — this only pins that
        /// the two still coexist behind one inventory.</summary>
        [Test]
        public void GrantRewardAsync_StillGrantsAfterAPurchaseOfTheSameKind()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 500);
            system.TryPurchasePowerUp(PowerUpKind.Bomb, 1);

            bool granted = _powerUpSystem.GrantRewardAsync(PowerUpKind.Bomb, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(granted);
            Assert.AreEqual(2, CountOf(PowerUpKind.Bomb), "An ad grant must still stack on a bought one.");
        }

        /// <summary>
        /// AC2's persistence half: both sides of a purchase reach the disk together, so a fresh
        /// model-and-system pair — i.e. a relaunch — reads back the debited balance and the granted
        /// count. There is no state in which one survived and the other did not.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_ThenANewSystem_LoadsBothTheDebitAndTheGrant()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 500);
            system.TryPurchasePowerUp(PowerUpKind.ColumnClear, 2);
            int expectedBalance = profileModel.CoinBalance.Value;

            var reloadedModel = new ProfileModel();
            CurrencySystem unused = CreateSystem(reloadedModel, new ScoreModel());

            Assert.AreEqual(expectedBalance, reloadedModel.CoinBalance.Value);
            Assert.AreEqual(2, CountOf(PowerUpKind.ColumnClear));
            Assert.Less(expectedBalance, 500, "The purchase must actually have cost something.");
        }

        /// <summary>A purchase takes nothing out of the convertible pool: coins are the only thing spent,
        /// and the score that bought them was already accounted for when it was converted.</summary>
        [Test]
        public void TryPurchasePowerUp_LeavesTheConvertiblePoolAlone()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 1000);
            system.ConvertScoreToCoins(1000);
            int availableAfterConversion = system.AvailableToConvert;

            system.TryPurchasePowerUp(PowerUpKind.Bomb, 1);

            Assert.AreEqual(availableAfterConversion, system.AvailableToConvert);
            Assert.AreEqual(1000, profileModel.TotalScoreEarned.Value);
            Assert.AreEqual(1000, profileModel.ScoreConverted.Value);
        }

        // --- Issue #479: the coin lives pack, the second coin sink ---

        /// <summary>
        /// The issue's AC in full: exactly the price leaves the balance, exactly ten lives arrive, and
        /// both are on the disk — a fresh balance and a fresh lives system read them back.
        /// </summary>
        [Test]
        public void TryPurchaseLivesPack_WithEnoughCoins_DebitsThePriceGrantsTenAndPersistsBoth()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithLivesPack(
                profileModel, coins: 500, lives: 15, out LivesSystem lives);
            int price = system.LivesPackPrice;

            bool bought = system.TryPurchaseLivesPack();

            Assert.IsTrue(bought);
            Assert.AreEqual(500 - price, profileModel.CoinBalance.Value);
            Assert.AreEqual(25, _livesModel.CurrentLives.Value);

            lives.Dispose();
            var reloadedProfile = new ProfileModel();
            CreateSystem(reloadedProfile, new ScoreModel());
            var reloadedLives = new LivesModel();
            LivesSystem reloadedLivesSystem = CreateLivesSystem(-1, reloadedLives);

            Assert.AreEqual(500 - price, reloadedProfile.CoinBalance.Value);
            Assert.AreEqual(25, reloadedLives.CurrentLives.Value);
            reloadedLivesSystem.Dispose();
        }

        /// <summary>The price charged is the config's placeholder figure, read through the one seam the
        /// sheet reads it through.</summary>
        [Test]
        public void LivesPackPrice_IsTheConfiguredPrice_AndIsUnbuyableWithoutAConfig()
        {
            CurrencySystem wired = CreateSystemWithLivesPack(
                new ProfileModel(), coins: 0, lives: 20, out LivesSystem lives);
            CurrencySystem unwired = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(_livesConfig.LivesPackCoinPrice, wired.LivesPackPrice);
            Assert.AreEqual(int.MaxValue, unwired.LivesPackPrice);
            lives.Dispose();
        }

        /// <summary>One coin short is a refusal: no partial debit and not a single life.</summary>
        [Test]
        public void TryPurchaseLivesPack_WithTooFewCoins_ChangesNothing()
        {
            var profileModel = new ProfileModel();
            int shortBalance = _livesConfig.LivesPackCoinPrice - 1;
            CurrencySystem system = CreateSystemWithLivesPack(
                profileModel, coins: shortBalance, lives: 15, out LivesSystem lives);

            bool bought = system.TryPurchaseLivesPack();

            Assert.IsFalse(bought);
            Assert.AreEqual(shortBalance, profileModel.CoinBalance.Value);
            Assert.AreEqual(shortBalance, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            Assert.AreEqual(15, _livesModel.CurrentLives.Value);
            lives.Dispose();
        }

        /// <summary>The pack is uncapped: bought at 22 — where the ad reads "Lives full" — it leaves 32.</summary>
        [TestCase(15, 25)]
        [TestCase(22, 32)]
        public void TryPurchaseLivesPack_IsNotClampedAtTheCap(int livesBefore, int expectedLives)
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithLivesPack(
                profileModel, coins: 1000, lives: livesBefore, out LivesSystem lives);

            Assert.IsTrue(system.TryPurchaseLivesPack());

            Assert.AreEqual(expectedLives, _livesModel.CurrentLives.Value);
            lives.Dispose();
        }

        /// <summary>Two packs back to back are two debits and twenty lives; the second is refused once the
        /// balance no longer covers it.</summary>
        [Test]
        public void TryPurchaseLivesPack_Repeatedly_ChargesEachAndStopsWhenTheBalanceRunsOut()
        {
            var profileModel = new ProfileModel();
            int price = _livesConfig.LivesPackCoinPrice;
            CurrencySystem system = CreateSystemWithLivesPack(
                profileModel, coins: (price * 2) + 1, lives: 20, out LivesSystem lives);

            Assert.IsTrue(system.TryPurchaseLivesPack());
            Assert.IsTrue(system.TryPurchaseLivesPack());
            Assert.IsFalse(system.TryPurchaseLivesPack());

            Assert.AreEqual(1, profileModel.CoinBalance.Value);
            Assert.AreEqual(40, _livesModel.CurrentLives.Value);
            lives.Dispose();
        }

        /// <summary>Without a lives system wired the pack is refused rather than debited for nothing.</summary>
        [Test]
        public void TryPurchaseLivesPack_WithNoLivesSystem_RefusesWithoutDebiting()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 1000);

            Assert.IsFalse(system.TryPurchaseLivesPack());
            Assert.AreEqual(1000, profileModel.CoinBalance.Value);
        }

        // --- Issue #165: time-limited power-up promotions ---
        //
        // Scoped to power-up prices, and deliberately not to coin bundles: a bundle's price lives in App
        // Store Connect and the Play Console (see CoinBundleConfig, which holds coin amounts and no
        // price at all), so there is nothing here for a bundle discount to discount.

        /// <summary>
        /// A fresh config runs no campaign at all. The default that matters most: every other test in
        /// this fixture, and every scene whose config field is unassigned, quotes standard prices
        /// because of it.
        /// </summary>
        [Test]
        public void GetActiveDiscountPercent_OnAFreshConfig_IsZeroForEveryKind()
        {
            Assert.AreEqual(0, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, _utcNow));
            Assert.AreEqual(0, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Joker, _utcNow));
        }

        /// <summary>AC1/AC2's config half: a window containing "now" yields the authored percentage.</summary>
        [Test]
        public void GetActiveDiscountPercent_InsideTheWindow_IsTheAuthoredPercentage()
        {
            AddCampaign(PowerUpKind.Bomb, 25, WindowAroundDefaultNow);

            Assert.AreEqual(25, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, _utcNow));
        }

        /// <summary>AC4's first half: a campaign that has not opened yet discounts nothing.</summary>
        [Test]
        public void GetActiveDiscountPercent_BeforeTheStart_IsZero()
        {
            AddCampaign(PowerUpKind.Bomb, 25, FutureWindow);

            Assert.AreEqual(0, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, _utcNow));
        }

        /// <summary>AC4's second half, and AC3: an ended campaign discounts nothing, with no code
        /// change and nothing to switch off — the dates alone decided it.</summary>
        [Test]
        public void GetActiveDiscountPercent_AfterTheEnd_IsZero()
        {
            AddCampaign(PowerUpKind.Bomb, 25, ExpiredWindow);

            Assert.AreEqual(0, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, _utcNow));
        }

        /// <summary>Both bounds are inclusive, which is the whole of what "runs until the 8th" has to
        /// mean to a player looking at the shop on the 8th.</summary>
        [Test]
        public void GetActiveDiscountPercent_OnEitherBoundary_IsStillActive()
        {
            AddCampaign(PowerUpKind.Bomb, 25, WindowAroundDefaultNow);

            DateTime start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime end = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

            Assert.AreEqual(25, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, start));
            Assert.AreEqual(25, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, end));
        }

        /// <summary>A campaign discounts the kind it names and no other. The one mistake that would turn
        /// a single sale into a storewide one.</summary>
        [Test]
        public void GetActiveDiscountPercent_ForAnUnrelatedKind_IsZero()
        {
            AddCampaign(PowerUpKind.Bomb, 25, WindowAroundDefaultNow);

            Assert.AreEqual(0, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.RowClear, _utcNow));
            Assert.AreEqual(0, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Joker, _utcNow));
        }

        /// <summary>
        /// The documented resolution rule for the authoring mistake that has no single correct answer:
        /// two live rows for one kind resolve to the first, and the discounts do not stack. Asserted on
        /// the smaller-first ordering on purpose — "largest wins" and "sum them" would both have picked
        /// the second row here, so this pins array order as the tie-break rather than accidentally
        /// agreeing with a rule nobody chose.
        /// </summary>
        [Test]
        public void GetActiveDiscountPercent_WithTwoOverlappingCampaigns_TakesTheFirstAndDoesNotStack()
        {
            AddCampaign(PowerUpKind.Bomb, 10, WindowAroundDefaultNow);
            AddCampaign(PowerUpKind.Bomb, 40, WindowAroundDefaultNow);

            Assert.AreEqual(10, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, _utcNow));
        }

        /// <summary>
        /// An expired row does not shadow a live one below it: "first match" means the first
        /// <em>active</em> match, not the first row that happens to name the kind. Otherwise a campaign
        /// nobody deleted would silently cancel every one authored after it.
        /// </summary>
        [Test]
        public void GetActiveDiscountPercent_WithAnExpiredRowAboveALiveOne_TakesTheLiveOne()
        {
            AddCampaign(PowerUpKind.Bomb, 40, ExpiredWindow);
            AddCampaign(PowerUpKind.Bomb, 15, WindowAroundDefaultNow);

            Assert.AreEqual(15, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, _utcNow));
        }

        /// <summary>
        /// A malformed or inverted window is read as no campaign rather than as an always-on one. An
        /// authoring mistake must never resolve in the direction of giving stock away.
        /// </summary>
        [Test]
        [TestCase("not a date", "2026-12-31T00:00:00Z")]
        [TestCase("2026-01-01T00:00:00Z", "")]
        [TestCase("2026-12-31T00:00:00Z", "2026-01-01T00:00:00Z")]
        public void GetActiveDiscountPercent_WithAnUnusableWindow_IsZero(string startUtc, string endUtc)
        {
            _promotionConfig.AddCampaignForTests(PowerUpKind.Bomb, 25, startUtc, endUtc);

            Assert.AreEqual(0, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, _utcNow));
        }

        /// <summary>A percentage outside 0-100 is clamped rather than trusted: a negative one would be a
        /// surcharge and one past a hundred would pay the player to shop.</summary>
        [Test]
        [TestCase(-20, 0)]
        [TestCase(140, 100)]
        public void GetActiveDiscountPercent_WithAnOutOfRangePercentage_IsClamped(
            int authored, int expected)
        {
            AddCampaign(PowerUpKind.Bomb, authored, WindowAroundDefaultNow);

            Assert.AreEqual(
                expected, _promotionConfig.GetActiveDiscountPercent(PowerUpKind.Bomb, _utcNow));
        }

        /// <summary>
        /// AC2's quoting half: inside the window the shop is quoted the discounted figure. Asserted
        /// against the standard price too, so this cannot pass on a config that priced Joker at 7 all
        /// along. (25% off 10 is 7.5, floored to 7 — see the rounding test below.)
        /// </summary>
        [Test]
        public void QuotePriceFor_InsideAnActivePromotion_QuotesTheDiscountedPrice()
        {
            AddCampaign(PowerUpKind.Joker, 25, WindowAroundDefaultNow);
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(10, _priceConfig.GetPrice(PowerUpKind.Joker), "Price config changed.");
            Assert.AreEqual(7L, system.QuotePriceFor(PowerUpKind.Joker, 1));
        }

        /// <summary>
        /// Pins the rounding decision, in the direction it was decided: the price is the remaining
        /// fraction floored (33), never the base less a floored discount (34). One coin, and the only
        /// coin in this feature a player could catch the game taking.
        /// </summary>
        [Test]
        public void QuotePriceFor_WithAPercentageThatDoesNotDivide_RoundsThePriceDown()
        {
            AddCampaign(PowerUpKind.Bomb, 33, WindowAroundDefaultNow);
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(50, _priceConfig.GetPrice(PowerUpKind.Bomb), "Price config changed.");
            Assert.AreEqual(33L, system.QuotePriceFor(PowerUpKind.Bomb, 1));
        }

        /// <summary>
        /// The discount is taken off the line total and rounded once, so three at a time is the floor of
        /// the real 100.5 rather than three separately-floored 33s.
        /// </summary>
        [Test]
        public void QuotePriceFor_WithAQuantity_DiscountsTheLineTotalOnce()
        {
            AddCampaign(PowerUpKind.Bomb, 33, WindowAroundDefaultNow);
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(100L, system.QuotePriceFor(PowerUpKind.Bomb, 3));
        }

        /// <summary>AC3: outside the window the quote is the standard price again, decided by the dates
        /// alone.</summary>
        [Test]
        public void QuotePriceFor_OutsideTheWindow_QuotesTheStandardPrice()
        {
            AddCampaign(PowerUpKind.Joker, 25, ExpiredWindow);
            AddCampaign(PowerUpKind.ColorCleanser, 50, FutureWindow);
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(10L, system.QuotePriceFor(PowerUpKind.Joker, 1));
            Assert.AreEqual(75L, system.QuotePriceFor(PowerUpKind.ColorCleanser, 1));
        }

        /// <summary>
        /// AC3 as the player meets it: the same system, the same config, the same campaign — and the
        /// price reverts because the clock moved past the end date. Nothing was reconfigured and nothing
        /// was restarted, which is the whole of "driven purely by the config's dates".
        /// </summary>
        [Test]
        public void QuotePriceFor_OnceTheWindowCloses_RevertsWithoutAnythingBeingChanged()
        {
            AddCampaign(PowerUpKind.Joker, 25, WindowAroundDefaultNow);
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(7L, system.QuotePriceFor(PowerUpKind.Joker, 1));

            _utcNow = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.AreEqual(10L, system.QuotePriceFor(PowerUpKind.Joker, 1));
        }

        /// <summary>
        /// AC2's charging half, and the bug this whole design exists to prevent: the balance is debited
        /// the discounted figure, not the standard one. Read off <see cref="ProfileModel.CoinBalance"/>
        /// rather than off the quote, so a purchase that quietly charged the base price would fail here
        /// even though the shop showed the sale.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_InsideAnActivePromotion_ChargesTheDiscountedPrice()
        {
            AddCampaign(PowerUpKind.Joker, 25, WindowAroundDefaultNow);
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 200);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Joker, 1);

            Assert.AreEqual(PowerUpPurchaseResult.Success, result);
            Assert.AreEqual(193, profileModel.CoinBalance.Value, "The standard 10 was charged.");
            Assert.AreEqual(193, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            Assert.AreEqual(1, CountOf(PowerUpKind.Joker));
        }

        /// <summary>
        /// AC4 at the till, which is where it has to hold: an expired campaign charges the full standard
        /// price. The negative case for the whole feature — a sale that outlives its end date is the one
        /// failure that costs real money.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_WithAnExpiredPromotion_ChargesTheFullStandardPrice()
        {
            AddCampaign(PowerUpKind.Joker, 25, ExpiredWindow);
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 200);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Joker, 1);

            Assert.AreEqual(PowerUpPurchaseResult.Success, result);
            Assert.AreEqual(190, profileModel.CoinBalance.Value);
        }

        /// <summary>AC4's other half at the till: a campaign that has not opened yet charges standard
        /// too.</summary>
        [Test]
        public void TryPurchasePowerUp_WithANotYetStartedPromotion_ChargesTheFullStandardPrice()
        {
            AddCampaign(PowerUpKind.Joker, 25, FutureWindow);
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 200);

            system.TryPurchasePowerUp(PowerUpKind.Joker, 1);

            Assert.AreEqual(190, profileModel.CoinBalance.Value);
        }

        /// <summary>
        /// The quote and the charge are the same number under a discount, exactly as they are without
        /// one. The contract the shop's price label depends on, restated for the promotional path.
        /// </summary>
        [Test]
        public void QuotePriceFor_UnderAPromotion_MatchesWhatAPurchaseActuallyCharges()
        {
            AddCampaign(PowerUpKind.ColorCleanser, 40, WindowAroundDefaultNow);
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 400);

            long quote = system.QuotePriceFor(PowerUpKind.ColorCleanser, 2);
            system.TryPurchasePowerUp(PowerUpKind.ColorCleanser, 2);

            Assert.AreEqual(quote, 400 - profileModel.CoinBalance.Value);
        }

        /// <summary>
        /// A discount can only make a purchase possible, never impossible: a balance short of the
        /// standard price still buys a discounted power-up, because the affordability check weighs the
        /// figure the player is actually charged.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_AffordableOnlyBecauseOfThePromotion_GoesThrough()
        {
            AddCampaign(PowerUpKind.Joker, 50, WindowAroundDefaultNow);
            var profileModel = new ProfileModel();
            // Six coins: short of Joker's standard 10, enough for the discounted 5.
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 6);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Joker, 1);

            Assert.AreEqual(PowerUpPurchaseResult.Success, result);
            Assert.AreEqual(1, profileModel.CoinBalance.Value);
        }

        /// <summary>
        /// A sale does not open a level gate. Currency never did (see the gating tests above) and a
        /// discount is still currency, so a locked kind is refused as locked however cheap it is.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_ForALockedKindOnPromotion_IsStillRefusedAsLocked()
        {
            AddCampaign(PowerUpKind.Joker, 90, WindowAroundDefaultNow);
            _levelProgressionModel.CurrentLevelNumber.Value = 1;
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 500);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.Joker, 1);

            Assert.AreEqual(PowerUpPurchaseResult.Locked, result);
            Assert.AreEqual(500, profileModel.CoinBalance.Value);
        }

        /// <summary>
        /// A 100% campaign is authorable and is honoured at zero rather than clamped up to a coin. It is
        /// pinned because the affordability check compares against the balance, and "free" is the one
        /// price a broke player must still be able to pay.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_UnderAFullDiscount_CostsNothingAndStillGrants()
        {
            AddCampaign(PowerUpKind.Joker, 100, WindowAroundDefaultNow);
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());

            Assert.AreEqual(0L, system.QuotePriceFor(PowerUpKind.Joker, 1));
            Assert.AreEqual(PowerUpPurchaseResult.Success, system.TryPurchasePowerUp(PowerUpKind.Joker, 1));
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(1, CountOf(PowerUpKind.Joker));
        }

        /// <summary>A non-positive quantity is still zero, discount or no discount: the guard runs before
        /// anything is priced.</summary>
        [Test]
        public void QuotePriceFor_UnderAPromotionWithANonPositiveQuantity_IsZero()
        {
            AddCampaign(PowerUpKind.Bomb, 25, WindowAroundDefaultNow);
            CurrencySystem system = CreateSystem(new ProfileModel(), new ScoreModel());

            Assert.AreEqual(0L, system.QuotePriceFor(PowerUpKind.Bomb, 0));
            Assert.AreEqual(0L, system.QuotePriceFor(PowerUpKind.Bomb, -3));
        }

        // --- Issue #166: coin cells ---

        /// <summary>
        /// Pins the placeholder payout. Not a claim about the economy — the number is expected to be
        /// retuned in the asset — only that a fresh config pays something, so a destroyed coin cell is
        /// worth more than nothing out of the box.
        /// </summary>
        [Test]
        public void CoinCellPayout_OnAFreshConfig_IsPositive()
        {
            Assert.Greater(_config.CoinCellPayout, 0);
        }

        /// <summary>
        /// AC2/AC3's crediting half: the announced payout lands in the balance exactly as announced, and
        /// reaches the disk with it. Deliberately not recomputed from the config here — whatever decided
        /// the doubling decided the figure, and this system's job is to bank what it was told.
        /// </summary>
        [Test]
        public void OnCoinCellsCleared_CreditsTheAnnouncedAmountAndPersistsIt()
        {
            var profileModel = new ProfileModel();
            CurrencySystem unused = CreateSystem(profileModel, new ScoreModel());

            _coinCellsBroker.Publish(new CoinCellsClearedMessage(12));

            Assert.AreEqual(12, profileModel.CoinBalance.Value);
            Assert.AreEqual(12, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
        }

        /// <summary>AC4: every destruction pays, so repeated payouts stack rather than replacing each
        /// other or being deduplicated.</summary>
        [Test]
        public void OnCoinCellsCleared_SeveralTimesOver_AccumulatesEveryPayout()
        {
            var profileModel = new ProfileModel();
            CurrencySystem unused = CreateSystem(profileModel, new ScoreModel());

            _coinCellsBroker.Publish(new CoinCellsClearedMessage(5));
            _coinCellsBroker.Publish(new CoinCellsClearedMessage(10));
            _coinCellsBroker.Publish(new CoinCellsClearedMessage(5));

            Assert.AreEqual(20, profileModel.CoinBalance.Value);
        }

        /// <summary>A coin cell is not a score conversion: it takes nothing out of the convertible pool
        /// and leaves both counters behind it exactly as it found them.</summary>
        [Test]
        public void OnCoinCellsCleared_LeavesTheConvertiblePoolAlone()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithPool(profileModel, pool: 100);

            _coinCellsBroker.Publish(new CoinCellsClearedMessage(7));

            Assert.AreEqual(7, profileModel.CoinBalance.Value);
            Assert.AreEqual(100, system.AvailableToConvert);
            Assert.AreEqual(0, profileModel.ScoreConverted.Value);
            Assert.AreEqual(0, _convertedBroker.Published.Count);
            Assert.AreEqual(0, _adGrantBroker.Published.Count);
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void OnCoinCellsCleared_WithANonPositiveAmount_ChangesNothing(int totalCoins)
        {
            var profileModel = new ProfileModel();
            CurrencySystem unused = CreateSystem(profileModel, new ScoreModel());

            _coinCellsBroker.Publish(new CoinCellsClearedMessage(totalCoins));

            Assert.AreEqual(0, profileModel.CoinBalance.Value);
        }

        /// <summary>Coin-cell coins go through the same bookkeeping the other two faucets do, so they
        /// survive a restart the same way.</summary>
        [Test]
        public void OnCoinCellsCleared_ThenANewSystem_LoadsTheCreditedBalance()
        {
            CurrencySystem unused = CreateSystem(new ProfileModel(), new ScoreModel());
            _coinCellsBroker.Publish(new CoinCellsClearedMessage(18));

            var reloadedModel = new ProfileModel();
            CurrencySystem reloaded = CreateSystem(reloadedModel, new ScoreModel());

            Assert.AreEqual(18, reloadedModel.CoinBalance.Value);
            Assert.AreEqual(0, reloaded.AvailableToConvert, "A coin cell earns no convertible score.");
        }

        /// <summary>Coin-cell coins are coins: they buy power-ups exactly as converted or ad-granted
        /// ones do, which is the whole point of paying into the same balance.</summary>
        [Test]
        public void OnCoinCellsCleared_TheCreditedCoinsCanBuyAPowerUp()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystem(profileModel, new ScoreModel());
            int price = (int)system.QuotePriceFor(PowerUpKind.Bomb, 1);

            _coinCellsBroker.Publish(new CoinCellsClearedMessage(price));

            Assert.AreEqual(PowerUpPurchaseResult.Success, system.TryPurchasePowerUp(PowerUpKind.Bomb, 1));
            Assert.AreEqual(0, profileModel.CoinBalance.Value);
            Assert.AreEqual(1, CountOf(PowerUpKind.Bomb));
        }

        /// <summary>
        /// The whole chain, end to end and through no stub at all: a real placement completes a row
        /// holding a real coin cell, the real <see cref="BoardSystem"/> resolves it, and the balance on
        /// <see cref="ProfileModel"/> grows by the configured payout. The one test that would fail if
        /// any link — the effect, the composite wiring, the message, the subscription — were missing.
        /// </summary>
        [Test]
        public void ARealPlacementDestroyingACoinCell_CreditsTheConfiguredPayoutToTheBalance()
        {
            var profileModel = new ProfileModel();
            CurrencySystem unused = CreateSystem(profileModel, new ScoreModel());

            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            BoardSystem boardSystem = CreateBoardSystemPayingCoins(boardModel, trayModel);

            var gap = new GridPosition(3, 5);
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, 5);
                if (!position.Equals(gap))
                {
                    boardModel.Occupy(position, 1);
                }
            }

            boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.Coin);
            trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);

            Assert.IsTrue(boardSystem.TryPlacePiece(0, gap));

            Assert.AreEqual(_config.CoinCellPayout, profileModel.CoinBalance.Value);
            Assert.AreEqual(_config.CoinCellPayout, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
        }

        // --- Issue #404: the Coin Sower is ad-only, earned two charges at a time and sown in bulk ---

        /// <summary>
        /// Coin Sower has no price row (issue #404), so the shop answers it the way it answers Hold: an
        /// unpriced kind fails the balance check however rich the player is, and nothing moves. This is
        /// what keeps the general shop and any stray purchase path from selling it for coins.
        /// </summary>
        [Test]
        public void TryPurchasePowerUp_CoinSower_IsAlwaysRefused_NowUnpriced()
        {
            var profileModel = new ProfileModel();
            CurrencySystem system = CreateSystemWithCoins(profileModel, coins: 100000);

            PowerUpPurchaseResult result = system.TryPurchasePowerUp(PowerUpKind.CoinSower, 1);

            Assert.AreEqual(PowerUpPurchaseResult.InsufficientCoins, result);
            Assert.AreEqual(100000, profileModel.CoinBalance.Value);
            Assert.AreEqual(0, CountOf(PowerUpKind.CoinSower));
            Assert.AreEqual(0, _grantedBroker.Published.Count);
        }

        /// <summary>
        /// The earned charges are the ones sown: two rewarded ads bank four charges through the same
        /// grant seam Hold uses, and the level-start picker's bulk spend takes exactly that many back
        /// out again, leaving nothing behind — and no coins moved at any point.
        /// </summary>
        [Test]
        public void TrySpendCoinSowerBulk_AfterAnAdGrant_EmptiesTheBankedCharges()
        {
            var profileModel = new ProfileModel();
            CurrencySystem unused = CreateSystemWithCoins(profileModel, coins: 1000);

            Assert.IsTrue(_powerUpSystem
                .GrantRewardAsync(PowerUpKind.CoinSower, CancellationToken.None, quantity: 2)
                .GetAwaiter().GetResult());
            Assert.IsTrue(_powerUpSystem
                .GrantRewardAsync(PowerUpKind.CoinSower, CancellationToken.None, quantity: 2)
                .GetAwaiter().GetResult());
            Assert.AreEqual(4, CountOf(PowerUpKind.CoinSower));

            Assert.IsTrue(_powerUpSystem.TrySpendCoinSowerBulk(4));

            Assert.AreEqual(0, CountOf(PowerUpKind.CoinSower));
            Assert.AreEqual(1000, profileModel.CoinBalance.Value);
        }

        /// <summary>
        /// A real, unstarted <see cref="BoardSystem"/> wired to the same coin channel and the same config
        /// the system under test reads, so the payout it announces is the one this fixture's
        /// <see cref="CurrencySystem"/> hears and the figure it quotes is the one that was configured.
        /// </summary>
        private BoardSystem CreateBoardSystemPayingCoins(BoardModel boardModel, TrayModel trayModel)
        {
            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                _coinCellsBroker,
                _config,
                reinforcedCellSeeder: null);
        }

        /// <summary>
        /// Builds a system whose balance already holds <paramref name="coins"/>, banked through the real
        /// ad-grant path rather than a direct write — so every purchase test starts from a balance that
        /// was filled the only way the game can fill one.
        /// </summary>
        private CurrencySystem CreateSystemWithCoins(ProfileModel profileModel, int coins)
        {
            CurrencySystem system = CreateSystem(
                profileModel, new ScoreModel(), new StubCoinRewardSource(granted: true));
            GrantCoins(system, profileModel, coins);
            return system;
        }

        /// <summary>
        /// Tops a balance up through the ad faucet: the purchase tests need coins, and this is one of
        /// only two ways the game can mint one. Asserted rather than assumed, so a test that fails
        /// downstream cannot be a setup that quietly banked nothing.
        /// </summary>
        private static void GrantCoins(CurrencySystem system, ProfileModel profileModel, int coins)
        {
            system.GrantCoinsFromAdAsync(coins, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(coins, profileModel.CoinBalance.Value, "Test setup failed to bank the coins.");
        }

        /// <summary>
        /// Builds a system whose pool already holds <paramref name="pool"/> points, by ending a run worth
        /// exactly that much. Routed through the real game-over path rather than a direct write, so every
        /// conversion test starts from a pool that was filled the only way the game can fill one.
        /// </summary>
        private CurrencySystem CreateSystemWithPool(
            ProfileModel profileModel, int pool, ICoinRewardSource coinRewardSource = null)
        {
            var scoreModel = new ScoreModel();
            scoreModel.Score.Value = pool;
            CurrencySystem system = CreateSystem(profileModel, scoreModel, coinRewardSource);
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));

            // The run is over; a later conversion must not accidentally bank this score a second time.
            scoreModel.Score.Value = 0;
            return system;
        }

        private CurrencySystem CreateSystem(
            ProfileModel profileModel,
            ScoreModel scoreModel,
            ICoinRewardSource coinRewardSource = null,
            DailyAdGrantModel dailyAdGrantModel = null,
            TestMessageBroker<RunStartedMessage> runStartedBroker = null,
            LivesSystem livesSystem = null)
        {
            return new CurrencySystem(
                profileModel,
                dailyAdGrantModel ?? new DailyAdGrantModel(),
                new CoinBundlePriceModel(),
                scoreModel,
                _levelProgressionModel,
                _config,
                _priceConfig,
                _promotionConfig,
                CreatePowerUpSystem(),
                coinRewardSource ?? new StubCoinRewardSource(granted: true),
                // The real-money path is covered in full by CurrencySystemPurchaseTests. Here it is
                // wired to a store that refuses everything, so nothing in this fixture can reach it by
                // accident and quietly bank coins no test asked for.
                new StubCoinPurchaseService(CoinPurchaseOutcome.Failed),
                StubPurchaseReceiptValidator.Rejecting(),
                _convertedBroker,
                _adGrantBroker,
                new TestMessageBroker<CoinsGrantedFromPurchaseMessage>(),
                _gameOverBroker,
                _coinCellsBroker,
                new TestMessageBroker<CoinProductsFetchedMessage>(),
                // The seeded-clock overload, so every quote in this fixture is priced at an instant the
                // test controls rather than at whenever the suite happens to run. Read through the
                // lambda on each quote, not captured by value, so a test can move "now" after building
                // the system — which is how the "reverts on its own" cases are written.
                () => _utcNow,
                // The same seeded instant doubles as the daily ad cap's "local now" (issue #257): this
                // fixture has no reason to run the two clocks apart, and reusing the one field lets a
                // day-rollover test move "today" the same way the promotion tests already move "now".
                () => _utcNow,
                runStartedBroker,
                // The lives pack's pair (issue #479): only wired when a test built a LivesSystem, so every
                // other test's system refuses a pack exactly as a misconfigured scene would.
                livesSystem != null ? _livesConfig : null,
                livesSystem);
        }

        /// <summary>
        /// A real <see cref="LivesSystem"/> over <see cref="_livesModel"/>, starting from
        /// <paramref name="lives"/> saved at the fixed <see cref="LivesNow"/> — so the pack grants through
        /// the very code the game grants through, and a reload reads the count back out of PlayerPrefs.
        /// Never started, so no countdown loop runs under a test.
        /// </summary>
        private LivesSystem CreateLivesSystem(int lives, LivesModel model = null)
        {
            if (lives >= 0)
            {
                var data = new LivesSaveData
                {
                    lives = lives,
                    lastRefillHourStamp = LivesNow.Ticks / TimeSpan.TicksPerHour,
                };
                PlayerPrefs.SetString(LIVES_SAVE_KEY, JsonUtility.ToJson(data));
            }

            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Path;
            return new LivesSystem(
                model ?? _livesModel,
                _livesConfig,
                gameModeModel,
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<RunRescuedMessage>(),
                new TestMessageBroker<RunStartedMessage>(),
                new DeterministicLivesRewardSource(),
                new TestMessageBroker<OutOfLivesMessage>(),
                () => LivesNow);
        }

        /// <summary>A system wired for the lives pack, holding <paramref name="coins"/> coins and
        /// <paramref name="lives"/> lives.</summary>
        private CurrencySystem CreateSystemWithLivesPack(
            ProfileModel profileModel, int coins, int lives, out LivesSystem livesSystem)
        {
            livesSystem = CreateLivesSystem(lives);
            CurrencySystem system = CreateSystem(
                profileModel, new ScoreModel(), new StubCoinRewardSource(granted: true), livesSystem: livesSystem);
            if (coins > 0)
            {
                GrantCoins(system, profileModel, coins);
            }

            return system;
        }

        /// <summary>
        /// A real <see cref="PowerUpSystem"/> over a fresh <see cref="PowerUpModel"/>, so a purchase
        /// grants through the very code the ad and badge paths grant through rather than through a stub
        /// that could agree with the test and disagree with the game. Both are stored on fields for the
        /// test to read.
        /// <para>
        /// The board, tray and clock it takes are real but unstarted: the purchase path touches none of
        /// them, and a stub of each would only be a second description of "does nothing".
        /// </para>
        /// </summary>
        private PowerUpSystem CreatePowerUpSystem()
        {
            _powerUpModel = new PowerUpModel();

            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            BoardSystem boardSystem = CreateBoardSystem(boardModel, trayModel);
            var ghostFitModel = new GhostFitModel();

            _powerUpSystem = new PowerUpSystem(
                _powerUpModel,
                _levelProgressionModel,
                ScriptableObject.CreateInstance<LevelCatalog>(),
                new GameModeModel(),
                new PathRunModel(),
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
                    _appliedBroker),
                ghostFitModel,
                new StubRewardSource(granted: true),
                _appliedBroker,
                _grantedBroker,
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());

            return _powerUpSystem;
        }

        /// <summary>A real, unstarted <see cref="BoardSystem"/>: the purchase path reads nothing from it,
        /// and <c>PowerUpSystem</c> only reads its <c>IsGameOver</c> flag, which is false until a run is
        /// started or checked.</summary>
        private static BoardSystem CreateBoardSystem(BoardModel boardModel, TrayModel trayModel)
        {
            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                reinforcedCellSeeder: null);
        }

        /// <summary>Only ever asked to hold and release the countdown by the paths under test here, and
        /// never even that — it is never ticked.</summary>
        private static TimerRunSystem CreateTimerRunSystem(BoardSystem boardSystem)
        {
            TimedModeConfig timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            return new TimerRunSystem(
                new TimerModel(),
                new RunPauseModel(),
                new GameModeSystem(new GameModeModel(), boardSystem),
                new TimedModeSystem(new TimedModeModel(), timedModeConfig),
                boardSystem,
                new TestMessageBroker<RunStartedMessage>(),
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

        /// <summary>The inventory counter for one kind, read off the model the last-built
        /// <see cref="PowerUpSystem"/> owns.</summary>
        private int CountOf(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.RowClear:
                    return _powerUpModel.RowClearCount.Value;
                case PowerUpKind.ColumnClear:
                    return _powerUpModel.ColumnClearCount.Value;
                case PowerUpKind.Joker:
                    return _powerUpModel.JokerCount.Value;
                case PowerUpKind.ColorCleanser:
                    return _powerUpModel.ColorCleanserCount.Value;
                case PowerUpKind.Rotate:
                    return _powerUpModel.RotateCount.Value;
                case PowerUpKind.Reroll:
                    return _powerUpModel.RerollCount.Value;
                case PowerUpKind.DoubleMultiplier:
                    return _powerUpModel.DoubleMultiplierCount.Value;
                case PowerUpKind.GhostFit:
                    return _powerUpModel.GhostFitCount.Value;
                case PowerUpKind.CoinSower:
                    return _powerUpModel.CoinSowerCount.Value;
                case PowerUpKind.PaintCross:
                    return _powerUpModel.PaintCrossCount.Value;
                default:
                    return _powerUpModel.BombCount.Value;
            }
        }

        /// <summary>
        /// A window straddling <see cref="DefaultNowUtc"/>, so a campaign authored with it is live at the
        /// instant every test starts from.
        /// </summary>
        private static readonly TestWindow WindowAroundDefaultNow =
            new TestWindow("2026-06-01T00:00:00Z", "2026-06-30T23:59:59Z");

        /// <summary>A window that closed well before <see cref="DefaultNowUtc"/>. AC4's expired
        /// campaign.</summary>
        private static readonly TestWindow ExpiredWindow =
            new TestWindow("2025-11-24T00:00:00Z", "2025-12-01T23:59:59Z");

        /// <summary>A window that has not opened by <see cref="DefaultNowUtc"/>. AC4's other half.</summary>
        private static readonly TestWindow FutureWindow =
            new TestWindow("2027-01-01T00:00:00Z", "2027-01-08T23:59:59Z");

        /// <summary>
        /// Appends one campaign to the fixture's promotion table. A wrapper over the config's own test
        /// seam purely so the windows above can be named rather than spelled out at twenty call sites —
        /// a mistyped date would be a test that passes for the wrong reason.
        /// </summary>
        private void AddCampaign(PowerUpKind kind, int discountPercent, TestWindow window)
        {
            _promotionConfig.AddCampaignForTests(
                kind, discountPercent, window.StartUtc, window.EndUtc);
        }

        /// <summary>A named pair of ISO-8601 bounds, in the shape the config's authoring seam takes.</summary>
        private readonly struct TestWindow
        {
            internal TestWindow(string startUtc, string endUtc)
            {
                StartUtc = startUtc;
                EndUtc = endUtc;
            }

            internal string StartUtc { get; }

            internal string EndUtc { get; }
        }

        private static void DeleteCurrencyKeys()
        {
            PlayerPrefs.DeleteKey(COIN_BALANCE_KEY);
            PlayerPrefs.DeleteKey(TOTAL_SCORE_EARNED_KEY);
            PlayerPrefs.DeleteKey(SCORE_CONVERTED_KEY);
            PlayerPrefs.DeleteKey(DAILY_AD_GRANTS_REMAINING_KEY);
            PlayerPrefs.DeleteKey(DAILY_AD_GRANT_DAY_MARKER_KEY);
        }

        private static void DeleteInventoryKeys()
        {
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Bomb));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.RowClear));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.ColumnClear));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Joker));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.ColorCleanser));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Rotate));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Reroll));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.DoubleMultiplier));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.GhostFit));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.CoinSower));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.PaintCross));
        }

        /// <summary>
        /// Ad seam stand-in: grants or refuses on command and counts how often it was asked, so a test
        /// can assert both the outcome and that a refused request never reached the source at all.
        /// </summary>
        private sealed class StubCoinRewardSource : ICoinRewardSource
        {
            private readonly bool _granted;

            internal StubCoinRewardSource(bool granted)
            {
                _granted = granted;
            }

            internal int RequestCount { get; private set; }

            public UniTask<CoinRewardResult> RequestCoinRewardAsync(
                int amount, CancellationToken cancellationToken)
            {
                RequestCount++;
                return UniTask.FromResult(new CoinRewardResult(amount, _granted));
            }
        }

        /// <summary>
        /// The power-up ad seam's stand-in, for the one regression test that checks the ad earning path
        /// still works alongside a purchase. Grants on command, exactly as PowerUpSystemTests' own stub
        /// does — the purchase path never reaches it.
        /// </summary>
        private sealed class StubRewardSource : IRewardSource
        {
            private readonly bool _granted;

            internal StubRewardSource(bool granted)
            {
                _granted = granted;
            }

            public UniTask<RewardResult> RequestRewardAsync(
                PowerUpKind kind, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(new RewardResult(kind, _granted));
            }
        }
    }
}
