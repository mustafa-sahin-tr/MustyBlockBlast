using System;
using System.Globalization;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// Time-limited discounts on power-up prices: one row per campaign, each naming a
    /// <see cref="PowerUpKind"/>, how much comes off it, and the window it comes off in. Static config,
    /// so it lives in a ScriptableObject for the reason <see cref="CurrencyConfig"/> and
    /// <see cref="PowerUpPriceConfig"/> do — running a weekend sale is an asset edit, never a code
    /// change.
    /// <para>
    /// Deliberately a second asset rather than two more columns on <see cref="PowerUpPriceConfig"/>.
    /// "What this costs" and "what it happens to cost this week" are different lifetimes: the first is
    /// retuned once a quarter by whoever owns the economy, the second is authored and thrown away by
    /// whoever runs the campaign. Keeping them apart means a sale can never overwrite a base price, an
    /// expired campaign can be deleted without touching the price table, and
    /// <see cref="PowerUpPriceConfig.GetPrice"/> keeps exactly one meaning: the undiscounted price.
    /// </para>
    /// <para>
    /// Scoped to power-ups, and only power-ups. Coin bundles are bought with real money and their
    /// prices live entirely in App Store Connect and the Play Console — see
    /// <see cref="CoinBundleConfig"/>, which holds coin amounts and deliberately no price at all, so
    /// that there is no second source of truth the store could disagree with. A discount authored here
    /// could not change what the player is actually charged for a bundle; a bundle sale is a price tier
    /// change or a promotional offer configured in the store, not a row in this asset.
    /// </para>
    /// <para>
    /// Windows are authored as ISO-8601 instants in UTC, as strings, because Unity cannot serialize a
    /// <see cref="DateTime"/> (see the serialization rules). UTC rather than local time on both sides:
    /// the config is authored once and read on devices in every timezone, so a campaign that ended
    /// "at midnight" has to mean one instant rather than twenty-four of them. The device clock is the
    /// only clock consulted — there is no server check here, by design (this is local config, not
    /// live-ops delivery), so a player who moves their clock can reach a sale early. That is accepted:
    /// the worst case is a discount on soft currency, which is not worth a network round trip on the
    /// shop's repaint path.
    /// </para>
    /// <para>
    /// Both bounds are inclusive, and an unparseable or inverted window is treated as no promotion at
    /// all rather than as an always-on one — the same safe reading <see cref="PowerUpPriceConfig"/>
    /// gives a missing row: an authoring mistake must never be resolved in the direction of giving
    /// stock away.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Promotion Config", fileName = "PromotionConfig")]
    public sealed class PromotionConfig : ScriptableObject
    {
        /// <summary>The discount a kind with no active row gets: none. Also what an unparseable or
        /// inverted window resolves to.</summary>
        private const int NO_DISCOUNT = 0;

        /// <summary>
        /// Deliberately empty, unlike <see cref="PowerUpPriceConfig"/>'s and
        /// <see cref="CoinBundleConfig"/>'s code-populated defaults. Those two describe something the
        /// game cannot work without, so a freshly created instance has to stand in for the asset; a
        /// promotion table's empty state is not a fallback at all — it is the normal state of an economy
        /// with no sale running, and it is the only default that cannot surprise anyone.
        /// <para>
        /// Shipping demonstration rows here instead would put a live discount inside every
        /// <see cref="ScriptableObject.CreateInstance{T}"/> in the codebase, including the ones in tests
        /// that assert an exact price, and would quietly discount the shop in any scene whose config
        /// field was left unassigned. The demonstration campaigns therefore live in the asset, where a
        /// designer can see, edit and delete them.
        /// </para>
        /// </summary>
        [Header("Campaigns")]
        [Tooltip("One row per campaign, in priority order. The first row whose kind matches and whose "
            + "window contains the current UTC instant wins; discounts never stack. Dates are ISO-8601 "
            + "UTC instants, e.g. 2026-09-01T00:00:00Z, and both bounds are inclusive.")]
        [SerializeField] private PromotionEntry[] _promotions = new PromotionEntry[0];

        /// <summary>
        /// The discount percentage (0-100) in force on <paramref name="kind"/> at
        /// <paramref name="nowUtc"/>, or <see cref="NO_DISCOUNT"/> when no campaign covers it.
        /// <para>
        /// First match wins, and discounts never stack. Two live rows for the same kind is an authoring
        /// mistake with no single correct reading, so array order is taken as the author's priority: the
        /// topmost live row applies and the rest are ignored. The alternatives were both worse —
        /// stacking turns two careless 60% rows into a free power-up, and taking the largest makes the
        /// row a designer wrote last silently override the one above it.
        /// </para>
        /// <para>
        /// <paramref name="nowUtc"/> is expected to be a UTC instant. A <see cref="DateTimeKind.Local"/>
        /// value is converted rather than compared as-is, so a caller that reaches for
        /// <see cref="DateTime.Now"/> gets the right answer instead of an answer that is wrong by its
        /// offset; an unspecified kind is trusted as UTC, because assuming local for it would shift a
        /// deliberately-constructed instant (a test's, typically) by wherever the machine happens to be.
        /// </para>
        /// <para>
        /// A linear scan over a handful of rows, and dates parsed on each call rather than cached. Never
        /// called per frame — a shop repaint and a purchase are both player-driven — so a parsed-window
        /// cache would buy nothing and would have to be invalidated whenever the asset was edited in
        /// play mode.
        /// </para>
        /// </summary>
        public int GetActiveDiscountPercent(PowerUpKind kind, DateTime nowUtc)
        {
            if (_promotions == null)
            {
                return NO_DISCOUNT;
            }

            DateTime comparableNowUtc = nowUtc.Kind == DateTimeKind.Local
                ? nowUtc.ToUniversalTime()
                : nowUtc;

            for (int promotionIndex = 0; promotionIndex < _promotions.Length; promotionIndex++)
            {
                PromotionEntry entry = _promotions[promotionIndex];
                if (entry.Kind != kind || !entry.IsActiveAt(comparableNowUtc))
                {
                    continue;
                }

                return entry.DiscountPercent;
            }

            return NO_DISCOUNT;
        }

        /// <summary>
        /// Test seam: appends one campaign to the table, in the position a designer would have authored
        /// it last. Exists because the table is a private serialized array — the shape
        /// <see cref="PowerUpPriceConfig"/> uses and the one the Inspector needs — which a test cannot
        /// otherwise fill without an editor <c>SerializedObject</c> dance that would describe Unity's
        /// serializer rather than this asset's rules.
        /// <para>
        /// A method rather than a public row type for the reason the row type is private at all: it is
        /// storage, not API, and the game has exactly one question to ask this asset. Appending also
        /// makes the first-match-wins ordering testable in the one way that matters — the order the rows
        /// were added is the order they are resolved in.
        /// </para>
        /// <para>
        /// Grown by reallocation on each call. It is called a handful of times by a handful of tests and
        /// never by the game, so a list field would cost the runtime an indirection to spare a test an
        /// allocation nobody measures.
        /// </para>
        /// </summary>
        internal void AddCampaignForTests(
            PowerUpKind kind, int discountPercent, string startDateUtc, string endDateUtc)
        {
            int existingCount = _promotions == null ? 0 : _promotions.Length;
            var grown = new PromotionEntry[existingCount + 1];
            for (int promotionIndex = 0; promotionIndex < existingCount; promotionIndex++)
            {
                grown[promotionIndex] = _promotions[promotionIndex];
            }

            grown[existingCount] = new PromotionEntry(
                kind, discountPercent, startDateUtc, endDateUtc);
            _promotions = grown;
        }

        /// <summary>
        /// One campaign row, in the shape Unity can serialize. Private for the reason
        /// <see cref="PowerUpPriceConfig"/>'s row type is: it is the asset's storage, not its API, and
        /// the only thing a reader needs is the resolved percentage.
        /// </summary>
        [Serializable]
        private struct PromotionEntry
        {
            [Tooltip("The power-up this campaign discounts.")]
            [SerializeField] private PowerUpKind _kind;

            [Tooltip("Percentage off the base price, 0-100. Clamped on read.")]
            [SerializeField] private int _discountPercent;

            [Tooltip("Inclusive start, as an ISO-8601 UTC instant: 2026-09-01T00:00:00Z.")]
            [SerializeField] private string _startDateUtc;

            [Tooltip("Inclusive end, as an ISO-8601 UTC instant: 2026-09-08T23:59:59Z.")]
            [SerializeField] private string _endDateUtc;

            internal PromotionEntry(
                PowerUpKind kind, int discountPercent, string startDateUtc, string endDateUtc)
            {
                _kind = kind;
                _discountPercent = discountPercent;
                _startDateUtc = startDateUtc;
                _endDateUtc = endDateUtc;
            }

            internal PowerUpKind Kind => _kind;

            /// <summary>
            /// The authored percentage, clamped into 0-100. Clamped here rather than at the call site so
            /// every reader gets it — the same contract <see cref="PowerUpPriceConfig.GetPrice"/> has
            /// with its own clamp. A negative figure would turn a sale into a surcharge, and one past a
            /// hundred would pay the player to shop.
            /// </summary>
            internal int DiscountPercent => Mathf.Clamp(_discountPercent, 0, 100);

            /// <summary>
            /// Whether <paramref name="nowUtc"/> falls inside this campaign's window, both bounds
            /// inclusive. False when either date fails to parse or when the end precedes the start:
            /// there is no reading of a malformed window that should cost the game money.
            /// </summary>
            internal bool IsActiveAt(DateTime nowUtc)
            {
                if (!TryParseUtc(_startDateUtc, out DateTime startUtc)
                    || !TryParseUtc(_endDateUtc, out DateTime endUtc)
                    || endUtc < startUtc)
                {
                    return false;
                }

                return nowUtc >= startUtc && nowUtc <= endUtc;
            }

            /// <summary>
            /// Parses one authored bound into a UTC instant. <see cref="CultureInfo.InvariantCulture"/>
            /// so the asset reads the same on a machine whose locale writes its dates the other way
            /// round, and <see cref="DateTimeStyles.AssumeUniversal"/> so a bound written without a
            /// trailing Z still means the instant the author meant rather than one offset from it.
            /// </summary>
            private static bool TryParseUtc(string value, out DateTime parsedUtc)
            {
                if (string.IsNullOrEmpty(value))
                {
                    parsedUtc = default;
                    return false;
                }

                return DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out parsedUtc);
            }
        }
    }
}
