namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Score was turned into coins. Carries both sides of the trade and both totals it left behind, so
    /// a consumer can show the whole outcome without reading the model — the same contract
    /// <see cref="PowerUpGrantedMessage"/> keeps with its post-grant count.
    /// <para>
    /// Only published for a conversion that actually happened. A refused conversion (nothing available,
    /// or more asked for than there is) publishes nothing at all.
    /// </para>
    /// </summary>
    public readonly struct ScoreConvertedToCoinsMessage
    {
        public ScoreConvertedToCoinsMessage(
            int amountConverted, int coinsGranted, int newCoinBalance, int newAvailableToConvert)
        {
            AmountConverted = amountConverted;
            CoinsGranted = coinsGranted;
            NewCoinBalance = newCoinBalance;
            NewAvailableToConvert = newAvailableToConvert;
        }

        /// <summary>Points taken out of the convertible pool.</summary>
        public int AmountConverted { get; }

        /// <summary>Coins those points bought, at the configured rate.</summary>
        public int CoinsGranted { get; }

        /// <summary>The coin balance after the grant.</summary>
        public int NewCoinBalance { get; }

        /// <summary>What is left convertible after the deduction, so the remainder needs no second read.</summary>
        public int NewAvailableToConvert { get; }
    }
}
