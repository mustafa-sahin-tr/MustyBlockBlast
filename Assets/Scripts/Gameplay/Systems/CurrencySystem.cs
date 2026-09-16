using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns the currency slice of <see cref="ProfileModel"/>: the coin balance, the lifetime score pool
    /// coins are bought out of, and how much of that pool has already been spent on them. The one and
    /// only writer of those three fields, and the one and only place a coin is created.
    /// <para>
    /// It shares <see cref="ProfileModel"/> with <see cref="ProfileSystem"/> rather than owning a wallet
    /// model of its own, because coins belong to the account: a linked-account migration must carry
    /// them across with the name and the avatar rather than hunt for them in a second place. The split
    /// is the same one <see cref="PowerUpSystem"/> has with the inventory — one writer per slice — and
    /// it extends to loading: the three fields here are loaded by this constructor, not by
    /// <see cref="ProfileSystem"/>'s, so "who writes it" and "who reads it back" are never two answers.
    /// </para>
    /// <para>
    /// A run's score is never reduced, consumed or spent. When a run ends its score is <em>added</em> to
    /// <see cref="ProfileModel.TotalScoreEarned"/> and nothing else happens — no coins are minted, no
    /// pool is drained. What the player may convert is the subtraction
    /// <see cref="AvailableToConvert"/>, and only an explicit <see cref="ConvertScoreToCoins"/> moves
    /// it. The consequence is worth stating plainly: coins are never granted automatically, mid-run or
    /// at the end of one. There are exactly two ways one comes into existence, and both are a deliberate
    /// player action — a conversion, or a watched rewarded ad.
    /// </para>
    /// <para>
    /// Persistence is flat PlayerPrefs keys, one per field, exactly as <see cref="ProfileSystem"/> and
    /// <see cref="PowerUpSystem"/> write theirs: every mutator persists synchronously, so there is no
    /// separate save step to forget. A conversion writes two of those keys and then flushes once with
    /// <see cref="PlayerPrefs.Save"/> — PlayerPrefs writes are in-memory until that flush, so the
    /// deduction and the credit reach the disk together. A crash can lose the whole conversion or keep
    /// the whole conversion; it cannot keep half of one.
    /// </para>
    /// </summary>
    public sealed class CurrencySystem : IDisposable
    {
        private const string COIN_BALANCE_KEY = "Profile.CoinBalance";
        private const string TOTAL_SCORE_EARNED_KEY = "Profile.TotalScoreEarned";
        private const string SCORE_CONVERTED_KEY = "Profile.ScoreConverted";

        private readonly ProfileModel _profileModel;
        private readonly ScoreModel _scoreModel;
        private readonly CurrencyConfig _config;
        private readonly ICoinRewardSource _coinRewardSource;
        private readonly IPublisher<ScoreConvertedToCoinsMessage> _convertedPublisher;
        private readonly IPublisher<CoinsGrantedFromAdMessage> _adGrantPublisher;
        private readonly IDisposable _gameOverSubscription;

        public CurrencySystem(
            ProfileModel profileModel,
            ScoreModel scoreModel,
            CurrencyConfig config,
            ICoinRewardSource coinRewardSource,
            IPublisher<ScoreConvertedToCoinsMessage> convertedPublisher,
            IPublisher<CoinsGrantedFromAdMessage> adGrantPublisher,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _profileModel = profileModel;
            _scoreModel = scoreModel;
            _config = config;
            _coinRewardSource = coinRewardSource;
            _convertedPublisher = convertedPublisher;
            _adGrantPublisher = adGrantPublisher;

            Load();

            // The run's score is only readable while the run is the current one, so the pool has to be
            // topped up on the message that ends it rather than looked up later.
            _gameOverSubscription = gameOverSubscriber.Subscribe(OnGameOver);
        }

        /// <summary>
        /// Points the player may still turn into coins: everything ever earned, less everything already
        /// converted. Computed on every read rather than cached, so there is no third number that could
        /// drift out of step with the two it is derived from.
        /// <para>
        /// Floored at zero defensively. The two counters can only move the way that keeps this
        /// non-negative, but a hand-edited or partially restored save has no such guarantee, and a
        /// negative "available" would read as a debt the player never took on.
        /// </para>
        /// </summary>
        public int AvailableToConvert
            => Mathf.Max(0, _profileModel.TotalScoreEarned.Value - _profileModel.ScoreConverted.Value);

        /// <summary>
        /// Coins <paramref name="scoreAmount"/> points would buy at the configured rate. What the
        /// conversion screen quotes before the player commits, so the number offered and the number
        /// granted come from the same arithmetic rather than two copies of it.
        /// </summary>
        public int QuoteCoinsFor(int scoreAmount)
            => scoreAmount <= 0 ? 0 : Mathf.FloorToInt(scoreAmount * _config.ScoreToCoinRate);

        /// <summary>Coins one watched ad is worth. Read by the conversion screen so it can say what the
        /// offer is before the player takes it.</summary>
        public int AdRewardCoins => _config.AdRewardCoins;

        /// <summary>
        /// Turns <paramref name="scoreAmount"/> of the convertible pool into coins. Partial by design:
        /// converting 60 of 100 available leaves 40 still available, forever, until the player converts
        /// that too.
        /// <para>
        /// Refused outright — nothing deducted, nothing granted, nothing published — when there is
        /// nothing to convert, when nothing is asked for, or when more is asked for than there is. The
        /// over-ask is rejected rather than silently clamped: the screen is built from
        /// <see cref="AvailableToConvert"/> and can never legitimately offer more than it, so an ask
        /// past it is a bug somewhere and quietly converting a different amount than the caller named
        /// would hide it.
        /// </para>
        /// <para>
        /// Rounds down. A rate that does not divide the amount evenly must not mint a coin the score did
        /// not pay for, and the points that bought no coin are still deducted — which is what keeps the
        /// player from converting 9 points at a time, nine times over, to beat the rate.
        /// </para>
        /// </summary>
        public void ConvertScoreToCoins(int scoreAmount)
        {
            if (scoreAmount <= 0 || scoreAmount > AvailableToConvert)
            {
                return;
            }

            int coins = QuoteCoinsFor(scoreAmount);

            // Deducted first, then credited, then flushed once: the two SetInt calls land in the same
            // in-memory page and reach the disk together, so there is no window in which the balance has
            // grown without the pool having shrunk.
            int newConverted = _profileModel.ScoreConverted.Value + scoreAmount;
            _profileModel.ScoreConverted.Value = newConverted;
            PlayerPrefs.SetInt(SCORE_CONVERTED_KEY, newConverted);

            int newBalance = CreditCoins(coins);

            PlayerPrefs.Save();

            _convertedPublisher.Publish(new ScoreConvertedToCoinsMessage(
                scoreAmount, coins, newBalance, AvailableToConvert));
        }

        /// <summary>
        /// Asks <see cref="ICoinRewardSource"/> for <paramref name="amount"/> coins and banks them if
        /// granted. Returns whether they were granted; a refusal leaves the balance untouched.
        /// <para>
        /// Entirely separate from conversion: it takes nothing out of the convertible pool and leaves
        /// <see cref="AvailableToConvert"/> exactly as it found it. The player has not sold any score
        /// here, they have watched an ad.
        /// </para>
        /// </summary>
        public async UniTask<bool> GrantCoinsFromAdAsync(int amount, CancellationToken cancellationToken)
        {
            if (amount <= 0)
            {
                return false;
            }

            CoinRewardResult result = await _coinRewardSource.RequestCoinRewardAsync(amount, cancellationToken);
            if (!result.Granted || result.Amount <= 0)
            {
                return false;
            }

            int newBalance = CreditCoins(result.Amount);
            PlayerPrefs.Save();

            _adGrantPublisher.Publish(new CoinsGrantedFromAdMessage(result.Amount, newBalance));
            return true;
        }

        public void Dispose()
        {
            _gameOverSubscription.Dispose();
        }

        /// <summary>
        /// The one and only way a coin enters the balance: increment, then persist. Both grant paths
        /// funnel through here so the two steps can never drift apart, whatever earned the coins.
        /// <para>
        /// Deliberately does not publish and does not flush. The two paths announce different things —
        /// a conversion is a trade, an ad grant is a gift — and the conversion has a second write to
        /// land before either may reach the disk, so the flush belongs to the caller that knows how many
        /// writes its own outcome is made of.
        /// </para>
        /// </summary>
        private int CreditCoins(int amount)
        {
            int newBalance = _profileModel.CoinBalance.Value + amount;
            _profileModel.CoinBalance.Value = newBalance;
            PlayerPrefs.SetInt(COIN_BALANCE_KEY, newBalance);
            return newBalance;
        }

        /// <summary>
        /// Banks the finished run's score into the lifetime convertible pool. The only place that pool
        /// grows — and it grows, never shrinks: the run's own score is left exactly as it was, so the
        /// end-of-run card and the leaderboard still see the full figure.
        /// <para>
        /// Mints nothing. A player who never opens the conversion screen ends the run with the same coin
        /// balance they started it with.
        /// </para>
        /// </summary>
        private void OnGameOver(GameOverMessage message)
        {
            int runScore = _scoreModel.Score.Value;
            if (runScore <= 0)
            {
                return;
            }

            int newTotal = _profileModel.TotalScoreEarned.Value + runScore;
            _profileModel.TotalScoreEarned.Value = newTotal;
            PlayerPrefs.SetInt(TOTAL_SCORE_EARNED_KEY, newTotal);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Reads the three counters back. Each is floored at zero for the reason
        /// <see cref="AvailableToConvert"/> is: a corrupt or hand-edited save must boot into a poor
        /// player, never an indebted one.
        /// </summary>
        private void Load()
        {
            _profileModel.CoinBalance.Value = Mathf.Max(0, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            _profileModel.TotalScoreEarned.Value = Mathf.Max(0, PlayerPrefs.GetInt(TOTAL_SCORE_EARNED_KEY, 0));
            _profileModel.ScoreConverted.Value = Mathf.Max(0, PlayerPrefs.GetInt(SCORE_CONVERTED_KEY, 0));
        }
    }
}
