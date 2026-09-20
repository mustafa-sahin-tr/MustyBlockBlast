namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// How one real-money purchase attempt ended (see <see cref="CoinPurchaseResult"/>). Its own type
    /// rather than a <c>bool</c>, and its own file, for the reason
    /// <see cref="PowerUpPurchaseResult"/> has both: the shop has to say *why* nothing was bought, and
    /// "you backed out" and "the store could not complete this" are two different sentences to the
    /// player — one of which is not a problem at all.
    /// <para>
    /// Every member but <see cref="Succeeded"/> credits nothing: no coins, no consumed transaction, no
    /// message. There is no partial outcome, because there is no partial purchase.
    /// </para>
    /// </summary>
    public enum CoinPurchaseOutcome
    {
        /// <summary>The store took the money and handed back a receipt. Whether that receipt is *worth*
        /// anything is still <see cref="IPurchaseReceiptValidator"/>'s answer, not this one.</summary>
        Succeeded,

        /// <summary>The player dismissed the store's own prompt. Not an error, and nothing to report to
        /// them — they already know they did it.</summary>
        Cancelled,

        /// <summary>Anything else: no store connection, an unknown SKU, a declined payment, a
        /// transaction the store refused. All one member because the player's next step is the same for
        /// all of them — try again later.</summary>
        Failed,
    }
}
