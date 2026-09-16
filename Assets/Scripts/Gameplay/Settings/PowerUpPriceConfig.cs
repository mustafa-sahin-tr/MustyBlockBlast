using System;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// What one of each <see cref="PowerUpKind"/> costs in coins. Static config, so it lives in a
    /// ScriptableObject for the reason <see cref="CurrencyConfig"/> does: retuning the shop is an asset
    /// edit, never a code change — and a shop is the one thing in an economy that is certain to be
    /// retuned.
    /// <para>
    /// A table keyed by kind rather than one named field per kind. The roster grows (it has grown from
    /// three kinds to ten), and a table adds a row where named fields would add a field, a property and
    /// a branch. It is also the shape the enum already persists in — see <c>PowerUpInventoryKey</c> —
    /// so the two read the same way.
    /// </para>
    /// <para>
    /// The shipped numbers are placeholders, scaled roughly against
    /// <see cref="PowerUpUnlockLevels"/>: the three kinds available from a fresh install are the
    /// cheapest, and each later gate costs more. They are not a balanced economy and are not claimed
    /// to be one; what matters here is that the price of a power-up has exactly one home.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Power-Up Price Config", fileName = "PowerUpPriceConfig")]
    public sealed class PowerUpPriceConfig : ScriptableObject
    {
        /// <summary>
        /// Price of a kind that has no row in the table. Deliberately unaffordable rather than free:
        /// a missing row is an authoring mistake, and the safe reading of "we do not know what this
        /// costs" is "it cannot be bought", never "take it". A purchase of an unpriced kind therefore
        /// fails the balance check like any other ask the player cannot afford.
        /// </summary>
        private const int UNPRICED = int.MaxValue;

        /// <summary>
        /// Populated in code rather than left empty for the asset to fill, for the reason
        /// <see cref="CurrencyConfig"/>'s defaults exist: a freshly created instance — the asset on its
        /// first import, or a test's <see cref="ScriptableObject.CreateInstance{T}"/> — must already
        /// price every kind, so a scene that boots on the fallback config still has a working shop
        /// rather than a wall of unbuyable rows.
        /// </summary>
        [Header("Prices")]
        [Tooltip("Coin price of one of each kind. Every PowerUpKind should have exactly one row; a kind "
            + "with no row cannot be bought at all.")]
        [SerializeField] private PowerUpPrice[] _prices =
        {
            // The starter three, offered from the first run (PowerUpUnlockLevels.ALWAYS_UNLOCKED).
            new PowerUpPrice(PowerUpKind.Bomb, 50),
            new PowerUpPrice(PowerUpKind.RowClear, 50),
            new PowerUpPrice(PowerUpKind.ColumnClear, 50),

            // One gate every five levels from here, and a price that climbs with each.
            new PowerUpPrice(PowerUpKind.Joker, 100),
            new PowerUpPrice(PowerUpKind.ColorCleanser, 150),
            new PowerUpPrice(PowerUpKind.Rotate, 200),
            new PowerUpPrice(PowerUpKind.Reroll, 250),
            new PowerUpPrice(PowerUpKind.DoubleMultiplier, 300),
            new PowerUpPrice(PowerUpKind.GhostFit, 350),

            // Priced per coin cell sown, not per level: the level-start picker charges
            // quantity * this, so the figure here is what one extra coin cell costs. Deliberately
            // cheap relative to the kinds above — a single coin cell is a small nudge, and the player
            // is expected to buy several at once.
            new PowerUpPrice(PowerUpKind.CoinSower, 60),
        };

        /// <summary>
        /// Most coin cells one level-start purchase may sow, whatever the player can afford. A ceiling on
        /// the offer rather than on the wallet: a board holds sixty-four cells and a level dressed with
        /// dozens of coins stops being a level with coins in it, so the picker is bounded here even for
        /// a player rich enough to buy past it.
        /// <para>
        /// A placeholder, like the prices above, and in the same asset for the same reason: the picker's
        /// ceiling and the price it charges are one tuning decision, and splitting them across two assets
        /// would invite one to be retuned without the other.
        /// </para>
        /// </summary>
        [Header("Coin Sower")]
        [Tooltip("Most coin cells one level-start Coin Sower purchase may sow.")]
        [SerializeField] private int _coinSowerMaxQuantity = 5;

        /// <summary>
        /// Coin price of one <paramref name="kind"/>, or <see cref="UNPRICED"/> when the table has no
        /// row for it. Clamped non-negative for the reason <see cref="CurrencyConfig.ScoreToCoinRate"/>
        /// is: a negative price would make buying a way to earn coins, and the clamp is here rather than
        /// at the call site so every reader gets it.
        /// <para>
        /// A linear scan over one row per kind. Never called per frame — a purchase and a shop repaint are
        /// both player-driven — so a dictionary would buy nothing and cost the allocation.
        /// </para>
        /// </summary>
        public int GetPrice(PowerUpKind kind)
        {
            if (_prices == null)
            {
                return UNPRICED;
            }

            for (int priceIndex = 0; priceIndex < _prices.Length; priceIndex++)
            {
                if (_prices[priceIndex].Kind == kind)
                {
                    return Mathf.Max(0, _prices[priceIndex].CoinPrice);
                }
            }

            return UNPRICED;
        }

        /// <summary>
        /// <see cref="_coinSowerMaxQuantity"/>, floored at zero: a negative ceiling would read as "offer
        /// a negative quantity", and the clamp is here rather than at the call site so every reader gets
        /// it — the same contract <see cref="GetPrice"/> has.
        /// </summary>
        public int CoinSowerMaxQuantity => Mathf.Max(0, _coinSowerMaxQuantity);

        /// <summary>One row of the price table: a kind and what it costs.</summary>
        [Serializable]
        private struct PowerUpPrice
        {
            [SerializeField] private PowerUpKind _kind;
            [SerializeField] private int _coinPrice;

            internal PowerUpPrice(PowerUpKind kind, int coinPrice)
            {
                _kind = kind;
                _coinPrice = coinPrice;
            }

            internal PowerUpKind Kind => _kind;

            internal int CoinPrice => _coinPrice;
        }
    }
}
