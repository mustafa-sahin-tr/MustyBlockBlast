using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>Outcome of one extra-moves reward request (issue #465). <see cref="Granted"/> is false when
    /// the player did not earn the moves (declined, dismissed, no fill) — never an error condition.</summary>
    public readonly struct ExtraMovesRewardResult
    {
        public ExtraMovesRewardResult(bool granted)
        {
            Granted = granted;
        }

        public bool Granted { get; }
    }

    /// <summary>
    /// Seam between a Path level's move budget (issue #465) and whatever pays for its "+moves" offer —
    /// today a rewarded ad — kept free of any ad-SDK type for the reason <see cref="IRewardSource"/> is.
    /// A fifth parallel seam, as <see cref="IRescueRewardSource"/> and <see cref="ILivesRewardSource"/>
    /// are: extra moves are neither an inventory item, nor coins, nor lives, nor a dock rescue, and their
    /// own interface keeps the flows unable to be wired to each other's outcome.
    /// </summary>
    public interface IExtraMovesRewardSource
    {
        UniTask<ExtraMovesRewardResult> RequestExtraMovesRewardAsync(CancellationToken cancellationToken);
    }
}
