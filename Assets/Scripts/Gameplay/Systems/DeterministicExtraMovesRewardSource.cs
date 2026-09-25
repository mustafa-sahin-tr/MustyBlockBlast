using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Stand-in extra-moves reward source for the Editor and every platform without the ad SDK: every
    /// request is granted, immediately and without a prompt — the counterpart of
    /// <see cref="DeterministicRescueRewardSource"/> for the move budget's offer (issue #465).
    /// </summary>
    public sealed class DeterministicExtraMovesRewardSource : IExtraMovesRewardSource
    {
        public UniTask<ExtraMovesRewardResult> RequestExtraMovesRewardAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(new ExtraMovesRewardResult(granted: true));
        }
    }
}
