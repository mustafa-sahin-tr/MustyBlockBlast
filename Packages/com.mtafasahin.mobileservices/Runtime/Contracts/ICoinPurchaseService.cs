using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// Seam between the coin economy and whatever store takes real money for a coin bundle, kept free of
    /// any purchasing-SDK type for the reason <see cref="IRewardSource"/> and
    /// <see cref="ICoinRewardSource"/> are kept free of an ad-SDK one — and rather more urgently than
    /// either, because this is the one seam behind which actual money moves. Nothing in
    /// <c>Gameplay</c> or <c>Core</c> may reference the SDK; exactly one type in <c>Presentation</c>
    /// does (see <c>UnityCoinPurchaseService</c>).
    /// <para>
    /// A parallel seam rather than a widening of <see cref="ICoinRewardSource"/>, on the same reasoning
    /// that split that one off <see cref="IRewardSource"/>: an ad grant and a purchase pay the same
    /// currency but they are not the same offer and they do not fail the same way. A declined ad costs
    /// the player nothing and needs no receipt; a declined card charge is a store-level failure, and a
    /// successful charge produces a receipt that has to be validated and then deduped for the rest of
    /// the install's life. Folding the two together would leave every implementation answering
    /// questions about a transaction it never had.
    /// </para>
    /// <para>
    /// No initialization method. Connecting to a store and fetching its products is the implementation's
    /// own bookkeeping, and an <c>InitializeAsync</c> here would be an interface member with no caller
    /// in this codebase — the same speculative surface the encapsulation rules forbid. Implementations
    /// are expected to make <see cref="PurchaseAsync"/> connect on demand and idempotently, so a caller
    /// has exactly one thing it can do and exactly one order to do it in.
    /// </para>
    /// </summary>
    public interface ICoinPurchaseService
    {
        /// <summary>
        /// Asks the store to sell <paramref name="sku"/>, completing when the player has either paid or
        /// not. Never throws for a refusal: a dismissal and a store failure are both ordinary outcomes
        /// reported through <see cref="CoinPurchaseResult"/>, because neither is a bug and both have to
        /// reach the player as a sentence rather than an exception.
        /// <para>
        /// A success means only that money moved and a receipt exists. It is emphatically *not* a
        /// licence to credit coins — that needs <see cref="IPurchaseReceiptValidator"/>'s verdict and a
        /// dedupe check first, both of which <see cref="CurrencySystem.PurchaseCoinBundleAsync"/> owns.
        /// </para>
        /// </summary>
        UniTask<CoinPurchaseResult> PurchaseAsync(string sku, CancellationToken cancellationToken);

        /// <summary>
        /// Connects to the store and fetches its catalog, idempotently — the very same connect-and-fetch
        /// <see cref="PurchaseAsync"/> already triggers on demand, exposed here so a caller that only
        /// wants to warm the connection (the Coins tab opening, issue #256) is not forced to attempt a
        /// purchase just to see a price. Returns whether the store is now ready; nothing here is a
        /// licence to sell — <see cref="PurchaseAsync"/> is still the only way to buy anything.
        /// <para>
        /// Implementations are expected to cache a successful connection so a second caller — whether
        /// this method again or <see cref="PurchaseAsync"/> — reuses it rather than reconnecting, exactly
        /// as the type's own class docs already promise for <see cref="PurchaseAsync"/>'s on-demand
        /// connect. This member only gives that promise a name callers who are not buying anything can
        /// reach.
        /// </para>
        /// </summary>
        UniTask<bool> EnsureReadyAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Tells the store the goods have been handed over, so it stops replaying the transaction and
        /// (on a consumable) lets the player buy the same bundle again.
        /// <para>
        /// Separate from <see cref="PurchaseAsync"/>, and called only *after* the coins are credited and
        /// flushed, which is the whole point of splitting it out: a store told "delivered" before the
        /// coins reach the disk would never replay a purchase a crash swallowed, and the player would be
        /// out of pocket with no trace. Confirming last makes that window recoverable instead, and the
        /// consumed-transaction set behind <see cref="CurrencySystem.PurchaseCoinBundleAsync"/> is what
        /// keeps the replay from paying twice.
        /// </para>
        /// <para>
        /// Synchronous and void because acknowledging is fire-and-forget: there is nothing a caller could
        /// usefully do about a failed acknowledgement that the store's own replay does not already do
        /// better.
        /// </para>
        /// </summary>
        void CompletePurchase(PurchaseReceipt receipt);
    }
}
