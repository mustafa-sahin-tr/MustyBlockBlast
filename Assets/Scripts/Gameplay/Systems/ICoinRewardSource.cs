using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>Outcome of one coin reward request. <see cref="Granted"/> is false when the player did
    /// not earn the coins (declined, dismissed, no fill) — never an error condition.</summary>
    public readonly struct CoinRewardResult
    {
        public CoinRewardResult(int amount, bool granted)
        {
            Amount = amount;
            Granted = granted;
        }

        /// <summary>Coins actually granted. The source is the authority on this, not the caller's ask:
        /// a partial fill is a real outcome and must not be rounded up by whoever requested it.</summary>
        public int Amount { get; }

        public bool Granted { get; }
    }

    /// <summary>
    /// Seam between the coin economy and whatever actually awards coins, kept free of any ad-SDK type
    /// for the reason <see cref="IRewardSource"/> is.
    /// <para>
    /// A parallel seam rather than a widening of <see cref="IRewardSource"/>: that one hands back a
    /// <see cref="PowerUpKind"/> and nothing else, and folding a quantity of a second currency into it
    /// would leave every implementation answering questions about a reward it was never asked for. The
    /// two flows also fail differently — a declined power-up ad costs the player an item they can earn
    /// again, a declined coin ad costs them nothing — so they are better off unable to be confused.
    /// </para>
    /// </summary>
    public interface ICoinRewardSource
    {
        UniTask<CoinRewardResult> RequestCoinRewardAsync(int amount, CancellationToken cancellationToken);
    }
}
