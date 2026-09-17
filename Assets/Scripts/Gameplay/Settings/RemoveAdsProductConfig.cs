using System;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The store product id of the one-time "remove ads" purchase. One field, for the reason
    /// <see cref="CurrencyConfig"/> is a handful: a store product id is set in App Store Connect and the
    /// Play Console, and the client's copy of it must be changeable without a code change — a SKU
    /// renamed in the store console and hard-coded in a build is a product nobody can buy until the next
    /// release.
    /// <para>
    /// Its own asset rather than a row in <see cref="CoinBundleConfig"/>, even though both describe
    /// things bought with real money. That config's rows carry a coin amount, which is the one figure a
    /// coin bundle has and this product has none of; and its whole line-up is registered with the store
    /// as <c>Consumable</c>, which this product must not be — it is bought once and owned forever.
    /// Folding the two together would mean a coin amount that means nothing on one row and a product
    /// type branch on every other.
    /// </para>
    /// <para>
    /// No price here, for the reason <see cref="CoinBundleConfig"/> has none: what the product costs in
    /// money lives in the store consoles and is read back from the store at runtime. A figure duplicated
    /// here would be a second source of truth that could silently disagree with what the player is
    /// actually charged.
    /// </para>
    /// <para>
    /// The shipped SKU is a placeholder, like the bundle line-up's. What matters is that it has exactly
    /// one home — and that the same asset is read by the store integration (which registers the product),
    /// the validator (which recognises its receipts) and
    /// <see cref="MustyBlockBlast.Gameplay.Systems.AdRemovalSystem"/> (which buys it), so the three can
    /// never disagree about which product this is.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Remove Ads Product Config", fileName = "RemoveAdsProductConfig")]
    public sealed class RemoveAdsProductConfig : ScriptableObject
    {
        [Header("Product")]
        [Tooltip("Store product id of the one-time Remove Ads purchase. Must match the non-consumable "
            + "product registered in App Store Connect and the Play Console exactly.")]
        [SerializeField] private string _sku = "remove_ads";

        /// <summary>
        /// The store product id, trimmed. Trimmed rather than taken verbatim because this value is typed
        /// into an Inspector field by hand and a stray space would produce a SKU the store has never
        /// heard of, failing as an opaque "no such product" rather than as the typo it is.
        /// </summary>
        public string Sku => string.IsNullOrEmpty(_sku) ? string.Empty : _sku.Trim();

        /// <summary>
        /// Whether this asset names a product at all. False on a blank SKU, which every reader treats as
        /// "there is nothing to sell" — the product is simply not registered with the store and not
        /// offered — rather than as an error to work around. It is the same reading
        /// <see cref="CoinBundleConfig.TryGetBundle"/> gives a missing row: the only safe interpretation
        /// of an unnamed product is that it cannot be bought, never that it should be handed over free.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Sku);

        /// <summary>
        /// Whether <paramref name="sku"/> is this product. The single question the purchase guard and the
        /// receipt validator both ask, so neither has to spell out the comparison — and so a blank
        /// config can never accidentally match a blank or null claimed SKU.
        /// <para>
        /// Ordinal, not culture-aware: a store product id is an opaque token compared byte for byte by
        /// the store itself, and a culture-sensitive comparison could call two different products equal
        /// on one device's locale and not another's.
        /// </para>
        /// </summary>
        public bool Matches(string sku)
            => IsValid && !string.IsNullOrEmpty(sku) && string.Equals(Sku, sku, StringComparison.Ordinal);
    }
}
