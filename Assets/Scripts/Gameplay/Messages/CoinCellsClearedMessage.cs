namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// One or more <see cref="MustyBlockBlast.Core.SpecialCellKind.Coin"/> cells were destroyed and owe
    /// the player <see cref="TotalCoins"/> coins. Published by whichever System resolved the destruction
    /// — a placement's cascade, a cascade phase within it, or a spent power-up — so a coin cell pays the
    /// same whatever set it off.
    /// <para>
    /// Carries the coins rather than a cell count, unlike <see cref="LaserFiredMessage"/> and its
    /// siblings: the doubling a coin destroyed at an intersection earns is decided by
    /// <see cref="MustyBlockBlast.Core.CoinEffect"/>, which is the only place holding the axis that
    /// decides it, so a count would be a figure no subscriber could turn back into a payout.
    /// </para>
    /// <para>
    /// A message, not a direct write: <see cref="MustyBlockBlast.Gameplay.Systems.CurrencySystem"/> is
    /// the one and only writer of the coin balance, and the Systems that resolve destructions have no
    /// business reaching into the wallet. They announce what was earned; crediting it is someone else's
    /// single responsibility.
    /// </para>
    /// <para>
    /// Published only when something was actually owed, so subscribers never have to handle a zero.
    /// </para>
    /// </summary>
    public readonly struct CoinCellsClearedMessage
    {
        public CoinCellsClearedMessage(int totalCoins)
        {
            TotalCoins = totalCoins;
        }

        /// <summary>Coins the destroyed coin cells are worth in total, with the intersection doubling
        /// already applied. Always greater than zero.</summary>
        public int TotalCoins { get; }
    }
}
