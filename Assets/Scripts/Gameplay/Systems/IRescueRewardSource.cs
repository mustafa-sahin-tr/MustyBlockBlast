using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>Outcome of one no-moves rescue request. <see cref="Granted"/> is false when the player
    /// did not earn the rescue (declined, dismissed, no fill) — never an error condition.</summary>
    public readonly struct RescueRewardResult
    {
        public RescueRewardResult(bool granted)
        {
            Granted = granted;
        }

        public bool Granted { get; }
    }

    /// <summary>
    /// Seam between the no-moves rescue (issue #370) and whatever actually pays for it, kept free of
    /// any ad-SDK type for the reason <see cref="IRewardSource"/> is.
    /// <para>
    /// A parallel seam rather than a widening of <see cref="IRewardSource"/>, exactly as
    /// <see cref="ICoinRewardSource"/> is: that one is keyed by <see cref="PowerUpKind"/> and hands back
    /// an inventory item, and a rescue is neither — it is not a power-up, never enters the inventory,
    /// and in particular reads and spends nothing of Reroll's. Adding a kind to that enum for it would
    /// put a non-item into every inventory key, unlock gate, shop list and "for each kind" test that
    /// enumerates it. Its own interface is what keeps the two flows unable to be confused.
    /// </para>
    /// </summary>
    public interface IRescueRewardSource
    {
        UniTask<RescueRewardResult> RequestRescueRewardAsync(CancellationToken cancellationToken);
    }
}
