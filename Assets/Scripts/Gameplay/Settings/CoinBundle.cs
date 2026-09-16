namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One coin bundle as readers see it: a SKU, a label and a coin amount, and no way to change any of
    /// them. Handed out by <see cref="CoinBundleConfig"/> so the storefront, the store integration and
    /// the receipt validator all read the same three facts without any of them holding — or being able
    /// to write to — the asset's own serialized row.
    /// </summary>
    public readonly struct CoinBundle
    {
        internal CoinBundle(string sku, string displayName, int coinAmount)
        {
            Sku = sku ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            CoinAmount = coinAmount;
        }

        /// <summary>Store product id, as registered with the platform.</summary>
        public string Sku { get; }

        /// <summary>What the storefront calls this bundle.</summary>
        public string DisplayName { get; }

        /// <summary>Coins this bundle pays.</summary>
        public int CoinAmount { get; }

        /// <summary>
        /// Whether this is a row that can actually be sold: it needs a SKU to ask the store for and a
        /// positive amount to pay out. A default-constructed bundle — what a lookup miss or an
        /// out-of-range index yields — is neither.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Sku) && CoinAmount > 0;
    }
}
