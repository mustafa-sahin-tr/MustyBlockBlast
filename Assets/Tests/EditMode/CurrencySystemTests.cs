using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the currency contract: a run's score is banked but never minted, conversion is partial and
    /// leaves the remainder alone, an empty pool is a complete no-op, the ad faucet is independent of the
    /// pool entirely, and every change survives a restart.
    /// </summary>
    public class CurrencySystemTests
    {
        private const string COIN_BALANCE_KEY = "Profile.CoinBalance";
        private const string TOTAL_SCORE_EARNED_KEY = "Profile.TotalScoreEarned";
        private const string SCORE_CONVERTED_KEY = "Profile.ScoreConverted";

        private TestMessageBroker<ScoreConvertedToCoinsMessage> _convertedBroker;
        private TestMessageBroker<CoinsGrantedFromAdMessage> _adGrantBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private CurrencyConfig _config;

        /// <summary>CurrencySystem loads the three counters in its constructor, so a balance left behind
        /// by a previous test would silently decide what the next one can convert.</summary>
        [SetUp]
        public void ClearPersistedCurrency()
        {
            DeleteCurrencyKeys();
            _convertedBroker = new TestMessageBroker<ScoreConvertedToCoinsMessage>();
            _adGrantBroker = new TestMessageBroker<CoinsGrantedFromAdMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _config = ScriptableObject.CreateInstance<CurrencyConfig>();
        }

        [TearDown]
        public void ClearPersistedCurrencyAfterwards()
        {
            DeleteCurrencyKeys();
            if (_config != null)
            {
                Object.DestroyImmediate(_config);
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
            ProfileModel profileModel, ScoreModel scoreModel, ICoinRewardSource coinRewardSource = null)
        {
            return new CurrencySystem(
                profileModel,
                scoreModel,
                _config,
                coinRewardSource ?? new StubCoinRewardSource(granted: true),
                _convertedBroker,
                _adGrantBroker,
                _gameOverBroker);
        }

        private static void DeleteCurrencyKeys()
        {
            PlayerPrefs.DeleteKey(COIN_BALANCE_KEY);
            PlayerPrefs.DeleteKey(TOTAL_SCORE_EARNED_KEY);
            PlayerPrefs.DeleteKey(SCORE_CONVERTED_KEY);
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
    }
}
