using System;
using System.Collections.Generic;
using System.Text;
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
    /// it. The consequence is worth stating plainly: coins are never granted merely for playing, and
    /// never at the end of a run. There are exactly three ways one comes into existence, and all three
    /// are something the player did — a conversion, a watched rewarded ad, or a destroyed
    /// <see cref="MustyBlockBlast.Core.SpecialCellKind.Coin"/> cell. The third is the only one that
    /// happens mid-run, and it is still earned: the coin cell had to be cleared, and one left standing
    /// when the run ends pays nothing.
    /// </para>
    /// <para>
    /// Persistence is flat PlayerPrefs keys, one per field, exactly as <see cref="ProfileSystem"/> and
    /// <see cref="PowerUpSystem"/> write theirs: every mutator persists synchronously, so there is no
    /// separate save step to forget. A conversion writes two of those keys and then flushes once with
    /// <see cref="PlayerPrefs.Save"/> — PlayerPrefs writes are in-memory until that flush, so the
    /// deduction and the credit reach the disk together. A crash can lose the whole conversion or keep
    /// the whole conversion; it cannot keep half of one.
    /// </para>
    /// <para>
    /// It is also the one and only place a coin is <em>spent</em>. Today that means exactly one sink,
    /// <see cref="TryPurchasePowerUp"/>: the debit lives here because the balance does, and the grant
    /// it pays for is delegated to <see cref="PowerUpSystem"/>, which owns the inventory. That keeps
    /// each half of a purchase behind its own single writer — the same one-writer-per-slice split this
    /// class already has with <see cref="ProfileSystem"/> — while the whole purchase still reaches the
    /// disk in one flush.
    /// </para>
    /// <para>
    /// The fourth and last faucet is the only one that costs real money:
    /// <see cref="PurchaseCoinBundleAsync"/>. It is orchestrated here rather than anywhere nearer the
    /// store for the reason every other credit is — this class is the one and only writer of the balance
    /// — and it is the one faucet with a memory: the transaction ids it has already honoured are
    /// persisted alongside the three counters, so a receipt the store replays after a crash, a restore
    /// or a reinstall pays exactly once and never again.
    /// </para>
    /// </summary>
    public sealed class CurrencySystem : IDisposable
    {
        private const string COIN_BALANCE_KEY = "Profile.CoinBalance";
        private const string TOTAL_SCORE_EARNED_KEY = "Profile.TotalScoreEarned";
        private const string SCORE_CONVERTED_KEY = "Profile.ScoreConverted";
        private const string CONSUMED_TRANSACTION_IDS_KEY = "Profile.ConsumedTransactionIds";

        /// <summary>
        /// Separator for the persisted transaction-id list. A newline because store transaction ids are
        /// opaque single-line tokens — Apple's are numeric, Google's are base64url purchase tokens — so
        /// none of them can contain one, which is what lets a flat string stand in for a set without a
        /// JSON dependency this class has never needed for the three counters beside it.
        /// </summary>
        private const char CONSUMED_ID_SEPARATOR = '\n';

        private readonly ProfileModel _profileModel;
        private readonly ScoreModel _scoreModel;
        private readonly LevelProgressionModel _levelProgressionModel;
        private readonly CurrencyConfig _config;
        private readonly PowerUpPriceConfig _priceConfig;
        private readonly PowerUpSystem _powerUpSystem;
        private readonly ICoinRewardSource _coinRewardSource;
        private readonly ICoinPurchaseService _coinPurchaseService;
        private readonly IPurchaseReceiptValidator _receiptValidator;
        private readonly IPublisher<ScoreConvertedToCoinsMessage> _convertedPublisher;
        private readonly IPublisher<CoinsGrantedFromAdMessage> _adGrantPublisher;
        private readonly IPublisher<CoinsGrantedFromPurchaseMessage> _purchaseGrantPublisher;
        private readonly IDisposable _gameOverSubscription;
        private readonly IDisposable _coinCellsSubscription;

        /// <summary>
        /// Every transaction id whose coins have already been banked. Held in memory for an O(1) check
        /// on a path that must never miss one, and loaded once in the constructor alongside the three
        /// counters — same lifetime, same single load, same single writer.
        /// </summary>
        private readonly HashSet<string> _consumedTransactionIds = new HashSet<string>();

        public CurrencySystem(
            ProfileModel profileModel,
            ScoreModel scoreModel,
            LevelProgressionModel levelProgressionModel,
            CurrencyConfig config,
            PowerUpPriceConfig priceConfig,
            PowerUpSystem powerUpSystem,
            ICoinRewardSource coinRewardSource,
            ICoinPurchaseService coinPurchaseService,
            IPurchaseReceiptValidator receiptValidator,
            IPublisher<ScoreConvertedToCoinsMessage> convertedPublisher,
            IPublisher<CoinsGrantedFromAdMessage> adGrantPublisher,
            IPublisher<CoinsGrantedFromPurchaseMessage> purchaseGrantPublisher,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<CoinCellsClearedMessage> coinCellsClearedSubscriber)
        {
            _profileModel = profileModel;
            _scoreModel = scoreModel;
            _levelProgressionModel = levelProgressionModel;
            _config = config;
            _priceConfig = priceConfig;
            _powerUpSystem = powerUpSystem;
            _coinRewardSource = coinRewardSource;
            _coinPurchaseService = coinPurchaseService;
            _receiptValidator = receiptValidator;
            _convertedPublisher = convertedPublisher;
            _adGrantPublisher = adGrantPublisher;
            _purchaseGrantPublisher = purchaseGrantPublisher;

            Load();

            // The run's score is only readable while the run is the current one, so the pool has to be
            // topped up on the message that ends it rather than looked up later.
            _gameOverSubscription = gameOverSubscriber.Subscribe(OnGameOver);

            // The third way a coin comes into existence, and the first that is not an explicit player
            // action at a screen: destroying a coin cell. It is still earned rather than granted — the
            // player had to clear the cell — and it is credited here rather than by whichever System
            // resolved the destruction, because this class is the one and only writer of the balance.
            _coinCellsSubscription = coinCellsClearedSubscriber.Subscribe(OnCoinCellsCleared);
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

        /// <summary>
        /// Buys <paramref name="sku"/> for real money and banks the coins it turns out to be worth.
        /// Returns whether coins were credited; every refusal along the way leaves the balance exactly
        /// as it found it.
        /// <para>
        /// Four gates, and no coin is minted until all four are passed. The store has to complete the
        /// purchase; the transaction must not be one already honoured; a validator has to vouch for the
        /// receipt; and the amount it vouches for has to be positive. A dismissal or a store failure
        /// stops at the first (see <see cref="CoinPurchaseOutcome"/> — the caller can tell them apart for
        /// its messaging, but both mean "credit nothing"), and the rest each stop where they are.
        /// </para>
        /// <para>
        /// The amount credited is the validator's, never the SKU's as looked up here. That is the whole
        /// point of the validation seam: the device's claim about what it bought is exactly the thing
        /// being checked, so re-deriving the payout from that claim would make the check ornamental. This
        /// class therefore does not consult <c>CoinBundleConfig</c> at all.
        /// </para>
        /// <para>
        /// The duplicate check runs <em>before</em> validation on purpose. It is a local set lookup and
        /// the validation behind it is a network round trip, so asking the cheap question first costs
        /// nothing; more to the point, an already-honoured receipt has no question left to ask — a
        /// validator that quite correctly approves it a second time must not be allowed to look like
        /// permission to pay twice.
        /// </para>
        /// <para>
        /// Then the ordering that makes the credit atomic, and it is deliberately the same one
        /// <see cref="ConvertScoreToCoins"/> uses rather than the mark-first alternative: record the
        /// transaction as consumed, credit the coins, flush <em>once</em>. Both writes are in-memory
        /// until that single <see cref="PlayerPrefs.Save"/>, so they reach the disk together. A crash
        /// can lose the whole credit or keep the whole credit; it cannot keep a transaction marked as
        /// spent on a balance that never grew. Losing the whole credit is the recoverable half of that
        /// pair — the store has not been told the goods were delivered yet (see the confirmation below),
        /// so it replays the purchase and the player is paid on the retry. Marking first would invert
        /// that into the one unrecoverable state: a receipt burned for coins nobody ever received.
        /// </para>
        /// <para>
        /// The store is acknowledged last, after the coins are on the disk, for that same reason — see
        /// <see cref="ICoinPurchaseService.CompletePurchase"/>. Confirming first would trade a
        /// recoverable failure for a permanent one.
        /// </para>
        /// </summary>
        public async UniTask<bool> PurchaseCoinBundleAsync(string sku, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(sku))
            {
                return false;
            }

            CoinPurchaseResult purchaseResult = await _coinPurchaseService.PurchaseAsync(
                sku, cancellationToken);
            if (!purchaseResult.Succeeded)
            {
                return false;
            }

            PurchaseReceipt receipt = purchaseResult.Receipt;

            // No id means nothing can be deduped, and a credit that cannot be deduped is one a replay
            // would pay again. Refused rather than credited on trust.
            if (string.IsNullOrEmpty(receipt.TransactionId))
            {
                return false;
            }

            if (_consumedTransactionIds.Contains(receipt.TransactionId))
            {
                // Already paid for once. Still acknowledged, so the store stops replaying a transaction
                // this install has already honoured.
                _coinPurchaseService.CompletePurchase(receipt);
                return false;
            }

            ValidatedPurchase validated = await _receiptValidator.ValidateAsync(
                receipt, cancellationToken);
            if (!validated.IsValid || validated.CoinAmount <= 0)
            {
                return false;
            }

            MarkTransactionConsumed(receipt.TransactionId);
            int newBalance = CreditCoins(validated.CoinAmount);

            PlayerPrefs.Save();

            _coinPurchaseService.CompletePurchase(receipt);

            _purchaseGrantPublisher.Publish(new CoinsGrantedFromPurchaseMessage(
                receipt.Sku, validated.CoinAmount, newBalance));
            return true;
        }

        /// <summary>
        /// Coins <paramref name="quantity"/> of <paramref name="kind"/> would cost. What the shop
        /// screen quotes before the player commits, so the price shown and the price charged come from
        /// the same arithmetic rather than two copies of it — the same contract
        /// <see cref="QuoteCoinsFor"/> has with the conversion screen.
        /// <para>
        /// A <c>long</c> because an unpriced kind is priced at <see cref="int.MaxValue"/> (see
        /// <see cref="PowerUpPriceConfig"/>), and multiplying that by a quantity would wrap an
        /// <c>int</c> round to a bargain. Nothing the player can afford ever leaves this range, so the
        /// only caller that sees a figure past <see cref="int.MaxValue"/> is the one about to refuse it.
        /// </para>
        /// </summary>
        public long QuotePriceFor(PowerUpKind kind, int quantity)
            => quantity <= 0 ? 0L : (long)_priceConfig.GetPrice(kind) * quantity;

        /// <summary>
        /// Buys <paramref name="quantity"/> of <paramref name="kind"/> with coins: debits the balance
        /// and grants the power-ups, or refuses and changes nothing at all. The returned
        /// <see cref="PowerUpPurchaseResult"/> says which, and on a refusal says why — there is no
        /// silent no-op here, because every refusal is something the shop has to tell the player.
        /// <para>
        /// Three guards, cheapest and most obviously-wrong first, and all three before a single
        /// mutation. An empty ask cannot come from a player and is rejected without consulting
        /// anything; the level gate is one comparison against a number already in hand; only then is
        /// the price looked up and weighed against the balance. Ordering them this way also means the
        /// answer the shop shows is the most specific true one: a locked kind reads as locked rather
        /// than as unaffordable, even when the player could not have afforded it either.
        /// </para>
        /// <para>
        /// The gate is checked through <see cref="PowerUpUnlockLevels.IsUnlockedAt"/>, the same public
        /// seam <see cref="PowerUpSystem"/> reads it through and the same frontier
        /// (<see cref="LevelProgressionModel.CurrentLevelNumber"/>) — so a kind the player has not
        /// reached cannot be bought however many coins they hold. Currency is not a key: it buys a
        /// power-up the player has already unlocked, and nothing else.
        /// </para>
        /// <para>
        /// The over-ask is refused rather than clamped down to what the balance covers, for the reason
        /// <see cref="ConvertScoreToCoins"/> refuses its own: the screen is built from the balance and
        /// the price, so an unaffordable ask is a bug somewhere, and quietly buying a different
        /// quantity than the caller named would hide it.
        /// </para>
        /// <para>
        /// Then the ordering that makes a purchase atomic: debit, grant, flush — once, at the end.
        /// Both halves write PlayerPrefs in memory only (this one directly, the grant through
        /// <see cref="PowerUpSystem.GrantPurchased"/>, which persists and deliberately does not flush),
        /// so the single <see cref="PlayerPrefs.Save"/> below is what puts them on the disk together. A
        /// crash can lose the whole purchase or keep the whole purchase; it cannot take the coins
        /// without handing over the power-ups, or the other way round.
        /// </para>
        /// </summary>
        public PowerUpPurchaseResult TryPurchasePowerUp(PowerUpKind kind, int quantity)
        {
            if (quantity <= 0)
            {
                return PowerUpPurchaseResult.InvalidQuantity;
            }

            if (!PowerUpUnlockLevels.IsUnlockedAt(kind, _levelProgressionModel.CurrentLevelNumber.Value))
            {
                return PowerUpPurchaseResult.Locked;
            }

            long totalPrice = QuotePriceFor(kind, quantity);
            if (totalPrice > _profileModel.CoinBalance.Value)
            {
                return PowerUpPurchaseResult.InsufficientCoins;
            }

            // Safe to narrow: the check above already proved this is no larger than the balance, which
            // is an int.
            int newBalance = _profileModel.CoinBalance.Value - (int)totalPrice;
            _profileModel.CoinBalance.Value = newBalance;
            PlayerPrefs.SetInt(COIN_BALANCE_KEY, newBalance);

            // Charged first, then handed over: the debit is this class's own write and the grant is
            // PowerUpSystem's, so paying before delivering is what keeps a granted power-up from ever
            // existing without the coins for it having left the balance.
            _powerUpSystem.GrantPurchased(kind, quantity);

            PlayerPrefs.Save();

            return PowerUpPurchaseResult.Success;
        }

        public void Dispose()
        {
            _gameOverSubscription.Dispose();
            _coinCellsSubscription.Dispose();
        }

        /// <summary>
        /// Banks the coins a resolution's destroyed coin cells earned. Goes through the same
        /// <see cref="CreditCoins"/> the other two faucets do, then flushes — one write, so one flush,
        /// exactly as the ad grant does.
        /// <para>
        /// A non-positive total is a no-op. The publishers only announce a real payout, so this cannot
        /// legitimately arrive; crediting it anyway would mean a zero-coin "earn" writing PlayerPrefs on
        /// every placement, and a negative one would silently fine the player.
        /// </para>
        /// <para>
        /// Takes nothing out of the convertible pool and does not touch it, exactly as an ad grant does
        /// not: the player has not sold any score here, they have cleared a coin cell.
        /// </para>
        /// </summary>
        private void OnCoinCellsCleared(CoinCellsClearedMessage message)
        {
            if (message.TotalCoins <= 0)
            {
                return;
            }

            CreditCoins(message.TotalCoins);
            PlayerPrefs.Save();
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
        /// Records <paramref name="transactionId"/> as honoured, in memory and in PlayerPrefs.
        /// Deliberately does not flush, for the reason <see cref="CreditCoins"/> does not: the credit
        /// beside it is the other half of one outcome, and the single flush belongs to the caller that
        /// knows how many writes its outcome is made of.
        /// <para>
        /// The whole list is rewritten on each consumption rather than appended to, because PlayerPrefs
        /// offers no append. It is a handful of tokens for a handful of lifetime purchases, written only
        /// when one completes, so the cost is irrelevant and the alternative — a second key holding a
        /// count, kept in step with a growing string — would be a second thing to get wrong.
        /// </para>
        /// </summary>
        private void MarkTransactionConsumed(string transactionId)
        {
            _consumedTransactionIds.Add(transactionId);

            var builder = new StringBuilder();
            foreach (string consumedId in _consumedTransactionIds)
            {
                if (builder.Length > 0)
                {
                    builder.Append(CONSUMED_ID_SEPARATOR);
                }

                builder.Append(consumedId);
            }

            PlayerPrefs.SetString(CONSUMED_TRANSACTION_IDS_KEY, builder.ToString());
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

            LoadConsumedTransactionIds();
        }

        /// <summary>
        /// Reads back the transaction ids this install has already been paid for. Loaded here, with the
        /// counters, because a purchase credit is as much this class's bookkeeping as the balance is —
        /// and because a set that loaded late, or not at all, would silently turn every replayed receipt
        /// into a second payout.
        /// <para>
        /// Empty entries are skipped rather than trusted: a missing key reads as an empty string, a
        /// trailing separator would split into one, and an empty id would match the guard in
        /// <see cref="PurchaseCoinBundleAsync"/> that already refuses receipts without one.
        /// </para>
        /// </summary>
        private void LoadConsumedTransactionIds()
        {
            _consumedTransactionIds.Clear();

            string persisted = PlayerPrefs.GetString(CONSUMED_TRANSACTION_IDS_KEY, string.Empty);
            if (string.IsNullOrEmpty(persisted))
            {
                return;
            }

            string[] ids = persisted.Split(CONSUMED_ID_SEPARATOR);
            for (int idIndex = 0; idIndex < ids.Length; idIndex++)
            {
                if (!string.IsNullOrEmpty(ids[idIndex]))
                {
                    _consumedTransactionIds.Add(ids[idIndex]);
                }
            }
        }
    }
}
