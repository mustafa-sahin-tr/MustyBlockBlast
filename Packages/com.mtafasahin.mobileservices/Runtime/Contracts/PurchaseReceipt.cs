namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// What a store hands back when a real-money purchase goes through: which product was bought, the
    /// store's own id for that one transaction, and the signed blob a validator can check it against.
    /// <para>
    /// Deliberately three plain strings and no store type at all. This is the return value of
    /// <see cref="ICoinPurchaseService"/>, so anything richer would drag the purchasing SDK across the
    /// seam that interface exists to hold — and the two things the coin economy actually does with a
    /// purchase (dedupe it by transaction, hand it to a validator) need nothing more than this.
    /// </para>
    /// </summary>
    public readonly struct PurchaseReceipt
    {
        public PurchaseReceipt(string transactionId, string sku, string rawReceiptJson)
        {
            TransactionId = transactionId ?? string.Empty;
            Sku = sku ?? string.Empty;
            RawReceiptJson = rawReceiptJson ?? string.Empty;
        }

        /// <summary>
        /// The store's identifier for this one transaction, and the key the coin economy dedupes on —
        /// see <see cref="CurrencySystem.PurchaseCoinBundleAsync"/>. Two credits of the same id are the
        /// same purchase, however many times the store replays it.
        /// <para>
        /// Empty rather than null on a default-constructed receipt, for the reason
        /// <see cref="IAuthService.PlayerId"/> is empty rather than null: callers should never have to
        /// null-check a string. An empty id is still not a usable one, and the credit path refuses it.
        /// </para>
        /// </summary>
        public string TransactionId { get; }

        /// <summary>Which bundle was bought, as the SKU authored in <c>CoinBundleConfig</c>.</summary>
        public string Sku { get; }

        /// <summary>
        /// The store receipt exactly as the store gave it, for a validator to check the signature of.
        /// Opaque here on purpose: only <see cref="IPurchaseReceiptValidator"/>'s implementation has any
        /// business reading it, and a real one reads it on a server rather than on the device.
        /// </summary>
        public string RawReceiptJson { get; }
    }
}
