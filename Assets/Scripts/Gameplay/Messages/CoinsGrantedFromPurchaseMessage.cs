namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Coins were credited for a validated real-money purchase. <see cref="NewCoinBalance"/> is the
    /// balance after the credit, so consumers never have to read the model to show the new total — the
    /// same shape <see cref="CoinsGrantedFromAdMessage"/> and <see cref="PowerUpGrantedMessage"/> use.
    /// <para>
    /// Its own channel rather than a flag on <see cref="CoinsGrantedFromAdMessage"/>, on the same
    /// reasoning that split that one off <see cref="ScoreConvertedToCoinsMessage"/>: a purchase is not a
    /// gift. Money changed hands, which makes it the one coin grant worth confirming to the player in
    /// its own right, and a consumer that wants to acknowledge a payment must not have to filter ad
    /// rewards out of the same stream.
    /// </para>
    /// <para>
    /// Carries the <see cref="Sku"/> as well as the amount, which the other two grants have no
    /// equivalent of: a receipt is the one coin credit that can be reconciled against something outside
    /// the game, and a consumer confirming a payment needs to be able to name what was bought.
    /// </para>
    /// </summary>
    public readonly struct CoinsGrantedFromPurchaseMessage
    {
        public CoinsGrantedFromPurchaseMessage(string sku, int amount, int newCoinBalance)
        {
            Sku = sku;
            Amount = amount;
            NewCoinBalance = newCoinBalance;
        }

        /// <summary>The store SKU that was bought.</summary>
        public string Sku { get; }

        /// <summary>
        /// Coins credited — the validator's figure, not the amount the device asked for. On this channel
        /// those are the same number today; behind a real validator they need not be, and this is the
        /// one that was actually banked.
        /// </summary>
        public int Amount { get; }

        /// <summary>The coin balance after the credit.</summary>
        public int NewCoinBalance { get; }
    }
}
