using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Stand-in coin reward source used until a rewarded-ad SDK is wired up: every request is granted
    /// in full, immediately and without a prompt. The coin counterpart of
    /// <see cref="DeterministicRewardSource"/>, and deterministic for the same reason — the rest of the
    /// coin flow has to be buildable and playable against it.
    /// </summary>
    public sealed class DeterministicCoinRewardSource : ICoinRewardSource
    {
        public UniTask<CoinRewardResult> RequestCoinRewardAsync(int amount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(new CoinRewardResult(amount, granted: true));
        }
    }
}
