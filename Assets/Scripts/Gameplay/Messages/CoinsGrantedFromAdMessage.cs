namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Coins were granted for a watched rewarded ad. <see cref="NewCoinBalance"/> is the balance after
    /// the grant, so consumers never have to read the model to show the new total — the same shape
    /// <see cref="PowerUpGrantedMessage"/> uses.
    /// <para>
    /// Deliberately its own channel rather than a flag on
    /// <see cref="ScoreConvertedToCoinsMessage"/>: an ad grant is not a conversion, takes nothing out of
    /// the convertible pool, and a consumer that wants to celebrate one must not have to filter out the
    /// other.
    /// </para>
    /// </summary>
    public readonly struct CoinsGrantedFromAdMessage
    {
        public CoinsGrantedFromAdMessage(int amount, int newCoinBalance)
        {
            Amount = amount;
            NewCoinBalance = newCoinBalance;
        }

        /// <summary>Coins the ad was worth.</summary>
        public int Amount { get; }

        /// <summary>The coin balance after the grant.</summary>
        public int NewCoinBalance { get; }
    }
}
