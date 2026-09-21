using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Stand-in rescue reward source used until a rewarded-ad SDK is wired up: every request is granted,
    /// immediately and without a prompt. The rescue counterpart of <see cref="DeterministicRewardSource"/>
    /// and <see cref="DeterministicCoinRewardSource"/>, and deterministic for the same reason — the
    /// no-moves rescue flow has to be buildable and playable against it.
    /// </summary>
    public sealed class DeterministicRescueRewardSource : IRescueRewardSource
    {
        public UniTask<RescueRewardResult> RequestRescueRewardAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(new RescueRewardResult(granted: true));
        }
    }
}
