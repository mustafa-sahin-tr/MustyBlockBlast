using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>Outcome of one reward request. <see cref="Granted"/> is false when the player did not
    /// earn the power-up (declined, dismissed, no fill) — never an error condition.</summary>
    public readonly struct RewardResult
    {
        public RewardResult(PowerUpKind kind, bool granted)
        {
            Kind = kind;
            Granted = granted;
        }

        public PowerUpKind Kind { get; }

        public bool Granted { get; }
    }

    /// <summary>
    /// Seam between the power-up economy and whatever actually awards a power-up. Deliberately free of
    /// any ad-SDK type so the whole grant flow is testable without an SDK, exactly like
    /// <see cref="ISfxService"/> keeps AudioSource out of the Systems that make noise.
    /// </summary>
    public interface IRewardSource
    {
        UniTask<RewardResult> RequestRewardAsync(PowerUpKind kind, CancellationToken cancellationToken);
    }
}
