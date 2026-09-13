using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Stand-in reward source used until a rewarded-ad SDK is wired up: every request is granted,
    /// immediately and without a prompt. Deterministic on purpose so the rest of the power-up flow can
    /// be built and played against it.
    /// </summary>
    public sealed class DeterministicRewardSource : IRewardSource
    {
        public UniTask<RewardResult> RequestRewardAsync(PowerUpKind kind, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(new RewardResult(kind, granted: true));
        }
    }
}
