using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>Outcome of one lives reward request (issue #478). <see cref="Granted"/> is false when the
    /// player did not earn the lives (declined, dismissed, no fill) — never an error condition.</summary>
    public readonly struct LivesRewardResult
    {
        public LivesRewardResult(int amount, bool granted)
        {
            Amount = amount;
            Granted = granted;
        }

        /// <summary>Lives the source actually awarded. The source is the authority on this, as it is for
        /// <see cref="CoinRewardResult.Amount"/>; <see cref="LivesSystem"/> still clamps what it banks at
        /// <see cref="Settings.LivesConfig.RegenCap"/>, whatever the source says.</summary>
        public int Amount { get; }

        public bool Granted { get; }
    }

    /// <summary>
    /// Seam between Path-mode lives (issue #478) and whatever actually pays for a top-up — today a
    /// rewarded ad — kept free of any ad-SDK type for the reason <see cref="IRewardSource"/> is.
    /// <para>
    /// A fourth parallel seam rather than a widening of <see cref="ICoinRewardSource"/>: that one pays a
    /// currency the player spends anywhere, and a life is not a coin — it is capped, refilled by the
    /// clock, and only Path reads it. Sharing an interface would let a coin ad and a lives ad be wired to
    /// each other's outcome; separate interfaces keep the two flows unable to be confused, exactly as
    /// <see cref="IRescueRewardSource"/> does for the no-moves rescue.
    /// </para>
    /// </summary>
    public interface ILivesRewardSource
    {
        UniTask<LivesRewardResult> RequestLivesRewardAsync(int amount, CancellationToken cancellationToken);
    }
}
