using System.Collections.Generic;
using Mtafasahin.Reactive;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// SKU to the store's localized price string for every coin bundle the storefront has heard back
    /// about (issue #256). Mutated exclusively by <see cref="Systems.CurrencySystem"/>, which replaces
    /// the whole table every time <see cref="Messages.CoinProductsFetchedMessage"/> arrives — a single
    /// fetch answers for every SKU at once, so there is no reason to write one row in isolation.
    /// <para>
    /// Deliberately not on <see cref="ProfileModel"/> or on <see cref="Settings.CoinBundleConfig"/>: a
    /// price is neither account state nor static config, it is what the store answered this session,
    /// and it sits here for the same reason <see cref="DailyAdGrantModel"/> sits apart from both — state
    /// that belongs to neither the player's identity nor the shipped asset.
    /// </para>
    /// <para>
    /// Empty until the first successful fetch, and a SKU missing from it is not an error: the shop
    /// simply has not heard a price for it yet — or never will, in the Editor's fake store, or with
    /// <see cref="Systems.ICoinPurchaseService"/>'s stub in tests — and every reader falls back to a
    /// plain "BUY" rather than showing nothing.
    /// </para>
    /// </summary>
    public sealed class CoinBundlePriceModel
    {
        private static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>();

        /// <summary>SKU to the store's localized price string ("$0.99", "₺49,99"), as of the last
        /// successful catalog fetch. Never null; empty until the first fetch completes.</summary>
        public ReactiveProperty<IReadOnlyDictionary<string, string>> SkuToLocalizedPrice { get; } =
            new ReactiveProperty<IReadOnlyDictionary<string, string>>(Empty);
    }
}
