using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// The store answered a coin-bundle catalog fetch (issue #256): SKU to localized price string for
    /// every product the fetch returned. Published only by the real store integration
    /// (<c>UnityCoinPurchaseService</c>, the one type in <c>Presentation</c> that touches the purchasing
    /// SDK) once <c>FetchProducts</c> completes; <see cref="Systems.CurrencySystem"/> is its one
    /// subscriber, and writes the whole table into <see cref="Models.CoinBundlePriceModel"/>.
    /// <para>
    /// Carries the whole table rather than one SKU at a time, because that is what one fetch answers:
    /// the SDK hands back every product's metadata in a single callback, and splitting it into one
    /// message per SKU would turn "the catalog just arrived" into several messages with no single moment
    /// a subscriber could call the whole thing "loaded".
    /// </para>
    /// </summary>
    public readonly struct CoinProductsFetchedMessage
    {
        public CoinProductsFetchedMessage(IReadOnlyDictionary<string, string> skuToLocalizedPrice)
        {
            SkuToLocalizedPrice = skuToLocalizedPrice;
        }

        /// <summary>SKU to the store's localized price string. Never null.</summary>
        public IReadOnlyDictionary<string, string> SkuToLocalizedPrice { get; }
    }
}
