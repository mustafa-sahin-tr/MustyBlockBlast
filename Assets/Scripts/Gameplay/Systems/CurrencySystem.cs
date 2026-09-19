using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;

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
    /// never at the end of a run. There are exactly four ways one comes into existence, and all four
    /// are something the player did — a conversion, a watched rewarded ad, a destroyed
    /// <see cref="MustyBlockBlast.Core.SpecialCellKind.Coin"/> cell, or a claimed badge (see
    /// <see cref="CreditBadgeReward"/>). The third is the only one that happens mid-run, and it is
    /// still earned: the coin cell had to be cleared, and one left standing when the run ends pays
    /// nothing.
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
        /// The two keys behind the daily coin-ad cap (issue #257). Named in the same "Profile.*" family
        /// as every other key above even though the value they hold is deliberately device-local rather
        /// than account-bound (see <see cref="DailyAdGrantModel"/>) — the prefix is this project's flat
        /// PlayerPrefs namespace for "state this class owns", not a claim about what carries across a
        /// linked-account migration.
        /// </summary>
        private const string DAILY_AD_GRANTS_REMAINING_KEY = "Profile.DailyAdGrantsRemaining";
        private const string DAILY_AD_GRANT_DAY_MARKER_KEY = "Profile.DailyAdGrantDayMarker";

        /// <summary>Format the local day marker is compared and stored in: sortable, unambiguous across
        /// locales, and stable regardless of the device's date-format setting.</summary>
        private const string DAY_MARKER_FORMAT = "yyyyMMdd";

        /// <summary>
        /// Separator for the persisted transaction-id list. A newline because store transaction ids are
        /// opaque single-line tokens — Apple's are numeric, Google's are base64url purchase tokens — so
        /// none of them can contain one, which is what lets a flat string stand in for a set without a
        /// JSON dependency this class has never needed for the three counters beside it.
        /// </summary>
        private const char CONSUMED_ID_SEPARATOR = '\n';

        private readonly ProfileModel _profileModel;
        private readonly DailyAdGrantModel _dailyAdGrantModel;
        private readonly CoinBundlePriceModel _coinBundlePriceModel;
        private readonly ScoreModel _scoreModel;
        private readonly LevelProgressionModel _levelProgressionModel;
        private readonly CurrencyConfig _config;
        private readonly PowerUpPriceConfig _priceConfig;
        private readonly PromotionConfig _promotionConfig;
        private readonly PowerUpSystem _powerUpSystem;
        private readonly ICoinRewardSource _coinRewardSource;
        private readonly ICoinPurchaseService _coinPurchaseService;
        private readonly IPurchaseReceiptValidator _receiptValidator;
        private readonly IPublisher<ScoreConvertedToCoinsMessage> _convertedPublisher;
        private readonly IPublisher<CoinsGrantedFromAdMessage> _adGrantPublisher;
        private readonly IPublisher<CoinsGrantedFromPurchaseMessage> _purchaseGrantPublisher;
        private readonly IDisposable _gameOverSubscription;
        private readonly IDisposable _coinCellsSubscription;
        private readonly IDisposable _coinProductsFetchedSubscription;

        /// <summary>
        /// Set once the coin catalog's connect-and-fetch has been asked for (issue #256), and never
        /// cleared. What makes <see cref="WarmUpCoinCatalog"/> idempotent for the Coins tab's own promise
        /// — "reopening does not produce a new fetch call" — independently of whatever caching
        /// <see cref="ICoinPurchaseService"/>'s own implementation happens to do underneath it.
        /// </summary>
        private bool _coinCatalogWarmUpStarted;

        /// <summary>
        /// Every transaction id whose coins have already been banked. Held in memory for an O(1) check
        /// on a path that must never miss one, and loaded once in the constructor alongside the three
        /// counters — same lifetime, same single load, same single writer.
        /// </summary>
        private readonly HashSet<string> _consumedTransactionIds = new HashSet<string>();

        /// <summary>
        /// Where "now" comes from when a promotion window is checked. Behind a delegate for the reason
        /// <see cref="LaserSpawnSystem"/>'s random seed is: a date-driven price is untestable against a
        /// clock nobody can move, and a test that had to wait for a window to open would be no test at
        /// all. The public constructor wires the real clock; only a test reaches the seeded overload.
        /// </summary>
        private readonly Func<DateTime> _utcNowProvider;

        /// <summary>
        /// Where "today" comes from when the daily ad cap's rollover is checked. A second, separate
        /// delegate from <see cref="_utcNowProvider"/> rather than a conversion of it, for the reason the
        /// two are decided differently in the issue: the promotion window is one UTC instant worldwide,
        /// but the daily ad cap resets on the device's own calendar day (issue #257) — converting a
        /// seeded UTC instant with <see cref="DateTime.ToLocalTime"/> would make every day-rollover test
        /// depend on the timezone of whatever machine runs it. Behind a delegate for the same reason the
        /// UTC one is: production supplies the real local clock, only a test reaches the seeded overload.
        /// </summary>
        private readonly Func<DateTime> _localNowProvider;

        /// <summary>
        /// DI entry point. Explicitly marked because VContainer, absent an <see cref="InjectAttribute"/>,
        /// resolves the constructor with the most parameters — which is the seeded-clock one below, not
        /// this one, now that it carries an extra <see cref="Func{DateTime}"/> parameter. Without this
        /// attribute VContainer silently picks that constructor instead and fails to resolve it (nothing
        /// registers a bare <see cref="Func{DateTime}"/>), breaking this System — and everything that
        /// depends on it — at runtime despite every EditMode test passing, since tests construct this
        /// class directly rather than through the container.
        /// </summary>
        [Inject]
        public CurrencySystem(
            ProfileModel profileModel,
            DailyAdGrantModel dailyAdGrantModel,
            CoinBundlePriceModel coinBundlePriceModel,
            ScoreModel scoreModel,
            LevelProgressionModel levelProgressionModel,
            CurrencyConfig config,
            PowerUpPriceConfig priceConfig,
            PromotionConfig promotionConfig,
            PowerUpSystem powerUpSystem,
            ICoinRewardSource coinRewardSource,
            ICoinPurchaseService coinPurchaseService,
            IPurchaseReceiptValidator receiptValidator,
            IPublisher<ScoreConvertedToCoinsMessage> convertedPublisher,
            IPublisher<CoinsGrantedFromAdMessage> adGrantPublisher,
            IPublisher<CoinsGrantedFromPurchaseMessage> purchaseGrantPublisher,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<CoinCellsClearedMessage> coinCellsClearedSubscriber,
            ISubscriber<CoinProductsFetchedMessage> coinProductsFetchedSubscriber)
            : this(
                profileModel,
                dailyAdGrantModel,
                coinBundlePriceModel,
                scoreModel,
                levelProgressionModel,
                config,
                priceConfig,
                promotionConfig,
                powerUpSystem,
                coinRewardSource,
                coinPurchaseService,
                receiptValidator,
                convertedPublisher,
                adGrantPublisher,
                purchaseGrantPublisher,
                gameOverSubscriber,
                coinCellsClearedSubscriber,
                coinProductsFetchedSubscriber,
                UtcNow,
                LocalNow)
        {
        }

        internal CurrencySystem(
            ProfileModel profileModel,
            DailyAdGrantModel dailyAdGrantModel,
            CoinBundlePriceModel coinBundlePriceModel,
            ScoreModel scoreModel,
            LevelProgressionModel levelProgressionModel,
            CurrencyConfig config,
            PowerUpPriceConfig priceConfig,
            PromotionConfig promotionConfig,
            PowerUpSystem powerUpSystem,
            ICoinRewardSource coinRewardSource,
            ICoinPurchaseService coinPurchaseService,
            IPurchaseReceiptValidator receiptValidator,
            IPublisher<ScoreConvertedToCoinsMessage> convertedPublisher,
            IPublisher<CoinsGrantedFromAdMessage> adGrantPublisher,
            IPublisher<CoinsGrantedFromPurchaseMessage> purchaseGrantPublisher,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<CoinCellsClearedMessage> coinCellsClearedSubscriber,
            ISubscriber<CoinProductsFetchedMessage> coinProductsFetchedSubscriber,
            Func<DateTime> utcNowProvider,
            Func<DateTime> localNowProvider = null)
        {
            _promotionConfig = promotionConfig;
            _utcNowProvider = utcNowProvider ?? UtcNow;
            _localNowProvider = localNowProvider ?? LocalNow;
            _profileModel = profileModel;
            _dailyAdGrantModel = dailyAdGrantModel;
            _coinBundlePriceModel = coinBundlePriceModel;
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

            // The store's answer to a catalog fetch (issue #256), published by whichever
            // ICoinPurchaseService implementation is bound — the Coins tab's rows have nothing to show
            // until this fires at least once.
            _coinProductsFetchedSubscription = coinProductsFetchedSubscriber.Subscribe(OnCoinProductsFetched);
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
        /// Coin-rewarding ads still available today (issue #257). The Coins tab's read-only window onto
        /// <see cref="DailyAdGrantModel.RemainingToday"/> — this class is the property's only writer, by
        /// way of <see cref="EnsureDailyAdCounterCurrent"/> and <see cref="GrantCoinsFromAdAsync"/>.
        /// <para>
        /// The getter itself rolls the counter over when the stored day has passed, rather than leaving
        /// that to whichever caller happens to ask first. That is what lets a tab reopened after midnight
        /// read the fresh cap without an app restart: every read through this property — including the
        /// Coins tab's own repaint — is a chance to notice the day changed, not just the one at launch.
        /// </para>
        /// </summary>
        public ReactiveProperty<int> RemainingAdGrantsToday
        {
            get
            {
                EnsureDailyAdCounterCurrent();
                return _dailyAdGrantModel.RemainingToday;
            }
        }

        /// <summary>
        /// SKU to the store's localized price string (issue #256). The Coins tab's read-only window onto
        /// <see cref="CoinBundlePriceModel.SkuToLocalizedPrice"/> — this class is the property's only
        /// writer, through <see cref="OnCoinProductsFetched"/>. Empty until the first successful catalog
        /// fetch; a bundle row whose SKU is missing from it shows the plain "BUY" its button always used
        /// to, which is exactly the fallback this property's emptiness is meant to produce.
        /// </summary>
        public ReactiveProperty<IReadOnlyDictionary<string, string>> CoinBundlePrices
            => _coinBundlePriceModel.SkuToLocalizedPrice;

        /// <summary>
        /// Connects to the store and fetches its catalog, once per this System's lifetime (issue #256,
        /// AC1). The Coins tab's own entry point for "show me a price without buying anything": it calls
        /// this on every open, and every call after the first is a no-op, which is what makes reopening
        /// the tab free.
        /// <para>
        /// Idempotent by a plain flag here rather than by trusting <see cref="ICoinPurchaseService"/>'s
        /// own connect-once cache alone — that cache exists too (see
        /// <see cref="ICoinPurchaseService.EnsureReadyAsync"/>'s docs) but is the implementation's detail
        /// to keep or drop, not a contract this System's own "no duplicate fetch" promise should lean on.
        /// A guard at this end means the promise holds even against an implementation that reconnects
        /// every time it is asked.
        /// </para>
        /// <para>
        /// Deliberately does not retry a failed attempt on a later tab open: the flag is set before the
        /// connect is even awaited and never cleared, on the same "idempotent means idempotent" reading
        /// AC1 asks for. A purchase attempt is unaffected either way — <see cref="PurchaseCoinBundleAsync"/>
        /// goes through <see cref="ICoinPurchaseService.PurchaseAsync"/>, which has always reconnected on
        /// demand on its own and continues to.
        /// </para>
        /// <para>
        /// Fire-and-forget from the caller's point of view — there is nothing to await that a repaint
        /// needs, since the price itself arrives later on <see cref="CoinProductsFetchedMessage"/> and
        /// repaints the Coins tab through <see cref="CoinBundlePrices"/>'s subscription instead.
        /// </para>
        /// </summary>
        public void WarmUpCoinCatalog(CancellationToken cancellationToken)
        {
            if (_coinCatalogWarmUpStarted)
            {
                return;
            }

            _coinCatalogWarmUpStarted = true;
            WarmUpCoinCatalogAsync(cancellationToken).Forget();
        }

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
        /// <para>
        /// Gated by the daily coin-ad cap (issue #257) before <see cref="ICoinRewardSource"/> is ever
        /// asked: once <see cref="RemainingAdGrantsToday"/> reads zero for today, this method returns
        /// <c>false</c> without requesting a reward at all — not a request that is granted and then
        /// discarded. A successful grant decrements the counter and persists it, in the same
        /// <see cref="PlayerPrefs.Save"/> as the coin credit, so a crash between the two can lose the
        /// whole grant or keep the whole grant, never bank the coins without spending the allowance.
        /// </para>
        /// </summary>
        public async UniTask<bool> GrantCoinsFromAdAsync(int amount, CancellationToken cancellationToken)
        {
            if (amount <= 0)
            {
                return false;
            }

            EnsureDailyAdCounterCurrent();
            if (_dailyAdGrantModel.RemainingToday.Value <= 0)
            {
                return false;
            }

            CoinRewardResult result = await _coinRewardSource.RequestCoinRewardAsync(amount, cancellationToken);
            if (!result.Granted || result.Amount <= 0)
            {
                return false;
            }

            int newBalance = CreditCoins(result.Amount);

            int newRemaining = _dailyAdGrantModel.RemainingToday.Value - 1;
            _dailyAdGrantModel.RemainingToday.Value = newRemaining;
            PlayerPrefs.SetInt(DAILY_AD_GRANTS_REMAINING_KEY, newRemaining);
            PlayerPrefs.SetString(DAILY_AD_GRANT_DAY_MARKER_KEY, _dailyAdGrantModel.DayMarker.Value);

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
        /// <para>
        /// Any live campaign in <see cref="PromotionConfig"/> is applied <em>here</em>, and deliberately
        /// nowhere else. This method is already the one place the shop quotes through and the one place
        /// <see cref="TryPurchasePowerUp"/> reads the figure it charges, so discounting it discounts both
        /// at once: there is no arrangement of a sale's start and end in which the price on the row and
        /// the price taken off the balance could disagree, because there is only one calculation. The
        /// alternative — a discounted quote beside an undiscounted charge — is the one bug in a
        /// promotion system a player would certainly notice and never forgive.
        /// </para>
        /// <para>
        /// Which campaign applies, and the fact that two overlapping ones do not stack, is
        /// <see cref="PromotionConfig.GetActiveDiscountPercent"/>'s decision and not re-litigated here:
        /// this method asks for one percentage and takes it. The window is checked against the device's
        /// UTC clock on every quote rather than latched at construction, so a sale that ends while the
        /// shop is open reverts on the next repaint, and a campaign is over when its end date says so
        /// with no code change and no restart.
        /// </para>
        /// <para>
        /// The discounted figure is rounded <em>down</em>, which is to say in the player's favour. The
        /// mirror of <see cref="QuoteCoinsFor"/>, which rounds down in the game's favour for the same
        /// underlying reason: rounding must never work against the party the arithmetic was advertised
        /// to. A row that says "33% off 50" and charges 34 is off by a coin in the one direction a
        /// player would rightly call a lie, so the price paid is the remaining fraction floored (33) and
        /// never the base less a floored discount (34).
        /// <para>
        /// Taken off the line total rather than off each unit, and the multiply happens before the
        /// divide, so the remainder is discarded exactly once however many are bought: 33% off three
        /// 50-coin power-ups is 100, the floor of the real 100.5, rather than three separately-floored
        /// 33s. The figure shown is therefore the honest reading of "33% off 150" and cannot drift
        /// further from it as the quantity climbs, which repeated per-unit rounding would.
        /// </para>
        /// </para>
        /// </summary>
        public long QuotePriceFor(PowerUpKind kind, int quantity)
        {
            if (quantity <= 0)
            {
                return 0L;
            }

            long basePrice = (long)_priceConfig.GetPrice(kind) * quantity;

            int discountPercent = _promotionConfig == null
                ? 0
                : _promotionConfig.GetActiveDiscountPercent(kind, _utcNowProvider());
            if (discountPercent <= 0)
            {
                return basePrice;
            }

            // The remaining fraction, not the base less the discount. The two differ by a coin whenever
            // the percentage does not divide the total, and only this one rounds the *price* down: the
            // other floors the discount, which quietly rounds the price up.
            return basePrice * (100 - discountPercent) / 100;
        }

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
        /// The price is read through <see cref="QuotePriceFor"/> and nowhere else, which is what makes a
        /// live promotion charge what the shop advertised: the discount lives in that one method, so this
        /// path inherits it without knowing a campaign exists. There is deliberately no second lookup of
        /// <see cref="PowerUpPriceConfig.GetPrice"/> here — a charge computed from the base price beside
        /// a quote computed from the discounted one is exactly the disagreement the single seam prevents.
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

        /// <summary>
        /// Banks the coins a claimed badge pays. Called by <see cref="BadgeSystem.ClaimReward"/> and by
        /// nothing else: the badge System decides whether a claim is due (unlocked, unclaimed, worth
        /// something) and this class only mints, because it is the one and only writer of the balance.
        /// <para>
        /// Credits and flushes in one call, so the coins are on the disk before the caller records the
        /// claim — the ordering that lets a crash between the two at worst pay once more, never leave a
        /// claimed badge unpaid. A non-positive amount is a no-op, as every other faucet's is.
        /// </para>
        /// <para>
        /// Takes nothing out of the convertible pool, exactly as an ad grant does not: the player has
        /// not sold any score here, they have earned a badge.
        /// </para>
        /// </summary>
        public void CreditBadgeReward(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            CreditCoins(amount);
            PlayerPrefs.Save();
        }

        public void Dispose()
        {
            _gameOverSubscription.Dispose();
            _coinCellsSubscription.Dispose();
            _coinProductsFetchedSubscription.Dispose();
        }

        /// <summary>The async body behind <see cref="WarmUpCoinCatalog"/>'s fire-and-forget call. Its own
        /// method rather than an inline lambda for the same reason every other <c>UniTaskVoid</c> here is
        /// — an <c>async</c> lambda assigned to <c>.Forget()</c> reads no differently, but a named method
        /// is what lets this file's other fire-and-forget callers (<see cref="PowerUpShopView"/>'s own)
        /// be compared against a consistent shape.</summary>
        private async UniTaskVoid WarmUpCoinCatalogAsync(CancellationToken cancellationToken)
        {
            await _coinPurchaseService.EnsureReadyAsync(cancellationToken);
        }

        /// <summary>
        /// Replaces <see cref="CoinBundlePriceModel.SkuToLocalizedPrice"/> wholesale with what the store
        /// just answered (issue #256). The one and only writer of that table, exactly as
        /// <see cref="CreditCoins"/> is the one and only writer of the balance — and, like every other
        /// mutator here, unconditional: a message that arrived at all is a fetch that returned at least
        /// one priced SKU (see <c>UnityCoinPurchaseService.PublishFetchedPrices</c>), so there is nothing
        /// to validate before writing it.
        /// <para>
        /// Not persisted. A price is what the store said this session, not player state — a relaunch
        /// re-fetches it the same way <see cref="WarmUpCoinCatalog"/> asked for it the first time, and a
        /// stale price surviving a restart would be worse than the plain "BUY" the tab shows until the
        /// fresh fetch lands.
        /// </para>
        /// </summary>
        private void OnCoinProductsFetched(CoinProductsFetchedMessage message)
        {
            _coinBundlePriceModel.SkuToLocalizedPrice.Value = message.SkuToLocalizedPrice;
        }

        /// <summary>
        /// The real clock, behind a named method rather than an inline lambda so the public constructor's
        /// default is one readable thing and the delegate it allocates is created once per system rather
        /// than captured from anywhere. UTC because <see cref="PromotionConfig"/> compares UTC instants —
        /// a campaign's window is one moment worldwide, not one per timezone.
        /// </summary>
        private static DateTime UtcNow() => DateTime.UtcNow;

        /// <summary>The real device clock, in local time. Behind a named method for the reason
        /// <see cref="UtcNow"/> is — see <see cref="_localNowProvider"/> for why this is a second
        /// delegate rather than a conversion of the UTC one.</summary>
        private static DateTime LocalNow() => DateTime.Now;

        /// <summary>
        /// Rolls <see cref="DailyAdGrantModel.RemainingToday"/> over to a fresh
        /// <see cref="CurrencyConfig.DailyAdRewardCap"/> the moment the stored day marker stops matching
        /// today — issue #257's AC4. Called from every path that reads or spends the counter
        /// (<see cref="RemainingAdGrantsToday"/>'s getter and <see cref="GrantCoinsFromAdAsync"/>) rather
        /// than only at construction, which is what lets a shop tab reopened after midnight show the
        /// fresh cap without the app having restarted in between.
        /// <para>
        /// Deliberately does not flush to PlayerPrefs itself. The reset is idempotent — recomputed from
        /// the stored marker on every call — so an in-memory rollover that is never followed by a grant
        /// is safely rediscovered the same way next launch; only <see cref="GrantCoinsFromAdAsync"/>'s
        /// own save needs to reach the disk, and it always persists whatever day marker this method last
        /// settled on.
        /// </para>
        /// </summary>
        private void EnsureDailyAdCounterCurrent()
        {
            string today = _localNowProvider().ToString(DAY_MARKER_FORMAT);
            if (_dailyAdGrantModel.DayMarker.Value == today)
            {
                return;
            }

            _dailyAdGrantModel.DayMarker.Value = today;
            _dailyAdGrantModel.RemainingToday.Value = _config.DailyAdRewardCap;
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
            LoadDailyAdGrantCounter();
        }

        /// <summary>
        /// Reads the daily ad cap's two keys back as-is, with no rollover decided here: a save from
        /// yesterday is loaded verbatim and only turned stale by <see cref="EnsureDailyAdCounterCurrent"/>
        /// on the next read, exactly as issue #257's AC4 asks for. Clamped to the configured cap rather
        /// than trusted outright, so a save written under a higher cap that was since retuned down can
        /// never leave more remaining today than the current config allows.
        /// </summary>
        private void LoadDailyAdGrantCounter()
        {
            _dailyAdGrantModel.DayMarker.Value = PlayerPrefs.GetString(DAILY_AD_GRANT_DAY_MARKER_KEY, string.Empty);
            _dailyAdGrantModel.RemainingToday.Value = Mathf.Clamp(
                PlayerPrefs.GetInt(DAILY_AD_GRANTS_REMAINING_KEY, _config.DailyAdRewardCap),
                0,
                _config.DailyAdRewardCap);
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
