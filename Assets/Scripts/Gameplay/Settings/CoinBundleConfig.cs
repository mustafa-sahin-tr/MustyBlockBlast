using System;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The coin bundles the player can buy with real money: one row per store SKU, each saying what that
    /// SKU is called and how many coins it pays. Static config, so it lives in a ScriptableObject for the
    /// reason <see cref="CurrencyConfig"/> and <see cref="PowerUpPriceConfig"/> do: retuning the
    /// storefront is an asset edit, never a code change — and a storefront is the one thing in an
    /// economy more certain to be retuned than a shop.
    /// <para>
    /// A table keyed by SKU rather than one named field per bundle, exactly as
    /// <see cref="PowerUpPriceConfig"/> is keyed by kind: bundle line-ups grow, shrink and get renamed
    /// by whoever owns the store listing, and a table adds a row where named fields would add a field, a
    /// property and a branch.
    /// </para>
    /// <para>
    /// Prices are deliberately absent. What a bundle costs in money is set in App Store Connect and the
    /// Play Console and read back from the store at runtime — a figure duplicated here would be a second
    /// source of truth that could silently disagree with what the player is actually charged. This asset
    /// owns only the half the store does not know: how many coins a SKU is worth.
    /// </para>
    /// <para>
    /// The shipped rows are placeholders, both in their SKUs and their amounts, scaled so that a larger
    /// bundle pays more coins per unit of money than a smaller one — the usual shape, and the one a
    /// later discount layer will tune rather than replace. They are not a balanced economy and are not
    /// claimed to be one; what matters here is that a bundle's coin value has exactly one home.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Coin Bundle Config", fileName = "CoinBundleConfig")]
    public sealed class CoinBundleConfig : ScriptableObject
    {
        /// <summary>
        /// Populated in code rather than left empty for the asset to fill, for the reason
        /// <see cref="PowerUpPriceConfig"/>'s defaults exist: a freshly created instance — the asset on
        /// its first import, or a test's <see cref="ScriptableObject.CreateInstance{T}"/> — must already
        /// describe a working line-up, so a scene that boots on the fallback config still has bundles to
        /// offer rather than an empty storefront.
        /// </summary>
        [Header("Bundles")]
        [Tooltip("One row per store SKU, in the order the storefront should list them. The SKU must "
            + "match the product id registered in App Store Connect and the Play Console exactly.")]
        [SerializeField] private CoinBundleRow[] _bundles =
        {
            new CoinBundleRow("coins_small", "Handful of Coins", 500),
            new CoinBundleRow("coins_medium", "Bag of Coins", 1200),
            new CoinBundleRow("coins_large", "Chest of Coins", 2800),
            new CoinBundleRow("coins_xl", "Vault of Coins", 6000),
        };

        /// <summary>
        /// How many bundles are on offer. Paired with <see cref="BundleAt"/> rather than exposing a
        /// collection, so the storefront and the store integration can both walk the line-up in an
        /// index loop without an enumerator allocation — the same reason
        /// <see cref="PowerUpPriceConfig.GetPrice"/> scans rather than dictionary-lookups.
        /// </summary>
        public int BundleCount => _bundles == null ? 0 : _bundles.Length;

        /// <summary>
        /// The bundle at <paramref name="index"/>, in authored order. An out-of-range index yields a
        /// default (and therefore <see cref="CoinBundle.IsValid"/>-false) bundle rather than throwing:
        /// every caller already has to handle an unpriced SKU, and one path for "there is nothing here"
        /// is better than two.
        /// </summary>
        public CoinBundle BundleAt(int index)
        {
            if (_bundles == null || index < 0 || index >= _bundles.Length)
            {
                return default;
            }

            return _bundles[index].ToBundle();
        }

        /// <summary>
        /// Looks <paramref name="sku"/> up in the table. False — and a default
        /// <paramref name="bundle"/> — when the table has no row for it.
        /// <para>
        /// A missing row means the same thing an unpriced power-up kind means in
        /// <see cref="PowerUpPriceConfig"/>: an authoring mistake, whose only safe reading is "this
        /// cannot be sold", never "hand it over for free". So it is reported as a miss and every caller
        /// refuses, rather than being papered over with a zero-coin bundle that would burn the
        /// transaction id on a purchase worth nothing.
        /// </para>
        /// <para>
        /// A linear scan over three or four rows. Never called per frame — a storefront repaint and a
        /// purchase are both player-driven — so a dictionary would buy nothing and cost the allocation.
        /// </para>
        /// </summary>
        public bool TryGetBundle(string sku, out CoinBundle bundle)
        {
            if (_bundles == null || string.IsNullOrEmpty(sku))
            {
                bundle = default;
                return false;
            }

            for (int bundleIndex = 0; bundleIndex < _bundles.Length; bundleIndex++)
            {
                if (_bundles[bundleIndex].Sku == sku)
                {
                    bundle = _bundles[bundleIndex].ToBundle();
                    return bundle.IsValid;
                }
            }

            bundle = default;
            return false;
        }

        /// <summary>
        /// One row of the bundle table, in the shape Unity can serialize. Private for the reason
        /// <see cref="PowerUpPriceConfig"/>'s row type is: it is the asset's storage, not its API, and
        /// readers get the immutable <see cref="CoinBundle"/> instead.
        /// </summary>
        [Serializable]
        private struct CoinBundleRow
        {
            [Tooltip("Store product id. Must match App Store Connect / Play Console exactly.")]
            [SerializeField] private string _sku;

            [Tooltip("What the storefront calls this bundle. A placeholder until the currency strings "
                + "reach the String Table.")]
            [SerializeField] private string _displayName;

            [Tooltip("Coins this bundle pays. The one figure the store does not know.")]
            [SerializeField] private int _coinAmount;

            internal CoinBundleRow(string sku, string displayName, int coinAmount)
            {
                _sku = sku;
                _displayName = displayName;
                _coinAmount = coinAmount;
            }

            internal string Sku => _sku;

            /// <summary>
            /// Clamped non-negative for the reason <see cref="CurrencyConfig.ScoreToCoinRate"/> is: a
            /// negative amount would turn paying money into a fine, and the clamp is here rather than at
            /// the call site so every reader gets it.
            /// </summary>
            internal CoinBundle ToBundle()
                => new CoinBundle(_sku, _displayName, Mathf.Max(0, _coinAmount));
        }
    }
}
