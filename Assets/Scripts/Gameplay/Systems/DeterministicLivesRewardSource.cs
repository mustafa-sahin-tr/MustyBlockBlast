using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Stand-in lives reward source used where no rewarded-ad SDK is wired up (the Editor, EditMode tests
    /// and every platform AdMob does not cover): every request is granted in full, immediately and
    /// without a prompt. The lives counterpart of <see cref="DeterministicCoinRewardSource"/> and
    /// <see cref="DeterministicRescueRewardSource"/>, and deterministic for the same reason — the
    /// out-of-lives flow (issue #478) has to be buildable and playable against it.
    /// </summary>
    public sealed class DeterministicLivesRewardSource : ILivesRewardSource
    {
        public UniTask<LivesRewardResult> RequestLivesRewardAsync(int amount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(new LivesRewardResult(amount, granted: true));
        }
    }
}
