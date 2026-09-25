using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// One or more <see cref="SpecialCellKind.Locked"/> cells opened in the resolution that just ended and
    /// "revealed gold" (issue #481): each pays <see cref="CoinsPerLock"/> coins, the same base amount a
    /// Coin cell pays. The coins themselves travel on <see cref="CoinCellsClearedMessage"/>, so the wallet
    /// has one way in for board coins; this message only says where the chests opened and what each was
    /// worth, for the board's "gold spills out" effect.
    /// <para>
    /// Published only when at least one lock opened, by whichever System resolved the destruction that
    /// opened it — a placement's cascade, a hammer, a spent power-up — so a lock pays the same whatever
    /// opened it. <see cref="Positions"/> is a copy owned by the message.
    /// </para>
    /// </summary>
    public readonly struct LockedCellsOpenedMessage
    {
        public LockedCellsOpenedMessage(IReadOnlyList<GridPosition> positions, int coinsPerLock)
        {
            Positions = positions;
            CoinsPerLock = coinsPerLock;
        }

        /// <summary>Where each lock stood, in the order they opened.</summary>
        public IReadOnlyList<GridPosition> Positions { get; }

        /// <summary>Coins each opened lock paid. Always greater than zero.</summary>
        public int CoinsPerLock { get; }
    }
}
