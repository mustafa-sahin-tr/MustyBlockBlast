namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// A validator's verdict on one <see cref="PurchaseReceipt"/>: whether the receipt is genuine, and
    /// what it is worth.
    /// <para>
    /// The amount comes back from the validator rather than being looked up by the caller, and that is
    /// the entire point of the type. A client that decides for itself how many coins a receipt is worth
    /// has learned nothing from validating it — the SKU in a receipt is a claim, and in a real
    /// implementation the price of that claim is resolved server-side, from the store's own record,
    /// independently of anything the device says. Returning it here is what makes
    /// <see cref="CurrencySystem.PurchaseCoinBundleAsync"/> credit a verified figure instead of a
    /// hopeful one.
    /// </para>
    /// </summary>
    public readonly struct ValidatedPurchase
    {
        public ValidatedPurchase(int coinAmount, bool isValid)
        {
            CoinAmount = coinAmount;
            IsValid = isValid;
        }

        /// <summary>
        /// Coins this receipt is worth according to the validator. Zero on a rejection, and treated as a
        /// rejection by the credit path even when <see cref="IsValid"/> is somehow true: crediting
        /// nothing is not a purchase, and writing a zero to the wallet would burn the transaction id for
        /// good on a receipt the validator could not price.
        /// </summary>
        public int CoinAmount { get; }

        /// <summary>Whether the receipt is genuine and unspent as far as the validator can tell.</summary>
        public bool IsValid { get; }

        /// <summary>The receipt is not one the validator will honour. Nothing is credited.</summary>
        public static ValidatedPurchase Rejected => new ValidatedPurchase(0, false);
    }
}
