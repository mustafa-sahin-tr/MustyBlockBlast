namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Outcome of one real-money purchase attempt: what the store did, and — only when it did take the
    /// money — the receipt it handed back.
    /// <para>
    /// The receipt is left at its default on every refusal rather than being made nullable, so there is
    /// no second way to ask "did this succeed": <see cref="Succeeded"/> is the one answer, and a caller
    /// that reads <see cref="Receipt"/> without checking it finds an empty transaction id the credit
    /// path refuses anyway.
    /// </para>
    /// </summary>
    public readonly struct CoinPurchaseResult
    {
        private CoinPurchaseResult(CoinPurchaseOutcome outcome, PurchaseReceipt receipt)
        {
            Outcome = outcome;
            Receipt = receipt;
        }

        /// <summary>What the store did. Carried so the caller can tell a dismissal from a failure
        /// without a second call — see <see cref="CoinPurchaseOutcome"/>.</summary>
        public CoinPurchaseOutcome Outcome { get; }

        /// <summary>The store's receipt, and meaningful only while <see cref="Succeeded"/>.</summary>
        public PurchaseReceipt Receipt { get; }

        public bool Succeeded => Outcome == CoinPurchaseOutcome.Succeeded;

        /// <summary>The store completed the purchase and gave up <paramref name="receipt"/>.</summary>
        public static CoinPurchaseResult FromReceipt(PurchaseReceipt receipt)
            => new CoinPurchaseResult(CoinPurchaseOutcome.Succeeded, receipt);

        /// <summary>The player dismissed the store prompt.</summary>
        public static CoinPurchaseResult Cancelled
            => new CoinPurchaseResult(CoinPurchaseOutcome.Cancelled, default);

        /// <summary>The purchase could not be completed, for any reason that is not a dismissal.</summary>
        public static CoinPurchaseResult Failed
            => new CoinPurchaseResult(CoinPurchaseOutcome.Failed, default);
    }
}
