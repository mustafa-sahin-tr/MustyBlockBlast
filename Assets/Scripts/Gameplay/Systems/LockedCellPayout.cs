using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// "An opened lock reveals gold" (issue #481), stated once for the two Systems that resolve
    /// destructions — <see cref="BoardSystem"/> (placements, the hammer) and <see cref="PowerUpSystem"/>
    /// (power-ups, the joker). Each calls <see cref="PayAndDrain"/> once its resolution is over; the board
    /// has recorded every lock that opened during it (<see cref="Board.OpenedLocks"/>), whichever way it
    /// opened.
    /// </summary>
    internal static class LockedCellPayout
    {
        /// <summary>
        /// Pays <paramref name="coinsPerLock"/> — the Coin cell's own base payout — for every lock
        /// <paramref name="board"/> recorded as opened, then forgets them so none is ever paid twice. The
        /// coins go through <see cref="CoinCellsClearedMessage"/>, the same way into the wallet as a Coin
        /// cell's (so they count wherever a run's board coins are counted); the positions go out on
        /// <see cref="LockedCellsOpenedMessage"/> for the board's effect. Silent when nothing opened, and
        /// when a lock is worth nothing. <paramref name="lockedCellsOpenedPublisher"/> may be null (test
        /// constructions): the coins are still paid.
        /// </summary>
        internal static void PayAndDrain(
            Board board,
            int coinsPerLock,
            IPublisher<CoinCellsClearedMessage> coinCellsClearedPublisher,
            IPublisher<LockedCellsOpenedMessage> lockedCellsOpenedPublisher)
        {
            IReadOnlyList<GridPosition> opened = board.OpenedLocks;
            if (opened.Count == 0)
            {
                return;
            }

            if (coinsPerLock > 0)
            {
                coinCellsClearedPublisher.Publish(new CoinCellsClearedMessage(opened.Count * coinsPerLock));

                // Copied, for the reason the vortex fill's lists are: the board reuses its buffer on the very
                // next resolution, and the effect reading this may still be playing then.
                lockedCellsOpenedPublisher?.Publish(
                    new LockedCellsOpenedMessage(new List<GridPosition>(opened), coinsPerLock));
            }

            board.ClearOpenedLocks();
        }
    }
}
