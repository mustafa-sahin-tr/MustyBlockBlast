namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Outcome of one attempt to buy power-ups with coins (see
    /// <see cref="CurrencySystem.TryPurchasePowerUp"/>). Its own type rather than a <c>bool</c>, and
    /// its own file for the reason <see cref="CoinRewardResult"/> and <see cref="RewardResult"/> have
    /// theirs: the shop has to say *why* it refused — "you cannot afford this" and "you have not
    /// reached this yet" are two different sentences to the player, and one of them is not even a
    /// problem they can solve with coins.
    /// <para>
    /// Every member but <see cref="Success"/> is a complete refusal: nothing debited, nothing granted,
    /// nothing persisted. There is no partial outcome to report.
    /// </para>
    /// </summary>
    public enum PowerUpPurchaseResult
    {
        /// <summary>Coins were debited and the power-ups granted, both persisted together.</summary>
        Success,

        /// <summary>Zero or fewer were asked for. A refusal rather than a silent no-op: an empty ask
        /// can only come from a caller that has lost track of its own quantity.</summary>
        InvalidQuantity,

        /// <summary>The kind is still behind its level gate (see <see cref="PowerUpUnlockLevels"/>).
        /// Coins never open that gate, however many the player holds.</summary>
        Locked,

        /// <summary>The total price is more than the balance. Refused rather than clamped to what the
        /// player can afford: they named a quantity, and buying a different one than they asked for
        /// would be a worse surprise than buying none.</summary>
        InsufficientCoins,
    }
}
